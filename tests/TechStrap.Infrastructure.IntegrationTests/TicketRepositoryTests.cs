using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SyntaxCircus.Common;
using TechStrap.Application.Persistence;
using TechStrap.Domain.Tickets;

namespace TechStrap.Infrastructure.IntegrationTests;

public sealed class TicketRepositoryTests(PostgresFixture postgres) : PostgresIntegrationTestBase(postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private async Task<long> CountAsync(string table)
    {
        await using var connection = new NpgsqlConnection(Database.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT count(*) FROM {table}";
        return (long)(await command.ExecuteScalarAsync(Ct))!;
    }

    private static Task<IReadOnlyList<TicketEvent>> EventsAsync(TicketScenario scenario, Guid ticketId) =>
        scenario.Host.ReadAsync(sp => sp.GetRequiredService<ITicketRepository>().GetEventsAsync(ticketId, Ct));

    [Fact]
    public async Task A_created_ticket_is_stored_with_its_first_message_and_Created_and_MessageAdded_events_in_one_commit()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);

        var ticket = await scenario.CreateTicketAsync();

        var loaded = (await scenario.LoadAsync(ticket.Id))!;
        loaded.Number.ToString().ShouldBe("ACME-1");
        loaded.Subject.ShouldBe("Cannot sign in");
        loaded.Status.ShouldBe(TicketStatus.New);
        loaded.RequesterId.ShouldBe(scenario.Requester.Id);
        loaded.CreatedAt.ShouldBe(ticket.CreatedAt);
        loaded.PendingEvents.ShouldBeEmpty();
        var messages = await host.ReadAsync(sp => sp.GetRequiredService<ITicketRepository>().GetMessagesAsync(ticket.Id, false, Ct));
        messages.ShouldHaveSingleItem().Body.ShouldBe("<p>I cannot sign in</p>");
        (await EventsAsync(scenario, ticket.Id)).Select(e => e.Type).ShouldBe([TicketEventType.Created, TicketEventType.MessageAdded]);
        ticket.PendingEvents.ShouldBeEmpty();
        ticket.PendingMessages.ShouldBeEmpty();
    }

    [Fact]
    public async Task Every_kind_of_mutation_writes_its_event_in_the_same_commit_as_the_change()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        var ticket = await scenario.CreateTicketAsync();
        var tagId = Guid.NewGuid();
        await host.CommitAsync(sp =>
        {
            sp.GetRequiredService<ITagRepository>().Add(Tag.Restore(tagId, "bug", "Bug", "#FF0000"));
            return Task.CompletedTask;
        });

        var result = await scenario.UpdateAsync(ticket.Id, t =>
        {
            t.AddAgentReply(scenario.Agent.Id, "<p>Looking</p>", host.Clock);
            t.AddInternalNote(scenario.Agent.Id, "check logs", host.Clock);
            t.Assign(scenario.Agent.Id, scenario.AgentActor, host.Clock);
            t.ChangePriority(TicketPriority.High, scenario.AgentActor, host.Clock);
            t.AddTag(tagId, scenario.AgentActor, host.Clock);
            t.MarkSpam(true, scenario.AgentActor, host.Clock);
            t.MarkSpam(false, scenario.AgentActor, host.Clock);
            t.ChangeStatus(TicketStatus.Solved, scenario.AgentActor, host.Clock);
        });

        result.IsSuccess.ShouldBeTrue();
        var loaded = (await scenario.LoadAsync(ticket.Id))!;
        loaded.Status.ShouldBe(TicketStatus.Solved);
        loaded.AssigneeId.ShouldBe(scenario.Agent.Id);
        loaded.Priority.ShouldBe(TicketPriority.High);
        loaded.TagIds.ShouldBe([tagId]);
        loaded.FirstResponseAt.ShouldNotBeNull();
        loaded.SolvedAt.ShouldNotBeNull();
        (await EventsAsync(scenario, ticket.Id)).Select(e => e.Type).ShouldBe(
        [
            TicketEventType.Created,
            TicketEventType.MessageAdded,
            TicketEventType.MessageAdded,
            TicketEventType.StatusChanged,
            TicketEventType.MessageAdded,
            TicketEventType.Assigned,
            TicketEventType.PriorityChanged,
            TicketEventType.TagAdded,
            TicketEventType.MarkedSpam,
            TicketEventType.MarkedSpam,
            TicketEventType.StatusChanged,
        ]);
        var publicMessages = await host.ReadAsync(sp => sp.GetRequiredService<ITicketRepository>().GetMessagesAsync(ticket.Id, true, Ct));
        publicMessages.Count.ShouldBe(2);
        (await host.ReadAsync(sp => sp.GetRequiredService<ITicketRepository>().GetMessagesAsync(ticket.Id, false, Ct))).Count.ShouldBe(3);
    }

    [Fact]
    public async Task Removing_a_tag_and_updating_twice_neither_loses_nor_duplicates_events()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        var tagId = Guid.NewGuid();
        await host.CommitAsync(sp => { sp.GetRequiredService<ITagRepository>().Add(Tag.Restore(tagId, "bug", "Bug", "#FF0000")); return Task.CompletedTask; });
        var ticket = await scenario.CreateTicketAsync(change: t => t.AddTag(tagId, scenario.AgentActor, host.Clock));

        await host.CommitAsync(async sp =>
        {
            var repository = sp.GetRequiredService<ITicketRepository>();
            var loaded = (await repository.GetByIdAsync(ticket.Id, Ct))!;
            loaded.RemoveTag(tagId, scenario.AgentActor, host.Clock);
            repository.Update(loaded);
            repository.Update(loaded);
        });

        (await scenario.LoadAsync(ticket.Id))!.TagIds.ShouldBeEmpty();
        (await EventsAsync(scenario, ticket.Id)).Count(e => e.Type == TicketEventType.TagRemoved).ShouldBe(1);
    }

    [Fact]
    public async Task A_failed_commit_leaves_no_ticket_message_or_event_from_that_unit_of_work()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        await scenario.CreateTicketAsync();
        var ticketsBefore = await CountAsync("tickets");
        var messagesBefore = await CountAsync("messages");
        var eventsBefore = await CountAsync("ticket_events");

        // A ticket that reuses ACME-1: the unique number index rejects the insert, after the message and events were staged too.
        var result = await host.CommitAsync(sp =>
        {
            var duplicateNumber = TicketNumber.Create("ACME", 1).Value;
            var ticket = Ticket.Create(duplicateNumber, scenario.Acme.Id, scenario.Requester.Id, "Duplicate", TicketChannel.Web, null, false, host.Clock).Value;
            ticket.AddCustomerReply(scenario.Requester.Id, "body", host.Clock);
            sp.GetRequiredService<ITicketRepository>().Add(ticket);
            return Task.CompletedTask;
        });

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe(PersistenceErrorCodes.Duplicate);
        (await CountAsync("tickets")).ShouldBe(ticketsBefore);
        (await CountAsync("messages")).ShouldBe(messagesBefore);
        (await CountAsync("ticket_events")).ShouldBe(eventsBefore);
    }

    [Fact]
    public async Task Two_stale_ticket_updates_give_one_success_and_one_conflict_and_the_loser_writes_no_events()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        var ticket = await scenario.CreateTicketAsync();
        var eventsBefore = await CountAsync("ticket_events");

        await using var first = host.CreateScope();
        await using var second = host.CreateScope();
        var firstRepository = first.ServiceProvider.GetRequiredService<ITicketRepository>();
        var secondRepository = second.ServiceProvider.GetRequiredService<ITicketRepository>();
        var loadedByFirst = (await firstRepository.GetByIdAsync(ticket.Id, Ct))!;
        var loadedBySecond = (await secondRepository.GetByIdAsync(ticket.Id, Ct))!;
        loadedByFirst.ChangePriority(TicketPriority.Urgent, scenario.AgentActor, host.Clock);
        loadedBySecond.ChangePriority(TicketPriority.Low, scenario.AgentActor, host.Clock);

        await using var firstWork = await first.ServiceProvider.GetRequiredService<IUnitOfWork>().BeginAsync(Ct);
        firstRepository.Update(loadedByFirst);
        var firstResult = await firstWork.CommitAsync(Ct);
        await using var secondWork = await second.ServiceProvider.GetRequiredService<IUnitOfWork>().BeginAsync(Ct);
        secondRepository.Update(loadedBySecond);
        var secondResult = await secondWork.CommitAsync(Ct);

        firstResult.IsSuccess.ShouldBeTrue();
        secondResult.Errors.ShouldHaveSingleItem().Code.ShouldBe(PersistenceErrorCodes.ConcurrencyConflict);
        secondResult.Errors[0].Kind.ShouldBe(ResultErrorKind.Conflict);
        (await scenario.LoadAsync(ticket.Id))!.Priority.ShouldBe(TicketPriority.Urgent);
        (await CountAsync("ticket_events")).ShouldBe(eventsBefore + 1);
    }

    [Fact]
    public async Task Moving_a_ticket_to_another_product_keeps_its_original_number_and_prefix()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        var ticket = await scenario.CreateTicketAsync();

        await scenario.UpdateAsync(ticket.Id, t => t.MoveToProduct(scenario.Orbitly.Id, scenario.AgentActor, host.Clock));

        var moved = (await host.ReadAsync(sp => sp.GetRequiredService<ITicketRepository>().GetByNumberAsync("acme-1", Ct)))!;
        moved.ProductId.ShouldBe(scenario.Orbitly.Id);
        moved.Number.ToString().ShouldBe("ACME-1");
        (await scenario.CreateTicketAsync(product: scenario.Orbitly)).Number.ToString().ShouldBe("ORB-1");
        (await EventsAsync(scenario, ticket.Id)).ShouldContain(e => e.Type == TicketEventType.ProductChanged);
    }

    [Fact]
    public async Task A_ticket_is_found_by_number_in_any_case_and_an_unknown_or_malformed_number_gives_null()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        var ticket = await scenario.CreateTicketAsync();
        var repository = (Func<string, Task<Ticket?>>)(number => host.ReadAsync(sp => sp.GetRequiredService<ITicketRepository>().GetByNumberAsync(number, Ct)));

        (await repository("ACME-1"))!.Id.ShouldBe(ticket.Id);
        (await repository(" acme-1 "))!.Id.ShouldBe(ticket.Id);
        (await repository("ACME-99")).ShouldBeNull();
        (await repository("nonsense")).ShouldBeNull();
    }

    [Fact]
    public async Task A_follow_up_of_a_closed_ticket_is_stored_linked_to_its_parent_and_the_parent_gains_only_an_event()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        var parent = await scenario.CreateTicketAsync();
        await scenario.UpdateAsync(parent.Id, t =>
        {
            t.ChangeStatus(TicketStatus.Solved, scenario.AgentActor, host.Clock);
            t.ChangeStatus(TicketStatus.Closed, Actor.System, host.Clock);
        });
        var closed = (await scenario.LoadAsync(parent.Id))!;
        var closedAt = closed.ClosedAt;
        var lastActivity = closed.LastActivityAt;
        host.Clock.Advance(TimeSpan.FromMinutes(5));
        Ticket? followUp = null;

        var result = await host.CommitAsync(async sp =>
        {
            var tickets = sp.GetRequiredService<ITicketRepository>();
            var loadedParent = (await tickets.GetByIdAsync(parent.Id, Ct))!;
            var number = (await sp.GetRequiredService<ITicketNumberAllocator>().AllocateAsync(loadedParent.ProductId, Ct)).Value;
            followUp = loadedParent.CreateFollowUp(number, host.Clock).Value;
            followUp.AddCustomerReply(scenario.Requester.Id, "<p>It is back</p>", host.Clock);
            tickets.Update(loadedParent);
            tickets.Add(followUp);
        });

        result.IsSuccess.ShouldBeTrue();
        var loadedFollowUp = (await scenario.LoadAsync(followUp!.Id))!;
        loadedFollowUp.ParentTicketId.ShouldBe(parent.Id);
        loadedFollowUp.Number.ToString().ShouldBe("ACME-2");
        var reloadedParent = (await scenario.LoadAsync(parent.Id))!;
        reloadedParent.Status.ShouldBe(TicketStatus.Closed);
        reloadedParent.ClosedAt.ShouldBe(closedAt);
        reloadedParent.LastActivityAt.ShouldBe(lastActivity);
        (await EventsAsync(scenario, parent.Id)).Last().Type.ShouldBe(TicketEventType.FollowUpCreated);
    }

    [Fact]
    public async Task Persisted_event_payloads_never_contain_the_subject_or_the_message_text()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        var ticket = await scenario.CreateTicketAsync("Secret subject for ann@example.com");
        await scenario.UpdateAsync(ticket.Id, t => t.AddAgentReply(scenario.Agent.Id, "<p>Private reply text</p>", host.Clock));

        var payloads = string.Join(' ', (await EventsAsync(scenario, ticket.Id)).Select(e => e.PayloadJson));

        payloads.ShouldNotContain("Secret");
        payloads.ShouldNotContain("ann@example.com");
        payloads.ShouldNotContain("Private");
    }

    [Fact]
    public async Task Attachments_are_stored_with_their_message_and_found_by_id_and_by_ticket()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        var ticket = await scenario.CreateTicketAsync();
        Guid attachmentId = Guid.Empty;

        await scenario.UpdateAsync(ticket.Id, t =>
        {
            var message = t.AddAgentReply(scenario.Agent.Id, "<p>See attached</p>", host.Clock).Value;
            attachmentId = message.AddAttachment("log.txt", "text/plain", 120, "tickets/1/log.txt", host.Clock).Value.Id;
        });

        var repository = (Func<IServiceProvider, ITicketRepository>)(sp => sp.GetRequiredService<ITicketRepository>());
        var single = await host.ReadAsync(sp => repository(sp).GetAttachmentAsync(ticket.Id, attachmentId, false, Ct));
        var all = await host.ReadAsync(sp => repository(sp).GetAttachmentsAsync(ticket.Id, false, Ct));

        single!.FileName.ShouldBe("log.txt");
        single.StorageKey.ShouldBe("tickets/1/log.txt");
        all.ShouldHaveSingleItem().Id.ShouldBe(attachmentId);
        (await host.ReadAsync(sp => repository(sp).GetAttachmentAsync(ticket.Id, Guid.NewGuid(), false, Ct))).ShouldBeNull();
    }

    [Fact]
    public async Task Access_tokens_are_found_by_hash_and_slide_or_revoke_through_an_update()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        var ticket = await scenario.CreateTicketAsync();
        var token = TicketAccessToken.Issue(ticket.Id, scenario.Requester.Id, "hash-1", host.Clock).Value;
        await host.CommitAsync(sp => { sp.GetRequiredService<ITicketRepository>().AddAccessToken(token); return Task.CompletedTask; });
        host.Clock.Advance(TimeSpan.FromDays(30));

        await host.CommitAsync(async sp =>
        {
            var repository = sp.GetRequiredService<ITicketRepository>();
            var loaded = (await repository.GetAccessTokenByHashAsync("hash-1", Ct))!;
            loaded.RecordUse(host.Clock).IsSuccess.ShouldBeTrue();
            repository.UpdateAccessToken(loaded);
        });
        var slid = (await host.ReadAsync(sp => sp.GetRequiredService<ITicketRepository>().GetAccessTokenByHashAsync("hash-1", Ct)))!;
        slid.ExpiresAt.ShouldBe(host.Clock.GetUtcNow().AddDays(90));
        slid.LastUsedAt.ShouldBe(host.Clock.GetUtcNow());

        await host.CommitAsync(async sp =>
        {
            var repository = sp.GetRequiredService<ITicketRepository>();
            var loaded = (await repository.GetAccessTokenByHashAsync("hash-1", Ct))!;
            loaded.Revoke(host.Clock);
            repository.UpdateAccessToken(loaded);
        });
        (await host.ReadAsync(sp => sp.GetRequiredService<ITicketRepository>().GetAccessTokenByHashAsync("hash-1", Ct)))!.IsValid(host.Clock).ShouldBeFalse();
        (await host.ReadAsync(sp => sp.GetRequiredService<ITicketRepository>().GetAccessTokenByHashAsync("other", Ct))).ShouldBeNull();
    }

    [Fact]
    public async Task Solved_tickets_older_than_the_cutoff_are_listed_oldest_first_and_can_be_closed_through_the_repository()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        var old = await scenario.CreateTicketAsync("old");
        await scenario.UpdateAsync(old.Id, t => t.ChangeStatus(TicketStatus.Solved, scenario.AgentActor, host.Clock));
        host.Clock.Advance(TimeSpan.FromDays(8));
        var recent = await scenario.CreateTicketAsync("recent");
        await scenario.UpdateAsync(recent.Id, t => t.ChangeStatus(TicketStatus.Solved, scenario.AgentActor, host.Clock));
        var cutoff = host.Clock.GetUtcNow().AddDays(-7);

        await host.CommitAsync(async sp =>
        {
            var repository = sp.GetRequiredService<ITicketRepository>();
            var due = await repository.ListSolvedBeforeAsync(cutoff, 10, Ct);
            due.ShouldHaveSingleItem().Id.ShouldBe(old.Id);
            due[0].ChangeStatus(TicketStatus.Closed, Actor.System, host.Clock).IsSuccess.ShouldBeTrue();
            repository.Update(due[0]);
        });

        (await scenario.LoadAsync(old.Id))!.Status.ShouldBe(TicketStatus.Closed);
        (await scenario.LoadAsync(recent.Id))!.Status.ShouldBe(TicketStatus.Solved);
        (await EventsAsync(scenario, old.Id)).Last().ActorType.ShouldBe(ActorType.System);
    }

    [Fact]
    public async Task Updating_a_ticket_that_was_not_loaded_in_the_scope_is_a_programming_error()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        var ticket = await scenario.CreateTicketAsync();
        await using var scope = host.CreateScope();

        Should.Throw<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<ITicketRepository>().Update(ticket))
            .Message.ShouldContain("was not loaded");
    }

    [Fact]
    public async Task The_database_stores_enums_as_text()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        await scenario.CreateTicketAsync();
        await using var context = Database.CreateDbContext();

        var status = await context.Database.SqlQueryRaw<string>("SELECT status AS \"Value\" FROM tickets").SingleAsync(Ct);

        status.ShouldBe("New");
    }
}
