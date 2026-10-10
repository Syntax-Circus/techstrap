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
/// A public agent reply: Markdown rendered then sanitized, attachments saved (with compensation if anything later fails), linked KB
/// articles, an optional "send and solve", and the customer email planned in the same unit of work. A linked article must be Published and
/// visible to the ticket (shared, or in the ticket's own product) and have a category, because the customer email links to its portal
/// page; anything else is a 400 <c>kb-article-not-linkable</c> and nothing is stored or sent (D-044).
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

        var solve = false;
        if (request.StatusAfter is not null)
        {
            if (!TicketNameParser.TryStatus(request.StatusAfter, out var statusAfter) || statusAfter is not (TicketStatus.Pending or TicketStatus.Solved))
            {
                return Fail(TicketErrors.Invalid("statusAfter", "status-after-invalid", "statusAfter must be Pending or Solved."));
            }

            solve = statusAfter == TicketStatus.Solved;
        }

        await using var scope = await unitOfWork.BeginAsync(cancellationToken);

        var loaded = await TicketMutation.LoadAsync(ticketId, request.RowVersion, rowVersionRequired: false, currentAgent, agents, tickets, cancellationToken);
        if (loaded.IsFailure)
        {
            return Fail(loaded.Errors[0]);
        }

        var (agent, ticket) = loaded.Value;

        var linked = new List<LinkedArticleDto>();
        var emailLinks = new List<ReplyArticleLink>();
        foreach (var articleId in articleIds)
        {
            var article = await kb.GetArticleAsync(articleId, cancellationToken);
            if (article is null)
            {
                return Fail(TicketErrors.Invalid("linkedArticleIds", "article-not-found", "One of the linked articles does not exist. Remove it and try again."));
            }

            var category = article.CategoryId is { } categoryId ? await kb.GetCategoryAsync(categoryId, cancellationToken) : null;
            if (category is null || !IsLinkable(article, category, ticket.ProductId))
            {
                return Fail(TicketErrors.Invalid(
                    "linkedArticleIds", "kb-article-not-linkable", "One of the linked articles is not published for this ticket's product. Remove it and try again."));
            }

            linked.Add(new LinkedArticleDto(article.Id, article.Title, article.Slug));
            emailLinks.Add(new ReplyArticleLink(article.Title, category.Slug, article.Slug, article.Id));
        }

        var html = sanitizer.Sanitize(markdown.ToHtml(request.Body ?? string.Empty));
        var message = ticket.AddAgentReply(agent.Id, html, clock);
        if (message.IsFailure)
        {
            return Fail(message.Error!.ToError());
        }

        var stored = new StoredAttachmentBatch(attachments, logger);
        try
        {
            var failed = await stored.SaveAllAsync(ticket.Id, message.Value, files, clock, cancellationToken);
            if (failed is not null)
            {
                return Fail(failed);
            }

            if (solve)
            {
                var solved = ticket.ChangeStatus(TicketStatus.Solved, Actor.ForAgent(agent.Id), clock);
                if (solved.IsFailure)
                {
                    await stored.DeleteAllAsync();
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

            await planner.PlanAgentReplyAsync(ticket, message.Value, agent, solve, emailLinks, cancellationToken);

            var committed = await TicketMutation.CommitAsync(scope, cancellationToken);
            if (committed.IsFailure)
            {
                await stored.DeleteAllAsync();
                return Fail(committed.Errors[0]);
            }

            // The reply is committed: nothing after this point (state read, DTO building) may delete its files.
            stored.Keep();
            var state = await TicketMutation.ReadStateAsync(ticket.Id, tickets, cancellationToken);
            if (state.IsFailure)
            {
                return Fail(state.Errors[0]);
            }

            var dto = new MessageDto(
                message.Value.Id, message.Value.AuthorType.ToWire(), message.Value.AuthorId, agent.Name ?? agent.Email, message.Value.Visibility.ToWire(),
                message.Value.Body, message.Value.CreatedAt, attachmentDtos, linked);
            return Result<AgentMessageResponse>.Success(new AgentMessageResponse(dto, state.Value));
        }
        catch
        {
            await stored.DeleteAllAsync();
            throw;
        }
    }

    // Published, in the ticket's product or shared, and filed in a category the same product can see (the portal address carries the category slug).
    private static bool IsLinkable(KbArticle article, KbCategory? category, Guid ticketProductId) =>
        article.Status == KbArticleStatus.Published
        && (article.ProductId is null || article.ProductId == ticketProductId)
        && category is not null
        && (category.ProductId is null || category.ProductId == ticketProductId);

    private static Result<AgentMessageResponse> Fail(ResultError error) => Result<AgentMessageResponse>.Failure(error);
}
