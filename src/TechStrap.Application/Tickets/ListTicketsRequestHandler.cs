using SyntaxCircus.Common;
using TechStrap.Application.Agents;
using TechStrap.Application.Persistence;
using TechStrap.Contracts.Paging;
using TechStrap.Contracts.Tickets;
using TechStrap.Domain.Rules;
using TechStrap.Domain.Tickets;

namespace TechStrap.Application.Tickets;

public interface IListTicketsRequestHandler
{
    Task<Result<PagedResponse<TicketSummaryDto>>> HandleAsync(ListTicketsRequest request, CancellationToken cancellationToken);
}

/// <summary>GET /api/tickets. The agent queue: a view, optional filters, optional search and a page, with names looked up in three batches.</summary>
public sealed class ListTicketsRequestHandler(
    ICurrentAgentClaims currentAgent,
    IAgentRepository agents,
    ITicketRepository tickets,
    IProductRepository products,
    ITagRepository tags) : IListTicketsRequestHandler
{
    public async Task<Result<PagedResponse<TicketSummaryDto>>> HandleAsync(ListTicketsRequest request, CancellationToken cancellationToken)
    {
        if (!TicketNameParser.TryView(request.View, out var view))
        {
            return Fail(TicketErrors.Invalid("view", "view-invalid", "Use Unassigned, Mine, Open, Pending, All or Spam."));
        }

        TicketStatus? status = null;
        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            if (!TicketNameParser.TryStatus(request.Status, out var parsedStatus))
            {
                return Fail(TicketErrors.Invalid("status", "status-invalid", "Use New, Open, Pending, Solved or Closed."));
            }

            status = parsedStatus;
        }

        TicketPriority? priority = null;
        if (!string.IsNullOrWhiteSpace(request.Priority))
        {
            if (!TicketNameParser.TryPriority(request.Priority, out var parsedPriority))
            {
                return Fail(TicketErrors.Invalid("priority", "priority-invalid", "Use Low, Normal, High or Urgent."));
            }

            priority = parsedPriority;
        }

        if (request.Page < 1)
        {
            return Fail(TicketErrors.Invalid("page", "page-invalid", "The page number starts at 1."));
        }

        if (request.PageSize != 0 && (request.PageSize < 1 || request.PageSize > Paging.MaxPageSize))
        {
            return Fail(TicketErrors.Invalid("pageSize", "page-size-invalid", $"The page size must be between 1 and {Paging.MaxPageSize}."));
        }

        var agent = await CurrentAgent.RequireActiveAsync(currentAgent, agents, cancellationToken);
        if (agent.IsFailure)
        {
            return Result<PagedResponse<TicketSummaryDto>>.Failure(agent.Errors[0]);
        }

        var pageSize = request.PageSize == 0 ? Paging.DefaultPageSize : request.PageSize;
        var search = request.Search is { Length: > DomainLimits.SearchTextMaxLength } text ? text[..DomainLimits.SearchTextMaxLength] : request.Search;
        var query = new TicketQuery(
            view, agent.Value.Id, request.ProductId, status, priority, request.AssigneeId, request.TagId, request.RequesterId, search, request.Page, pageSize);

        var found = await tickets.ListAsync(query, cancellationToken);

        var productNames = (await products.ListAsync(activeOnly: false, cancellationToken)).ToDictionary(product => product.Id, product => product.Name);
        var assigneeIds = found.Items.Where(row => row.AssigneeId is not null).Select(row => row.AssigneeId!.Value).Distinct().ToList();
        var assigneeNames = (await agents.GetByIdsAsync(assigneeIds, cancellationToken)).ToDictionary(a => a.Id, a => a.Name ?? a.Email);
        var tagsById = (await tags.ListAsync(cancellationToken)).ToDictionary(tag => tag.Id, tag => new TicketTagDto(tag.Id, tag.Name, tag.Colour));

        var items = found.Items.Select(row => new TicketSummaryDto(
            row.Id, row.Number, row.Subject, row.Status.ToWire(), row.Priority.ToWire(),
            row.ProductId, productNames.GetValueOrDefault(row.ProductId, string.Empty),
            row.RequesterId, row.RequesterEmail, row.RequesterName,
            row.AssigneeId, row.AssigneeId is { } assigneeId ? assigneeNames.GetValueOrDefault(assigneeId, string.Empty) : null,
            row.IsSpam,
            [.. row.TagIds.Where(tagsById.ContainsKey).Select(id => tagsById[id])],
            row.CreatedAt, row.LastActivityAt)).ToList();

        return Result<PagedResponse<TicketSummaryDto>>.Success(new PagedResponse<TicketSummaryDto>(items, found.Page, found.PageSize, found.TotalCount));
    }

    private static Result<PagedResponse<TicketSummaryDto>> Fail(ResultError error) => Result<PagedResponse<TicketSummaryDto>>.Failure(error);
}
