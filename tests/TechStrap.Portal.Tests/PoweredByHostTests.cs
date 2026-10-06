using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TechStrap.Contracts.Products;
using TechStrap.Portal.Components.Ui;

namespace TechStrap.Portal.Tests;

/// <summary>
/// P09-T22 (D-024): the "Powered by TechStrap" line is the only place the TechStrap name appears on the Portal. By default every page type carries exactly one link to the repository and no other
/// TechStrap text; the installation setting <c>TECHSTRAP_PORTAL_SHOW_POWERED_BY=false</c> removes the whole line everywhere; it defaults to true; and a value that is not true or false stops the start.
/// </summary>
public sealed partial class PoweredByHostTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private const string Href = "https://github.com/Syntax-Circus/techstrap";

    [GeneratedRegex("""<footer class="ts-powered">.*?</footer>""", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex FooterElement();

    // The root, a themed product page, the calm failure page of a product, an unknown product, an unknown route, the not-found page and the error page.
    public static TheoryData<string> PageTypes() => ["/", "/p/paperplane", "/p/down", "/p/gone", "/no/such/route", "/not-found", "/error"];

    private static PortalFactory Factory(string? setting = null)
    {
        var settings = setting is null ? null : new Dictionary<string, string?> { ["TECHSTRAP_PORTAL_SHOW_POWERED_BY"] = setting };
        var factory = new PortalFactory("Production", settings);
        factory.Api.OnJson(HttpMethod.Get, "/api/public/products/paperplane", new PublicProductDto("paperplane", "Paperplane", "https://cdn.example.com/logo.png", "#F59E0B", "#000000", "#9D6507"));
        factory.Api.OnStatus(HttpMethod.Get, "/api/public/products/down", System.Net.HttpStatusCode.ServiceUnavailable);
        factory.Api.OnProblem(HttpMethod.Get, "/api/public/products/gone", System.Net.HttpStatusCode.NotFound, "product-not-found", "No such product.");
        return factory;
    }

    // Whatever the status: a 404 and a 503 page are pages too.
    private static async Task<string> BodyAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path, Ct);
        return await response.Content.ReadAsStringAsync(Ct);
    }

    [Theory]
    [MemberData(nameof(PageTypes))]
    public async Task By_default_every_page_type_has_exactly_one_link_to_the_repository_and_no_other_TechStrap_text(string path)
    {
        await using var factory = Factory();
        using var client = factory.CreateClient();

        var html = await BodyAsync(client, path);

        Regex.Matches(html, "href=\"" + Regex.Escape(Href) + "\"").Count.ShouldBe(1, path);
        FooterElement().Matches(html).Count.ShouldBe(1, path);
        var footer = FooterElement().Match(html).Value;
        footer.ShouldContain("Powered by <a href=\"" + Href + "\" rel=\"noopener\">TechStrap</a>");
        FooterElement().Replace(html, string.Empty).ShouldNotContain("TechStrap", Case.Insensitive, $"{path}: the footer line is the only TechStrap text");
    }

    [Theory]
    [MemberData(nameof(PageTypes))]
    public async Task When_the_setting_is_false_no_page_type_has_the_line_the_link_or_the_name(string path)
    {
        await using var factory = Factory("false");
        using var client = factory.CreateClient();

        var html = await BodyAsync(client, path);

        html.ShouldNotContain("ts-powered", Case.Sensitive, path);
        html.ShouldNotContain(Href, Case.Sensitive, path);
        html.ShouldNotContain("TechStrap", Case.Insensitive, path);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("TRUE")]
    [InlineData("True")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_true_or_a_blank_setting_shows_the_line(string setting)
    {
        await using var factory = Factory(setting);
        using var client = factory.CreateClient();

        (await client.GetStringAsync("/", Ct)).ShouldContain("ts-powered");
    }

    [Fact]
    public async Task The_setting_defaults_to_true_when_the_variable_is_not_set_at_all()
    {
        await using var factory = Factory();
        using var client = factory.CreateClient();

        factory.Services.GetRequiredService<IOptions<PoweredByOptions>>().Value.Show.ShouldBeTrue();
        new PoweredByOptions().Show.ShouldBeTrue();
        (await client.GetStringAsync("/", Ct)).ShouldContain("ts-powered");
    }

    [Theory]
    [InlineData("maybe")]
    [InlineData("1")]
    [InlineData("0")]
    [InlineData("yes")]
    [InlineData("no")]
    [InlineData("tru")]
    [InlineData("truee")]
    [InlineData("false false")]
    public async Task A_value_that_is_not_true_or_false_stops_the_start_and_names_the_key(string setting)
    {
        await using var factory = Factory(setting);

        var failure = StartupFailure.Capture(factory);

        failure.Message.ShouldContain("TECHSTRAP_PORTAL_SHOW_POWERED_BY");
        failure.Message.ShouldContain("true or false");
    }
}
