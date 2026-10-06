using Microsoft.AspNetCore.Components;
using TechStrap.Portal.Clients;
using TechStrap.Portal.Forms;
using TechStrap.Portal.Products;
using TechStrap.Portal.Routing;

namespace TechStrap.Portal.Components.Pages;

/// <summary>
/// The contact form of a product (P09-T06, T21): a static-SSR form with antiforgery that creates a ticket through <see cref="IPublicTicketClient"/> and redirects to the "received" page (post, redirect, get: a
/// refresh never sends it twice). Review Focus 3: the antiforgery token is checked by the framework before the handler runs (a post without one is a 400); the request size limit is on the page and applies before
/// the form is read; the files are checked against <c>IntakeLimits</c> before anything is sent; the honeypot goes to the API as it arrived; a prefilled value from <c>?subject&amp;name&amp;email</c> is shown in the
/// visible, editable inputs and checked on post exactly like typed text, and nothing is ever submitted for the visitor. A failure keeps what the visitor wrote (but not their files: a browser never keeps those).
/// An unknown, inactive or malformed product is the uniform 404, before the handler runs, because the base class loads the product on every request including the post.
/// </summary>
public partial class Contact : ProductPageBase
{
    /// <summary>The form's handler name: the framework posts it with the form and binds <see cref="Form"/> only when it matches.</summary>
    public const string FormHandler = "contact";

    [SupplyParameterFromForm(FormName = FormHandler)]
    private ContactFormViewModel? Form { get; set; }

    [SupplyParameterFromQuery(Name = "subject")]
    private string? PrefillSubject { get; set; }

    [SupplyParameterFromQuery(Name = "name")]
    private string? PrefillName { get; set; }

    [SupplyParameterFromQuery(Name = "email")]
    private string? PrefillEmail { get; set; }

    [Inject]
    private IPublicTicketClient Tickets { get; set; } = default!;

    [Inject]
    private ReceivedReference References { get; set; } = default!;

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
            // A post binds Form; a first visit has none, so it opens with the prefill (empty when the query has none).
            Form ??= ContactFormViewModel.FromPrefill(PrefillSubject, PrefillName, PrefillEmail);
        }
    }

    private string? ErrorOf(string field) => Errors.FirstOrDefault(error => error.Field == field)?.Message;

    private async Task SubmitAsync()
    {
        if (Theme is null)
        {
            return;
        }

        var form = Form ??= new ContactFormViewModel();
        Errors = ContactFormValidator.Validate(form);
        if (Errors.Count > 0)
        {
            return;
        }

        var request = new NewTicketRequest(
            form.Email!.Trim(), form.Name!.Trim(), form.Subject!.Trim(), form.Body!.Trim(), form.Website, AttachmentRules.ToUploads(form.Files));
        var cancellation = Http.HttpContext?.RequestAborted ?? CancellationToken.None;
        var result = await Tickets.SubmitAsync(Key, request, cancellation);
        if (result.IsSuccess)
        {
            // Straight after the redirect: nothing else may run or render.
            Redirects.NavigateTo(PortalRoutes.ContactReceived(Key, References.Protect(Key, result.Value.TicketNumber)));
            return;
        }

        var failure = FormFailure.From(result.Errors);
        if (failure.IsNotFound)
        {
            NotFoundAfterTheming();
            return;
        }

        Errors = failure.Errors;
        Notice = failure.Notice;
        if (failure.Status != StatusCodes.Status200OK && Http.HttpContext is { Response.HasStarted: false } context)
        {
            context.Response.StatusCode = failure.Status;
        }
    }
}
