using TechStrap.Portal.Clients;

namespace TechStrap.Portal.Tickets;

/// <summary>
/// Reads the new ticket's token out of <c>FollowUpViewUrl</c>, the absolute link the API makes for a follow-up (<c>{public url}/t/{token}</c>), so the Portal can send the visitor to its own page for it. Review Focus 1:
/// only the last path segment is used, and only if <see cref="TicketToken.TryParse"/> accepts it; the host, the scheme, a query and a fragment are all ignored, and the redirect is built by the Portal's own route builder, so
/// a visitor can never be sent to another site whatever the API's configuration or a poisoned value says. Anything that is not an absolute http(s) link with a valid last segment is no token.
/// </summary>
public static class FollowUpLink
{
    public static bool TryGetToken(string? url, out TicketToken token)
    {
        token = default;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return false;
        }

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length > 0 && TicketToken.TryParse(segments[^1], out token);
    }
}
