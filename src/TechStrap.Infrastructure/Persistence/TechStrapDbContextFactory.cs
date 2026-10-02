using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TechStrap.Infrastructure.Persistence;

/// <summary>
/// Used only by the dotnet ef tool, so migrations can be generated without starting a host or a database.
/// Override the connection with the TECHSTRAP_DESIGN_CONNECTION environment variable if needed.
/// </summary>
public sealed class TechStrapDbContextFactory : IDesignTimeDbContextFactory<TechStrapDbContext>
{
    private const string DesignConnectionVariable = "TECHSTRAP_DESIGN_CONNECTION";
    private const string DefaultDesignConnection = "Host=localhost;Port=5432;Database=techstrap_design;Username=postgres;Password=postgres";

    public TechStrapDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable(DesignConnectionVariable) ?? DefaultDesignConnection;
        var builder = new DbContextOptionsBuilder<TechStrapDbContext>();
        TechStrapDatabase.Configure(builder, connectionString);
        return new TechStrapDbContext(builder.Options);
    }
}
