using System.Net;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog.Events;
using TechStrap.Admin.Clients;
using TechStrap.Contracts.Kb;
using TechStrap.Contracts.Products;
using TechStrap.Tests.Shared.AdminHost;

namespace TechStrap.Api.Tests;

/// <summary>
/// Never log search text, an article's text or a picture's file name (PHASE-08, the 07c lesson): whatever the Admin does for a signed-in agent in the knowledge base, the words an agent typed or chose never reach a log line, even at
/// Verbose, and a lapsed session sends nothing more (the auth package logs the path and query of an unauthenticated call, which would carry the search text). Each test proves the text really traveled, or it proves nothing.
/// </summary>
/// <remarks>Runs in the non-parallel <see cref="ProcessEnvironmentCollection"/> like <see cref="AdminLeakTests"/>: the Admin host reads process environment variables while it starts.</remarks>
[Collection(ProcessEnvironmentCollection.Name)]
public sealed class KbAdminLeakTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static readonly Guid ProductId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid ArticleId = Guid.Parse("dddddddd-0000-0000-0000-000000000001");
    private static readonly Guid CategoryId = Guid.Parse("cccccccc-0000-0000-0000-000000000001");

    // Words shaped so the PII log redactor would not necessarily mask them: the test proves the Admin never logs them, not that a redactor hid them.
    private const string SearchTerm = "leakprobe.kb.search@example.com";
    private const string ArticleText = "leakprobe-confidential-draft-text-0123456789";
    private const string PictureName = "leakprobe-board-minutes.png";

    private static string Everything(LogEvent e) => string.Join('\n', [e.RenderMessage(), e.Exception?.ToString() ?? string.Empty, .. e.Properties.Values.Select(v => v.ToString())]);

    private static AdminFactory VerboseFactory() => new(settings: new Dictionary<string, string?>
    {
        ["Serilog:MinimumLevel:Default"] = "Verbose",
        ["Serilog:MinimumLevel:Override:Microsoft"] = "Verbose",
        ["Serilog:MinimumLevel:Override:Microsoft.AspNetCore"] = "Verbose",
        ["Serilog:MinimumLevel:Override:System"] = "Verbose",
    });

    private static void AssertVerboseWasCaptured(AdminFactory factory) =>
        factory.LogSink.Events.ShouldContain(e => e.Level <= LogEventLevel.Debug, "the Verbose setting must have taken effect, or this test only scanned Information and above");

    private static void AssertNothingLeaked(AdminFactory factory) =>
        factory.LogSink.Events.Select(Everything).ShouldAllBe(text =>
            !text.Contains("leakprobe", StringComparison.OrdinalIgnoreCase) && !text.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase)
            && !text.Contains(ArticleText, StringComparison.OrdinalIgnoreCase) && !text.Contains(PictureName, StringComparison.OrdinalIgnoreCase));

    private static void StubKb(AdminFactory factory)
    {
        var product = new ProductDto(ProductId, "orbitly", "Orbitly", "ORB", true, new ProductBrandingDto("Orbitly", null, "#1D4ED8", "#FFFFFF", "#1D4ED8", null, null), 1);
        factory.Api
            .OnJson(HttpMethod.Get, "/api/products", (IReadOnlyList<ProductDto>)[product])
            .OnJson(HttpMethod.Get, "/api/kb/categories", (IReadOnlyList<KbCategoryDto>)[new KbCategoryDto(CategoryId, ProductId, "account", "Account", null, 10, 1)])
            .OnJson(HttpMethod.Get, $"/api/kb/articles/{ArticleId}", new KbArticleDto(
                ArticleId, ProductId, CategoryId, "reset-password", "Reset your password", "How.", ArticleText, KbArticleStatuses.Draft, Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, 3));
    }

    // The positive control: a scan that cannot see a term proves nothing, so the scan is shown to fail when a term is logged.
    [Fact]
    public async Task The_scan_sees_a_term_when_one_is_logged_at_Verbose()
    {
        await using var factory = VerboseFactory();
        StubKb(factory);
        using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Agent);
        (await client.GetStringAsync("/kb", Ct)).ShouldNotBeNull();

        factory.Services.GetRequiredService<ILoggerFactory>().CreateLogger("canary").LogTrace("typed {Text}", ArticleText);

        AssertVerboseWasCaptured(factory);
        Should.Throw<Shouldly.ShouldAssertException>(() => AssertNothingLeaked(factory));
    }

    [Fact]
    public async Task A_search_term_appears_in_no_log_event_after_a_mid_session_401_and_the_list_is_never_asked_after_it()
    {
        await using var factory = VerboseFactory();
        StubKb(factory);

        // The agent is let in by /me, then the first data call answers 401. After that the token is evicted, so every later call of the page must stay local, and the search is on the list call.
        factory.Api.OnStatus(HttpMethod.Get, "/api/products", HttpStatusCode.Unauthorized);
        using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Agent);

        var html = await client.GetStringAsync($"/kb?search={Uri.EscapeDataString(SearchTerm)}", Ct);

        factory.Api.Requests.ShouldContain(r => r.Path == "/api/products", "the 401 must actually have been answered, or this test proves nothing");
        html.ShouldNotBeNull();
        AssertVerboseWasCaptured(factory);
        AssertNothingLeaked(factory);
        factory.Api.Requests.Where(r => r.Path == "/api/kb/articles").ShouldBeEmpty("once the session lapsed no request may be sent, and the list call carries the search text");
    }

    [Fact]
    public async Task An_articles_text_a_previews_text_and_a_pictures_name_appear_in_no_log_event_even_when_the_calls_fail()
    {
        await using var factory = VerboseFactory();
        StubKb(factory);
        factory.Api
            .OnJson(HttpMethod.Post, "/api/kb/preview", new KbPreviewResponse("<p>fine</p>"))
            .OnJson(HttpMethod.Post, "/api/kb/images", new KbImageUploadResponse("kb-images/a.png", "https://api.test/kb-images/a.png"), HttpStatusCode.Created);

        using (var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Agent))
        {
            (await client.GetStringAsync($"/kb/{ArticleId}", Ct)).ShouldContain("Reset your password");
        }

        // The same calls the editor makes, through the host's own client pipeline (named clients, auth handler, token cache): a circuit has no HTTP request, so the scope gets the sign-in the circuit would have.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var authentication = (IHostEnvironmentAuthenticationStateProvider)scope.ServiceProvider.GetRequiredService<AuthenticationStateProvider>();
            authentication.SetAuthenticationState(Task.FromResult(new AuthenticationState(AdminTestPrincipal.Agent.ToClaimsPrincipal(AdminTestAuth.Scheme))));
            var kb = scope.ServiceProvider.GetRequiredService<IKbClient>();
            var picture = new KbImageFile(PictureName, "image/png", () => new MemoryStream([0x89, 0x50, 0x4E, 0x47]));

            (await kb.PreviewAsync(new KbPreviewRequest(ArticleText), CancellationToken.None)).IsSuccess.ShouldBeTrue();
            (await kb.UploadImageAsync(picture, CancellationToken.None)).IsSuccess.ShouldBeTrue();

            factory.Api.OnStatus(HttpMethod.Post, "/api/kb/preview", HttpStatusCode.InternalServerError);
            factory.Api.OnStatus(HttpMethod.Post, "/api/kb/images", HttpStatusCode.InternalServerError);
            (await kb.PreviewAsync(new KbPreviewRequest(ArticleText), CancellationToken.None)).IsFailure.ShouldBeTrue();
            (await kb.UploadImageAsync(picture, CancellationToken.None)).IsFailure.ShouldBeTrue();
        }

        factory.Api.Requests.ShouldContain(r => r.Path == "/api/kb/preview" && r.Body!.Contains(ArticleText, StringComparison.Ordinal), "the article text really traveled, or this test proves nothing");
        factory.Api.Requests.ShouldContain(r => r.Path == "/api/kb/images" && r.Body!.Contains(PictureName, StringComparison.Ordinal), "the picture's name really traveled, or this test proves nothing");
        factory.Api.AssertEveryCallBore(AdminTestPrincipal.Agent);
        AssertVerboseWasCaptured(factory);
        AssertNothingLeaked(factory);
    }
}
