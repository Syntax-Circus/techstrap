using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace TechStrap.Infrastructure.Persistence;

/// <summary>The database connection string (<c>ConnectionStrings:TechStrap</c>), bound so that a missing one stops the Api and the Worker at start.</summary>
public sealed class DatabaseConnectionOptions
{
    public string? ConnectionString { get; set; }
}

/// <summary>
/// Outside Development a blank <c>ConnectionStrings:TechStrap</c> fails the start and names the key, instead of surfacing later as a driver error that does not
/// say which setting is missing. Development keeps starting without one (the tests and a developer's first run do not need a database).
/// </summary>
internal sealed class DatabaseConnectionOptionsValidator(IHostEnvironment environment) : IValidateOptions<DatabaseConnectionOptions>
{
    public ValidateOptionsResult Validate(string? name, DatabaseConnectionOptions options) =>
        environment.IsDevelopment() || !string.IsNullOrWhiteSpace(options.ConnectionString)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail($"ConnectionStrings:{TechStrapDatabase.ConnectionStringName} is required outside Development.");
}
