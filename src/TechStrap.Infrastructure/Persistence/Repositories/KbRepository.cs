using Microsoft.EntityFrameworkCore;
using SyntaxCircus.Common;
using TechStrap.Application.Knowledge;
using TechStrap.Application.Persistence;
using TechStrap.Domain.Knowledge;
using TechStrap.Infrastructure.Persistence.Mapping;
using TechStrap.Infrastructure.Persistence.Records;

namespace TechStrap.Infrastructure.Persistence.Repositories;

internal sealed class KbRepository(TechStrapDbContext context) : IKbRepository
{
    public async Task<KbArticle?> GetArticleAsync(Guid id, CancellationToken cancellationToken) =>
        (await context.Set<KbArticleRecord>().FirstOrDefaultAsync(a => a.Id == id, cancellationToken))?.ToDomain();

    public async Task<KbArticle?> GetArticleBySlugAsync(Guid? productId, string slug, CancellationToken cancellationToken) =>
        (await context.Set<KbArticleRecord>().FirstOrDefaultAsync(a => a.ProductId == productId && a.Slug == slug, cancellationToken))?.ToDomain();

    public async Task<KbArticle?> GetPublishedArticleBySlugAsync(Guid? productId, string slug, CancellationToken cancellationToken) =>
        (await context.Set<KbArticleRecord>().FirstOrDefaultAsync(
            a => a.ProductId == productId && a.Slug == slug && a.Status == KbArticleStatus.Published, cancellationToken))?.ToDomain();

    public async Task<KbArticle?> GetPublishedArticleAsync(Guid id, CancellationToken cancellationToken) =>
        (await context.Set<KbArticleRecord>().FirstOrDefaultAsync(a => a.Id == id && a.Status == KbArticleStatus.Published, cancellationToken))?.ToDomain();

    public Task<PagedResult<KbArticle>> ListArticlesAsync(KbArticleQuery query, CancellationToken cancellationToken) =>
        ListAsync(query.ProductId, query.IncludeShared, query.SharedOnly, query.Status, query.CategoryId, query.Page, query.PageSize, cancellationToken);

    public Task<PagedResult<KbArticle>> ListPublishedArticlesAsync(PublishedKbArticleQuery query, CancellationToken cancellationToken) =>
        ListAsync(query.ProductId, query.IncludeShared, false, KbArticleStatus.Published, query.CategoryId, query.Page, query.PageSize, cancellationToken);

    public Task<PagedResult<KbArticle>> SearchAsync(KbSearchQuery query, CancellationToken cancellationToken) =>
        SearchAsync(query.Text, query.ProductId, query.IncludeShared, query.SharedOnly, query.Status, query.CategoryId, query.Page, query.PageSize, cancellationToken);

    public Task<PagedResult<KbArticle>> SearchPublishedAsync(PublishedKbSearchQuery query, CancellationToken cancellationToken) =>
        SearchAsync(query.Text, query.ProductId, query.IncludeShared, false, KbArticleStatus.Published, query.CategoryId, query.Page, query.PageSize, cancellationToken);

    // One filter for the agent and customer entry points, so the two cannot drift apart. Only the public methods above choose the status.
    private IQueryable<KbArticleRecord> Filter(
        IQueryable<KbArticleRecord> articles, Guid? productId, bool includeShared, bool sharedOnly, KbArticleStatus? status, Guid? categoryId)
    {
        if (sharedOnly)
        {
            articles = articles.Where(a => a.ProductId == null);
        }
        else if (productId is { } product)
        {
            articles = includeShared
                ? articles.Where(a => a.ProductId == product || a.ProductId == null)
                : articles.Where(a => a.ProductId == product);
        }

        if (status is { } wanted)
        {
            articles = articles.Where(a => a.Status == wanted);
        }

        if (categoryId is { } category)
        {
            articles = articles.Where(a => a.CategoryId == category);
        }

        return articles;
    }

    private async Task<PagedResult<KbArticle>> ListAsync(
        Guid? productId, bool includeShared, bool sharedOnly, KbArticleStatus? status, Guid? categoryId, int requestedPage, int requestedPageSize, CancellationToken cancellationToken)
    {
        var page = Paging.NormalizePage(requestedPage);
        var pageSize = Paging.NormalizePageSize(requestedPageSize);

        var articles = Filter(context.Set<KbArticleRecord>().AsNoTracking(), productId, includeShared, sharedOnly, status, categoryId);
        var total = await articles.CountAsync(cancellationToken);
        var records = await articles.OrderByDescending(a => a.UpdatedAt).ThenByDescending(a => a.Id)
            .Skip(Paging.Offset(page, pageSize)).Take(pageSize)
            .ToListAsync(cancellationToken);
        return new PagedResult<KbArticle>([.. records.Select(a => a.ToDomain())], page, pageSize, total);
    }

