using TechStrap.Tests.Shared.AdminHost;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace TechStrap.Admin.Tests;

/// <summary>BRAND.md section 3: the cheeky 404 belongs to 404 only. An error that blocks work never gets the humor.</summary>
public sealed class ErrorStatusHostTests
{
    [Theory]
    [InlineData(500)]
    [InlineData(403)]
    public async Task A_non_404_error_response_does_not_get_the_branded_404(int status)
    {
        await using var factory = new AdminFactory().WithWebHostBuilder(b => b.ConfigureServices(services =>
            services.AddSingleton<IStartupFilter>(new ForcedStatusStartupFilter())));
        using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Agent);

        var response = await client.GetAsync("/__test/status/" + status, TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        ((int)response.StatusCode).ShouldBe(status);
        html.ShouldNotContain("ts-window");
        html.ShouldNotContain("This page fell out of its strap.");
        html.ShouldNotContain("ERROR 404");
    }

    /// <summary>Test-only: answers /__test/status/{code} with an empty body after the app pipeline, so the status code pages middleware sees it.</summary>
    private sealed class ForcedStatusStartupFilter : IStartupFilter
    {
        private const string Prefix = "/__test/status/";

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);
            app.Use(async (context, nextMiddleware) =>
            {
                var path = context.Request.Path.Value ?? string.Empty;
                if (path.StartsWith(Prefix, StringComparison.Ordinal) && int.TryParse(path[Prefix.Length..], out var code))
                {
                    context.Response.StatusCode = code;
                    return;
                }

                await nextMiddleware();
            });
        };
    }
}
