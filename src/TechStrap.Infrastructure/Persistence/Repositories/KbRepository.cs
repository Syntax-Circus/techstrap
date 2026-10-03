using Microsoft.EntityFrameworkCore;
using SyntaxCircus.Common;
using TechStrap.Application.Knowledge;
using TechStrap.Application.Persistence;
using TechStrap.Domain.Knowledge;
using TechStrap.Domain.Rules;
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

    public async Task<PagedResult<KbArticle>> ListArticlesAsync(KbArticleQuery query, CancellationToken cancellationToken)
    {
        var page = Paging.NormalizePage(query.Page);
        var pageSize = Paging.NormalizePageSize(query.PageSize);

        var articles = context.Set<KbArticleRecord>().AsNoTracking();
        if (query.ProductId is { } productId)
        {
            articles = query.IncludeShared
                ? articles.Where(a => a.ProductId == productId || a.ProductId == null)
                : articles.Where(a => a.ProductId == productId);
        }

        if (query.Status is { } status)
        {
            articles = articles.Where(a => a.Status == status);
        }

        if (query.CategoryId is { } categoryId)
        {
            articles = articles.Where(a => a.CategoryId == categoryId);
        }

        var total = await articles.CountAsync(cancellationToken);
        var records = await articles.OrderByDescending(a => a.UpdatedAt).ThenByDescending(a => a.Id)
            .Skip(Paging.Offset(page, pageSize)).Take(pageSize)
            .ToListAsync(cancellationToken);
        return new PagedResult<KbArticle>([.. records.Select(a => a.ToDomain())], page, pageSize, total);
    }

    public async Task<PagedResult<KbArticle>> SearchAsync(KbSearchQuery query, CancellationToken cancellationToken)
    {
        var page = Paging.NormalizePage(query.Page);
        var pageSize = Paging.NormalizePageSize(query.PageSize);
        var text = query.Text?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return new PagedResult<KbArticle>([], page, pageSize, 0);
        }

        if (text.Length > DomainLimits.SearchTextMaxLength)
        {
            text = text[..DomainLimits.SearchTextMaxLength];
        }

        var articles = context.Set<KbArticleRecord>().AsNoTracking()
            .Where(a => a.SearchVector.Matches(EF.Functions.WebSearchToTsQuery(FullTextSearch.Config, text)));
        if (query.ProductId is { } productId)
        {
            articles = query.IncludeShared
                ? articles.Where(a => a.ProductId == productId || a.ProductId == null)
                : articles.Where(a => a.ProductId == productId);
        }

        if (query.Status is { } status)
        {
            articles = articles.Where(a => a.Status == status);
        }

        if (query.CategoryId is { } categoryId)
        {
            articles = articles.Where(a => a.CategoryId == categoryId);
        }

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

    public void UpdateCategory(KbCategory category) => category.CopyTo(context.FindLoaded<KbCategoryRecord>(category.Id));

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
}
