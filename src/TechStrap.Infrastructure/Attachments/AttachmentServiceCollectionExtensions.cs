using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SyntaxCircus.Storage;
using TechStrap.Application.Attachments;
using TechStrap.Application.Knowledge;
using TechStrap.Application.Products;

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
        services.TryAddScoped<IKbImageStore, KbImageStore>();
        services.TryAddScoped<IProductLogoStore, ProductLogoStore>();
        return services;
    }
}
