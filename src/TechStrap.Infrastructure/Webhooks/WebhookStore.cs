using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SyntaxCircus.Common;
using TechStrap.Application.Persistence;
using TechStrap.Application.Webhooks;
using TechStrap.Contracts.Webhooks;
using TechStrap.Domain.Admin;
using TechStrap.Infrastructure.Persistence;
using TechStrap.Infrastructure.Persistence.Records;

namespace TechStrap.Infrastructure.Webhooks;

internal sealed class WebhookStore(TechStrapDbContext context, IUnitOfWork unitOfWork, TimeProvider clock) : IWebhookStore
{
    private const int PageSize = 25;
    internal static ResultError Invalid(string message) => new("webhook-invalid", message, ResultErrorKind.Validation, "webhooks");
    private static ResultError Missing() => new("webhook-not-found", "That product, endpoint or delivery does not exist.", ResultErrorKind.NotFound);
    private static ResultError Stale() => new("webhook-stale", "Webhook settings changed. Reload before saving.", ResultErrorKind.Conflict);

    private Task<bool> ExistsAsync(Guid? productId, CancellationToken ct) => productId is null ? Task.FromResult(true) : context.Set<ProductRecord>().AnyAsync(x => x.Id == productId, ct);

    public async Task<Result<WebhookSettingsDto>> GetAsync(Guid? productId, CancellationToken cancellationToken)
    {
        if (!await ExistsAsync(productId, cancellationToken)) return Result<WebhookSettingsDto>.Failure(Missing());
        var id = productId ?? WebhookPolicy.GlobalScope;
        var record = await context.Set<WebhookSettingsRecord>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        var mode = record?.Mode ?? (productId is null ? "Replace" : "Inherit");
        var own = record is null ? [] : WebhookJson.Endpoints(record.Endpoints).Select(WebhookJson.Redact).ToArray();
        IReadOnlyList<WebhookEndpointDto> effective = mode == "Disabled" ? [] : own;
        if (mode == "Inherit")
        {
            var global = await context.Set<WebhookSettingsRecord>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == WebhookPolicy.GlobalScope, cancellationToken);
            effective = global is null || global.Mode == "Disabled" ? [] : WebhookJson.Endpoints(global.Endpoints).Select(WebhookJson.Redact).ToArray();
        }
        return Result<WebhookSettingsDto>.Success(new(mode, record?.Version ?? 0, own, effective));
    }

    public async Task<Result<WebhookSettingsDto>> UpdateAsync(Guid? productId, UpdateWebhookSettingsRequest request, Guid actorId, CancellationToken cancellationToken)
    {
        if (!await ExistsAsync(productId, cancellationToken)) return Result<WebhookSettingsDto>.Failure(Missing());
        if (request.Mode is not ("Inherit" or "Disabled" or "Replace") || (productId is null && request.Mode == "Inherit") || request.Endpoints is null || request.Endpoints.Count > WebhookPolicy.MaxEndpoints)
            return Result<WebhookSettingsDto>.Failure(Invalid("Choose a valid mode and at most 20 endpoints."));
        await using var transaction = await unitOfWork.BeginAsync(cancellationToken);
        var scope = productId ?? WebhookPolicy.GlobalScope;
        var record = await context.Set<WebhookSettingsRecord>().SingleOrDefaultAsync(x => x.Id == scope, cancellationToken);
        if ((record?.Version ?? 0) != request.Version) return Result<WebhookSettingsDto>.Failure(Stale());
        var existing = record is null ? [] : WebhookJson.Endpoints(record.Endpoints);
        var endpoints = new List<WebhookEndpointRequest>();
        foreach (var edit in request.Endpoints)
        {
            if (edit is null) return Result<WebhookSettingsDto>.Failure(Invalid("Endpoint cannot be empty."));
            var old = existing.SingleOrDefault(x => x.Id == edit.Id);
            if (edit.Id != Guid.Empty && old is null) return Result<WebhookSettingsDto>.Failure(Invalid("Reload before editing an unknown endpoint."));
            var endpoint = edit with { Id = old?.Id ?? Guid.CreateVersion7(), Name = edit.Name?.Trim() ?? "", Url = edit.Url ?? old?.Url };
            if (endpoint.Name.Length is 0 or > WebhookPolicy.MaxNameLength || !WebhookPolicy.ValidUrl(endpoint.Url)
                || endpoint.Format is not ("Discord" or "Json") || endpoint.TicketTypes is null || endpoint.TicketStatuses is null || endpoint.FeatureStatuses is null
                || endpoint.TicketTypes.Any(x => x is not ("Support" or "FeatureRequest"))
                || endpoint.TicketStatuses.Except(WebhookPolicy.TicketStatuses).Any() || endpoint.FeatureStatuses.Except(WebhookPolicy.FeatureStatuses).Any()
                || (endpoint.Format == "Discord" && !DiscordWebhookUrl(endpoint.Url!)))
                return Result<WebhookSettingsDto>.Failure(Invalid("Enter a valid endpoint name, public HTTPS URL, format and status selections. Discord requires a discord.com/api/webhooks URL."));
            if (endpoints.Any(x => x.Id == endpoint.Id)) return Result<WebhookSettingsDto>.Failure(Invalid("An endpoint cannot appear twice."));
            endpoints.Add(endpoint);
        }
        if (record is null) { record = new() { Id = scope }; context.Add(record); }
        record.Mode = request.Mode;
        record.Endpoints = WebhookJson.Write(endpoints);
        record.Version++;
        Audit(actorId, AdminEventType.WebhookSettingsUpdated, scope, new { mode = request.Mode, endpointIds = endpoints.Select(x => x.Id).ToArray() });
        // Pending snapshots may not outlive an explicit disable, removal or inheritance change.
        var pending = await context.Set<WebhookDeliveryRecord>().Where(x => (x.Status == "Pending" || x.Status == "Sending")
            && (productId != null ? x.ProductId == productId : x.ScopeId == scope)).ToListAsync(cancellationToken);
        foreach (var delivery in pending)
        {
            if (productId is not null && delivery.ProductId != productId) continue;
            if (productId is null && delivery.ScopeId != scope) continue;
            if (!await IsActiveAsync(context, delivery, cancellationToken)) { delivery.Status = "Discarded"; delivery.Url = ""; delivery.ClaimId = null; delivery.LockedUntil = null; }
        }
        var committed = await transaction.CommitAsync(cancellationToken);
        return committed.IsFailure ? Result<WebhookSettingsDto>.Failure(committed.Errors[0]) : await GetAsync(productId, cancellationToken);
    }

    private static bool DiscordWebhookUrl(string url)
    {
        var uri = new Uri(url);
        return uri.Host.Equals("discord.com", StringComparison.OrdinalIgnoreCase) && uri.IsDefaultPort
            && uri.AbsolutePath.StartsWith("/api/webhooks/", StringComparison.Ordinal) && uri.AbsolutePath.Split('/').Length == 5;
    }

    public async Task<Result<WebhookDeliveryPageDto>> ListAsync(Guid? productId, int page, CancellationToken cancellationToken)
    {
        if (!await ExistsAsync(productId, cancellationToken)) return Result<WebhookDeliveryPageDto>.Failure(Missing());
        page = Math.Clamp(page, 1, 1000000);
        var query = context.Set<WebhookDeliveryRecord>().AsNoTracking();
        if (productId is not null) query = query.Where(x => x.ProductId == productId);
        var rows = await query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Skip((page - 1) * PageSize).Take(PageSize + 1)
            .Select(x => new WebhookDeliveryDto(x.Id, x.EndpointId, x.EndpointName, x.ProductId, x.EventType, x.Status, x.Attempts, x.CreatedAt, x.NextAttemptAt, x.LastError)).ToListAsync(cancellationToken);
        return Result<WebhookDeliveryPageDto>.Success(new(rows.Take(PageSize).ToArray(), page, rows.Count > PageSize));
    }

    public async Task<Result<WebhookActionResponse>> ActAsync(Guid? productId, WebhookActionRequest request, Guid actorId, CancellationToken cancellationToken)
    {
        if (!await ExistsAsync(productId, cancellationToken)) return Result<WebhookActionResponse>.Failure(Missing());
        await using var transaction = await unitOfWork.BeginAsync(cancellationToken);
        Guid deliveryId;
        if (request.Action == "Test")
        {
            var scope = productId ?? WebhookPolicy.GlobalScope;
            var config = await context.Set<WebhookSettingsRecord>().SingleOrDefaultAsync(x => x.Id == scope, cancellationToken);
            if (config?.Mode != "Replace") return Result<WebhookActionResponse>.Failure(Invalid("Save an enabled endpoint in Replace mode before testing it."));
            var endpoint = WebhookJson.Endpoints(config.Endpoints).SingleOrDefault(x => x.Id == request.Id && x.Enabled);
            if (endpoint is null) return Result<WebhookActionResponse>.Failure(Missing());
            var product = productId is null ? new WebhookProductDto(Guid.Empty, "test", "TechStrap") : await context.Set<ProductRecord>().Where(x => x.Id == productId).Select(x => new WebhookProductDto(x.Id, x.Key, x.Name)).SingleAsync(cancellationToken);
            var now = clock.GetUtcNow();
            var payload = new WebhookEventDto(1, Guid.CreateVersion7(), "webhook.test", now, product, new(Guid.Empty, "Test", "Webhook test notification", "Test", null, null, null, null, null));
            var delivery = WebhookCapture.NewDelivery(scope, endpoint, payload, now);
            context.Add(delivery);
            deliveryId = delivery.Id;
        }
        else
        {
            var delivery = await context.Set<WebhookDeliveryRecord>().SingleOrDefaultAsync(x => x.Id == request.Id, cancellationToken);
            if (delivery is null || (productId is not null && delivery.ProductId != productId)) return Result<WebhookActionResponse>.Failure(Missing());
            if (request.Action == "Retry" && delivery.Status == "DeadLettered")
            {
                if (!await IsActiveAsync(context, delivery, cancellationToken)) return Result<WebhookActionResponse>.Failure(Invalid("The destination is no longer enabled for this product."));
                delivery.Status = "Pending"; delivery.Attempts = 0; delivery.LastError = null; delivery.NextAttemptAt = clock.GetUtcNow();
            }
            else if (request.Action == "Discard" && delivery.Status is "Pending" or "DeadLettered") { delivery.Status = "Discarded"; delivery.Url = ""; }
            else return Result<WebhookActionResponse>.Failure(Invalid("Only dead letters can be retried; only pending deliveries or dead letters can be discarded."));
            deliveryId = delivery.Id;
        }
        Audit(actorId, AdminEventType.WebhookDeliveryAction, deliveryId, new { action = request.Action, deliveryId });
        var committed = await transaction.CommitAsync(cancellationToken);
        return committed.IsFailure ? Result<WebhookActionResponse>.Failure(committed.Errors[0]) : Result<WebhookActionResponse>.Success(new(deliveryId));
    }

    internal static async Task<bool> IsActiveAsync(TechStrapDbContext context, WebhookDeliveryRecord delivery, CancellationToken ct)
    {
        if (delivery.TicketId is { } ticketId && !await context.Set<TicketRecord>().AsNoTracking().AnyAsync(x => x.Id == ticketId && x.Subject != TechStrap.Domain.Requesters.ErasureMarker.Text, ct)) return false;
        var productConfig = context.Set<WebhookSettingsRecord>().Local.SingleOrDefault(x => x.Id == delivery.ProductId)
            ?? await context.Set<WebhookSettingsRecord>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == delivery.ProductId, ct);
        var effectiveScope = productConfig is null || productConfig.Mode == "Inherit" ? WebhookPolicy.GlobalScope : delivery.ProductId;
        if (effectiveScope != delivery.ScopeId || productConfig?.Mode == "Disabled") return false;
        var config = context.Set<WebhookSettingsRecord>().Local.SingleOrDefault(x => x.Id == effectiveScope)
            ?? await context.Set<WebhookSettingsRecord>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == effectiveScope, ct);
        return config?.Mode == "Replace" && WebhookJson.Endpoints(config.Endpoints).Any(x => x.Id == delivery.EndpointId && x.Enabled);
    }

    private void Audit(Guid actor, AdminEventType type, Guid subject, object payload) => context.Add(new AdminEventRecord
    {
        Id = Guid.CreateVersion7(), Type = type, ActorId = actor, SubjectId = subject, SubjectType = AdminSubjectType.Webhook,
        Payload = WebhookJson.Write(payload), OccurredAt = clock.GetUtcNow(),
    });
}
