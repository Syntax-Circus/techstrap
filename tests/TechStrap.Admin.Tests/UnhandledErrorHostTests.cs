using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace TechStrap.Admin.Tests;

/// <summary>An unhandled exception in production shows a plain error page: cause and next step, no humour (BRAND.md section 3).</summary>
public sealed class UnhandledErrorHostTests
{
    private sealed class ThrowingStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);
            app.Map("/__test/throw", branch => branch.Run(_ => throw new InvalidOperationException("boom")));
        };
    }

    [Fact]
    public async Task An_unhandled_exception_returns_500_with_the_plain_error_page()
    {
        await using var factory = new AdminFactory("Production").WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton<IStartupFilter, ThrowingStartupFilter>()));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/__test/throw", TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        html.ShouldContain("Something went wrong on our side.");
        html.ShouldNotContain("ts-window");
        html.ShouldNotContain("boom");
        html.ShouldNotContain("ERROR 404");
        html.ShouldNotContain("fell out of its strap");
    }
}
