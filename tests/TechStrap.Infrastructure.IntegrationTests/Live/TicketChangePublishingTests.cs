using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using TechStrap.Application.Live;
using TechStrap.Application.Persistence;
using TechStrap.Contracts.Live;
using TechStrap.Contracts.Tickets;
using TechStrap.Domain.Tickets;
using TechStrap.Infrastructure.Persistence;
using TechStrap.Infrastructure.Persistence.Records;

namespace TechStrap.Infrastructure.IntegrationTests.Live;

/// <summary>
/// The post-commit hook (D-018) against real Postgres: a committed unit of work publishes one change per ticket, and nothing that did not commit ever does.
/// The broadcaster is a recorder registered over the default, exactly as the Api and the Worker register theirs.
/// </summary>
public sealed class TicketChangePublishingTests(PostgresFixture postgres) : PostgresIntegrationTestBase(postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private readonly RecordingBroadcaster _recorder = new();

    private PersistenceTestHost NewHost() =>
        new(Database, configure: services => services.Replace(ServiceDescriptor.Singleton<ITicketChangeBroadcaster>(_recorder)));

    private static async Task StageNewTicketAsync(IServiceProvider services, TicketScenario scenario, string subject)
    {
        var number = (await services.GetRequiredService<ITicketNumberAllocator>().AllocateAsync(scenario.Acme.Id, Ct)).Value;
        var ticket = Ticket.Create(number, scenario.Acme.Id, scenario.Requester.Id, subject, TicketChannel.Web, null, false, scenario.Host.Clock).Value;
        ticket.AddCustomerReply(scenario.Requester.Id, "<p>Help</p>", scenario.Host.Clock).IsSuccess.ShouldBeTrue();
        services.GetRequiredService<ITicketRepository>().Add(ticket);
    }

    [Fact]
    public async Task Committing_a_new_ticket_publishes_one_created_change_with_ids_and_no_customer_actor()
    {
        await using var host = NewHost();
        var scenario = await TicketScenario.CreateAsync(host);
        _recorder.Attempts.ShouldBeEmpty();

        var ticket = await scenario.CreateTicketAsync();

        var change = _recorder.Attempts.ShouldHaveSingleItem();
        change.Kind.ShouldBe(TicketChangeKinds.Created);
        change.EventType.ShouldBe(TicketEventTypes.Created);
        change.TicketId.ShouldBe(ticket.Id);
        change.TicketNumber.ShouldBe(ticket.Number.ToString());
        change.ProductId.ShouldBe(scenario.Acme.Id);
        change.ActorAgentId.ShouldBeNull();
        change.EventId.ShouldNotBe(Guid.Empty);
        change.OccurredAt.ShouldBe(host.Clock.GetUtcNow() - TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Several_events_in_one_commit_publish_one_change_named_by_the_newest()
    {
        await using var host = NewHost();
        var scenario = await TicketScenario.CreateAsync(host);
        var ticket = await scenario.CreateTicketAsync();
        _recorder.Attempts.Count.ShouldBe(1);

        var result = await scenario.UpdateAsync(ticket.Id, loaded =>
        {
            loaded.ChangeStatus(TicketStatus.Open, scenario.AgentActor, host.Clock).IsSuccess.ShouldBeTrue();
            host.Clock.Advance(TimeSpan.FromSeconds(1));
            loaded.ChangePriority(TicketPriority.High, scenario.AgentActor, host.Clock).IsSuccess.ShouldBeTrue();
            host.Clock.Advance(TimeSpan.FromSeconds(1));
            loaded.AddInternalNote(scenario.Agent.Id, "<p>looking</p>", host.Clock).IsSuccess.ShouldBeTrue();
        });

        result.IsSuccess.ShouldBeTrue();
        _recorder.Attempts.Count.ShouldBe(2);
        var change = _recorder.Attempts[1];
        change.Kind.ShouldBe(TicketChangeKinds.Updated);
        change.EventType.ShouldBe(TicketEventTypes.MessageAdded);
        change.ActorAgentId.ShouldBe(scenario.Agent.Id);
        change.TicketId.ShouldBe(ticket.Id);
    }

    [Fact]
    public async Task One_commit_that_touches_two_tickets_publishes_one_change_for_each()
    {
        await using var host = NewHost();
        var scenario = await TicketScenario.CreateAsync(host);

        var result = await host.CommitAsync(async services =>
        {
            await StageNewTicketAsync(services, scenario, "First");
            await StageNewTicketAsync(services, scenario, "Second");
        });

        result.IsSuccess.ShouldBeTrue();
        _recorder.Attempts.Count.ShouldBe(2);
        _recorder.Attempts.Select(change => change.TicketId).Distinct().Count().ShouldBe(2);
        _recorder.Attempts.ShouldAllBe(change => change.Kind == TicketChangeKinds.Created);
    }

    [Fact]
    public async Task Moving_a_ticket_to_another_product_publishes_the_new_product()
    {
        await using var host = NewHost();
        var scenario = await TicketScenario.CreateAsync(host);
        var ticket = await scenario.CreateTicketAsync();

        (await scenario.UpdateAsync(ticket.Id, loaded =>
            loaded.MoveToProduct(scenario.Orbitly.Id, scenario.AgentActor, host.Clock).IsSuccess.ShouldBeTrue())).IsSuccess.ShouldBeTrue();

        var change = _recorder.Attempts.Last();
        change.EventType.ShouldBe(TicketEventTypes.ProductChanged);
        change.ProductId.ShouldBe(scenario.Orbitly.Id);
    }

    [Fact]
    public async Task Work_that_was_saved_but_rolled_back_publishes_nothing_and_leaves_nothing_behind_in_the_next_scope()
    {
        await using var host = NewHost();
        var scenario = await TicketScenario.CreateAsync(host);

        await using (var scope = host.CreateScope())
        {
            await using var unitOfWork = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().BeginAsync(Ct);
            await StageNewTicketAsync(scope.ServiceProvider, scenario, "Rolled back");

            // SaveChanges runs (the events are captured) but the transaction is never committed: disposing the scope rolls it back.
            await scope.ServiceProvider.GetRequiredService<TechStrapDbContext>().SaveChangesAsync(Ct);
        }

        _recorder.Attempts.ShouldBeEmpty();

        var committed = await scenario.CreateTicketAsync("Committed");
        _recorder.Attempts.ShouldHaveSingleItem().TicketId.ShouldBe(committed.Id);
    }

    [Fact]
    public async Task A_rollback_followed_by_a_commit_in_the_same_scope_publishes_only_the_commit()
    {
        await using var host = NewHost();
        var scenario = await TicketScenario.CreateAsync(host);
        await using var scope = host.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        await using (var first = await unitOfWork.BeginAsync(Ct))
        {
            await StageNewTicketAsync(scope.ServiceProvider, scenario, "Dropped");
            await scope.ServiceProvider.GetRequiredService<TechStrapDbContext>().SaveChangesAsync(Ct);
        }

        scope.ServiceProvider.GetRequiredService<TechStrapDbContext>().ChangeTracker.Clear();
        await using var second = await unitOfWork.BeginAsync(Ct);
        await StageNewTicketAsync(scope.ServiceProvider, scenario, "Kept");
        (await second.CommitAsync(Ct)).IsSuccess.ShouldBeTrue();

        _recorder.Attempts.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_concurrency_conflict_publishes_nothing_for_the_loser()
    {
        await using var host = NewHost();
        var scenario = await TicketScenario.CreateAsync(host);
        var ticket = await scenario.CreateTicketAsync();
        var before = _recorder.Attempts.Count;

        await using var winnerScope = host.CreateScope();
        await using var loserScope = host.CreateScope();
        await using var winner = await winnerScope.ServiceProvider.GetRequiredService<IUnitOfWork>().BeginAsync(Ct);
        await using var loser = await loserScope.ServiceProvider.GetRequiredService<IUnitOfWork>().BeginAsync(Ct);
        var winnersTicket = (await winnerScope.ServiceProvider.GetRequiredService<ITicketRepository>().GetByIdAsync(ticket.Id, Ct))!;
        var losersTicket = (await loserScope.ServiceProvider.GetRequiredService<ITicketRepository>().GetByIdAsync(ticket.Id, Ct))!;
        winnersTicket.ChangeStatus(TicketStatus.Open, scenario.AgentActor, host.Clock).IsSuccess.ShouldBeTrue();
        losersTicket.ChangePriority(TicketPriority.Urgent, scenario.AgentActor, host.Clock).IsSuccess.ShouldBeTrue();
        winnerScope.ServiceProvider.GetRequiredService<ITicketRepository>().Update(winnersTicket);
        loserScope.ServiceProvider.GetRequiredService<ITicketRepository>().Update(losersTicket);

        (await winner.CommitAsync(Ct)).IsSuccess.ShouldBeTrue();
        var lost = await loser.CommitAsync(Ct);

        lost.IsFailure.ShouldBeTrue();
        lost.Errors[0].Code.ShouldBe(PersistenceErrorCodes.ConcurrencyConflict);
        var published = _recorder.Attempts.Skip(before).ToList();
        published.ShouldHaveSingleItem().EventType.ShouldBe(TicketEventTypes.StatusChanged);
    }

    [Fact]
    public async Task A_unique_violation_publishes_nothing()
    {
        await using var host = NewHost();
        var scenario = await TicketScenario.CreateAsync(host);
        var first = await host.CommitAsync(services =>
        {
            var ticket = Ticket.Create(TicketNumber.Create("ACME", 900).Value, scenario.Acme.Id, scenario.Requester.Id, "One", TicketChannel.Web, null, false, host.Clock).Value;
            services.GetRequiredService<ITicketRepository>().Add(ticket);
            return Task.CompletedTask;
        });
        first.IsSuccess.ShouldBeTrue();
        _recorder.Attempts.Count.ShouldBe(1);

        var second = await host.CommitAsync(services =>
        {
            var ticket = Ticket.Create(TicketNumber.Create("ACME", 900).Value, scenario.Acme.Id, scenario.Requester.Id, "Two", TicketChannel.Web, null, false, host.Clock).Value;
            services.GetRequiredService<ITicketRepository>().Add(ticket);
            return Task.CompletedTask;
        });

        second.IsFailure.ShouldBeTrue();
        second.Errors[0].Code.ShouldBe(PersistenceErrorCodes.Duplicate);
        _recorder.Attempts.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_broadcaster_that_throws_never_fails_the_commit_and_does_not_poison_the_next_one()
    {
        await using var host = NewHost();
        var scenario = await TicketScenario.CreateAsync(host);
        _recorder.Behaviour = (_, _) => throw new InvalidOperationException("the hub is down");

        var ticket = await scenario.CreateTicketAsync("Survives");

        _recorder.Attempts.ShouldHaveSingleItem();
        (await scenario.LoadAsync(ticket.Id)).ShouldNotBeNull();

        _recorder.Behaviour = null;
        var next = await scenario.CreateTicketAsync("Next");
        _recorder.Attempts.Count.ShouldBe(2);
        _recorder.Attempts[1].TicketId.ShouldBe(next.Id);
    }

    [Fact]
    public async Task One_ticket_failing_to_publish_does_not_stop_the_other_ticket_in_the_same_commit()
    {
        await using var host = NewHost();
        var scenario = await TicketScenario.CreateAsync(host);
        var calls = 0;
        _recorder.Behaviour = (_, _) => Interlocked.Increment(ref calls) == 1 ? throw new InvalidOperationException("first fails") : Task.CompletedTask;

        var result = await host.CommitAsync(async services =>
        {
            await StageNewTicketAsync(services, scenario, "First");
            await StageNewTicketAsync(services, scenario, "Second");
        });

        result.IsSuccess.ShouldBeTrue();
        _recorder.Attempts.Count.ShouldBe(2);
    }

    [Fact(Timeout = 60000)]
    public async Task A_broadcaster_that_hangs_is_cut_off_and_the_commit_still_succeeds()
    {
        await using var host = NewHost();
        var scenario = await TicketScenario.CreateAsync(host);
        var cancelled = false;
        _recorder.Behaviour = async (_, token) =>
        {
            // Waits for the publisher's own time limit (never for the test's token, which only guards the test itself).
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, TestContext.Current.CancellationToken);
            try
            {
                await Task.Delay(Timeout.Infinite, linked.Token);
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
                throw;
            }
        };

        var ticket = await scenario.CreateTicketAsync("Slow hub");

        cancelled.ShouldBeTrue();
        (await scenario.LoadAsync(ticket.Id)).ShouldNotBeNull();
    }

    [Fact]
    public async Task A_save_with_no_explicit_transaction_publishes_once_after_it_completes()
    {
        await using var host = NewHost();
        var scenario = await TicketScenario.CreateAsync(host);
        var ticket = await scenario.CreateTicketAsync();
        var before = _recorder.Attempts.Count;

        await using var scope = host.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TechStrapDbContext>();
        (await scope.ServiceProvider.GetRequiredService<ITicketRepository>().GetByIdAsync(ticket.Id, Ct)).ShouldNotBeNull();
        context.Set<TicketEventRecord>().Add(new TicketEventRecord
        {
            Id = Guid.CreateVersion7(),
            TicketId = ticket.Id,
            Type = TicketEventType.Assigned,
            ActorType = ActorType.System,
            Payload = "{}",
            OccurredAt = host.Clock.GetUtcNow(),
        });

        // One INSERT: EF opens no transaction for a single statement, so there is no commit to wait for.
        context.Database.CurrentTransaction.ShouldBeNull();
        await context.SaveChangesAsync(Ct);

        var published = _recorder.Attempts.Skip(before).ToList();
        published.ShouldHaveSingleItem().EventType.ShouldBe(TicketEventTypes.Assigned);
    }

    [Fact]
    public async Task A_save_that_uses_an_implicit_transaction_publishes_once_when_that_transaction_commits()
    {
        await using var host = NewHost();
        var scenario = await TicketScenario.CreateAsync(host);

        await using (var scope = host.CreateScope())
        {
            // A ticket, a message and two events are several statements, so EF wraps them in a transaction of its own.
            var ticket = Ticket.Create(TicketNumber.Create("ACME", 800).Value, scenario.Acme.Id, scenario.Requester.Id, "Implicit", TicketChannel.Web, null, false, host.Clock).Value;
            ticket.AddCustomerReply(scenario.Requester.Id, "<p>Help</p>", host.Clock).IsSuccess.ShouldBeTrue();
            scope.ServiceProvider.GetRequiredService<ITicketRepository>().Add(ticket);
            await scope.ServiceProvider.GetRequiredService<TechStrapDbContext>().SaveChangesAsync(Ct);
        }

        _recorder.Attempts.ShouldHaveSingleItem().Kind.ShouldBe(TicketChangeKinds.Created);
    }

    [Fact]
    public async Task A_failed_save_without_a_transaction_drops_what_it_captured()
    {
        await using var host = NewHost();
        var scenario = await TicketScenario.CreateAsync(host);
        var first = await scenario.CreateTicketAsync("First");
        var other = await scenario.CreateTicketAsync("Other");
        var before = _recorder.Attempts.Count;
        var eventId = Guid.CreateVersion7();

        TicketEventRecord Event(Guid id, Guid ticketId) => new()
        {
            Id = id,
            TicketId = ticketId,
            Type = TicketEventType.Assigned,
            ActorType = ActorType.System,
            Payload = "{}",
            OccurredAt = host.Clock.GetUtcNow(),
        };

        await using (var scope = host.CreateScope())
        {
            (await scope.ServiceProvider.GetRequiredService<ITicketRepository>().GetByIdAsync(first.Id, Ct)).ShouldNotBeNull();
            scope.ServiceProvider.GetRequiredService<TechStrapDbContext>().Set<TicketEventRecord>().Add(Event(eventId, first.Id));
            await scope.ServiceProvider.GetRequiredService<TechStrapDbContext>().SaveChangesAsync(Ct);
        }

        _recorder.Attempts.Count.ShouldBe(before + 1);

        // A second context inserts an event for the other ticket under the same primary key: the INSERT fails, with no transaction around it to roll back.
        await using var second = host.CreateScope();
        var context = second.ServiceProvider.GetRequiredService<TechStrapDbContext>();
        var tickets = second.ServiceProvider.GetRequiredService<ITicketRepository>();
        (await tickets.GetByIdAsync(first.Id, Ct)).ShouldNotBeNull();
        (await tickets.GetByIdAsync(other.Id, Ct)).ShouldNotBeNull();
        context.Set<TicketEventRecord>().Add(Event(eventId, other.Id));
        await Should.ThrowAsync<DbUpdateException>(() => context.SaveChangesAsync(Ct));
        _recorder.Attempts.Count.ShouldBe(before + 1);

        // What the failed save captured (the other ticket) must not ride along with the next good save (the first ticket).
        context.ChangeTracker.Entries<TicketEventRecord>().Where(entry => entry.State == EntityState.Added).ToList().ForEach(entry => entry.State = EntityState.Detached);
        context.Set<TicketEventRecord>().Add(Event(Guid.CreateVersion7(), first.Id));
        await context.SaveChangesAsync(Ct);

        var published = _recorder.Attempts.Skip(before + 1).ToList();
        published.ShouldHaveSingleItem().TicketId.ShouldBe(first.Id);
    }

    [Fact]
    public async Task Events_that_were_only_read_are_not_announced_again()
    {
        await using var host = NewHost();
        var scenario = await TicketScenario.CreateAsync(host);
        var ticket = await scenario.CreateTicketAsync();
        var before = _recorder.Attempts.Count;

        var result = await host.CommitAsync(async services =>
        {
            // The ticket's old events are tracked, unchanged, in the same context that then saves a new one.
            await services.GetRequiredService<TechStrapDbContext>().Set<TicketEventRecord>().Where(item => item.TicketId == ticket.Id).ToListAsync(Ct);
            var repository = services.GetRequiredService<ITicketRepository>();
            var loaded = (await repository.GetByIdAsync(ticket.Id, Ct))!;
            loaded.ChangeStatus(TicketStatus.Open, scenario.AgentActor, host.Clock).IsSuccess.ShouldBeTrue();
            repository.Update(loaded);
        });

        result.IsSuccess.ShouldBeTrue();
        var change = _recorder.Attempts.Skip(before).ShouldHaveSingleItem();
        change.Kind.ShouldBe(TicketChangeKinds.Updated);
        change.EventType.ShouldBe(TicketEventTypes.StatusChanged);
    }

    [Fact]
    public async Task Without_a_registration_of_its_own_the_broadcaster_is_the_null_one()
    {
        await using var host = new PersistenceTestHost(Database);
        await using var scope = host.CreateScope();

        scope.ServiceProvider.GetRequiredService<ITicketChangeBroadcaster>().GetType().Name.ShouldBe("NullTicketChangeBroadcaster");
        var scenario = await TicketScenario.CreateAsync(host);
        (await scenario.CreateTicketAsync()).ShouldNotBeNull();
    }

    [Fact]
    public async Task Deleting_a_ticket_publishes_a_resync_because_no_event_describes_it()
    {
        await using var host = NewHost();
        var scenario = await TicketScenario.CreateAsync(host);
        var ticket = await scenario.CreateTicketAsync();
        var before = _recorder.Attempts.Count;

        var result = await host.CommitAsync(async services =>
        {
            var repository = services.GetRequiredService<ITicketRepository>();
            repository.Remove((await repository.GetByIdAsync(ticket.Id, Ct))!);
        });

        result.IsSuccess.ShouldBeTrue();
        var change = _recorder.Attempts.Skip(before).ShouldHaveSingleItem();
        change.Kind.ShouldBe(TicketChangeKinds.Resync);
        change.TicketId.ShouldBe(Guid.Empty);
    }

    [Fact]
    public async Task The_append_only_guard_still_refuses_a_modified_event_and_nothing_is_published()
    {
        await using var host = NewHost();
        var scenario = await TicketScenario.CreateAsync(host);
        var ticket = await scenario.CreateTicketAsync();
        var before = _recorder.Attempts.Count;

        await using var scope = host.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TechStrapDbContext>();
        var record = await context.Set<TicketEventRecord>().FirstAsync(item => item.TicketId == ticket.Id, Ct);
        record.Payload = "{\"edited\":true}";

        await Should.ThrowAsync<InvalidOperationException>(() => context.SaveChangesAsync(Ct));

        _recorder.Attempts.Count.ShouldBe(before);
    }

    private static TicketEventRecord AssignedEvent(Guid ticketId, DateTimeOffset occurredAt, string payload = "{}") => new()
    {
        Id = Guid.CreateVersion7(),
        TicketId = ticketId,
        Type = TicketEventType.Assigned,
        ActorType = ActorType.System,
        Payload = payload,
        OccurredAt = occurredAt,
    };

    [Fact(Timeout = 120000)]
    public async Task A_commit_that_fails_in_the_database_publishes_nothing_and_its_staged_changes_do_not_ride_along_with_the_next_commit()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var host = NewHost();
        var scenario = await TicketScenario.CreateAsync(host);
        var first = await scenario.CreateTicketAsync("First");
        var other = await scenario.CreateTicketAsync("Other");
        await using (var connection = new NpgsqlConnection(Database.ConnectionString))
        {
            await connection.OpenAsync(ct);
            await using var command = new NpgsqlCommand("ALTER TABLE ticket_events ADD CONSTRAINT uq_test_deferred UNIQUE (ticket_id, payload) DEFERRABLE INITIALLY DEFERRED", connection);
            await command.ExecuteNonQueryAsync(ct);
        }

        var before = _recorder.Attempts.Count;
        await using var scope = host.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TechStrapDbContext>();
        var tickets = scope.ServiceProvider.GetRequiredService<ITicketRepository>();
        (await tickets.GetByIdAsync(first.Id, ct)).ShouldNotBeNull();
        (await tickets.GetByIdAsync(other.Id, ct)).ShouldNotBeNull();

        // The deferred constraint is checked by Postgres at COMMIT, so SaveChanges succeeds (and stages the change) and the commit itself fails.
        await using (var failing = await context.Database.BeginTransactionAsync(ct))
        {
            context.Set<TicketEventRecord>().Add(AssignedEvent(first.Id, host.Clock.GetUtcNow(), "{\"d\":1}"));
            context.Set<TicketEventRecord>().Add(AssignedEvent(first.Id, host.Clock.GetUtcNow(), "{\"d\":1}"));
            await context.SaveChangesAsync(ct);
            await Should.ThrowAsync<PostgresException>(() => failing.CommitAsync(ct));
        }

        _recorder.Attempts.Count.ShouldBe(before);

        // The same context commits something else: only that change is published, not the one the failed commit had staged.
        await using (var good = await context.Database.BeginTransactionAsync(ct))
        {
            context.Set<TicketEventRecord>().Add(AssignedEvent(other.Id, host.Clock.GetUtcNow()));
            await context.SaveChangesAsync(ct);
            await good.CommitAsync(ct);
        }

        _recorder.Attempts.Skip(before).ShouldHaveSingleItem().TicketId.ShouldBe(other.Id);
    }

    [Fact(Timeout = 120000)]
    public async Task Two_saves_and_one_commit_publish_one_change_for_the_ticket_named_by_the_newest_event()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var host = NewHost();
        var scenario = await TicketScenario.CreateAsync(host);
        var ticket = await scenario.CreateTicketAsync();
        var before = _recorder.Attempts.Count;
        var older = AssignedEvent(ticket.Id, host.Clock.GetUtcNow());
        var newer = AssignedEvent(ticket.Id, host.Clock.GetUtcNow() + TimeSpan.FromSeconds(5));

        await using var scope = host.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TechStrapDbContext>();
        (await scope.ServiceProvider.GetRequiredService<ITicketRepository>().GetByIdAsync(ticket.Id, ct)).ShouldNotBeNull();
        await using var unitOfWork = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().BeginAsync(ct);
        context.Set<TicketEventRecord>().Add(older);
        await context.SaveChangesAsync(ct);
        context.Set<TicketEventRecord>().Add(newer);
        await context.SaveChangesAsync(ct);
        _recorder.Attempts.Count.ShouldBe(before);

        (await unitOfWork.CommitAsync(ct)).IsSuccess.ShouldBeTrue();

        var change = _recorder.Attempts.Skip(before).ShouldHaveSingleItem();
        change.TicketId.ShouldBe(ticket.Id);
        change.EventId.ShouldBe(newer.Id);
    }

    [Fact(Timeout = 120000)]
    public async Task The_capture_relies_on_the_append_only_guard_running_first_so_a_refused_save_stages_nothing()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var host = NewHost();
        var scenario = await TicketScenario.CreateAsync(host);
        var first = await scenario.CreateTicketAsync("First");
        var other = await scenario.CreateTicketAsync("Other");
        var before = _recorder.Attempts.Count;

        await using var scope = host.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TechStrapDbContext>();
        var tickets = scope.ServiceProvider.GetRequiredService<ITicketRepository>();
        (await tickets.GetByIdAsync(first.Id, ct)).ShouldNotBeNull();
        (await tickets.GetByIdAsync(other.Id, ct)).ShouldNotBeNull();
        var existing = await context.Set<TicketEventRecord>().FirstAsync(item => item.TicketId == first.Id, ct);

        // An added event plus a modified one: the guard throws from SavingChanges, which EF runs outside the try that raises SaveChangesFailed. The capture must not have staged the
        // added event before that, because nothing would ever drop it.
        var added = AssignedEvent(first.Id, host.Clock.GetUtcNow());
        context.Set<TicketEventRecord>().Add(added);
        existing.Payload = "{\"edited\":true}";
        await Should.ThrowAsync<InvalidOperationException>(() => context.SaveChangesAsync(ct));
        context.Entry(added).State = EntityState.Detached;
        context.Entry(existing).State = EntityState.Unchanged;
        _recorder.Attempts.Count.ShouldBe(before);

        context.Set<TicketEventRecord>().Add(AssignedEvent(other.Id, host.Clock.GetUtcNow()));
        await context.SaveChangesAsync(ct);

        _recorder.Attempts.Skip(before).ShouldHaveSingleItem().TicketId.ShouldBe(other.Id);
    }

    [Fact]
    public async Task Configure_keeps_its_two_argument_shape_and_adds_no_hook_for_callers_that_use_it_directly()
    {
        typeof(TechStrapDatabase).GetMethod(nameof(TechStrapDatabase.Configure))!.GetParameters().Length.ShouldBe(2);
        await using var host = NewHost();
        var scenario = await TicketScenario.CreateAsync(host);
        var ticket = await scenario.CreateTicketAsync();
        var before = _recorder.Attempts.Count;

        // A context built by the static Configure (the way the tools and most tests build one) has the guard but no hook, and still works.
        await using var plain = Database.CreateDbContext();
        plain.Set<TicketEventRecord>().Add(new TicketEventRecord
        {
            Id = Guid.CreateVersion7(),
            TicketId = ticket.Id,
            Type = TicketEventType.Assigned,
            ActorType = ActorType.System,
            Payload = "{}",
            OccurredAt = host.Clock.GetUtcNow(),
        });
        await plain.SaveChangesAsync(Ct);

        _recorder.Attempts.Count.ShouldBe(before);
    }
}
