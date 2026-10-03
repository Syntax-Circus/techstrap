using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SyntaxCircus.Common;
using TechStrap.Application.Persistence;
using TechStrap.Domain.Products;

namespace TechStrap.Infrastructure.IntegrationTests;

public sealed class TicketNumberAllocatorTests(PostgresFixture postgres) : PostgresIntegrationTestBase(postgres)
{
    private const int Concurrency = 50;
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static async Task<Product> SeedProductAsync(PersistenceTestHost host, string key = "acme", string prefix = "ACME")
    {
        var product = Product.Create(key, key, prefix, null, host.Clock).Value;
        (await host.CommitAsync(sp => { sp.GetRequiredService<IProductRepository>().Add(product); return Task.CompletedTask; })).IsSuccess.ShouldBeTrue();
        return product;
    }

    private static async Task<string> AllocateAndCommitAsync(PersistenceTestHost host, Guid productId)
    {
        string? number = null;
        var committed = await host.CommitAsync(async sp =>
        {
            var result = await sp.GetRequiredService<ITicketNumberAllocator>().AllocateAsync(productId, Ct);
            result.IsSuccess.ShouldBeTrue();
            number = result.Value.ToString();
        });
        committed.IsSuccess.ShouldBeTrue();
        return number!;
    }

    [Fact]
    public async Task Numbers_start_at_one_use_the_product_prefix_and_count_up()
    {
        await using var host = new PersistenceTestHost(Database);
        var product = await SeedProductAsync(host);

        var numbers = new[]
        {
            await AllocateAndCommitAsync(host, product.Id),
            await AllocateAndCommitAsync(host, product.Id),
            await AllocateAndCommitAsync(host, product.Id),
        };

        numbers.ShouldBe(["ACME-1", "ACME-2", "ACME-3"]);
    }


