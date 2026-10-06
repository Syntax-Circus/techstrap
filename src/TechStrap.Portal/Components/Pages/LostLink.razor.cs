using Microsoft.AspNetCore.Components;
using TechStrap.Portal.Clients;
using TechStrap.Portal.Forms;
using TechStrap.Portal.Products;
using TechStrap.Portal.Routing;

namespace TechStrap.Portal.Components.Pages;

/// <summary>
/// The lost-link page of a product (P09-T10). One address; a post asks the API to email a new link and redirects (post, redirect, get) to the same page with <c>?sent=1</c>, which shows one sentence. Review Focus 2:
/// the page never looks at what the API answered beyond success or failure, shows no address and no hint, so every well-formed address gets the byte-identical response (the timing difference D-038 accepts is not
/// addressed here); a malformed address is an ordinary field error (it leaks nothing and the API is not asked); a 429 is a calm notice. Timing is out of scope (D-038); the responses are not.
/// </summary>
public partial class LostLink : ProductPageBase
{
    public const string FormHandler = "lost-link";

    [SupplyParameterFromForm(FormName = FormHandler)]
    private LostLinkFormViewModel? Form { get; set; }

    [SupplyParameterFromQuery(Name = PortalRoutes.SentParameter)]
    private string? SentFlag { get; set; }

    [Inject]
    private ICustomerTicketClient Customers { get; set; } = default!;

    [Inject]
    private NavigationManager Redirects { get; set; } = default!;

    [Inject]
    private IHttpContextAccessor Http { get; set; } = default!;

    private IReadOnlyList<FormError> Errors { get; set; } = [];

    private string? Notice { get; set; }

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        if (Theme is not null)
        {
            Form ??= new LostLinkFormViewModel();
        }
    }

    private string? ErrorOf(string field) => Errors.FirstOrDefault(error => error.Field == field)?.Message;

    private async Task SubmitAsync()
    {
        if (Theme is null)
        {
            return;
        }

        var form = Form ??= new LostLinkFormViewModel();
        Errors = EmailRules.Check(form.Email) is { } error ? [error] : [];
        if (Errors.Count > 0)
        {
            return;
        }

        var result = await Customers.RequestAccessLinkAsync(form.Email!.Trim(), Http.HttpContext?.RequestAborted ?? CancellationToken.None);
        if (result.IsSuccess)
        {
            Redirects.NavigateTo(PortalRoutes.LostLinkSent(Key));
            return;
        }

        // The route has no product or ticket for the API to not find, so a not-found here is the API misrouted: the same calm notice as an outage.
        var failure = FormFailure.From(result.Errors);
        if (failure.IsNotFound)
        {
            failure = new FormFailure([], FormCopy.Unavailable, StatusCodes.Status503ServiceUnavailable, false);
        }
        Errors = failure.Errors;
        Notice = failure.Notice;
        if (failure.Status != StatusCodes.Status200OK && Http.HttpContext is { Response.HasStarted: false } context)
        {
            context.Response.StatusCode = failure.Status;
        }
    }
}
