using TechStrap.Domain.Tickets;

namespace TechStrap.Domain.Tests.Tickets;

public sealed class TicketMutationTests
{
    private readonly TicketFactory _factory = new();

    private static TicketEventType[] Types(Ticket ticket) => [.. ticket.PendingEvents.Select(e => e.Type)];

    [Fact]
    public void An_agent_reply_adds_a_public_message_sets_Pending_and_the_first_response_time()
    {
        var ticket = _factory.Saved();
        _factory.Clock.Advance(TimeSpan.FromMinutes(12));

        var message = ticket.AddAgentReply(_factory.AgentId, "<p>On it</p>", _factory.Clock).Value;

        ticket.Status.ShouldBe(TicketStatus.Pending);
        ticket.FirstResponseAt.ShouldBe(_factory.Clock.GetUtcNow());
        ticket.LastActivityAt.ShouldBe(_factory.Clock.GetUtcNow());
        ticket.PendingMessages.ShouldHaveSingleItem().ShouldBe(message);
        message.IsVisibleToCustomer.ShouldBeTrue();
        Types(ticket).ShouldBe([TicketEventType.MessageAdded, TicketEventType.StatusChanged]);
    }

    [Fact]
    public void A_second_agent_reply_does_not_move_the_first_response_time()
    {
        var ticket = _factory.Saved();
        ticket.AddAgentReply(_factory.AgentId, "one", _factory.Clock);
        var first = ticket.FirstResponseAt;
        _factory.Clock.Advance(TimeSpan.FromHours(1));

        ticket.AddAgentReply(_factory.AgentId, "two", _factory.Clock);

        ticket.FirstResponseAt.ShouldBe(first);
    }

    [Fact]
    public void An_agent_reply_on_a_Solved_ticket_keeps_it_Solved()
    {
        var ticket = _factory.InStatus(TicketStatus.Solved);

        ticket.AddAgentReply(_factory.AgentId, "ps", _factory.Clock);

        ticket.Status.ShouldBe(TicketStatus.Solved);
        Types(ticket).ShouldBe([TicketEventType.MessageAdded]);
    }

    [Fact]
    public void An_internal_note_changes_no_status_and_is_hidden_from_the_customer()
    {
        var ticket = _factory.Saved();

        var note = ticket.AddInternalNote(_factory.AgentId, "check logs", _factory.Clock).Value;

        ticket.Status.ShouldBe(TicketStatus.New);
        note.IsVisibleToCustomer.ShouldBeFalse();
        ticket.FirstResponseAt.ShouldBeNull();
        Types(ticket).ShouldBe([TicketEventType.MessageAdded]);
    }

    [Theory]
    [InlineData(TicketStatus.Pending, TicketStatus.Open, 2)]
    [InlineData(TicketStatus.Solved, TicketStatus.Open, 2)]
    [InlineData(TicketStatus.New, TicketStatus.New, 1)]
    [InlineData(TicketStatus.Open, TicketStatus.Open, 1)]
    public void A_customer_reply_reopens_Pending_and_Solved_tickets(TicketStatus start, TicketStatus expected, int events)
    {
        var ticket = _factory.InStatus(start);

        ticket.AddCustomerReply(_factory.RequesterId, "still broken", _factory.Clock).IsSuccess.ShouldBeTrue();

        ticket.Status.ShouldBe(expected);
        ticket.PendingEvents.Count.ShouldBe(events);
        ticket.PendingEvents[0].ActorType.ShouldBe(ActorType.Requester);
    }

    [Fact]
    public void Reopening_a_Solved_ticket_clears_solved_at()
    {
        var ticket = _factory.InStatus(TicketStatus.Solved);
        ticket.SolvedAt.ShouldNotBeNull();

        ticket.AddCustomerReply(_factory.RequesterId, "no it is not", _factory.Clock);

        ticket.SolvedAt.ShouldBeNull();
    }

    [Fact]
    public void Solving_sets_solved_at_and_closing_sets_closed_at()
    {
        var ticket = _factory.Saved();
        _factory.Clock.Advance(TimeSpan.FromHours(2));
        ticket.ChangeStatus(TicketStatus.Solved, _factory.Agent, _factory.Clock);
        var solvedAt = _factory.Clock.GetUtcNow();
        _factory.Clock.Advance(TimeSpan.FromDays(7));
        ticket.ChangeStatus(TicketStatus.Closed, Actor.System, _factory.Clock);

        ticket.SolvedAt.ShouldBe(solvedAt);
        ticket.ClosedAt.ShouldBe(_factory.Clock.GetUtcNow());
        ticket.PendingEvents.Select(e => e.ActorType).ShouldBe([ActorType.Agent, ActorType.System]);
    }

