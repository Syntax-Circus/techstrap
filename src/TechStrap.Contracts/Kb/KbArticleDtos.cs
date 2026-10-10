namespace TechStrap.Contracts.Kb;

/// <summary>
/// The agent view of one article, with the Markdown source. <paramref name="Version"/> is the concurrency token: send it back
/// unchanged in <see cref="UpdateKbArticleRequest"/>. <paramref name="Status"/> is one of <see cref="KbArticleStatuses"/>.
/// </summary>
/// <param name="Id">The article's id.</param>
/// <param name="ProductId">The owning product, or null for a shared article.</param>
/// <param name="CategoryId">The category, or null if uncategorized.</param>
/// <param name="Slug">The permanent URL slug.</param>
/// <param name="Title">The article title.</param>
/// <param name="Summary">The author's summary; may be null.</param>
/// <param name="BodyMarkdown">The Markdown source.</param>
/// <param name="Status">One of <see cref="KbArticleStatuses"/>.</param>
/// <param name="AuthorAgentId">The agent who created the article.</param>
/// <param name="CreatedAt">When it was created.</param>
/// <param name="UpdatedAt">When it was last changed.</param>
/// <param name="PublishedAt">When it was first published; null while a draft.</param>
/// <param name="Version">The concurrency token; see the summary.</param>
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
/// <param name="Id">The article's id.</param>
/// <param name="ProductId">The owning product, or null for a shared article.</param>
/// <param name="CategoryId">The category, or null if uncategorized.</param>
/// <param name="Slug">The permanent URL slug.</param>
/// <param name="Title">The article title.</param>
/// <param name="Status">One of <see cref="KbArticleStatuses"/>.</param>
/// <param name="UpdatedAt">When it was last changed.</param>
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
/// <param name="ProductId">The owning product, or null for a shared article; permanent.</param>
/// <param name="CategoryId">The category; optional.</param>
/// <param name="Slug">The URL slug; required and permanent.</param>
/// <param name="Title">The article title; required.</param>
/// <param name="Summary">The summary; optional.</param>
/// <param name="BodyMarkdown">The Markdown source; required.</param>
public sealed record CreateKbArticleRequest(Guid? ProductId, Guid? CategoryId, string? Slug, string? Title, string? Summary, string? BodyMarkdown);

/// <summary>
/// Replaces the editable fields. <paramref name="Version"/> must equal the version last read, otherwise the update is a 409. Updating an
/// Archived article returns it to Draft. The product and the slug cannot change.
/// </summary>
/// <param name="CategoryId">The category; null removes the article from its category.</param>
/// <param name="Title">The article title; required.</param>
/// <param name="Summary">The summary; optional.</param>
/// <param name="BodyMarkdown">The Markdown source; required.</param>
/// <param name="Version">The version last read; a mismatch is a 409.</param>
public sealed record UpdateKbArticleRequest(Guid? CategoryId, string? Title, string? Summary, string? BodyMarkdown, uint Version);

/// <summary>The filters of <c>GET api/kb/articles</c>. <paramref name="Text"/> is a full-text search; blank lists by newest update.</summary>
/// <param name="ProductId">Only this product's articles (plus the shared ones unless <paramref name="IncludeShared"/> is false).</param>
/// <param name="SharedOnly">Only shared articles. It wins over <paramref name="ProductId"/>.</param>
/// <param name="IncludeShared">With <paramref name="ProductId"/>, whether the shared articles are listed too.</param>
/// <param name="Status">One of <see cref="KbArticleStatuses"/>; null for every status.</param>
/// <param name="CategoryId">Only articles in this category; null for every category.</param>
/// <param name="Text">The full-text search; blank lists by newest update.</param>
/// <param name="Page">The 1-based page number.</param>
/// <param name="PageSize">The number of articles per page.</param>
public sealed record ListKbArticlesRequest(
    Guid? ProductId,
    bool SharedOnly,
    bool IncludeShared,
    string? Status,
    Guid? CategoryId,
    string? Text,
    int Page,
    int PageSize);

/// <summary>Asks for Markdown to be rendered as the public page would show it.</summary>
/// <param name="BodyMarkdown">The Markdown to render, at most <see cref="KbLimits.MaxPreviewChars"/> characters.</param>
public sealed record KbPreviewRequest(string? BodyMarkdown);

/// <summary>Sanitized HTML, produced by the same pipeline the public article page uses.</summary>
/// <param name="Html">The sanitized HTML.</param>
public sealed record KbPreviewResponse(string Html);

/// <summary>The stored key (<c>kb-images/{guid}.{ext}</c>) and the absolute public URL to put in the Markdown.</summary>
/// <param name="Key">The stored key.</param>
/// <param name="Url">The absolute public URL.</param>
public sealed record KbImageUploadResponse(string Key, string Url);
