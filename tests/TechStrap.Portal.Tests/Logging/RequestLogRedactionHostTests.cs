using Serilog.Events;
using TechStrap.Contracts.Products;

namespace TechStrap.Portal.Tests.Logging;

/// <summary>
/// P09-T17 at the host, at Verbose: the framework logs every request's path and query, so a <c>/t/{token}</c> address and the contact page's <c>?name=&amp;email=</c> prefill (09b) must never reach a log
/// event in the clear, in its message or in any property. The first checks of each test are the controls: the request line really was logged, with the secret masked, so the scan can see it.
/// </summary>
public sealed class RequestLogRedactionHostTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private const string Token = "AbCdEfGhIjKlMnOpQrStUvWxYz0123456789_-AbCdE";   // exactly 43 base64url characters

    private static string Everything(LogEvent e) => string.Join('\n', [e.RenderMessage(), e.Exception?.ToString() ?? string.Empty, .. e.Properties.Values.Select(v => v.ToString())]);

    private static PortalFactory Verbose()
    {
        var factory = new PortalFactory("Production", PortalFactory.VerboseLogging);
        factory.Api.OnJson(HttpMethod.Get, "/api/public/products/paperplane", new PublicProductDto("paperplane", "Paperplane", null, "#F59E0B", "#000000", "#9D6507"));
        return factory;
    }

    private static void AssertVerboseWasCaptured(PortalFactory factory) =>
        factory.LogSink.Events.ShouldContain(e => e.Level <= LogEventLevel.Debug, "the Verbose setting must have taken effect, or this only scanned Information and above");

    [Theory]
    [InlineData("/t/" + Token)]
    [InlineData("/T/" + Token + "/")]
    [InlineData("/t/" + Token + "/attachments/11111111-2222-3333-4444-555555555555")]
    [InlineData("/t/" + Token + "?x=1")]
    public async Task A_ticket_address_never_puts_the_token_in_a_log_event(string path)
    {
        await using var factory = Verbose();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(path, Ct);

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.NotFound);
        AssertVerboseWasCaptured(factory);
        factory.LogSink.Events.ShouldContain(e => Everything(e).Contains("[token]", StringComparison.Ordinal), "control: the request line was logged, with the token masked");
        factory.LogSink.Events.Select(Everything).ShouldAllBe(text => !text.Contains(Token, StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_prefill_query_values_of_the_contact_page_never_reach_a_log_event()
    {
        await using var factory = Verbose();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/p/paperplane/contact?subject=Printer&name=Jane%20Doe&email=jane.doe%40example.com", Ct);

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.NotFound, "the contact page arrives in PHASE-09b; the request is logged all the same");
        AssertVerboseWasCaptured(factory);
        factory.LogSink.Events.ShouldContain(e => Everything(e).Contains("name=[redacted]", StringComparison.Ordinal), "control: the query string was logged, with the name masked");
        factory.LogSink.Events.Select(Everything).ShouldAllBe(text =>
            !text.Contains("Jane", StringComparison.OrdinalIgnoreCase)
            && !text.Contains("Doe", StringComparison.Ordinal)
            && !text.Contains("jane.doe", StringComparison.OrdinalIgnoreCase)
            && !text.Contains("example.com", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("/p/paperplane?name=Jane+Doe")]
    [InlineData("/p/paperplane?NAME=Jane%20Doe&EMAIL=jane%40example.com")]
    [InlineData("/p/paperplane/kb/search?q=a&name=Jane%20Doe")]
    public async Task A_name_in_the_query_of_any_page_is_masked_too(string path)
    {
        await using var factory = Verbose();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(path, Ct);

        AssertVerboseWasCaptured(factory);
        factory.LogSink.Events.ShouldContain(e => Everything(e).Contains("[redacted]", StringComparison.Ordinal), "control: the query string was logged, masked");
        factory.LogSink.Events.Select(Everything).ShouldAllBe(text => !text.Contains("Jane", StringComparison.OrdinalIgnoreCase) && !text.Contains("Doe", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_name_the_page_echoes_never_reaches_the_log_through_the_api_call_either()
    {
        await using var factory = Verbose();
        using var client = factory.CreateClient();

        await client.GetAsync("/p/paperplane?name=Jane%20Doe", Ct);

        factory.Api.Requests.ShouldHaveSingleItem().Query.ShouldBeEmpty("the Portal sends the API the product key and nothing from the visitor's query");
    }
}
