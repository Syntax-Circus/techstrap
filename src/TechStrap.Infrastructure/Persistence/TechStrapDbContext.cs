using Microsoft.EntityFrameworkCore;

namespace TechStrap.Infrastructure.Persistence;

/// <summary>
/// The TechStrap database. The model is empty in PHASE-01; PHASE-03 adds the entity configurations.
/// Naming (snake_case) comes from <see cref="TechStrapDatabase.Configure"/>.
/// </summary>
public sealed class TechStrapDbContext(DbContextOptions<TechStrapDbContext> options) : DbContext(options);
