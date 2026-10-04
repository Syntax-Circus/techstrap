namespace TechStrap.Admin.Clients;

/// <summary>
/// The error codes components branch on. Codes that come from the API are its problem <c>type</c> (or, for a validation failure, the entries of
/// <c>errorCodes</c>); the others are produced by the API connection when the API gave no code.
/// </summary>
public static class ApiErrorCodes
{
    // Produced by the Admin.
    public const string Unauthenticated = "unauthenticated";
    public const string Forbidden = "forbidden";
    public const string NotFound = "not-found";
    public const string Conflict = "conflict";
    public const string ValidationFailed = "validation-failed";
    public const string ApiUnavailable = "api-unavailable";
    public const string ApiTimeout = "api-timeout";
    public const string UnexpectedResponse = "api-unexpected-response";
    public const string ApiError = "api-error";

    // Produced by the API.
    public const string ConcurrencyConflict = "concurrency-conflict";
    public const string TicketClosed = "ticket-closed";
    public const string InvalidStatusTransition = "invalid-status-transition";
    public const string TicketNotFound = "ticket-not-found";
    public const string AgentNotFound = "agent-not-found";
    public const string TagNotFound = "tag-not-found";
    public const string ProductNotFound = "product-not-found";
    public const string RowVersionRequired = "row-version-required";
    public const string AgentAccessRequired = "agent-access-required";
    public const string AgentInactive = "agent-inactive";
    public const string AgentEmailRequired = "agent-email-required";
    public const string AgentIdentityInvalid = "agent-identity-invalid";
    public const string AgentNotProvisioned = "agent-not-provisioned";
    public const string AdminAccessRequired = "admin-access-required";

    /// <summary>A write that failed this way may still have been applied (the answer was lost, late or unreadable): say so and offer a reload, never a bare retry.</summary>
    public static bool IsUncertainWrite(string code) => code is ApiTimeout or ApiUnavailable or UnexpectedResponse or ApiError;
}
