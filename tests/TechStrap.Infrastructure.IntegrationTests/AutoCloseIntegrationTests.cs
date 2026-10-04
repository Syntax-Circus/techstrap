using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TechStrap.Application.Intake;
using TechStrap.Application.Persistence;
using TechStrap.Application.Security;
using TechStrap.Application.Tickets.AutoClose;
using TechStrap.Application.Tickets.Customer;
using TechStrap.Contracts.Tickets;
using TechStrap.Domain.Products;
using TechStrap.Domain.Requesters;
using TechStrap.Domain.Tickets;
using TechStrap.Infrastructure.AutoClose;
using TechStrap.Infrastructure.Intake;
using TechStrap.Infrastructure.Tickets;

namespace TechStrap.Infrastructure.IntegrationTests;

/// <summary>The real auto-close handler against real Postgres: spam never starves the batch, and a racing customer reply is never lost.</summary>
public sealed class AutoCloseIntegrationTests(PostgresFixture postgres) : PostgresIntegrationTestBase(postgres)
{
    private const int BatchSize = 3;
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private PersistenceTestHost NewHost(Action<IServiceCollection>? extra = null) =>
        new(Database, configure: services =>
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [PortalLinkOptions.PublicUrlKey] = "https://help.test",
                    [AutoCloseOptions.DaysKey] = "7",
                    ["AutoClose:BatchSize"] = BatchSize.ToString(),
                })
                .Build();
            services.AddLogging();
            services.AddTechStrapIntake(configuration);
            services.AddTechStrapTicketOperations(configuration);
            services.AddTechStrapAutoClose(configuration);
            services.AddScoped<IAddCustomerReplyRequestHandler, AddCustomerReplyRequestHandler>();
            extra?.Invoke(services);
        });

    private sealed record Seed(Guid ProductId, Guid RequesterId);

    private static async Task<Seed> SeedBaseAsync(PersistenceTestHost host)
    {
        Guid productId = default, requesterId = default;
        (await host.CommitAsync(sp =>
        {
            var product = Product.Create("orbitly", "Orbitly", "ORB", null, host.Clock).Value;
            sp.GetRequiredService<IProductRepository>().Add(product);
            var requester = Requester.Create("pat@example.com", "Pat", null, host.Clock).Value;
            sp.GetRequiredService<IRequesterRepository>().Add(requester);
            (productId, requesterId) = (product.Id, requester.Id);
            return Task.CompletedTask;
        })).IsSuccess.ShouldBeTrue();
        return new Seed(productId, requesterId);
    }

    private static async Task<(Guid TicketId, string Token)> SeedSolvedAsync(PersistenceTestHost host, Seed seed, bool spam)
    {
        Guid ticketId = default;
        var token = "";
        (await host.CommitAsync(async sp =>
        {
            var number = (await sp.GetRequiredService<ITicketNumberAllocator>().AllocateAsync(seed.ProductId, Ct)).Value;
            var ticket = Ticket.Create(number, seed.ProductId, seed.RequesterId, "Cannot log in", TicketChannel.Web, null, false, host.Clock).Value;
            ticket.AddCustomerReply(seed.RequesterId, "<p>Help</p>", host.Clock).IsSuccess.ShouldBeTrue();
            var actor = Actor.ForAgent(Guid.NewGuid());
            if (spam)
            {
                ticket.MarkSpam(true, actor, host.Clock).IsSuccess.ShouldBeTrue();
            }

            ticket.ChangeStatus(TicketStatus.Solved, actor, host.Clock).IsSuccess.ShouldBeTrue();
            var tickets = sp.GetRequiredService<ITicketRepository>();
            tickets.Add(ticket);
            var issued = sp.GetRequiredService<IAccessTokenService>().Issue(ticket.Id, seed.RequesterId).Value;
            tickets.AddAccessToken(issued.Token);
            (ticketId, token) = (ticket.Id, issued.PlaintextToken);
        })).IsSuccess.ShouldBeTrue();
        return (ticketId, token);
    }

    private static async Task<AutoCloseResult> RunAsync(PersistenceTestHost host)
    {
        await using var scope = host.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IAutoCloseSolvedTicketsHandler>().HandleAsync(Ct);
        result.IsSuccess.ShouldBeTrue();
        return result.Value;
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
    public async Task A_seeded_solved_ticket_closes_after_n_days_and_no_email_is_queued()
    {
        await using var host = NewHost();
        var seed = await SeedBaseAsync(host);
        var (ticketId, _) = await SeedSolvedAsync(host, seed, spam: false);

        host.Clock.Advance(TimeSpan.FromDays(6));
        (await RunAsync(host)).ShouldBe(new AutoCloseResult(0, 0, 0));
        host.Clock.Advance(TimeSpan.FromDays(1) + TimeSpan.FromMinutes(1));
        (await RunAsync(host)).ShouldBe(new AutoCloseResult(1, 1, 0));

        (await ScalarAsync($"SELECT count(*) FROM tickets WHERE id = '{ticketId}' AND status = 'Closed' AND closed_at IS NOT NULL")).ShouldBe(1);
        (await ScalarAsync("SELECT count(*) FROM ticket_events WHERE type = 'StatusChanged' AND actor_type = 'System' AND payload::text LIKE '%Closed%'")).ShouldBe(1);
        (await ScalarAsync("SELECT count(*) FROM email_outbox")).ShouldBe(0);
        (await RunAsync(host)).ShouldBe(new AutoCloseResult(0, 0, 0));
    }

    [Fact]
    public async Task Spam_does_not_starve_the_batch_and_a_racing_reply_is_never_lost()
    {
        var rendezvous = new Rendezvous();
        await using var host = NewHost(services => Decorate(services, rendezvous));
        var seed = await SeedBaseAsync(host);

        // Solved spam older than every real ticket and more than a batch of it: if spam were not excluded it would fill every batch.
        for (var i = 0; i < BatchSize + 5; i++)
        {
            await SeedSolvedAsync(host, seed, spam: true);
        }

        host.Clock.Advance(TimeSpan.FromHours(1));
        await SeedSolvedAsync(host, seed, spam: false);
        await SeedSolvedAsync(host, seed, spam: false);
        host.Clock.Advance(TimeSpan.FromDays(8));

        var first = await RunAsync(host);

        first.Closed.ShouldBe(2);
        first.Conflicts.ShouldBe(0);
        (await ScalarAsync("SELECT count(*) FROM tickets WHERE status = 'Closed' AND NOT is_spam")).ShouldBe(2);
        (await ScalarAsync("SELECT count(*) FROM tickets WHERE status = 'Solved' AND is_spam")).ShouldBe(BatchSize + 5);

        // The race: the close and a customer reply both load the ticket as Solved before either commits.
        var (raced, token) = await SeedSolvedAsync(host, seed, spam: false);
        host.Clock.Advance(TimeSpan.FromDays(8));
        rendezvous.Target = raced;
        rendezvous.Armed = true;

        var closing = Task.Run(() => RunAsync(host), Ct);
        var replying = Task.Run(async () =>
        {
            await using var scope = host.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<IAddCustomerReplyRequestHandler>()
                .HandleAsync(token, new AddCustomerReplyRequest("Still broken"), [], Ct);
        }, Ct);
        var closed = await closing;
        var reply = await replying;

        rendezvous.Arrived.ShouldBe(2);
        reply.IsSuccess.ShouldBeTrue(reply.IsFailure ? reply.Errors[0].Code : "");
        // Never a lost message, whoever won.
        (await ScalarAsync($"SELECT count(*) FROM messages WHERE id = '{reply.Value.MessageId}' AND body = '<p>Still broken</p>'")).ShouldBe(1);
        if (closed.Conflicts == 1)
        {
            // The reply won: the close failed on its own and the ticket is open again, so the next run does not touch it.
            closed.Closed.ShouldBe(0);
            reply.Value.FollowUpCreated.ShouldBeFalse();
            (await ScalarAsync($"SELECT count(*) FROM tickets WHERE id = '{raced}' AND status = 'Open'")).ShouldBe(1);
            (await RunAsync(host)).Closed.ShouldBe(0);
        }
        else
        {
            // The close won: the reply became a follow-up on the closed ticket.
            closed.Closed.ShouldBe(1);
            reply.Value.FollowUpCreated.ShouldBeTrue();
            (await ScalarAsync($"SELECT count(*) FROM tickets WHERE id = '{raced}' AND status = 'Closed'")).ShouldBe(1);
            (await ScalarAsync($"SELECT count(*) FROM tickets WHERE parent_ticket_id = '{raced}'")).ShouldBe(1);
        }
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
        private int _arrived;

        public bool Armed { get; set; }

        public Guid Target { get; set; }

        public int Arrived => Volatile.Read(ref _arrived);

        public async Task ArriveAsync()
        {
            if (Interlocked.Increment(ref _arrived) == 2)
            {
                _both.SetResult();
            }

            await _both.Task.WaitAsync(TimeSpan.FromSeconds(20), Ct);
        }
    }

    /// <summary>Delegates every call; the first load of the target ticket in a scope waits (after reading) until both racers have loaded it.</summary>
    public class GatedTicketRepository : DispatchProxy
    {
        private bool _gated;

        internal ITicketRepository Inner { get; set; } = null!;

        internal Rendezvous Rendezvous { get; set; } = null!;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            try
            {
                var result = targetMethod!.Invoke(Inner, args);
                if (targetMethod.Name == nameof(ITicketRepository.GetByIdAsync) && Rendezvous.Armed && !_gated && (Guid)args![0]! == Rendezvous.Target)
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
            await Rendezvous.ArriveAsync();
            return ticket;
        }
    }
}
