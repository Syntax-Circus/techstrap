namespace TechStrap.Contracts.Intake;

/// <summary>Where the intake API is mapped. Relative with no leading slash, so the SDK can append it to a base address that carries a path.</summary>
public static class IntakeRoutes
{
    /// <summary>The ticket intake route, <c>api/intake/tickets</c>: <c>POST</c> submits a ticket.</summary>
    public const string Tickets = "api/intake/tickets";
}
