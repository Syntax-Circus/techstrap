using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SyntaxCircus.Common;
using TechStrap.Application.Email;
using TechStrap.Application.Persistence;
using TechStrap.Application.Tickets.Notifications;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Application.Tickets.Customer;

public interface IRequestNewAccessLinkRequestHandler
{
    /// <summary>Success (no value) for every well-formed address, known or not (D-038); Validation only for a malformed address.</summary>
    Task<Result> HandleAsync(RequestNewAccessLinkRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// POST /api/customer/access-link. Every well-formed address runs the same count and lookup before the last branch, so a known and an
/// unknown address cost about the same and answer identically. Only a known, non-erased requester under the per-address cap with at
/// least one ticket gets an email, to the stored address. Nothing here logs the address or a token.
/// </summary>
public sealed class RequestNewAccessLinkRequestHandler(
    IRequesterRepository requesters,
    ITicketRepository tickets,
    IEmailOutboxStore outboxStore,
    ITicketNotificationPlanner planner,
    IUnitOfWork unitOfWork,
    TimeProvider clock,
    IOptions<LostLinkOptions> options,
    ILogger<RequestNewAccessLinkRequestHandler> logger) : IRequestNewAccessLinkRequestHandler
{
    public async Task<Result> HandleAsync(RequestNewAccessLinkRequest request, CancellationToken cancellationToken)
    {
        if (!CustomerEmailAddress.TryNormalize(request.Email, out var email))
        {
            return Result.Failure(CustomerErrors.EmailInvalid());
        }

        var limits = options.Value;
        var since = clock.GetUtcNow().AddMinutes(-limits.PerAddressWindowMinutes);
        var recent = await outboxStore.CountRecentAsync(EmailTemplates.AccessLinks, email, since, cancellationToken);
        var requester = await requesters.GetByEmailAsync(email, cancellationToken);
        if (recent >= limits.PerAddressLimit || requester is null || requester.IsErased)
        {
            return Ignored();
        }

        // Everything past the shared count and lookup runs for known requesters only, so a fault here must not surface as a 500
        // (that would tell the caller the address is known). Only cancellation escapes. The message is never logged.
        try
        {
            var links = await tickets.ListRecentTicketsForRequesterAsync(requester.Id, limits.MaxLinks, cancellationToken);
            if (links.Count == 0)
            {
                return Ignored();
            }

            await using var scope = await unitOfWork.BeginAsync(cancellationToken);
            await planner.PlanAccessLinksAsync(requester, links, cancellationToken);
            var committed = await scope.CommitAsync(cancellationToken);
            if (committed.IsFailure)
            {
                logger.LogWarning("Access-link request not saved ({Code})", committed.Errors[0].Code);
            }
        }
        catch (Exception exception) when (!(exception is OperationCanceledException && cancellationToken.IsCancellationRequested))
        {
            logger.LogWarning("Access-link request not completed ({ExceptionType})", exception.GetType().Name);
        }

        return Result.Success();
    }

    private Result Ignored()
    {
        logger.LogInformation("Access-link request ignored");
        return Result.Success();
    }
}
