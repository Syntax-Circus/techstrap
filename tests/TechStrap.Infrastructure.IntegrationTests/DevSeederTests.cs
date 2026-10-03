using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SyntaxCircus.Common;
using TechStrap.Application.Knowledge;
using TechStrap.Application.Persistence;
using TechStrap.Application.Seeding;
using TechStrap.Application.Tickets;
using TechStrap.Domain.Knowledge;
using TechStrap.Domain.Products;
using TechStrap.Domain.Tickets;
using TechStrap.Infrastructure.Persistence;
using TechStrap.Infrastructure.Security;
using TechStrap.Infrastructure.Seeding;

namespace TechStrap.Infrastructure.IntegrationTests;

public sealed class DevSeederTests(PostgresFixture postgres) : PostgresIntegrationTestBase(postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private ServiceProvider BuildProvider(bool failTicketNumbering = false, bool pooled = false)
    {
        var connectionString = pooled
            ? new NpgsqlConnectionStringBuilder(Database.ConnectionString) { Pooling = true, MaxPoolSize = 20 }.ConnectionString
            : Database.ConnectionString;
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [$"ConnectionStrings:{TechStrapDatabase.ConnectionStringName}"] = connectionString })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddTechStrapPersistence();
        services.AddTechStrapDevelopmentSeeding();
        if (failTicketNumbering)
        {
            // Part 1 (the foundation) commits; part 2 needs ticket numbers and fails after the seed lock was taken.
            services.AddScoped<ITicketNumberAllocator, FailingAllocator>();
        }

        return services.BuildServiceProvider();
    }

    private sealed class FailingAllocator : ITicketNumberAllocator
    {
        public Task<Result<TicketNumber>> AllocateAsync(Guid productId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("injected seed failure");
    }

    private async Task<bool> SeedLockIsFreeAsync()
    {
        await using var connection = new NpgsqlConnection(Database.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT pg_try_advisory_lock(@key)";
        command.Parameters.AddWithValue("key", TechStrapDatabase.SeedLockKey);
        var acquired = (bool)(await command.ExecuteScalarAsync(Ct))!;
        if (acquired)
        {
            command.CommandText = "SELECT pg_advisory_unlock(@key)";
            await command.ExecuteNonQueryAsync(Ct);
        }

        return acquired;
    }

    private static async Task SeedAsync(ServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IDevelopmentDataSeeder>().SeedAsync(Ct);
    }

    private static async Task<T> ReadAsync<T>(ServiceProvider provider, Func<IServiceProvider, Task<T>> read)
    {
        await using var scope = provider.CreateAsyncScope();
        return await read(scope.ServiceProvider);
    }

    [Fact]
    public async Task Seeding_creates_tickets_in_every_status_a_spam_ticket_and_a_follow_up_pair()
    {
        await using var provider = BuildProvider();

        await SeedAsync(provider);

        var all = await ReadAsync(provider, sp => sp.GetRequiredService<ITicketRepository>().ListAsync(new TicketQuery(TicketView.All, PageSize: 100), Ct));
        all.Items.Select(t => t.Status).Distinct().Order().ShouldBe(Enum.GetValues<TicketStatus>().Order());
        var spam = await ReadAsync(provider, sp => sp.GetRequiredService<ITicketRepository>().ListAsync(new TicketQuery(TicketView.Spam), Ct));
        spam.Items.ShouldHaveSingleItem().Subject.ShouldContain("watches");
        var closed = all.Items.Single(t => t.Status == TicketStatus.Closed);
        var followUp = await ReadAsync(provider, async sp =>
        {
            var tickets = sp.GetRequiredService<ITicketRepository>();
            var number = all.Items.Single(t => t.Subject == closed.Subject && t.Status == TicketStatus.New).Number;
            return (await tickets.GetByNumberAsync(number, Ct))!;
        });
        followUp.ParentTicketId.ShouldBe(closed.Id);
    }

    [Fact]
    public async Task Seeding_creates_both_api_key_kinds_and_a_published_knowledge_base_article()
    {
        await using var provider = BuildProvider();

        await SeedAsync(provider);

        var orbitly = (await ReadAsync(provider, sp => sp.GetRequiredService<IProductRepository>().GetByKeyAsync("orbitly", Ct)))!;
        var keys = await ReadAsync(provider, sp => sp.GetRequiredService<IProductRepository>().ListApiKeysAsync(orbitly.Id, Ct));
        keys.Select(k => k.Kind).Order().ShouldBe([ApiKeyKind.Trusted, ApiKeyKind.Public]);
        var published = await ReadAsync(provider, sp => sp.GetRequiredService<IKbRepository>().ListArticlesAsync(new KbArticleQuery(Status: KbArticleStatus.Published), Ct));
        published.Items.Count.ShouldBeGreaterThanOrEqualTo(2);
        var found = await ReadAsync(provider, sp => sp.GetRequiredService<IKbRepository>().SearchAsync(new KbSearchQuery("reset password", Status: KbArticleStatus.Published), Ct));
        found.Items.ShouldHaveSingleItem().Slug.ShouldBe("reset-password");
    }

    [Fact]
    public async Task Seeding_is_idempotent_a_second_run_changes_nothing()
    {
        await using var provider = BuildProvider();
        await SeedAsync(provider);
        var before = await ReadAsync(provider, sp => sp.GetRequiredService<ITicketRepository>().ListAsync(new TicketQuery(TicketView.All, PageSize: 100), Ct));

        await SeedAsync(provider);

        var after = await ReadAsync(provider, sp => sp.GetRequiredService<ITicketRepository>().ListAsync(new TicketQuery(TicketView.All, PageSize: 100), Ct));
        after.TotalCount.ShouldBe(before.TotalCount);
        (await ReadAsync(provider, sp => sp.GetRequiredService<IProductRepository>().ListAsync(false, Ct))).Count.ShouldBe(2);
    }

    [Fact]
    public async Task Two_instances_seeding_at_the_same_time_both_finish_without_error_and_the_data_exists_once()
    {
        await using var first = BuildProvider();
        await using var second = BuildProvider();

        await Task.WhenAll(SeedAsync(first), SeedAsync(second));

        (await ReadAsync(first, sp => sp.GetRequiredService<IProductRepository>().ListAsync(false, Ct))).Count.ShouldBe(2);
        var all = await ReadAsync(first, sp => sp.GetRequiredService<ITicketRepository>().ListAsync(new TicketQuery(TicketView.All, PageSize: 100), Ct));
        var spam = await ReadAsync(first, sp => sp.GetRequiredService<ITicketRepository>().ListAsync(new TicketQuery(TicketView.Spam), Ct));
        (all.TotalCount + spam.TotalCount).ShouldBe(7);
        (await ReadAsync(first, sp => sp.GetRequiredService<IKbRepository>().ListArticlesAsync(new KbArticleQuery(), Ct))).TotalCount.ShouldBe(4);
    }

    [Fact]
    public async Task A_seed_that_fails_after_taking_the_lock_releases_it_even_on_pooled_connections()
    {
        await using var provider = BuildProvider(failTicketNumbering: true, pooled: true);

        await Should.ThrowAsync<InvalidOperationException>(() => SeedAsync(provider));

        (await SeedLockIsFreeAsync()).ShouldBeTrue();
    }

    [Fact]
    public async Task A_duplicate_conflict_while_seeding_fails_the_run_naming_the_part_instead_of_being_skipped()
    {
        await using (var context = Database.CreateDbContext())
        {
            await RecordSeed.ProductAsync(context, "paperplane", "PPL");
        }

        await using var provider = BuildProvider();

        var failure = await Should.ThrowAsync<InvalidOperationException>(() => SeedAsync(provider));

        failure.Message.ShouldContain("foundation");
        failure.Message.ShouldContain(PersistenceErrorCodes.Duplicate);
        (await SeedLockIsFreeAsync()).ShouldBeTrue();
    }

    [Fact]
    public async Task A_run_that_stopped_after_part_one_is_finished_by_the_next_run()
    {
        await using (var failing = BuildProvider(failTicketNumbering: true))
        {
            await Should.ThrowAsync<InvalidOperationException>(() => SeedAsync(failing));
        }

        await using var provider = BuildProvider();
        (await ReadAsync(provider, sp => sp.GetRequiredService<IProductRepository>().ListAsync(false, Ct))).Count.ShouldBe(2);
        (await ReadAsync(provider, sp => sp.GetRequiredService<IKbRepository>().ListArticlesAsync(new KbArticleQuery(), Ct))).TotalCount.ShouldBe(0);

        await SeedAsync(provider);

        var all = await ReadAsync(provider, sp => sp.GetRequiredService<ITicketRepository>().ListAsync(new TicketQuery(TicketView.All, PageSize: 100), Ct));
        var spam = await ReadAsync(provider, sp => sp.GetRequiredService<ITicketRepository>().ListAsync(new TicketQuery(TicketView.Spam), Ct));
        (all.TotalCount + spam.TotalCount).ShouldBe(7);
        (await ReadAsync(provider, sp => sp.GetRequiredService<IProductRepository>().ListAsync(false, Ct))).Count.ShouldBe(2);
    }

    [Fact]
    public async Task Seeded_numbers_are_per_product_and_events_were_written_with_every_ticket()
    {
        await using var provider = BuildProvider();
        await SeedAsync(provider);

        var all = await ReadAsync(provider, sp => sp.GetRequiredService<ITicketRepository>().ListAsync(new TicketQuery(TicketView.All, PageSize: 100), Ct));
        var orbitlyNumbers = all.Items.Where(t => t.Number.StartsWith("ORB-", StringComparison.Ordinal)).Select(t => t.Number).Order().ToList();

        orbitlyNumbers.ShouldBe(["ORB-1", "ORB-2", "ORB-3", "ORB-4", "ORB-5"]);
        foreach (var ticket in all.Items)
        {
            var events = await ReadAsync(provider, sp => sp.GetRequiredService<ITicketRepository>().GetEventsAsync(ticket.Id, Ct));
            events.ShouldContain(e => e.Type == TicketEventType.Created);
        }
    }
}
