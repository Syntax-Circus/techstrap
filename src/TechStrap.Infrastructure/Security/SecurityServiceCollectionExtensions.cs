using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TechStrap.Application.ApiKeys;
using TechStrap.Application.Security;

namespace TechStrap.Infrastructure.Security;

public static class SecurityServiceCollectionExtensions
{
    public static IServiceCollection AddTechStrapSecurity(this IServiceCollection services)
    {
        services.TryAddSingleton<IApiKeyHasher, ApiKeyHasher>();
        services.TryAddScoped<IAccessTokenService, AccessTokenService>();
        return services;
    }
}
