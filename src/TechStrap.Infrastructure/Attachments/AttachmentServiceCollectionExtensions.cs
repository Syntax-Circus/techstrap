using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SyntaxCircus.Storage;
using TechStrap.Application.Attachments;

namespace TechStrap.Infrastructure.Attachments;

public static class AttachmentServiceCollectionExtensions
{
    public static IServiceCollection AddTechStrapAttachments(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddStorageProvider(configuration);
        services.AddOptions<LocalStorageOptions>()
            .Bind(configuration.GetSection(LocalStorageOptions.SectionName))
            .Validate(
                o => !string.Equals(configuration["Storage:Provider"] ?? "Local", "Local", StringComparison.OrdinalIgnoreCase) || !string.IsNullOrWhiteSpace(o.RootPath),
                "Storage:Local:RootPath is required for the Local storage provider.")
            .ValidateOnStart();
        services.TryAddScoped<IAttachmentStore, AttachmentStore>();
        return services;
    }
}