    [Fact]
    public void An_invalid_transition_is_a_conflict_and_changes_nothing()
    {
        var ticket = _factory.InStatus(TicketStatus.Open);

        var result = ticket.ChangeStatus(TicketStatus.New, _factory.Agent, _factory.Clock);

        result.Error!.Code.ShouldBe("invalid-status-transition");
        result.Error.Kind.ShouldBe(DomainErrorKind.Conflict);
        ticket.Status.ShouldBe(TicketStatus.Open);
        ticket.PendingEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Assigning_raises_one_event_and_assigning_the_same_agent_again_raises_none()
    {
        var ticket = _factory.Saved();

        ticket.Assign(_factory.AgentId, _factory.Agent, _factory.Clock);
        ticket.Assign(_factory.AgentId, _factory.Agent, _factory.Clock);

        ticket.AssigneeId.ShouldBe(_factory.AgentId);
        Types(ticket).ShouldBe([TicketEventType.Assigned]);
        ticket.PendingEvents[0].PayloadJson.ShouldContain(_factory.AgentId.ToString());
    }

    [Fact]
    public void Unassigning_clears_the_assignee_with_one_event()
    {
        var ticket = _factory.Saved();
        ticket.Assign(_factory.AgentId, _factory.Agent, _factory.Clock);
        ticket.AcceptChanges();

        ticket.Assign(null, _factory.Agent, _factory.Clock);

        ticket.AssigneeId.ShouldBeNull();
        Types(ticket).ShouldBe([TicketEventType.Assigned]);
    }

    [Fact]
    public void Changing_priority_raises_one_event_and_the_same_priority_raises_none()
    {
        var ticket = _factory.Saved();

        ticket.ChangePriority(TicketPriority.Urgent, _factory.Agent, _factory.Clock);
        ticket.ChangePriority(TicketPriority.Urgent, _factory.Agent, _factory.Clock);

        ticket.Priority.ShouldBe(TicketPriority.Urgent);
        Types(ticket).ShouldBe([TicketEventType.PriorityChanged]);
        ticket.PendingEvents[0].PayloadJson.ShouldContain("Urgent");
    }

    [Fact]
    public void Moving_to_another_product_keeps_the_number_and_raises_one_event()
    {
        var ticket = _factory.Saved();
        var number = ticket.Number;
        var other = Guid.NewGuid();

        ticket.MoveToProduct(other, _factory.Agent, _factory.Clock);

        ticket.ProductId.ShouldBe(other);
        ticket.Number.ShouldBe(number);
        ticket.Number.ToString().ShouldBe("ACME-142");
        Types(ticket).ShouldBe([TicketEventType.ProductChanged]);
    }

    [Fact]
    public void Moving_to_the_same_product_raises_nothing()
    {
        var ticket = _factory.Saved();

        ticket.MoveToProduct(_factory.ProductId, _factory.Agent, _factory.Clock);

        ticket.PendingEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Adding_and_removing_a_tag_each_raise_one_event_and_repeats_are_idempotent()
    {
        var ticket = _factory.Saved();
        var tag = Guid.NewGuid();

        ticket.AddTag(tag, _factory.Agent, _factory.Clock);
        ticket.AddTag(tag, _factory.Agent, _factory.Clock);
        ticket.TagIds.ShouldBe([tag]);
        ticket.RemoveTag(tag, _factory.Agent, _factory.Clock);
        ticket.RemoveTag(tag, _factory.Agent, _factory.Clock);

        ticket.TagIds.ShouldBeEmpty();
        Types(ticket).ShouldBe([TicketEventType.TagAdded, TicketEventType.TagRemoved]);
    }

    [Fact]
    public void Marking_spam_and_clearing_it_leave_the_status_alone()
    {
        var ticket = _factory.InStatus(TicketStatus.Open);

        ticket.MarkSpam(true, _factory.Agent, _factory.Clock);
        ticket.MarkSpam(true, _factory.Agent, _factory.Clock);
        ticket.MarkSpam(false, _factory.Agent, _factory.Clock);

        ticket.IsSpam.ShouldBeFalse();
        ticket.Status.ShouldBe(TicketStatus.Open);
        Types(ticket).ShouldBe([TicketEventType.MarkedSpam, TicketEventType.MarkedSpam]);
    }

    [Fact]
    public void Every_mutation_on_a_Closed_ticket_is_a_conflict_with_no_event_and_no_change()
    {
        var ticket = _factory.InStatus(TicketStatus.Closed);
        var activity = ticket.LastActivityAt;
        _factory.Clock.Advance(TimeSpan.FromDays(1));

        DomainResult[] attempts =
        [
            ticket.ChangeStatus(TicketStatus.Open, _factory.Agent, _factory.Clock),
            ticket.AddAgentReply(_factory.AgentId, "x", _factory.Clock),
            ticket.AddInternalNote(_factory.AgentId, "x", _factory.Clock),
            ticket.AddCustomerReply(_factory.RequesterId, "x", _factory.Clock),
            ticket.Assign(_factory.AgentId, _factory.Agent, _factory.Clock),
            ticket.ChangePriority(TicketPriority.High, _factory.Agent, _factory.Clock),
            ticket.MoveToProduct(Guid.NewGuid(), _factory.Agent, _factory.Clock),
            ticket.AddTag(Guid.NewGuid(), _factory.Agent, _factory.Clock),
            ticket.RemoveTag(Guid.NewGuid(), _factory.Agent, _factory.Clock),
            ticket.MarkSpam(true, _factory.Agent, _factory.Clock),
        ];

        attempts.ShouldAllBe(result => result.IsFailure && result.Error!.Code == "ticket-closed" && result.Error.Kind == DomainErrorKind.Conflict);
        ticket.PendingEvents.ShouldBeEmpty();
        ticket.PendingMessages.ShouldBeEmpty();
        ticket.LastActivityAt.ShouldBe(activity);
        ticket.Status.ShouldBe(TicketStatus.Closed);
    }

    [Fact]
    public void Events_and_messages_of_one_ticket_get_strictly_increasing_times_even_when_the_clock_stands_still()
    {
        var ticket = _factory.New();
        ticket.AddCustomerReply(_factory.RequesterId, "one", _factory.Clock);
        ticket.AddAgentReply(_factory.AgentId, "two", _factory.Clock);
        ticket.Assign(_factory.AgentId, _factory.Agent, _factory.Clock);
        ticket.ChangePriority(TicketPriority.High, _factory.Agent, _factory.Clock);

        var eventTimes = ticket.PendingEvents.Select(e => e.OccurredAt).ToList();
        var messageTimes = ticket.PendingMessages.Select(m => m.CreatedAt).ToList();

        eventTimes.ShouldBe(eventTimes.Order());
        eventTimes.Distinct().Count().ShouldBe(eventTimes.Count);
        messageTimes[0].ShouldBeLessThan(messageTimes[1]);
        eventTimes[0].ShouldBe(ticket.CreatedAt);
    }

    [Fact]
    public void A_restored_ticket_stamps_new_events_after_its_last_activity_even_if_the_clock_has_not_moved()
    {
        var ticket = _factory.Saved();
        var restored = Ticket.Restore(
            ticket.Id, ticket.Number, ticket.ProductId, ticket.RequesterId, ticket.Subject, ticket.Status, ticket.Priority, ticket.AssigneeId, ticket.Channel,
            ticket.IsSpam, ticket.ParentTicketId, ticket.MetadataJson, ticket.MetadataTrusted, ticket.CustomFieldsJson, ticket.CreatedAt, ticket.FirstResponseAt,
            ticket.SolvedAt, ticket.ClosedAt, ticket.LastActivityAt, ticket.TagIds, 1);

        restored.ChangePriority(TicketPriority.High, _factory.Agent, _factory.Clock);

        restored.PendingEvents[0].OccurredAt.ShouldBeGreaterThan(ticket.LastActivityAt);
    }

    [Fact]
    public void Accepting_changes_clears_pending_events_and_messages()
    {
        var ticket = _factory.New();
        ticket.AddAgentReply(_factory.AgentId, "x", _factory.Clock);

        ticket.AcceptChanges();

        ticket.PendingEvents.ShouldBeEmpty();
        ticket.PendingMessages.ShouldBeEmpty();
    }

    [Fact]
    public void A_restored_ticket_round_trips_its_fields_and_has_nothing_pending()
    {
        var ticket = _factory.Saved();
        var tag = Guid.NewGuid();
        ticket.AddTag(tag, _factory.Agent, _factory.Clock);

        var restored = Ticket.Restore(
            ticket.Id, ticket.Number, ticket.ProductId, ticket.RequesterId, ticket.Subject, ticket.Status, ticket.Priority, ticket.AssigneeId, ticket.Channel,
            ticket.IsSpam, ticket.ParentTicketId, ticket.MetadataJson, ticket.MetadataTrusted, ticket.CustomFieldsJson, ticket.CreatedAt, ticket.FirstResponseAt,
            ticket.SolvedAt, ticket.ClosedAt, ticket.LastActivityAt, ticket.TagIds, 42);

        restored.TagIds.ShouldBe([tag]);
        restored.Version.ShouldBe(42u);
        restored.PendingEvents.ShouldBeEmpty();
    }
}
