using SyntaxCircus.Common;
using TechStrap.Contracts.Features;
using TechStrap.Contracts.Paging;

namespace TechStrap.Application.Features;

public interface IUnpublishFeatureRequestHandler
{
    Task<Result<FeatureDto>> HandleAsync(Guid id, uint version, CancellationToken cancellationToken);
}

/// <summary>Handles the UnpublishFeature feature board operation.</summary>
public sealed class UnpublishFeatureRequestHandler(IFeatureService features) : IUnpublishFeatureRequestHandler
{
    public Task<Result<FeatureDto>> HandleAsync(Guid id, uint version, CancellationToken cancellationToken) =>
        features.SetPublishedAsync(id, false, version, cancellationToken);
}
