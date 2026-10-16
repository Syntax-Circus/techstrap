namespace TechStrap.Infrastructure.Persistence.Records;

internal sealed class WebhookSettingsRecord
{
    public Guid Id { get; set; }
    public string Mode { get; set; } = "Inherit";
    public string Endpoints { get; set; } = "[]";
    public long Version { get; set; }
}

internal sealed class WebhookDeliveryRecord
{
    public Guid Id { get; set; }
    public Guid EventId { get; set; }
    public Guid ScopeId { get; set; }
    public Guid ProductId { get; set; }
    public Guid? TicketId { get; set; }
    public Guid EndpointId { get; set; }
    public string EndpointName { get; set; } = "";
    public string Url { get; set; } = "";
    public string Format { get; set; } = "";
    public string Payload { get; set; } = "{}";
    public string EventType { get; set; } = "";
    public string Status { get; set; } = "Pending";
    public int Attempts { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
    public Guid? ClaimId { get; set; }
    public string? LastError { get; set; }
}
