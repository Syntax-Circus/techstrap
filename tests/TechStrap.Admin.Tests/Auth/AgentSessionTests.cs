using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SyntaxCircus.Common;
using TechStrap.Admin.Auth;
using TechStrap.Admin.Clients;
using TechStrap.Contracts.Agents;

namespace TechStrap.Admin.Tests.Auth;

/// <summary>Review Focus 1: the API's answer to <c>GET /api/agents/me</c> decides who may work, and nothing else is trusted.</summary>
public sealed class AgentSessionTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private readonly IAgentsClient _agents = Substitute.For<IAgentsClient>();

    private AgentSession Session() => new(_agents);

    private static AgentDto Agent(string role = AgentRoles.Agent, bool active = true) => new(Guid.NewGuid(), "Sam", "sam@orbitly.test", role, active, null, null);

    private static Result<AgentDto> Refused(string code, ResultErrorKind kind = ResultErrorKind.Forbidden) =>
        Result<AgentDto>.Failure(new ResultError(code, "The API says no.", kind));

    [Fact]
    public async Task An_active_agent_is_ready_and_an_agent_is_not_an_admin()
    {
        _agents.GetMeAsync(Ct).Returns(Result<AgentDto>.Success(Agent()));
        var session = Session();

        await session.EnsureLoadedAsync(Ct);

        session.State.ShouldBe(AgentSessionState.Ready);
        session.Agent!.Email.ShouldBe("sam@orbitly.test");
        session.IsAdmin.ShouldBeFalse();
    }

    [Fact]
    public async Task Only_the_api_role_makes_an_admin()
    {
        _agents.GetMeAsync(Ct).Returns(Result<AgentDto>.Success(Agent(AgentRoles.Admin)));
        var session = Session();

        await session.EnsureLoadedAsync(Ct);

        session.IsAdmin.ShouldBeTrue();
    }

    [Theory]
    [InlineData(ApiErrorCodes.AgentAccessRequired)]
    [InlineData(ApiErrorCodes.AgentInactive)]
    [InlineData(ApiErrorCodes.AgentEmailRequired)]
    [InlineData(ApiErrorCodes.AgentIdentityInvalid)]
    public async Task Every_403_is_no_access_with_the_reason_and_never_an_agent(string code)
    {
        _agents.GetMeAsync(Ct).Returns(Refused(code));
        var session = Session();

        await session.EnsureLoadedAsync(Ct);

        session.State.ShouldBe(AgentSessionState.NoAccess);
        session.ErrorCode.ShouldBe(code);
        session.ErrorMessage.ShouldBe("The API says no.");
        session.Agent.ShouldBeNull();
        session.IsAdmin.ShouldBeFalse();
    }

    [Fact]
    public async Task A_deactivated_agent_the_api_still_returns_is_refused()
    {
        _agents.GetMeAsync(Ct).Returns(Result<AgentDto>.Success(Agent(AgentRoles.Admin, active: false)));
        var session = Session();

        await session.EnsureLoadedAsync(Ct);

        session.State.ShouldBe(AgentSessionState.NoAccess);
        session.ErrorCode.ShouldBe(ApiErrorCodes.AgentInactive);
        session.IsAdmin.ShouldBeFalse();
    }

    [Fact]
    public async Task A_401_is_an_expired_session_and_a_transport_failure_is_unavailable_and_both_are_asked_again()
    {
        _agents.GetMeAsync(Ct).Returns(
            Refused(ApiErrorCodes.Unauthenticated, ResultErrorKind.Unauthenticated),
            Refused(ApiErrorCodes.ApiUnavailable, ResultErrorKind.Failure),
            Result<AgentDto>.Success(Agent()));
        var session = Session();

        await session.EnsureLoadedAsync(Ct);
        session.State.ShouldBe(AgentSessionState.SessionExpired);
        await session.EnsureLoadedAsync(Ct);
        session.State.ShouldBe(AgentSessionState.Unavailable);
        await session.EnsureLoadedAsync(Ct);

        session.State.ShouldBe(AgentSessionState.Ready);
        await _agents.Received(3).GetMeAsync(Ct);
    }

    [Fact]
    public async Task Ready_and_no_access_are_final_so_the_api_is_asked_once_per_scope()
    {
        _agents.GetMeAsync(Ct).Returns(Result<AgentDto>.Success(Agent()));
        var session = Session();

        await session.EnsureLoadedAsync(Ct);
        await session.EnsureLoadedAsync(Ct);

        await _agents.Received(1).GetMeAsync(Ct);

        _agents.ClearReceivedCalls();
        _agents.GetMeAsync(Ct).Returns(Refused(ApiErrorCodes.AgentAccessRequired));
        var refused = Session();
        await refused.EnsureLoadedAsync(Ct);
        await refused.EnsureLoadedAsync(Ct);
        await _agents.Received(1).GetMeAsync(Ct);
    }

    [Fact]
    public async Task Concurrent_callers_share_one_request()
    {
        var gate = new TaskCompletionSource<Result<AgentDto>>();
        _agents.GetMeAsync(Ct).Returns(gate.Task);
        var session = Session();

        var first = session.EnsureLoadedAsync(Ct);
        var second = session.EnsureLoadedAsync(Ct);
        gate.SetResult(Result<AgentDto>.Success(Agent()));
        await Task.WhenAll(first, second);

        await _agents.Received(1).GetMeAsync(Ct);
        session.State.ShouldBe(AgentSessionState.Ready);
    }

    [Fact]
    public async Task A_cancelled_load_does_not_pin_the_session()
    {
        _agents.GetMeAsync(Arg.Any<CancellationToken>()).Throws(new OperationCanceledException());
        var session = Session();

        await Should.ThrowAsync<OperationCanceledException>(() => session.EnsureLoadedAsync(Ct));

        _agents.GetMeAsync(Arg.Any<CancellationToken>()).Returns(Result<AgentDto>.Success(Agent()));
        await session.EnsureLoadedAsync(Ct);
        session.State.ShouldBe(AgentSessionState.Ready);
    }

    [Fact]
    public async Task A_waiter_whose_shared_load_was_cancelled_loads_for_itself()
    {
        var gate = new TaskCompletionSource<Result<AgentDto>>();
        _agents.GetMeAsync(Arg.Any<CancellationToken>()).Returns(_ => gate.Task, _ => Task.FromResult(Result<AgentDto>.Success(Agent())));
        var session = Session();
        using var owner = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        var first = session.EnsureLoadedAsync(owner.Token);
        var second = session.EnsureLoadedAsync(Ct);
        gate.SetException(new OperationCanceledException());

        await Should.ThrowAsync<OperationCanceledException>(() => first);
        await second;
        session.State.ShouldBe(AgentSessionState.Ready);
    }

    [Fact]
    public async Task Reload_asks_again_and_a_change_is_announced()
    {
        _agents.GetMeAsync(Ct).Returns(Refused(ApiErrorCodes.AgentInactive), Result<AgentDto>.Success(Agent()));
        var session = Session();
        var changes = 0;
        session.Changed += () => changes++;

        await session.EnsureLoadedAsync(Ct);
        await session.ReloadAsync(Ct);

        session.State.ShouldBe(AgentSessionState.Ready);
        changes.ShouldBe(2);
    }

    // The My settings save reloads the session (the display name is part of /api/agents/me). The page must not unmount while that is in flight.
    [Fact]
    public async Task A_reload_keeps_the_session_ready_with_the_current_agent_until_the_answer_arrives()
    {
        var admin = Agent(AgentRoles.Admin);
        _agents.GetMeAsync(Arg.Any<CancellationToken>()).Returns(Result<AgentDto>.Success(admin));
        var session = Session();
        await session.EnsureLoadedAsync(Ct);
        var changes = 0;
        session.Changed += () => changes++;
        var gate = new TaskCompletionSource<Result<AgentDto>>();
        _agents.GetMeAsync(Arg.Any<CancellationToken>()).Returns(gate.Task);

        var reload = session.ReloadAsync(Ct);

        reload.IsCompleted.ShouldBeFalse();
        session.State.ShouldBe(AgentSessionState.Ready);
        session.Agent.ShouldBe(admin);
        session.IsAdmin.ShouldBeTrue();
        changes.ShouldBe(0);

        gate.SetResult(Result<AgentDto>.Success(admin with { PublicDisplayName = "Samantha" }));
        await reload;

        session.State.ShouldBe(AgentSessionState.Ready);
        session.Agent!.PublicDisplayName.ShouldBe("Samantha");
        changes.ShouldBe(1);
    }

    [Fact]
    public async Task While_a_reload_is_in_flight_another_caller_does_not_start_a_second_request()
    {
        _agents.GetMeAsync(Arg.Any<CancellationToken>()).Returns(Result<AgentDto>.Success(Agent()));
        var session = Session();
        await session.EnsureLoadedAsync(Ct);
        _agents.ClearReceivedCalls();
        var gate = new TaskCompletionSource<Result<AgentDto>>();
        _agents.GetMeAsync(Arg.Any<CancellationToken>()).Returns(gate.Task);

        var reload = session.ReloadAsync(Ct);
        await session.EnsureLoadedAsync(Ct).WaitAsync(TimeSpan.FromSeconds(5), Ct);
        gate.SetResult(Result<AgentDto>.Success(Agent()));
        await reload;

        await _agents.Received(1).GetMeAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_reload_that_finds_the_agent_demoted_removes_the_admin_role_at_once()
    {
        _agents.GetMeAsync(Arg.Any<CancellationToken>()).Returns(Result<AgentDto>.Success(Agent(AgentRoles.Admin)), Result<AgentDto>.Success(Agent(AgentRoles.Agent)));
        var session = Session();
        await session.EnsureLoadedAsync(Ct);
        session.IsAdmin.ShouldBeTrue();

        await session.ReloadAsync(Ct);

        session.State.ShouldBe(AgentSessionState.Ready);
        session.IsAdmin.ShouldBeFalse();
    }

    [Theory]
    [InlineData(ApiErrorCodes.AgentInactive, ResultErrorKind.Forbidden, AgentSessionState.NoAccess)]
    [InlineData(ApiErrorCodes.Unauthenticated, ResultErrorKind.Unauthenticated, AgentSessionState.SessionExpired)]
    public async Task A_reload_that_the_api_refuses_wins_over_the_ready_session(string code, ResultErrorKind kind, AgentSessionState expected)
    {
        _agents.GetMeAsync(Arg.Any<CancellationToken>()).Returns(Result<AgentDto>.Success(Agent(AgentRoles.Admin)), Refused(code, kind));
        var session = Session();
        await session.EnsureLoadedAsync(Ct);

        await session.ReloadAsync(Ct);

        session.State.ShouldBe(expected);
        session.Agent.ShouldBeNull();
        session.IsAdmin.ShouldBeFalse();
    }

    [Fact]
    public async Task A_reload_that_cannot_reach_the_api_keeps_the_ready_session_and_does_not_lock_the_agent_out()
    {
        var admin = Agent(AgentRoles.Admin);
        _agents.GetMeAsync(Arg.Any<CancellationToken>()).Returns(Result<AgentDto>.Success(admin), Refused(ApiErrorCodes.ApiUnavailable, ResultErrorKind.Failure));
        var session = Session();
        await session.EnsureLoadedAsync(Ct);
        var changes = 0;
        session.Changed += () => changes++;

        await session.ReloadAsync(Ct);

        session.State.ShouldBe(AgentSessionState.Ready);
        session.Agent.ShouldBe(admin);
        session.ErrorCode.ShouldBeNull();
        changes.ShouldBe(0);
    }

    [Fact]
    public async Task A_cancelled_reload_leaves_the_ready_session_alone()
    {
        var admin = Agent(AgentRoles.Admin);
        _agents.GetMeAsync(Arg.Any<CancellationToken>()).Returns(Result<AgentDto>.Success(admin));
        var session = Session();
        await session.EnsureLoadedAsync(Ct);
        _agents.GetMeAsync(Arg.Any<CancellationToken>()).Throws(new OperationCanceledException());

        await Should.ThrowAsync<OperationCanceledException>(() => session.ReloadAsync(Ct));

        session.State.ShouldBe(AgentSessionState.Ready);
        session.Agent.ShouldBe(admin);
    }

    [Fact]
    public async Task A_reload_from_a_failed_state_starts_again_from_not_loaded_so_the_gate_shows_checking()
    {
        var gate = new TaskCompletionSource<Result<AgentDto>>();
        _agents.GetMeAsync(Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult(Refused(ApiErrorCodes.ApiUnavailable, ResultErrorKind.Failure)), _ => gate.Task);
        var session = Session();
        await session.EnsureLoadedAsync(Ct);
        session.State.ShouldBe(AgentSessionState.Unavailable);

        var reload = session.ReloadAsync(Ct);

        session.State.ShouldBe(AgentSessionState.NotLoaded);
        gate.SetResult(Result<AgentDto>.Success(Agent()));
        await reload;
        session.State.ShouldBe(AgentSessionState.Ready);
    }

    [Fact]
    public async Task A_reload_that_times_out_keeps_the_ready_session()
    {
        var admin = Agent(AgentRoles.Admin);
        _agents.GetMeAsync(Arg.Any<CancellationToken>()).Returns(Result<AgentDto>.Success(admin), Refused(ApiErrorCodes.ApiTimeout, ResultErrorKind.Failure));
        var session = Session();
        await session.EnsureLoadedAsync(Ct);

        await session.ReloadAsync(Ct);

        session.State.ShouldBe(AgentSessionState.Ready);
        session.Agent.ShouldBe(admin);
    }

    // Only an unreachable API or a timeout keeps the session: any other answer (not found, a response the Admin does not understand) is not proof the agent may still work.
    [Theory]
    [InlineData(ApiErrorCodes.UnexpectedResponse, ResultErrorKind.Failure)]
    [InlineData(ApiErrorCodes.ApiError, ResultErrorKind.Failure)]
    [InlineData("not-found", ResultErrorKind.NotFound)]
    public async Task A_reload_with_any_other_failure_ends_the_ready_session_like_a_first_load(string code, ResultErrorKind kind)
    {
        _agents.GetMeAsync(Arg.Any<CancellationToken>()).Returns(Result<AgentDto>.Success(Agent(AgentRoles.Admin)), Refused(code, kind));
        var session = Session();
        await session.EnsureLoadedAsync(Ct);

        await session.ReloadAsync(Ct);

        session.State.ShouldBe(AgentSessionState.Unavailable);
        session.Agent.ShouldBeNull();
        session.IsAdmin.ShouldBeFalse();
    }
}
