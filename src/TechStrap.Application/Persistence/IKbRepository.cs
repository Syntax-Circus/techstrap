using SyntaxCircus.Common;
using TechStrap.Application.Knowledge;
using TechStrap.Domain.Knowledge;

namespace TechStrap.Application.Persistence;

public interface IKbRepository
{
    Task<KbArticle?> GetArticleAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Agent-facing: returns the article in any status. A null product looks in the shared slug space.</summary>
    Task<KbArticle?> GetArticleBySlugAsync(Guid? productId, string slug, CancellationToken cancellationToken);

    /// <summary>Customer and portal lookup: null unless the article is Published, so a draft or archived article is never served. A null product looks in the shared slug space.</summary>
    Task<KbArticle?> GetPublishedArticleBySlugAsync(Guid? productId, string slug, CancellationToken cancellationToken);

    /// <remarks>Any status unless <see cref="KbArticleQuery.Status"/> is set. A customer or portal handler MUST pass <c>Status = Published</c>.</remarks>
    Task<PagedResult<KbArticle>> ListArticlesAsync(KbArticleQuery query, CancellationToken cancellationToken);

    /// <summary>Full-text search ranked by relevance; blank text returns an empty page.</summary>
    /// <remarks>Any status unless <see cref="KbSearchQuery.Status"/> is set. A customer or portal handler MUST pass <c>Status = Published</c>. Text longer than <c>DomainLimits.SearchTextMaxLength</c> is truncated.</remarks>
    Task<PagedResult<KbArticle>> SearchAsync(KbSearchQuery query, CancellationToken cancellationToken);

    /// <summary>
    /// TODO(Application handler): the repository cannot check that the article's category belongs to the article's product (the method returns
    /// nothing and the schema has no composite key for it). The create and edit handlers must load the category and require its ProductId to
    /// equal the article's ProductId, or be null (a shared category) when the article belongs to a product. A shared article may only use a
    /// shared category. Return a Validation error from the handler otherwise, before calling this.
    /// </summary>
    void AddArticle(KbArticle article);

    /// <summary>See the category and product TODO on <see cref="AddArticle"/>. Throws when the article was not loaded in this unit of work.</summary>
    void UpdateArticle(KbArticle article);

    Task<KbCategory?> GetCategoryAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Ordered by sort order then name. With a product, <paramref name="includeShared"/> adds the shared categories; without one, every category is returned.</summary>
    Task<IReadOnlyList<KbCategory>> ListCategoriesAsync(Guid? productId, bool includeShared, CancellationToken cancellationToken);

    void AddCategory(KbCategory category);

    void UpdateCategory(KbCategory category);

    void RemoveCategory(KbCategory category);

    void AddTicketArticle(TicketArticle link);

    Task<IReadOnlyList<KbArticle>> ListLinkedArticlesAsync(Guid ticketId, CancellationToken cancellationToken);
}
