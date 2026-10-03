using TechStrap.Application;

namespace TechStrap.Api.Startup;

/// <summary>Registers every Application handler (class XxxHandler with interface IXxxHandler) as scoped.</summary>
public static class ApplicationHandlerRegistration
{
    public static IServiceCollection AddApplicationHandlers(this IServiceCollection services)
    {
        foreach (var (contract, implementation) in Handlers())
        {
            services.AddScoped(contract, implementation);
        }

        return services;
    }

    public static IEnumerable<(Type Contract, Type Implementation)> Handlers() =>
        typeof(ApplicationAssemblyMarker).Assembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } && type.Name.EndsWith("Handler", StringComparison.Ordinal))
            .Select(type => (Contract: type.GetInterface("I" + type.Name), Implementation: type))
            .Where(pair => pair.Contract is not null)
            .Select(pair => (pair.Contract!, pair.Implementation));
}
