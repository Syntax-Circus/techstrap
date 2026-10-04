using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TechStrap.Application.Email;
using TechStrap.Application.Intake;
using TechStrap.Application.Persistence;
using TechStrap.Application.Security;
using TechStrap.Application.Tickets.Notifications;
using TechStrap.Domain.Agents;
using TechStrap.Domain.Outbox;
using TechStrap.Domain.Products;
using TechStrap.Domain.Requesters;
using TechStrap.Domain.Tickets;

namespace TechStrap.Infrastructure.Tickets;

/// <summary>
/// Stages outbox rows in the caller's unit of work. Payloads are template data (D-033); neither payload nor link is ever logged,
/// and customer payloads carry only the resolved public agent name (D-024).
/// </summary>
internal sealed class TicketNotificationPlanner(
    IRequesterRepository requesters,
    IProductRepository products,
    ITicketRepository tickets,
    IAccessTokenService accessTokens,
    IEmailOutbox outbox,
    IOptions<PortalLinkOptions> portalOptions,
    IOptions<AdminLinkOptions> adminOptions,
    TimeProvider clock,
    ILogger<TicketNotificationPlanner> logger) : ITicketNotificationPlanner
{
    private static readonly JsonSerializerOptions PayloadJson = new(JsonSerializerDefaults.Web);

    public Task PlanAgentReplyAsync(Ticket ticket, Message message, Agent author, bool solved, CancellationToken cancellationToken) =>
        PlanCustomerAsync(
            ticket,
            EmailTemplates.AgentReply,
            (requester, product, link) => new AgentReplyEmail(
                ticket.Number.ToString(),
                ticket.Subject,
                requester.Name,
                link,
                AgentPublicIdentity.Resolve(author, product.Branding.DisplayName),
                message.Id,
                solved),
            cancellationToken);

    public Task PlanSolvedAsync(Ticket ticket, CancellationToken cancellationToken) =>
        PlanCustomerAsync(
            ticket,
            EmailTemplates.TicketSolved,
            (requester, _, link) => new TicketSolvedEmail(ticket.Number.ToString(), ticket.Subject, requester.Name, link, TicketNotices.DefaultReopenDays),
            cancellationToken);

    public async Task PlanAssignedAsync(Ticket ticket, Agent assignee, Agent actor, CancellationToken cancellationToken)
    {
        if (assignee.Id == actor.Id || !assignee.IsActive)
        {
            logger.LogInformation("Skipped {Kind} for ticket {TicketId}: assignee does not need an alert", EmailTemplates.TicketAssigned, ticket.Id);
            return;
        }

        var product = await products.GetByIdAsync(ticket.ProductId, cancellationToken);
        if (product is null)
        {
            logger.LogInformation("Skipped {Kind} for ticket {TicketId}: product unavailable", EmailTemplates.TicketAssigned, ticket.Id);
            return;
        }

        var number = ticket.Number.ToString();
        var payload = new TicketAssignedEmail(number, ticket.Subject, product.Name, actor.Name, adminOptions.Value.TicketLink(number));
        Stage(EmailTemplates.TicketAssigned, assignee.Email, payload, ticket);
    }

    private async Task PlanCustomerAsync<TPayload>(
        Ticket ticket,
        string kind,
        Func<Requester, Product, string, TPayload> build,
        CancellationToken cancellationToken)
    {
        if (ticket.IsSpam)
        {
            // Owner decision (D-035): spam tickets never email the customer. Nothing is staged, including the access token.
            logger.LogInformation("Skipped {Kind} for ticket {TicketId}: ticket is flagged as spam", kind, ticket.Id);
            return;
        }

        var requester = await requesters.GetByIdAsync(ticket.RequesterId, cancellationToken);
        if (requester is null || requester.IsErased)
        {
            logger.LogInformation("Skipped {Kind} for ticket {TicketId}: requester unavailable", kind, ticket.Id);
            return;
        }

        var product = await products.GetByIdAsync(ticket.ProductId, cancellationToken);
        if (product is null)
        {
            logger.LogInformation("Skipped {Kind} for ticket {TicketId}: product unavailable", kind, ticket.Id);
            return;
        }

        var issued = accessTokens.Issue(ticket.Id, requester.Id);
        if (issued.IsFailure)
        {
            logger.LogInformation("Skipped {Kind} for ticket {TicketId}: access token not issued ({Code})", kind, ticket.Id, issued.Error!.Code);
            return;
        }

        // The token is staged only once the email row is known to be valid, so a failed enqueue never leaves an orphan token.
        var link = portalOptions.Value.TicketLink(issued.Value.PlaintextToken);
        Stage(kind, requester.Email, build(requester, product, link), ticket, issued.Value.Token);
    }

    private void Stage<TPayload>(string kind, string toAddress, TPayload payload, Ticket ticket, TicketAccessToken? token = null)
    {
        var json = JsonSerializer.Serialize(payload, PayloadJson);
        var item = EmailOutboxItem.Enqueue(kind, toAddress, json, ticket.ProductId, ticket.Id, clock);
        if (item.IsFailure)
        {
            logger.LogInformation("Skipped {Kind} for ticket {TicketId}: not queued ({Code})", kind, ticket.Id, item.Error!.Code);
            return;
        }

        if (token is not null)
        {
            tickets.AddAccessToken(token);
        }

        outbox.Enqueue(item.Value);
    }
}
