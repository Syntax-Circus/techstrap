using System.Net;
using AngleSharp.Html.Parser;
using Microsoft.AspNetCore.Mvc.Testing;
using TechStrap.Contracts.Agents;
using TechStrap.Contracts.Paging;
using TechStrap.Contracts.Products;
using TechStrap.Contracts.Tags;
using TechStrap.Contracts.Tickets;
using TechStrap.Tests.Shared.AdminHost;

namespace TechStrap.Admin.Tests;

/// <summary>
/// The whole Admin host with the fake sign-in and the stub API: what an agent, an admin, a refused user and an anonymous visitor are shown, in what order the API is
/// asked, and what the attachment pass-through returns. Pages are prerendered, so the HTML already contains the data the stub served.
/// </summary>
public sealed class AdminHostSmokeTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static readonly Guid OrbitlyId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid TicketId = Guid.Parse("dddddddd-0000-0000-0000-000000000042");
    private static readonly Guid RequesterId = Guid.Parse("cccccccc-0000-0000-0000-000000000001");
    private static readonly Guid AttachmentId = Guid.Parse("eeeeeeee-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static HttpClient NoRedirectClient(AdminFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    /// <summary>The factory with a happy-path API behind it: one open ticket, one product, one agent, no tags.</summary>
    private static AdminFactory FactoryWithOneTicket()
    {
        var factory = new AdminFactory();
        var summary = new TicketSummaryDto(
            TicketId, "ORB-42", "Cannot log in", "Open", "Normal", OrbitlyId, "Orbitly", RequesterId, "ada@example.com", "Ada Lovelace",
            null, null, false, [], Now.AddDays(-1), Now.AddMinutes(-5));
        var detail = new TicketDetailDto(
            TicketId, "ORB-42", "Cannot log in", "Open", "Normal", OrbitlyId, "Orbitly", new TicketRequesterDto(RequesterId, "ada@example.com", "Ada Lovelace", null),
            null, null, false, [], "Email", null, null, false, Now.AddDays(-1), null, null, null, Now.AddMinutes(-5), 7,
            [new MessageDto(Guid.NewGuid(), "Requester", null, "Ada Lovelace", "Public", "<p>I cannot log in</p>", Now.AddDays(-1), [], [])],
            [new TicketEventDto(Guid.NewGuid(), "Created", "Requester", null, "Ada Lovelace", "{\"channel\":\"Email\"}", Now.AddDays(-1))]);
        var product = new ProductDto(OrbitlyId, "orbitly", "Orbitly", "ORB", true, new ProductBrandingDto("Orbitly", null, "#1D4ED8", "#FFFFFF", "#1D4ED8", null, null), 1);
        factory.Api
            .OnJson(HttpMethod.Get, "/api/tickets", new PagedResponse<TicketSummaryDto>([summary], 1, 25, 1))
            .OnJson(HttpMethod.Get, "/api/tickets/counts", new TicketViewCountsResponse(1, 0, 1, 0, 1, 0))
            .OnJson(HttpMethod.Get, "/api/tickets/ORB-42", detail)
            .OnJson(HttpMethod.Get, "/api/products", (IReadOnlyList<ProductDto>)[product])
            .OnJson(HttpMethod.Get, "/api/tags", (IReadOnlyList<TagDto>)[])
            .OnJson(HttpMethod.Get, "/api/agents", new PagedResponse<AgentListItemDto>([new AgentListItemDto(Guid.NewGuid(), "Sam Agent", "Sam Agent", null, null, null, null)], 1, 100, 1));
        return factory;
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/queue/mine")]
    [InlineData("/tickets/ORB-42")]
    public async Task An_anonymous_visitor_is_sent_to_the_sign_in_landing_and_the_api_is_never_asked(string path)
    {
        await using var factory = FactoryWithOneTicket();
        using var client = NoRedirectClient(factory);

        using var response = await client.GetAsync(path, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().ShouldContain("/signin");
        factory.Api.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_anonymous_download_is_refused_and_the_api_is_never_asked()
    {
        await using var factory = FactoryWithOneTicket();
        using var client = NoRedirectClient(factory);

        using var response = await client.GetAsync($"/attachments/{AttachmentId}", Ct);

        ((int)response.StatusCode).ShouldBeOneOf(302, 401);
        factory.Api.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_agent_sees_the_queue_and_the_api_was_asked_who_they_are_before_any_ticket_call()
    {
        await using var factory = FactoryWithOneTicket();
        using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Agent);

        var html = await client.GetStringAsync("/", Ct);

        html.ShouldContain("class=\"ts-queue\"");
        html.ShouldContain("href=\"/tickets/ORB-42\"");
        html.ShouldContain("Cannot log in");
        html.ShouldContain("href=\"/queue/spam\"");
        html.ShouldNotContain("You don't have access");
        var paths = factory.Api.Requests.Select(r => r.Path).ToList();
        paths[0].ShouldBe("/api/agents/me");
        paths.IndexOf("/api/tickets").ShouldBeGreaterThan(0);
        factory.Api.AssertEveryCallBore(AdminTestPrincipal.Agent);
        factory.Api.Requests.First(r => r.Path == "/api/tickets").Query.ShouldContain("view=Unassigned");
    }

    [Fact]
    public async Task An_agent_opens_a_ticket_with_the_composer_and_the_controls_and_no_delete_or_erase()
    {
        await using var factory = FactoryWithOneTicket();
        using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Agent);

        var html = await client.GetStringAsync("/tickets/ORB-42", Ct);

        var page = await new HtmlParser().ParseDocumentAsync(html, Ct);
        page.QuerySelector("h1.ts-ticket-subject")!.TextContent.ShouldBe("Cannot log in");
        page.QuerySelector(".ts-message-body")!.TextContent.ShouldBe("I cannot log in");
        page.QuerySelector("section.ts-composer").ShouldNotBeNull();
        page.QuerySelector("section.ts-sidebar").ShouldNotBeNull();
        html.ShouldContain("Mark as spam");
        html.ShouldNotContain("Delete ticket");
        html.ShouldNotContain("Erase requester");
        factory.Api.Requests[0].Path.ShouldBe("/api/agents/me");
        factory.Api.AssertEveryCallBore(AdminTestPrincipal.Agent);
    }

    [Fact]
    public async Task An_admin_also_gets_delete_and_erase()
    {
        await using var factory = FactoryWithOneTicket();
        using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Admin);

        var html = await client.GetStringAsync("/tickets/ORB-42", Ct);

        html.ShouldContain("Delete ticket (permanent)");
        html.ShouldContain("Erase requester (permanent)");
        html.ShouldContain("Mark as spam");
        factory.Api.AssertEveryCallBore(AdminTestPrincipal.Admin);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/queue/spam")]
    [InlineData("/tickets/ORB-42")]
    public async Task A_user_the_api_refuses_gets_the_no_access_page_and_no_ticket_data_is_requested(string path)
    {
        await using var factory = FactoryWithOneTicket();
        using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Outsider);

        using var response = await client.GetAsync(path, Ct);
        var html = await response.Content.ReadAsStringAsync(Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await new HtmlParser().ParseDocumentAsync(html, Ct)).QuerySelector("section.ts-no-access h1")!.TextContent.ShouldBe("You don't have access to TechStrap.");
        html.ShouldNotContain("Cannot log in");
        html.ShouldNotContain("class=\"ts-queue\"");
        factory.Api.Requests.ShouldAllBe(r => r.Path == "/api/agents/me");
        factory.Api.AssertEveryCallBore(AdminTestPrincipal.Outsider);
    }

    [Fact]
    public async Task A_deactivated_agent_is_refused_on_the_ticket_page_and_nothing_else_is_asked()
    {
        await using var factory = FactoryWithOneTicket();
        factory.Api.OnProblem(HttpMethod.Get, "/api/agents/me", HttpStatusCode.Forbidden, "agent-inactive", "Deactivated.");
        using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Admin);

        var html = await client.GetStringAsync("/tickets/ORB-42", Ct);

        html.ShouldContain("has been deactivated");
        html.ShouldNotContain("Delete ticket");
        factory.Api.Requests.ShouldAllBe(r => r.Path == "/api/agents/me");
        factory.Api.AssertEveryCallBore(AdminTestPrincipal.Admin);
    }

    [Fact]
    public async Task The_attachment_pass_through_streams_a_forced_download_and_leaks_no_credentials()
    {
        await using var factory = FactoryWithOneTicket();
        byte[] bytes = [1, 2, 3, 4, 5];
        factory.Api.On(HttpMethod.Get, $"/api/attachments/{AttachmentId}", _ =>
        {
            var upstream = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
            upstream.Content.Headers.ContentType = new("image/png");
            upstream.Content.Headers.ContentDisposition = new("inline") { FileName = "shot.png" };
            return upstream;
        });
        using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Agent);

        using var response = await client.GetAsync($"/attachments/{AttachmentId}", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsByteArrayAsync(Ct)).ShouldBe(bytes);
        response.Content.Headers.ContentDisposition!.DispositionType.ShouldBe("attachment");
        response.Headers.GetValues("X-Content-Type-Options").ShouldContain("nosniff");
        var token = AdminTestPrincipal.Agent.AccessToken;
        response.Headers.Contains("Authorization").ShouldBeFalse();
        response.Headers.Contains("Set-Cookie").ShouldBeFalse();
        response.Headers.SelectMany(h => h.Value).Concat(response.Content.Headers.SelectMany(h => h.Value)).ShouldAllBe(v => !v.Contains(token));
        factory.Api.AssertEveryCallBore(AdminTestPrincipal.Agent);
    }

    [Fact]
    public async Task An_attachment_the_api_does_not_know_is_not_found()
    {
        await using var factory = FactoryWithOneTicket();
        factory.Api.OnProblem(HttpMethod.Get, $"/api/attachments/{AttachmentId}", HttpStatusCode.NotFound, "attachment-not-found", "No such attachment.");
        using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Agent);

        using var response = await client.GetAsync($"/attachments/{AttachmentId}", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        factory.Api.AssertEveryCallBore(AdminTestPrincipal.Agent);
    }
}
