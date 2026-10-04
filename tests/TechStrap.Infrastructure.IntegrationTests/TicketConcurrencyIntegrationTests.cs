using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
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

    private PersistenceTestHost NewHost(Action<IServiceCollection>? extra = null) =>
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
            extra?.Invoke(services);
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
        var rendezvous = new Rendezvous();
        await using var host = NewHost(services => Decorate(services, rendezvous));
        var ticketId = await SeedAsync(host);
        var v0 = await VersionAsync(host, ticketId);
        var statusEventsBefore = await ScalarAsync("SELECT count(*) FROM ticket_events WHERE type = 'StatusChanged'");
        rendezvous.Armed = true;

        async Task<Result<TicketStateDto>> RaceAsync(string status)
        {
            // Own DI scope per racer (own DbContext and connection). The gated repository holds each racer after its load
            // until the other has loaded too, so both pass the v0 check before either commits.
            await using var scope = host.CreateScope();
            var handler = scope.ServiceProvider.GetRequiredService<IChangeTicketStatusRequestHandler>();
            return await handler.HandleAsync(ticketId, new ChangeTicketStatusRequest(status, v0), Ct);
        }

        var results = await Task.WhenAll(Task.Run(() => RaceAsync("Solved"), Ct), Task.Run(() => RaceAsync("Pending"), Ct));

        // Both racers loaded the ticket at v0 before either committed, so the loser can only have failed at commit.
        rendezvous.LoadedVersions.ShouldBe([v0, v0]);
        results.Count(r => r.IsSuccess).ShouldBe(1);
        results.Single(r => r.IsFailure).Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            e => e.Kind.ShouldBe(ResultErrorKind.Conflict),
            e => e.Code.ShouldBe(PersistenceErrorCodes.ConcurrencyConflict));
        var winner = results.Single(r => r.IsSuccess).Value;
        (await ScalarAsync($"SELECT count(*) FROM tickets WHERE id = '{ticketId}' AND status = '{winner.Status}'")).ShouldBe(1);
        (await ScalarAsync("SELECT count(*) FROM ticket_events WHERE type = 'StatusChanged'")).ShouldBe(statusEventsBefore + 1);
        // The solved notice is staged in the same unit of work: only a winning Solved racer may leave an outbox row.
        (await ScalarAsync("SELECT count(*) FROM email_outbox")).ShouldBe(winner.Status == "Solved" ? 1 : 0);
    }

    private static void Decorate(IServiceCollection services, Rendezvous rendezvous)
    {
        var original = services.Last(d => d.ServiceType == typeof(ITicketRepository));
        services.AddScoped(sp =>
        {
            var proxy = DispatchProxy.Create<ITicketRepository, GatedTicketRepository>();
            var gated = (GatedTicketRepository)(object)proxy;
            gated.Inner = (ITicketRepository)ActivatorUtilities.CreateInstance(sp, original.ImplementationType!);
            gated.Rendezvous = rendezvous;
            return proxy;
        });
    }

    public sealed class Rendezvous
    {
        private readonly TaskCompletionSource _both = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly List<uint> _loaded = [];
        private int _arrived;

        public bool Armed { get; set; }

        public IReadOnlyList<uint> LoadedVersions
        {
            get
            {
                lock (_loaded)
                {
                    return [.. _loaded];
                }
            }
        }

        public async Task ArriveAsync(uint loadedVersion)
        {
            lock (_loaded)
            {
                _loaded.Add(loadedVersion);
            }

            if (Interlocked.Increment(ref _arrived) == 2)
            {
                _both.SetResult();
            }

            await _both.Task.WaitAsync(TimeSpan.FromSeconds(20), Ct);
        }
    }

    /// <summary>Delegates every call; after the first GetByIdAsync of a scope returns, waits until both racers have loaded.</summary>
    public class GatedTicketRepository : DispatchProxy
    {
        private bool _gated;

        public ITicketRepository Inner { get; set; } = null!;

        public Rendezvous Rendezvous { get; set; } = null!;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            try
            {
                var result = targetMethod!.Invoke(Inner, args);
                if (targetMethod.Name == nameof(ITicketRepository.GetByIdAsync) && Rendezvous.Armed && !_gated)
                {
                    _gated = true;
                    return GateAsync((Task<Ticket?>)result!);
                }

                return result;
            }
            catch (TargetInvocationException ex) when (ex.InnerException is not null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw;
            }
        }

        private async Task<Ticket?> GateAsync(Task<Ticket?> load)
        {
            var ticket = await load;
            await Rendezvous.ArriveAsync(ticket?.Version ?? 0);
            return ticket;
        }
    }
}
