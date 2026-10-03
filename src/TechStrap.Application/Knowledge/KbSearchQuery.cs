using TechStrap.Application.Persistence;
using TechStrap.Domain.Knowledge;

namespace TechStrap.Application.Knowledge;

/// <summary>
/// A knowledge-base full-text search (D-011, D-027): web-search syntax over title (weight A), summary (B) and body (C), best
/// match first. Public search passes <c>Status = Published</c>. With a product, <see cref="IncludeShared"/> also searches shared articles.
/// Implementations normalize <see cref="Page"/> and <see cref="PageSize"/> through <see cref="Paging"/> before querying.
/// </summary>
public sealed record KbSearchQuery(
    string Text,
    Guid? ProductId = null,
    bool IncludeShared = true,
    KbArticleStatus? Status = null,
    Guid? CategoryId = null,
    int Page = 1,
    int PageSize = Paging.DefaultPageSize);
