namespace TechStrap.Portal.Clients;

/// <summary>
/// The error codes pages branch on. A 400 keeps the codes the API sends (the entries of <c>errorCodes</c>, one per field); every other code is decided by the Portal from the status, so a page never
/// depends on the API's own wording and a not-found is one code whatever the API called it.
/// </summary>
public static class ApiErrorCodes
{
    public const string ValidationFailed = "validation-failed";
    public const string NotFound = "not-found";
    public const string PayloadTooLarge = "payload-too-large";
    public const string UnsupportedMediaType = "unsupported-media-type";
    public const string RateLimited = "rate-limited";

    /// <summary>The API could not be reached or failed (any 5xx, a transport error or a timeout).</summary>
    public const string ApiUnavailable = "api-unavailable";

    public const string UnexpectedResponse = "api-unexpected-response";

    /// <summary>Any other status (401, 403, 409 and so on): the public routes do not answer them, so a page treats it as a failure.</summary>
    public const string ApiError = "api-error";
}
