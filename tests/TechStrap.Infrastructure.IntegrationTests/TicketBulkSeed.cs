using Npgsql;

namespace TechStrap.Infrastructure.IntegrationTests;

/// <summary>Inserts many tickets with one SQL statement, for paging, clamping and query-plan tests that need volume.</summary>
internal static class TicketBulkSeed
{
    /// <summary>
    /// Adds <paramref name="count"/> tickets numbered BULK-1..n to the scenario's first product. Every 50th is spam and every 20th is
    /// Solved (solved_at = <paramref name="at"/>). With <paramref name="distinctTimes"/> false every row has the same
    /// <c>last_activity_at</c>, so only the sort tiebreaker orders them. Runs ANALYZE afterwards so the planner has statistics.
    /// </summary>
    public static async Task RunAsync(TicketScenario scenario, string connectionString, int count, DateTimeOffset at, bool distinctTimes, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using (var insert = connection.CreateCommand())
        {
            insert.CommandText = """
                INSERT INTO tickets (id, number, product_id, requester_id, subject, status, priority, channel, is_spam, metadata_trusted, created_at, last_activity_at, solved_at)
                SELECT gen_random_uuid(), 'BULK-' || g, @product, @requester, 'Bulk ' || g,
                       CASE WHEN g % 20 = 0 THEN 'Solved' ELSE 'Open' END, 'Normal', 'Web', g % 50 = 1, false, @at,
                       CASE WHEN @distinct THEN @at + g * interval '1 second' ELSE @at END,
                       CASE WHEN g % 20 = 0 THEN @at END
                FROM generate_series(1, @count) AS g
                """;
            insert.Parameters.AddWithValue("product", scenario.Acme.Id);
            insert.Parameters.AddWithValue("requester", scenario.Requester.Id);
            insert.Parameters.AddWithValue("at", at);
            insert.Parameters.AddWithValue("distinct", distinctTimes);
            insert.Parameters.AddWithValue("count", count);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var analyze = connection.CreateCommand();
        analyze.CommandText = "ANALYZE tickets";
        await analyze.ExecuteNonQueryAsync(cancellationToken);
    }
}
