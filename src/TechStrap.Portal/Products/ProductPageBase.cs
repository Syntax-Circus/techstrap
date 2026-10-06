using Microsoft.AspNetCore.Components;
using SyntaxCircus.Common;
using TechStrap.Portal.Clients;
using TechStrap.Portal.Routing;

namespace TechStrap.Portal.Products;

/// <summary>
/// The base of every page under <c>/p/{key}</c> (D-045). It loads the product once and puts it in the <see cref="ProductScope"/>, so the layout can theme the page. Review Focus 3: an unknown product,
/// an inactive product and a key that is not a slug all end in <c>NavigationManager.NotFound()</c>, which renders the same neutral page, with the same status, as an unknown route, so nothing tells
/// a visitor which products exist; a key that is not a slug is answered without a call. When the API fails the page shows a calm message (<see cref="UnavailableMessage"/>) and a 503, or a 429 when it
/// is rate limiting, never a stack trace.
/// </summary>
public abstract class ProductPageBase : ComponentBase
{
    [Parameter]
    public string Key { get; set; } = string.Empty;

    [Inject]
    private IPublicProductClient Products { get; set; } = default!;

    [Inject]
    private ProductScope Scope { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [Inject]
    private IHostEnvironment Environment { get; set; } = default!;

    [Inject]
    private IHttpContextAccessor HttpContextAccessor { get; set; } = default!;

    /// <summary>The product's theme once it has loaded; null when the product is unknown or the API failed.</summary>
    protected ProductThemeViewModel? Theme => Scope.Theme;

    /// <summary>The fixed sentence to show when the API could not be asked (<see cref="ProblemCopy"/>); null when the product loaded or was not found.</summary>
    protected string? UnavailableMessage { get; private set; }

    /// <summary>This page's own address, root-relative, for a "Try again" link (the document's <c>base</c> is <c>/</c>).</summary>
    protected string RetryHref => "/" + Navigation.ToBaseRelativePath(Navigation.Uri);

    /// <summary>The token of the request this page is serving: an API call made for it stops when the visitor goes away.</summary>
    protected CancellationToken RequestAborted => HttpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None;

    /// <summary>
    /// What a page does with a failed read of its own data once the product has loaded (the help-centre pages): a not-found is the neutral 404 (the product is forgotten first, so the 404 is byte for byte the page an unknown route
    /// gets), a rate limit is a 429 and anything else a 503, each with the fixed sentence of <see cref="ProblemCopy"/>; whatever the API said is never shown. The page keeps its theme and shows <see cref="UnavailableMessage"/>.
    /// </summary>
    protected void Fail(ResultError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        if (error.Kind == ResultErrorKind.NotFound)
        {
            NotFoundAfterTheming();
            return;
        }

        Unavailable(error);
    }

    /// <summary>Ends the request in the neutral 404 after the product was set (a post the API answers 404 to): the product is forgotten first, so the 404 carries none of its accent, header or footer.</summary>
    protected void NotFoundAfterTheming()
    {
        Scope.Clear();
        Navigation.NotFound();
    }

    protected override async Task OnInitializedAsync()
    {
        // The client refuses a malformed key too (without a call); this check keeps the page from depending on that, and ProductPageBaseTests pins it.
        if (!ProductKeyShape.IsWellFormed(Key))
        {
            Navigation.NotFound();
            return;
        }

        var result = await Products.GetAsync(Key, RequestAborted);
        if (result.IsSuccess)
        {
            Scope.Set(ProductThemeViewModel.From(result.Value, Environment.IsDevelopment()));
            return;
        }

        var error = result.Errors[0];
        if (error.Kind == ResultErrorKind.NotFound)
        {
            Navigation.NotFound();
            return;
        }

        Unavailable(error);
    }

    // Fixed copy only: whatever the API said (a 400's own detail included) is never shown on a product page.
    private void Unavailable(ResultError error)
    {
        var rateLimited = error.Code == ApiErrorCodes.RateLimited;
        UnavailableMessage = rateLimited ? ProblemCopy.RateLimited : ProblemCopy.ApiUnavailable;
        SetStatus(rateLimited ? StatusCodes.Status429TooManyRequests : StatusCodes.Status503ServiceUnavailable);
    }

    private void SetStatus(int status)
    {
        if (HttpContextAccessor.HttpContext is { Response.HasStarted: false } context)
        {
            context.Response.StatusCode = status;
        }
    }
}
