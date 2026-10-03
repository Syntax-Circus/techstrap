using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using TechStrap.Application.Tickets;
using TechStrap.Domain.Tickets;
using TechStrap.Infrastructure.Persistence.Records;
using TechStrap.Infrastructure.Persistence.Repositories;

namespace TechStrap.Infrastructure.IntegrationTests;

/// <summary>
/// The partial indexes must be usable by the SQL EF actually emits. Each test seeds enough rows, runs ANALYZE, disables sequential
/// scans inside a transaction and asserts on the EXPLAIN plan text of the repository's own query.
/// </summary>
public sealed class TicketQueryPlanTests(PostgresFixture postgres) : PostgresIntegrationTestBase(postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private async Task<string> ExplainAsync(Func<TechStrap.Infrastructure.Persistence.TechStrapDbContext, IQueryable<TicketRecord>> build, DateTimeOffset? parameter = null)
    {
        await using var context = Database.CreateDbContext();
        await using var transaction = await context.Database.BeginTransactionAsync(Ct);
        await context.Database.ExecuteSqlRawAsync("SET LOCAL enable_seqscan = off", Ct);

        // ToQueryString prefixes parameters as SQL comments and leaves the @name placeholders in the text.
        var sql = string.Join('\n', build(context).ToQueryString().Split('\n').Where(line => !line.TrimStart().StartsWith("--", StringComparison.Ordinal)));
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = "EXPLAIN " + sql;
        if (parameter is { } value)
        {
            command.Parameters.Add(new NpgsqlParameter(Regex.Match(sql, "@\\w+").Value[1..], value));
        }

        var lines = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(Ct);
        while (await reader.ReadAsync(Ct))
        {
            lines.Add(reader.GetString(0));
        }

        return string.Join('\n', lines);
    }

    [Fact]
    public async Task The_Spam_view_uses_the_partial_spam_index()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        await TicketBulkSeed.RunAsync(scenario, Database.ConnectionString, 5000, host.Clock.GetUtcNow(), true, Ct);

        var plan = await ExplainAsync(context => TicketRepository.ViewQuery(context.Set<TicketRecord>().AsNoTracking(), new TicketQuery(TicketView.Spam))
            .OrderByDescending(t => t.LastActivityAt).ThenByDescending(t => t.Id));

        plan.ShouldContain("ix_tickets_last_activity_at_when_spam");
        plan.ShouldNotContain("Seq Scan");
    }

    [Fact]
    public async Task The_auto_close_query_uses_the_partial_solved_index()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        var solvedAt = host.Clock.GetUtcNow().AddDays(-30);
        await TicketBulkSeed.RunAsync(scenario, Database.ConnectionString, 5000, solvedAt, true, Ct);

        var plan = await ExplainAsync(
            context => TicketRepository.SolvedBeforeQuery(context.Set<TicketRecord>().AsNoTracking(), host.Clock.GetUtcNow()),
            host.Clock.GetUtcNow());

        plan.ShouldContain("ix_tickets_solved_at_when_solved");
        plan.ShouldNotContain("Seq Scan");
    }
}
