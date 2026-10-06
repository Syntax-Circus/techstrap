namespace TechStrap.Portal.Clients;

/// <summary>
/// The fixed, user-safe sentences for what can go wrong between the Portal and the API. They never carry exception text, a host, a port or anything the API said (the API's own detail can name
/// a table or a limit), and they never name the API: a visitor does not know there is one. Pages show these as they are.
/// </summary>
public static class ProblemCopy
{
    public const string Invalid = "Some of what you entered needs another look.";
    public const string NotFound = "We could not find that.";
    public const string PayloadTooLarge = "That is too large to send. Remove a file or two and try again.";
    public const string UnsupportedMediaType = "That could not be sent in that form. Reload the page and try again.";
    public const string RateLimited = "You have sent a lot in a short time. Wait a minute and try again.";
    public const string ReplyConflict = "Your reply could not be saved this time. Your text is still here: send it again.";
    public const string ApiUnavailable = "We could not reach our support system. Try again in a moment.";
    public const string ApiError = "Something went wrong on our side. Try again in a moment.";
    public const string UnexpectedResponse = "We got an answer we did not expect. Try again in a moment.";
}
