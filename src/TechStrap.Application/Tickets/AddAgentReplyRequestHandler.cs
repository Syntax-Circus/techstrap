using Microsoft.Extensions.Logging;
using SyntaxCircus.Common;
using TechStrap.Application.Agents;
using TechStrap.Application.Attachments;
using TechStrap.Application.Content;
using TechStrap.Application.Intake;
using TechStrap.Application.Persistence;
using TechStrap.Application.Results;
using TechStrap.Application.Tickets.Notifications;
using TechStrap.Contracts.Intake;
using TechStrap.Contracts.Tickets;
using TechStrap.Domain.Knowledge;
using TechStrap.Domain.Rules;
using TechStrap.Domain.Tickets;

namespace TechStrap.Application.Tickets;

public interface IAddAgentReplyRequestHandler
{
    Task<Result<AgentMessageResponse>> HandleAsync(
        Guid ticketId, AddAgentReplyRequest request, IReadOnlyList<IncomingAttachment> attachments, CancellationToken cancellationToken);
}

/// <summary>
/// A public agent reply: Markdown rendered then sanitised, attachments saved (with compensation if anything later fails), linked KB
/// articles, an optional "send and solve", and the customer email planned in the same unit of work.
/// </summary>
public sealed class AddAgentReplyRequestHandler(
    ICurrentAgentClaims currentAgent,
    IAgentRepository agents,
    ITicketRepository tickets,
    IKbRepository kb,
    IAttachmentStore attachments,
    IMarkdownRenderer markdown,
    IHtmlSanitizer sanitizer,
    ITicketNotificationPlanner planner,
    IUnitOfWork unitOfWork,
    TimeProvider clock,
    ILogger<AddAgentReplyRequestHandler> logger) : IAddAgentReplyRequestHandler
{
    public async Task<Result<AgentMessageResponse>> HandleAsync(
        Guid ticketId, AddAgentReplyRequest request, IReadOnlyList<IncomingAttachment> files, CancellationToken cancellationToken)
    {
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

        var articleIds = (request.LinkedArticleIds ?? []).Distinct().ToList();
        if (articleIds.Count > TicketOperationLimits.MaxLinkedArticles)
        {
            return Fail(TicketErrors.Invalid("linkedArticleIds", "linked-articles-too-many", $"Link at most {TicketOperationLimits.MaxLinkedArticles} articles to one reply."));
        }

        bool solve;
        switch (request.StatusAfter)
        {
            case null or "Pending":
                solve = false;
                break;
            case "Solved":
                solve = true;
                break;
            default:
                return Fail(TicketErrors.Invalid("statusAfter", "status-after-invalid", "statusAfter must be Pending or Solved."));
        }

        await using var scope = await unitOfWork.BeginAsync(cancellationToken);

        var loaded = await TicketMutation.LoadAsync(ticketId, request.RowVersion, rowVersionRequired: false, currentAgent, agents, tickets, cancellationToken);
        if (loaded.IsFailure)
        {
            return Fail(loaded.Errors[0]);
        }

        var (agent, ticket) = loaded.Value;

        var linked = new List<LinkedArticleDto>();
        foreach (var articleId in articleIds)
        {
            var article = await kb.GetArticleAsync(articleId, cancellationToken);
            if (article is null)
            {
                return Fail(TicketErrors.Invalid("linkedArticleIds", "article-not-found", "One of the linked articles does not exist. Remove it and try again."));
            }

            linked.Add(new LinkedArticleDto(article.Id, article.Title, article.Slug));
        }

        var html = sanitizer.Sanitize(markdown.ToHtml(request.Body ?? string.Empty));
        var message = ticket.AddAgentReply(agent.Id, html, clock);
        if (message.IsFailure)
        {
            return Fail(message.Error!.ToError());
        }

        var stored = new List<string>();
        try
        {
            foreach (var file in files)
            {
                var saved = await attachments.SaveAsync(ticket.Id, file, cancellationToken);
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

            if (solve)
            {
                var solved = ticket.ChangeStatus(TicketStatus.Solved, Actor.ForAgent(agent.Id), clock);
                if (solved.IsFailure)
                {
                    await DeleteStoredAsync(stored);
                    return Fail(solved.Error!.ToError());
                }
            }

            // Staging the ticket accepts its pending changes and clears NewAttachments, so the DTOs are built first.
            var attachmentDtos = message.Value.NewAttachments.Select(attachment => attachment.ToDto()).ToList();

            tickets.Update(ticket);
            foreach (var articleId in articleIds)
            {
                kb.AddTicketArticle(new TicketArticle(ticket.Id, message.Value.Id, articleId));
            }

            await planner.PlanAgentReplyAsync(ticket, message.Value, agent, solve, cancellationToken);

            var committed = await TicketMutation.CommitAsync(scope, ticket.Id, tickets, cancellationToken);
            if (committed.IsFailure)
            {
                await DeleteStoredAsync(stored);
                return Fail(committed.Errors[0]);
            }

            // A failure after commit (for example building the DTO) must not delete committed files.
            stored.Clear();
            var dto = new MessageDto(
                message.Value.Id, message.Value.AuthorType.ToWire(), message.Value.AuthorId, agent.Name ?? agent.Email, message.Value.Visibility.ToWire(),
                message.Value.Body, message.Value.CreatedAt, attachmentDtos, linked);
            return Result<AgentMessageResponse>.Success(new AgentMessageResponse(dto, committed.Value));
        }
        catch
        {
            await DeleteStoredAsync(stored);
            throw;
        }
    }

    private static Result<AgentMessageResponse> Fail(ResultError error) => Result<AgentMessageResponse>.Failure(error);

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
}
