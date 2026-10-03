using Microsoft.Extensions.Time.Testing;
using TechStrap.Domain.Tickets;

namespace TechStrap.Domain.Tests.Tickets;

/// <summary>Builds tickets in a known state for the ticket tests.</summary>
internal sealed class TicketFactory
{
    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 2, 9, 0, 0, TimeSpan.Zero));

    public Guid ProductId { get; } = Guid.NewGuid();

    public Guid RequesterId { get; } = Guid.NewGuid();

    public Guid AgentId { get; } = Guid.NewGuid();

    public Actor Agent => Actor.ForAgent(AgentId);

    public TicketNumber Number(long sequence = 142) => TicketNumber.Create("ACME", sequence).Value;

    public Ticket New(string subject = "Cannot sign in") =>
        Ticket.Create(Number(), ProductId, RequesterId, subject, TicketChannel.Web, null, false, Clock).Value;

    /// <summary>A ticket whose creation event has been persisted, so tests only see events of the mutation under test.</summary>
    public Ticket Saved(string subject = "Cannot sign in")
    {
        var ticket = New(subject);
        ticket.AcceptChanges();
        return ticket;
    }

    public Ticket InStatus(TicketStatus status)
    {
        var ticket = Saved();
        foreach (var step in PathTo(status))
        {
            ticket.ChangeStatus(step, Actor.System, Clock).IsSuccess.ShouldBeTrue();
        }

        ticket.AcceptChanges();
        return ticket;
    }

    /// <summary>A Closed ticket that still carries a tag, with its events persisted.</summary>
    public Ticket ClosedWithTag(out Guid tagId)
    {
        tagId = Guid.CreateVersion7();
        var ticket = Saved();
        ticket.AddTag(tagId, Agent, Clock).IsSuccess.ShouldBeTrue();
        ticket.ChangeStatus(TicketStatus.Solved, Actor.System, Clock).IsSuccess.ShouldBeTrue();
        ticket.ChangeStatus(TicketStatus.Closed, Actor.System, Clock).IsSuccess.ShouldBeTrue();
        ticket.AcceptChanges();
        return ticket;
    }

    private static TicketStatus[] PathTo(TicketStatus status) => status switch
    {
        TicketStatus.New => [],
        TicketStatus.Open => [TicketStatus.Open],
        TicketStatus.Pending => [TicketStatus.Pending],
        TicketStatus.Solved => [TicketStatus.Solved],
        TicketStatus.Closed => [TicketStatus.Solved, TicketStatus.Closed],
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };
}
