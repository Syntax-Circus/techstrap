using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SyntaxCircus.Common;
using TechStrap.Application.Intake;
using TechStrap.Application.Persistence;
using TechStrap.Application.Security;
using TechStrap.Application.Tickets.Customer;
using TechStrap.Contracts.Tickets;
using TechStrap.Domain.Agents;
using TechStrap.Domain.Products;
using TechStrap.Domain.Requesters;
using TechStrap.Domain.Tickets;
using TechStrap.Infrastructure.Intake;
using TechStrap.Infrastructure.Tickets;

namespace TechStrap.Infrastructure.IntegrationTests;

/// <summary>The real customer reply handler and planner against real Postgres: reopen, follow-ups, and the concurrent-follow-up race.</summary>
public sealed class CustomerReplyIntegrationTests(PostgresFixture postgres) : PostgresIntegrationTestBase(postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private sealed record Seed(Guid TicketId, string Token, string Number);

    private PersistenceTestHost NewHost(Action<IServiceCollection>? extra = null) =>
        new(Database, configure: services =>
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { [PortalLinkOptions.PublicUrlKey] = "https://help.test" })
                .Build();
            services.AddLogging();
            services.AddTechStrapIntake(configuration);
            services.AddTechStrapTicketOperations(configuration);
            services.AddScoped<IAddCustomerReplyRequestHandler, AddCustomerReplyRequestHandler>();
            extra?.Invoke(services);
        });

    private static async Task<Seed> SeedAsync(PersistenceTestHost host, TicketStatus status)
    {
        Guid ticketId = default;
        var token = "";
        var number = "";
        Guid productId = default, requesterId = default, samId = default;
        (await host.CommitAsync(async sp =>
        {
            var product = Product.Create("orbitly", "Orbitly", "ORB", null, host.Clock).Value;
            sp.GetRequiredService<IProductRepository>().Add(product);
            var requester = Requester.Create("pat@example.com", "Pat", null, host.Clock).Value;
            sp.GetRequiredService<IRequesterRepository>().Add(requester);
            var sam = Agent.Create("sub-sam", "Sam Taylor", "sam.taylor@techstrap.test", AgentRole.Agent, host.Clock).Value;
            var alex = Agent.Create("sub-alex", "Alex Doe", "alex.doe@techstrap.test", AgentRole.Agent, host.Clock).Value;
            var agents = sp.GetRequiredService<IAgentRepository>();
            agents.Add(sam);
            agents.Add(alex);
            await agents.SetNotificationPreferenceAsync(new AgentNotificationPreference(sam.Id, product.Id, true), Ct);
            await agents.SetNotificationPreferenceAsync(new AgentNotificationPreference(alex.Id, product.Id, true), Ct);
            (productId, requesterId, samId) = (product.Id, requester.Id, sam.Id);
        })).IsSuccess.ShouldBeTrue();
        (await host.CommitAsync(async sp =>
        {
            var number0 = (await sp.GetRequiredService<ITicketNumberAllocator>().AllocateAsync(productId, Ct)).Value;
            var ticket = Ticket.Create(number0, productId, requesterId, "Cannot log in", TicketChannel.Web, null, false, host.Clock).Value;
            ticket.AddCustomerReply(requesterId, "<p>Help</p>", host.Clock).IsSuccess.ShouldBeTrue();
            var actor = Actor.ForAgent(samId);
            foreach (var step in status == TicketStatus.Closed ? [TicketStatus.Solved, TicketStatus.Closed] : new[] { status })
            {
                ticket.ChangeStatus(step, actor, host.Clock).IsSuccess.ShouldBeTrue();
            }

            var tickets = sp.GetRequiredService<ITicketRepository>();
            tickets.Add(ticket);
            var issued = sp.GetRequiredService<IAccessTokenService>().Issue(ticket.Id, requesterId).Value;
            tickets.AddAccessToken(issued.Token);
            (ticketId, token, number) = (ticket.Id, issued.PlaintextToken, ticket.Number.ToString());
        })).IsSuccess.ShouldBeTrue();
        return new Seed(ticketId, token, number);
    }

    private static async Task<Result<CustomerReplyResponse>> ReplyAsync(PersistenceTestHost host, string token, string body)
    {
        await using var scope = host.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IAddCustomerReplyRequestHandler>().HandleAsync(token, new AddCustomerReplyRequest(body), [], Ct);
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
    public async Task A_reply_reopens_a_solved_ticket_and_commits_message_event_and_alert_together()
    {
        await using var host = NewHost();
        var seed = await SeedAsync(host, TicketStatus.Solved);

        var result = await ReplyAsync(host, seed.Token, "Still broken");

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Errors[0].Code + ": " + result.Errors[0].Message : "");
        result.Value.FollowUpCreated.ShouldBeFalse();
        (await ScalarAsync($"SELECT count(*) FROM tickets WHERE id = '{seed.TicketId}' AND status = 'Open'")).ShouldBe(1);
        (await ScalarAsync($"SELECT count(*) FROM messages WHERE id = '{result.Value.MessageId}' AND author_type = 'Requester' AND body = '<p>Still broken</p>'")).ShouldBe(1);
        (await ScalarAsync("SELECT count(*) FROM ticket_events WHERE type = 'MessageAdded' AND actor_type = 'Requester'")).ShouldBe(2);
        (await ScalarAsync("SELECT count(*) FROM ticket_events WHERE type = 'StatusChanged' AND payload::text LIKE '%Solved%' AND payload::text LIKE '%Open%'")).ShouldBe(1);
        (await ScalarAsync("SELECT count(*) FROM email_outbox WHERE kind = 'customer-reply-alert'")).ShouldBe(2);
        (await ScalarAsync("SELECT count(*) FROM tickets")).ShouldBe(1);
    }

    [Fact]
    public async Task A_reply_on_a_closed_ticket_commits_the_follow_up_its_message_tokens_and_emails()
    {
        await using var host = NewHost();
        var seed = await SeedAsync(host, TicketStatus.Closed);

        var result = await ReplyAsync(host, seed.Token, "It broke again");

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Errors[0].Code + ": " + result.Errors[0].Message : "");
        result.Value.FollowUpCreated.ShouldBeTrue();
        result.Value.TicketNumber.ShouldBe("ORB-2");
        result.Value.FollowUpViewUrl.ShouldNotBeNull().ShouldStartWith("https://help.test/t/");
        (await ScalarAsync($"SELECT count(*) FROM tickets WHERE parent_ticket_id = '{seed.TicketId}' AND number = 'ORB-2'")).ShouldBe(1);
        (await ScalarAsync($"SELECT count(*) FROM tickets WHERE id = '{seed.TicketId}' AND status = 'Closed'")).ShouldBe(1);
        (await ScalarAsync("SELECT count(*) FROM ticket_events e JOIN tickets t ON t.id = e.ticket_id WHERE t.parent_ticket_id IS NOT NULL AND e.type = 'Created'")).ShouldBe(1);
        (await ScalarAsync("SELECT count(*) FROM ticket_events e JOIN tickets t ON t.id = e.ticket_id WHERE t.parent_ticket_id IS NOT NULL AND e.type = 'MessageAdded'")).ShouldBe(1);
        (await ScalarAsync($"SELECT count(*) FROM ticket_events WHERE ticket_id = '{seed.TicketId}' AND type = 'FollowUpCreated'")).ShouldBe(1);
        (await ScalarAsync($"SELECT count(*) FROM messages WHERE id = '{result.Value.MessageId}' AND body = '<p>It broke again</p>'")).ShouldBe(1);
        (await ScalarAsync("SELECT count(*) FROM ticket_access_tokens")).ShouldBe(3); // the seed, the response link and the confirmation email link
        (await ScalarAsync("SELECT count(*) FROM email_outbox WHERE kind = 'ticket-confirmation' AND to_address = 'pat@example.com'")).ShouldBe(1);
        (await ScalarAsync("SELECT count(*) FROM email_outbox WHERE kind = 'new-ticket-alert'")).ShouldBe(2);
        (await ScalarAsync("SELECT count(*) FROM email_outbox WHERE kind = 'customer-reply-alert'")).ShouldBe(0);

        // The same text again, within the window, replays that follow-up: no new rows other than a fresh token.
        var again = await ReplyAsync(host, seed.Token, "It broke again");
        again.IsSuccess.ShouldBeTrue();
        again.Value.TicketNumber.ShouldBe("ORB-2");
        again.Value.MessageId.ShouldBe(result.Value.MessageId);
        again.Value.FollowUpViewUrl.ShouldNotBe(result.Value.FollowUpViewUrl);
        (await ScalarAsync("SELECT count(*) FROM tickets")).ShouldBe(2);
        (await ScalarAsync("SELECT count(*) FROM ticket_access_tokens")).ShouldBe(4);
        (await ScalarAsync("SELECT count(*) FROM email_outbox")).ShouldBe(3);
    }

    [Fact]
    public async Task Two_concurrent_replies_on_a_closed_ticket_create_one_follow_up()
    {
        var rendezvous = new Rendezvous();
        await using var host = NewHost(services => Decorate(services, rendezvous));
        var seed = await SeedAsync(host, TicketStatus.Closed);
        rendezvous.Armed = true;

        Task<Result<CustomerReplyResponse>> RaceAsync() => Task.Run(() => ReplyAsync(host, seed.Token, "It broke again"), Ct);

        var results = await Task.WhenAll(RaceAsync(), RaceAsync());

        // Both read "no follow-up yet" before either committed, so the loser can only have converged through its retry.
        rendezvous.Arrived.ShouldBe(2);
        results.ShouldAllBe(r => r.IsSuccess);
        results.Select(r => r.Value.TicketNumber).Distinct().ShouldHaveSingleItem();
        results.ShouldAllBe(r => r.Value.FollowUpCreated);
        results.Select(r => r.Value.MessageId).Distinct().ShouldHaveSingleItem();
        (await ScalarAsync($"SELECT count(*) FROM tickets WHERE parent_ticket_id = '{seed.TicketId}'")).ShouldBe(1);
        (await ScalarAsync("SELECT count(*) FROM tickets")).ShouldBe(2);
        (await ScalarAsync($"SELECT count(*) FROM ticket_events WHERE ticket_id = '{seed.TicketId}' AND type = 'FollowUpCreated'")).ShouldBe(1);
        (await ScalarAsync("SELECT count(*) FROM email_outbox WHERE kind = 'ticket-confirmation'")).ShouldBe(1);
        (await ScalarAsync("SELECT count(*) FROM email_outbox WHERE kind = 'new-ticket-alert'")).ShouldBe(2);
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

    /// <summary>Delegates every call; the first dedupe read of a scope waits (after reading) until both racers have read.</summary>
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
                if (targetMethod.Name == nameof(ITicketRepository.ListRecentFollowUpsAsync) && Rendezvous.Armed && !_gated)
                {
                    _gated = true;
                    return GateAsync((Task<IReadOnlyList<TechStrap.Application.Tickets.FollowUpCandidate>>)result!);
                }

                return result;
            }
            catch (TargetInvocationException ex) when (ex.InnerException is not null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw;
            }
        }

        private async Task<IReadOnlyList<TechStrap.Application.Tickets.FollowUpCandidate>> GateAsync(Task<IReadOnlyList<TechStrap.Application.Tickets.FollowUpCandidate>> read)
        {
            var candidates = await read;
            await Rendezvous.ArriveAsync();
            return candidates;
        }
    }
}
