using Microsoft.EntityFrameworkCore;
using Npgsql;
using TechStrap.Infrastructure.Persistence;
using TechStrap.Infrastructure.Persistence.Records;

namespace TechStrap.Infrastructure.IntegrationTests;

/// <summary>Database-level behaviour of the ticket tables: uniqueness, cascade, append-only events and erase feasibility.</summary>
public sealed class TicketSchemaTests(PostgresFixture postgres) : PostgresIntegrationTestBase(postgres)
{
    [Theory]
    [InlineData("tickets")]
    [InlineData("messages")]
    [InlineData("attachments")]
    [InlineData("ticket_events")]
    [InlineData("ticket_tags")]
    [InlineData("ticket_access_tokens")]
    public void The_ticket_tables_are_in_the_model(string table)
    {
        ModelInspector.HasTable(table).ShouldBeTrue();
    }

    [Theory]
    [InlineData("tickets", "number")]
    [InlineData("ticket_access_tokens", "token_hash")]
    public void The_documented_unique_indexes_exist(string table, string columns)
    {
        ModelInspector.HasUniqueIndex(ModelInspector.Table(table), columns.Split(',')).ShouldBeTrue();
    }

    [Fact]
    public void Ticket_rows_carry_the_xmin_concurrency_token_and_events_carry_none()
    {
        ModelInspector.Table("tickets").GetProperties().Single(p => p.IsConcurrencyToken).GetColumnName().ShouldBe("xmin");
        ModelInspector.Table("ticket_events").GetProperties().ShouldAllBe(p => !p.IsConcurrencyToken);
    }

    private async Task<(ProductRecord Product, RequesterRecord Requester, TicketRecord Ticket)> SeedTicketAsync(TechStrapDbContext context, string number = "ACME-1")
    {
        var product = await RecordSeed.ProductAsync(context);
        var requester = await RecordSeed.RequesterAsync(context);
        var ticket = await RecordSeed.TicketAsync(context, product, requester, number);
        return (product, requester, ticket);
    }

