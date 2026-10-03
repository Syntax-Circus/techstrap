using Microsoft.EntityFrameworkCore;
using TechStrap.Infrastructure.Persistence.Configurations;

namespace TechStrap.Infrastructure.Persistence;

/// <summary>
/// The TechStrap database. The model is the set of internal persistence records (D-026) configured by the
/// <c>IEntityTypeConfiguration</c> classes in <c>Persistence/Configurations</c>; the context never exposes them. Naming
/// (snake_case) comes from <see cref="TechStrapDatabase.Configure"/>.
/// </summary>
public sealed class TechStrapDbContext(DbContextOptions<TechStrapDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("citext");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TechStrapDbContext).Assembly);
        ForeignKeyNaming.ApplyTableBasedNames(modelBuilder);
    }
}
