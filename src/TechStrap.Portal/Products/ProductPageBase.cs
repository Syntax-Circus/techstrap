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

    protected override async Task OnInitializedAsync()
    {
        if (!ProductKeyShape.IsWellFormed(Key))
        {
            Navigation.NotFound();
            return;
        }

        var result = await Products.GetAsync(Key, HttpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None);
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

        UnavailableMessage = error.Message;
        SetStatus(error.Code == ApiErrorCodes.RateLimited ? StatusCodes.Status429TooManyRequests : StatusCodes.Status503ServiceUnavailable);
    }

    private void SetStatus(int status)
    {
        if (HttpContextAccessor.HttpContext is { Response.HasStarted: false } context)
        {
            context.Response.StatusCode = status;
        }
    }
}
