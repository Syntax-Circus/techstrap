using TechStrap.Admin.Features.Settings;

namespace TechStrap.Admin.Features.Ops.DeadLetters;

/// <summary>
/// The copy of the failed-emails page. A "dead letter" is an email that used up its retries. The last error is shown as a plain category, never as the raw text, and a long or unknown text is cut short.
/// </summary>
public static class DeadLettersCopy
{
    public const int PageSize = 25;
    public const int MaxErrorLength = 80;

    public const string Heading = "Failed emails";
    public const string Intro = "Emails TechStrap gave up sending after several tries. Retry puts one back in the queue; Discard stops trying for good.";
    public const string Loading = "Loading failed emails";
    public const string LoadFailed = "Couldn't load the failed emails.";
    public const string Empty = "No failed emails";

    public const string ColumnRecipient = "Recipient";
    public const string ColumnEmail = "Email";
    public const string ColumnTicket = "Ticket";
    public const string ColumnAttempts = "Tries";
    public const string ColumnError = "Last error";
    public const string ColumnCreated = "Created";
    public const string ColumnActions = "Actions";
    public const string OpenTicket = "Open ticket";
    public const string NoTicket = "\u2014";
    public const string Retry = "Retry";
    public const string Discard = "Discard";
    public const string ReloadList = "Reload list";

    public const string DiscardTitle = "Discard this failed email?";
    public const string DiscardConfirm = "Discard email";
    public const string DiscardBody = "TechStrap will stop trying to send it, and it will not be sent. This can't be undone.";
    public const string DiscardUncertain = "The discard may have gone through. Reload the list to check before you try again.";
    public const string AlreadyHandled = "That email was already retried or discarded.";
    public const string Gone = "That email isn't in the list any more.";

    public const string ErrorNone = "No error recorded";
    public const string ErrorTransient = "Temporary SMTP problem";
    public const string ErrorPermanent = "SMTP refused the email";
    public const string ErrorAuthentication = "SMTP sign-in failed";
    public const string ErrorTimeout = "SMTP timed out";
    public const string ErrorUnknown = "Unknown SMTP error";

    /// <summary>The words for the API's last-error category (<c>smtp-transient</c> and its siblings). Anything else is free text from the API: it is shortened and shown as text, never as markup.</summary>
    public static string ErrorLabel(string? lastError) => CategoryOf(lastError) switch
    {
        null or "" => ErrorNone,
        "smtp-transient" => ErrorTransient,
        "smtp-permanent" => ErrorPermanent,
        "smtp-authentication" => ErrorAuthentication,
        "smtp-timeout" => ErrorTimeout,
        "smtp-unknown" => ErrorUnknown,
        _ => SafeText.Clip(lastError, MaxErrorLength),
    };

    // The API may append a reason to the category ("smtp-transient; worker lease expired"): the category before the first semicolon is what the page words.
    private static string? CategoryOf(string? lastError)
    {
        var first = lastError?.Split(';')[0].Trim();
        return first is "smtp-transient" or "smtp-permanent" or "smtp-authentication" or "smtp-timeout" or "smtp-unknown" ? first : lastError;
    }

    public static string KindLabel(string kind) => EmailKinds.Label(kind);

    public static string Retried(string kind) => $"Queued a retry for the {EmailKinds.Label(kind).ToLowerInvariant()} email";

    public static string Discarded(string kind) => $"Discarded the {EmailKinds.Label(kind).ToLowerInvariant()} email";

    public static string DiscardSubject(string kind, string recipient) => $"{EmailKinds.Label(kind)} to {recipient}.";

    public static string RetryFailed(string reason) => $"Couldn't retry the email. Nothing was changed. {reason}";

    public static string DiscardFailed(string reason) => $"Couldn't discard the email. Nothing was changed. {reason}";

    public const string RetryUncertain = "The retry may have been queued. Reload the list to check before you try again.";
}
