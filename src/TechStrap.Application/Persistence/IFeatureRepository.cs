using SyntaxCircus.Common;
using TechStrap.Domain.Features;
using TechStrap.Contracts.Features;

namespace TechStrap.Application.Persistence;

public interface IFeatureRepository
{
    Task<Feature?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<Feature?> GetBySlugAsync(Guid productId, string slug, CancellationToken cancellationToken);
    Task<bool> SlugTakenAsync(Guid productId, string slug, CancellationToken cancellationToken);
    Task<PagedResult<Feature>> ListAsync(
        Guid? productId,
        bool publishedOnly,
        FeatureProgressStatus? status,
        string? q,
        int page,
        int pageSize,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<LinkedFeatureTicketDto>> ListTicketsAsync(Guid featureId, CancellationToken cancellationToken);
    void Add(Feature feature);
    void Update(Feature feature);
}
