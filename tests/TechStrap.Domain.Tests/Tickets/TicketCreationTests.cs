using System.Text.Json;
using TechStrap.Domain.Rules;
using TechStrap.Domain.Tickets;

namespace TechStrap.Domain.Tests.Tickets;

public sealed class TicketCreationTests
{
    private readonly TicketFactory _factory = new();

    [Fact]
    public void A_new_ticket_is_New_with_normal_priority_and_one_Created_event()
    {
        var ticket = _factory.New();

        ticket.Status.ShouldBe(TicketStatus.New);
        ticket.Priority.ShouldBe(TicketPriority.Normal);
        ticket.IsSpam.ShouldBeFalse();
        ticket.AssigneeId.ShouldBeNull();
        ticket.ParentTicketId.ShouldBeNull();
        ticket.Number.ToString().ShouldBe("ACME-142");
        ticket.CreatedAt.ShouldBe(_factory.Clock.GetUtcNow());
        ticket.LastActivityAt.ShouldBe(ticket.CreatedAt);

        var created = ticket.PendingEvents.ShouldHaveSingleItem();
        created.Type.ShouldBe(TicketEventType.Created);
        created.ActorType.ShouldBe(ActorType.Requester);
        created.ActorId.ShouldBe(_factory.RequesterId);
        created.TicketId.ShouldBe(ticket.Id);
    }

