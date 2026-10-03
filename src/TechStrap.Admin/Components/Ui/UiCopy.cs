namespace TechStrap.Admin.Components.Ui;

/// <summary>
/// Copy for the blocking-failure states, defined once so the running app and the style guide cannot drift apart.
/// Voice rules (docs/BRAND.md section 3): plain cause plus next step, sentence case, no humour, no exclamation marks.
/// </summary>
public static class UiCopy
{
    public const string ReconnectFirst = "Connection lost. Reconnecting...";
    public const string ReconnectRetryingBefore = "Still offline. Trying again in ";
    public const string ReconnectRetryingAfter = " seconds.";
    public const string ReconnectFailed = "Could not reconnect. Your changes are still here; retry, or reload the page.";
    public const string ReconnectPaused = "This session was paused by the server.";
    public const string ReconnectResumeFailed = "Could not resume the session. Retry, or reload the page.";
    public const string RetryLabel = "Retry";
    public const string ResumeLabel = "Resume";

    public const string ErrorTitle = "Couldn't load this screen.";
    public const string ErrorDescription = "Something went wrong while drawing it. Try again, and tell an admin if it keeps happening.";
    public const string ErrorHomeLabel = "Back to the start";
}
