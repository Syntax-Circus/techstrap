using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TechStrap.Application.Email;
using TechStrap.Application.Intake;
using TechStrap.Application.Persistence;
using TechStrap.Application.Security;
using TechStrap.Application.Tickets;
using TechStrap.Application.Tickets.AutoClose;
using TechStrap.Application.Tickets.Customer;
using TechStrap.Application.Tickets.Notifications;
using TechStrap.Domain.Agents;
using TechStrap.Domain.Outbox;
using TechStrap.Domain.Products;
using TechStrap.Domain.Requesters;
using TechStrap.Domain.Rules;
using TechStrap.Domain.Tickets;

namespace TechStrap.Infrastructure.Tickets;

/// <summary>
/// Stages outbox rows in the caller's unit of work. Payloads are template data (D-033); neither payload nor link is ever logged,
/// and customer payloads carry only the resolved public agent name (D-024).
/// </summary>
internal sealed class TicketNotificationPlanner(
    IRequesterRepository requesters,
    IProductRepository products,
    IAgentRepository agents,
    ITicketRepository tickets,
    IAccessTokenService accessTokens,
    IEmailOutbox outbox,
    IOptions<PortalLinkOptions> portalOptions,
    IOptions<AdminLinkOptions> adminOptions,
    IOptions<AutoCloseOptions> autoClose,
    IOptions<LostLinkOptions> lostLink,
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
                solved,
                autoClose.Value.Days),
            cancellationToken);

    public Task PlanSolvedAsync(Ticket ticket, CancellationToken cancellationToken) =>
        PlanCustomerAsync(
            ticket,
            EmailTemplates.TicketSolved,
            (requester, _, link) => new TicketSolvedEmail(ticket.Number.ToString(), ticket.Subject, requester.Name, link, autoClose.Value.Days),
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

    public Task PlanFollowUpConfirmationAsync(Ticket followUp, CancellationToken cancellationToken) =>
        PlanCustomerAsync(
            followUp,
            EmailTemplates.TicketConfirmation,
            (requester, _, link) => new TicketConfirmationEmail(followUp.Number.ToString(), followUp.Subject, requester.Name, link, null),
            cancellationToken);

    public async Task PlanNewTicketAsync(Ticket ticket, Requester requester, bool isFollowUp, CancellationToken cancellationToken)
    {
        if (ticket.IsSpam)
        {
            logger.LogInformation("Skipped {Kind} for ticket {TicketId}: ticket is flagged as spam", EmailTemplates.NewTicketAlert, ticket.Id);
            return;
        }

        var product = await products.GetByIdAsync(ticket.ProductId, cancellationToken);
        if (product is null)
        {
            logger.LogInformation("Skipped {Kind} for ticket {TicketId}: product unavailable", EmailTemplates.NewTicketAlert, ticket.Id);
            return;
        }

        var recipients = await agents.ListAgentsToAlertForProductAsync(ticket.ProductId, cancellationToken);
        var number = ticket.Number.ToString();
        var payload = new NewTicketAlertEmail(number, ticket.Subject, product.Name, requester.Name ?? requester.Email, isFollowUp, adminOptions.Value.TicketLink(number));
        foreach (var agent in recipients.DistinctBy(a => a.Id))
        {
            Stage(EmailTemplates.NewTicketAlert, agent.Email, payload, ticket);
        }
    }

    public async Task PlanCustomerReplyAsync(Ticket ticket, bool reopened, CancellationToken cancellationToken)
    {
        if (ticket.IsSpam)
        {
            logger.LogInformation("Skipped {Kind} for ticket {TicketId}: ticket is flagged as spam", EmailTemplates.CustomerReplyAlert, ticket.Id);
            return;
        }

        var product = await products.GetByIdAsync(ticket.ProductId, cancellationToken);
        if (product is null)
        {
            logger.LogInformation("Skipped {Kind} for ticket {TicketId}: product unavailable", EmailTemplates.CustomerReplyAlert, ticket.Id);
            return;
        }

        IReadOnlyList<Agent> recipients = [];
        if (ticket.AssigneeId is { } assigneeId)
        {
            var assignee = await agents.GetByIdAsync(assigneeId, cancellationToken);
            if (assignee is { IsActive: true })
            {
                recipients = [assignee];
            }
        }

        if (recipients.Count == 0)
        {
            recipients = await agents.ListAgentsToAlertForProductAsync(ticket.ProductId, cancellationToken);
        }

        var number = ticket.Number.ToString();
        var payload = new CustomerReplyAlertEmail(number, ticket.Subject, product.Name, reopened, adminOptions.Value.TicketLink(number));
        foreach (var agent in recipients.DistinctBy(a => a.Id))
        {
            Stage(EmailTemplates.CustomerReplyAlert, agent.Email, payload, ticket);
        }
    }

    public async Task PlanAccessLinksAsync(Requester requester, IReadOnlyList<RequesterTicketLink> ticketLinks, CancellationToken cancellationToken)
    {
        if (requester.IsErased || ticketLinks.Count == 0)
        {
            logger.LogInformation("Skipped {Kind}: requester unavailable or no tickets", EmailTemplates.AccessLinks);
            return;
        }

        var staged = new List<(RequesterTicketLink Link, AccessLinkEntry Entry, TicketAccessToken Token)>();
        foreach (var link in ticketLinks.Take(lostLink.Value.MaxLinks))
        {
            var issued = accessTokens.Issue(link.TicketId, requester.Id);
            if (issued.IsFailure)
            {
                logger.LogInformation("Skipped a link for {Kind}: access token not issued ({Code})", EmailTemplates.AccessLinks, issued.Error!.Code);
                continue;
            }

            staged.Add((link, new AccessLinkEntry(link.Number, link.Subject, portalOptions.Value.TicketLink(issued.Value.PlaintextToken)), issued.Value.Token));
        }

        if (staged.Count == 0)
        {
            return;
        }

        // Branding follows the first staged link (most recently active survivor): the drain requires a product on the row.
        var first = staged[0].Link;
        if (await products.GetByIdAsync(first.ProductId, cancellationToken) is null)
        {
            logger.LogInformation("Skipped {Kind}: product unavailable", EmailTemplates.AccessLinks);
            return;
        }

        var dropped = 0;
        string json;
        while (true)
        {
            json = JsonSerializer.Serialize(new AccessLinksEmail(requester.Name, [.. staged.Select(s => s.Entry)]), PayloadJson);
            if (json.Length <= DomainLimits.OutboxPayloadMaxLength || staged.Count <= 1)
            {
                break;
            }

            staged.RemoveAt(staged.Count - 1);
            dropped++;
        }

        if (dropped > 0)
        {
            logger.LogInformation("Dropped {Dropped} links from {Kind}: payload size cap", dropped, EmailTemplates.AccessLinks);
        }

        var item = EmailOutboxItem.Enqueue(EmailTemplates.AccessLinks, requester.Email, json, first.ProductId, first.TicketId, clock);
        if (item.IsFailure)
        {
            logger.LogInformation("Skipped {Kind}: not queued ({Code})", EmailTemplates.AccessLinks, item.Error!.Code);
            return;
        }

        // Tokens are staged only once the row is valid: all tokens or none (06a ordering rule).
        foreach (var (_, _, token) in staged)
        {
            tickets.AddAccessToken(token);
        }

        outbox.Enqueue(item.Value);
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
