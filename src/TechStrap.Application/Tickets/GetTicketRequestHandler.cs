using SyntaxCircus.Common;
using TechStrap.Application.Agents;
using TechStrap.Application.Persistence;
using TechStrap.Contracts.Tickets;
using TechStrap.Domain.Tickets;

namespace TechStrap.Application.Tickets;

public interface IGetTicketRequestHandler
{
    /// <param name="reference">A ticket id (Guid) or a ticket number such as ORB-42.</param>
    Task<Result<TicketDetailDto>> HandleAsync(string reference, CancellationToken cancellationToken);
}

/// <summary>
/// GET /api/tickets/{reference}. The agent-only ticket detail: every message (internal notes included) and every event, oldest first,
/// with attachments and linked articles on their messages. Names are loaded in one batch each.
/// </summary>
public sealed class GetTicketRequestHandler(
    ICurrentAgentClaims currentAgent,
    IAgentRepository agents,
    ITicketRepository tickets,
    IRequesterRepository requesters,
    IProductRepository products,
    ITagRepository tags,
    IKbRepository kb) : IGetTicketRequestHandler
{
    public async Task<Result<TicketDetailDto>> HandleAsync(string reference, CancellationToken cancellationToken)
    {
        if (currentAgent.Current is null)
        {
            return Result<TicketDetailDto>.Failure(AgentErrors.AccessRequired());
        }

        Ticket? ticket = null;
        if (Guid.TryParse(reference, out var id))
        {
            ticket = await tickets.GetByIdAsync(id, cancellationToken);
        }
        else if (TicketNumber.TryParse(reference, out var number))
        {
            ticket = await tickets.GetByNumberAsync(number.ToString(), cancellationToken);
        }

        if (ticket is null)
        {
            return Result<TicketDetailDto>.Failure(TicketErrors.NotFound());
        }

        var messages = await tickets.GetMessagesAsync(ticket.Id, publicOnly: false, cancellationToken);
        var events = await tickets.GetEventsAsync(ticket.Id, cancellationToken);
        var attachments = (await tickets.GetAttachmentsAsync(ticket.Id, publicOnly: false, cancellationToken))
            .ToLookup(attachment => attachment.MessageId);
        var links = (await kb.ListTicketArticlesAsync(ticket.Id, cancellationToken)).ToLookup(link => link.MessageId);

        var articles = new Dictionary<Guid, LinkedArticleDto>();
        foreach (var articleId in links.SelectMany(group => group).Select(link => link.ArticleId).Distinct())
        {
            if (await kb.GetArticleAsync(articleId, cancellationToken) is { } article)
            {
                articles[articleId] = new LinkedArticleDto(article.Id, article.Title, article.Slug);
            }
        }

        var agentIds = messages.Where(m => m.AuthorType == AuthorType.Agent && m.AuthorId is not null).Select(m => m.AuthorId!.Value)
            .Concat(events.Where(e => e.ActorType == ActorType.Agent && e.ActorId is not null).Select(e => e.ActorId!.Value))
            .Concat(ticket.AssigneeId is { } assignee ? [assignee] : [])
            .Distinct()
            .ToList();
        var agentNames = (await agents.GetByIdsAsync(agentIds, cancellationToken)).ToDictionary(agent => agent.Id, agent => agent.Name ?? agent.Email);

        var requester = await requesters.GetByIdAsync(ticket.RequesterId, cancellationToken);
        var requesterName = requester is null ? null : requester.Name ?? requester.Email;
        var product = await products.GetByIdAsync(ticket.ProductId, cancellationToken);
        var tagsById = (await tags.ListAsync(cancellationToken)).ToDictionary(tag => tag.Id, tag => new TicketTagDto(tag.Id, tag.Name, tag.Colour));

        string? AgentName(Guid? agentId) => agentId is { } key ? agentNames.GetValueOrDefault(key) : null;

        var messageDtos = messages.Select(message => new MessageDto(
            message.Id, message.AuthorType.ToWire(), message.AuthorId,
            message.AuthorType switch
            {
                AuthorType.Agent => AgentName(message.AuthorId),
                AuthorType.Requester => requesterName,
                _ => null,
            },
            message.Visibility.ToWire(), message.Body, message.CreatedAt,
            [.. attachments[message.Id].Select(attachment => attachment.ToDto())],
            [.. links[message.Id].Select(link => link.ArticleId).Distinct().Where(articles.ContainsKey).Select(articleId => articles[articleId])])).ToList();

        var eventDtos = events.Select(item => new TicketEventDto(
            item.Id, item.Type.ToWire(), item.ActorType.ToWire(), item.ActorId,
            item.ActorType switch
            {
                ActorType.Agent => AgentName(item.ActorId),
                ActorType.Requester => requesterName,
                _ => "System",
            },
            item.PayloadJson, item.OccurredAt)).ToList();

        return Result<TicketDetailDto>.Success(new TicketDetailDto(
            ticket.Id, ticket.Number.ToString(), ticket.Subject, ticket.Status.ToWire(), ticket.Priority.ToWire(),
            ticket.ProductId, product?.Name ?? string.Empty,
            new TicketRequesterDto(ticket.RequesterId, requester?.Email ?? string.Empty, requester?.Name, requester?.ExternalUserRef),
            ticket.AssigneeId, AgentName(ticket.AssigneeId), ticket.IsSpam,
            [.. ticket.TagIds.Where(tagsById.ContainsKey).Select(tagId => tagsById[tagId])],
            ticket.Channel.ToWire(), ticket.ParentTicketId, ticket.MetadataJson, ticket.MetadataTrusted,
            ticket.CreatedAt, ticket.FirstResponseAt, ticket.SolvedAt, ticket.ClosedAt, ticket.LastActivityAt, ticket.Version,
            messageDtos, eventDtos));
    }
}
