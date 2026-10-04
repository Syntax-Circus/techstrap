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
}
