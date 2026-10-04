using SyntaxCircus.Common;
using TechStrap.Application.Persistence;
using TechStrap.Contracts.Tags;

namespace TechStrap.Application.Tags;

public interface IListTagSummariesRequestHandler
{
    Task<Result<IReadOnlyList<TagSummaryDto>>> HandleAsync(CancellationToken cancellationToken);
}

/// <summary>GET /api/tags/summary (Admin): every tag with its ticket count, ordered by name, for the Admin tag list and the delete confirmation (D-041).</summary>
public sealed class ListTagSummariesRequestHandler(ITagRepository tags) : IListTagSummariesRequestHandler
{
    public async Task<Result<IReadOnlyList<TagSummaryDto>>> HandleAsync(CancellationToken cancellationToken) =>
        Result<IReadOnlyList<TagSummaryDto>>.Success([.. (await tags.ListWithTicketCountsAsync(cancellationToken)).Select(TagMapping.ToSummaryDto)]);
}
