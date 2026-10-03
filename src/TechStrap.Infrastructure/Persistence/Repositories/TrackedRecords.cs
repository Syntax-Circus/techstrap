using Microsoft.EntityFrameworkCore;

namespace TechStrap.Infrastructure.Persistence.Repositories;

internal static class TrackedRecords
{
    /// <summary>
    /// The record this scope already loaded for the given Guid primary key. Update methods copy domain changes onto it, so EF sends only the
    /// changed columns and checks the xmin token loaded with it. Updating something that was never loaded is a programming error.
    /// </summary>
    public static TRecord FindLoaded<TRecord>(this DbContext context, Guid key)
        where TRecord : class =>
        context.Set<TRecord>().Local.FindEntry(key)?.Entity
        ?? throw new InvalidOperationException($"{typeof(TRecord).Name} {key} was not loaded in this unit of work; load it through the repository before updating it.");

    /// <summary>
    /// Makes the UPDATE run <c>WHERE xmin = version</c> with the token the caller's Domain object carries (D-026), not the one loaded in this
    /// scope. Call it after copying the domain changes onto the tracked record. <paramref name="record"/> must be tracked and have a
    /// <c>Version</c> concurrency property.
    /// </summary>
    public static void ApplyOriginalVersion<TRecord>(this DbContext context, TRecord record, uint version)
        where TRecord : class =>
        context.Entry(record).Property("Version").OriginalValue = version;
}
