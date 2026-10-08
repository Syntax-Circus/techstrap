using Microsoft.AspNetCore.Components;
using SyntaxCircus.Common;
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
/// An unknown, inactive or malformed product ends in <c>NotFound()</c> before the handler runs, because the base class loads the product on every request including the post: a GET is the uniform 404 page, and a post gets
/// the framework's plain-text 400 ("Cannot submit the form ... no form on the page"), because the form was never rendered. Nothing is created either way. A product that vanishes after it was loaded (the API answers the
/// post with a 404) is the neutral 404, because the product is forgotten first (<see cref="ProductPageBase"/>).
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

        // A double click sends once: the id this form carried claims the write (D-045 09d addendum). A repeat is sent where the first went, or to the received page without a reference when the first's answer is unknown.
        var outcome = await Guard.RunAsync(
            SubmitKey.TryCreate(FormHandler, Key, form.SubmitId, SubmitContent.Digest([form.Name, form.Email, form.Subject, form.Body], form.Files)),
            new SubmitTarget(Links.ContactReceived(Key)),
            async cancellation =>
            {
                var result = await Tickets.SubmitAsync(Key, request, cancellation);
                return result.IsSuccess
                    ? Result<SubmitTarget>.Success(new SubmitTarget(Links.ContactReceived(Key, References.Protect(Key, result.Value.TicketNumber))))
                    : Result<SubmitTarget>.Failure(result.Errors[0], [.. result.Errors.Skip(1)]);
            },
            RequestAborted);
        if (outcome.Status == SubmitStatus.Done)
        {
            // Straight after the redirect: nothing else may run or render.
            Redirects.NavigateTo(outcome.Target.Path!);
            return;
        }

        var failure = outcome.Status == SubmitStatus.Failed ? FormFailure.From(outcome.Errors) : FormFailure.Unknown;
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
