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

    // Knowledge base. The publish check names title, slug, body or category; a category has a name, a description and a sort order; an upload has its file.
    public const string Title = "title";
    public const string Summary = "summary";
    public const string Body = "body";
    public const string Category = "category";

    /// <summary>The name the API gives a category error on a request (kb-category-not-found, kb-category-scope-mismatch): the request property, not the publish check's <c>category</c>.</summary>
    public const string CategoryId = "categoryId";
    public const string Description = "description";
    public const string SortOrder = "sort-order";
    public const string File = "file";
}
