namespace TechStrap.Contracts.Kb;

/// <summary>
/// The agent view of one article, with the Markdown source. <paramref name="Version"/> is the concurrency token: send it back
/// unchanged in <see cref="UpdateKbArticleRequest"/>. <paramref name="Status"/> is one of <see cref="KbArticleStatuses"/>.
/// </summary>
public sealed record KbArticleDto(
    Guid Id,
    Guid? ProductId,
    Guid? CategoryId,
    string Slug,
    string Title,
    string? Summary,
    string BodyMarkdown,
    string Status,
    Guid AuthorAgentId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? PublishedAt,
    uint Version);

/// <summary>One row of the agent article list. A null product means the article is shared by every product.</summary>
public sealed record KbArticleListItemDto(
    Guid Id,
    Guid? ProductId,
    Guid? CategoryId,
    string Slug,
    string Title,
    string Status,
    DateTimeOffset UpdatedAt);

/// <summary>
/// A new article, always a Draft. A null <paramref name="ProductId"/> makes it shared. The product and the slug are permanent. A shared
/// article may only use a shared category; a product article may use a shared category or one of its own product.
/// </summary>
public sealed record CreateKbArticleRequest(Guid? ProductId, Guid? CategoryId, string? Slug, string? Title, string? Summary, string? BodyMarkdown);

/// <summary>
/// Replaces the editable fields. <paramref name="Version"/> must equal the version last read, otherwise the update is a 409. Updating an
/// Archived article returns it to Draft. The product and the slug cannot change.
/// </summary>
public sealed record UpdateKbArticleRequest(Guid? CategoryId, string? Title, string? Summary, string? BodyMarkdown, uint Version);

/// <summary>The filters of <c>GET api/kb/articles</c>. <paramref name="Text"/> is a full-text search; blank lists by newest update.</summary>
/// <param name="ProductId">Only this product's articles (plus the shared ones unless <paramref name="IncludeShared"/> is false).</param>
/// <param name="SharedOnly">Only shared articles. It wins over <paramref name="ProductId"/>.</param>
/// <param name="Status">One of <see cref="KbArticleStatuses"/>; null for every status.</param>
public sealed record ListKbArticlesRequest(
    Guid? ProductId,
    bool SharedOnly,
    bool IncludeShared,
    string? Status,
    Guid? CategoryId,
    string? Text,
    int Page,
    int PageSize);

public sealed record KbPreviewRequest(string? BodyMarkdown);

/// <summary>Sanitised HTML, produced by the same pipeline the public article page uses.</summary>
public sealed record KbPreviewResponse(string Html);

/// <summary>The stored key (<c>kb-images/{guid}.{ext}</c>) and the absolute public URL to put in the Markdown.</summary>
public sealed record KbImageUploadResponse(string Key, string Url);
