using Microsoft.EntityFrameworkCore;
using TechStrap.Infrastructure.Persistence;

namespace TechStrap.Infrastructure.IntegrationTests;

/// <summary>
/// Proves TechStrapDatabase.Configure applies the snake_case convention. TechStrapDbContext has no
/// entities yet, so this uses a throwaway context built with the same options. No database needed.
/// </summary>
public sealed class SnakeCaseConventionTests
{
    [Fact]
    public void Tables_and_columns_are_snake_case()
    {
        var options = new DbContextOptionsBuilder<ConventionProbeContext>();
        TechStrapDatabase.Configure(options, "Host=localhost;Database=probe");
        using var context = new ConventionProbeContext(options.Options);

        var entity = context.Model.FindEntityType(typeof(TicketProbe))!;

        entity.GetTableName().ShouldBe("ticket_probe");
        entity.FindProperty(nameof(TicketProbe.LastActivityAt))!.GetColumnName().ShouldBe("last_activity_at");
    }

    private sealed class TicketProbe
    {
        public int Id { get; set; }

        public DateTimeOffset LastActivityAt { get; set; }
    }

    private sealed class ConventionProbeContext(DbContextOptions<ConventionProbeContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.Entity<TicketProbe>();
    }
}
