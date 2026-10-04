using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SyntaxCircus.Common;
using TechStrap.Application.Persistence;
using TechStrap.Domain.Outbox;
using TechStrap.Domain.Products;
using TechStrap.Domain.Tickets;

namespace TechStrap.Application.Email;

public sealed record DrainResult(int Claimed, int Sent, int Failed);

public interface IDrainEmailOutboxHandler
{
    Task<Result<DrainResult>> HandleAsync(string workerId, CancellationToken cancellationToken);
}

internal static class DrainFailures
{
    public const string UnknownKind = "unknown-kind";
    public const string PayloadInvalid = "payload-invalid";
    public const string ProductMissing = "product-missing";
    public const string RenderFailed = "render-failed";
    public const string MessageMissing = "message-missing";
}

/// <summary>Claims a batch of due outbox rows, renders and sends each one, and marks it. Backoff and dead-lettering live in the Domain.</summary>
public sealed class DrainEmailOutboxHandler(
    IEmailOutboxStore store,
    IProductRepository products,
    ITicketRepository tickets,
    IEmailTemplateRenderer renderer,
    IOutboundEmailSender sender,
    IOptions<EmailOutboxWorkerOptions> options,
    ILogger<DrainEmailOutboxHandler> logger) : IDrainEmailOutboxHandler
{
    private static readonly JsonSerializerOptions _payloadOptions = new(JsonSerializerDefaults.Web);

    public async Task<Result<DrainResult>> HandleAsync(string workerId, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var batch = await store.ClaimBatchAsync(workerId, settings.BatchSize, TimeSpan.FromSeconds(settings.LeaseSeconds), cancellationToken);
        var productCache = new Dictionary<Guid, Product?>();
        int sent = 0, failed = 0;
        foreach (var item in batch)
        {
            var outcome = await SendOneAsync(item, productCache, cancellationToken);
            if (outcome is null)
            {
                // The email is already accepted by SMTP: a shutdown-time cancellation must not leave the row to be re-sent after the lease.
                var marked = await store.MarkSentAsync(item.Id, workerId, CancellationToken.None);
                if (marked.IsFailure)
                {
                    logger.LogWarning("Outbox row {OutboxId} was sent but could not be marked sent: {Code}.", item.Id, marked.Errors[0].Code);
                }

                sent++;
            }
            else
            {
                var marked = await store.MarkFailedAsync(item.Id, workerId, outcome, cancellationToken);
                if (marked.IsFailure)
                {
                    logger.LogWarning("Outbox row {OutboxId} failed ({Category}) and could not be marked: {Code}.", item.Id, outcome, marked.Errors[0].Code);
                }

                failed++;
            }
        }

        return Result<DrainResult>.Success(new DrainResult(batch.Count, sent, failed));
    }

    private async Task<string?> SendOneAsync(EmailOutboxItem item, Dictionary<Guid, Product?> productCache, CancellationToken cancellationToken)
    {
        var (model, failure) = Parse(item);
        if (model is null)
        {
            return failure;
        }

        if (item.ProductId is not { } productId)
        {
            return DrainFailures.ProductMissing;
        }

        if (!productCache.TryGetValue(productId, out var product))
        {
            product = await products.GetByIdAsync(productId, cancellationToken);
            productCache[productId] = product;
        }

        if (product is null)
        {
            return DrainFailures.ProductMissing;
        }

        string? messageHtml = null;
        if (model is AgentReplyEmail reply)
        {
            var message = await tickets.GetMessageAsync(reply.MessageId, cancellationToken);
            if (message is null || message.Visibility != MessageVisibility.Public)
            {
                return DrainFailures.MessageMissing;
            }

            messageHtml = message.Body;
        }

        RenderedEmail rendered;
        try
        {
            var branding = product.Branding;
            var emailBranding = new EmailBranding(branding.DisplayName, branding.LogoPath, branding.AccentColour, branding.FromAddress, branding.ReplyTo);
            rendered = model switch
            {
                TicketConfirmationEmail confirmation => renderer.RenderTicketConfirmation(confirmation, emailBranding),
                AgentReplyEmail agentReply => renderer.RenderAgentReply(agentReply, messageHtml!, emailBranding),
                TicketSolvedEmail solved => renderer.RenderTicketSolved(solved, emailBranding),
                TicketAssignedEmail assigned => renderer.RenderTicketAssigned(assigned, emailBranding),
                NewTicketAlertEmail newTicket => renderer.RenderNewTicketAlert(newTicket, emailBranding),
                CustomerReplyAlertEmail customerReply => renderer.RenderCustomerReplyAlert(customerReply, emailBranding),
                AccessLinksEmail accessLinks => renderer.RenderAccessLinks(accessLinks, emailBranding),
                _ => throw new InvalidOperationException("Unreachable outbox kind."),
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("Rendering outbox row {OutboxId} failed: {ExceptionType}.", item.Id, ex.GetType().Name);
            return DrainFailures.RenderFailed;
        }

        var result = await sender.SendAsync(
            new OutboundEmail(item.ToAddress, rendered.Subject, rendered.Text, rendered.Html, rendered.From, rendered.ReplyTo, OutboundMessageIds.For(item.Id)),
            cancellationToken);
        return result.IsFailure ? result.Errors[0].Code : null;
    }

    /// <summary>The one place that knows the kinds: deserialises and validates a payload, or names why it cannot be sent.</summary>
    private static (object? Model, string? Failure) Parse(EmailOutboxItem item)
    {
        try
        {
            return item.Kind switch
            {
                EmailTemplates.TicketConfirmation => Check(item, (TicketConfirmationEmail m) => Present(m.TicketNumber, m.PortalLink)),
                EmailTemplates.AgentReply => Check(item, (AgentReplyEmail m) => Present(m.TicketNumber, m.PortalLink, m.AgentPublicName) && m.MessageId != Guid.Empty),
                EmailTemplates.TicketSolved => Check(item, (TicketSolvedEmail m) => Present(m.TicketNumber, m.PortalLink)),
                EmailTemplates.TicketAssigned => Check(item, (TicketAssignedEmail m) => Present(m.TicketNumber)),
                EmailTemplates.NewTicketAlert => Check(item, (NewTicketAlertEmail m) => Present(m.TicketNumber, m.Subject, m.ProductName, m.RequesterLabel)),
                EmailTemplates.CustomerReplyAlert => Check(item, (CustomerReplyAlertEmail m) => Present(m.TicketNumber, m.Subject, m.ProductName)),
                EmailTemplates.AccessLinks => Check(item, (AccessLinksEmail m) => m.Links is { Count: > 0 } && m.Links.All(l => l is not null && Present(l.TicketNumber, l.PortalLink))),
                _ => (null, DrainFailures.UnknownKind),
            };
        }
        catch (JsonException)
        {
            return (null, DrainFailures.PayloadInvalid);
        }
    }

    private static (object? Model, string? Failure) Check<T>(EmailOutboxItem item, Func<T, bool> isValid) where T : class
    {
        var model = JsonSerializer.Deserialize<T>(item.PayloadJson, _payloadOptions);
        return model is not null && isValid(model) ? (model, null) : (null, DrainFailures.PayloadInvalid);
    }

    private static bool Present(params string?[] values) => values.All(value => !string.IsNullOrWhiteSpace(value));
}
