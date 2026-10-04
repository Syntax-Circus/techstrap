namespace TechStrap.Contracts.Branding;

/// <summary>
/// The branding field rules the Admin editor applies before it submits, so an agent sees the error at the field. The API stays the authority: the Domain
/// repeats each rule (<c>Guard.Colour</c>, <c>Guard.OptionalImageUrl</c>) and a parity test in Application.Tests keeps the two in step, because Domain cannot reference Contracts.
/// </summary>
public static class BrandingRules
{
    /// <summary>A hex colour, <c>#RRGGBB</c>, either case. The API stores it upper-case.</summary>
    public const string ColourPattern = "^#[0-9A-Fa-f]{6}$";

    /// <summary>The longest logo URL, the same limit as the Domain (<c>DomainLimits.UrlMaxLength</c>).</summary>
    public const int LogoUrlMaxLength = 500;

    /// <summary>
    /// True for a blank value (no logo) and for an absolute <c>https</c> URL with a host and no user info. <c>http</c> is accepted only for the
    /// loopback hosts <c>localhost</c> and <c>127.0.0.1</c>, so a developer can serve a logo from a local machine. Everything else, including
    /// <c>javascript:</c>, <c>data:</c>, <c>file:</c>, relative paths and protocol-relative URLs, is refused, because the logo becomes an image source in
    /// customer emails and on the portal.
    /// </summary>
    public static bool IsAcceptableLogoUrl(string? value)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return true;
        }

        if (text.Length > LogoUrlMaxLength || text.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
        {
            return false;
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.UserInfo.Length > 0 || uri.Host.Length == 0)
        {
            return false;
        }

        return uri.Scheme == Uri.UriSchemeHttps
            || (uri.Scheme == Uri.UriSchemeHttp && uri.Host is "localhost" or "127.0.0.1");
    }
}
