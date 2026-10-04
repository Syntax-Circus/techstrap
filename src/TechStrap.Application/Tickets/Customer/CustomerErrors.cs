using SyntaxCircus.Common;
using TechStrap.Domain.Requesters;
using TechStrap.Domain.Tickets;

namespace TechStrap.Application.Tickets.Customer;

internal static class CustomerErrors
{
    public const string NotFoundCode = "not-found";

    /// <summary>The one failure every customer route returns for any access problem (D-038). Never vary the text.</summary>
    public static ResultError NotFound() => new(NotFoundCode, "Not found.", ResultErrorKind.NotFound);

    public static ResultError EmailInvalid() => new("email-invalid", "Enter a valid email address.", ResultErrorKind.Validation, "email");

    public static ResultError ReplyConflict() =>
        new("reply-conflict", "Your reply could not be saved. Please try again.", ResultErrorKind.Conflict);
}

internal sealed record CustomerContext(TicketAccessToken Token, Ticket Ticket, Requester Requester);
