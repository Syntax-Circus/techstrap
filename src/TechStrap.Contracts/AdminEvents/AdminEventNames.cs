namespace TechStrap.Contracts.AdminEvents;

/// <summary>Wire names for the admin event type enum (<c>AdminEventDto.Type</c>). Contracts carries no enums (naming rule).</summary>
public static class AdminEventTypes
{
    public const string ProductCreated = "ProductCreated";
    public const string ProductUpdated = "ProductUpdated";
    public const string ApiKeyCreated = "ApiKeyCreated";
    public const string ApiKeyRevoked = "ApiKeyRevoked";
    public const string AgentUpdated = "AgentUpdated";
    public const string TagCreated = "TagCreated";
    public const string TagUpdated = "TagUpdated";
    public const string TagDeleted = "TagDeleted";
    public const string RequesterErased = "RequesterErased";
    public const string TicketDeleted = "TicketDeleted";
    public const string DeadLetterRetried = "DeadLetterRetried";
    public const string DeadLetterDiscarded = "DeadLetterDiscarded";
}

/// <summary>Wire names for the admin subject type enum (<c>AdminEventDto.SubjectType</c> and the <c>subjectType</c> filter). Contracts carries no enums (naming rule).</summary>
public static class AdminSubjectTypes
{
    public const string Product = "Product";
    public const string ApiKey = "ApiKey";
    public const string Agent = "Agent";
    public const string Tag = "Tag";
    public const string Requester = "Requester";
    public const string Ticket = "Ticket";
    public const string EmailOutbox = "EmailOutbox";
}
