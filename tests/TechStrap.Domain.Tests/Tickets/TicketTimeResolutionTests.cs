using Microsoft.Extensions.Time.Testing;
using TechStrap.Domain.Tickets;

namespace TechStrap.Domain.Tests.Tickets;

public sealed class TicketTimeResolutionTests
{
    [Fact]
    public void Every_stored_ticket_time_is_whole_microseconds()
    {
        var factory = new TicketFactory();
        factory.Clock.SetUtcNow(new DateTimeOffset(2026, 10, 2, 9, 0, 0, TimeSpan.Zero).AddTicks(3));
        var ticket = factory.New();

        (ticket.CreatedAt.Ticks % 10).ShouldBe(0);

        ticket.AddAgentReply(factory.AgentId, "hello", factory.Clock).IsSuccess.ShouldBeTrue();
        (ticket.FirstResponseAt!.Value.Ticks % 10).ShouldBe(0);

        ticket.ChangeStatus(TicketStatus.Solved, factory.Agent, factory.Clock).IsSuccess.ShouldBeTrue();
        (ticket.SolvedAt!.Value.Ticks % 10).ShouldBe(0);

        ticket.ChangeStatus(TicketStatus.Closed, factory.Agent, factory.Clock).IsSuccess.ShouldBeTrue();
        (ticket.ClosedAt!.Value.Ticks % 10).ShouldBe(0);
        (ticket.LastActivityAt.Ticks % 10).ShouldBe(0);
    }
}
