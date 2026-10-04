using System.Net;
using TechStrap.Tests.Shared.AdminHost;

namespace TechStrap.Admin.Tests;

/// <summary>Review Focus 1 at the host: the API's answer to /api/agents/me decides what the signed-in user is shown.</summary>
public sealed class AgentAccessHostTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_agent_sees_the_page_and_the_api_was_asked_who_they_are_first()
    {
        await using var factory = new AdminFactory();
        using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Agent);

        var html = await client.GetStringAsync("/", Ct);

        html.ShouldContain("shell-placeholder");
        html.ShouldNotContain("You don't have access");
        factory.Api.Requests.First().Path.ShouldBe("/api/agents/me");
        factory.Api.Requests.First().Authorization.ShouldBe("Bearer " + AdminTestPrincipal.Agent.AccessToken);
    }

    [Fact]
    public async Task A_user_the_api_refuses_sees_no_access_and_never_the_page()
    {
        await using var factory = new AdminFactory();
        using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Outsider);

        using var response = await client.GetAsync("/", Ct);
        var html = await response.Content.ReadAsStringAsync(Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await new AngleSharp.Html.Parser.HtmlParser().ParseDocumentAsync(html, Ct)).QuerySelector("section.ts-no-access h1")!.TextContent.ShouldBe("You don't have access to TechStrap.");
        html.ShouldContain("not in the techstrap-agents group");
        html.ShouldNotContain("shell-placeholder");
        factory.Api.Requests.ShouldAllBe(r => r.Path == "/api/agents/me");
        factory.Api.Requests.ShouldAllBe(r => r.Authorization == "Bearer " + AdminTestPrincipal.Outsider.AccessToken);
    }

    [Fact]
    public async Task A_deactivated_agent_is_refused_the_same_way()
    {
        await using var factory = new AdminFactory();
        factory.Api.OnProblem(HttpMethod.Get, "/api/agents/me", HttpStatusCode.Forbidden, "agent-inactive", "Deactivated.");
        using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Admin);

        var html = await client.GetStringAsync("/", Ct);

        html.ShouldContain("has been deactivated");
        html.ShouldNotContain("shell-placeholder");
        factory.Api.Requests.ShouldAllBe(r => r.Authorization == "Bearer " + AdminTestPrincipal.Admin.AccessToken);
    }

    // Carried ruling: the gate prints the session's error message raw, so a transport failure must show fixed copy, never exception text, a host or a port.
    [Fact]
    public async Task An_unreachable_api_shows_fixed_copy_and_never_the_exception_text_a_host_or_a_port()
    {
        await using var factory = new AdminFactory();
        factory.Api.On(HttpMethod.Get, "/api/agents/me", _ => throw new HttpRequestException("No connection could be made because the target machine actively refused it (10.1.2.3:5432)"));
        using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Agent);

        var html = await client.GetStringAsync("/", Ct);

        html.ShouldContain("TechStrap could not check your access.");
        html.ShouldContain("TechStrap could not reach the API. Try again in a moment.");
        html.ShouldNotContain("10.1.2.3");
        html.ShouldNotContain("5432");
        html.ShouldNotContain("actively refused");
        html.ShouldNotContain("shell-placeholder");
        factory.Api.Requests.ShouldAllBe(r => r.Authorization == "Bearer " + AdminTestPrincipal.Agent.AccessToken);
    }

    // Carried ruling: /not-found and /error render static, where the gate's Retry button could do nothing. They render their content directly for a
    // signed-in user, so the page never offers a dead button, and the API is not asked anything.
    [Fact]
    public async Task A_static_page_for_a_signed_in_user_renders_its_content_and_never_a_dead_retry_button_even_when_the_api_is_down()
    {
        await using var factory = new AdminFactory();
        factory.Api.OnStatus(HttpMethod.Get, "/api/agents/me", HttpStatusCode.ServiceUnavailable);
        using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Agent);

        // The address itself, and an unknown address (re-executed to it with a 404).
        foreach (var (path, status) in new[] { ("/not-found", HttpStatusCode.OK), ("/no-such-page", HttpStatusCode.NotFound) })
        {
            using var response = await client.GetAsync(path, Ct);
            var html = await response.Content.ReadAsStringAsync(Ct);

            response.StatusCode.ShouldBe(status);
            AssertStaticContent(html);
        }

        factory.Api.Requests.ShouldBeEmpty();
    }

    private static void AssertStaticContent(string html)
    {
        html.ShouldContain("This page fell out of its strap.");
        html.ShouldNotContain("ts-gate");
        html.ShouldNotContain("could not check your access");
        html.ShouldNotContain("Checking your access");
    }
}
