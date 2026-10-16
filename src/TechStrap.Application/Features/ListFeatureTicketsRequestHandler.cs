using SyntaxCircus.Common;
using TechStrap.Contracts.Features;
using TechStrap.Contracts.Paging;

namespace TechStrap.Application.Features;

public interface IListFeatureTicketsRequestHandler
{
    Task<Result<IReadOnlyList<LinkedFeatureTicketDto>>> HandleAsync(Guid id, CancellationToken cancellationToken);
}

/// <summary>Handles the ListFeatureTickets feature board operation.</summary>
public sealed class ListFeatureTicketsRequestHandler(IFeatureService features) : IListFeatureTicketsRequestHandler
{
    public Task<Result<IReadOnlyList<LinkedFeatureTicketDto>>> HandleAsync(Guid id, CancellationToken cancellationToken) =>
        features.ListTicketsAsync(id, cancellationToken);
}
