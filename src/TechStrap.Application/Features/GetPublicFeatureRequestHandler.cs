using SyntaxCircus.Common;
using TechStrap.Contracts.Features;
using TechStrap.Contracts.Paging;

namespace TechStrap.Application.Features;

public interface IGetPublicFeatureRequestHandler
{
    Task<Result<PublicFeatureDto>> HandleAsync(string key, string slug, CancellationToken cancellationToken);
}

/// <summary>Handles the GetPublicFeature feature board operation.</summary>
public sealed class GetPublicFeatureRequestHandler(IFeatureService features) : IGetPublicFeatureRequestHandler
{
    public Task<Result<PublicFeatureDto>> HandleAsync(string key, string slug, CancellationToken cancellationToken) =>
        features.GetPublicAsync(key, slug, cancellationToken);
}
