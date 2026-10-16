namespace TechStrap.Contracts.Webhooks;

/// <summary>A destination edit. Null URL retains an existing secret; reads never return it.</summary>
public sealed record WebhookEndpointRequest
{
    /// <summary>Endpoint configuration value.</summary>
    public Guid Id { get; init; }
    /// <summary>Endpoint configuration value.</summary>
    public string Name { get; init; } = "Webhook";
    /// <summary>Endpoint configuration value.</summary>
    public string? Url { get; init; }
    /// <summary>Endpoint configuration value.</summary>
    public string Format { get; init; } = "Discord";
    /// <summary>Endpoint configuration value.</summary>
    public bool Enabled { get; init; } = true;
    /// <summary>Endpoint configuration value.</summary>
    public bool TicketCreated { get; init; } = true;
    /// <summary>Endpoint configuration value.</summary>
    public bool FeatureCreated { get; init; } = true;
    /// <summary>Endpoint configuration value.</summary>
    public string[] TicketTypes { get; init; } = ["Support", "FeatureRequest"];
    /// <summary>Endpoint configuration value.</summary>
    public string[] TicketStatuses { get; init; } = [];
    /// <summary>Endpoint configuration value.</summary>
    public string[] FeatureStatuses { get; init; } = [];
}

/// <summary>Only the hostname is displayed; path and query may contain credentials.</summary>
public sealed record WebhookEndpointDto(WebhookEndpointRequest Settings, string Destination);
/// <summary>Webhook configuration or delivery contract.</summary>
public sealed record WebhookSettingsDto(string Mode, long Version, IReadOnlyList<WebhookEndpointDto> Endpoints, IReadOnlyList<WebhookEndpointDto> EffectiveEndpoints);
/// <summary>Webhook configuration or delivery contract.</summary>
public sealed record UpdateWebhookSettingsRequest(string Mode, long Version, IReadOnlyList<WebhookEndpointRequest> Endpoints);
/// <summary>Webhook configuration or delivery contract.</summary>
public sealed record WebhookDeliveryDto(Guid Id, Guid EndpointId, string EndpointName, Guid ProductId, string EventType, string Status, int Attempts, DateTimeOffset CreatedAt, DateTimeOffset NextAttemptAt, string? LastError);
/// <summary>Webhook configuration or delivery contract.</summary>
public sealed record WebhookDeliveryPageDto(IReadOnlyList<WebhookDeliveryDto> Items, int Page, bool HasMore);
/// <summary>Webhook configuration or delivery contract.</summary>
public sealed record WebhookActionRequest(string Action, Guid Id);
/// <summary>Webhook configuration or delivery contract.</summary>
public sealed record WebhookActionResponse(Guid Id);

/// <summary>Version 1 public webhook envelope. Contains no requester data or bearer links.</summary>
public sealed record WebhookEventDto(int SchemaVersion, Guid EventId, string EventType, DateTimeOffset OccurredAt, WebhookProductDto Product, WebhookEntityDto Entity);
/// <summary>Webhook configuration or delivery contract.</summary>
public sealed record WebhookProductDto(Guid Id, string Key, string Name);
/// <summary>Webhook configuration or delivery contract.</summary>
public sealed record WebhookEntityDto(Guid Id, string Type, string Title, string Status, string? PreviousStatus, string? Number, string? TicketType, string? PublicationState, string? AdminUrl);
