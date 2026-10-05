using TechStrap.Contracts.Kb;
using TechStrap.Contracts.Paging;

namespace TechStrap.Admin.Tests.Support;

internal static partial class TestData
{
    public static readonly Guid GettingStartedId = Guid.Parse("cccccccc-0000-0000-0000-000000000001");
    public static readonly Guid AccountCategoryId = Guid.Parse("cccccccc-0000-0000-0000-000000000002");
    public static readonly Guid ArticleId = Guid.Parse("dddddddd-0000-0000-0000-000000000001");

    public static KbArticleListItemDto KbItem(
        string title = "Reset your password",
        string slug = "reset-password",
        string status = KbArticleStatuses.Published,
        Guid? productId = null,
        Guid? categoryId = null,
        Guid? id = null) =>
        new(id ?? Guid.NewGuid(), productId, categoryId, slug, title, status, Now.AddHours(-2));

    public static PagedResponse<KbArticleListItemDto> KbPage(IReadOnlyList<KbArticleListItemDto> items, int page = 1, int total = -1) =>
        new(items, page, 25, total < 0 ? items.Count : total);

    public static KbCategoryDto KbCategory(string name = "Getting started", string slug = "getting-started", Guid? productId = null, Guid? id = null, int sortOrder = 10, uint version = 1, string? description = null) =>
        new(id ?? GettingStartedId, productId, slug, name, description, sortOrder, version);

    public static KbArticleDto KbArticle(
        string title = "Reset your password",
        string slug = "reset-password",
        string status = KbArticleStatuses.Draft,
        Guid? productId = null,
        Guid? categoryId = null,
        string summary = "How to reset it.",
        string body = "# Steps",
        uint version = 3,
        Guid? id = null,
        DateTimeOffset? publishedAt = null) =>
        new(id ?? ArticleId, productId, categoryId, slug, title, summary, body, status, Guid.NewGuid(), Now.AddDays(-2), Now.AddHours(-1), publishedAt, version);
}