    private async Task<long> CountAsync(string table)
    {
        await using var connection = new NpgsqlConnection(Database.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT count(*) FROM {table}";
        return (long)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    [Fact]
    public async Task A_ticket_number_is_unique_across_all_products()
    {
        await using var context = CreateDbContext();
        var (_, requester, _) = await SeedTicketAsync(context, "ACME-1");
        var other = await RecordSeed.ProductAsync(context, "orbitly", "ORB");

        var duplicate = async () => await RecordSeed.TicketAsync(context, other, requester, "ACME-1");

        var failure = await Should.ThrowAsync<DbUpdateException>(duplicate);
        (failure.InnerException as PostgresException)!.SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
    }

    [Fact]
    public async Task Hard_deleting_a_ticket_removes_its_messages_attachments_events_tag_links_and_tokens()
    {
        await using var context = CreateDbContext();
        var (_, requester, ticket) = await SeedTicketAsync(context);
        var message = await RecordSeed.MessageAsync(context, ticket);
        await RecordSeed.EventAsync(context, ticket);
        var tag = new TagRecord { Id = Guid.CreateVersion7(), Slug = "billing", Name = "Billing", Colour = "#AA00FF" };
        context.Set<TagRecord>().Add(tag);
        context.Set<TicketTagRecord>().Add(new TicketTagRecord { TicketId = ticket.Id, TagId = tag.Id });
        context.Set<AttachmentRecord>().Add(new AttachmentRecord
        {
            Id = Guid.CreateVersion7(), TicketId = ticket.Id, MessageId = message.Id, FileName = "a.txt", ContentType = "text/plain", Size = 5, StorageKey = "k", CreatedAt = RecordSeed.Now,
        });
        context.Set<TicketAccessTokenRecord>().Add(new TicketAccessTokenRecord
        {
            Id = Guid.CreateVersion7(), TicketId = ticket.Id, RequesterId = requester.Id, TokenHash = "h", ExpiresAt = RecordSeed.Now.AddDays(90),
        });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        await using var admin = new NpgsqlConnection(Database.ConnectionString);
        await admin.OpenAsync(TestContext.Current.CancellationToken);
        await using var delete = admin.CreateCommand();
        delete.CommandText = "DELETE FROM tickets";
        await delete.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);

        foreach (var table in new[] { "messages", "attachments", "ticket_events", "ticket_tags", "ticket_access_tokens" })
        {
            (await CountAsync(table)).ShouldBe(0, table);
        }

        (await CountAsync("tags")).ShouldBe(1);
    }

    [Fact]
    public async Task Modifying_a_ticket_event_through_ef_is_refused()
    {
        await using var context = CreateDbContext();
        var (_, _, ticket) = await SeedTicketAsync(context);
        var ticketEvent = await RecordSeed.EventAsync(context, ticket);

        ticketEvent.Payload = "{\"tampered\":true}";
        var save = async () => await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        (await Should.ThrowAsync<InvalidOperationException>(save)).Message.ShouldBe(AppendOnlyTicketEventInterceptor.Message);
    }

    [Fact]
    public async Task Deleting_a_single_ticket_event_through_ef_is_refused()
    {
        await using var context = CreateDbContext();
        var (_, _, ticket) = await SeedTicketAsync(context);
        var ticketEvent = await RecordSeed.EventAsync(context, ticket);

        context.Set<TicketEventRecord>().Remove(ticketEvent);
        var save = async () => await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        (await Should.ThrowAsync<InvalidOperationException>(save)).Message.ShouldBe(AppendOnlyTicketEventInterceptor.Message);
        (await CountAsync("ticket_events")).ShouldBe(1);
    }

    [Fact]
    public async Task Deleting_the_whole_ticket_through_ef_may_remove_its_events()
    {
        await using var context = CreateDbContext();
        var (_, _, ticket) = await SeedTicketAsync(context);
        var ticketEvent = await RecordSeed.EventAsync(context, ticket);

        context.Set<TicketEventRecord>().Remove(ticketEvent);
        context.Set<TicketRecord>().Remove(ticket);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        (await CountAsync("ticket_events")).ShouldBe(0);
        (await CountAsync("tickets")).ShouldBe(0);
    }

    [Fact]
    public async Task Erasing_a_requester_anonymises_rows_without_touching_a_single_event()
    {
        await using var context = CreateDbContext();
        var (_, requester, ticket) = await SeedTicketAsync(context);
        await RecordSeed.MessageAsync(context, ticket, "<p>my phone is 555-0100</p>");
        await RecordSeed.EventAsync(context, ticket);
        var before = await ReadEventPayloadsAsync();

        await using var connection = new NpgsqlConnection(Database.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var erase = connection.CreateCommand();
        erase.CommandText =
            "UPDATE requesters SET email = 'erased-' || id || '@invalid', name = NULL, erased_at = now() WHERE id = @id;" +
            "UPDATE messages SET body = 'erased' WHERE author_id = @id;";
        erase.Parameters.AddWithValue("id", requester.Id);
        await erase.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);

        (await ReadEventPayloadsAsync()).ShouldBe(before);
        before.ShouldAllBe(payload => !payload.Contains("ann@example.com", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task The_partial_indexes_for_solved_tickets_and_spam_exist()
    {
        await using var connection = new NpgsqlConnection(Database.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT indexname, indexdef FROM pg_indexes WHERE tablename = 'tickets'";
        var definitions = new Dictionary<string, string>();
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            definitions[reader.GetString(0)] = reader.GetString(1);
        }

        definitions["ix_tickets_solved_at_when_solved"].ShouldContain("WHERE");
        definitions["ix_tickets_last_activity_at_when_spam"].ShouldContain("is_spam = true");
        definitions.ShouldContainKey("ix_tickets_number");
        definitions["ix_tickets_number"].ShouldContain("UNIQUE");
    }

    private async Task<List<string>> ReadEventPayloadsAsync()
    {
        await using var connection = new NpgsqlConnection(Database.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload::text FROM ticket_events ORDER BY id";
        var payloads = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            payloads.Add(reader.GetString(0));
        }

        return payloads;
    }
}
