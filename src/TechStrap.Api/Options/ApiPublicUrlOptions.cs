namespace TechStrap.Api.Options;

/// <summary>
/// The Api's own public address (<c>TECHSTRAP_API_PUBLIC_URL</c>, D-044): the origin readers of the portal and the Admin use to load KB images from
/// <c>{url}/kb-images/{name}</c>. Required outside Development, where blank falls back to the origin of the request that uploads the image.
/// </summary>
public sealed class ApiPublicUrlOptions
{
    public const string Key = "TECHSTRAP_API_PUBLIC_URL";

    public string PublicUrl { get; set; } = string.Empty;

    /// <summary>
    /// True when the value is acceptable. Blank is acceptable only in Development. A value must be an absolute http or https URL with a host
    /// and no user info, query or fragment (a path prefix is allowed, for an Api published under one).
    /// </summary>
    public static bool IsAcceptable(string? value, bool isDevelopment)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return isDevelopment;
        }

        return Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
            && uri.Scheme is "http" or "https"
            && !string.IsNullOrEmpty(uri.Host)
            && string.IsNullOrEmpty(uri.UserInfo)
            && string.IsNullOrEmpty(uri.Query)
            && string.IsNullOrEmpty(uri.Fragment);
    }
}
