using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using TechStrap.Contracts.Products;
using TechStrap.Portal.Products;

namespace TechStrap.Portal.Tests.Products;

/// <summary>
/// Review Focus 3 (product enumeration) and the PHASE-04 no-brand guard. An unknown, an inactive and a malformed product key are answered exactly like an unknown route, so nothing tells a
/// visitor which products exist; and the not-found and error pages are never branded, because they are rendered in a fresh scope that knows no product.
/// </summary>
public sealed class NeutralPagesGuardTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static readonly PublicProductDto Paperplane = new("paperplane", "Paperplane", "https://cdn.example.com/paperplane.png", "#F59E0B", "#000000", "#9D6507");

    private static async Task<(HttpStatusCode Status, string Body, string[] Headers)> GetAsync(PortalFactory factory, string path)
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(path, Ct);
        var headers = response.Headers.Concat(response.Content.Headers).Where(h => h.Key is not ("Date" or "X-Correlation-Id" or "Content-Length")).OrderBy(h => h.Key).Select(h => $"{h.Key}: {string.Join(",", h.Value)}").ToArray();
        return (response.StatusCode, await response.Content.ReadAsStringAsync(Ct), headers);
    }

    private static PortalFactory Factory(string environment = "Production", Action<IServiceCollection>? configure = null)
    {
        var factory = new PortalFactory(environment, configureServices: configure);
        factory.Api.OnJson(HttpMethod.Get, "/api/public/products/paperplane", Paperplane);
        factory.Api.OnProblem(HttpMethod.Get, "/api/public/products/gone", HttpStatusCode.NotFound, "product-not-found", "No such product.");
        factory.Api.OnProblem(HttpMethod.Get, "/api/public/products/inactive", HttpStatusCode.NotFound, "product-not-found", "That product is not active.");
        return factory;
    }

    [Fact]
    public async Task An_unknown_inactive_and_malformed_key_are_identical_to_each_other_and_their_page_is_byte_identical_to_an_unknown_routes()
    {
        await using var factory = Factory();
        var unknownRoute = await GetAsync(factory, "/no/such/route");

        var answers = new List<(string Path, (HttpStatusCode Status, string Body, string[] Headers) Answer)>();
        foreach (var path in new[] { "/p/gone", "/p/inactive", "/p/BAD_KEY", "/p/Paperplane", "/p/a%20b", "/p/-x", "/p/x--y", "/p/" + new string('a', 41) })
        {
            answers.Add((path, await GetAsync(factory, path)));
        }

        unknownRoute.Status.ShouldBe(HttpStatusCode.NotFound);
        unknownRoute.Body.ShouldContain("Page not found");
        var first = answers[0].Answer;
        foreach (var (path, answer) in answers)
        {
            // Nothing about the answer tells an unknown product from an inactive one from a malformed key: the status, every header and the whole body match.
            answer.Status.ShouldBe(HttpStatusCode.NotFound, path);
            answer.Headers.ShouldBe(first.Headers, path);
            answer.Body.ShouldBe(first.Body, path);

            // And the page is the very page an unknown route gets.
            answer.Body.ShouldBe(unknownRoute.Body, path);
        }
    }

    [Fact]
    public async Task A_malformed_key_never_reaches_the_api_and_an_unknown_one_costs_exactly_one_read()
    {
        await using var factory = Factory();

        await GetAsync(factory, "/p/BAD_KEY");
        await GetAsync(factory, "/p/a%20b");
        await GetAsync(factory, "/p/" + new string('a', 41));
        factory.Api.Requests.ShouldBeEmpty("a key that is not a slug is answered without a call");

        await GetAsync(factory, "/p/gone");
        factory.Api.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task The_not_found_page_has_no_product_in_it_and_no_api_call_is_needed_to_render_it()
    {
        await using var factory = Factory();

        var (status, body, _) = await GetAsync(factory, "/p/gone");

        status.ShouldBe(HttpStatusCode.NotFound);
        body.ShouldNotContain("gone");
        body.ShouldNotContain("--ts-accent");
        body.ShouldNotContain("ts-product");
        body.ShouldNotContain("style=");
        body.ShouldContain("ts-powered");
        factory.Api.Count(HttpMethod.Get, "/api/public/products/gone").ShouldBe(1);
    }

    // Unknown route under a known product: the 404 is rendered in its own scope (the status-code re-execution), which has no product.
    [Fact]
    public async Task An_unknown_route_under_a_known_product_is_the_neutral_404_not_a_branded_one()
    {
        await using var factory = Factory();

        var (status, body, _) = await GetAsync(factory, "/p/paperplane/no-such-page");

        status.ShouldBe(HttpStatusCode.NotFound);
        body.ShouldNotContain("Paperplane");
        body.ShouldNotContain("--ts-accent");
        body.ShouldNotContain("ts-product");
    }

    // The product is per request: one visitor's product must never theme the next visitor's neutral page.
    [Fact]
    public async Task A_product_page_never_themes_a_later_request_to_a_neutral_page()
    {
        await using var factory = Factory();
        using var client = factory.CreateClient();

        (await client.GetStringAsync("/p/paperplane", Ct)).ShouldContain("--ts-accent:#F59E0B");
        using var unknownRoute = await client.GetAsync("/no/such/route", Ct);
        using var unknownProduct = await client.GetAsync("/p/gone", Ct);
        using var error = await client.GetAsync("/error", Ct);
        var root = await client.GetStringAsync("/", Ct);

        foreach (var body in new[] { await unknownRoute.Content.ReadAsStringAsync(Ct), await unknownProduct.Content.ReadAsStringAsync(Ct), await error.Content.ReadAsStringAsync(Ct), root })
        {
            body.ShouldNotContain("Paperplane");
            body.ShouldNotContain("--ts-accent");
            body.ShouldNotContain("ts-product");
        }
    }

    private sealed class BrandThenThrowStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);
            app.Map("/__test/brand-then-throw", branch => branch.Run(context =>
            {
                // A page that had already resolved its product, then failed: the product is in this request's scope.
                context.RequestServices.GetRequiredService<ProductScope>().Set(new ProductThemeViewModel("paperplane", "Paperplane", "#F59E0B", "https://cdn.example.com/paperplane.png"));
                throw new InvalidOperationException("boom Paperplane");
            }));
        };
    }

    [Fact]
    public async Task An_unhandled_exception_after_the_product_was_resolved_is_the_plain_neutral_error_page()
    {
        await using var factory = Factory(configure: services => services.AddSingleton<IStartupFilter, BrandThenThrowStartupFilter>());

        var (status, body, _) = await GetAsync(factory, "/__test/brand-then-throw");

        status.ShouldBe(HttpStatusCode.InternalServerError);
        body.ShouldContain("Something went wrong.");
        body.ShouldNotContain("Paperplane");
        body.ShouldNotContain("boom");
        body.ShouldNotContain("--ts-accent");
        body.ShouldNotContain("ts-product");
        body.ShouldNotContain("style=");
    }

    [Fact]
    public async Task The_error_and_not_found_pages_themselves_are_neutral()
    {
        await using var factory = Factory();

        var error = await GetAsync(factory, "/error");
        var notFound = await GetAsync(factory, "/not-found");

        foreach (var (_, body, _) in new[] { error, notFound })
        {
            body.ShouldNotContain("ts-product");
            body.ShouldNotContain("--ts-accent");
            body.ShouldNotContain("style=");
        }

        factory.Api.Requests.ShouldBeEmpty();
    }
}
