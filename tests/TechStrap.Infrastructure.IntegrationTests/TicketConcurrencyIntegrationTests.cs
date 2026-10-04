using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SyntaxCircus.Common;
using TechStrap.Application.Agents;
using TechStrap.Application.Intake;
using TechStrap.Application.Persistence;
using TechStrap.Application.Tickets;
using TechStrap.Contracts.Tickets;
using TechStrap.Domain.Agents;
using TechStrap.Domain.Products;
using TechStrap.Domain.Requesters;
using TechStrap.Domain.Tickets;
using TechStrap.Infrastructure.Intake;
using TechStrap.Infrastructure.Tickets;

namespace TechStrap.Infrastructure.IntegrationTests;

/// <summary>The real status and note handlers against real Postgres: D-036 optimistic concurrency end to end.</summary>
public sealed class TicketConcurrencyIntegrationTests(PostgresFixture postgres) : PostgresIntegrationTestBase(postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private sealed class StubAgentClaims : ICurrentAgentClaims
    {
        public AgentClaims? Current { get; } = new("sub-sam", "Sam Taylor", "sam.taylor@techstrap.test", AgentRole.Agent);
    }

    private PersistenceTestHost NewHost() =>
        new(Database, configure: services =>
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { [PortalLinkOptions.PublicUrlKey] = "https://help.test" })
                .Build();
            services.AddLogging();
            services.AddTechStrapIntake(configuration);
            services.AddTechStrapTicketOperations(configuration);
            services.AddSingleton<ICurrentAgentClaims, StubAgentClaims>();
            services.AddScoped<IChangeTicketStatusRequestHandler, ChangeTicketStatusRequestHandler>();
            services.AddScoped<IAddInternalNoteRequestHandler, AddInternalNoteRequestHandler>();
        });

    private static async Task<Guid> SeedAsync(PersistenceTestHost host)
    {
        Guid ticketId = default;
        (await host.CommitAsync(sp =>
        {
            var product = Product.Create("orbitly", "Orbitly", "ORB", null, host.Clock).Value;
            sp.GetRequiredService<IProductRepository>().Add(product);
            var requester = Requester.Create("pat@example.com", "Pat", null, host.Clock).Value;
            sp.GetRequiredService<IRequesterRepository>().Add(requester);
            var sam = Agent.Create("sub-sam", "Sam Taylor", "sam.taylor@techstrap.test", AgentRole.Agent, host.Clock).Value;
            sp.GetRequiredService<IAgentRepository>().Add(sam);
            var ticket = Ticket.Create(TicketNumber.Create("ORB", 1).Value, product.Id, requester.Id, "Cannot log in", TicketChannel.Web, null, false, host.Clock).Value;
            ticket.AddCustomerReply(requester.Id, "<p>Help</p>", host.Clock).IsSuccess.ShouldBeTrue();
            ticket.ChangeStatus(TicketStatus.Open, Actor.ForAgent(sam.Id), host.Clock).IsSuccess.ShouldBeTrue();
            sp.GetRequiredService<ITicketRepository>().Add(ticket);
            ticketId = ticket.Id;
            return Task.CompletedTask;
        })).IsSuccess.ShouldBeTrue();
        return ticketId;
    }

    private static Task<uint> VersionAsync(PersistenceTestHost host, Guid ticketId) =>
        host.ReadAsync(async sp => (await sp.GetRequiredService<ITicketRepository>().GetStateAsync(ticketId, Ct))!.Version);

    private static async Task<Result<TicketStateDto>> ChangeStatusAsync(PersistenceTestHost host, Guid ticketId, string status, uint? rowVersion)
    {
        await using var scope = host.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IChangeTicketStatusRequestHandler>()
            .HandleAsync(ticketId, new ChangeTicketStatusRequest(status, rowVersion), Ct);
    }

    private async Task<long> ScalarAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(Database.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync(Ct));
    }

    [Fact]
    public async Task A_stale_row_version_changes_nothing_and_a_parallel_reply_still_lands()
    {
        await using var host = NewHost();
        var ticketId = await SeedAsync(host);
        var v0 = await VersionAsync(host, ticketId);
        var statusEventsBefore = await ScalarAsync("SELECT count(*) FROM ticket_events WHERE type = 'StatusChanged'");
        var messagesBefore = await ScalarAsync("SELECT count(*) FROM messages");

        // Agent A moves the ticket on with the current version.
        var a = await ChangeStatusAsync(host, ticketId, "Pending", v0);
        a.IsSuccess.ShouldBeTrue();
        var v1 = a.Value.RowVersion;
        v1.ShouldNotBe(v0);

        // Agent B still holds v0: the change is refused and leaves no trace.
        var b = await ChangeStatusAsync(host, ticketId, "Solved", v0);
        b.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            e => e.Kind.ShouldBe(ResultErrorKind.Conflict),
            e => e.Code.ShouldBe(PersistenceErrorCodes.ConcurrencyConflict));
        (await ScalarAsync($"SELECT count(*) FROM tickets WHERE id = '{ticketId}' AND status = 'Pending' AND solved_at IS NULL")).ShouldBe(1);
        (await ScalarAsync("SELECT count(*) FROM ticket_events WHERE type = 'StatusChanged'")).ShouldBe(statusEventsBefore + 1);
        (await ScalarAsync("SELECT count(*) FROM email_outbox")).ShouldBe(0);
        (await VersionAsync(host, ticketId)).ShouldBe(v1);

        // B's note carries no row version, so it is not subject to the check and still lands.
        await using var scope = host.CreateScope();
        var note = await scope.ServiceProvider.GetRequiredService<IAddInternalNoteRequestHandler>()
            .HandleAsync(ticketId, new AddInternalNoteRequest("Seen on two accounts", null), Ct);
        note.IsSuccess.ShouldBeTrue();
        note.Value.Message.Visibility.ShouldBe("Internal");
        (await ScalarAsync("SELECT count(*) FROM messages")).ShouldBe(messagesBefore + 1);
        (await ScalarAsync($"SELECT count(*) FROM tickets WHERE id = '{ticketId}' AND status = 'Pending'")).ShouldBe(1);
    }

    [Fact]
    public async Task Two_simultaneous_status_changes_with_the_same_version_let_exactly_one_win()
    {
        await using var host = NewHost();
        var ticketId = await SeedAsync(host);
        var v0 = await VersionAsync(host, ticketId);
        var statusEventsBefore = await ScalarAsync("SELECT count(*) FROM ticket_events WHERE type = 'StatusChanged'");
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ready = 0;

        async Task<Result<TicketStateDto>> RaceAsync(string status)
        {
            // Each racer has its own DI scope (own DbContext and connection) and waits at the gate for the other.
            await using var scope = host.CreateScope();
            var handler = scope.ServiceProvider.GetRequiredService<IChangeTicketStatusRequestHandler>();
            Interlocked.Increment(ref ready);
            await gate.Task;
            return await handler.HandleAsync(ticketId, new ChangeTicketStatusRequest(status, v0), Ct);
        }

        var first = Task.Run(() => RaceAsync("Pending"), Ct);
        var second = Task.Run(() => RaceAsync("Solved"), Ct);
        while (Volatile.Read(ref ready) < 2)
        {
            await Task.Delay(5, Ct);
        }

        gate.SetResult();
        var results = await Task.WhenAll(first, second);

        results.Count(r => r.IsSuccess).ShouldBe(1);
        var loser = results.Single(r => r.IsFailure);
        loser.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            e => e.Kind.ShouldBe(ResultErrorKind.Conflict),
            e => e.Code.ShouldBe(PersistenceErrorCodes.ConcurrencyConflict));
        var winner = results.Single(r => r.IsSuccess).Value;
        (await ScalarAsync($"SELECT count(*) FROM tickets WHERE id = '{ticketId}' AND status = '{winner.Status}'")).ShouldBe(1);
        (await ScalarAsync("SELECT count(*) FROM ticket_events WHERE type = 'StatusChanged'")).ShouldBe(statusEventsBefore + 1);
    }
}
