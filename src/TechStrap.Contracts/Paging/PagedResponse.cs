namespace TechStrap.Contracts.Paging;

/// <summary>One page of results. <paramref name="Page"/> is 1-based; <paramref name="TotalCount"/> counts every matching item.</summary>
public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);
