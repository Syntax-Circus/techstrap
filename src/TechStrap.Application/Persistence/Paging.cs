namespace TechStrap.Application.Persistence;

/// <summary>
/// Paging and batch limits shared by every list query. Page numbers start at 1. Implementations must pass every page, page size
/// and batch size through this class before querying: the values come from callers and are not trusted.
/// </summary>
public static class Paging
{
    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 100;

    /// <summary>Largest page number, chosen so <c>(page - 1) * MaxPageSize</c> always fits in an <see cref="int"/>.</summary>
    public const int MaxPage = int.MaxValue / MaxPageSize;

    public const int DefaultBatchSize = 20;
    public const int MaxBatchSize = 500;

    public static int NormalizePage(int page) => Math.Clamp(page, 1, MaxPage);

    public static int NormalizePageSize(int pageSize) =>
        pageSize < 1 ? DefaultPageSize : Math.Min(pageSize, MaxPageSize);

    /// <summary>The number of rows to skip for a page, from the normalized page and page size. Cannot overflow.</summary>
    public static int Offset(int page, int pageSize) => (NormalizePage(page) - 1) * NormalizePageSize(pageSize);

    /// <summary>For limits that are not pages (worker batches, sweeps): below one gives the default, above the maximum is capped.</summary>
    public static int NormalizeBatchSize(int batchSize) =>
        batchSize < 1 ? DefaultBatchSize : Math.Min(batchSize, MaxBatchSize);
}
