using SyntaxCircus.Common;
using TechStrap.Application.Persistence;

namespace TechStrap.Application.Tickets;

/// <summary>Shared ticket outcomes. The messages are shown to agents: each gives the plain cause and the next step (BRAND.md section 3).</summary>
internal static class TicketErrors
{
    public static ResultError NotFound() => new("ticket-not-found", "That ticket does not exist.", ResultErrorKind.NotFound);

    public static ResultError RowVersionRequired() =>
        new("row-version-required", "Send the ticket's rowVersion with this change.", ResultErrorKind.Validation, "rowVersion");

    public static ResultError Stale() =>
        new(PersistenceErrorCodes.ConcurrencyConflict, "Someone else changed this ticket. Reload it and try again.", ResultErrorKind.Conflict);

    public static ResultError Invalid(string target, string code, string message) => new(code, message, ResultErrorKind.Validation, target);

    public static ResultError AgentNotFound() => new("agent-not-found", "That agent does not exist.", ResultErrorKind.NotFound);

    public static ResultError AttachmentNotFound() => new("attachment-not-found", "That attachment does not exist.", ResultErrorKind.NotFound);
}
