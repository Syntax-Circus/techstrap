using Microsoft.AspNetCore.Components;
using SyntaxCircus.Common;
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
    private SubmitGuard Guard { get; set; } = default!;

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

        // A double click asks for one email, not two: the id this form carried claims the write (D-045 09d addendum). The target never depends on the answer, so a repeat always goes to the same page.
        var sent = new SubmitTarget(PortalRoutes.LostLinkSent(Key));
        var email = form.Email!.Trim();
        var outcome = await Guard.RunAsync(
            SubmitKey.TryCreate(FormHandler, Key, form.SubmitId, SubmitContent.Digest([form.Email])),
            sent,
            async cancellation =>
            {
                var result = await Customers.RequestAccessLinkAsync(email, cancellation);
                return result.IsSuccess ? Result<SubmitTarget>.Success(sent) : Result<SubmitTarget>.Failure(result.Errors[0], [.. result.Errors.Skip(1)]);
            },
            RequestAborted);
        if (outcome.Status == SubmitStatus.Done)
        {
            Redirects.NavigateTo(outcome.Target.Path!);
            return;
        }

        // The route has no product or ticket for the API to not find, so a not-found here is the API misrouted: the same calm notice as an outage.
        var failure = outcome.Status == SubmitStatus.Failed ? FormFailure.From(outcome.Errors) : FormFailure.Unknown;
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
