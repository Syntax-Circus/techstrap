using System.Net;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Admin.Auth;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Tests.Clients;
using TechStrap.Contracts.Agents;
using TechStrap.Tests.Shared.AdminHost;

namespace TechStrap.Admin.Tests.Auth;

/// <summary>
/// Review Focus 2, session half: a 401 from any API call after the agent was let in moves <see cref="AgentSession"/> to SessionExpired, through one choke point
/// (<c>ApiConnection</c>), and keeps the agent so the page can stay mounted.
/// </summary>
public sealed class SessionExpiryTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static AgentDto Agent() => new(Guid.NewGuid(), "Sam", "sam@orbitly.test", AgentRoles.Agent, true, null, null);

    /// <summary>The real clients over the stub API, with the agent already let in by the real <c>/me</c> call.</summary>
    private static async Task<(ApiHarness Api, AgentSession Session)> ReadyAsync()
    {
        var api = await ApiHarness.CreateAsync();
        api.Stub.WithTestAgents();
        var session = api.Get<AgentSession>();
        await session.EnsureLoadedAsync(Ct);
        session.State.ShouldBe(AgentSessionState.Ready);
        return (api, session);
    }

    [Fact]
    public async Task A_401_on_a_read_moves_a_ready_session_to_expired_and_keeps_the_agent()
    {
        var (api, session) = await ReadyAsync();
        await using var scope = api;
        api.Stub.OnStatus(HttpMethod.Get, "/api/tickets/counts", HttpStatusCode.Unauthorized);

        var result = await api.Get<ApiConnection>().GetAsync<int>("api/tickets/counts", Ct);

        result.Errors[0].Code.ShouldBe(ApiErrorCodes.Unauthenticated);
        session.State.ShouldBe(AgentSessionState.SessionExpired);
        session.ExpiredWhileWorking.ShouldBeTrue();
        session.Agent.ShouldNotBeNull("the page stays mounted, so the agent stays");
        session.IsAdmitted.ShouldBeTrue();
        session.ErrorCode.ShouldBe(ApiErrorCodes.Unauthenticated);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task A_401_on_a_write_expires_the_session_too(string verb)
    {
        var (api, session) = await ReadyAsync();
        await using var scope = api;
        var method = new HttpMethod(verb);
        api.Stub.OnStatus(method, "/api/thing", HttpStatusCode.Unauthorized);

        var result = await api.Get<ApiConnection>().SendAsync(method, "api/thing", new { note = "x" }, Ct);

        result.IsFailure.ShouldBeTrue();
        session.ExpiredWhileWorking.ShouldBeTrue();
    }

    [Fact]
    public async Task A_401_on_a_write_with_a_body_expires_the_session()
    {
        var (api, session) = await ReadyAsync();
        await using var scope = api;
        api.Stub.OnStatus(HttpMethod.Post, "/api/thing", HttpStatusCode.Unauthorized);

        var result = await api.Get<ApiConnection>().SendAsync<int>(HttpMethod.Post, "api/thing", new { note = "x" }, Ct);

        result.Errors[0].Code.ShouldBe(ApiErrorCodes.Unauthenticated);
        session.ExpiredWhileWorking.ShouldBeTrue();
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task Any_other_failure_leaves_the_session_ready(HttpStatusCode status)
    {
        var (api, session) = await ReadyAsync();
        await using var scope = api;
        api.Stub.OnStatus(HttpMethod.Get, "/api/thing", status);

        await api.Get<ApiConnection>().GetAsync<int>("api/thing", Ct);

        session.State.ShouldBe(AgentSessionState.Ready);
        session.ExpiredWhileWorking.ShouldBeFalse();
    }

    [Fact]
    public async Task Several_401s_raise_one_change_so_the_banner_does_not_flicker()
    {
        var (api, session) = await ReadyAsync();
        await using var scope = api;
        var changes = 0;
        session.Changed += () => changes++;
        api.Stub.OnStatus(HttpMethod.Get, "/api/thing", HttpStatusCode.Unauthorized);

        await api.Get<ApiConnection>().GetAsync<int>("api/thing", Ct);
        await api.Get<ApiConnection>().GetAsync<int>("api/thing", Ct);
        await api.Get<ApiConnection>().GetAsync<int>("api/thing", Ct);

        changes.ShouldBe(1);
        session.State.ShouldBe(AgentSessionState.SessionExpired);
    }

    [Fact]
    public void A_401_before_the_agent_is_admitted_is_left_to_the_first_load()
    {
        var agents = Substitute.For<IAgentsClient>();
        var expiry = new SessionExpiry();
        var session = new AgentSession(agents, expiry);
        var raised = false;
        session.Changed += () => raised = true;

        expiry.Report();

        session.State.ShouldBe(AgentSessionState.NotLoaded);
        session.ExpiredWhileWorking.ShouldBeFalse();
        raised.ShouldBeFalse();
    }

    [Fact]
    public async Task A_401_on_the_very_first_load_is_the_full_expired_state_with_no_agent()
    {
        var agents = Substitute.For<IAgentsClient>();
        agents.GetMeAsync(Ct).Returns(Result<AgentDto>.Failure(new ResultError(ApiErrorCodes.Unauthenticated, "No.", ResultErrorKind.Unauthenticated)));
        var session = new AgentSession(agents, new SessionExpiry());

        await session.EnsureLoadedAsync(Ct);

        session.State.ShouldBe(AgentSessionState.SessionExpired);
        session.Agent.ShouldBeNull();
        session.ExpiredWhileWorking.ShouldBeFalse("nothing was mounted yet, so the gate shows the full page");
        session.IsAdmitted.ShouldBeFalse();
    }

    [Fact]
    public async Task A_reload_that_answers_401_keeps_the_agent_and_expires_the_session()
    {
        var agents = Substitute.For<IAgentsClient>();
        agents.GetMeAsync(Ct).Returns(
            Result<AgentDto>.Success(Agent()),
            Result<AgentDto>.Failure(new ResultError(ApiErrorCodes.Unauthenticated, "No.", ResultErrorKind.Unauthenticated)));
        var session = new AgentSession(agents, new SessionExpiry());
        await session.EnsureLoadedAsync(Ct);

        await session.ReloadAsync(Ct);

        session.ExpiredWhileWorking.ShouldBeTrue();
        session.Agent.ShouldNotBeNull();
    }

    [Fact]
    public async Task A_reload_after_the_session_expired_does_not_drop_to_checking_and_never_returns_to_ready()
    {
        var agents = Substitute.For<IAgentsClient>();
        var expiry = new SessionExpiry();
        var gate = new TaskCompletionSource<Result<AgentDto>>();
        agents.GetMeAsync(Ct).Returns(Task.FromResult(Result<AgentDto>.Success(Agent())), gate.Task);
        var session = new AgentSession(agents, expiry);
        await session.EnsureLoadedAsync(Ct);
        expiry.Report();

        var reload = session.ReloadAsync(Ct);

        session.IsAdmitted.ShouldBeTrue("the page must stay mounted while the reload runs");
        gate.SetResult(Result<AgentDto>.Success(Agent()));
        await reload;
        session.State.ShouldBe(AgentSessionState.SessionExpired, "a lapsed circuit stays lapsed: only a new sign-in (a new circuit) brings the agent back");
        session.ExpiredWhileWorking.ShouldBeTrue();
    }

    [Fact]
    public async Task A_late_me_success_after_the_lapse_keeps_the_session_expired()
    {
        var agents = Substitute.For<IAgentsClient>();
        var expiry = new SessionExpiry();
        var late = new TaskCompletionSource<Result<AgentDto>>();
        agents.GetMeAsync(Ct).Returns(Task.FromResult(Result<AgentDto>.Success(Agent())), late.Task);
        var session = new AgentSession(agents, expiry);
        await session.EnsureLoadedAsync(Ct);
        var reload = session.ReloadAsync(Ct);
        expiry.Report();

        late.SetResult(Result<AgentDto>.Success(Agent()));
        await reload;

        session.State.ShouldBe(AgentSessionState.SessionExpired);
        session.ExpiredWhileWorking.ShouldBeTrue();
    }

    [Fact]
    public async Task After_a_401_no_further_call_leaves_the_client_and_each_answers_the_expired_failure()
    {
        var (api, session) = await ReadyAsync();
        await using var scope = api;
        api.Stub.OnStatus(HttpMethod.Get, "/api/thing", HttpStatusCode.Unauthorized);
        await api.Get<ApiConnection>().GetAsync<int>("api/thing", Ct);
        var sent = api.Stub.Requests.Count;
        var connection = api.Get<ApiConnection>();

        var read = await connection.GetAsync<int>("api/tickets?search=ada%40example.com", Ct);
        var write = await connection.SendAsync(HttpMethod.Post, "api/thing", new { note = "x" }, Ct);
        var writeWithBody = await connection.SendAsync<int>(HttpMethod.Put, "api/thing", new { note = "x" }, Ct);
        using var content = new StringContent("x");
        var multipart = await connection.SendContentAsync<int>(HttpMethod.Post, "api/thing", content, Ct);

        api.Stub.Requests.Count.ShouldBe(sent, "nothing may be sent once the session has lapsed");
        foreach (var errors in new[] { read.Errors, write.Errors, writeWithBody.Errors, multipart.Errors })
        {
            errors[0].Code.ShouldBe(ApiErrorCodes.Unauthenticated);
            errors[0].Kind.ShouldBe(ResultErrorKind.Unauthenticated);
            errors[0].Message.ShouldBe(SessionExpiry.ExpiredMessage);
        }

        session.State.ShouldBe(AgentSessionState.SessionExpired);
    }

    [Fact]
    public async Task SessionExpiry_is_scoped_and_the_session_from_the_container_listens_to_its_own_scope()
    {
        await using var api = await ApiHarness.CreateAsync();
        api.Stub.WithTestAgents();
        var factory = api.Get<IServiceScopeFactory>();
        await using var one = factory.CreateAsyncScope();
        await using var two = factory.CreateAsyncScope();
        var sessionOne = one.ServiceProvider.GetRequiredService<AgentSession>();
        var sessionTwo = two.ServiceProvider.GetRequiredService<AgentSession>();
        await sessionOne.EnsureLoadedAsync(Ct);
        await sessionTwo.EnsureLoadedAsync(Ct);

        var expiryOne = one.ServiceProvider.GetRequiredService<SessionExpiry>();
        expiryOne.ShouldBeSameAs(one.ServiceProvider.GetRequiredService<SessionExpiry>());
        expiryOne.ShouldNotBeSameAs(two.ServiceProvider.GetRequiredService<SessionExpiry>());
        expiryOne.Report();

        sessionOne.ExpiredWhileWorking.ShouldBeTrue();
        sessionTwo.State.ShouldBe(AgentSessionState.Ready, "another circuit's session is not affected");
    }

    [Fact]
    public async Task A_load_that_throws_can_be_marked_unavailable_and_a_ready_session_is_never_downgraded()
    {
        var agents = Substitute.For<IAgentsClient>();
        agents.GetMeAsync(Ct).Returns(Result<AgentDto>.Success(Agent()));
        var ready = new AgentSession(agents, new SessionExpiry());
        await ready.EnsureLoadedAsync(Ct);
        ready.MarkUnavailable();
        ready.State.ShouldBe(AgentSessionState.Ready);

        var fresh = new AgentSession(Substitute.For<IAgentsClient>(), new SessionExpiry());
        fresh.MarkUnavailable();
        fresh.State.ShouldBe(AgentSessionState.Unavailable);
        fresh.ErrorCode.ShouldBe(ApiErrorCodes.ApiUnavailable);
    }
}
