using Microsoft.AspNetCore.Http;
using NSubstitute;
using TechStrap.Api.Options;
using TechStrap.Api.Startup;

namespace TechStrap.Api.Tests.Kb;

/// <summary>D-044: <c>TECHSTRAP_API_PUBLIC_URL</c> is required outside Development, validated on start, and the only source of an image URL when it is set.</summary>
[Collection(ProcessEnvironmentCollection.Name)]
public sealed class ApiPublicUrlTests
{
    [Theory]
    [InlineData("https://api.example.com", false, true)]
    [InlineData("https://api.example.com/", false, true)]
    [InlineData("http://localhost:8080", false, true)]
    [InlineData("https://example.com/techstrap-api", false, true)]
    [InlineData("", false, false)]
    [InlineData("   ", false, false)]
    [InlineData(null, false, false)]
    [InlineData("", true, true)]
    [InlineData(null, true, true)]
    [InlineData("api.example.com", true, false)]
    [InlineData("/kb", true, false)]
    [InlineData("ftp://api.example.com", true, false)]
    [InlineData("javascript:alert(1)", true, false)]
    [InlineData("https://user:pw@api.example.com", true, false)]
    [InlineData("https://api.example.com?x=1", true, false)]
    [InlineData("https://api.example.com#frag", true, false)]
    [InlineData("https://api.example.com/a b", true, false)]
    [InlineData("https://api.example.com/<x>", true, false)]
    [InlineData("https://api.example.com/\"x", true, false)]
    [InlineData("https://api.example.com/(x)", true, false)]
    [InlineData("https://api.example.com/x)", true, false)]
    [InlineData("https://api.example.com/	x", true, false)]
    public void The_value_is_checked_the_way_the_start_up_check_does(string? value, bool isDevelopment, bool acceptable) =>
        ApiPublicUrlOptions.IsAcceptable(value, isDevelopment).ShouldBe(acceptable);

    [Fact]
    public async Task Production_refuses_to_start_without_it_and_names_the_key()
    {
        await using var factory = new ApiFactory(
            environment: "Production",
            settings: new Dictionary<string, string?> { ["TrustedProxy:RequireTrustedProxiesInProduction"] = "false", ["TECHSTRAP_API_PUBLIC_URL"] = "" });

        var failure = Should.Throw<Exception>(() => factory.CreateClient());

        string.Join(" ", Messages(failure)).ShouldContain("TECHSTRAP_API_PUBLIC_URL");
    }

    [Fact]
    public async Task Development_starts_with_it_blank()
    {
        await using var development = new ApiFactory();
        using var client = development.CreateClient();

        (await client.GetAsync("/health/live", TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task A_malformed_address_stops_start_even_in_Development()
    {
        await using var factory = new ApiFactory(settings: new Dictionary<string, string?> { ["TECHSTRAP_API_PUBLIC_URL"] = "api.example.com" });

        var failure = Should.Throw<Exception>(() => factory.CreateClient());

        string.Join(" ", Messages(failure)).ShouldContain("TECHSTRAP_API_PUBLIC_URL");
    }

    [Fact]
    public void A_configured_address_builds_the_url_and_ignores_the_request_host()
    {
        var accessor = Substitute.For<IHttpContextAccessor>();
        var context = new DefaultHttpContext();
        context.Request.Scheme = "http";
        context.Request.Host = new HostString("evil.example");
        accessor.HttpContext.Returns(context);
        var urls = new KbImageUrls(Microsoft.Extensions.Options.Options.Create(new ApiPublicUrlOptions { PublicUrl = "https://api.example.com/" }), accessor);

        urls.UrlFor("abc.png").ShouldBe("https://api.example.com/kb-images/abc.png");
    }

    [Fact]
    public void The_url_is_built_from_the_parsed_address_not_the_raw_string()
    {
        var accessor = Substitute.For<IHttpContextAccessor>();
        var urls = new KbImageUrls(Microsoft.Extensions.Options.Options.Create(new ApiPublicUrlOptions { PublicUrl = "HTTPS://Api.Example.com:443/base/" }), accessor);

        urls.UrlFor("abc.png").ShouldBe("https://api.example.com/base/kb-images/abc.png");
    }

    [Fact]
    public void A_blank_address_falls_back_to_the_origin_of_the_request()
    {
        var accessor = Substitute.For<IHttpContextAccessor>();
        var context = new DefaultHttpContext();
        context.Request.Scheme = "http";
        context.Request.Host = new HostString("localhost", 8080);
        context.Request.PathBase = "/base";
        accessor.HttpContext.Returns(context);
        var urls = new KbImageUrls(Microsoft.Extensions.Options.Options.Create(new ApiPublicUrlOptions()), accessor);

        urls.UrlFor("abc.png").ShouldBe("http://localhost:8080/base/kb-images/abc.png");
    }

    [Fact]
    public void A_blank_address_with_no_request_is_an_error_not_a_relative_url()
    {
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns((HttpContext?)null);
        var urls = new KbImageUrls(Microsoft.Extensions.Options.Options.Create(new ApiPublicUrlOptions()), accessor);

        Should.Throw<InvalidOperationException>(() => urls.UrlFor("abc.png"));
    }

    private static IEnumerable<string> Messages(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            yield return current.Message;
        }
    }
}
