using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TechStrap.Application.Persistence;
using TechStrap.Domain.Products;
using TechStrap.Infrastructure.Persistence.Records;

namespace TechStrap.Infrastructure.IntegrationTests;

/// <summary>Database-level checks of the core tables that the model-only schema tests cannot make.</summary>
public sealed class CoreSchemaDatabaseTests(PostgresFixture postgres) : PostgresIntegrationTestBase(postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private async Task<long> CountAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(Database.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (long)(await command.ExecuteScalarAsync(Ct))!;
    }

    [Fact]
    public async Task Two_api_keys_with_the_same_hash_are_refused_by_the_database()
    {
        await using var context = CreateDbContext();
        var product = await RecordSeed.ProductAsync(context);
        ProductApiKeyRecord Key(string prefix) => new()
        {
            Id = Guid.CreateVersion7(), ProductId = product.Id, Kind = ApiKeyKind.Trusted, KeyHash = "same-hash", KeyPrefix = prefix, CreatedAt = RecordSeed.Now,
        };

        context.Set<ProductApiKeyRecord>().Add(Key("tsk_aaaa"));
        await context.SaveChangesAsync(Ct);
        context.Set<ProductApiKeyRecord>().Add(Key("tsk_bbbb"));

        var failure = await Should.ThrowAsync<DbUpdateException>(async () => await context.SaveChangesAsync(Ct));
        (failure.InnerException as PostgresException)!.SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
    }

    [Fact]
    public async Task Deleting_a_product_removes_its_ticket_sequence_row()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        var allocated = await host.CommitAsync(async sp =>
            (await sp.GetRequiredService<ITicketNumberAllocator>().AllocateAsync(scenario.Orbitly.Id, Ct)).IsSuccess.ShouldBeTrue());
        allocated.IsSuccess.ShouldBeTrue();

        (await CountAsync($"SELECT count(*) FROM product_ticket_sequences WHERE product_id = '{scenario.Orbitly.Id}'")).ShouldBe(1);
        await using (var connection = new NpgsqlConnection(Database.ConnectionString))
        {
            await connection.OpenAsync(Ct);
            await using var delete = connection.CreateCommand();
            delete.CommandText = $"DELETE FROM products WHERE id = '{scenario.Orbitly.Id}'";
            await delete.ExecuteNonQueryAsync(Ct);
        }

        (await CountAsync($"SELECT count(*) FROM product_ticket_sequences WHERE product_id = '{scenario.Orbitly.Id}'")).ShouldBe(0);
    }
}
