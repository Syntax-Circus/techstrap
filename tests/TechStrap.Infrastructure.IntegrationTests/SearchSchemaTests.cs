using Npgsql;

namespace TechStrap.Infrastructure.IntegrationTests;

/// <summary>D-027: the search vectors are stored generated columns with GIN indexes, maintained by Postgres alone.</summary>
public sealed class SearchSchemaTests(PostgresFixture postgres) : PostgresIntegrationTestBase(postgres)
{
    /// <summary>Postgres error 428C9, generated_always: a generated column cannot be written.</summary>
    private const string GeneratedAlwaysSqlState = "428C9";

    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private async Task<NpgsqlConnection> OpenAsync()
    {
        var connection = new NpgsqlConnection(Database.ConnectionString);
        await connection.OpenAsync(Ct);
        return connection;
    }

    private static async Task<List<string>> ReadStringsAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var rows = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(Ct);
        while (await reader.ReadAsync(Ct))
        {
            rows.Add(reader.GetString(0));
        }

        return rows;
    }

    [Theory]
    [InlineData("tickets", "subject")]
    [InlineData("messages", "body")]
    [InlineData("kb_articles", "body_markdown")]
    public async Task The_search_vector_is_a_generated_always_column_built_with_setweight(string table, string sourceColumn)
    {
        await using var connection = await OpenAsync();

        var rows = await ReadStringsAsync(
            connection,
            $"SELECT is_generated || '|' || generation_expression FROM information_schema.columns WHERE table_name = '{table}' AND column_name = 'search_vector'");

        var column = rows.ShouldHaveSingleItem();
        column.ShouldStartWith("ALWAYS|");
        column.ShouldContain("setweight");
        column.ShouldContain(sourceColumn);
    }

    [Fact]
    public async Task The_kb_vector_weights_title_a_summary_b_and_body_c()
    {
        await using var connection = await OpenAsync();

        var expression = (await ReadStringsAsync(
            connection,
            "SELECT generation_expression FROM information_schema.columns WHERE table_name = 'kb_articles' AND column_name = 'search_vector'")).Single();

        expression.IndexOf("'A'", StringComparison.Ordinal).ShouldBeLessThan(expression.IndexOf("'B'", StringComparison.Ordinal));
        expression.IndexOf("'B'", StringComparison.Ordinal).ShouldBeLessThan(expression.IndexOf("'C'", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_application_cannot_write_a_search_vector_because_postgres_owns_it()
    {
        await using var connection = await OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE tickets SET search_vector = to_tsvector('english', 'x')";

        var failure = await Should.ThrowAsync<PostgresException>(() => command.ExecuteNonQueryAsync(Ct));

        failure.SqlState.ShouldBe(GeneratedAlwaysSqlState);
    }

    [Fact]
    public async Task No_user_triggers_exist_so_nothing_but_the_generated_columns_maintains_the_vectors()
    {
        await using var connection = await OpenAsync();

        (await ReadStringsAsync(connection, "SELECT tgname FROM pg_trigger WHERE NOT tgisinternal")).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("ix_tickets_search_vector")]
    [InlineData("ix_messages_search_vector")]
    [InlineData("ix_kb_articles_search_vector")]
    public async Task Each_vector_has_a_gin_index(string indexName)
    {
        await using var connection = await OpenAsync();

        var definition = (await ReadStringsAsync(connection, $"SELECT indexdef FROM pg_indexes WHERE indexname = '{indexName}'")).ShouldHaveSingleItem();

        definition.ShouldContain("USING gin");
    }

    [Fact]
    public async Task Changing_a_subject_in_sql_updates_the_vector_without_any_application_code()
    {
        await using var context = Database.CreateDbContext();
        var product = await RecordSeed.ProductAsync(context);
        var requester = await RecordSeed.RequesterAsync(context);
        await RecordSeed.TicketAsync(context, product, requester, "ACME-1");
        await using var connection = await OpenAsync();
        await using (var update = connection.CreateCommand())
        {
            update.CommandText = "UPDATE tickets SET subject = 'Refund request'";
            await update.ExecuteNonQueryAsync(Ct);
        }

        var matches = await ReadStringsAsync(connection, "SELECT number FROM tickets WHERE search_vector @@ websearch_to_tsquery('english', 'refund')");

        matches.ShouldBe(["ACME-1"]);
    }

    [Fact]
    public async Task The_planner_can_use_the_ticket_gin_index_for_a_search_over_many_rows()
    {
        await using var connection = await OpenAsync();
        await using (var seed = connection.CreateCommand())
        {
            seed.CommandText =
                """
                INSERT INTO products (id, key, name, number_prefix, display_name, accent_colour, is_active)
                VALUES ('00000000-0000-0000-0000-000000000001', 'acme', 'Acme', 'ACME', 'Acme', '#1F6FEB', true);
                INSERT INTO requesters (id, email) VALUES ('00000000-0000-0000-0000-000000000002', 'ann@example.com');
                INSERT INTO tickets (id, number, product_id, requester_id, subject, status, priority, channel, is_spam, metadata_trusted, created_at, last_activity_at)
                SELECT gen_random_uuid(), 'ACME-' || n, '00000000-0000-0000-0000-000000000001', '00000000-0000-0000-0000-000000000002',
                       'ordinary subject number ' || n || CASE WHEN n % 1000 = 0 THEN ' zebrafish' ELSE '' END,
                       'New', 'Normal', 'Web', false, false, now(), now()
                FROM generate_series(1, 5000) AS n;
                ANALYZE tickets;
                """;
            await seed.ExecuteNonQueryAsync(Ct);
        }

        await using (var planner = connection.CreateCommand())
        {
            // Postgres may still prefer a sequential scan on a small table; turning it off proves the index CAN serve the query.
            planner.CommandText = "SET enable_seqscan = off";
            await planner.ExecuteNonQueryAsync(Ct);
        }

        var plan = string.Join('\n', await ReadStringsAsync(
            connection,
            "EXPLAIN SELECT id FROM tickets WHERE search_vector @@ websearch_to_tsquery('english', 'zebrafish')"));

        plan.ShouldContain("ix_tickets_search_vector");
    }
}
