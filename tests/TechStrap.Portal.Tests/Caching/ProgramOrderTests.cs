using TechStrap.Tests.Shared;

namespace TechStrap.Portal.Tests.Caching;

/// <summary>
/// The order of the Portal's pipeline is part of its safety (PHASE-09c Review Focus 3): the shared headers and the per-path rules before the cache (so a cached answer is sent with the request's own headers), the error pages
/// before the cache (so a 404 is re-executed at <c>/not-found</c>, which is never kept), and the cache before the endpoints (so it can answer instead of them). This pins the lines of <c>Program.cs</c> in that order.
/// </summary>
public sealed class ProgramOrderTests
{
    private static int Index(string program, string call)
    {
        var index = program.IndexOf(call, StringComparison.Ordinal);
        index.ShouldBeGreaterThanOrEqualTo(0, $"Program.cs must call {call}");
        return index;
    }

    [Fact]
    public void The_output_cache_runs_after_the_headers_and_the_error_pages_and_before_the_form_limit_and_the_endpoints()
    {
        var program = File.ReadAllText(RepositoryRoot.Combine("src", "TechStrap.Portal", "Program.cs"));

        var order = new[]
        {
            Index(program, "app.UseTechStrapWebHost(PortalHeaderRules.Rules("),
            Index(program, "app.UsePortalSeo();"),
            Index(program, "app.UseTechStrapErrorPages();"),
            Index(program, "app.UsePortalOutputCache();"),
            Index(program, "app.UseMiddleware<RequestTooLargeMiddleware>();"),
            Index(program, "app.UseAntiforgery();"),
            Index(program, "app.MapPortalSeo();"),
            Index(program, "app.MapRazorComponentsWithStaticAssets<App>();"),
        };

        order.ShouldBe(order.Order());
        program.Split("UsePortalOutputCache", StringSplitOptions.None).Length.ShouldBe(2, "the cache is added once");
        program.ShouldContain("builder.Services.AddPortalOutputCache();");
    }
}
