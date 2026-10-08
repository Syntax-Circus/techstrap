namespace TechStrap.Contracts.AdminEvents;

/// <summary>Wire names for the admin event type enum (<c>AdminEventDto.Type</c>). Contracts carries no enums (naming rule).</summary>
public static class AdminEventTypes
{
    /// <summary>A product was created.</summary>
    public const string ProductCreated = "ProductCreated";
    /// <summary>A product's settings were changed.</summary>
    public const string ProductUpdated = "ProductUpdated";
    /// <summary>A product API key was created.</summary>
    public const string ApiKeyCreated = "ApiKeyCreated";
    /// <summary>A product API key was revoked and no longer authenticates.</summary>
    public const string ApiKeyRevoked = "ApiKeyRevoked";
    /// <summary>An agent was activated or deactivated by an Admin.</summary>
    public const string AgentUpdated = "AgentUpdated";
    /// <summary>A tag was created.</summary>
    public const string TagCreated = "TagCreated";
    /// <summary>A tag's name or colour was changed.</summary>
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
}

/// <summary>Wire names for the admin subject type enum (<c>AdminEventDto.SubjectType</c> and the <c>subjectType</c> filter). Contracts carries no enums (naming rule).</summary>
public static class AdminSubjectTypes
{
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
}
