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
public sealed class AdminLeakTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static readonly Guid AttachmentId = Guid.Parse("eeeeeeee-0000-0000-0000-000000000001");

    private static string Everything(LogEvent e) => string.Join('\n', [e.RenderMessage(), e.Exception?.ToString() ?? string.Empty, .. e.Properties.Values.Select(v => v.ToString())]);

    [Fact]
    public async Task The_access_token_appears_in_no_log_event_page_or_download()
    {
        await using var factory = new AdminFactory();
        // The queue and the ticket are not configured, so the stub answers 404 and the pages show their error states. A 5xx here would trip the read client's circuit
        // breaker and the download below would never reach the API.
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
        factory.LogSink.Events.ShouldNotBeEmpty();
        factory.LogSink.Events.Select(Everything).ShouldAllBe(text => !text.Contains(token) && !text.Contains("Bearer "));
    }
    // A secret shaped so the PII log redactor does not mask it: the test proves the Admin never logs it, not that the redactor hid it.
    private const string KeySecret = "tsk_live_leakcheck_0123456789abcdef0123456789abcdef";

    [Fact]
    public async Task The_api_key_secret_appears_in_no_log_event_page_url_or_header_when_a_key_is_created_through_the_host_pipeline()
    {
        var productId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
        var key = new ProductApiKeyDto(Guid.Parse("eeeeeeee-0000-0000-0000-000000000001"), productId, ApiKeyKinds.Trusted, "tsk_leakchk", "CI", DateTimeOffset.UtcNow, null, null);
        var product = new ProductDto(productId, "orbitly", "Orbitly", "ORB", true, new ProductBrandingDto("Orbitly", null, "#1D4ED8", "#FFFFFF", "#1D4ED8", null, null), 3);
        await using var factory = new AdminFactory();
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
        factory.LogSink.Events.ShouldNotBeEmpty();
        factory.LogSink.Events.Select(Everything).ShouldAllBe(text => !text.Contains(KeySecret) && !text.Contains("Bearer ") && !text.Contains(AdminTestPrincipal.Admin.AccessToken));
    }

    [Fact]
    public async Task The_access_token_appears_in_no_log_event_or_page_of_any_admin_page_for_an_admin()
    {
        var productId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
        await using var factory = new AdminFactory();
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
        factory.LogSink.Events.ShouldNotBeEmpty();
        factory.LogSink.Events.Select(Everything).ShouldAllBe(text => !text.Contains(token) && !text.Contains("Bearer "));
    }
}
