namespace TechStrap.Hosting.Security;

/// <summary>
/// The Content-Security-Policy of each kind of TechStrap host (D-042). The policies are plain strings built by <see cref="CspBuilder"/>; the hosts pass them to
/// <c>AddTechStrapWebHost</c> or <c>AddTechStrapSecurityHeaders</c>.
/// </summary>
public static class TechStrapCsp
{
    private const string Self = "'self'";
    private const string None = "'none'";

    /// <summary>
    /// The policy for a Blazor Server host that serves HTML (Admin, Portal). Scripts are strict: only files of this origin, so no inline script, no <c>eval</c>, no other
    /// host. Styles are strict too (<c>style-src 'self'</c>), with one deliberate relaxation: <c>style-src-attr 'unsafe-inline'</c>. Blazor writes <c>style="..."</c> attributes, and the
    /// product colours (<c>TagChip</c>, <c>AccentPreview</c>, the Portal's accent scope) are arbitrary validated hex values that classes cannot cover, so inline style
    /// attributes are allowed while style elements and stylesheets stay same-origin. Attribute styles cannot run script.
    /// </summary>
    /// <param name="formActionOrigins">
    /// Origins a form may submit or redirect to besides this one. The Admin passes its identity provider's origin: the sign-in link and the sign-out form answer with a
    /// redirect to the provider, and Chromium applies <c>form-action</c> to the redirects that follow a form submission. Null, blank and non-http(s) entries are ignored.
    /// </param>
    /// <param name="allowLoopbackImages">
    /// Development only: lets a product logo on <c>http://localhost</c> or <c>http://127.0.0.1</c> show in the branding preview (the logo rule accepts that form, D-041).
    /// A real browser never loads a customer's loopback address, so it is not needed or allowed elsewhere.
    /// </param>
    public static string ForBlazorApp(IEnumerable<string?>? formActionOrigins = null, bool allowLoopbackImages = false)
    {
        var images = new List<string> { Self, "https:", "data:" };
        if (allowLoopbackImages)
        {
            images.AddRange(["http://localhost:*", "http://127.0.0.1:*"]);
        }

        // img-src https: because a product logo is an https URL on any host (D-041); data: because the compiled Bootstrap CSS draws the form-select arrow, the checkbox tick and
        // the close icon as data: SVG background images (CspStyleTests proves every url() in app.css is covered). connect-src 'self' also covers the circuit's WebSocket in
        // browsers that implement CSP 3 (Chromium and Firefox); see ADMIN-APP.md for the browser check. font-src and the rest are same-origin: the fonts are self-hosted.
        var policy = new CspBuilder()
            .Directive("default-src", Self)
            .Directive("script-src", Self)
            .Directive("style-src", Self)
            .Directive("style-src-attr", "'unsafe-inline'")
            .Directive("img-src", [.. images])
            .Directive("connect-src", Self)
            .Directive("font-src", Self)
            .Directive("object-src", None)
            .Directive("frame-ancestors", None)
            .Directive("base-uri", Self)
            .Directive("form-action", Self);
        foreach (var origin in (formActionOrigins ?? []).Select(OriginOf).OfType<string>())
        {
            policy.Allow("form-action", origin);
        }

        return policy.Build();
    }

    /// <summary>
    /// The policy for the API, which serves JSON and file downloads and never a page: nothing may load, frame or submit anything. Downloads get <c>sandbox</c> appended by
    /// the attachment middleware.
    /// </summary>
    public static string ForApi() => new CspBuilder()
        .Directive("default-src", None)
        .Directive("base-uri", None)
        .Directive("form-action", None)
        .Directive("frame-ancestors", None)
        .Build();

    /// <summary>
    /// <c>scheme://host[:port]</c> of an absolute http or https URL (the port only when it is not the scheme's default), or null for anything else: blank text, a relative
    /// path, another scheme (<c>javascript:</c>), a URL with user info. Used to turn a configured identity-provider authority into a CSP source.
    /// </summary>
    public static string? OriginOf(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)
            || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
            || !string.IsNullOrEmpty(uri.UserInfo)
            || HasUserInfoMarker(url.Trim())
            || string.IsNullOrEmpty(uri.Host))
        {
            return null;
        }

        // IdnHost is the ASCII (punycode) form of the host, so a non-ASCII authority still gives a source CspBuilder accepts and a browser matches.
        var host = uri.IdnHost.Contains(':', StringComparison.Ordinal) && !uri.IdnHost.StartsWith('[') ? $"[{uri.IdnHost}]" : uri.IdnHost;
        return $"{uri.Scheme}://{host}{(uri.IsDefaultPort ? string.Empty : ":" + uri.Port)}";
    }

    // "https://@idp.test/" has an empty user info, which Uri.UserInfo reports as empty: an "@" in the authority part is refused whatever precedes it.
    private static bool HasUserInfoMarker(string url)
    {
        var start = url.IndexOf("://", StringComparison.Ordinal) + 3;
        var end = url.IndexOfAny(['/', '?', '#', '\\'], start);
        return url[start..(end < 0 ? url.Length : end)].Contains('@', StringComparison.Ordinal);
    }
}
