using TechStrap.Application.Persistence;

namespace TechStrap.Application.Knowledge;

/// <summary>
/// Customer-facing filters for a knowledge-base article list, newest update first. It has no status: the repository only ever returns
/// Published articles for it. With a product, <see cref="IncludeShared"/> also returns shared articles.
/// </summary>
public sealed record PublishedKbArticleQuery(
    Guid? ProductId = null,
    bool IncludeShared = true,
    Guid? CategoryId = null,
    int Page = 1,
    int PageSize = Paging.DefaultPageSize);
