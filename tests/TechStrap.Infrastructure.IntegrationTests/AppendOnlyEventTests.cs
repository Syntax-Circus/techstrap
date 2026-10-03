using Microsoft.EntityFrameworkCore;
using Npgsql;
using TechStrap.Domain.Admin;
using TechStrap.Infrastructure.Persistence;
using TechStrap.Infrastructure.Persistence.Records;

namespace TechStrap.Infrastructure.IntegrationTests;

/// <summary>The second line of defence: EF refuses to modify or delete events of either append-only table.</summary>
public sealed class AppendOnlyEventTests(PostgresFixture postgres) : PostgresIntegrationTestBase(postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static async Task<AdminEventRecord> SeedAdminEventAsync(TechStrapDbContext context)
    {
        var record = new AdminEventRecord
        {
            Id = Guid.CreateVersion7(),
            Type = AdminEventType.ProductCreated,
            ActorId = Guid.NewGuid(),
            SubjectType = AdminSubjectType.Product,
            SubjectId = Guid.NewGuid(),
            Payload = "{}",
            OccurredAt = RecordSeed.Now,
        };
        context.Set<AdminEventRecord>().Add(record);
        await context.SaveChangesAsync(Ct);
        return record;
    }

    private async Task<long> CountAsync(string table)
    {
        await using var connection = new NpgsqlConnection(Database.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT count(*) FROM {table}";
        return (long)(await command.ExecuteScalarAsync(Ct))!;
    }

    [Fact]
    public async Task Modifying_an_admin_event_through_ef_is_refused()
    {
        await using var context = CreateDbContext();
        var record = await SeedAdminEventAsync(context);

        record.Payload = "{\"tampered\":true}";
        var save = async () => await context.SaveChangesAsync(Ct);

        (await Should.ThrowAsync<InvalidOperationException>(save)).Message.ShouldBe(AppendOnlyEventInterceptor.Message);
    }

    [Fact]
    public async Task Deleting_an_admin_event_through_ef_is_refused_and_the_row_stays()
    {
        await using var context = CreateDbContext();
        var record = await SeedAdminEventAsync(context);

        context.Set<AdminEventRecord>().Remove(record);
        var save = async () => await context.SaveChangesAsync(Ct);

        (await Should.ThrowAsync<InvalidOperationException>(save)).Message.ShouldBe(AppendOnlyEventInterceptor.Message);
        (await CountAsync("admin_events")).ShouldBe(1);
    }

    [Fact]
    public async Task Modifying_a_ticket_event_through_the_synchronous_save_is_refused_too()
    {
        await using var context = CreateDbContext();
        var product = await RecordSeed.ProductAsync(context);
        var requester = await RecordSeed.RequesterAsync(context);
        var ticket = await RecordSeed.TicketAsync(context, product, requester, "ACME-1");
        var ticketEvent = await RecordSeed.EventAsync(context, ticket);

        ticketEvent.Payload = "{\"tampered\":true}";

        Should.Throw<InvalidOperationException>(() => context.SaveChanges()).Message.ShouldBe(AppendOnlyEventInterceptor.Message);
    }

    [Fact]
    public async Task An_event_attached_from_elsewhere_and_then_changed_is_refused()
    {
        Guid eventId;
        await using (var seed = CreateDbContext())
        {
            var product = await RecordSeed.ProductAsync(seed);
            var requester = await RecordSeed.RequesterAsync(seed);
            var ticket = await RecordSeed.TicketAsync(seed, product, requester, "ACME-1");
            eventId = (await RecordSeed.EventAsync(seed, ticket)).Id;
        }

        await using var context = CreateDbContext();
        var attached = new TicketEventRecord { Id = eventId, TicketId = Guid.NewGuid(), Payload = "{}" };
        context.Attach(attached);
        attached.Payload = "{\"tampered\":true}";

        var save = async () => await context.SaveChangesAsync(Ct);

        (await Should.ThrowAsync<InvalidOperationException>(save)).Message.ShouldBe(AppendOnlyEventInterceptor.Message);
    }

    [Fact]
    public async Task Moving_an_event_to_another_ticket_is_refused()
    {
        await using var context = CreateDbContext();
        var product = await RecordSeed.ProductAsync(context);
        var requester = await RecordSeed.RequesterAsync(context);
        var first = await RecordSeed.TicketAsync(context, product, requester, "ACME-1");
        var second = await RecordSeed.TicketAsync(context, product, requester, "ACME-2");
        var ticketEvent = await RecordSeed.EventAsync(context, first);

        ticketEvent.TicketId = second.Id;
        var save = async () => await context.SaveChangesAsync(Ct);

        (await Should.ThrowAsync<InvalidOperationException>(save)).Message.ShouldBe(AppendOnlyEventInterceptor.Message);
    }
}
