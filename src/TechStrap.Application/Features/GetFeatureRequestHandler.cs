using SyntaxCircus.Common;
using TechStrap.Contracts.Features;
using TechStrap.Contracts.Paging;

namespace TechStrap.Application.Features;

public interface IGetFeatureRequestHandler
{
    Task<Result<FeatureDto>> HandleAsync(Guid id, CancellationToken cancellationToken);
}

/// <summary>Handles the GetFeature feature board operation.</summary>
public sealed class GetFeatureRequestHandler(IFeatureService features) : IGetFeatureRequestHandler
{
    public Task<Result<FeatureDto>> HandleAsync(Guid id, CancellationToken cancellationToken) =>
        features.GetAsync(id, cancellationToken);
}
