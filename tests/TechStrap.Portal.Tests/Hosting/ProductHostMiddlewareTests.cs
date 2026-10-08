using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Contracts.Products;
using TechStrap.Portal.Clients;
using TechStrap.Portal.Hosting;
using TechStrap.Portal.Routing;
using TechStrap.Portal.Settings;

namespace TechStrap.Portal.Tests.Hosting;

/// <summary>
/// P11e-T05 at the middleware, with a real map and resolver behind a stub product client: the rewrite table, the pass-through paths, the canonical 301s (GET and HEAD only), the POST that is rewritten and never
/// redirected, the unknown host that behaves as the default host, and the scoped context.
/// </summary>
public sealed class ProductHostMiddlewareTests
{
    private const string DragonHost = "support.dragonpoop.com";
    private const string PublicUrl = "https://portal.test";

    private sealed record Outcome(DefaultHttpContext Http, ProductHostContext Context, bool NextCalled, string? NextPath, string? NextQuery);

    private static async Task<Outcome> RunAsync(CancellationToken ct, string host, string path, string method = "GET", string query = "")
    {
        var client = Substitute.For<IPublicProductClient>();
        client.ListAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result<IReadOnlyList<PublicProductSummaryDto>>.Success(
        [
            new PublicProductSummaryDto("dragon-poop", "Dragon Poop", DragonHost),
            new PublicProductSummaryDto("paper-plane", "Paper Plane", "help.paperplane.com"),
            new PublicProductSummaryDto("who-flung-poo", "Who Flung Poo"),
        ])));
        var services = new ServiceCollection();
        services.AddScoped(_ => client);
        var provider = services.BuildServiceProvider();
        var options = Options.Create(new PortalOptions { PublicUrl = PublicUrl + "/", ApiBaseUrl = "http://api.test/" });
        var map = new ProductHostMap(provider.GetRequiredService<IServiceScopeFactory>(), new FakeTimeProvider(), NullLogger<ProductHostMap>.Instance);
        var resolver = new ProductHostResolver(map, options);

        var http = new DefaultHttpContext { RequestAborted = ct };
        http.Request.Method = method;
        http.Request.Host = new HostString(host);
        http.Request.Path = path;
        http.Request.QueryString = new QueryString(query);
        var context = new ProductHostContext();
        var nextCalled = false;
        string? nextPath = null;
        string? nextQuery = null;
        await new ProductHostMiddleware(next =>
        {
            nextCalled = true;
            nextPath = next.Request.Path.Value;
            nextQuery = next.Request.QueryString.Value;
            return Task.CompletedTask;
        }).InvokeAsync(http, resolver, context, map, options);
        return new Outcome(http, context, nextCalled, nextPath, nextQuery);
    }

    [Theory(Timeout = 30_000)]
    [InlineData("/", "/p/dragon-poop")]
    [InlineData("/contact", "/p/dragon-poop/contact")]
    [InlineData("/contact/received", "/p/dragon-poop/contact/received")]
    [InlineData("/lost-link", "/p/dragon-poop/lost-link")]
    [InlineData("/kb", "/p/dragon-poop/kb")]
    [InlineData("/kb/accounts", "/p/dragon-poop/kb/accounts")]
    [InlineData("/kb/accounts/reset-password", "/p/dragon-poop/kb/accounts/reset-password")]
    [InlineData("/kb/search", "/p/dragon-poop/kb/search")]
    [InlineData("/suggest", "/p/dragon-poop/suggest")]
    public async Task A_clean_path_on_a_product_host_is_rewritten_to_the_product_path_and_the_query_is_kept(string path, string rewritten)
    {
        var ct = TestContext.Current.CancellationToken;
        var outcome = await RunAsync(ct, DragonHost, path, query: "?q=a%20b");

        outcome.NextCalled.ShouldBeTrue();
        outcome.NextPath.ShouldBe(rewritten);
        outcome.NextQuery.ShouldBe("?q=a%20b");
        outcome.Http.Response.StatusCode.ShouldBe(200);
        outcome.Http.Request.PathBase.Value.ShouldBeNullOrEmpty();
    }

    [Theory(Timeout = 30_000)]
    [InlineData("/t/AbC-_0123456789AbC-_0123456789AbC-_01234567")]
    [InlineData("/t/AbC-_0123456789AbC-_0123456789AbC-_01234567/attachments/11111111-2222-3333-4444-555555555555")]
    [InlineData("/_framework/blazor.web.js")]
    [InlineData("/_blazor/negotiate")]
    [InlineData("/_content/Some.Package/x.js")]
    [InlineData("/css/portal.css")]
    [InlineData("/js/portal-forms.js")]
    [InlineData("/img/logo.png")]
    [InlineData("/favicon.ico")]
    [InlineData("/sitemap.xml")]
    [InlineData("/robots.txt")]
    [InlineData("/health/live")]
    [InlineData("/not-found")]
    [InlineData("/error")]
    [InlineData("/_styleguide")]
    [InlineData("/nope")]
    public async Task A_path_served_on_every_host_and_an_unknown_path_pass_through_unchanged(string path)
    {
        var ct = TestContext.Current.CancellationToken;
        var outcome = await RunAsync(ct, DragonHost, path, query: "?x=1");

        outcome.NextCalled.ShouldBeTrue();
        outcome.NextPath.ShouldBe(path);
        outcome.NextQuery.ShouldBe("?x=1");
        outcome.Context.IsProductHost.ShouldBeTrue();
    }

    [Theory(Timeout = 30_000)]
    [InlineData("/p/dragon-poop", "https://support.dragonpoop.com/")]
    [InlineData("/p/dragon-poop/", "https://support.dragonpoop.com/")]
    [InlineData("/p/dragon-poop/contact", "https://support.dragonpoop.com/contact")]
    [InlineData("/p/dragon-poop/kb/accounts/reset-password", "https://support.dragonpoop.com/kb/accounts/reset-password")]
    public async Task The_same_products_own_prefix_on_its_host_is_a_301_to_the_clean_path_with_the_query(string path, string location)
    {
        var ct = TestContext.Current.CancellationToken;
        var outcome = await RunAsync(ct, DragonHost, path, query: "?x=1");

        outcome.NextCalled.ShouldBeFalse();
        outcome.Http.Response.StatusCode.ShouldBe(301);
        outcome.Http.Response.Headers.Location.ToString().ShouldBe(location + "?x=1");
    }

    [Fact(Timeout = 30_000)]
    public async Task A_head_request_is_redirected_like_a_get()
    {
        var ct = TestContext.Current.CancellationToken;
        var outcome = await RunAsync(ct, DragonHost, "/p/dragon-poop/contact", "HEAD");

        outcome.Http.Response.StatusCode.ShouldBe(301);
        outcome.Http.Response.Headers.Location.ToString().ShouldBe("https://support.dragonpoop.com/contact");
    }

    [Fact(Timeout = 30_000)]
    public async Task A_path_that_would_make_an_open_redirect_stays_on_the_stored_host()
    {
        var ct = TestContext.Current.CancellationToken;
        var outcome = await RunAsync(ct, DragonHost, "/p/dragon-poop//evil.test/x");

        outcome.Http.Response.StatusCode.ShouldBe(301);
        outcome.Http.Response.Headers.Location.ToString().ShouldStartWith("https://support.dragonpoop.com/");
    }

    [Fact(Timeout = 30_000)]
    public async Task Another_products_path_on_a_product_host_goes_to_that_products_host_when_it_has_one()
    {
        var ct = TestContext.Current.CancellationToken;
        var outcome = await RunAsync(ct, DragonHost, "/p/paper-plane/kb", query: "?page=2");

        outcome.NextCalled.ShouldBeFalse();
        outcome.Http.Response.StatusCode.ShouldBe(301);
        outcome.Http.Response.Headers.Location.ToString().ShouldBe("https://help.paperplane.com/kb?page=2");
    }

    [Fact(Timeout = 30_000)]
    public async Task Another_products_path_on_a_product_host_goes_to_the_public_url_when_that_product_has_no_host()
    {
        var ct = TestContext.Current.CancellationToken;
        var outcome = await RunAsync(ct, DragonHost, "/p/who-flung-poo/kb");

        outcome.Http.Response.StatusCode.ShouldBe(301);
        outcome.Http.Response.Headers.Location.ToString().ShouldBe("https://portal.test/p/who-flung-poo/kb");
    }

    [Theory(Timeout = 30_000)]
    [InlineData("portal.test")]
    [InlineData("evil.example")]
    public async Task On_the_default_host_and_an_unknown_host_a_products_path_with_a_host_is_a_301_to_that_host(string host)
    {
        var ct = TestContext.Current.CancellationToken;
        var outcome = await RunAsync(ct, host, "/p/dragon-poop/contact", query: "?x=1");

        outcome.NextCalled.ShouldBeFalse();
        outcome.Http.Response.StatusCode.ShouldBe(301);
        outcome.Http.Response.Headers.Location.ToString().ShouldBe("https://support.dragonpoop.com/contact?x=1");
    }

    [Theory(Timeout = 30_000)]
    [InlineData("localhost:5000")]
    [InlineData("127.0.0.1")]
    [InlineData("[::1]:8082")]
    public async Task A_host_that_can_never_be_a_product_host_is_never_looked_up_rewritten_or_redirected(string host)
    {
        var ct = TestContext.Current.CancellationToken;

        var contact = await RunAsync(ct, host, "/contact");
        var product = await RunAsync(ct, host, "/p/dragon-poop/contact");

        contact.NextPath.ShouldBe("/contact");
        product.NextCalled.ShouldBeTrue();
        product.NextPath.ShouldBe("/p/dragon-poop/contact");
        product.Http.Response.StatusCode.ShouldBe(200);
        contact.Context.IsProductHost.ShouldBeFalse();
    }

    [Fact(Timeout = 30_000)]
    public async Task On_the_default_host_a_product_without_a_host_is_untouched()
    {
        var ct = TestContext.Current.CancellationToken;
        var outcome = await RunAsync(ct, "portal.test", "/p/who-flung-poo/contact");

        outcome.NextCalled.ShouldBeTrue();
        outcome.NextPath.ShouldBe("/p/who-flung-poo/contact");
        outcome.Http.Response.StatusCode.ShouldBe(200);
    }

    [Fact(Timeout = 30_000)]
    public async Task A_post_on_a_product_host_is_rewritten_not_redirected_in_the_middleware()
    {
        var ct = TestContext.Current.CancellationToken;
        var outcome = await RunAsync(ct, DragonHost, "/contact", "POST");

        outcome.NextCalled.ShouldBeTrue();
        outcome.NextPath.ShouldBe("/p/dragon-poop/contact");
        outcome.Http.Response.StatusCode.ShouldBe(200);
        outcome.Http.Response.Headers.Location.ToString().ShouldBeEmpty();
    }

    [Theory(Timeout = 30_000)]
    [InlineData(DragonHost, "/p/dragon-poop/contact")]
    [InlineData(DragonHost, "/p/paper-plane/contact")]
    [InlineData("portal.test", "/p/dragon-poop/contact")]
    public async Task A_post_to_a_product_path_is_never_redirected_whatever_the_host(string host, string path)
    {
        var ct = TestContext.Current.CancellationToken;
        var outcome = await RunAsync(ct, host, path, "POST");

        outcome.NextCalled.ShouldBeTrue();
        outcome.NextPath.ShouldBe(path);
        outcome.Http.Response.StatusCode.ShouldBe(200);
    }

    [Theory(Timeout = 30_000)]
    [InlineData("evil.example")]
    [InlineData("support.dragonpoop.com.evil")]
    [InlineData("portal.test")]
    public async Task An_unknown_host_behaves_as_the_default_host_with_nothing_rewritten_and_no_product(string host)
    {
        var ct = TestContext.Current.CancellationToken;
        var contact = await RunAsync(ct, host, "/contact");
        var home = await RunAsync(ct, host, "/");

        contact.NextPath.ShouldBe("/contact");
        home.NextPath.ShouldBe("/");
        contact.Context.IsProductHost.ShouldBeFalse();
        contact.Context.Key.ShouldBeNull();
        contact.Context.Host.ShouldBeNull();
    }

    [Fact(Timeout = 30_000)]
    public async Task The_context_carries_the_key_and_the_stored_host_of_a_product_host()
    {
        var ct = TestContext.Current.CancellationToken;
        var outcome = await RunAsync(ct, "Support.DragonPoop.com:8443", "/");

        outcome.Context.IsProductHost.ShouldBeTrue();
        outcome.Context.Key.ShouldBe("dragon-poop");
        outcome.Context.Host.ShouldBe(DragonHost);
    }

    [Theory(Timeout = 30_000)]
    [InlineData("/p/dragon-poop", "dragon-poop", "")]
    [InlineData("/p/dragon-poop/", "dragon-poop", "/")]
    [InlineData("/p/dragon-poop/kb/a", "dragon-poop", "/kb/a")]
    [InlineData("/P/Dragon-Poop/kb", "Dragon-Poop", "/kb")]
    public void TryStripProductPrefix_splits_the_key_and_the_rest(string path, string key, string rest)
    {
        var ct = TestContext.Current.CancellationToken;
        PortalRoutes.TryStripProductPrefix(path, out var actualKey, out var actualRest).ShouldBeTrue();

        actualKey.ShouldBe(key);
        (actualRest.Value ?? string.Empty).ShouldBe(rest);
    }

    [Theory(Timeout = 30_000)]
    [InlineData("/")]
    [InlineData("/p")]
    [InlineData("/p/")]
    [InlineData("/pa/x")]
    [InlineData("/kb/p/x")]
    [InlineData("/t/p/x")]
    public void TryStripProductPrefix_rejects_other_paths(string path)
    {
        var ct = TestContext.Current.CancellationToken;
        PortalRoutes.TryStripProductPrefix(path, out _, out _).ShouldBeFalse();
    }
}
