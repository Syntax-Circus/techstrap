using TechStrap.Domain.Agents;
using TechStrap.Domain.Requesters;
using TechStrap.Domain.Tickets;

namespace TechStrap.Application.Tickets.Notifications;

/// <summary>
/// Decides who is emailed about an agent action and stages outbox rows (template data only, D-033) in the caller's
/// unit of work. Never throws for an email problem: a skipped or failed notice is logged by code and the action stands.
/// </summary>
public interface ITicketNotificationPlanner
{
    Task PlanAgentReplyAsync(Ticket ticket, Message message, Agent author, bool solved, CancellationToken cancellationToken);

    Task PlanSolvedAsync(Ticket ticket, CancellationToken cancellationToken);

    Task PlanAssignedAsync(Ticket ticket, Agent assignee, Agent actor, CancellationToken cancellationToken);

    /// <summary>New-ticket alert to agents opted in for the ticket's product (active only). Nothing for spam tickets (a follow-up of a spam ticket is spam).</summary>
    Task PlanNewTicketAsync(Ticket ticket, Requester requester, bool isFollowUp, CancellationToken cancellationToken);

    /// <summary>Customer replied: to the assignee if set and active, else to agents opted in for the product. Nothing for spam tickets (D-038).</summary>
    Task PlanCustomerReplyAsync(Ticket ticket, bool reopened, CancellationToken cancellationToken);

    /// <summary>Confirmation to the requester for a follow-up ticket (ticket-confirmation kind, fresh token). Skips spam and erased, as for any customer email.</summary>
    Task PlanFollowUpConfirmationAsync(Ticket followUp, CancellationToken cancellationToken);

    /// <summary>One access-links email to the requester's own address with a fresh link per ticket (D-037: nothing revoked). Skips erased requesters and empty lists.</summary>
    Task PlanAccessLinksAsync(Requester requester, IReadOnlyList<RequesterTicketLink> tickets, CancellationToken cancellationToken);
}
