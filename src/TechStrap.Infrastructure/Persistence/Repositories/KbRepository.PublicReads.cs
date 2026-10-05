using Microsoft.EntityFrameworkCore;
using SyntaxCircus.Common;
using TechStrap.Application.Knowledge;
using TechStrap.Application.Persistence;
using TechStrap.Contracts.Kb;
using TechStrap.Domain.Knowledge;
using TechStrap.Infrastructure.Persistence.Mapping;
using TechStrap.Infrastructure.Persistence.Records;

namespace TechStrap.Infrastructure.Persistence.Repositories;

// The portal reads (D-044). Every method here forces Status = Published and a product scope; none takes a status from the caller.
internal sealed partial class KbRepository
{
    private sealed class SnippetRow
    {
        public Guid Id { get; set; }

        public string Snippet { get; set; } = string.Empty;
    }

    // Published articles of the product and the shared space whose category is also the product's or shared. An article with no category never qualifies.
    private IQueryable<KbArticleRecord> PublishedIn(Guid productId) =>
        context.Set<KbArticleRecord>().AsNoTracking().Where(article =>
            article.Status == KbArticleStatus.Published
            && (article.ProductId == productId || article.ProductId == null)
            && context.Set<KbCategoryRecord>().Any(category => category.Id == article.CategoryId && (category.ProductId == productId || category.ProductId == null)));

    public async Task<PagedResult<PublicKbSearchHit>> SearchPublicAsync(PublicKbSearchQuery query, CancellationToken cancellationToken)
    {
        var page = Paging.NormalizePage(query.Page);
        var pageSize = Math.Clamp(Paging.NormalizePageSize(query.PageSize), 1, KbLimits.MaxPublicSearchPageSize);
        var text = SearchText.Normalize(query.Text);
        if (string.IsNullOrEmpty(text))
        {
            return new PagedResult<PublicKbSearchHit>([], page, pageSize, 0);
        }

        var matches = PublishedIn(query.ProductId)
            .Where(article => article.SearchVector.Matches(EF.Functions.WebSearchToTsQuery(FullTextSearch.Config, text)));
        if (!string.IsNullOrWhiteSpace(query.CategorySlug))
        {
            var categorySlug = query.CategorySlug.Trim();
            matches = matches.Where(article => context.Set<KbCategoryRecord>().Any(category => category.Id == article.CategoryId && category.Slug == categorySlug));
        }

        var total = await matches.CountAsync(cancellationToken);
        var ids = await matches
            .OrderByDescending(article => article.SearchVector.Rank(EF.Functions.WebSearchToTsQuery(FullTextSearch.Config, text)))
            .ThenByDescending(article => article.UpdatedAt).ThenByDescending(article => article.Id)
            .Skip(Paging.Offset(page, pageSize)).Take(pageSize)
            .Select(article => article.Id)
            .ToListAsync(cancellationToken);
        if (ids.Count == 0)
        {
            return new PagedResult<PublicKbSearchHit>([], page, pageSize, total);
        }

        var rows = await (
            from article in context.Set<KbArticleRecord>().AsNoTracking()
            where ids.Contains(article.Id)
            join category in context.Set<KbCategoryRecord>().AsNoTracking() on article.CategoryId equals category.Id
            join owner in context.Set<ProductRecord>().AsNoTracking() on article.ProductId equals owner.Id into owners
            from owner in owners.DefaultIfEmpty()
            select new { article.Id, article.Slug, article.Title, CategorySlug = category.Slug, CategoryName = category.Name, ProductKey = owner == null ? null : owner.Key })
            .ToListAsync(cancellationToken);

        // ts_headline runs only for the page of ids, over the summary only; empty (quoted) markers keep the snippet plain text.
        var idArray = ids.ToArray();
        var snippets = await context.Database.SqlQuery<SnippetRow>($"""
            SELECT a.id AS "Id",
                   ts_headline({FullTextSearch.Config}::regconfig, coalesce(a.summary, ''), websearch_to_tsquery({FullTextSearch.Config}::regconfig, {text}),
                               'StartSel="", StopSel="", MaxWords=35, MinWords=15, MaxFragments=1, ShortWord=2') AS "Snippet"
            FROM kb_articles a
            WHERE a.id = ANY({idArray})
            """).ToListAsync(cancellationToken);
        var snippetById = snippets.ToDictionary(row => row.Id, row => row.Snippet);
        var rowById = rows.ToDictionary(row => row.Id);

        // The page order is the rank order, which the join does not keep.
        return new PagedResult<PublicKbSearchHit>(
            [.. ids.Where(rowById.ContainsKey).Select(id => rowById[id]).Select(row =>
                new PublicKbSearchHit(row.Slug, row.Title, snippetById.GetValueOrDefault(row.Id, string.Empty), row.CategorySlug, row.CategoryName, row.ProductKey))],
            page, pageSize, total);
    }

