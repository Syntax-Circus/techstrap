using Microsoft.AspNetCore.Components;
using SyntaxCircus.Common;
using TechStrap.Portal.Clients;
using TechStrap.Portal.Forms;
using TechStrap.Portal.Products;
using TechStrap.Portal.Routing;
using TechStrap.Portal.Tickets;

namespace TechStrap.Portal.Components.Pages;

/// <summary>
/// The customer's ticket at <c>/t/{token}</c> (P09-T08, T09, T23): the public conversation, the status in the customer's words and the reply form, themed with the product the ticket belongs to. Review Focus 1: the
/// token stays in the path and in the <c>X-Ticket-Token</c> header of the API call; it is parsed first (a malformed one is the uniform 404 and the API is never asked), it is never put in a query, a log or a link built
/// from its printed form, and the redirects are built by <see cref="PortalRoutes"/>. Review Focus 2: an unknown, expired or revoked token is the same 404 as a malformed one. An inactive or unknown product is not a
/// 404: the ticket shows in the neutral theme. Reply: redirect after post to the same page, or, when the API started a follow-up (a reply to a Closed ticket), to the follow-up's own page, whose token is read out of
/// the API's link and checked (<see cref="FollowUpLink"/>); if it cannot be read the page says the follow-up was started and sends the visitor nowhere.
/// </summary>
public partial class Ticket
{
    public const string ReplyHandler = "reply";

    [Parameter]
    public string Token { get; set; } = string.Empty;

    [SupplyParameterFromForm(FormName = ReplyHandler)]
    private ReplyFormViewModel? Reply { get; set; }

    [Inject]
    private ICustomerTicketClient Tickets { get; set; } = default!;

    [Inject]
    private IPublicProductClient Products { get; set; } = default!;

    [Inject]
    private ProductScope Scope { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [Inject]
    private PortalLinks Links { get; set; } = default!;

    [Inject]
    private SubmitGuard Guard { get; set; } = default!;

    [Inject]
    private IHostEnvironment Environment { get; set; } = default!;

    [Inject]
    private IHttpContextAccessor Http { get; set; } = default!;

    private TicketToken _token;

    private CustomerTicketViewModel? Model { get; set; }

    private string? UnavailableMessage { get; set; }

    private IReadOnlyList<FormError> Errors { get; set; } = [];

    private string? Notice { get; set; }

    private bool FollowUpConfirmation { get; set; }

    private CancellationToken Cancellation => Http.HttpContext?.RequestAborted ?? CancellationToken.None;

    protected override async Task OnInitializedAsync()
    {
        if (!TicketToken.TryParse(Token, out _token))
        {
            Navigation.NotFound();
            return;
        }

        var result = await Tickets.GetAsync(_token, Cancellation);
        if (result.IsFailure)
        {
            var failure = FormFailure.From(result.Errors);
            if (failure.IsNotFound)
            {
                Navigation.NotFound();
                return;
            }

            UnavailableMessage = failure.Status == StatusCodes.Status429TooManyRequests ? ProblemCopy.RateLimited : TicketCopy.Unavailable;
            SetStatus(failure.Status);
            return;
        }

        Model = CustomerTicketPresenter.Present(result.Value, _token);

        // The link carries no product, so the page asks for the ticket's own. A product that is inactive or unknown is the neutral theme, never a 404: the customer still has a ticket.
        var product = await Products.GetAsync(result.Value.ProductKey, Cancellation);
        if (product.IsSuccess)
        {
            Scope.Set(ProductThemeViewModel.From(product.Value, Environment.IsDevelopment()));
        }

        Reply ??= new ReplyFormViewModel();
    }

    private string? ErrorOf(string field) => Errors.FirstOrDefault(error => error.Field == field)?.Message;

    private async Task ReplyAsync()
    {
        if (Model is null)
        {
            return;
        }

        var form = Reply ??= new ReplyFormViewModel();
        Errors = ReplyFormValidator.Validate(form);
        if (Errors.Count > 0)
        {
            return;
        }

        // A double click sends one reply: the id this form carried, with the ticket's token, claims the write (D-045 09d addendum). A repeat goes where the first went (the same page, or the follow-up's page), or back to this
        // ticket's page when the first's answer is unknown. The stored target can be a follow-up's access token: it stays in memory, for <see cref="SubmitGuard.Lifetime"/>, and is never logged.
        var reply = new CustomerReply(form.Body!.Trim(), AttachmentRules.ToUploads(form.Files));
        var outcome = await Guard.RunAsync(
            SubmitKey.TryCreate(ReplyHandler, _token.Value, form.SubmitId, SubmitContent.Digest([form.Body], form.Files), foldScope: false),
            new SubmitTarget(Links.Ticket(_token)),
            async cancellation =>
            {
                var result = await Tickets.ReplyAsync(_token, reply, cancellation);
                if (result.IsFailure)
                {
                    return Result<SubmitTarget>.Failure(result.Errors[0], [.. result.Errors.Skip(1)]);
                }

                if (!result.Value.FollowUpCreated)
                {
                    return Result<SubmitTarget>.Success(new SubmitTarget(Links.Ticket(_token)));
                }

                return Result<SubmitTarget>.Success(FollowUpLink.TryGetToken(result.Value.FollowUpViewUrl, out var followUp) ? new SubmitTarget(Links.Ticket(followUp)) : SubmitTarget.None);
            },
            Cancellation);
        if (outcome.Status == SubmitStatus.Done)
        {
            // Straight after each redirect: nothing else may run or render.
            if (outcome.Target.Path is { } path)
            {
                Navigation.NavigateTo(path);
                return;
            }

            FollowUpConfirmation = true;
            return;
        }

        var failure = outcome.Status == SubmitStatus.Failed ? FormFailure.From(outcome.Errors) : FormFailure.Unknown;
        if (failure.IsNotFound)
        {
            Scope.Clear();
            Navigation.NotFound();
            return;
        }

        Errors = failure.Errors;
        Notice = failure.Notice;
        SetStatus(failure.Status);
    }

    private void SetStatus(int status)
    {
        if (status != StatusCodes.Status200OK && Http.HttpContext is { Response.HasStarted: false } context)
        {
            context.Response.StatusCode = status;
        }
    }
}
