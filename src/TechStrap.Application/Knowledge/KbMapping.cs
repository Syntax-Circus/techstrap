using TechStrap.Contracts.Kb;
using TechStrap.Domain.Knowledge;

namespace TechStrap.Application.Knowledge;

internal static class KbMapping
{
    public static KbArticleDto ToDto(KbArticle article) =>
        new(
            article.Id, article.ProductId, article.CategoryId, article.Slug, article.Title, article.Summary, article.BodyMarkdown, StatusName(article.Status),
            article.AuthorId, article.CreatedAt, article.UpdatedAt, article.PublishedAt, article.Version);

    public static KbArticleListItemDto ToListItem(KbArticle article) =>
        new(article.Id, article.ProductId, article.CategoryId, article.Slug, article.Title, StatusName(article.Status), article.UpdatedAt);

    public static KbCategoryDto ToDto(KbCategory category) =>
        new(category.Id, category.ProductId, category.Slug, category.Name, category.Description, category.SortOrder, category.Version);

    public static string StatusName(KbArticleStatus status) => status switch
    {
        KbArticleStatus.Draft => KbArticleStatuses.Draft,
        KbArticleStatus.Published => KbArticleStatuses.Published,
        KbArticleStatus.Archived => KbArticleStatuses.Archived,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unmapped article status."),
    };

    /// <summary>Case-insensitive; blank means no filter. Returns false for a name that is not a status.</summary>
    public static bool TryParseStatus(string? text, out KbArticleStatus? status)
    {
        status = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (Enum.TryParse<KbArticleStatus>(text.Trim(), ignoreCase: true, out var parsed) && Enum.IsDefined(parsed))
        {
            status = parsed;
            return true;
        }

        return false;
    }
}
