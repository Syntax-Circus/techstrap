using System.Globalization;

namespace TechStrap.Portal.Components;

/// <summary>
/// The words of the help-centre pages (PHASE-09c). Plain copy, no humour (BRAND.md), the product's name only where a page needs it. A page references these and never writes a sentence of its own. Everything a
/// visitor typed or an agent wrote (a search text, a category name, an article title) is passed in as an argument and shown by Razor, which encodes it; none of it is ever markup.
/// </summary>
public static class KbCopy
{
    // The help-centre home.
    public const string HomeHeading = "Help centre";
    public const string EmptyHomeHeading = "No articles yet";
    public const string EmptyHomeText = "There are no help articles for this product yet. If you need help, contact support.";

    // Navigation.
    public const string BreadcrumbLabel = "Breadcrumb";
    public const string PagerLabel = "Pages";
    public const string PreviousPage = "Previous page";
    public const string NextPage = "Next page";

    // The end of an article.
    public const string StillNeedHelp = "Still need help?";
    public const string StillNeedHelpText = "If this did not answer your question, contact support and we will help you.";

    // Search.
    /// <summary>The h1 of the search page: different from the help centre's, so two tabs of the two pages are told apart.</summary>
    public const string SearchPageHeading = "Search the help centre";

    public const string SearchHeading = "Search results";
    public const string SearchPromptHeading = "What are you looking for?";
    public const string SearchPromptText = "Type a few words about your question and search the help articles.";
    public const string NoResultsHeading = "No articles found";
    public const string NoResultsText = "Try different words, or contact support and we will help you.";
    public const string ContactUs = "Contact support";

    public static string HomeTitle(string productName) => $"{productName} Help Centre";

    public static string HomeDescription(string productName) => $"Help articles and answers for {productName}.";

    public static string CategoryTitle(string categoryName, string productName, int page) =>
        page <= 1 ? $"{categoryName} - {productName} Help Centre" : $"{categoryName} (page {page.ToString(CultureInfo.InvariantCulture)}) - {productName} Help Centre";

    public static string CategoryDescription(string categoryName, string productName) => $"Help articles about {categoryName} for {productName}.";

    public static string ArticleTitle(string articleTitle, string productName) => $"{articleTitle} - {productName} Help Centre";

    public static string ArticleDescriptionFallback(string articleTitle, string productName) => $"{articleTitle}. Help article for {productName}.";

    public static string SearchTitle(string productName) => $"Search - {productName} Help Centre";

    public static string SearchDescription(string productName) => $"Search the help articles for {productName}.";

    public static string ArticleCount(int count) => count == 1 ? "1 article" : $"{count.ToString(CultureInfo.InvariantCulture)} articles";

    public static string ResultCount(int total) => total == 1 ? "1 result" : $"{total.ToString(CultureInfo.InvariantCulture)} results";

    public static string PageOf(int page, int pages) => $"Page {page.ToString(CultureInfo.InvariantCulture)} of {pages.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>The day an article was last changed, in UTC, so the page reads the same everywhere.</summary>
    public static string UpdatedOn(DateTimeOffset moment) => $"Updated {moment.UtcDateTime.ToString("d MMM yyyy", CultureInfo.InvariantCulture)}";
}
