using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SyntaxCircus.Common;
using TechStrap.Application.Attachments;
using TechStrap.Application.Content;
using TechStrap.Application.Intake;
using TechStrap.Application.Persistence;
using TechStrap.Application.Results;
using TechStrap.Application.Security;
using TechStrap.Application.Tickets.Notifications;
using TechStrap.Contracts.Intake;
using TechStrap.Contracts.Tickets;
using TechStrap.Domain.Rules;
using TechStrap.Domain.Tickets;

namespace TechStrap.Application.Tickets.Customer;

public interface IAddCustomerReplyRequestHandler
{
    Task<Result<CustomerReplyResponse>> HandleAsync(
        string? token, AddCustomerReplyRequest request, IReadOnlyList<IncomingAttachment> attachments, CancellationToken cancellationToken);
}

/// <summary>
/// POST /api/customer/ticket/replies. A reply on an open or Solved ticket adds a message (a Solved ticket reopens); a reply on a Closed
/// ticket creates a follow-up ticket instead (FR-TKT-12). The same text within <see cref="FollowUpDedupeWindow"/> replays the existing
/// follow-up (D-037), and two racing replies converge on one follow-up because the loser's commit conflicts on the parent and its retry
/// finds the winner's follow-up. Every access failure is the uniform NotFound (D-038).
/// </summary>
public sealed class AddCustomerReplyRequestHandler(
    IAccessTokenService accessTokens,
    ITicketRepository tickets,
    IRequesterRepository requesters,
    ITicketNumberAllocator allocator,
    IAttachmentStore attachments,
    IHtmlSanitizer sanitizer,
    ITicketNotificationPlanner planner,
    IUnitOfWork unitOfWork,
    TimeProvider clock,
    IOptions<PortalLinkOptions> portal,
    ILogger<AddCustomerReplyRequestHandler> logger) : IAddCustomerReplyRequestHandler
{
    /// <summary>D-037: an identical reply on the same Closed ticket inside this window is the same submission, not a second follow-up.</summary>
    public static readonly TimeSpan FollowUpDedupeWindow = TimeSpan.FromMinutes(2);

    private const int MaxAttempts = 2;

    private readonly record struct Attempt(Result<CustomerReplyResponse> Result, bool Conflict);

    public async Task<Result<CustomerReplyResponse>> HandleAsync(
        string? token, AddCustomerReplyRequest request, IReadOnlyList<IncomingAttachment> files, CancellationToken cancellationToken)
    {
        // Shape errors reveal nothing about the token, so they come before any database work.
        if (request.Body is { } rawBody && rawBody.Length > DomainLimits.MessageBodyMaxLength)
        {
            return Fail(IntakeErrors.BodyTooLong());
        }

        if (files.Count > IntakeLimits.MaxFiles)
        {
            return Fail(IntakeErrors.TooManyFiles());
        }

        if (files.Sum(file => file.Length) > IntakeLimits.MaxMessageBytes)
        {
            return Fail(IntakeErrors.MessageTooLarge());
        }

        var html = sanitizer.Sanitize(CustomerText.ToHtml(request.Body ?? string.Empty));

        for (var attempt = 1; ; attempt++)
        {
            var stored = new StoredAttachmentBatch(attachments, logger);
            Attempt outcome;
            try
            {
                outcome = await AttemptAsync(token, html, files, stored, cancellationToken);
            }
            catch
            {
                await stored.DeleteAllAsync();
                throw;
            }

            if (!outcome.Conflict)
            {
                return outcome.Result;
            }

            // Streams may not be re-readable, so only a reply without files is retried as it stands.
            if (files.Count == 0 && attempt < MaxAttempts)
            {
                continue;
            }

            if (files.Count > 0 && await TryReplayInFreshScopeAsync(token, html, files, cancellationToken) is { } replayed)
            {
                return replayed;
            }

            return Fail(CustomerErrors.ReplyConflict());
        }
    }

    private async Task<Attempt> AttemptAsync(
        string? token, string html, IReadOnlyList<IncomingAttachment> files, StoredAttachmentBatch stored, CancellationToken cancellationToken)
    {
        await using var scope = await unitOfWork.BeginAsync(cancellationToken);

        var access = await CustomerAccess.ResolveAsync(token, accessTokens, tickets, requesters, clock, cancellationToken);
        if (access.IsFailure)
        {
            return Failed(CustomerErrors.NotFound());
        }

        var (accessToken, ticket, requester) = access.Value;
        if (accessToken.RecordUse(clock).IsFailure)
        {
            return Failed(CustomerErrors.NotFound());
        }

        tickets.UpdateAccessToken(accessToken);

        Result<CustomerReplyResponse> response;
        if (ticket.Status != TicketStatus.Closed)
        {
            var before = ticket.Status;
            var message = ticket.AddCustomerReply(requester.Id, html, clock);
            if (message.IsFailure)
            {
                return Failed(message.Error!.ToError());
            }

            var saved = await stored.SaveAllAsync(ticket.Id, message.Value, files, clock, cancellationToken);
            if (saved is not null)
            {
                return Failed(saved);
            }

            tickets.Update(ticket);
            var reopened = before is TicketStatus.Pending or TicketStatus.Solved && ticket.Status == TicketStatus.Open;
            await planner.PlanCustomerReplyAsync(ticket, reopened, cancellationToken);
            response = Result<CustomerReplyResponse>.Success(new CustomerReplyResponse(ticket.Number.ToString(), message.Value.Id, false, null));
        }
        else if (await ReplayAsync(ticket, requester.Id, html, files, cancellationToken) is { } replay)
        {
            if (replay.IsFailure)
            {
                return new Attempt(replay, false);
            }

            response = replay;
        }
        else
        {
            var allocated = await allocator.AllocateAsync(ticket.ProductId, cancellationToken);
            if (allocated.IsFailure)
            {
                return Failed(CustomerErrors.NotFound());
            }

            var created = ticket.CreateFollowUp(allocated.Value, clock);
            if (created.IsFailure)
            {
                return Failed(created.Error!.ToError());
            }

            var followUp = created.Value;
            var message = followUp.AddCustomerReply(requester.Id, html, clock);
            if (message.IsFailure)
            {
                return Failed(message.Error!.ToError());
            }

            var saved = await stored.SaveAllAsync(followUp.Id, message.Value, files, clock, cancellationToken);
            if (saved is not null)
            {
                return Failed(saved);
            }

            tickets.Add(followUp);
            tickets.Update(ticket);
            var link = IssueLink(followUp.Id, requester.Id);
            if (link.IsFailure)
            {
                await stored.DeleteAllAsync();
                return Failed(link.Errors[0]);
            }

            await planner.PlanFollowUpConfirmationAsync(followUp, cancellationToken);
            if (!followUp.IsSpam)
            {
                await planner.PlanNewTicketAsync(followUp, requester, isFollowUp: true, cancellationToken);
            }

            response = Result<CustomerReplyResponse>.Success(new CustomerReplyResponse(followUp.Number.ToString(), message.Value.Id, true, link.Value));
        }

        var committed = await TicketMutation.CommitAsync(scope, cancellationToken);
        if (committed.IsSuccess)
        {
            // The rows now reference these files: nothing after this point may delete them.
            stored.Keep();
            return new Attempt(response, false);
        }

        await stored.DeleteAllAsync();
        return FromCommitFailure(committed.Errors[0]);
    }

    /// <summary>The dedupe check for a Closed parent: a recent follow-up with the same body and the same attachment names (by count and name) is replayed with a fresh token, or null when there is none.
    /// A different file set is a new submission, so it creates a new follow-up instead of dropping its files.</summary>
    private async Task<Result<CustomerReplyResponse>?> ReplayAsync(
        Ticket parent, Guid requesterId, string html, IReadOnlyList<IncomingAttachment> files, CancellationToken cancellationToken)
    {
        var candidates = await tickets.ListRecentFollowUpsAsync(parent.Id, clock.GetUtcNow() - FollowUpDedupeWindow, cancellationToken);
        var match = candidates.FirstOrDefault(candidate => candidate.FirstMessageBody == html && SameFiles(candidate.FirstMessageFileNames, files));
        if (match is null)
        {
            return null;
        }

        var link = IssueLink(match.TicketId, requesterId);
        return link.IsFailure
            ? Result<CustomerReplyResponse>.Failure(link.Errors[0])
            : Result<CustomerReplyResponse>.Success(new CustomerReplyResponse(match.Number, match.FirstMessageId, true, link.Value));
    }

    private static bool SameFiles(IReadOnlyList<string> existing, IReadOnlyList<IncomingAttachment> incoming) =>
        existing.Count == incoming.Count
        && existing.Order(StringComparer.Ordinal).SequenceEqual(incoming.Select(file => AttachmentFileName.Sanitize(file.FileName)).Order(StringComparer.Ordinal), StringComparer.Ordinal);

    /// <summary>After a conflict with files (which cannot be re-read): the winner may be the same text, so look once more on a clean read.</summary>
    private async Task<Result<CustomerReplyResponse>?> TryReplayInFreshScopeAsync(
        string? token, string html, IReadOnlyList<IncomingAttachment> files, CancellationToken cancellationToken)
    {
        await using var scope = await unitOfWork.BeginAsync(cancellationToken);

        var access = await CustomerAccess.ResolveAsync(token, accessTokens, tickets, requesters, clock, cancellationToken);
        if (access.IsFailure || access.Value.Ticket.Status != TicketStatus.Closed)
        {
            return null;
        }

        if (access.Value.Token.RecordUse(clock).IsFailure)
        {
            return null;
        }

        tickets.UpdateAccessToken(access.Value.Token);

        if (await ReplayAsync(access.Value.Ticket, access.Value.Requester.Id, html, files, cancellationToken) is not { } replay)
        {
            return null;
        }

        if (replay.IsFailure)
        {
            return replay;
        }

        var committed = await TicketMutation.CommitAsync(scope, cancellationToken);
        return committed.IsSuccess ? replay : null;
    }

    private Result<string> IssueLink(Guid ticketId, Guid requesterId)
    {
        var issued = accessTokens.Issue(ticketId, requesterId);
        if (issued.IsFailure)
        {
            return Result<string>.Failure(issued.Error!.ToError());
        }

        tickets.AddAccessToken(issued.Value.Token);
        return Result<string>.Success(portal.Value.TicketLink(issued.Value.PlaintextToken));
    }

    private static Attempt FromCommitFailure(ResultError error) => error.Code switch
    {
        PersistenceErrorCodes.ConcurrencyConflict => new Attempt(Result<CustomerReplyResponse>.Failure(CustomerErrors.ReplyConflict()), true),
        PersistenceErrorCodes.ReferenceViolation => Failed(CustomerErrors.NotFound()),
        _ => Failed(error),
    };

    private static Attempt Failed(ResultError error) => new(Fail(error), false);

    private static Result<CustomerReplyResponse> Fail(ResultError error) => Result<CustomerReplyResponse>.Failure(error);
}
