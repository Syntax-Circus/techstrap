using System.Net;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Events;
using TechStrap.Admin.Clients;
using TechStrap.Contracts.AdminEvents;
using TechStrap.Contracts.Agents;
using TechStrap.Contracts.ApiKeys;
using TechStrap.Contracts.DeadLetters;
using TechStrap.Contracts.Paging;
using TechStrap.Contracts.Products;
using TechStrap.Contracts.Tags;
using TechStrap.Tests.Shared.AdminHost;

namespace TechStrap.Api.Tests;

/// <summary>Review Focus 2 at the host: whatever the Admin does for a signed-in agent, the access token never reaches a log line, a page, or a download.</summary>
/// <remarks>The OTLP test sets process environment variables (the exporter options bind before host settings), so the class runs in the non-parallel <see cref="ProcessEnvironmentCollection"/>.</remarks>
[Collection(ProcessEnvironmentCollection.Name)]
public sealed class AdminLeakTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static readonly Guid AttachmentId = Guid.Parse("eeeeeeee-0000-0000-0000-000000000001");

    private static string Everything(LogEvent e) => string.Join('\n', [e.RenderMessage(), e.Exception?.ToString() ?? string.Empty, .. e.Properties.Values.Select(v => v.ToString())]);

    // Every level is captured, including the Trace and Debug lines of System.Net.Http and ASP.NET Core: a secret that only shows at Verbose is still a leak.
    private static AdminFactory VerboseFactory() => new(settings: new Dictionary<string, string?>
    {
        ["Serilog:MinimumLevel:Default"] = "Verbose",
        ["Serilog:MinimumLevel:Override:Microsoft"] = "Verbose",
        ["Serilog:MinimumLevel:Override:Microsoft.AspNetCore"] = "Verbose",
        ["Serilog:MinimumLevel:Override:System"] = "Verbose",
    });

    private static void AssertVerboseWasCaptured(AdminFactory factory) =>
        factory.LogSink.Events.ShouldContain(e => e.Level <= LogEventLevel.Debug, "the Verbose setting must have taken effect, or these tests only scanned Information and above");

    [Fact]
    public async Task The_access_token_appears_in_no_log_event_page_or_download()
    {
        await using var factory = VerboseFactory();
        // The queue and the ticket are not configured, so the stub answers 404 and the pages show their error states. The read client has no circuit breaker (D-040), so
        // nothing here can stop the download below reaching the API.
        factory.Api.On(HttpMethod.Get, $"/api/attachments/{AttachmentId}", _ => new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) });
        var token = AdminTestPrincipal.Agent.AccessToken;
        using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Agent);

        var pages = new List<string>
        {
            await client.GetStringAsync("/", Ct),
            await client.GetStringAsync("/queue/spam", Ct),
            await client.GetStringAsync("/tickets/ORB-42", Ct),
        };
        using var download = await client.GetAsync($"/attachments/{AttachmentId}", Ct);

        download.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
        factory.Api.Requests.ShouldContain(r => r.Path == $"/api/attachments/{AttachmentId}");
        factory.Api.AssertEveryCallBore(AdminTestPrincipal.Agent);
        factory.Api.Requests.ShouldContain(r => r.Authorization == "Bearer " + token, "the token must actually have been used, or this test proves nothing");
        pages.ShouldAllBe(html => !html.Contains(token));
        (await download.Content.ReadAsStringAsync(Ct)).ShouldNotContain(token);
        download.Headers.SelectMany(h => h.Value).ShouldAllBe(v => !v.Contains(token));
        AssertVerboseWasCaptured(factory);
        factory.LogSink.Events.Select(Everything).ShouldAllBe(text => !text.Contains(token) && !text.Contains("Bearer "));
    }
    [Fact]
    public async Task A_search_term_appears_in_no_log_event_after_a_mid_session_401()
    {
        // An email-shaped term the PII redactor would not necessarily mask: the test proves the Admin never logs it, not that a redactor hid it.
        const string term = "leakprobe.ada.lovelace@example.com";
        await using var factory = VerboseFactory();
        // The agent is let in by /me, then the first data call answers 401. After that the token is evicted, so every later call of the page must stay local.
        factory.Api.OnStatus(HttpMethod.Get, "/api/products", HttpStatusCode.Unauthorized);
        using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Agent);

        var html = await client.GetStringAsync($"/queue/all?search={Uri.EscapeDataString(term)}", Ct);

        factory.Api.Requests.ShouldContain(r => r.Path == "/api/products", "the 401 must actually have been answered, or this test proves nothing");
        html.ShouldNotBeNull();
        AssertVerboseWasCaptured(factory);
        factory.LogSink.Events.Select(Everything).ShouldAllBe(text => !text.Contains("leakprobe", StringComparison.OrdinalIgnoreCase) && !text.Contains(term, StringComparison.OrdinalIgnoreCase));
        factory.Api.Requests.Where(r => r.Path != "/api/agents/me" && r.Path != "/api/products").ShouldBeEmpty("once the session lapsed no request may be sent");
    }

    // A secret shaped so the PII log redactor does not mask it: the test proves the Admin never logs it, not that the redactor hid it.
    private const string KeySecret = "tsk_live_leakcheck_0123456789abcdef0123456789abcdef";

    [Fact]
    public async Task The_api_key_secret_appears_in_no_log_event_page_url_or_header_when_a_key_is_created_through_the_host_pipeline()
    {
        var productId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
        var key = new ProductApiKeyDto(Guid.Parse("eeeeeeee-0000-0000-0000-000000000001"), productId, ApiKeyKinds.Trusted, "tsk_leakchk", "CI", DateTimeOffset.UtcNow, null, null);
        var product = new ProductDto(productId, "orbitly", "Orbitly", "ORB", true, new ProductBrandingDto("Orbitly", null, "#1D4ED8", "#FFFFFF", "#1D4ED8", null, null), 3);
        await using var factory = VerboseFactory();
        factory.Api
            .OnJson(HttpMethod.Post, $"/api/products/{productId}/api-keys", new CreateProductApiKeyResponse(key, KeySecret), HttpStatusCode.Created)
            .OnJson(HttpMethod.Get, $"/api/products/{productId}/api-keys", (IReadOnlyList<ProductApiKeyDto>)[key])
            .OnJson(HttpMethod.Get, $"/api/products/{productId}", product)
            .OnJson(HttpMethod.Get, "/api/products", (IReadOnlyList<ProductDto>)[product])
            .On(HttpMethod.Delete, $"/api/products/{productId}/api-keys/{key.Id}", _ => new HttpResponseMessage(HttpStatusCode.NoContent));

        // The same call the keys page makes, through the host's own client pipeline (named clients, auth handler, token cache), as the admin: a circuit has no HTTP request, so the scope gets the sign-in the circuit would have.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var authentication = (IHostEnvironmentAuthenticationStateProvider)scope.ServiceProvider.GetRequiredService<AuthenticationStateProvider>();
            authentication.SetAuthenticationState(Task.FromResult(new AuthenticationState(AdminTestPrincipal.Admin.ToClaimsPrincipal(AdminTestAuth.Scheme))));
            var products = scope.ServiceProvider.GetRequiredService<IProductsClient>();

            var created = await products.CreateApiKeyAsync(productId, new CreateProductApiKeyRequest(ApiKeyKinds.Trusted, "CI"), CancellationToken.None);
            created.IsSuccess.ShouldBeTrue(string.Join("; ", created.IsFailure ? created.Errors.Select(e => e.Message) : []));
            created.Value.PlaintextKey.ShouldBe(KeySecret, "the secret really travelled through the pipeline, or this test proves nothing");
            (await products.RevokeApiKeyAsync(productId, key.Id, CancellationToken.None)).IsSuccess.ShouldBeTrue();
        }

        using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Admin);
        var pages = new List<string>
        {
            await client.GetStringAsync($"/settings/products/{productId}/keys", Ct),
            await client.GetStringAsync($"/settings/products/{productId}", Ct),
            await client.GetStringAsync("/settings/products", Ct),
        };

        pages.ShouldAllBe(html => !html.Contains(KeySecret));
        pages[0].ShouldContain("tsk_leakchk");
        factory.Api.Requests.ShouldContain(r => r.Method == HttpMethod.Post && r.Path.EndsWith("/api-keys", StringComparison.Ordinal));
        factory.Api.Requests.ShouldAllBe(r => !r.Path.Contains(KeySecret) && !r.Query.Contains(KeySecret), "the key is never in a URL");
        factory.Api.Requests.ShouldAllBe(r => r.Body == null || !r.Body.Contains(KeySecret), "nothing the Admin sends carries the key");
        factory.Api.AssertEveryCallBore(AdminTestPrincipal.Admin);
        AssertVerboseWasCaptured(factory);
        factory.LogSink.Events.Select(Everything).ShouldAllBe(text => !text.Contains(KeySecret) && !text.Contains("Bearer ") && !text.Contains(AdminTestPrincipal.Admin.AccessToken));
    }

    [Fact]
    public async Task The_access_token_appears_in_no_log_event_or_page_of_any_admin_page_for_an_admin()
    {
        var productId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
        await using var factory = VerboseFactory();
        var product = new ProductDto(productId, "orbitly", "Orbitly", "ORB", true, new ProductBrandingDto("Orbitly", null, "#1D4ED8", "#FFFFFF", "#1D4ED8", null, null), 3);
        factory.Api
            .OnJson(HttpMethod.Get, "/api/products", (IReadOnlyList<ProductDto>)[product])
            .OnJson(HttpMethod.Get, $"/api/products/{productId}", product)
            .OnJson(HttpMethod.Get, $"/api/products/{productId}/api-keys", (IReadOnlyList<ProductApiKeyDto>)[])
            .OnJson(HttpMethod.Get, "/api/agents", new PagedResponse<AgentListItemDto>([], 1, 25, 0))
            .OnJson(HttpMethod.Get, "/api/agents/me/notification-preferences", (IReadOnlyList<NotificationPreferenceDto>)[])
            .OnJson(HttpMethod.Get, "/api/tags/summary", (IReadOnlyList<TagSummaryDto>)[])
            .OnJson(HttpMethod.Get, "/api/admin-events", new PagedResponse<AdminEventDto>([], 1, 25, 0))
            .OnJson(HttpMethod.Get, "/api/dead-letters", new PagedResponse<DeadLetterDto>([], 1, 25, 0));
        var token = AdminTestPrincipal.Admin.AccessToken;
        using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Admin);

        var pages = new List<string>();
        foreach (var path in new[]
                 {
                     "/settings/products", "/settings/products/new", $"/settings/products/{productId}", $"/settings/products/{productId}/keys", "/settings/agents", "/settings/tags",
                     "/settings/audit", "/ops/dead-letters", "/account/notifications",
                 })
        {
            pages.Add(await client.GetStringAsync(path, Ct));
        }

        factory.Api.Requests.ShouldContain(r => r.Authorization == "Bearer " + token, "the token must actually have been used, or this test proves nothing");
        factory.Api.AssertEveryCallBore(AdminTestPrincipal.Admin);
        pages.ShouldAllBe(html => !html.Contains(token));
        AssertVerboseWasCaptured(factory);
        factory.LogSink.Events.Select(Everything).ShouldAllBe(text => !text.Contains(token) && !text.Contains("Bearer "));
    }

    // The OTLP exporter makes its HTTP calls through IHttpClientFactory. The factory's default logging handler writes raw header values into structured log state at Trace, which would put an
    // OTLP "x-api-key" in the logs. The Admin removes the default logging handler from every factory client, so the secret never reaches a log event.
    private const string OtlpSecret = "otlp-secret-0123456789abcdef0123456789abcdef";

    [Fact]
    public async Task An_OTLP_header_secret_appears_in_no_log_event_even_at_Verbose()
    {
        // The observability options are read while Program.cs builds the host, before the factory's in-memory settings exist, so they arrive as environment variables (like the test issuer does).
        var variables = new Dictionary<string, string>
        {
            ["OpenTelemetry__Enabled"] = "true",
            ["OpenTelemetry__OtlpEndpoint"] = "http://127.0.0.1:1/",
            ["OpenTelemetry__OtlpProtocol"] = "http/protobuf",
            ["OpenTelemetry__Headers"] = $"x-api-key={OtlpSecret}",
        };
        foreach (var (key, value) in variables)
        {
            Environment.SetEnvironmentVariable(key, value);
        }

        var factory = VerboseFactory();
        try
        {
            using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Agent);
            (await client.GetStringAsync("/", Ct)).ShouldNotContain(OtlpSecret);
            (await client.GetStringAsync("/queue/spam", Ct)).ShouldNotContain(OtlpSecret);
        }
        finally
        {
            // Disposing the host flushes the trace exporter, which sends (and fails against the closed port) through the factory's HTTP client.
            await factory.DisposeAsync();
            foreach (var key in variables.Keys)
            {
                Environment.SetEnvironmentVariable(key, null);
            }
        }

        AssertVerboseWasCaptured(factory);
        factory.LogSink.Events.Select(Everything).ShouldAllBe(text => !text.Contains(OtlpSecret));
    }
}
