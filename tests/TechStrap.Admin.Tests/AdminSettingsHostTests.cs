using System.Net;
using AngleSharp.Html.Parser;
using Microsoft.AspNetCore.Mvc.Testing;
using TechStrap.Contracts.AdminEvents;
using TechStrap.Contracts.Agents;
using TechStrap.Contracts.ApiKeys;
using TechStrap.Contracts.DeadLetters;
using TechStrap.Contracts.Paging;
using TechStrap.Contracts.Products;
using TechStrap.Contracts.Tags;
using TechStrap.Tests.Shared.AdminHost;

namespace TechStrap.Admin.Tests;

/// <summary>
/// The whole Admin host with the fake sign-in and the stub API, for the settings and operations pages (PHASE-07b). Review Focus 1: a plain agent never reaches an admin page or action, whether by address or by
/// the navigation, and the guard neither denies an admin nor shows the page to an agent while the session is loading. Pages prerender, so the HTML already holds the data the stub served.
/// </summary>
public sealed class AdminSettingsHostTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static readonly Guid OrbitlyId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid KeyId = Guid.Parse("eeeeeeee-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static readonly string[] AdminLinks =
        ["/settings/products", "/settings/agents", "/settings/tags", "/settings/audit", "/ops/dead-letters"];

    // Every admin route with a sentence its page shows to an admin (taken from the stub data below).
    private static readonly (string Path, string Expected)[] Pages =
    [
        ("/settings/products", "Orbitly"),
        ("/settings/products/new", "New product"),
        ($"/settings/products/{OrbitlyId}", "Orbitly Cloud"),
        ($"/settings/products/{OrbitlyId}/keys", "tsk_ab12"),
        ("/settings/agents", "Roles come from your identity provider"),
        ("/settings/tags", "urgent"),
        ("/settings/audit", "Deleted tag bug, removed from 2 tickets"),
        ("/ops/dead-letters", "a***@example.com"),
    ];

    public static TheoryData<string, string> AdminPages
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var (path, expected) in Pages)
            {
                data.Add(path, expected);
            }

            return data;
        }
    }

    public static TheoryData<string> AdminPaths
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var (path, _) in Pages)
            {
                data.Add(path);
            }

            return data;
        }
    }

    private static HttpClient NoRedirectClient(AdminFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    /// <summary>The factory with a happy-path API behind it: one product with a key, two agents, two tags, one audit event and one failed email.</summary>
    private static AdminFactory FactoryWithSettingsData()
    {
        var factory = new AdminFactory();
        var product = new ProductDto(OrbitlyId, "orbitly", "Orbitly", "ORB", true, new ProductBrandingDto("Orbitly Cloud", null, "#1D4ED8", "#FFFFFF", "#1D4ED8", null, null), 7);
        var key = new ProductApiKeyDto(KeyId, OrbitlyId, ApiKeyKinds.Trusted, "tsk_ab12", "Billing server", Now.AddDays(-2), null, null);
        factory.Api
            .OnJson(HttpMethod.Get, "/api/products", (IReadOnlyList<ProductDto>)[product])
            .OnJson(HttpMethod.Get, $"/api/products/{OrbitlyId}", product)
            .OnJson(HttpMethod.Get, $"/api/products/{OrbitlyId}/api-keys", (IReadOnlyList<ProductApiKeyDto>)[key])
            .OnJson(HttpMethod.Get, "/api/agents", new PagedResponse<AgentListItemDto>(
                [new AgentListItemDto(Guid.NewGuid(), "Sam Ortiz", "Sam Ortiz", "sam@example.com", AgentRoles.Agent, true, Now.AddHours(-1)),
                 new AgentListItemDto(Guid.NewGuid(), "Ada Admin", "Ada Admin", "ada@example.com", AgentRoles.Admin, true, Now)], 1, 25, 2))
            .OnJson(HttpMethod.Get, "/api/agents/me/notification-preferences", (IReadOnlyList<NotificationPreferenceDto>)[new NotificationPreferenceDto(OrbitlyId, "Orbitly", true)])
            .OnJson(HttpMethod.Get, "/api/tags/summary", (IReadOnlyList<TagSummaryDto>)[new TagSummaryDto(Guid.NewGuid(), "urgent", "urgent", "#DC2626", 12)])
            .OnJson(HttpMethod.Get, "/api/admin-events", new PagedResponse<AdminEventDto>(
                [new AdminEventDto(Guid.NewGuid(), AdminEventTypes.TagDeleted, Guid.NewGuid(), "Ada Admin", AdminSubjectTypes.Tag, Guid.NewGuid(), "{\"slug\":\"bug\",\"detachedTicketCount\":2}", Now.AddMinutes(-5))], 1, 25, 1))
            .OnJson(HttpMethod.Get, "/api/dead-letters", new PagedResponse<DeadLetterDto>(
                [new DeadLetterDto(Guid.NewGuid(), "agent-reply", "a***@example.com", null, OrbitlyId, 5, "smtp-transient", Now.AddHours(-3))], 1, 25, 1));
        return factory;
    }

    // ---- anonymous and refused users ------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(AdminPaths))]
    public async Task An_anonymous_visitor_is_sent_to_the_sign_in_landing_and_the_api_is_never_asked(string path)
    {
        await using var factory = FactoryWithSettingsData();
        using var client = NoRedirectClient(factory);

        using var response = await client.GetAsync(path, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().ShouldContain("/signin");
        factory.Api.Requests.ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(AdminPaths))]
    public async Task A_user_the_api_refuses_gets_the_whole_app_no_access_page_and_only_the_me_call_is_made(string path)
    {
        await using var factory = FactoryWithSettingsData();
        using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Outsider);

        var html = await client.GetStringAsync(path, Ct);

        (await new HtmlParser().ParseDocumentAsync(html, Ct)).QuerySelector("section.ts-no-access h1")!.TextContent.ShouldBe("You don't have access to TechStrap.");
        factory.Api.Requests.ShouldAllBe(r => r.Path == "/api/agents/me");
        factory.Api.AssertEveryCallBore(AdminTestPrincipal.Outsider);
    }

    // ---- Review Focus 1: a plain agent -----------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(AdminPaths))]
    public async Task A_plain_agent_who_opens_an_admin_page_by_address_gets_the_page_level_no_access_and_no_admin_call_is_made(string path)
    {
        await using var factory = FactoryWithSettingsData();
        using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Agent);

        using var response = await client.GetAsync(path, Ct);
        var html = await response.Content.ReadAsStringAsync(Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await new HtmlParser().ParseDocumentAsync(html, Ct)).QuerySelector("section.ts-no-access h1")!.TextContent.ShouldBe("You don't have access to this page.");
        html.ShouldNotContain("Orbitly Cloud");
        html.ShouldNotContain("tsk_ab12");
        html.ShouldNotContain("urgent");
        html.ShouldNotContain("a***@example.com");
        html.ShouldNotContain("Deleted tag bug");
        html.ShouldNotContain("New product");
        factory.Api.Requests.ShouldAllBe(r => r.Path == "/api/agents/me", "a plain agent's page load must not call an endpoint the API would refuse");
        factory.Api.AssertEveryCallBore(AdminTestPrincipal.Agent);
    }

    [Fact]
    public async Task A_plain_agents_navigation_has_no_admin_links_but_has_my_settings()
    {
        await using var factory = FactoryWithSettingsData();
        using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Agent);

        var html = await client.GetStringAsync("/", Ct);

        foreach (var link in AdminLinks)
        {
            html.ShouldNotContain($"href=\"{link}\"");
        }

        html.ShouldContain("href=\"/account/notifications\"");
        factory.Api.AssertEveryCallBore(AdminTestPrincipal.Agent);
    }

    [Fact]
    public async Task A_plain_agent_can_open_my_settings_and_it_asks_only_for_what_an_agent_may_read()
    {
        await using var factory = FactoryWithSettingsData();
        using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Agent);

        var html = await client.GetStringAsync("/account/notifications", Ct);

        html.ShouldContain("My settings");
        html.ShouldContain("New tickets in Orbitly");
        html.ShouldContain("Customers see: ");
        html.ShouldNotContain("You don't have access");
        factory.Api.Requests.Select(r => r.Path).Distinct().Order().ShouldBe(["/api/agents/me", "/api/agents/me/notification-preferences", "/api/products"]);
        factory.Api.AssertEveryCallBore(AdminTestPrincipal.Agent);
    }

    // ---- an admin ---------------------------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(AdminPages))]
    public async Task An_admin_sees_each_admin_page_with_its_data_and_the_api_is_asked_who_they_are_first(string path, string expected)
    {
        await using var factory = FactoryWithSettingsData();
        using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Admin);

        var html = await client.GetStringAsync(path, Ct);

        html.ShouldContain(expected);
        html.ShouldNotContain("You don't have access");
        factory.Api.Requests[0].Path.ShouldBe("/api/agents/me");
        factory.Api.AssertEveryCallBore(AdminTestPrincipal.Admin);
    }

    [Fact]
    public async Task An_admins_navigation_has_every_admin_link_and_my_settings()
    {
        await using var factory = FactoryWithSettingsData();
        using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Admin);

        var html = await client.GetStringAsync("/settings/tags", Ct);

        foreach (var link in AdminLinks)
        {
            html.ShouldContain($"href=\"{link}\"");
        }

        html.ShouldContain("href=\"/account/notifications\"");
        factory.Api.AssertEveryCallBore(AdminTestPrincipal.Admin);
    }

    [Fact]
    public async Task The_admin_pages_never_render_the_raw_audit_payload_or_a_full_recipient_address()
    {
        await using var factory = FactoryWithSettingsData();
        using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Admin);

        var audit = await client.GetStringAsync("/settings/audit", Ct);
        var letters = await client.GetStringAsync("/ops/dead-letters", Ct);

        audit.ShouldNotContain("detachedTicketCount");
        audit.ShouldNotContain("&quot;slug&quot;");
        letters.ShouldContain("a***@example.com");
        letters.ShouldNotContain("ada@example.com");
        factory.Api.AssertEveryCallBore(AdminTestPrincipal.Admin);
    }
}
