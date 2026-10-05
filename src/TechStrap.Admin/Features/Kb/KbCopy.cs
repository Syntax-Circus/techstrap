namespace TechStrap.Admin.Features.Kb;

/// <summary>The words of the article list and the parts the knowledge base screens share. Plain, sentence case (docs/BRAND.md section 3).</summary>
public static class KbCopy
{
    public const string Heading = "Knowledge base";
    public const string TableLabel = "Articles";
    public const string Loading = "Loading articles";
    public const string LoadFailed = "Couldn't load the articles.";
    public const string TryAgain = "Try again in a moment.";
    public const string NewArticle = "New article";
    public const string Categories = "Categories";

    public const string SearchLabel = "Search articles";
    public const string AllProducts = "All products";
    public const string AllCategories = "All categories";
    public const string AllStatuses = "Any status";
    public const string ClearFilters = "Clear filters";
    public const string Refresh = "Refresh";

    public const string ColumnTitle = "Title";
    public const string ColumnProduct = "Product";
    public const string ColumnCategory = "Category";
    public const string ColumnStatus = "Status";
    public const string ColumnUpdated = "Updated";

    public const string Shared = "Shared";
    public const string NoCategory = "No category";
    public const string UnknownProduct = "Another product";

    public const string NoMatchHeading = "No articles match";
    public const string NoArticlesHeading = "No articles yet";
    public const string NoArticlesHint = "Write the first article, then publish it for customers to find.";

    public static string Count(int shown, int total) => $"{shown} of {total} articles";
}
