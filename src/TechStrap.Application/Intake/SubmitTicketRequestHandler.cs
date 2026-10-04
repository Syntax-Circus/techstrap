using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SyntaxCircus.Common;
using TechStrap.Application.Attachments;
using TechStrap.Application.Content;
using TechStrap.Application.Email;
using TechStrap.Application.Persistence;
using TechStrap.Application.Results;
using TechStrap.Application.Security;
using TechStrap.Application.Tickets.Notifications;
using TechStrap.Contracts.Intake;
using TechStrap.Domain.Outbox;
using TechStrap.Domain.Products;
using TechStrap.Domain.Requesters;
using TechStrap.Domain.Rules;
using TechStrap.Domain.Tickets;

namespace TechStrap.Application.Intake;

public interface ISubmitTicketRequestHandler
{
    Task<Result<SubmitTicketResponse>> HandleAsync(SubmitTicketRequest request, SubmitTicketContext context, CancellationToken cancellationToken);
}

/// <summary>
/// The one handler behind every ticket-submission route: web form, trusted key and public key (D-016, D-020, D-032, D-033).
/// Creates (or reuses) the requester, the ticket with its first message and attachments, the customer access token and the
/// confirmation outbox row in one unit of work. A commit that loses a unique-index race ("duplicate") is retried once.
/// </summary>
public sealed class SubmitTicketRequestHandler(
    IProductRepository products,
    IRequesterRepository requesters,
    ITicketRepository tickets,
    ITicketNumberAllocator allocator,
    IAccessTokenService tokens,
    IAttachmentStore attachments,
    IHtmlSanitizer sanitizer,
    IEmailOutbox outbox,
    ITicketNotificationPlanner planner,
    IIntakeIdempotencyStore idempotency,
    IUnitOfWork unitOfWork,
    TimeProvider clock,
    IOptions<PortalLinkOptions> portal,
    ILogger<SubmitTicketRequestHandler> logger) : ISubmitTicketRequestHandler
{
    private const int MaxAttempts = 2;
    private const int PruneBatch = 100;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly record struct Attempt(Result<SubmitTicketResponse> Result, bool RetryOnDuplicate, bool Prune);

    public async Task<Result<SubmitTicketResponse>> HandleAsync(SubmitTicketRequest request, SubmitTicketContext context, CancellationToken cancellationToken)
    {
        if (Validate(request, context) is { } invalid)
        {
            return Result<SubmitTicketResponse>.Failure(invalid);
        }

        var product = context.Channel == IntakeChannel.Api
            ? await products.GetByIdAsync(context.ProductId!.Value, cancellationToken)
            : await products.GetByKeyAsync(context.ProductKey ?? "", cancellationToken);
        if (product is null || !product.IsActive)
        {
            return Result<SubmitTicketResponse>.Failure(IntakeErrors.ProductNotFound());
        }

        if (context.HoneypotTripped)
        {
            return Result<SubmitTicketResponse>.Success(new SubmitTicketResponse($"{product.NumberPrefix}-{Random.Shared.Next(1000, 100000)}", null, []));
        }

        var warnings = new List<string>();
        if (!context.Trusted && !string.IsNullOrWhiteSpace(request.ExternalUserRef))
        {
            warnings.Add(IntakeWarnings.ExternalUserRefIgnored);
        }

        var bodyHtml = sanitizer.Sanitize(CustomerText.ToHtml(request.Body ?? ""));
        var metadataJson = request.Metadata is { Count: > 0 } ? JsonSerializer.Serialize(request.Metadata, JsonOptions) : null;

        for (var attempt = 1; ; attempt++)
        {
            var stored = new List<string>();
            Attempt outcome;
            try
            {
                outcome = await AttemptAsync(request, context, product, warnings, bodyHtml, metadataJson, stored, cancellationToken);
            }
            catch
            {
                await DeleteStoredAsync(stored);
                throw;
            }

            if (outcome.RetryOnDuplicate && attempt < MaxAttempts)
            {
                continue;
            }

            // The scope is disposed by now: PruneAsync deletes with its own statement and must not run on the completed transaction.
            if (outcome.Prune)
            {
                await PruneAsync(cancellationToken);
            }

            return outcome.Result;
        }
    }

    private static ResultError? Validate(SubmitTicketRequest request, SubmitTicketContext context)
    {
        // Before CustomerText and the sanitizer expand the body, so a huge body never reaches them.
        if (request.Body is { } rawBody && rawBody.Length > DomainLimits.MessageBodyMaxLength)
        {
            return IntakeErrors.BodyTooLong();
        }

        if (context.Attachments.Count > IntakeLimits.MaxFiles)
        {
            return IntakeErrors.TooManyFiles();
        }

        if (context.Attachments.Sum(file => file.Length) > IntakeLimits.MaxMessageBytes)
        {
            return IntakeErrors.MessageTooLarge();
        }

        if (request.Metadata is { } metadata)
        {
            if (metadata.Count > IntakeLimits.MaxMetadataKeys)
            {
                return IntakeErrors.MetadataInvalid($"Send at most {IntakeLimits.MaxMetadataKeys} metadata entries.");
            }

            foreach (var (key, value) in metadata)
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    return IntakeErrors.MetadataInvalid("Metadata keys cannot be blank.");
                }

                if (key.Length > IntakeLimits.MaxMetadataKeyLength)
                {
                    return IntakeErrors.MetadataInvalid($"A metadata key can be at most {IntakeLimits.MaxMetadataKeyLength} characters.");
                }

                if (value is not null && value.Length > IntakeLimits.MaxMetadataValueLength)
                {
                    return IntakeErrors.MetadataInvalid($"A metadata value can be at most {IntakeLimits.MaxMetadataValueLength} characters.");
                }
            }
        }

        if (context.IdempotencyKey is { } idempotencyKey
            && (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > IntakeLimits.MaxIdempotencyKeyLength))
        {
            return IntakeErrors.IdempotencyKeyInvalid();
        }

        if (context.Channel == IntakeChannel.Api && (context.ProductId is null || context.ApiKeyId is null))
        {
            return IntakeErrors.ApiKeyRequired();
        }

        return null;
    }

    private async Task<Attempt> AttemptAsync(
        SubmitTicketRequest request,
        SubmitTicketContext context,
        Product product,
        List<string> warnings,
        string bodyHtml,
        string? metadataJson,
        List<string> stored,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var isApi = context.Channel == IntakeChannel.Api;
        var idempotencyKey = isApi ? context.IdempotencyKey : null;

        await using var scope = await unitOfWork.BeginAsync(cancellationToken);

        if (idempotencyKey is not null)
        {
            var entry = await idempotency.FindAsync(context.ApiKeyId!.Value, idempotencyKey, cancellationToken);
            if (entry is not null)
            {
                if (entry.CreatedAt > now - IIntakeIdempotencyStore.Retention
                    && await tickets.GetByIdAsync(entry.TicketId, cancellationToken) is { } original
                    && JsonSerializer.Deserialize<SubmitTicketResponse>(entry.ResponseJson, JsonOptions) is { } storedResponse)
                {
                    return await ReplayAsync(scope, original, storedResponse, cancellationToken);
                }

                idempotency.Remove(entry);
            }
        }

        var requester = await UpsertRequesterAsync(request, context, cancellationToken);
        if (requester.IsFailure)
        {
            return Fail(requester.Errors[0]);
        }

        var allocated = await allocator.AllocateAsync(product.Id, cancellationToken);
        if (allocated.IsFailure)
        {
            return Fail(allocated.Errors[0]);
        }

        var number = allocated.Value;
        var channel = isApi ? TicketChannel.Api : TicketChannel.Web;
        var ticket = Ticket.Create(number, product.Id, requester.Value.Id, request.Subject, channel, metadataJson, context.Trusted, clock);
        if (ticket.IsFailure)
        {
            return Fail(ticket.Error!.ToError());
        }

        var message = ticket.Value.AddCustomerReply(requester.Value.Id, bodyHtml, clock);
        if (message.IsFailure)
        {
            return Fail(message.Error!.ToError());
        }

        foreach (var file in context.Attachments)
        {
            var saved = await attachments.SaveAsync(ticket.Value.Id, file, cancellationToken);
            if (saved.IsFailure)
            {
                await DeleteStoredAsync(stored);
                return Fail(saved.Errors[0]);
            }

            stored.Add(saved.Value.StorageKey);
            var added = message.Value.AddAttachment(saved.Value.FileName, saved.Value.ContentType, saved.Value.Size, saved.Value.StorageKey, clock);
            if (added.IsFailure)
            {
                await DeleteStoredAsync(stored);
                return Fail(added.Error!.ToError());
            }
        }

        tickets.Add(ticket.Value);

        var issued = tokens.Issue(ticket.Value.Id, requester.Value.Id);
        if (issued.IsFailure)
        {
            await DeleteStoredAsync(stored);
            return Fail(issued.Error!.ToError());
        }

        tickets.AddAccessToken(issued.Value.Token);
        var link = portal.Value.TicketLink(issued.Value.PlaintextToken);

        EnqueueConfirmation(number, ticket.Value, requester.Value, product, link);
        await planner.PlanNewTicketAsync(ticket.Value, requester.Value, isFollowUp: false, cancellationToken);

        var response = new SubmitTicketResponse(number.ToString(), isApi ? link : null, warnings.ToArray());

        if (isApi)
        {
            var apiKey = await products.GetApiKeyAsync(context.ApiKeyId!.Value, cancellationToken);
            if (apiKey is not null && apiKey.RecordUse(clock).IsSuccess)
            {
                products.UpdateApiKey(apiKey);
            }

            if (idempotencyKey is not null)
            {
                // The link holds a plaintext access token, which must never be stored outside email_outbox.payload (D-033).
                idempotency.Add(context.ApiKeyId.Value, idempotencyKey, ticket.Value.Id, JsonSerializer.Serialize(response with { ViewUrl = null }, JsonOptions), now);
            }
        }

        var committed = await scope.CommitAsync(cancellationToken);
        if (committed.IsSuccess)
        {
            // Committed rows now reference these files: an exception from scope disposal must not delete them.
            stored.Clear();
            return new Attempt(Result<SubmitTicketResponse>.Success(response), false, idempotencyKey is not null);
        }

        await DeleteStoredAsync(stored);
        var error = committed.Errors[0];

        // Attachment streams may not be seekable, so a retry is only safe without files; the customer can resubmit.
        return new Attempt(
            Result<SubmitTicketResponse>.Failure(error),
            error.Code == PersistenceErrorCodes.Duplicate && context.Attachments.Count == 0,
            false);
    }

    private async Task<Attempt> ReplayAsync(IUnitOfWorkScope scope, Ticket original, SubmitTicketResponse storedResponse, CancellationToken cancellationToken)
    {
        var issued = tokens.Issue(original.Id, original.RequesterId);
        if (issued.IsFailure)
        {
            return Fail(issued.Error!.ToError());
        }

        tickets.AddAccessToken(issued.Value.Token);
        var committed = await scope.CommitAsync(cancellationToken);
        return committed.IsSuccess
            ? new Attempt(Result<SubmitTicketResponse>.Success(storedResponse with { ViewUrl = portal.Value.TicketLink(issued.Value.PlaintextToken) }), false, false)
            : Fail(committed.Errors[0]);
    }

    private async Task<Result<Requester>> UpsertRequesterAsync(SubmitTicketRequest request, SubmitTicketContext context, CancellationToken cancellationToken)
    {
        var email = request.Email?.Trim() ?? "";
        var name = string.IsNullOrWhiteSpace(request.Name) ? null : request.Name.Trim();
        var externalRef = request.ExternalUserRef;

        var existing = await requesters.GetByEmailAsync(email, cancellationToken);
        if (existing is null)
        {
            var created = Requester.Create(email, name, context.Trusted ? externalRef : null, clock);
            if (created.IsFailure)
            {
                return Result<Requester>.Failure(created.Error!.ToError());
            }

            requesters.Add(created.Value);
            return Result<Requester>.Success(created.Value);
        }

        var newName = existing.Name ?? name;
        var newRef = context.Trusted && !string.IsNullOrWhiteSpace(externalRef) ? externalRef : existing.ExternalUserRef;
        if (newName != existing.Name || newRef != existing.ExternalUserRef)
        {
            var updated = existing.UpdateProfile(newName, newRef);
            if (updated.IsFailure)
            {
                return Result<Requester>.Failure(updated.Error!.ToError());
            }

            requesters.Update(existing);
        }

        return Result<Requester>.Success(existing);
    }

    private void EnqueueConfirmation(TicketNumber number, Ticket ticket, Requester requester, Product product, string link)
    {
        var payload = JsonSerializer.Serialize(new TicketConfirmationEmail(number.ToString(), ticket.Subject, requester.Name, link, null), JsonOptions);
        var item = EmailOutboxItem.Enqueue(EmailTemplates.TicketConfirmation, requester.Email, payload, product.Id, ticket.Id, clock);
        if (item.IsSuccess)
        {
            outbox.Enqueue(item.Value);
        }
        else
        {
            // A failed email never fails the submission. Log the code only: the payload holds the plaintext link.
            logger.LogWarning("Ticket confirmation was not queued ({Code}).", item.Error!.Code);
        }
    }

    private async Task PruneAsync(CancellationToken cancellationToken)
    {
        try
        {
            await idempotency.PruneAsync(clock.GetUtcNow() - IIntakeIdempotencyStore.Retention, PruneBatch, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning("Pruning expired idempotency keys failed ({ExceptionType}).", ex.GetType().Name);
        }
    }

    private async Task DeleteStoredAsync(List<string> stored)
    {
        foreach (var key in stored)
        {
            try
            {
                await attachments.DeleteAsync(key, CancellationToken.None);
            }
            catch (Exception ex)
            {
                // Best effort: an orphaned file must not mask the original outcome, but it must be visible.
                logger.LogWarning("Attachment cleanup failed for {StorageKey} ({ExceptionType}).", key, ex.GetType().Name);
            }
        }

        stored.Clear();
    }

    private static Attempt Fail(ResultError error) => new(Result<SubmitTicketResponse>.Failure(error), false, false);
}
