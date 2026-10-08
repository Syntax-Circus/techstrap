namespace TechStrap.Contracts.Http;

/// <summary>Header names shared by the API, Portal and SDK (02-ARCHITECTURE section 4).</summary>
public static class HeaderNames
{
    /// <summary>Carries the product API key on intake requests: <c>X-Api-Key</c>.</summary>
    public const string ApiKey = "X-Api-Key";
    /// <summary>Carries the ticket access token that lets a requester view or reply to one ticket: <c>X-Ticket-Token</c>.</summary>
    public const string TicketToken = "X-Ticket-Token";
    /// <summary>Carries the caller's idempotency key on ticket submission: <c>Idempotency-Key</c>. The API creates at most one ticket per key.</summary>
    public const string IdempotencyKey = "Idempotency-Key";
}
