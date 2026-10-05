using SyntaxCircus.Common;
using TechStrap.Application.Knowledge;
using TechStrap.Domain.Knowledge;

namespace TechStrap.Application.Persistence;

public interface IKbRepository
{
    /// <summary>Agent-facing; returns any status; never call from customer handlers (use <see cref="GetPublishedArticleAsync"/>).</summary>
    Task<KbArticle?> GetArticleAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Customer and portal lookup by id: null unless the article is Published.</summary>
    Task<KbArticle?> GetPublishedArticleAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Agent-facing; returns any status; never call from customer handlers (use <see cref="GetPublishedArticleBySlugAsync"/>). A null product looks in the shared slug space.</summary>
    Task<KbArticle?> GetArticleBySlugAsync(Guid? productId, string slug, CancellationToken cancellationToken);

    /// <summary>Customer and portal lookup: null unless the article is Published, so a draft or archived article is never served. A null product looks in the shared slug space.</summary>
    Task<KbArticle?> GetPublishedArticleBySlugAsync(Guid? productId, string slug, CancellationToken cancellationToken);

    /// <summary>Agent-facing; returns any status unless <see cref="KbArticleQuery.Status"/> is set; never call from customer handlers (use <see cref="ListPublishedArticlesAsync"/>).</summary>
    Task<PagedResult<KbArticle>> ListArticlesAsync(KbArticleQuery query, CancellationToken cancellationToken);

    /// <summary>Customer and portal list: only Published articles, whatever the caller passes (the query has no status).</summary>
    Task<PagedResult<KbArticle>> ListPublishedArticlesAsync(PublishedKbArticleQuery query, CancellationToken cancellationToken);

    /// <summary>Agent-facing full-text search ranked by relevance; blank text returns an empty page.</summary>
    /// <remarks>Returns any status unless <see cref="KbSearchQuery.Status"/> is set; never call from customer handlers (use <see cref="SearchPublishedAsync"/>). Text longer than <c>DomainLimits.SearchTextMaxLength</c> is truncated.</remarks>
    Task<PagedResult<KbArticle>> SearchAsync(KbSearchQuery query, CancellationToken cancellationToken);

    /// <summary>Customer and portal full-text search: only Published articles, whatever the caller passes. Blank text returns an empty page; long text is truncated.</summary>
    Task<PagedResult<KbArticle>> SearchPublishedAsync(PublishedKbSearchQuery query, CancellationToken cancellationToken);

    /// <summary>
    /// The repository cannot check that the article's category belongs to the article's product (the method returns nothing and the schema has no
    /// composite key for it). The create and edit handlers enforce it with <c>KbCategoryRules.CheckAsync</c> (D-044): a shared article may only use a
    /// shared category, and a product article a shared category or one of its own product.
    /// <para>
    /// Staging only. After a failed commit, reload the article and redo the change; do not retry the same object. After a successful
    /// commit the Domain <c>Version</c> is stale: reload before updating again.
    /// </para>
    /// </summary>
    void AddArticle(KbArticle article);

    /// <summary>See the category and product rule on <see cref="AddArticle"/>. Throws when the article was not loaded in this unit of work. After a failed commit reload the article rather than retrying the same object; after a successful commit its <c>Version</c> is stale, so reload before updating again.</summary>
    void UpdateArticle(KbArticle article);

    /// <summary>
    /// The cross-scope slug rule (D-044). With a product: true when that product or the shared space already has an article with the slug.
    /// With no product (a shared article): true when any article of any scope has it. Any status counts. Callers pass the normalised slug
    /// (the <c>Guard.Slug</c> output); the method does not normalise it. A create handler asks this first. Two concurrent creates of the same slug in
    /// the same scope end in a <c>duplicate</c> commit conflict through the unique index, but two concurrent creates in DIFFERENT scopes can both
    /// succeed; that risk is accepted (D-044).
    /// </summary>
    Task<bool> ArticleSlugTakenAsync(Guid? productId, string slug, CancellationToken cancellationToken);

    /// <summary>The same cross-scope rule for category slugs (D-044), so a portal category address is never ambiguous. Callers pass the normalised slug (the <c>Guard.Slug</c> output).</summary>
    Task<bool> CategorySlugTakenAsync(Guid? productId, string slug, CancellationToken cancellationToken);

    /// <summary>True when any article, in any status, uses the category. The category delete handler asks this before removing it (the foreign key is the backstop).</summary>
    Task<bool> CategoryHasArticlesAsync(Guid categoryId, CancellationToken cancellationToken);

    Task<KbCategory?> GetCategoryAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Ordered by sort order then name. With a product, <paramref name="includeShared"/> adds the shared categories; without one, every category is returned.</summary>
    Task<IReadOnlyList<KbCategory>> ListCategoriesAsync(Guid? productId, bool includeShared, CancellationToken cancellationToken);

    void AddCategory(KbCategory category);

    void UpdateCategory(KbCategory category);

    void RemoveCategory(KbCategory category);

    void AddTicketArticle(TicketArticle link);

    /// <summary>Agent-facing; returns linked articles in any status; never expose to customers.</summary>
    Task<IReadOnlyList<KbArticle>> ListLinkedArticlesAsync(Guid ticketId, CancellationToken cancellationToken);

    /// <summary>Every ticket-to-article link of a ticket with its message id (per-message linked articles).</summary>
    Task<IReadOnlyList<TicketArticle>> ListTicketArticlesAsync(Guid ticketId, CancellationToken cancellationToken);
}
