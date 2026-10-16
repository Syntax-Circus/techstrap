using SyntaxCircus.Common;
using TechStrap.Contracts.Features;
using TechStrap.Contracts.Paging;

namespace TechStrap.Application.Features;

public interface IUpdateFeatureRequestHandler
{
    Task<Result<FeatureDto>> HandleAsync(Guid id, UpdateFeatureRequest request, CancellationToken cancellationToken);
}

/// <summary>Handles the UpdateFeature feature board operation.</summary>
public sealed class UpdateFeatureRequestHandler(IFeatureService features) : IUpdateFeatureRequestHandler
{
    public Task<Result<FeatureDto>> HandleAsync(Guid id, UpdateFeatureRequest request, CancellationToken cancellationToken) =>
        features.UpdateAsync(id, request, cancellationToken);
}
