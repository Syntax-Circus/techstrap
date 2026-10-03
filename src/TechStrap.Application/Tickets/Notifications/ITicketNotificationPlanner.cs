using TechStrap.Domain.Agents;
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
}
