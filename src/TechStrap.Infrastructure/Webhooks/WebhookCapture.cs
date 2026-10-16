using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TechStrap.Application.Tickets.Notifications;
using TechStrap.Application.Webhooks;
using TechStrap.Contracts.Webhooks;
using TechStrap.Domain.Tickets;
using TechStrap.Infrastructure.Persistence;
using TechStrap.Infrastructure.Persistence.Records;

namespace TechStrap.Infrastructure.Webhooks;

/// <summary>Runs before SaveChanges inside the caller's transaction, including worker mutations.</summary>
internal sealed class WebhookCapture(TechStrapDbContext context, IOptions<AdminLinkOptions> links, TimeProvider clock)
{
    internal async Task StageAsync(CancellationToken ct)
    {
        context.ChangeTracker.DetectChanges();
        var tickets = context.ChangeTracker.Entries<TicketRecord>().Where(e => e.State is EntityState.Added or EntityState.Modified).Select(e => e.Entity).ToDictionary(x => x.Id);
        var events = context.ChangeTracker.Entries<TicketEventRecord>().Where(e => e.State == EntityState.Added && e.Entity.Type is TicketEventType.Created or TicketEventType.StatusChanged).Select(e => e.Entity).ToArray();
        var features = context.ChangeTracker.Entries<FeatureRecord>().Where(e => e.State == EntityState.Added || (e.State == EntityState.Modified && e.Property(x => x.ProgressStatus).OriginalValue != e.Entity.ProgressStatus)).ToArray();
        if (events.Length == 0 && features.Length == 0) return;

        var settings = await context.Set<WebhookSettingsRecord>().AsNoTracking().ToDictionaryAsync(x => x.Id, ct);
        if (settings.Count == 0) return;
        foreach (var change in events)
        {
            if (!tickets.TryGetValue(change.TicketId, out var ticket)) continue;
            string? previous = null;
            var status = ticket.Status.ToString();
            if (change.Type == TicketEventType.StatusChanged)
            {
                using var json = JsonDocument.Parse(change.Payload);
                previous = json.RootElement.GetProperty("from").GetString();
                status = json.RootElement.GetProperty("to").GetString()!;
                if (previous == status) continue;
            }
            await StageEventAsync(settings, change.Id, change.Type == TicketEventType.Created ? "ticket.created" : "ticket.status_changed", change.OccurredAt, ticket.ProductId,
                new(ticket.Id, "Ticket", ticket.Subject, status, previous, ticket.Number, ticket.Type.ToString(), null, links.Value.TicketLink(ticket.Number)), ct);
        }
        foreach (var entry in features)
        {
            var feature = entry.Entity;
            var created = entry.State == EntityState.Added;
            var adminUrl = string.IsNullOrWhiteSpace(links.Value.PublicUrl) ? null : $"{links.Value.PublicUrl.TrimEnd('/')}/features/{feature.Id}";
            await StageEventAsync(settings, Guid.CreateVersion7(), created ? "feature.created" : "feature.status_changed", feature.UpdatedAt, feature.ProductId,
                new(feature.Id, "Feature", feature.Title, feature.ProgressStatus.ToString(), created ? null : entry.Property(x => x.ProgressStatus).OriginalValue.ToString(), null, null, feature.PublicationState.ToString(), adminUrl), ct);
        }
    }

    private async Task StageEventAsync(Dictionary<Guid, WebhookSettingsRecord> settings, Guid eventId, string eventType, DateTimeOffset occurredAt, Guid productId, WebhookEntityDto entity, CancellationToken ct)
    {
        var scopeId = productId;
        settings.TryGetValue(productId, out var config);
        if (config is null || config.Mode == "Inherit") { scopeId = WebhookPolicy.GlobalScope; settings.TryGetValue(scopeId, out config); }
        if (config is null || config.Mode == "Disabled") return;
        var endpoints = WebhookJson.Endpoints(config.Endpoints).Where(x => WebhookPolicy.Matches(x, eventType, entity.TicketType, entity.Status)).ToArray();
        if (endpoints.Length == 0) return;
        var product = await context.Set<ProductRecord>().FindAsync([productId], ct);
        if (product is null) return;
        var payload = new WebhookEventDto(1, eventId, eventType, occurredAt, new(product.Id, product.Key, product.Name), entity);
        foreach (var endpoint in endpoints) context.Set<WebhookDeliveryRecord>().Add(NewDelivery(scopeId, endpoint, payload, clock.GetUtcNow()));
    }

    internal static WebhookDeliveryRecord NewDelivery(Guid scopeId, WebhookEndpointRequest endpoint, WebhookEventDto payload, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(), EventId = payload.EventId, ScopeId = scopeId, ProductId = payload.Product.Id,
        TicketId = payload.Entity.Type == "Ticket" ? payload.Entity.Id : null,
        EndpointId = endpoint.Id, EndpointName = endpoint.Name, Url = endpoint.Url!, Format = endpoint.Format,
        Payload = WebhookJson.Write(payload), EventType = payload.EventType, CreatedAt = now, NextAttemptAt = now,
    };
}
