using SyntaxCircus.Common;

namespace TechStrap.Application.Live;

/// <summary>Live-update outcomes. The hub shows an agent only these fixed messages; the listener logs the code only.</summary>
internal static class LiveErrors
{
    public static class Codes
    {
        public const string PresenceInvalid = "presence-invalid";
        public const string PresenceNotJoined = "presence-not-joined";
        public const string TicketNotFound = "ticket-not-found";
        public const string ChangeInvalid = "ticket-change-invalid";
        public const string ChangeTooLarge = "ticket-change-too-large";
        public const string RelayFailed = "ticket-change-relay-failed";
    }

    public static ResultError PresenceInvalid() =>
        new(Codes.PresenceInvalid, "That presence update is not valid.", ResultErrorKind.Validation);

    public static ResultError PresenceNotJoined() =>
        new(Codes.PresenceNotJoined, "Open the ticket first.", ResultErrorKind.Validation);

    public static ResultError TicketNotFound() =>
        new(Codes.TicketNotFound, "That ticket does not exist.", ResultErrorKind.NotFound);

    public static ResultError ChangeInvalid() =>
        new(Codes.ChangeInvalid, "That ticket change is not valid.", ResultErrorKind.Validation);

    public static ResultError ChangeTooLarge() =>
        new(Codes.ChangeTooLarge, "That ticket change is too large.", ResultErrorKind.Validation);

    public static ResultError RelayFailed() =>
        new(Codes.RelayFailed, "The ticket change could not be passed on.", ResultErrorKind.Failure);
}
