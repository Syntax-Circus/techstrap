using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SyntaxCircus.Common;
using TechStrap.Application.Persistence;
using TechStrap.Domain.Outbox;
using TechStrap.Domain.Products;

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
}

/// <summary>Claims a batch of due outbox rows, renders and sends each one, and marks it. Backoff and dead-lettering live in the Domain.</summary>
public sealed class DrainEmailOutboxHandler(
    IEmailOutboxStore store,
    IProductRepository products,
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
                var marked = await store.MarkSentAsync(item.Id, workerId, cancellationToken);
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
        if (item.Kind != EmailTemplates.TicketConfirmation)
        {
            return DrainFailures.UnknownKind;
        }

        TicketConfirmationEmail? model;
        try
        {
            model = JsonSerializer.Deserialize<TicketConfirmationEmail>(item.PayloadJson, _payloadOptions);
        }
        catch (JsonException)
        {
            return DrainFailures.PayloadInvalid;
        }

        if (model is null || string.IsNullOrWhiteSpace(model.TicketNumber) || string.IsNullOrWhiteSpace(model.PortalLink))
        {
            return DrainFailures.PayloadInvalid;
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

        RenderedEmail rendered;
        try
        {
            var branding = product.Branding;
            rendered = renderer.RenderTicketConfirmation(
                model,
                new EmailBranding(branding.DisplayName, branding.LogoPath, branding.AccentColour, branding.FromAddress, branding.ReplyTo));
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
}
