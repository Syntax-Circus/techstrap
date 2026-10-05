namespace TechStrap.Admin.Features.Kb;

/// <summary>The words of the reply composer's article picker and its chips. Plain, sentence case (docs/BRAND.md section 3).</summary>
public static class ArticlePickerCopy
{
    public const string SearchLabel = "Search published articles";
    public const string SearchHelp = "Only published articles of this ticket's product, and shared ones, can be linked. The customer's email gets a link to each.";
    public const string Searching = "Searching";
    public const string NoResults = "No published article matches.";
    public const string ResultsLabel = "Published articles";
    public const string SearchFailed = "Couldn't search the articles. Try again in a moment.";
    public const string Add = "Add";
    public const string Added = "Added";
    public const string Toggle = "Link a knowledge base article";
    public const string ToggleClose = "Hide the article search";
    public const string LinkedLabel = "Linked articles";
    public const string Remove = "Remove";

    public static string AddLabel(string title) => $"Add {title}";

    public static string RemoveLabel(string title) => $"Remove {title}";

    public static string LimitReached(int limit) => $"A reply can link up to {limit} articles.";

    public static string Count(int count) => count == 1 ? "1 article" : $"{count} articles";
}