    private async Task<PagedResult<KbArticle>> SearchAsync(
        string? rawText, Guid? productId, bool includeShared, bool sharedOnly, KbArticleStatus? status, Guid? categoryId, int requestedPage, int requestedPageSize, CancellationToken cancellationToken)
    {
        var page = Paging.NormalizePage(requestedPage);
        var pageSize = Paging.NormalizePageSize(requestedPageSize);
        var text = SearchText.Normalize(rawText);
        if (string.IsNullOrEmpty(text))
        {
            return new PagedResult<KbArticle>([], page, pageSize, 0);
        }

        var articles = Filter(
            context.Set<KbArticleRecord>().AsNoTracking()
                .Where(a => a.SearchVector.Matches(EF.Functions.WebSearchToTsQuery(FullTextSearch.Config, text))),
            productId, includeShared, sharedOnly, status, categoryId);
        var total = await articles.CountAsync(cancellationToken);
        var records = await articles
            .OrderByDescending(a => a.SearchVector.Rank(EF.Functions.WebSearchToTsQuery(FullTextSearch.Config, text)))
            .ThenByDescending(a => a.UpdatedAt).ThenByDescending(a => a.Id)
            .Skip(Paging.Offset(page, pageSize)).Take(pageSize)
            .ToListAsync(cancellationToken);
        return new PagedResult<KbArticle>([.. records.Select(a => a.ToDomain())], page, pageSize, total);
    }
    public void AddArticle(KbArticle article) => context.Set<KbArticleRecord>().Add(article.ToRecord());

    public void UpdateArticle(KbArticle article)
    {
        var record = context.FindLoaded<KbArticleRecord>(article.Id);
        article.CopyTo(record);
        if (context.Entry(record).State != EntityState.Added)
        {
            // The check is against the version the caller's Domain object carries, not the one this scope loaded (D-026).
            context.ApplyOriginalVersion(record, article.Version);
        }
    }

    public Task<bool> ArticleSlugTakenAsync(Guid? productId, string slug, CancellationToken cancellationToken) =>
        productId is { } product
            ? context.Set<KbArticleRecord>().AnyAsync(a => a.Slug == slug && (a.ProductId == product || a.ProductId == null), cancellationToken)
            : context.Set<KbArticleRecord>().AnyAsync(a => a.Slug == slug, cancellationToken);

    public Task<bool> CategorySlugTakenAsync(Guid? productId, string slug, CancellationToken cancellationToken) =>
        productId is { } product
            ? context.Set<KbCategoryRecord>().AnyAsync(c => c.Slug == slug && (c.ProductId == product || c.ProductId == null), cancellationToken)
            : context.Set<KbCategoryRecord>().AnyAsync(c => c.Slug == slug, cancellationToken);

    public Task<bool> CategoryHasArticlesAsync(Guid categoryId, CancellationToken cancellationToken) =>
        context.Set<KbArticleRecord>().AnyAsync(a => a.CategoryId == categoryId, cancellationToken);

    public async Task<KbCategory?> GetCategoryAsync(Guid id, CancellationToken cancellationToken) =>
        (await context.Set<KbCategoryRecord>().FirstOrDefaultAsync(c => c.Id == id, cancellationToken))?.ToDomain();

    public async Task<IReadOnlyList<KbCategory>> ListCategoriesAsync(Guid? productId, bool includeShared, CancellationToken cancellationToken)
    {
        var categories = context.Set<KbCategoryRecord>().AsNoTracking();
        if (productId is { } id)
        {
            categories = includeShared
                ? categories.Where(c => c.ProductId == id || c.ProductId == null)
                : categories.Where(c => c.ProductId == id);
        }

        var records = await categories.OrderBy(c => c.SortOrder).ThenBy(c => c.Name).ThenBy(c => c.Id).ToListAsync(cancellationToken);
        return [.. records.Select(c => c.ToDomain())];
    }

    public void AddCategory(KbCategory category) => context.Set<KbCategoryRecord>().Add(category.ToRecord());

    public void UpdateCategory(KbCategory category)
    {
        var record = context.FindLoaded<KbCategoryRecord>(category.Id);
        category.CopyTo(record);
        if (context.Entry(record).State != EntityState.Added)
        {
            // The check is against the version the caller's Domain object carries, not the one this scope loaded (D-026).
            context.ApplyOriginalVersion(record, category.Version);
        }
    }

    public void RemoveCategory(KbCategory category) => context.Set<KbCategoryRecord>().Remove(context.FindLoaded<KbCategoryRecord>(category.Id));

    public void AddTicketArticle(TicketArticle link) => context.Set<TicketArticleRecord>().Add(link.ToRecord());

    public async Task<IReadOnlyList<KbArticle>> ListLinkedArticlesAsync(Guid ticketId, CancellationToken cancellationToken)
    {
        // An EXISTS filter rather than a join plus DISTINCT: a tsvector column has no equality operator, so DISTINCT cannot compare rows.
        var links = context.Set<TicketArticleRecord>();
        var records = await context.Set<KbArticleRecord>().AsNoTracking()
            .Where(a => links.Any(l => l.TicketId == ticketId && l.ArticleId == a.Id))
            .OrderBy(a => a.Title).ThenBy(a => a.Id)
            .ToListAsync(cancellationToken);
        return [.. records.Select(a => a.ToDomain())];
    }

    public async Task<IReadOnlyList<TicketArticle>> ListTicketArticlesAsync(Guid ticketId, CancellationToken cancellationToken)
    {
        var records = await context.Set<TicketArticleRecord>().AsNoTracking()
            .Where(l => l.TicketId == ticketId)
            .OrderBy(l => l.MessageId).ThenBy(l => l.ArticleId)
            .ToListAsync(cancellationToken);
        return [.. records.Select(l => new TicketArticle(l.TicketId, l.MessageId, l.ArticleId))];
    }
}
