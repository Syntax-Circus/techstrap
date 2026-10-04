using SyntaxCircus.Common;
using TechStrap.Application.Persistence;
using TechStrap.Contracts.DeadLetters;
using TechStrap.Contracts.Paging;

namespace TechStrap.Application.DeadLetters;

public interface IListDeadLettersRequestHandler
{
    Task<Result<PagedResponse<DeadLetterDto>>> HandleAsync(int page, int pageSize, CancellationToken cancellationToken);
}

/// <summary>GET /api/dead-letters (Admin, D-006, D-022, D-039): newest first, recipient masked, payload never read.</summary>
public sealed class ListDeadLettersRequestHandler(IEmailOutboxStore store) : IListDeadLettersRequestHandler
{
    public async Task<Result<PagedResponse<DeadLetterDto>>> HandleAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        var found = await store.ListDeadLettersAsync(page, pageSize, cancellationToken);
        return Result<PagedResponse<DeadLetterDto>>.Success(new PagedResponse<DeadLetterDto>(
            [.. found.Items.Select(item => new DeadLetterDto(
                item.Id, item.Kind, MaskedRecipient.Mask(item.ToAddress), item.TicketId, item.ProductId, item.Attempts, item.LastError, item.CreatedAt))],
            found.Page,
            found.PageSize,
            found.TotalCount));
    }
}