    public async Task<PublicKbArticleView?> GetPublicArticleAsync(Guid productId, string categorySlug, string slug, CancellationToken cancellationToken)
    {
        // The product's own article wins over a shared one with the same slug (the cross-scope rule makes that a legacy case only).
        var article = await PublishedIn(productId)
            .Where(a => a.Slug == slug && context.Set<KbCategoryRecord>().Any(category => category.Id == a.CategoryId && category.Slug == categorySlug))
            .OrderBy(a => a.ProductId == null ? 1 : 0)
            .FirstOrDefaultAsync(cancellationToken);
        if (article is null)
        {
            return null;
        }

        var category = await context.Set<KbCategoryRecord>().AsNoTracking().FirstAsync(c => c.Id == article.CategoryId, cancellationToken);
        var productKey = article.ProductId is { } ownerId
            ? await context.Set<ProductRecord>().AsNoTracking().Where(p => p.Id == ownerId).Select(p => p.Key).FirstOrDefaultAsync(cancellationToken)
            : null;
        return new PublicKbArticleView(article.ToDomain(), category.Slug, category.Name, productKey);
    }

    public async Task<IReadOnlyList<PublicKbLinkTarget>> ListPublicLinkTargetsAsync(Guid productId, IReadOnlyList<Guid> articleIds, CancellationToken cancellationToken)
    {
        if (articleIds.Count == 0)
        {
            return [];
        }

        var ids = articleIds.Distinct().ToList();
        return await PublishedIn(productId)
            .Where(article => ids.Contains(article.Id))
            .Join(context.Set<KbCategoryRecord>(), article => article.CategoryId, category => category.Id, (article, category) => new PublicKbLinkTarget(article.Id, category.Slug, article.Slug))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PublicKbCategoryCount>> ListPublicCategoriesAsync(Guid productId, CancellationToken cancellationToken)
    {
        var counts = await PublishedIn(productId)
            .GroupBy(article => article.CategoryId)
            .Select(group => new { CategoryId = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);
        if (counts.Count == 0)
        {
            return [];
        }

        var ids = counts.Select(count => count.CategoryId).ToList();
        var categories = await context.Set<KbCategoryRecord>().AsNoTracking()
            .Where(category => ids.Contains(category.Id))
            .OrderBy(category => category.SortOrder).ThenBy(category => category.Name).ThenBy(category => category.Id)
            .ToListAsync(cancellationToken);
        var countById = counts.ToDictionary(count => count.CategoryId!.Value, count => count.Count);
        return [.. categories.Select(category => new PublicKbCategoryCount(category.ToDomain(), countById[category.Id]))];
    }

    public async Task<IReadOnlyList<PublicKbSitemapRow>> ListPublicSitemapAsync(Guid productId, CancellationToken cancellationToken) =>
        await (
            from article in PublishedIn(productId)
            join category in context.Set<KbCategoryRecord>().AsNoTracking() on article.CategoryId equals category.Id
            join owner in context.Set<ProductRecord>().AsNoTracking() on article.ProductId equals owner.Id into owners
            from owner in owners.DefaultIfEmpty()
            orderby article.UpdatedAt descending, article.Id descending
            select new PublicKbSitemapRow(owner == null ? null : owner.Key, category.Slug, article.Slug, article.UpdatedAt))
            .Take(KbLimits.MaxSitemapEntries)
            .ToListAsync(cancellationToken);
}
