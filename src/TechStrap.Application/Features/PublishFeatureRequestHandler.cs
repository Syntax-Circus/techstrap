using SyntaxCircus.Common;
using TechStrap.Contracts.Features;
using TechStrap.Contracts.Paging;

namespace TechStrap.Application.Features;

public interface IPublishFeatureRequestHandler
{
    Task<Result<FeatureDto>> HandleAsync(Guid id, uint version, CancellationToken cancellationToken);
}

/// <summary>Handles the PublishFeature feature board operation.</summary>
public sealed class PublishFeatureRequestHandler(IFeatureService features) : IPublishFeatureRequestHandler
{
    public Task<Result<FeatureDto>> HandleAsync(Guid id, uint version, CancellationToken cancellationToken) =>
        features.SetPublishedAsync(id, true, version, cancellationToken);
}
