using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using TechStrap.Portal.Hosting;

namespace TechStrap.Portal.Tests.Hosting;

/// <summary>
/// The status-code and error pages are re-executed in a scope of their own. The product host the middleware found must reach that scope too, so it is kept on the request (its feature collection) and every scope reads it
/// from there.
/// </summary>
public sealed class ProductHostRegistrationTests
{
    [Fact]
    public void Every_scope_of_a_request_sees_the_product_host_found_for_that_request()
    {
        var services = new ServiceCollection();
        services.AddProductHosts();
        using var provider = services.BuildServiceProvider();
        var http = new DefaultHttpContext();
        http.Features.Set(new ProductHostContext { Key = "dragon-poop", Host = "support.dragonpoop.com" });
        provider.GetRequiredService<IHttpContextAccessor>().HttpContext = http;

        using var original = provider.CreateScope();
        using var reExecuted = provider.CreateScope();

        foreach (var scope in new[] { original, reExecuted })
        {
            var context = scope.ServiceProvider.GetRequiredService<ProductHostContext>();
            context.IsProductHost.ShouldBeTrue();
            context.Key.ShouldBe("dragon-poop");
            context.Host.ShouldBe("support.dragonpoop.com");
        }
    }

    [Fact]
    public void Without_a_request_the_context_is_empty()
    {
        var services = new ServiceCollection();
        services.AddProductHosts();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<ProductHostContext>().IsProductHost.ShouldBeFalse();
    }
}