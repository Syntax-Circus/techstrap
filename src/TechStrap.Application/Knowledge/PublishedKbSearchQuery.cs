using TechStrap.Application.Persistence;

namespace TechStrap.Application.Knowledge;

/// <summary>
/// Customer-facing knowledge-base full-text search. It has no status: the repository only ever returns Published articles for it.
/// With a product, <see cref="IncludeShared"/> also searches shared articles.
/// </summary>
public sealed record PublishedKbSearchQuery(
    string Text,
    Guid? ProductId = null,
    bool IncludeShared = true,
    Guid? CategoryId = null,
    int Page = 1,
    int PageSize = Paging.DefaultPageSize);
