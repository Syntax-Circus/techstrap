using SyntaxCircus.Common;
using TechStrap.Contracts.Features;
using TechStrap.Contracts.Paging;

namespace TechStrap.Application.Features;

public interface ICreateFeatureRequestHandler
{
    Task<Result<FeatureDto>> HandleAsync(CreateFeatureRequest request, CancellationToken cancellationToken);
}

/// <summary>Handles the CreateFeature feature board operation.</summary>
public sealed class CreateFeatureRequestHandler(IFeatureService features) : ICreateFeatureRequestHandler
{
    public Task<Result<FeatureDto>> HandleAsync(CreateFeatureRequest request, CancellationToken cancellationToken) =>
        features.CreateAsync(request, cancellationToken);
}
