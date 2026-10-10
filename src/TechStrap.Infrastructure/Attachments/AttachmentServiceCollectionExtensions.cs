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

    /// <summary>The Worker's view of uploaded logo addresses (D-052): optional TECHSTRAP_API_PUBLIC_URL; blank means null URLs and emails fall back to the linked logo.</summary>
    public static IServiceCollection AddTechStrapProductLogoUrls(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ProductLogoUrlOptions>()
            .Configure(options => options.PublicUrl = configuration[ProductLogoUrlOptions.PublicUrlKey]?.Trim() ?? string.Empty)
            .Validate(
                options => string.IsNullOrWhiteSpace(options.PublicUrl)
                    || (Uri.TryCreate(options.PublicUrl, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0),
                $"{ProductLogoUrlOptions.PublicUrlKey} must be blank or an absolute http or https URL without user info, query or fragment.")
            .ValidateOnStart();
        services.TryAddSingleton<IProductLogoUrls, ConfiguredProductLogoUrls>();
        return services;
    }
}