    private async Task<string> ProductXminAsync(Guid productId)
    {
        await using var connection = new NpgsqlConnection(Database.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT xmin::text FROM products WHERE id = @id";
        command.Parameters.AddWithValue("id", productId);
        return (string)(await command.ExecuteScalarAsync(Ct))!;
    }

    [Fact]
    public async Task Creating_tickets_never_changes_the_product_rows_xmin()
    {
        await using var host = new PersistenceTestHost(Database);
        var product = await SeedProductAsync(host);
        var before = await ProductXminAsync(product.Id);

        await AllocateAndCommitAsync(host, product.Id);
        await AllocateAndCommitAsync(host, product.Id);
        await AllocateAndCommitAsync(host, product.Id);

        (await ProductXminAsync(product.Id)).ShouldBe(before);
    }

    [Fact]
    public async Task A_product_edit_in_flight_while_tickets_are_created_still_succeeds()
    {
        await using var host = new PersistenceTestHost(Database);
        var product = await SeedProductAsync(host);
        await using var editor = host.CreateScope();
        var products = editor.ServiceProvider.GetRequiredService<IProductRepository>();
        var loaded = (await products.GetByIdAsync(product.Id, Ct))!;
        loaded.UpdateDetails("Acme Renamed", loaded.Branding);

        for (var i = 0; i < 5; i++)
        {
            await AllocateAndCommitAsync(host, product.Id);
        }

        await using var work = await editor.ServiceProvider.GetRequiredService<IUnitOfWork>().BeginAsync(Ct);
        products.Update(loaded);
        var result = await work.CommitAsync(Ct);

        result.IsSuccess.ShouldBeTrue();
        (await host.ReadAsync(sp => sp.GetRequiredService<IProductRepository>().GetByIdAsync(product.Id, Ct)))!.Name.ShouldBe("Acme Renamed");
        (await AllocateAndCommitAsync(host, product.Id)).ShouldBe("ACME-6");
    }

    [Fact]
    public async Task Two_creators_racing_on_a_products_very_first_ticket_get_one_and_two()
    {
        await using var host = new PersistenceTestHost(Database);
        var product = await SeedProductAsync(host);
        var start = new TaskCompletionSource();

        var racers = Enumerable.Range(0, 2).Select(async _ =>
        {
            await start.Task;
            return await AllocateAndCommitAsync(host, product.Id);
        }).ToList();
        start.SetResult();

        (await Task.WhenAll(racers)).Order().ShouldBe(["ACME-1", "ACME-2"]);
    }
    [Fact]
    public async Task Two_products_have_independent_sequences_with_their_own_prefix()
    {
        await using var host = new PersistenceTestHost(Database);
        var acme = await SeedProductAsync(host);
        var orbitly = await SeedProductAsync(host, "orbitly", "ORB");

        (await AllocateAndCommitAsync(host, acme.Id)).ShouldBe("ACME-1");
        (await AllocateAndCommitAsync(host, orbitly.Id)).ShouldBe("ORB-1");
        (await AllocateAndCommitAsync(host, acme.Id)).ShouldBe("ACME-2");
        (await AllocateAndCommitAsync(host, orbitly.Id)).ShouldBe("ORB-2");
    }

    [Fact]
    public async Task Fifty_concurrent_creators_for_one_product_get_one_to_fifty_with_no_gaps_or_duplicates()
    {
        await using var host = new PersistenceTestHost(Database);
        var product = await SeedProductAsync(host);
        var start = new TaskCompletionSource();

        var creators = Enumerable.Range(0, Concurrency).Select(async _ =>
        {
            await start.Task;
            long sequence = 0;
            var committed = await host.CommitAsync(async sp =>
            {
                var result = await sp.GetRequiredService<ITicketNumberAllocator>().AllocateAsync(product.Id, Ct);
                result.IsSuccess.ShouldBeTrue();
                sequence = result.Value.Sequence;

                // Hold the transaction open so the other creators really queue behind the row lock.
                await Task.Delay(5, Ct);
            });
            committed.IsSuccess.ShouldBeTrue();
            return sequence;
        }).ToList();

        start.SetResult();
        var sequences = await Task.WhenAll(creators);

        sequences.Order().ShouldBe(Enumerable.Range(1, Concurrency).Select(n => (long)n));
        (await AllocateAndCommitAsync(host, product.Id)).ShouldBe($"ACME-{Concurrency + 1}");
    }

    [Fact]
    public async Task A_rolled_back_transaction_gives_its_number_back_so_the_next_creator_gets_the_same_one()
    {
        await using var host = new PersistenceTestHost(Database);
        var product = await SeedProductAsync(host);

        await using (var scope = host.CreateScope())
        {
            await using var work = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().BeginAsync(Ct);
            var taken = await scope.ServiceProvider.GetRequiredService<ITicketNumberAllocator>().AllocateAsync(product.Id, Ct);
            taken.Value.ToString().ShouldBe("ACME-1");
        }

        (await AllocateAndCommitAsync(host, product.Id)).ShouldBe("ACME-1");
        (await AllocateAndCommitAsync(host, product.Id)).ShouldBe("ACME-2");
    }

    [Fact]
    public async Task A_rollback_in_the_middle_leaves_no_gap()
    {
        await using var host = new PersistenceTestHost(Database);
        var product = await SeedProductAsync(host);
        await AllocateAndCommitAsync(host, product.Id);

        await using (var scope = host.CreateScope())
        {
            await using var work = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().BeginAsync(Ct);
            await scope.ServiceProvider.GetRequiredService<ITicketNumberAllocator>().AllocateAsync(product.Id, Ct);
        }

        (await AllocateAndCommitAsync(host, product.Id)).ShouldBe("ACME-2");
    }

    [Fact]
    public async Task An_unknown_product_is_a_not_found_result()
    {
        await using var host = new PersistenceTestHost(Database);
        ResultError? error = null;

        await host.CommitAsync(async sp =>
        {
            var result = await sp.GetRequiredService<ITicketNumberAllocator>().AllocateAsync(Guid.NewGuid(), Ct);
            error = result.Errors.Single();
        });

        error!.Kind.ShouldBe(ResultErrorKind.NotFound);
        error.Code.ShouldBe("product-not-found");
    }

    [Fact]
    public async Task Allocating_outside_a_unit_of_work_scope_is_a_programming_error()
    {
        await using var host = new PersistenceTestHost(Database);
        var product = await SeedProductAsync(host);
        await using var scope = host.CreateScope();

        await Should.ThrowAsync<InvalidOperationException>(() =>
            scope.ServiceProvider.GetRequiredService<ITicketNumberAllocator>().AllocateAsync(product.Id, Ct));
    }
}
