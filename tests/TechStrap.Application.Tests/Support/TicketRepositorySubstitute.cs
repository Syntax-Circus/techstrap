using NSubstitute;
using TechStrap.Application.Persistence;
using TechStrap.Domain.Tickets;

namespace TechStrap.Application.Tests.Support;

/// <summary>An ITicketRepository whose <c>Update</c> behaves like the real one: it accepts the ticket's pending changes while staging.</summary>
public static class TicketRepositorySubstitute
{
    /// <param name="beforeAccept">Runs with the ticket just before its pending messages, attachments and events are cleared.</param>
    public static ITicketRepository Create(Action<Ticket>? beforeAccept = null)
    {
        var repository = Substitute.For<ITicketRepository>();
        repository.When(r => r.Update(Arg.Any<Ticket>())).Do(call =>
        {
            var ticket = call.Arg<Ticket>();
            beforeAccept?.Invoke(ticket);
            ticket.AcceptChanges();
        });
        return repository;
    }
}
