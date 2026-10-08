namespace TechStrap.Client;

/// <summary>Stable, machine-readable codes the client reports for a failed call. Match on these, not on message text.</summary>
public static class TechStrapClientErrorCodes
{
    /// <summary>The API rejected the key (missing, unknown, revoked or not allowed for this product).</summary>
    public const string InvalidApiKey = "invalid-api-key";

    /// <summary>The API rejected the request body as invalid.</summary>
    public const string ValidationFailed = "validation-failed";

    /// <summary>The request body was larger than the API accepts.</summary>
    public const string PayloadTooLarge = "payload-too-large";

    /// <summary>The API does not accept the content type that was sent.</summary>
    public const string UnsupportedMediaType = "unsupported-media-type";

    /// <summary>The API is rate limiting this caller; retry later.</summary>
    public const string RateLimited = "rate-limited";

    /// <summary>The API could not be reached or answered with a server error after the retries were spent.</summary>
    public const string ApiUnavailable = "api-unavailable";

    /// <summary>The API answered with something the client does not understand.</summary>
    public const string UnexpectedResponse = "api-unexpected-response";

    /// <summary>The API reported an error that has no more specific code.</summary>
    public const string ApiError = "api-error";
}
