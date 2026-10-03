using Microsoft.EntityFrameworkCore;
using TechStrap.Infrastructure.Persistence;

namespace TechStrap.Api.Tests;

/// <summary>The migration ids compiled into the Infrastructure assembly, so tests do not hard-code how many there are.</summary>
internal static class ExpectedMigrations
{
    public static IReadOnlyList<string> Ids()
    {
        var options = new DbContextOptionsBuilder<TechStrapDbContext>();
        TechStrapDatabase.Configure(options, "Host=localhost;Database=ids_only");
        using var context = new TechStrapDbContext(options.Options);
        return [.. context.Database.GetMigrations()];
    }
}
