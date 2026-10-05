using System.Text.RegularExpressions;
using TechStrap.Admin.Clients;
using TechStrap.Contracts.Kb;

namespace TechStrap.Admin.Features.Kb;

/// <summary>The limits the editor checks before it sends. They are the server's own (the Domain's), so a message here is never stricter than the API.</summary>
public static class KbEditorLimits
{
    public const int TitleMaxLength = 200;
    public const int SummaryMaxLength = 500;
    public const int SlugMaxLength = 80;

    /// <summary>The longest article body. The API says the preview limit equals it, so a body that can be saved can be previewed.</summary>
    public const int BodyMaxLength = KbLimits.MaxPreviewChars;
}

/// <summary>
/// The form model of the article editor. It holds exactly what is on screen, checks it with the server's rules and builds the requests. <see cref="Version"/> is the one the article was loaded with:
/// it travels with every update, so a stale save is a 409 and never an overwrite. The product and the slug are chosen once, when the article is created; after that they are only shown.
/// </summary>
internal sealed partial class KbArticleEditorViewModel
{
    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$")]
    private static partial Regex SlugPattern();

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NotSlugCharacters();

    /// <summary>The article's id once it exists; empty while it is being created.</summary>
    public Guid Id { get; set; }

    public Guid? ProductId { get; set; }

    public Guid? CategoryId { get; set; }

    public string Slug { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    /// <summary>One of <see cref="KbArticleStatuses"/>; Draft for an article that has not been created yet.</summary>
    public string Status { get; set; } = KbArticleStatuses.Draft;

    public uint Version { get; set; }

    public DateTimeOffset? PublishedAt { get; set; }

    public bool IsPublished => Status == KbArticleStatuses.Published;

    public static KbArticleEditorViewModel From(KbArticleDto article) => new()
    {
        Id = article.Id,
        ProductId = article.ProductId,
        CategoryId = article.CategoryId,
        Slug = article.Slug,
        Title = article.Title,
        Summary = article.Summary ?? string.Empty,
        Body = article.BodyMarkdown,
        Status = article.Status,
        Version = article.Version,
        PublishedAt = article.PublishedAt,
    };

    public CreateKbArticleRequest ToCreateRequest() => new(ProductId, CategoryId, Slug.Trim(), Title.Trim(), Blank(Summary), Body);

    public UpdateKbArticleRequest ToUpdateRequest() => new(CategoryId, Title.Trim(), Blank(Summary), Body, Version);

    /// <summary>The fields an agent can change, as one value, so "has anything changed since it was loaded or saved" is a comparison and not a flag that stays set when the text is typed back.</summary>
    public Snapshot Take() => new(ProductId, CategoryId, Slug, Title, Summary, Body);

    public sealed record Snapshot(Guid? ProductId, Guid? CategoryId, string Slug, string Title, string Summary, string Body);

    private static string? Blank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>The message for one field, or null when it is fine. The slug is checked only when the article is being created: afterwards it cannot change.</summary>
    public string? Check(string field, bool creating) => field switch
    {
        ApiFields.Title => string.IsNullOrWhiteSpace(Title) ? KbEditorCopy.TitleRequired
            : Title.Trim().Length > KbEditorLimits.TitleMaxLength ? KbEditorCopy.TitleTooLong : null,
        ApiFields.Slug when creating => string.IsNullOrWhiteSpace(Slug) ? KbEditorCopy.SlugRequired
            : Slug.Trim().Length > KbEditorLimits.SlugMaxLength || !SlugPattern().IsMatch(Slug.Trim()) ? KbEditorCopy.SlugInvalid : null,
        ApiFields.Summary => Summary.Trim().Length > KbEditorLimits.SummaryMaxLength ? KbEditorCopy.SummaryTooLong : null,
        ApiFields.Body => string.IsNullOrWhiteSpace(Body) ? KbEditorCopy.BodyRequired
            : Body.Length > KbEditorLimits.BodyMaxLength ? KbEditorCopy.BodyTooLong : null,
        _ => null,
    };

    /// <summary>The first thing publishing needs that is missing, as the API's own field name (title, slug, body or category), or null when the article can be published.</summary>
    public string? FirstMissingForPublish() =>
        string.IsNullOrWhiteSpace(Title) ? ApiFields.Title
        : string.IsNullOrWhiteSpace(Slug) ? ApiFields.Slug
        : string.IsNullOrWhiteSpace(Body) ? ApiFields.Body
        : CategoryId is null ? ApiFields.Category
        : null;

    /// <summary>A slug suggested from a title ("Reset your password" gives "reset-your-password"). The agent can change it until the article is created.</summary>
    public static string SlugFrom(string title)
    {
        var slug = NotSlugCharacters().Replace(title.Trim().ToLowerInvariant(), "-").Trim('-');
        return slug.Length > KbEditorLimits.SlugMaxLength ? slug[..KbEditorLimits.SlugMaxLength].TrimEnd('-') : slug;
    }
}
