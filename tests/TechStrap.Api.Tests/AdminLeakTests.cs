using Serilog.Events;
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
}
