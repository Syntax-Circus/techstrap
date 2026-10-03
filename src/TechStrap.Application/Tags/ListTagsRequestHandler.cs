using SyntaxCircus.Common;
using TechStrap.Application.Persistence;
using TechStrap.Contracts.Tags;

namespace TechStrap.Application.Tags;

public interface IListTagsRequestHandler
{
    Task<Result<IReadOnlyList<TagDto>>> HandleAsync(CancellationToken cancellationToken);
}

/// <summary>GET /api/tags (Agent): every tag, ordered by name, for filters and the tag picker (D-022).</summary>
public sealed class ListTagsRequestHandler(ITagRepository tags) : IListTagsRequestHandler
{
    public async Task<Result<IReadOnlyList<TagDto>>> HandleAsync(CancellationToken cancellationToken) =>
        Result<IReadOnlyList<TagDto>>.Success([.. (await tags.ListAsync(cancellationToken)).Select(TagMapping.ToDto)]);
}
