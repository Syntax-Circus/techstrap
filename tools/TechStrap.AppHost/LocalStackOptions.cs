using Microsoft.Extensions.Configuration;

namespace TechStrap.AppHost;

/// <summary>The local stack's own switches, set in the AppHost's user secrets.</summary>
/// <param name="SeedDevData"><c>LocalStack:SeedDevData</c>, on unless it is <c>false</c>.</param>
public sealed record LocalStackOptions(bool SeedDevData)
{
    private const string SeedDevDataKey = "LocalStack:SeedDevData";

    /// <summary>Throws, naming the setting, when a value is neither true nor false.</summary>
    public static LocalStackOptions FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var value = configuration[SeedDevDataKey]?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return new LocalStackOptions(SeedDevData: true);
        }

        return bool.TryParse(value, out var seedDevData)
            ? new LocalStackOptions(seedDevData)
            : throw new InvalidOperationException($"{SeedDevDataKey} must be true or false.");
    }
}
