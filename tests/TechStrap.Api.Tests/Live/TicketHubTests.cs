using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Events;
using TechStrap.Api.Tests.Auth;
using TechStrap.Api.Tests.Tickets;
using TechStrap.Application.Live;
using TechStrap.Contracts.Live;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Api.Tests.Live;

/// <summary>
/// The agent hub through a real <see cref="HubConnection"/> on the in-memory server and a migrated database: who may connect (Review Focus 1), what a connection
/// receives, presence between two agents, and that a REST write is announced once, after its commit (Review Focus 2).
/// </summary>
public sealed class TicketHubTests(TestPostgres postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<(ApiFactory Factory, ApiTestDatabase Database, TicketSeed Seed)> StartAsync(Dictionary<string, string?>? extra = null)
    {
        var database = await ApiTestDatabase.CreateAsync(postgres);
        var settings = new Dictionary<string, string?>(database.Settings) { ["TECHSTRAP_PORTAL_PUBLIC_URL"] = "https://help.test" };
        foreach (var (key, value) in extra ?? [])
        {
            settings[key] = value;
        }

        var factory = new ApiFactory(settings: settings);
        return (factory, database, await TicketTestData.SeedAsync(factory, Ct));
    }

    private static TicketChange Change(Guid? ticketId = null, string number = "ORB-1") =>
        new(Guid.NewGuid(), ticketId ?? Guid.NewGuid(), number, Guid.NewGuid(), TicketEventTypes.StatusChanged, null, DateTimeOffset.UtcNow, TicketChangeKinds.Updated);

    private static async Task<HttpRequestException> StartFailureAsync(HubConnection connection)
    {
        await using (connection)
        {
            return await Should.ThrowAsync<HttpRequestException>(() => connection.StartAsync(Ct));
        }
    }

    // ---- who may connect ---------------------------------------------------------------------------------------------------------------------------------

    [Fact(Timeout = 120000)]
    public async Task A_connection_without_a_token_is_401()
    {
        _ = TestContext.Current.CancellationToken;
        var (factory, _, _) = await StartAsync();
        await using var _f = factory;

        var failure = await StartFailureAsync(HubTestSupport.Connect(factory, headerToken: null));

        failure.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact(Timeout = 120000)]
    public async Task A_valid_token_for_someone_who_is_not_an_agent_is_403()
    {
        _ = TestContext.Current.CancellationToken;
        var (factory, _, _) = await StartAsync();
        await using var _f = factory;
        var outsider = TestJwt.Token("outsider", ["some-other-group"], email: "outsider@example.com", name: "Outsider");

        var failure = await StartFailureAsync(HubTestSupport.Connect(factory, outsider));

        failure.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact(Timeout = 120000)]
    public async Task A_deactivated_agent_is_refused_at_the_handshake()
    {
        _ = TestContext.Current.CancellationToken;
        var (factory, database, _) = await StartAsync();
        await using var _f = factory;
        await database.ExecuteAsync("UPDATE agents SET is_active = false WHERE oidc_subject = 'kim'");

        var failure = await StartFailureAsync(HubTestSupport.Connect(factory, HubTestSupport.AgentToken("kim", "Kim")));

        failure.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact(Timeout = 120000)]
    public async Task A_token_in_the_query_string_is_refused_even_when_it_is_a_valid_agent_token()
    {
        _ = TestContext.Current.CancellationToken;
        var (factory, _, _) = await StartAsync();
        await using var _f = factory;
        var token = HubTestSupport.AgentToken("sam", "Sam");

        var failure = await StartFailureAsync(HubTestSupport.Connect(factory, headerToken: null, queryToken: token));

        failure.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact(Timeout = 120000)]
    public async Task A_raw_negotiate_with_the_token_only_in_the_query_is_401_and_with_the_header_is_200()
    {
        var ct = TestContext.Current.CancellationToken;
        var (factory, _, _) = await StartAsync();
        await using var _f = factory;
        var token = HubTestSupport.AgentToken("sam", "Sam");
        using var client = factory.CreateClient();

        using var viaQuery = await client.PostAsync($"{TicketHubRoutes.Path}/negotiate?negotiateVersion=1&access_token={Uri.EscapeDataString(token)}", null, ct);
        using var viaHeader = await client.Bearer(token).PostAsync($"{TicketHubRoutes.Path}/negotiate?negotiateVersion=1", null, ct);

        viaQuery.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        viaHeader.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact(Timeout = 120000)]
    public async Task A_token_that_is_about_to_expire_closes_the_connection_when_it_does()
    {
        var (factory, _, _) = await StartAsync();
        await using var _f = factory;
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var connection = HubTestSupport.Connect(factory, HubTestSupport.AgentToken("sam", "Sam", expires: DateTime.UtcNow.AddSeconds(4)));
        connection.Closed += _ =>
        {
            closed.TrySetResult();
            return Task.CompletedTask;
        };
        var testToken = TestContext.Current.CancellationToken;
        await connection.StartAsync(testToken);
        connection.State.ShouldBe(HubConnectionState.Connected);

        // The server closes the connection at the token's expiry (CloseOnAuthenticationExpiration); the wait is only a ceiling, not a measurement.
        await closed.Task.WaitAsync(HubTestSupport.Patience, testToken);

        connection.State.ShouldBe(HubConnectionState.Disconnected);
    }

    // ---- what a connection receives ---------------------------------------------------------------------------------------------------------------------

    [Fact(Timeout = 120000)]
    public async Task Every_agent_connection_is_in_the_queue_and_receives_ticket_changes_with_ids_only()
    {
        var ct = TestContext.Current.CancellationToken;
        var (factory, _, _) = await StartAsync();
        await using var _f = factory;
        await using var sam = HubTestSupport.Connect(factory, HubTestSupport.AgentToken("sam", "Sam"));
        await using var kim = HubTestSupport.Connect(factory, HubTestSupport.AgentToken("kim", "Kim"));
        using var samInbox = new HubTestSupport.Inbox<TicketChangedDto>(sam, TicketHubMethods.TicketChanged);
        using var kimInbox = new HubTestSupport.Inbox<TicketChangedDto>(kim, TicketHubMethods.TicketChanged);
        await sam.StartAsync(ct);
        await kim.StartAsync(ct);
        await sam.ReadyAsync(ct);
        await kim.ReadyAsync(ct);
        var change = Change(number: "ORB-7");

        await factory.Services.GetRequiredService<ITicketChangeBroadcaster>().PublishAsync(change, ct);

        (await samInbox.NextAsync()).ShouldBe(change.ToDto());
        (await kimInbox.NextAsync()).ShouldBe(change.ToDto());
    }

    [Fact(Timeout = 120000)]
    public async Task The_hubs_broadcaster_is_the_one_the_rest_of_the_host_resolves()
    {
        _ = TestContext.Current.CancellationToken;
        var (factory, _, _) = await StartAsync();
        await using var _f = factory;

        factory.Services.GetRequiredService<ITicketChangeBroadcaster>().GetType().Name.ShouldBe("SignalRTicketChangeBroadcaster");
    }

    // ---- presence ---------------------------------------------------------------------------------------------------------------------------------------

    [Fact(Timeout = 120000)]
    public async Task Two_agents_on_one_ticket_see_each_other_come_replying_and_go()
    {
        var ct = TestContext.Current.CancellationToken;
        var (factory, _, seed) = await StartAsync();
        await using var _f = factory;
        var ticketId = seed.Tickets[0].Id;
        await using var sam = HubTestSupport.Connect(factory, HubTestSupport.AgentToken("sam", "Sam"));
        await using var kim = HubTestSupport.Connect(factory, HubTestSupport.AgentToken("kim", "Kim"));
        using var samSees = new HubTestSupport.Inbox<TicketPresenceDto>(sam, TicketHubMethods.PresenceChanged);
        await sam.StartAsync(ct);
        await kim.StartAsync(ct);

        var first = await sam.InvokeAsync<TicketPresenceDto>(TicketHubMethods.JoinTicket, ticketId, ct);
        first.TicketId.ShouldBe(ticketId);
        first.Viewers.Select(viewer => (viewer.DisplayName, viewer.State)).ShouldBe([("Sam", TicketPresenceStates.Viewing)]);

        var second = await kim.InvokeAsync<TicketPresenceDto>(TicketHubMethods.JoinTicket, ticketId, ct);
        second.Viewers.Select(viewer => viewer.DisplayName).ShouldBe(["Kim", "Sam"]);
        (await samSees.NextAsync()).Viewers.Select(viewer => viewer.DisplayName).ShouldBe(["Kim", "Sam"]);

        await kim.InvokeAsync(TicketHubMethods.SetComposing, ticketId, true, ct);
        var composing = await samSees.NextAsync();
        composing.Viewers.Single(viewer => viewer.DisplayName == "Kim").State.ShouldBe(TicketPresenceStates.Composing);

        await kim.StopAsync(ct);
        var gone = await samSees.NextAsync();
        gone.Viewers.Select(viewer => viewer.DisplayName).ShouldBe(["Sam"]);
    }

    [Fact(Timeout = 120000)]
    public async Task Leaving_a_ticket_is_announced_and_the_leaver_stops_receiving_its_presence()
    {
        var ct = TestContext.Current.CancellationToken;
        var (factory, _, seed) = await StartAsync();
        await using var _f = factory;
        var ticketId = seed.Tickets[0].Id;
        await using var sam = HubTestSupport.Connect(factory, HubTestSupport.AgentToken("sam", "Sam"));
        await using var kim = HubTestSupport.Connect(factory, HubTestSupport.AgentToken("kim", "Kim"));
        using var samSees = new HubTestSupport.Inbox<TicketPresenceDto>(sam, TicketHubMethods.PresenceChanged);
        using var kimSees = new HubTestSupport.Inbox<TicketPresenceDto>(kim, TicketHubMethods.PresenceChanged);
        using var kimQueue = new HubTestSupport.Inbox<TicketChangedDto>(kim, TicketHubMethods.TicketChanged);
        await sam.StartAsync(ct);
        await kim.StartAsync(ct);
        await kim.ReadyAsync(ct);
        await sam.InvokeAsync<TicketPresenceDto>(TicketHubMethods.JoinTicket, ticketId, ct);
        await kim.InvokeAsync<TicketPresenceDto>(TicketHubMethods.JoinTicket, ticketId, ct);
        (await samSees.NextAsync()).Viewers.Count.ShouldBe(2);

        await kim.InvokeAsync(TicketHubMethods.LeaveTicket, ticketId, ct);
        (await samSees.NextAsync()).Viewers.Select(viewer => viewer.DisplayName).ShouldBe(["Sam"]);

        // Kim is out of the ticket's group now: a later presence push for the ticket reaches Sam only.
        // The client dispatches handlers apart from invocation completions, so Kim's own leave announcement may still be in flight:
        // a queue message sent after the push is the barrier (a connection gets its messages in the order they were sent).
        var broadcaster = factory.Services.GetRequiredService<ITicketChangeBroadcaster>();
        await broadcaster.PublishPresenceAsync(new TicketPresence(ticketId, []), ct);
        (await samSees.NextAsync()).Viewers.ShouldBeEmpty();
        var barrier = Change();
        await broadcaster.PublishAsync(barrier, ct);
        (await kimQueue.NextAsync()).EventId.ShouldBe(barrier.EventId);
        kimSees.Pending().ShouldAllBe(seen => seen.Viewers.Count > 0);
    }

    [Fact(Timeout = 120000)]
    public async Task Joining_an_unknown_ticket_is_a_hub_exception_with_the_fixed_message_and_joins_no_group()
    {
        var ct = TestContext.Current.CancellationToken;
        var (factory, _, _) = await StartAsync();
        await using var _f = factory;
        var unknown = Guid.NewGuid();
        await using var sam = HubTestSupport.Connect(factory, HubTestSupport.AgentToken("sam", "Sam"));
        using var presence = new HubTestSupport.Inbox<TicketPresenceDto>(sam, TicketHubMethods.PresenceChanged);
        using var queue = new HubTestSupport.Inbox<TicketChangedDto>(sam, TicketHubMethods.TicketChanged);
        await sam.StartAsync(ct);
        await sam.ReadyAsync(ct);

        var refusal = await Should.ThrowAsync<HubException>(() => sam.InvokeAsync<TicketPresenceDto>(TicketHubMethods.JoinTicket, unknown, ct));

        refusal.Message.ShouldEndWith(TicketHubMessages.TicketNotFound);
        refusal.Message.ShouldNotContain(unknown.ToString());

        // Not in the group: a presence push for that id never arrives. The queue message sent after it is the barrier (a connection gets its messages in the order they were sent).
        var broadcaster = factory.Services.GetRequiredService<ITicketChangeBroadcaster>();
        await broadcaster.PublishPresenceAsync(new TicketPresence(unknown, []), ct);
        var barrier = Change();
        await broadcaster.PublishAsync(barrier, ct);
        (await queue.NextAsync()).EventId.ShouldBe(barrier.EventId);
        presence.Pending().ShouldBeEmpty();
    }

    [Fact(Timeout = 120000)]
    public async Task Composing_on_a_ticket_that_was_never_joined_is_refused()
    {
        var ct = TestContext.Current.CancellationToken;
        var (factory, _, seed) = await StartAsync();
        await using var _f = factory;
        await using var sam = HubTestSupport.Connect(factory, HubTestSupport.AgentToken("sam", "Sam"));
        await sam.StartAsync(ct);

        var refusal = await Should.ThrowAsync<HubException>(() => sam.InvokeAsync(TicketHubMethods.SetComposing, seed.Tickets[0].Id, true, ct));

        refusal.Message.ShouldEndWith("Open the ticket first.");
    }

    [Fact(Timeout = 120000)]
    public async Task The_name_other_agents_see_is_the_internal_name_never_the_public_display_name()
    {
        var ct = TestContext.Current.CancellationToken;
        var (factory, database, seed) = await StartAsync();
        await using var _f = factory;
        await database.ExecuteAsync("UPDATE agents SET public_display_name = 'Sam from Support' WHERE oidc_subject = 'sam'");
        await using var sam = HubTestSupport.Connect(factory, HubTestSupport.AgentToken("sam", "Sam"));
        await sam.StartAsync(ct);

        var presence = await sam.InvokeAsync<TicketPresenceDto>(TicketHubMethods.JoinTicket, seed.Tickets[0].Id, ct);

        presence.Viewers.Single().DisplayName.ShouldBe("Sam");
    }

    // ---- a REST write is announced once, after its commit -----------------------------------------------------------------------------------------------

    [Fact(Timeout = 120000)]
    public async Task A_rest_status_change_reaches_the_queue_exactly_once_and_a_rejected_one_never_does()
    {
        var ct = TestContext.Current.CancellationToken;
        var (factory, _, seed) = await StartAsync();
        await using var _f = factory;
        var ticket = seed.Tickets[1];
        await using var kim = HubTestSupport.Connect(factory, HubTestSupport.AgentToken("kim", "Kim"));
        using var queue = new HubTestSupport.Inbox<TicketChangedDto>(kim, TicketHubMethods.TicketChanged);
        await kim.StartAsync(ct);
        await kim.ReadyAsync(ct);
        using var sam = TicketTestData.AgentClient(factory, "sam");
        var version = await TicketTestData.VersionAsync(sam, ticket.Id);

        using var stale = await sam.PutAsJsonAsync($"/api/tickets/{ticket.Id}/status", new ChangeTicketStatusRequest("Open", version + 100), ct);
        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        using var accepted = await sam.PutAsJsonAsync($"/api/tickets/{ticket.Id}/status", new ChangeTicketStatusRequest("Open", version), ct);
        accepted.StatusCode.ShouldBe(HttpStatusCode.OK);

        var change = await queue.NextAsync();
        change.TicketId.ShouldBe(ticket.Id);
        change.TicketNumber.ShouldBe(ticket.Number.ToString());
        change.ProductId.ShouldBe(ticket.ProductId);
        change.EventType.ShouldBe(TicketEventTypes.StatusChanged);
        change.Kind.ShouldBe(TicketChangeKinds.Updated);
        change.ActorAgentId.ShouldBe(seed.Sam.Id);

        // Anything else the same two requests produced would arrive before this barrier.
        var barrier = Change();
        await factory.Services.GetRequiredService<ITicketChangeBroadcaster>().PublishAsync(barrier, ct);
        (await queue.NextAsync()).EventId.ShouldBe(barrier.EventId);
        queue.Pending().ShouldBeEmpty();
    }

    // ---- logs -------------------------------------------------------------------------------------------------------------------------------------------

    [Fact(Timeout = 120000)]
    public async Task No_token_reaches_a_log_event_at_any_level_whether_it_came_in_a_header_or_a_refused_query()
    {
        var ct = TestContext.Current.CancellationToken;
        var (factory, _, seed) = await StartAsync(new Dictionary<string, string?>
        {
            ["Serilog:MinimumLevel:Default"] = "Verbose",
            ["Serilog:MinimumLevel:Override:Microsoft"] = "Verbose",
            ["Serilog:MinimumLevel:Override:Microsoft.AspNetCore"] = "Verbose",
            ["Serilog:MinimumLevel:Override:System"] = "Verbose",
        });
        await using var _f = factory;
        var headerToken = HubTestSupport.AgentToken("sam", "Sam");
        var queryToken = HubTestSupport.AgentToken("kim", "Kim");
        await using (var good = HubTestSupport.Connect(factory, headerToken))
        {
            await good.StartAsync(ct);
            await good.InvokeAsync<TicketPresenceDto>(TicketHubMethods.JoinTicket, seed.Tickets[0].Id, ct);
        }

        await StartFailureAsync(HubTestSupport.Connect(factory, headerToken: null, queryToken: queryToken));

        factory.LogSink.Events.ShouldContain(e => e.Level <= LogEventLevel.Debug, "the Verbose setting must have taken effect, or this test only scanned Information and above");
        factory.LogSink.Events.ShouldContain(e => e.RenderMessage().Contains("/hubs/tickets"), "the hub requests must have been logged, or this test proves nothing");
        foreach (var logEvent in factory.LogSink.Events)
        {
            var text = string.Join('\n', [logEvent.RenderMessage(), logEvent.Exception?.ToString() ?? string.Empty, .. logEvent.Properties.Values.Select(value => value.ToString())]);
            text.ShouldNotContain(headerToken);
            text.ShouldNotContain(queryToken);
            text.ShouldNotContain("eyJ");
        }
    }
}
