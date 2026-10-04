using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SyntaxCircus.Common;
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

    private PersistenceTestHost NewHost(Action<IServiceCollection>? extra = null, Microsoft.Extensions.Time.Testing.FakeTimeProvider? clock = null) =>
        new(Database, clock, configure: services =>
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
    public async Task Spam_does_not_starve_the_batch()
    {
        await using var host = NewHost();
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

        var run = await RunAsync(host);

        run.Closed.ShouldBe(2);
        run.Conflicts.ShouldBe(0);
        (await ScalarAsync("SELECT count(*) FROM tickets WHERE status = 'Closed' AND NOT is_spam")).ShouldBe(2);
        (await ScalarAsync("SELECT count(*) FROM tickets WHERE status = 'Solved' AND is_spam")).ShouldBe(BatchSize + 5);
    }

    /// <summary>One Solved ticket old enough to close, a close host and a reply host on the same database and clock, both loading it before either commits.</summary>
    private sealed class RaceSetup : IAsyncDisposable
    {
        public required PersistenceTestHost Closer { get; init; }

        public required PersistenceTestHost Replier { get; init; }

        public required CommitHooks CloseHooks { get; init; }

        public required CommitHooks ReplyHooks { get; init; }

        public required Rendezvous Rendezvous { get; init; }

        public Guid TicketId { get; set; }

        public string Token { get; set; } = "";

        public async ValueTask DisposeAsync()
        {
            await Closer.DisposeAsync();
            await Replier.DisposeAsync();
        }

        public Task<AutoCloseResult> CloseAsync() => Task.Run(() => RunAsync(Closer), Ct);

        public Task<Result<CustomerReplyResponse>> ReplyAsync() => Task.Run(async () =>
        {
            await using var scope = Replier.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<IAddCustomerReplyRequestHandler>()
                .HandleAsync(Token, new AddCustomerReplyRequest("Still broken"), [], Ct);
        }, Ct);
    }

    private async Task<RaceSetup> NewRaceAsync()
    {
        var rendezvous = new Rendezvous();
        var closeHooks = new CommitHooks();
        var replyHooks = new CommitHooks();
        var clock = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(new DateTimeOffset(2026, 10, 2, 9, 0, 0, TimeSpan.Zero));
        var setup = new RaceSetup
        {
            Closer = NewHost(s => { Decorate(s, rendezvous); DecorateCommits(s, closeHooks); }, clock),
            Replier = NewHost(s => { Decorate(s, rendezvous); DecorateCommits(s, replyHooks); }, clock),
            CloseHooks = closeHooks,
            ReplyHooks = replyHooks,
            Rendezvous = rendezvous,
        };
        var seed = await SeedBaseAsync(setup.Closer);
        (setup.TicketId, setup.Token) = await SeedSolvedAsync(setup.Closer, seed, spam: false);
        setup.Closer.Clock.Advance(TimeSpan.FromDays(8));
        rendezvous.Target = setup.TicketId;
        rendezvous.Armed = true;
        return setup;
    }

    [Fact]
    public async Task A_reply_that_commits_before_the_close_makes_that_close_conflict_and_the_ticket_stays_open()
    {
        await using var race = await NewRaceAsync();
        var replyCommitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        race.CloseHooks.Before = () => replyCommitted.Task.WaitAsync(TimeSpan.FromSeconds(20), Ct);
        race.ReplyHooks.After = () => replyCommitted.TrySetResult();

        var closing = race.CloseAsync();
        var replying = race.ReplyAsync();
        var closed = await closing;
        var reply = await replying;

        race.Rendezvous.Arrived.ShouldBe(2);
        reply.IsSuccess.ShouldBeTrue(reply.IsFailure ? reply.Errors[0].Code : "");
        reply.Value.FollowUpCreated.ShouldBeFalse();
        closed.ShouldBe(new AutoCloseResult(1, 0, 1));
        (await ScalarAsync($"SELECT count(*) FROM tickets WHERE id = '{race.TicketId}' AND status = 'Open'")).ShouldBe(1);
        (await ScalarAsync($"SELECT count(*) FROM messages WHERE id = '{reply.Value.MessageId}' AND body = '<p>Still broken</p>'")).ShouldBe(1);
        (await RunAsync(race.Closer)).Closed.ShouldBe(0);
        (await ScalarAsync($"SELECT count(*) FROM tickets WHERE id = '{race.TicketId}' AND status = 'Open'")).ShouldBe(1);
    }

    [Fact]
    public async Task A_close_that_commits_before_the_reply_turns_the_reply_into_a_follow_up()
    {
        await using var race = await NewRaceAsync();
        var closeCommitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        race.ReplyHooks.Before = () => closeCommitted.Task.WaitAsync(TimeSpan.FromSeconds(20), Ct);
        race.CloseHooks.After = () => closeCommitted.TrySetResult();

        var closing = race.CloseAsync();
        var replying = race.ReplyAsync();
        var closed = await closing;
        var reply = await replying;

        race.Rendezvous.Arrived.ShouldBe(2);
        closed.ShouldBe(new AutoCloseResult(1, 1, 0));
        reply.IsSuccess.ShouldBeTrue(reply.IsFailure ? reply.Errors[0].Code : "");
        reply.Value.FollowUpCreated.ShouldBeTrue();
        (await ScalarAsync($"SELECT count(*) FROM tickets WHERE id = '{race.TicketId}' AND status = 'Closed'")).ShouldBe(1);
        (await ScalarAsync($"SELECT count(*) FROM tickets WHERE parent_ticket_id = '{race.TicketId}'")).ShouldBe(1);
        (await ScalarAsync($"SELECT count(*) FROM messages WHERE id = '{reply.Value.MessageId}' AND body = '<p>Still broken</p>'")).ShouldBe(1);
    }

    [Fact]
    public async Task A_ticket_reopened_and_solved_again_after_the_list_is_skipped_by_its_fresh_solved_time()
    {
        var rendezvous = new Rendezvous();
        await using var host = NewHost(s => Decorate(s, rendezvous));
        var seed = await SeedBaseAsync(host);
        var (ticketId, _) = await SeedSolvedAsync(host, seed, spam: false);
        host.Clock.Advance(TimeSpan.FromDays(8));
        var listed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        rendezvous.AfterList = async () =>
        {
            listed.TrySetResult();
            await release.Task.WaitAsync(TimeSpan.FromSeconds(20), Ct);
        };

        var run = Task.Run(() => RunAsync(host), Ct);
        await listed.Task.WaitAsync(TimeSpan.FromSeconds(20), Ct);
        (await host.CommitAsync(async sp =>
        {
            var tickets = sp.GetRequiredService<ITicketRepository>();
            var ticket = (await tickets.GetByIdAsync(ticketId, Ct))!;
            var actor = Actor.ForAgent(Guid.NewGuid());
            ticket.ChangeStatus(TicketStatus.Open, actor, host.Clock).IsSuccess.ShouldBeTrue();
            ticket.ChangeStatus(TicketStatus.Solved, actor, host.Clock).IsSuccess.ShouldBeTrue();
            tickets.Update(ticket);
        })).IsSuccess.ShouldBeTrue();
        release.SetResult();
        var result = await run;

        result.ShouldBe(new AutoCloseResult(1, 0, 0));
        (await ScalarAsync($"SELECT count(*) FROM tickets WHERE id = '{ticketId}' AND status = 'Solved'")).ShouldBe(1);
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

    private static void DecorateCommits(IServiceCollection services, CommitHooks hooks)
    {
        var original = services.Last(d => d.ServiceType == typeof(IUnitOfWork));
        services.AddScoped<IUnitOfWork>(sp => new HookedUnitOfWork((IUnitOfWork)ActivatorUtilities.CreateInstance(sp, original.ImplementationType!), hooks));
    }

    /// <summary>Runs <see cref="Before"/> ahead of, and <see cref="After"/> once after, the first commit made through the decorated unit of work.</summary>
    public sealed class CommitHooks
    {
        private int _before;
        private int _after;

        public Func<Task>? Before { get; set; }

        public Action? After { get; set; }

        internal Task RunBeforeAsync() => Before is { } before && Interlocked.Exchange(ref _before, 1) == 0 ? before() : Task.CompletedTask;

        internal void RunAfter()
        {
            if (After is { } after && Interlocked.Exchange(ref _after, 1) == 0)
            {
                after();
            }
        }
    }

    private sealed class HookedUnitOfWork(IUnitOfWork inner, CommitHooks hooks) : IUnitOfWork
    {
        public async Task<IUnitOfWorkScope> BeginAsync(CancellationToken cancellationToken) =>
            new HookedScope(await inner.BeginAsync(cancellationToken), hooks);
    }

    private sealed class HookedScope(IUnitOfWorkScope inner, CommitHooks hooks) : IUnitOfWorkScope
    {
        public async Task<Result> CommitAsync(CancellationToken cancellationToken)
        {
            await hooks.RunBeforeAsync();
            var result = await inner.CommitAsync(cancellationToken);
            hooks.RunAfter();
            return result;
        }

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }

    public sealed class Rendezvous
    {
        private readonly TaskCompletionSource _both = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrived;

        public bool Armed { get; set; }

        public Guid Target { get; set; }

        /// <summary>Awaited by the first <c>ListSolvedBeforeAsync</c> after it has read, before it returns.</summary>
        public Func<Task>? AfterList { get; set; }

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

                if (targetMethod.Name == nameof(ITicketRepository.ListSolvedBeforeAsync) && Rendezvous.AfterList is { } afterList)
                {
                    Rendezvous.AfterList = null;
                    return AfterListAsync((Task<IReadOnlyList<Ticket>>)result!, afterList);
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

        private static async Task<IReadOnlyList<Ticket>> AfterListAsync(Task<IReadOnlyList<Ticket>> read, Func<Task> afterList)
        {
            var listed = await read;
            await afterList();
            return listed;
        }
    }
}
