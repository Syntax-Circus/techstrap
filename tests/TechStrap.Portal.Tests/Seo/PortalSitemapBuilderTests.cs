using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SyntaxCircus.AspNetCore.Common;
using TechStrap.Contracts.Kb;
using TechStrap.Contracts.Products;
using TechStrap.Portal.Clients;
using TechStrap.Portal.Hosting;
using TechStrap.Portal.Routing;
using TechStrap.Portal.Seo;
using TechStrap.Portal.Settings;
using TechStrap.Portal.Tests.Api;

namespace TechStrap.Portal.Tests.Seo;

/// <summary>
/// PHASE-09c Review Focus 4 (sitemap robustness), the content of the sitemap: absolute addresses built from the public URL, a shared article under each product, the categories derived from the articles, the 50,000
/// cap, and a build that fails whole rather than listing half a site. The API only ever returns published articles, so the build lists exactly what it is given.
/// </summary>
public sealed class PortalSitemapBuilderTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static readonly DateTimeOffset Newer = new(2026, 10, 5, 23, 59, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Older = new(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);

    private static KbSitemapEntryDto Article(string? product, string category, string slug, DateTimeOffset updated) => new(product, category, slug, updated);

    private static async Task<IReadOnlyList<SitemapEntry>> BuildAsync(ApiHarness api, int reservedForStatic = 0, ProductHostContext? host = null) =>
        await api.Get<PortalSitemapBuilder>().BuildAsync(reservedForStatic, host ?? new ProductHostContext(), Ct);

    private static ApiHarness Harness()
    {
        var api = ApiHarness.Create();
        api.Stub.OnJson(HttpMethod.Get, "/api/public/products", new[] { new PublicProductSummaryDto("acme", "Acme"), new PublicProductSummaryDto("orbitly", "Orbitly") });
        return api;
    }

    [Fact]
    public async Task Each_products_home_help_centre_categories_and_articles_are_listed_with_absolute_addresses()
    {
        using var api = Harness();
        api.Stub.OnJson(HttpMethod.Get, "/api/public/kb/acme/sitemap", new[]
        {
            Article("acme", "accounts", "reset-password", Newer),
            Article("acme", "billing", "invoices", Older),
            Article("acme", "accounts", "change-email", Older),
        });
        api.Stub.OnJson(HttpMethod.Get, "/api/public/kb/orbitly/sitemap", Array.Empty<KbSitemapEntryDto>());

        var entries = await BuildAsync(api);

        entries.Select(entry => entry.Url).ShouldBe(
        [
            "https://portal.test/p/acme",
            "https://portal.test/p/acme/kb",
            "https://portal.test/p/acme/kb/accounts",
            "https://portal.test/p/acme/kb/billing",
            "https://portal.test/p/acme/kb/accounts/reset-password",
            "https://portal.test/p/acme/kb/billing/invoices",
            "https://portal.test/p/acme/kb/accounts/change-email",
            "https://portal.test/p/orbitly",
        ]);
        entries.ShouldAllBe(entry => entry.Url.StartsWith("https://portal.test/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_category_and_the_help_centre_home_are_as_new_as_their_newest_article_and_an_article_has_its_own_day()
    {
        using var api = Harness();
        api.Stub.OnJson(HttpMethod.Get, "/api/public/kb/acme/sitemap", new[]
        {
            Article("acme", "accounts", "old-one", Older),
            Article("acme", "accounts", "new-one", Newer),
            Article("acme", "billing", "invoices", Older),
        });
        api.Stub.OnJson(HttpMethod.Get, "/api/public/kb/orbitly/sitemap", Array.Empty<KbSitemapEntryDto>());

        var entries = (await BuildAsync(api)).ToDictionary(entry => entry.Url, entry => entry.LastModified);

        entries["https://portal.test/p/acme"].ShouldBeNull("a product home has no date of its own");
        entries["https://portal.test/p/acme/kb"].ShouldBe(new DateOnly(2026, 10, 5));
        entries["https://portal.test/p/acme/kb/accounts"].ShouldBe(new DateOnly(2026, 10, 5));
        entries["https://portal.test/p/acme/kb/billing"].ShouldBe(new DateOnly(2026, 9, 1));
        entries["https://portal.test/p/acme/kb/accounts/old-one"].ShouldBe(new DateOnly(2026, 9, 1));
        entries["https://portal.test/p/acme/kb/accounts/new-one"].ShouldBe(new DateOnly(2026, 10, 5));
    }

    [Fact]
    public async Task A_shared_article_is_listed_under_each_product_it_is_visible_to_and_never_under_a_path_of_its_own()
    {
        using var api = Harness();
        api.Stub.OnJson(HttpMethod.Get, "/api/public/kb/acme/sitemap", new[] { Article(null, "general", "shared-tips", Newer), Article("acme", "general", "acme-only", Older) });
        api.Stub.OnJson(HttpMethod.Get, "/api/public/kb/orbitly/sitemap", new[] { Article(null, "general", "shared-tips", Newer) });

        var urls = (await BuildAsync(api)).Select(entry => entry.Url).ToList();

        urls.ShouldContain("https://portal.test/p/acme/kb/general/shared-tips");
        urls.ShouldContain("https://portal.test/p/orbitly/kb/general/shared-tips");
        urls.ShouldContain("https://portal.test/p/acme/kb/general/acme-only");
        urls.ShouldNotContain(url => url.Contains("/p//", StringComparison.Ordinal) || url.Contains("/p/kb", StringComparison.Ordinal));
        urls.Count(url => url.EndsWith("/shared-tips", StringComparison.Ordinal)).ShouldBe(2);
    }

    [Fact]
    public async Task A_product_with_no_article_has_a_home_and_no_help_centre_entries()
    {
        using var api = Harness();
        api.Stub.OnJson(HttpMethod.Get, "/api/public/kb/acme/sitemap", Array.Empty<KbSitemapEntryDto>());
        api.Stub.OnJson(HttpMethod.Get, "/api/public/kb/orbitly/sitemap", Array.Empty<KbSitemapEntryDto>());

        (await BuildAsync(api)).Select(entry => entry.Url).ShouldBe(["https://portal.test/p/acme", "https://portal.test/p/orbitly"]);
    }

    [Fact]
    public async Task No_product_at_all_is_an_empty_list_and_one_call()
    {
        using var api = ApiHarness.Create();
        api.Stub.OnJson(HttpMethod.Get, "/api/public/products", Array.Empty<PublicProductSummaryDto>());

        (await BuildAsync(api)).ShouldBeEmpty();
        api.Stub.Requests.ShouldHaveSingleItem().Path.ShouldBe("/api/public/products");
    }

    [Fact]
    public async Task The_build_asks_once_for_the_products_and_once_per_product_forwarding_the_visitors_address()
    {
        using var api = Harness();
        api.Stub.OnJson(HttpMethod.Get, "/api/public/kb/acme/sitemap", Array.Empty<KbSitemapEntryDto>());
        api.Stub.OnJson(HttpMethod.Get, "/api/public/kb/orbitly/sitemap", Array.Empty<KbSitemapEntryDto>());

        await BuildAsync(api);

        api.Stub.Requests.Select(request => request.Path).ShouldBe(["/api/public/products", "/api/public/kb/acme/sitemap", "/api/public/kb/orbitly/sitemap"]);
        api.Stub.Requests.ShouldAllBe(request => request.Client == ApiClientNames.Read);
        api.Stub.AssertEveryCallBore(ApiHarness.DefaultClientIp);
    }

    [Fact]
    public async Task A_slug_with_characters_outside_a_path_segment_is_escaped_so_it_cannot_add_a_segment_a_query_or_a_fragment()
    {
        using var api = Harness();
        api.Stub.OnJson(HttpMethod.Get, "/api/public/kb/acme/sitemap", new[] { Article("acme", "a/b?c#d", "x y&z", Newer) });
        api.Stub.OnJson(HttpMethod.Get, "/api/public/kb/orbitly/sitemap", Array.Empty<KbSitemapEntryDto>());

        var urls = (await BuildAsync(api)).Select(entry => entry.Url).ToList();

        urls.ShouldContain("https://portal.test/p/acme/kb/a%2Fb%3Fc%23d/x%20y%26z");
        urls.ShouldContain("https://portal.test/p/acme/kb/a%2Fb%3Fc%23d");
    }

    [Fact]
    public async Task The_same_address_twice_is_listed_once()
    {
        using var api = Harness();
        api.Stub.OnJson(HttpMethod.Get, "/api/public/kb/acme/sitemap", new[] { Article("acme", "accounts", "reset", Newer), Article(null, "accounts", "reset", Older) });
        api.Stub.OnJson(HttpMethod.Get, "/api/public/kb/orbitly/sitemap", Array.Empty<KbSitemapEntryDto>());

        var urls = (await BuildAsync(api)).Select(entry => entry.Url).ToList();

        urls.Count(url => url.EndsWith("/accounts/reset", StringComparison.Ordinal)).ShouldBe(1);
        urls.Distinct().Count().ShouldBe(urls.Count);
    }

    [Fact]
    public async Task At_most_fifty_thousand_addresses_are_listed_the_first_ones_and_the_cut_is_logged()
    {
        var logs = new List<string>();
        using var api = ApiHarness.Create(configure: services => services.AddLogging(logging => logging.AddProvider(new ListLoggerProvider(logs))));
        api.Stub.OnJson(HttpMethod.Get, "/api/public/products", new[] { new PublicProductSummaryDto("acme", "Acme") });
        api.Stub.OnJson(
            HttpMethod.Get,
            "/api/public/kb/acme/sitemap",
            Enumerable.Range(0, PortalSitemapBuilder.MaxUrls + 10).Select(i => Article("acme", "accounts", $"article-{i}", Newer)).ToArray());

        var entries = await BuildAsync(api);

        entries.Count.ShouldBe(PortalSitemapBuilder.MaxUrls);
        entries[0].Url.ShouldBe("https://portal.test/p/acme");
        entries.ShouldNotContain(entry => entry.Url.EndsWith("/article-" + (PortalSitemapBuilder.MaxUrls + 5), StringComparison.Ordinal));
        logs.ShouldContain(line => line.Contains("only the first 50000 are listed", StringComparison.Ordinal));
        PortalSitemapBuilder.MaxUrls.ShouldBe(50_000);
    }

    [Fact]
    public async Task The_static_entries_count_against_the_limit_so_the_whole_file_never_goes_over_it()
    {
        using var api = ApiHarness.Create();
        api.Stub.OnJson(HttpMethod.Get, "/api/public/products", new[] { new PublicProductSummaryDto("acme", "Acme") });
        api.Stub.OnJson(
            HttpMethod.Get,
            "/api/public/kb/acme/sitemap",
            Enumerable.Range(0, PortalSitemapBuilder.MaxUrls + 10).Select(i => Article("acme", "accounts", $"article-{i}", Newer)).ToArray());

        var entries = await BuildAsync(api, reservedForStatic: 1);

        entries.Count.ShouldBe(PortalSitemapBuilder.MaxUrls - 1);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task A_failing_product_list_fails_the_build_and_nothing_is_listed(HttpStatusCode status)
    {
        using var api = ApiHarness.Create();
        api.Stub.OnStatus(HttpMethod.Get, "/api/public/products", status);

        var error = await Should.ThrowAsync<SitemapBuildException>(() => BuildAsync(api));

        error.Message.ShouldStartWith("The product list failed");
    }

    [Fact]
    public async Task One_products_failing_sitemap_fails_the_whole_build_rather_than_leaving_it_out_for_fifteen_minutes()
    {
        using var api = Harness();
        api.Stub.OnJson(HttpMethod.Get, "/api/public/kb/acme/sitemap", new[] { Article("acme", "accounts", "reset", Newer) });
        api.Stub.OnStatus(HttpMethod.Get, "/api/public/kb/orbitly/sitemap", HttpStatusCode.TooManyRequests);

        var error = await Should.ThrowAsync<SitemapBuildException>(() => BuildAsync(api));

        error.Message.ShouldStartWith("The sitemap of a product failed");
        error.Message.ShouldNotContain("orbitly", Case.Insensitive);
    }

    [Theory]
    [InlineData(null, "https://portal.test/")]
    [InlineData("", "https://portal.test/")]
    [InlineData("   ", "https://portal.test/")]
    [InlineData("paperplane", null)]
    public void The_root_page_is_a_static_entry_only_when_no_default_product_is_configured_because_then_it_is_a_redirect(string? defaultProduct, string? expected)
    {
        var options = new PortalOptions { ApiBaseUrl = "http://api.test/", PublicUrl = "https://portal.test/", DefaultProduct = defaultProduct };

        var entries = PortalSitemap.StaticEntries(options).Select(entry => entry.Url).ToList();

        entries.ShouldBe(expected is null ? [] : [expected]);
    }

    private static ApiHarness HostHarness()
    {
        var api = ApiHarness.Create();
        api.Stub.OnJson(HttpMethod.Get, "/api/public/products", new[] { new PublicProductSummaryDto("acme", "Acme", "support.acme.test"), new PublicProductSummaryDto("orbitly", "Orbitly") });
        api.Stub.OnJson(HttpMethod.Get, "/api/public/kb/acme/sitemap", new[] { Article("acme", "accounts", "reset", Newer) });
        api.Stub.OnJson(HttpMethod.Get, "/api/public/kb/orbitly/sitemap", new[] { Article("orbitly", "billing", "invoices", Older) });
        return api;
    }

    [Fact]
    public async Task On_a_product_host_only_that_product_is_listed_with_clean_paths_on_its_host()
    {
        using var api = HostHarness();

        var urls = (await BuildAsync(api, host: new ProductHostContext { Key = "acme", Host = "support.acme.test" })).Select(entry => entry.Url).ToList();

        urls.ShouldBe(
        [
            "https://support.acme.test/",
            "https://support.acme.test/kb",
            "https://support.acme.test/kb/accounts",
            "https://support.acme.test/kb/accounts/reset",
        ]);
        api.Stub.Requests.ShouldNotContain(request => request.Path.Contains("orbitly", StringComparison.Ordinal), "the other product's articles are not even asked for");
    }

    [Fact]
    public async Task On_the_default_host_a_product_with_its_own_host_is_not_listed()
    {
        using var api = HostHarness();

        var urls = (await BuildAsync(api)).Select(entry => entry.Url).ToList();

        urls.ShouldBe(["https://portal.test/p/orbitly", "https://portal.test/p/orbitly/kb", "https://portal.test/p/orbitly/kb/billing", "https://portal.test/p/orbitly/kb/billing/invoices"]);
        urls.ShouldNotContain(url => url.Contains("acme", StringComparison.Ordinal));
    }

    // Review F5: the addresses come from the fresh product list, never from the map's snapshot, which may be up to a minute old.
    [Fact]
    public async Task A_host_cleared_in_the_fresh_list_gives_default_host_addresses_even_while_the_map_still_has_it()
    {
        using var api = HostHarness();
        await api.Get<ProductHostMap>().RefreshAsync(Ct);
        api.Get<ProductHostMap>().TryGetHost("acme", out _).ShouldBeTrue("the snapshot still holds the old host");
        api.Stub.OnJson(HttpMethod.Get, "/api/public/products", new[] { new PublicProductSummaryDto("acme", "Acme"), new PublicProductSummaryDto("orbitly", "Orbitly") });

        var urls = (await BuildAsync(api)).Select(entry => entry.Url).ToList();

        urls.ShouldContain("https://portal.test/p/acme");
        urls.ShouldContain("https://portal.test/p/acme/kb/accounts/reset");
        urls.ShouldNotContain(url => url.Contains("support.acme.test", StringComparison.Ordinal));
    }

    [Fact]
    public void The_entries_of_a_product_are_relative_when_there_is_no_public_address()
    {
        using var api = Harness();
        var links = new PortalLinks(new ProductHostContext(), api.Get<ProductHostMap>(), Microsoft.Extensions.Options.Options.Create(new PortalOptions()));
        var entries = PortalSitemapBuilder.EntriesOf(links, "acme", [Article("acme", "accounts", "reset", Newer)]).Select(entry => entry.Url).ToList();

        entries.ShouldBe(["/p/acme", "/p/acme/kb", "/p/acme/kb/accounts", "/p/acme/kb/accounts/reset"]);
    }

    private sealed class ListLoggerProvider(List<string> lines) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new ListLogger(lines);

        public void Dispose()
        {
        }

        private sealed class ListLogger(List<string> lines) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (lines)
                {
                    lines.Add(formatter(state, exception));
                }
            }
        }
    }
}
