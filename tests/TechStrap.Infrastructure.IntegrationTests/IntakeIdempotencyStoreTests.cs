using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SyntaxCircus.Common;
using TechStrap.Application.Persistence;
using TechStrap.Domain.Products;
using TechStrap.Infrastructure.Persistence.Records;

namespace TechStrap.Infrastructure.IntegrationTests;

public sealed class IntakeIdempotencyStoreTests(PostgresFixture postgres) : PostgresIntegrationTestBase(postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private sealed record Seeded(Guid ApiKeyId, Guid OtherApiKeyId, Guid[] TicketIds);

    private async Task<Seeded> SeedAsync()
    {
        await using var context = CreateDbContext();
        var product = await RecordSeed.ProductAsync(context);
        var requester = await RecordSeed.RequesterAsync(context);
        var tickets = new List<Guid>();
        for (var i = 1; i <= 5; i++)
        {
            tickets.Add((await RecordSeed.TicketAsync(context, product, requester, $"ACME-{i}")).Id);
        }

        ProductApiKeyRecord NewKey(string prefix) => new()
        {
            Id = Guid.CreateVersion7(), ProductId = product.Id, Kind = ApiKeyKind.Trusted, KeyHash = prefix, KeyPrefix = prefix, CreatedAt = RecordSeed.Now,
        };
        var first = NewKey("tsk_aaaa");
        var second = NewKey("tsk_bbbb");
        context.Set<ProductApiKeyRecord>().AddRange(first, second);
        await context.SaveChangesAsync(Ct);
        return new Seeded(first.Id, second.Id, [.. tickets]);
    }

    private static Task<Result> AddAsync(PersistenceTestHost host, Guid apiKeyId, string key, Guid ticketId, string json = "{}", DateTimeOffset? at = null) =>
        host.CommitAsync(sp =>
        {
            sp.GetRequiredService<IIntakeIdempotencyStore>().Add(apiKeyId, key, ticketId, json, at ?? RecordSeed.Now);
            return Task.CompletedTask;
        });

    private static Task<IntakeIdempotencyEntry?> FindAsync(PersistenceTestHost host, Guid apiKeyId, string key) =>
        host.ReadAsync(sp => sp.GetRequiredService<IIntakeIdempotencyStore>().FindAsync(apiKeyId, key, Ct));

    [Fact]
    public async Task A_stored_entry_is_found_by_the_same_api_key_and_key()
    {
        var seed = await SeedAsync();
        await using var host = new PersistenceTestHost(Database);
        var at = RecordSeed.Now.AddMinutes(3);

        (await AddAsync(host, seed.ApiKeyId, "order-42", seed.TicketIds[0], "{\"ticket\":\"ACME-1\"}", at)).IsSuccess.ShouldBeTrue();

        var found = (await FindAsync(host, seed.ApiKeyId, "order-42"))!;
        found.ResponseJson.ShouldContain("ACME-1");
        found.TicketId.ShouldBe(seed.TicketIds[0]);
        found.CreatedAt.ShouldBe(at);
        found.ApiKeyId.ShouldBe(seed.ApiKeyId);

        await using var connection = new NpgsqlConnection(Database.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT key_hash FROM intake_idempotency_keys";
        var stored = (string)(await command.ExecuteScalarAsync(Ct))!;
        stored.ShouldBe("sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("order-42"))));
        stored.ShouldNotContain("order-42");
    }

    [Fact]
    public async Task The_same_key_under_another_api_key_is_not_found()
    {
        var seed = await SeedAsync();
        await using var host = new PersistenceTestHost(Database);
        (await AddAsync(host, seed.ApiKeyId, "order-42", seed.TicketIds[0])).IsSuccess.ShouldBeTrue();

        (await FindAsync(host, seed.OtherApiKeyId, "order-42")).ShouldBeNull();
        (await FindAsync(host, seed.ApiKeyId, "other-key")).ShouldBeNull();
    }

    [Fact]
    public async Task A_concurrent_duplicate_is_a_duplicate_commit_conflict()
    {
        var seed = await SeedAsync();
        await using var host = new PersistenceTestHost(Database);
        (await AddAsync(host, seed.ApiKeyId, "order-42", seed.TicketIds[0])).IsSuccess.ShouldBeTrue();

        var second = await AddAsync(host, seed.ApiKeyId, "order-42", seed.TicketIds[1]);

        second.IsSuccess.ShouldBeFalse();
        second.Errors[0].Kind.ShouldBe(ResultErrorKind.Conflict);
        second.Errors[0].Code.ShouldBe(PersistenceErrorCodes.Duplicate);
    }

    [Fact]
    public async Task Remove_then_add_reuses_an_expired_key_in_one_commit()
    {
        var seed = await SeedAsync();
        await using var host = new PersistenceTestHost(Database);
        (await AddAsync(host, seed.ApiKeyId, "order-42", seed.TicketIds[0])).IsSuccess.ShouldBeTrue();

        var result = await host.CommitAsync(async sp =>
        {
            var store = sp.GetRequiredService<IIntakeIdempotencyStore>();
            var expired = (await store.FindAsync(seed.ApiKeyId, "order-42", Ct))!;
            store.Remove(expired);
            store.Add(seed.ApiKeyId, "order-42", seed.TicketIds[1], "{\"n\":2}", RecordSeed.Now.AddDays(2));
        });

        result.IsSuccess.ShouldBeTrue();
        var found = (await FindAsync(host, seed.ApiKeyId, "order-42"))!;
        found.TicketId.ShouldBe(seed.TicketIds[1]);
        found.ResponseJson.ShouldContain("2");
    }

    [Fact]
    public async Task Prune_deletes_only_old_entries_and_respects_the_limit()
    {
        var seed = await SeedAsync();
        await using var host = new PersistenceTestHost(Database);
        var old = RecordSeed.Now;
        (await AddAsync(host, seed.ApiKeyId, "old-1", seed.TicketIds[0], at: old)).IsSuccess.ShouldBeTrue();
        (await AddAsync(host, seed.ApiKeyId, "old-2", seed.TicketIds[1], at: old.AddMinutes(1))).IsSuccess.ShouldBeTrue();
        (await AddAsync(host, seed.ApiKeyId, "old-3", seed.TicketIds[2], at: old.AddMinutes(2))).IsSuccess.ShouldBeTrue();
        (await AddAsync(host, seed.ApiKeyId, "new-1", seed.TicketIds[3], at: old.AddDays(2))).IsSuccess.ShouldBeTrue();

        var pruned = await host.ReadAsync(sp => sp.GetRequiredService<IIntakeIdempotencyStore>().PruneAsync(old.AddDays(1), 2, Ct));

        pruned.ShouldBe(2);
        (await FindAsync(host, seed.ApiKeyId, "old-1")).ShouldBeNull();
        (await FindAsync(host, seed.ApiKeyId, "old-2")).ShouldBeNull();
        (await FindAsync(host, seed.ApiKeyId, "old-3")).ShouldNotBeNull();
        (await FindAsync(host, seed.ApiKeyId, "new-1")).ShouldNotBeNull();
    }
}
