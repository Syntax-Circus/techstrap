using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TechStrap.Application.Email;
using TechStrap.Application.Persistence;

namespace TechStrap.Infrastructure.Email;

public static class OutboxRetentionServiceCollectionExtensions
{
    /// <summary>Everything the Worker needs for the outbox retention sweep: the validated options and the handler (D-039).</summary>
    public static IServiceCollection AddTechStrapOutboxRetention(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(OutboxRetentionOptions.SectionName);
        services.AddOptions<OutboxRetentionOptions>()
            .Configure(options =>
            {
                // Bound by hand so a non-numeric value reaches the validation below instead of throwing from the binder.
                if (bool.TryParse(section["Enabled"], out var enabled))
                {
                    options.Enabled = enabled;
                }

                options.Days = Read(section, "Days", options.Days);
                options.IntervalMinutes = Read(section, "IntervalMinutes", options.IntervalMinutes);
                options.BatchSize = Read(section, "BatchSize", options.BatchSize);
            })
            .Validate(_ => IsBlankOr(section["Enabled"], v => bool.TryParse(v, out bool _)), "OutboxRetention:Enabled must be true or false.")
            .Validate(_ => IsBlankOr(section["Days"], v => int.TryParse(v, out int _)), "OutboxRetention:Days must be a whole number.")
            .Validate(_ => IsBlankOr(section["IntervalMinutes"], v => int.TryParse(v, out int _)), "OutboxRetention:IntervalMinutes must be a whole number.")
            .Validate(_ => IsBlankOr(section["BatchSize"], v => int.TryParse(v, out int _)), "OutboxRetention:BatchSize must be a whole number.")
            .Validate(o => o.Days is >= 1 and <= 3650, "OutboxRetention:Days must be between 1 and 3650.")
            .Validate(o => o.IntervalMinutes is >= 1 and <= 1440, "OutboxRetention:IntervalMinutes must be between 1 and 1440.")
            .Validate(o => o.BatchSize is >= 1 and <= Paging.MaxBatchSize, $"OutboxRetention:BatchSize must be between 1 and {Paging.MaxBatchSize}.")
            .ValidateOnStart();
        services.TryAddScoped<IPurgeEmailOutboxHandler, PurgeEmailOutboxHandler>();
        return services;
    }

    private static bool IsBlankOr(string? raw, Func<string, bool> parses) => string.IsNullOrWhiteSpace(raw) || parses(raw);

    private static int Read(IConfigurationSection section, string key, int fallback) => int.TryParse(section[key], out var value) ? value : fallback;
}
