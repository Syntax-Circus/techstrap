namespace TechStrap.Admin.Clients;

/// <summary>
/// The field names a 400 answer targets (<see cref="SyntaxCircus.Common.ResultError.Target"/>), exactly as the API sends them: kebab-case, not the camelCase of the JSON
/// request. A form maps an error to its input by comparing the target with one of these.
/// </summary>
public static class ApiFields
{
    // Products and branding.
    public const string Key = "key";
    public const string Name = "name";
    public const string NumberPrefix = "number-prefix";
    public const string DisplayName = "display-name";
    public const string LogoPath = "logo-path";
    public const string AccentColour = "accent-colour";
    public const string FromAddress = "from-address";
    public const string ReplyTo = "reply-to";

    // API keys.
    public const string Kind = "kind";
    public const string Label = "label";

    // Tags.
    public const string Slug = "slug";
    public const string Colour = "colour";

    // Agents.
    public const string PublicDisplayName = "public-display-name";
}
