using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SyntaxCircus.Common;
using TechStrap.Application.Persistence;
using TechStrap.Application.Tickets;
using TechStrap.Domain.Rules;
using TechStrap.Domain.Tickets;
using TechStrap.Infrastructure.Persistence;
using TechStrap.Infrastructure.Persistence.Records;

namespace TechStrap.Infrastructure.IntegrationTests;

/// <summary>
/// The review-driven guarantees of the ticket repository: cross-scope concurrency (D-026), no double-insert of pending parts,
/// follow-up races, customer visibility (D-024), paging bounds and the agent-only list projection.
/// </summary>
public sealed class TicketRepositoryGuardTests(PostgresFixture postgres) : PostgresIntegrationTestBase(postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private async Task<long> CountAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(Database.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (long)(await command.ExecuteScalarAsync(Ct))!;
    }

    private static ITicketRepository Tickets(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<ITicketRepository>();

    // ---- Item 1: cross-scope concurrency (D-026) ----

    [Fact]
    public async Task A_stale_ticket_kept_from_one_scope_and_updated_in_a_third_scope_is_a_concurrency_conflict()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        var ticket = await scenario.CreateTicketAsync();
        await using var scopeA = host.CreateScope();
        var stale = (await Tickets(scopeA).GetByIdAsync(ticket.Id, Ct))!;
        (await scenario.UpdateAsync(ticket.Id, t => t.ChangePriority(TicketPriority.High, scenario.AgentActor, host.Clock))).IsSuccess.ShouldBeTrue();
        var eventsBefore = await CountAsync("SELECT count(*) FROM ticket_events");

        await using var scopeC = host.CreateScope();
        var repository = Tickets(scopeC);
        (await repository.GetByIdAsync(ticket.Id, Ct)).ShouldNotBeNull();
        stale.ChangePriority(TicketPriority.Low, scenario.AgentActor, host.Clock);
        await using var work = await scopeC.ServiceProvider.GetRequiredService<IUnitOfWork>().BeginAsync(Ct);
        repository.Update(stale);
        var result = await work.CommitAsync(Ct);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe(PersistenceErrorCodes.ConcurrencyConflict);
        result.Errors[0].Kind.ShouldBe(ResultErrorKind.Conflict);
        (await scenario.LoadAsync(ticket.Id))!.Priority.ShouldBe(TicketPriority.High);
        (await CountAsync("SELECT count(*) FROM ticket_events")).ShouldBe(eventsBefore);
    }

    [Fact]
    public async Task A_ticket_kept_from_one_scope_and_updated_in_another_succeeds_when_nobody_changed_it_meanwhile()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        var ticket = await scenario.CreateTicketAsync();
        await using var scopeA = host.CreateScope();
        var kept = (await Tickets(scopeA).GetByIdAsync(ticket.Id, Ct))!;

        await using var scopeC = host.CreateScope();
        var repository = Tickets(scopeC);
        (await repository.GetByIdAsync(ticket.Id, Ct)).ShouldNotBeNull();
        kept.ChangePriority(TicketPriority.Urgent, scenario.AgentActor, host.Clock);
        await using var work = await scopeC.ServiceProvider.GetRequiredService<IUnitOfWork>().BeginAsync(Ct);
        repository.Update(kept);
        var result = await work.CommitAsync(Ct);

        result.IsSuccess.ShouldBeTrue();
        (await scenario.LoadAsync(ticket.Id))!.Priority.ShouldBe(TicketPriority.Urgent);
    }

    // ---- Item 2: pending changes are persisted once ----

    [Fact]
    public async Task Saving_the_same_ticket_twice_in_one_unit_of_work_inserts_its_message_attachment_and_events_once()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        var ticket = await scenario.CreateTicketAsync();
        var messagesBefore = await CountAsync("SELECT count(*) FROM messages");
        var eventsBefore = await CountAsync("SELECT count(*) FROM ticket_events");

        var result = await host.CommitAsync(async sp =>
        {
            var repository = sp.GetRequiredService<ITicketRepository>();
            var loaded = (await repository.GetByIdAsync(ticket.Id, Ct))!;
            var reply = loaded.AddAgentReply(scenario.Agent.Id, "<p>See attached</p>", host.Clock).Value;
            reply.AddAttachment("log.txt", "text/plain", 10, "tickets/1/log.txt", host.Clock).IsSuccess.ShouldBeTrue();
            repository.Update(loaded);
            repository.Update(loaded);
            loaded.PendingMessages.ShouldBeEmpty();
            loaded.PendingEvents.ShouldBeEmpty();
            reply.NewAttachments.ShouldBeEmpty();
        });

        result.IsSuccess.ShouldBeTrue();
        (await CountAsync("SELECT count(*) FROM messages")).ShouldBe(messagesBefore + 1);
        (await CountAsync("SELECT count(*) FROM attachments")).ShouldBe(1);
        (await CountAsync("SELECT count(*) FROM ticket_events")).ShouldBe(eventsBefore + 2);
    }

    [Fact]
    public async Task A_ticket_added_and_then_updated_in_the_same_unit_of_work_is_inserted_once_with_its_events()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);

        var result = await host.CommitAsync(async sp =>
        {
            var number = (await sp.GetRequiredService<ITicketNumberAllocator>().AllocateAsync(scenario.Acme.Id, Ct)).Value;
            var ticket = Ticket.Create(number, scenario.Acme.Id, scenario.Requester.Id, "Added then updated", TicketChannel.Web, null, false, host.Clock).Value;
            ticket.AddCustomerReply(scenario.Requester.Id, "first", host.Clock).IsSuccess.ShouldBeTrue();
            var repository = sp.GetRequiredService<ITicketRepository>();
            repository.Add(ticket);
            ticket.ChangePriority(TicketPriority.High, scenario.AgentActor, host.Clock).IsSuccess.ShouldBeTrue();
            repository.Update(ticket);
        });

        result.IsSuccess.ShouldBeTrue();
        (await CountAsync("SELECT count(*) FROM tickets")).ShouldBe(1);
        (await CountAsync("SELECT count(*) FROM messages")).ShouldBe(1);
        (await CountAsync("SELECT count(*) FROM ticket_events")).ShouldBe(3);
        (await CountAsync("SELECT count(*) FROM tickets WHERE priority = 'High'")).ShouldBe(1);
    }

    // ---- Item 3: follow-ups ----

    private static async Task<(TicketScenario Scenario, Ticket Parent)> ClosedParentAsync(PersistenceTestHost host)
    {
        var scenario = await TicketScenario.CreateAsync(host);
        var parent = await scenario.CreateTicketAsync();
        await scenario.UpdateAsync(parent.Id, t =>
        {
            t.ChangeStatus(TicketStatus.Solved, scenario.AgentActor, host.Clock);
            t.ChangeStatus(TicketStatus.Closed, Actor.System, host.Clock);
        });
        host.Clock.Advance(TimeSpan.FromMinutes(5));
        return (scenario, parent);
    }

    private static async Task<Result> CreateFollowUpAsync(PersistenceTestHost host, TicketScenario scenario, AsyncServiceScope scope, Ticket parent)
    {
        await using var work = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().BeginAsync(Ct);
        var number = (await scope.ServiceProvider.GetRequiredService<ITicketNumberAllocator>().AllocateAsync(parent.ProductId, Ct)).Value;
        var followUp = parent.CreateFollowUp(number, host.Clock).Value;
        followUp.AddCustomerReply(scenario.Requester.Id, "<p>It is back</p>", host.Clock).IsSuccess.ShouldBeTrue();
        var repository = Tickets(scope);
        repository.Update(parent);
        repository.Add(followUp);
        parent.PendingEvents.ShouldBeEmpty();
        followUp.PendingEvents.ShouldBeEmpty();
        return await work.CommitAsync(Ct);
    }

    [Fact]
    public async Task Two_concurrent_follow_ups_of_the_same_closed_ticket_give_one_success_and_one_conflict()
    {
        await using var host = new PersistenceTestHost(Database);
        var (scenario, parent) = await ClosedParentAsync(host);
        await using var first = host.CreateScope();
        await using var second = host.CreateScope();
        var parentInFirst = (await Tickets(first).GetByIdAsync(parent.Id, Ct))!;
        var parentInSecond = (await Tickets(second).GetByIdAsync(parent.Id, Ct))!;

        var firstResult = await CreateFollowUpAsync(host, scenario, first, parentInFirst);
        var secondResult = await CreateFollowUpAsync(host, scenario, second, parentInSecond);

        firstResult.IsSuccess.ShouldBeTrue();
        secondResult.Errors.ShouldHaveSingleItem().Code.ShouldBe(PersistenceErrorCodes.ConcurrencyConflict);
        (await CountAsync($"SELECT count(*) FROM tickets WHERE parent_ticket_id = '{parent.Id}'")).ShouldBe(1);
        (await CountAsync($"SELECT count(*) FROM ticket_events WHERE ticket_id = '{parent.Id}' AND type = 'FollowUpCreated'")).ShouldBe(1);
    }

    [Fact]
    public async Task A_follow_up_commits_both_aggregates_and_clears_their_pending_changes()
    {
        await using var host = new PersistenceTestHost(Database);
        var (scenario, parent) = await ClosedParentAsync(host);
        await using var scope = host.CreateScope();
        var loadedParent = (await Tickets(scope).GetByIdAsync(parent.Id, Ct))!;
        var lastActivity = loadedParent.LastActivityAt;

        var result = await CreateFollowUpAsync(host, scenario, scope, loadedParent);

        result.IsSuccess.ShouldBeTrue();
        (await scenario.LoadAsync(parent.Id))!.LastActivityAt.ShouldBe(lastActivity);
        (await CountAsync($"SELECT count(*) FROM tickets WHERE parent_ticket_id = '{parent.Id}'")).ShouldBe(1);
        (await CountAsync($"SELECT count(*) FROM messages m JOIN tickets t ON t.id = m.ticket_id WHERE t.parent_ticket_id = '{parent.Id}'")).ShouldBe(1);
    }

    // ---- Items 4 and 5: customer visibility (D-024) ----

    [Fact]
    public async Task Public_only_attachment_methods_hide_attachments_of_internal_notes()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        var ticket = await scenario.CreateTicketAsync();
        var publicId = Guid.Empty;
        var internalId = Guid.Empty;
        await scenario.UpdateAsync(ticket.Id, t =>
        {
            var reply = t.AddAgentReply(scenario.Agent.Id, "<p>Public</p>", host.Clock).Value;
            publicId = reply.AddAttachment("public.txt", "text/plain", 1, "k/public", host.Clock).Value.Id;
            var note = t.AddInternalNote(scenario.Agent.Id, "private", host.Clock).Value;
            internalId = note.AddAttachment("internal.txt", "text/plain", 1, "k/internal", host.Clock).Value.Id;
        });

        var publicList = await host.ReadAsync(sp => sp.GetRequiredService<ITicketRepository>().GetAttachmentsAsync(ticket.Id, true, Ct));
        var agentList = await host.ReadAsync(sp => sp.GetRequiredService<ITicketRepository>().GetAttachmentsAsync(ticket.Id, false, Ct));
        var internalPublic = await host.ReadAsync(sp => sp.GetRequiredService<ITicketRepository>().GetAttachmentAsync(ticket.Id, internalId, true, Ct));
        var internalAgent = await host.ReadAsync(sp => sp.GetRequiredService<ITicketRepository>().GetAttachmentAsync(ticket.Id, internalId, false, Ct));
        var publicSingle = await host.ReadAsync(sp => sp.GetRequiredService<ITicketRepository>().GetAttachmentAsync(ticket.Id, publicId, true, Ct));
        var otherTicket = await host.ReadAsync(sp => sp.GetRequiredService<ITicketRepository>().GetAttachmentAsync(Guid.NewGuid(), publicId, true, Ct));

        publicList.ShouldHaveSingleItem().Id.ShouldBe(publicId);
        agentList.Select(a => a.Id).Order().ShouldBe(new[] { publicId, internalId }.Order());
        internalPublic.ShouldBeNull();
        internalAgent.ShouldNotBeNull();
        publicSingle.ShouldNotBeNull();
        otherTicket.ShouldBeNull();
    }

    [Fact]
    public async Task Public_only_messages_never_include_an_internal_note()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        var ticket = await scenario.CreateTicketAsync();
        await scenario.UpdateAsync(ticket.Id, t => t.AddInternalNote(scenario.Agent.Id, "agents only", host.Clock));

        var publicMessages = await host.ReadAsync(sp => sp.GetRequiredService<ITicketRepository>().GetMessagesAsync(ticket.Id, true, Ct));

        publicMessages.ShouldAllBe(m => m.Visibility == MessageVisibility.Public);
        publicMessages.ShouldNotContain(m => m.Body == "agents only");
    }

    // ---- Item 6: paging and sort ----

    [Fact]
    public async Task An_enormous_page_size_is_clamped_to_the_maximum_and_a_huge_page_number_gives_an_empty_page()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        await TicketBulkSeed.RunAsync(scenario, Database.ConnectionString, 150, host.Clock.GetUtcNow(), true, Ct);
        var repository = (Func<TicketQuery, Task<PagedResult<TicketSummary>>>)(query => host.ReadAsync(sp => sp.GetRequiredService<ITicketRepository>().ListAsync(query, Ct)));

        var clamped = await repository(new TicketQuery(TicketView.All, PageSize: int.MaxValue));
        var negativePage = await repository(new TicketQuery(TicketView.All, Page: -5, PageSize: 0));
        var hugePage = await repository(new TicketQuery(TicketView.All, Page: int.MaxValue, PageSize: int.MaxValue));
        var searchClamped = await repository(new TicketQuery(TicketView.All, SearchText: "bulk", PageSize: int.MaxValue));

        clamped.Items.Count.ShouldBe(Paging.MaxPageSize);
        clamped.PageSize.ShouldBe(Paging.MaxPageSize);
        clamped.TotalCount.ShouldBe(147);
        negativePage.Page.ShouldBe(1);
        negativePage.PageSize.ShouldBe(Paging.DefaultPageSize);
        negativePage.Items.Count.ShouldBe(Paging.DefaultPageSize);
        hugePage.Items.ShouldBeEmpty();
        hugePage.Page.ShouldBe(Paging.MaxPage);
        searchClamped.Items.Count.ShouldBe(Paging.MaxPageSize);
        searchClamped.PageSize.ShouldBe(Paging.MaxPageSize);
    }

    [Fact]
    public async Task Search_text_cut_through_an_emoji_does_not_throw()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        await scenario.CreateTicketAsync();
        var text = "printer" + new string(' ', DomainLimits.SearchTextMaxLength - 8) + "\U0001F600";

        var result = await host.ReadAsync(sp => sp.GetRequiredService<ITicketRepository>().ListAsync(new TicketQuery(TicketView.All, SearchText: text), Ct));

        result.ShouldNotBeNull();
    }

    [Fact]
    public async Task Overlong_search_text_is_truncated_to_the_search_limit()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        var ticket = await scenario.CreateTicketAsync();
        var text = "sign" + new string(' ', DomainLimits.SearchTextMaxLength + 100) + "zzzzqqq";

        var result = await host.ReadAsync(sp => sp.GetRequiredService<ITicketRepository>().ListAsync(new TicketQuery(TicketView.All, SearchText: text), Ct));

        result.Items.ShouldHaveSingleItem().Id.ShouldBe(ticket.Id);
    }

    [Fact]
    public async Task Tickets_with_identical_activity_times_page_without_gaps_or_repeats()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        await TicketBulkSeed.RunAsync(scenario, Database.ConnectionString, 58 + 2, host.Clock.GetUtcNow(), false, Ct);
        var seen = new List<Guid>();

        for (var page = 1; page <= 6; page++)
        {
            var result = await host.ReadAsync(sp => sp.GetRequiredService<ITicketRepository>().ListAsync(new TicketQuery(TicketView.All, Page: page, PageSize: 10), Ct));
            seen.AddRange(result.Items.Select(i => i.Id));
        }

        seen.Count.ShouldBe(58);
        seen.Distinct().Count().ShouldBe(58);
        var everything = await host.ReadAsync(sp => sp.GetRequiredService<ITicketRepository>().ListAsync(new TicketQuery(TicketView.All, PageSize: 100), Ct));
        seen.ShouldBe(everything.Items.Select(i => i.Id).ToList());
    }

    [Fact]
    public async Task The_auto_close_batch_limit_is_normalized_through_the_batch_size_rules()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        var solvedAt = host.Clock.GetUtcNow().AddDays(-30);
        await TicketBulkSeed.RunAsync(scenario, Database.ConnectionString, 12_000, solvedAt, false, Ct);
        var cutoff = host.Clock.GetUtcNow();
        var repository = (Func<int, Task<IReadOnlyList<Ticket>>>)(limit => host.ReadAsync(sp => sp.GetRequiredService<ITicketRepository>().ListSolvedBeforeAsync(cutoff, limit, Ct)));

        (await repository(int.MaxValue)).Count.ShouldBe(Paging.MaxBatchSize);
        (await repository(0)).Count.ShouldBe(Paging.DefaultBatchSize);
        (await repository(-3)).Count.ShouldBe(Paging.DefaultBatchSize);
        (await repository(7)).Count.ShouldBe(7);
    }

    // ---- Item 8: hard delete ----

    [Fact]
    public async Task Hard_deleting_a_ticket_that_has_follow_ups_is_a_reference_violation_conflict_not_an_exception()
    {
        await using var host = new PersistenceTestHost(Database);
        var (scenario, parent) = await ClosedParentAsync(host);
        await using (var scope = host.CreateScope())
        {
            var loaded = (await Tickets(scope).GetByIdAsync(parent.Id, Ct))!;
            (await CreateFollowUpAsync(host, scenario, scope, loaded)).IsSuccess.ShouldBeTrue();
        }

        // ITicketRepository has no delete, so this goes through the context: the unit of work still has to translate the FK failure.
        await using var deleteScope = host.CreateScope();
        await using var work = await deleteScope.ServiceProvider.GetRequiredService<IUnitOfWork>().BeginAsync(Ct);
        var context = deleteScope.ServiceProvider.GetRequiredService<TechStrapDbContext>();
        var record = await context.Set<TicketRecord>().SingleAsync(t => t.Id == parent.Id, Ct);
        context.Set<TicketRecord>().Remove(record);
        var result = await work.CommitAsync(Ct);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe(PersistenceErrorCodes.ReferenceViolation);
        result.Errors[0].Kind.ShouldBe(ResultErrorKind.Conflict);
        (await scenario.LoadAsync(parent.Id)).ShouldNotBeNull();
    }

    // ---- Item 9: agent-only projection ----

    [Fact]
    public void Only_the_agent_list_returns_a_type_that_carries_the_requester_email()
    {
        var offenders = typeof(ITicketRepository).GetMethods()
            .Where(method => Unwrap(method.ReturnType).GetProperties().Any(p => p.Name == "RequesterEmail"))
            .Select(method => method.Name)
            .ToList();

        offenders.ShouldBe([nameof(ITicketRepository.ListAsync)]);

        static Type Unwrap(Type type)
        {
            while (type.IsGenericType)
            {
                type = type.GetGenericArguments()[0];
            }

            return type;
        }
    }
}