    [Fact]
    public void The_subject_is_trimmed_required_and_length_limited()
    {
        _factory.New("  Help  ").Subject.ShouldBe("Help");
        Ticket.Create(_factory.Number(), _factory.ProductId, _factory.RequesterId, " ", TicketChannel.Web, null, false, _factory.Clock)
            .Error!.Code.ShouldBe("subject-required");
        Ticket.Create(_factory.Number(), _factory.ProductId, _factory.RequesterId, new string('a', DomainLimits.SubjectMaxLength + 1), TicketChannel.Web, null, false, _factory.Clock)
            .Error!.Code.ShouldBe("subject-too-long");
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    [InlineData("\"text\"")]
    public void Metadata_that_is_not_a_json_object_is_rejected(string metadata)
    {
        Ticket.Create(_factory.Number(), _factory.ProductId, _factory.RequesterId, "S", TicketChannel.Api, metadata, true, _factory.Clock)
            .Error!.Code.ShouldBe("metadata-invalid");
    }

    [Fact]
    public void Metadata_and_its_trust_flag_are_kept()
    {
        var ticket = Ticket.Create(_factory.Number(), _factory.ProductId, _factory.RequesterId, "S", TicketChannel.Api, "{\"os\":\"iOS 19\"}", false, _factory.Clock).Value;

        ticket.MetadataJson.ShouldBe("{\"os\":\"iOS 19\"}");
        ticket.MetadataTrusted.ShouldBeFalse();
        ticket.Channel.ShouldBe(TicketChannel.Api);
    }

    [Fact]
    public void Event_payloads_hold_ids_and_enum_names_only_never_subject_or_message_text()
    {
        var ticket = _factory.New("Secret subject for ann@example.com");
        ticket.AddAgentReply(_factory.AgentId, "Body with ann@example.com inside", _factory.Clock);
        ticket.AddCustomerReply(_factory.RequesterId, "Another body with ann@example.com", _factory.Clock);
        ticket.AddTag(Guid.NewGuid(), _factory.Agent, _factory.Clock);
        ticket.Assign(_factory.AgentId, _factory.Agent, _factory.Clock);

        var payloads = string.Join(' ', ticket.PendingEvents.Select(e => e.PayloadJson));

        payloads.ShouldNotContain("Secret");
        payloads.ShouldNotContain("ann@example.com");
        payloads.ShouldNotContain("Body");
        foreach (var ticketEvent in ticket.PendingEvents)
        {
            JsonDocument.Parse(ticketEvent.PayloadJson).RootElement.ValueKind.ShouldBe(JsonValueKind.Object);
        }
    }

    [Fact]
    public void A_ticket_event_exposes_no_way_to_change_it()
    {
        typeof(TicketEvent).GetProperties().ShouldAllBe(property => property.SetMethod == null || !property.SetMethod.IsPublic);
        typeof(TicketEvent).GetMethods().Where(m => m.DeclaringType == typeof(TicketEvent) && m.IsPublic && !m.IsSpecialName && m.IsStatic == false)
            .Select(m => m.Name).ShouldBeEmpty();
    }

    [Fact]
    public void A_follow_up_of_a_closed_ticket_links_to_it_and_leaves_it_unchanged()
    {
        var closed = _factory.InStatus(TicketStatus.Closed);
        var closedAt = closed.ClosedAt;
        var lastActivity = closed.LastActivityAt;

        var followUp = closed.CreateFollowUp(_factory.Number(143), _factory.Clock).Value;

        followUp.ParentTicketId.ShouldBe(closed.Id);
        followUp.Number.ToString().ShouldBe("ACME-143");
        followUp.Subject.ShouldBe(closed.Subject);
        followUp.ProductId.ShouldBe(closed.ProductId);
        followUp.RequesterId.ShouldBe(closed.RequesterId);
        followUp.Status.ShouldBe(TicketStatus.New);
        followUp.PendingEvents.ShouldHaveSingleItem().Type.ShouldBe(TicketEventType.Created);
        followUp.PendingEvents[0].PayloadJson.ShouldContain(closed.Id.ToString());
        closed.Status.ShouldBe(TicketStatus.Closed);
        closed.ClosedAt.ShouldBe(closedAt);
        closed.LastActivityAt.ShouldBe(lastActivity);
        var parentEvent = closed.PendingEvents.ShouldHaveSingleItem();
        parentEvent.Type.ShouldBe(TicketEventType.FollowUpCreated);
        parentEvent.PayloadJson.ShouldContain(followUp.Id.ToString());
    }

    [Theory]
    [InlineData(TicketStatus.New)]
    [InlineData(TicketStatus.Open)]
    [InlineData(TicketStatus.Pending)]
    [InlineData(TicketStatus.Solved)]
    public void Only_a_closed_ticket_can_have_a_follow_up(TicketStatus status)
    {
        var result = _factory.InStatus(status).CreateFollowUp(_factory.Number(143), _factory.Clock);

        result.Error!.Code.ShouldBe("ticket-not-closed");
        result.Error.Kind.ShouldBe(DomainErrorKind.Conflict);
    }

    [Fact]
    public void A_second_follow_up_of_the_same_closed_ticket_succeeds_and_raises_a_second_event()
    {
        var closed = _factory.InStatus(TicketStatus.Closed);

        var first = closed.CreateFollowUp(_factory.Number(143), _factory.Clock).Value;
        var second = closed.CreateFollowUp(_factory.Number(144), _factory.Clock).Value;

        second.Id.ShouldNotBe(first.Id);
        second.ParentTicketId.ShouldBe(closed.Id);
        closed.Status.ShouldBe(TicketStatus.Closed);
        closed.PendingEvents.Select(e => e.Type).ShouldBe([TicketEventType.FollowUpCreated, TicketEventType.FollowUpCreated]);
        closed.PendingEvents[1].OccurredAt.ShouldBeGreaterThan(closed.PendingEvents[0].OccurredAt);
        closed.PendingEvents[1].PayloadJson.ShouldContain(second.Id.ToString());
    }

    [Fact]
    public void A_follow_up_of_a_spam_ticket_is_spam_with_a_system_marked_spam_event()
    {
        var parent = _factory.InStatus(TicketStatus.Solved);
        parent.MarkSpam(true, Actor.ForAgent(Guid.NewGuid()), _factory.Clock).IsSuccess.ShouldBeTrue();
        parent.ChangeStatus(TicketStatus.Closed, Actor.System, _factory.Clock).IsSuccess.ShouldBeTrue();

        var followUp = parent.CreateFollowUp(_factory.Number(143), _factory.Clock).Value;

        followUp.IsSpam.ShouldBeTrue();
        followUp.PendingEvents.Select(e => e.Type).ShouldBe([TicketEventType.Created, TicketEventType.MarkedSpam]);
        followUp.PendingEvents[1].ActorType.ShouldBe(ActorType.System);
        followUp.PendingEvents[1].PayloadJson.ShouldContain("true");
    }

    [Fact]
    public void A_follow_up_of_a_normal_ticket_is_not_spam()
    {
        var followUp = _factory.InStatus(TicketStatus.Closed).CreateFollowUp(_factory.Number(143), _factory.Clock).Value;

        followUp.IsSpam.ShouldBeFalse();
    }
}
