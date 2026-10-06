using SyntaxCircus.Common;
using TechStrap.Contracts.Kb;
using TechStrap.Portal.Clients;
using TechStrap.Portal.Routing;

namespace TechStrap.Portal.Suggestions;

/// <summary>One suggestion for the contact page's element: plain text for the title and the snippet, and a path of this site for the link.</summary>
public sealed record SuggestionDto(string Title, string Snippet, string Href);

/// <summary>
/// <c>GET /p/{key}/suggest?q=</c>: the Portal-hosted adapter behind the contact page's <c>ts-kb-suggestions</c> element (D-045 addendum; exempt like D-017: it runs no workflow of its own, it asks the API's public
/// search and reshapes the answer). A browser script cannot call the API itself (no CSP allowance, and the API would see the Portal's address, not the visitor's), and a circuit would lose the visitor's address
/// (D-019), so this is an ordinary request through <see cref="IPublicKbClient"/>, which forwards the visitor's address like every Portal call. Rules: a blank or missing text is an empty list and no call (so is a key that is not a slug: the client refuses it without a call and any failure is an empty list); the text
/// is cut at <see cref="KbLimits.MaxSearchTextChars"/> without splitting a surrogate pair; at most <see cref="MaxItems"/> items; every link is built here with <see cref="PortalRoutes.KbArticle"/> from the
/// product the visitor is on (a shared article is linked under it), never from anything the API sent; the API's 429 is passed on as a 429 and any other failure is an empty list, because suggestions must never
/// get in the way of the form. The response is plain JSON and never stored (the form-page header rule adds noindex).
/// </summary>
public static class SuggestEndpoint
{
    public const int MaxItems = 5;

    public static IEndpointRouteBuilder MapSuggest(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(PortalRoutes.SuggestTemplate, HandleAsync);
        return endpoints;
    }

    private static async Task<IResult> HandleAsync(string key, string? q, IPublicKbClient kb, HttpContext http, CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        var text = Clean(q);
        if (text.Length == 0)
        {
            return Results.Json(Array.Empty<SuggestionDto>());
        }

        var result = await kb.SearchAsync(key, text, MaxItems, cancellationToken);
        if (result.IsFailure)
        {
            return result.Errors[0].Code == ApiErrorCodes.RateLimited
                ? Results.Json(Array.Empty<SuggestionDto>(), statusCode: StatusCodes.Status429TooManyRequests)
                : Results.Json(Array.Empty<SuggestionDto>());
        }

        var items = result.Value.Items
            .Where(hit => !string.IsNullOrWhiteSpace(hit.Slug) && !string.IsNullOrWhiteSpace(hit.CategorySlug) && !string.IsNullOrWhiteSpace(hit.Title))
            .Take(MaxItems)
            .Select(hit => new SuggestionDto(hit.Title, hit.Snippet ?? string.Empty, PortalRoutes.KbArticle(key, hit.CategorySlug, hit.Slug)))
            .ToList();
        return Results.Json(items);
    }

    private static string Clean(string? text)
    {
        var trimmed = text?.Trim() ?? string.Empty;
        if (trimmed.Length <= KbLimits.MaxSearchTextChars)
        {
            return trimmed;
        }

        var cut = trimmed[..KbLimits.MaxSearchTextChars];
        return char.IsHighSurrogate(cut[^1]) ? cut[..^1] : cut;
    }
}
