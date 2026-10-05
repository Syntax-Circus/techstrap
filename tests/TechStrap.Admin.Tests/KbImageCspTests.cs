using TechStrap.Hosting.Security;

namespace TechStrap.Admin.Tests;

/// <summary>
/// Review Focus 2, the browser half (PHASE-08): an uploaded picture is served by the API at <c>{TECHSTRAP_API_PUBLIC_URL}/kb-images/{key}</c>, and the Admin's preview (and, later, the Portal) draws it through <c>img-src</c>. In
/// Production the address is an https one, which <c>https:</c> already covers. In Development it is the API on a loopback port (compose publishes it on 127.0.0.1:8080, or the request's own origin is used), which the
/// loopback entries cover on any port. Without them a picture would upload and then never show in the preview. This asks the policy the question a browser asks, for each shape of address.
/// </summary>
public sealed class KbImageCspTests
{
    private static string[] ImageSources(bool development) =>
        TechStrapCsp.ForBlazorApp(allowLoopbackImages: development).Split(';', StringSplitOptions.TrimEntries)
            .Select(directive => directive.Split(' '))
            .Single(parts => parts[0] == "img-src")[1..];

    /// <summary>The part of CSP source matching these tests need: a scheme-only source (<c>https:</c>) and a host source with an optional wildcard port (<c>http://localhost:*</c>).</summary>
    private static bool Allows(string[] sources, string address)
    {
        var url = new Uri(address);
        return sources.Any(source =>
        {
            if (source.EndsWith(':'))
            {
                return string.Equals(source[..^1], url.Scheme, StringComparison.Ordinal);
            }

            if (!Uri.TryCreate(source.Replace(":*", ":0", StringComparison.Ordinal), UriKind.Absolute, out var allowed))
            {
                return false;
            }

            var anyPort = source.EndsWith(":*", StringComparison.Ordinal);
            return string.Equals(allowed.Scheme, url.Scheme, StringComparison.Ordinal)
                && string.Equals(allowed.Host, url.Host, StringComparison.OrdinalIgnoreCase)
                && (anyPort || allowed.Port == url.Port);
        });
    }

    [Theory]
    [InlineData("http://localhost:8080/kb-images/0198c7e2-1111-7000-8000-000000000001.png")]
    [InlineData("http://localhost:5280/kb-images/0198c7e2-1111-7000-8000-000000000001.webp")]
    [InlineData("http://127.0.0.1:8080/kb-images/0198c7e2-1111-7000-8000-000000000001.png")]
    [InlineData("https://api.example.com/kb-images/0198c7e2-1111-7000-8000-000000000001.png")]
    public void In_development_a_picture_from_the_api_on_loopback_or_over_https_is_allowed(string address) =>
        Allows(ImageSources(development: true), address).ShouldBeTrue(address);

    [Theory]
    [InlineData("https://api.example.com/kb-images/0198c7e2-1111-7000-8000-000000000001.png", true)]
    [InlineData("http://localhost:8080/kb-images/0198c7e2-1111-7000-8000-000000000001.png", false)]
    [InlineData("http://127.0.0.1:8080/kb-images/0198c7e2-1111-7000-8000-000000000001.png", false)]
    [InlineData("http://api.example.com/kb-images/0198c7e2-1111-7000-8000-000000000001.png", false)]
    public void In_production_only_an_https_address_is_allowed_so_the_api_public_url_must_be_https(string address, bool allowed) =>
        Allows(ImageSources(development: false), address).ShouldBe(allowed, address);

    [Theory]
    [InlineData("http://api/kb-images/0198c7e2-1111-7000-8000-000000000001.png")]
    [InlineData("http://api.example.com/kb-images/0198c7e2-1111-7000-8000-000000000001.png")]
    public void In_development_a_plain_http_address_that_is_not_loopback_is_still_refused(string address) =>
        Allows(ImageSources(development: true), address).ShouldBeFalse(address);

    [Fact]
    public void The_matcher_itself_tells_a_wildcard_port_from_a_fixed_one()
    {
        Allows(["http://localhost:*"], "http://localhost:1").ShouldBeTrue();
        Allows(["http://localhost:8080"], "http://localhost:8080/x.png").ShouldBeTrue();
        Allows(["http://localhost:8080"], "http://localhost:9090/x.png").ShouldBeFalse();
        Allows(["https:"], "http://a.example/x.png").ShouldBeFalse();
    }
}
