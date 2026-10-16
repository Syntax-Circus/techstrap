namespace TechStrap.Contracts.AdminEvents;

/// <summary>Wire names for the admin event type enum (<c>AdminEventDto.Type</c>). Contracts carries no enums (naming rule).</summary>
public static class AdminEventTypes
{
    /// <summary>Webhook settings were changed.</summary>
    public const string WebhookSettingsUpdated = "WebhookSettingsUpdated";
    /// <summary>A webhook test, retry or discard was requested.</summary>
    public const string WebhookDeliveryAction = "WebhookDeliveryAction";
    /// <summary>A product was created.</summary>
    public const string ProductCreated = "ProductCreated";
    /// <summary>A product's name, branding or active status was changed.</summary>
    public const string ProductUpdated = "ProductUpdated";
    /// <summary>A product API key was created.</summary>
    public const string ApiKeyCreated = "ApiKeyCreated";
    /// <summary>A product API key was revoked and no longer authenticates.</summary>
    public const string ApiKeyRevoked = "ApiKeyRevoked";
    /// <summary>An agent was activated or deactivated by an Admin.</summary>
    public const string AgentUpdated = "AgentUpdated";
    /// <summary>A tag was created.</summary>
    public const string TagCreated = "TagCreated";
    /// <summary>A tag's name or color was changed.</summary>
    public const string TagUpdated = "TagUpdated";
    /// <summary>A tag was deleted and removed from every ticket that carried it.</summary>
    public const string TagDeleted = "TagDeleted";
    /// <summary>A requester's personal data was erased.</summary>
    public const string RequesterErased = "RequesterErased";
    /// <summary>A ticket was permanently deleted.</summary>
    public const string TicketDeleted = "TicketDeleted";
    /// <summary>A dead-lettered outgoing email was put back in the queue for another delivery attempt.</summary>
    public const string DeadLetterRetried = "DeadLetterRetried";
    /// <summary>A dead-lettered outgoing email was discarded without being sent.</summary>
    public const string DeadLetterDiscarded = "DeadLetterDiscarded";
    /// <summary>The deployment's default theme pack was changed (D-053).</summary>
    public const string SiteSettingsUpdated = "SiteSettingsUpdated";
    /// <summary>A feature was created.</summary>
    public const string FeatureCreated = "FeatureCreated";
    /// <summary>A feature was updated.</summary>
    public const string FeatureUpdated = "FeatureUpdated";
    /// <summary>A feature was published.</summary>
    public const string FeaturePublished = "FeaturePublished";
    /// <summary>A feature was unpublished.</summary>
    public const string FeatureUnpublished = "FeatureUnpublished";
}

/// <summary>Wire names for the admin subject type enum (<c>AdminEventDto.SubjectType</c> and the <c>subjectType</c> filter). Contracts carries no enums (naming rule).</summary>
public static class AdminSubjectTypes
{
    /// <summary>The event concerns webhook settings or delivery.</summary>
    public const string Webhook = "Webhook";
    /// <summary>The event concerns a product.</summary>
    public const string Product = "Product";
    /// <summary>The event concerns a product API key.</summary>
    public const string ApiKey = "ApiKey";
    /// <summary>The event concerns an agent.</summary>
    public const string Agent = "Agent";
    /// <summary>The event concerns a tag.</summary>
    public const string Tag = "Tag";
    /// <summary>The event concerns a requester.</summary>
    public const string Requester = "Requester";
    /// <summary>The event concerns a ticket.</summary>
    public const string Ticket = "Ticket";
    /// <summary>The event concerns a queued outgoing email.</summary>
    public const string EmailOutbox = "EmailOutbox";
    /// <summary>The event concerns the deployment-wide site settings (D-053).</summary>
    public const string SiteSettings = "SiteSettings";
    /// <summary>The event concerns a curated feature.</summary>
    public const string Feature = "Feature";
}
