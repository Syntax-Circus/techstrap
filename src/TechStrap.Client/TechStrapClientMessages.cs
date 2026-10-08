namespace TechStrap.Client;

/// <summary>Fixed, user-safe copy for the errors the client reports. Server text, exception text and hosts never reach a message, except a 400's own validation text.</summary>
internal static class TechStrapClientMessages
{
    public const string Invalid = "The request was not accepted. Check the values and try again.";
    public const string InvalidApiKey = "The API key was not accepted.";
    public const string PayloadTooLarge = "The request was too large.";
    public const string UnsupportedMediaType = "The request content type is not supported.";
    public const string RateLimited = "Too many requests. Try again later.";
    public const string ApiUnavailable = "The support service is unavailable. Try again later.";
    public const string UnexpectedResponse = "The support service sent a response that could not be understood.";
    public const string ApiError = "The support service reported an error.";
}
