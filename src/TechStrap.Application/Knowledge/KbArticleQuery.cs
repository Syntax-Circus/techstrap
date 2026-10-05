using TechStrap.Application.Persistence;
using TechStrap.Domain.Knowledge;

namespace TechStrap.Application.Knowledge;

/// <summary>
/// Filters for a KB article list, newest update first. With a product, <see cref="IncludeShared"/> also returns shared articles.
/// <see cref="SharedOnly"/> returns only the shared articles and wins over <see cref="ProductId"/>.
/// Implementations normalize <see cref="Page"/> and <see cref="PageSize"/> through <see cref="Paging"/> before querying.
/// </summary>
public sealed record KbArticleQuery(
    Guid? ProductId = null,
    bool IncludeShared = true,
    KbArticleStatus? Status = null,
    Guid? CategoryId = null,
    int Page = 1,
    int PageSize = Paging.DefaultPageSize,
    bool SharedOnly = false);
