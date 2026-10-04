namespace TechStrap.Admin.Auth;

/// <summary>
/// The only URLs the sign-in flow will send an agent back to: local paths of this app. Anything else (another host, a protocol-relative
/// "//host", a backslash trick, a control or non-ASCII character, more than 2048 characters, or the sign-in routes themselves) becomes the home page, so the returnUrl parameter cannot
/// be used as an open redirect.
/// </summary>
public static class LocalReturnUrl
{
    public const string Home = "/";

    private const int MaxLength = 2048;

    public static string Sanitize(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl)
            || returnUrl[0] != '/'
            || (returnUrl.Length > 1 && returnUrl[1] is '/' or '\\')
            || returnUrl.Length > MaxLength
            || returnUrl.Any(c => c is < '!' or > '~')
            || returnUrl.Contains('\\'))
        {
            return Home;
        }

        if (returnUrl.StartsWith(AdminAuthentication.SignInPath, StringComparison.OrdinalIgnoreCase)
            || returnUrl.StartsWith(AdminAuthentication.SignOutPath, StringComparison.OrdinalIgnoreCase))
        {
            return Home;
        }

        return returnUrl;
    }
}
