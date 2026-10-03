namespace TechStrap.Contracts.Http;

/// <summary>Header names shared by the API, Portal and SDK (02-ARCHITECTURE section 4).</summary>
public static class HeaderNames
{
    public const string ApiKey = "X-Api-Key";
    public const string TicketToken = "X-Ticket-Token";
    public const string IdempotencyKey = "Idempotency-Key";
}
