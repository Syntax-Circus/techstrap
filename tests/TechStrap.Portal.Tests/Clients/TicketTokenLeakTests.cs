using Microsoft.Extensions.DependencyInjection;
using Serilog.Events;
using TechStrap.Contracts.Http;
using TechStrap.Contracts.Products;
using TechStrap.Portal.Clients;

namespace TechStrap.Portal.Tests.Clients;

/// <summary>
/// Review Focus 1 at the host: a call made as a ticket's customer sends the token in <c>X-Ticket-Token</c>, and the token is in no log event at any level. The calls really go through the host's
/// own client pipeline. Serilog's PII redactor masks a 43-character token, so a clean scan alone would prove little: the real test also pins that the Portal's clients write no log event of their own
/// (the factory's default logging is what writes request headers), and the negative control shows that the same scan does see a header value when that logging is put back.
/// </summary>
public sealed class TicketTokenLeakTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    // 43 base64url characters: the shape the Serilog redactor masks.
    private static readonly string Token = "Zk9_-" + new string('q', TicketToken.Length - 5);

    // Not the shape the redactor masks (47 characters, with a prefix and hyphens), so the control can see it in the sink.
    private const string Secret = "control-secret-0123456789abcdef0123456789abcdef";

    private static string Everything(LogEvent e) => string.Join('\n', [e.RenderMessage(), e.Exception?.ToString() ?? string.Empty, .. e.Properties.Values.Select(v => v.ToString())]);

    private static string SourceContext(LogEvent e) => e.Properties.TryGetValue("SourceContext", out var value) && value is ScalarValue { Value: string context } ? context : string.Empty;

    [Fact]
    public async Task Control_with_the_default_http_client_logging_put_back_a_header_value_reaches_the_Serilog_sink_at_Verbose()
    {
        await using var factory = new PortalFactory(
            settings: PortalFactory.VerboseLogging,
            configureServices: services => services.AddHttpClient(ApiClientNames.Read).AddDefaultLogger());
        factory.Api.OnJson(HttpMethod.Get, "/api/thing", new PublicProductDto("x", "X", null, "#000000", "#FFFFFF", "#000000"));
        using var host = factory.CreateClient(); // starts the host

        using var client = factory.Services.GetRequiredService<IHttpClientFactory>().CreateClient(ApiClientNames.Read);
        client.DefaultRequestHeaders.Add(HeaderNames.TicketToken, Secret);
        using var response = await client.GetAsync("api/thing", Ct);

        response.IsSuccessStatusCode.ShouldBeTrue();
        factory.Api.Requests.ShouldHaveSingleItem().TicketToken.ShouldBe(Secret, "the request must really have carried the header");
        factory.LogSink.Events.ShouldContain(e => SourceContext(e).StartsWith("System.Net.Http.HttpClient", StringComparison.Ordinal) && e.Level <= LogEventLevel.Debug, "no Debug-or-lower HttpClient event was captured, so the scan could not see this client");
        factory.LogSink.Events.Select(Everything).ShouldContain(text => text.Contains(Secret, StringComparison.Ordinal), "the factory's default logging must leak here, or the real check below cannot fail");
    }

    [Fact]
    public async Task The_token_appears_in_no_log_event_at_any_level_and_the_clients_write_no_event_of_their_own()
    {
        await using var factory = new PortalFactory(settings: PortalFactory.VerboseLogging);
        factory.Api.OnJson(HttpMethod.Get, "/api/customer/ticket", new PublicProductDto("x", "X", null, "#000000", "#FFFFFF", "#000000"));
        factory.Api.OnStatus(HttpMethod.Post, "/api/customer/ticket/replies", System.Net.HttpStatusCode.ServiceUnavailable);
        using var host = factory.CreateClient();
        await using var scope = factory.Services.CreateAsyncScope();
        var connection = scope.ServiceProvider.GetRequiredService<ApiConnection>();
        TicketToken.TryParse(Token, out var token).ShouldBeTrue();

        (await connection.GetAsync<PublicProductDto>("api/customer/ticket", token, Ct)).IsSuccess.ShouldBeTrue();
        (await connection.SendAsync(HttpMethod.Post, "api/customer/ticket/replies", new { body = "hello" }, token, Ct)).IsFailure.ShouldBeTrue();

        factory.Api.Requests.Count.ShouldBe(2, "the calls must really have been sent, or this test proves nothing");
        factory.Api.Requests.ShouldAllBe(r => r.TicketToken == Token);
        factory.LogSink.Events.ShouldContain(e => e.Level <= LogEventLevel.Debug, "the Verbose setting must have taken effect, or this only scanned Information and above");
        factory.LogSink.Events.Select(Everything).ShouldAllBe(text => !text.Contains(Token, StringComparison.Ordinal));
        factory.LogSink.Events.ShouldNotContain(e => SourceContext(e).StartsWith("System.Net.Http.HttpClient", StringComparison.Ordinal), "the Portal's API clients must have no logging handlers");
    }
}
