using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using TechStrap.Portal.Hosting;
using TechStrap.Portal.Routing;
using TechStrap.Portal.Settings;

namespace TechStrap.Portal.Tests.Routing;

/// <summary>The links of a component rendered on the default host with no product host known (bUnit tests): every link keeps its /p/{key} path.</summary>
internal static class PortalLinksKit
{
    public static IServiceCollection AddPortalLinks(this IServiceCollection services)
    {
        var map = new ProductHostMap(Substitute.For<IServiceScopeFactory>(), TimeProvider.System, NullLogger<ProductHostMap>.Instance, new HttpContextAccessor());
        return services.AddSingleton(new PortalLinks(new ProductHostContext(), map, Options.Create(new PortalOptions { PublicUrl = "https://portal.test" })));
    }
}
