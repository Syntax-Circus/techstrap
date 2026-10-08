using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Application.Agents;
using TechStrap.Application.Persistence;
using TechStrap.Application.Tests.Support;
using TechStrap.Contracts.Agents;
using TechStrap.Domain.Admin;
using TechStrap.Domain.Agents;

namespace TechStrap.Application.Tests.Agents;

public sealed class UpdateAgentRequestHandlerTests
{
    private readonly ICurrentAgentClaims _claims = Substitute.For<ICurrentAgentClaims>();
    private readonly IAgentRepository _agents = Substitute.For<IAgentRepository>();
    private readonly IAdminEventRepository _events = Substitute.For<IAdminEventRepository>();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 3, 9, 0, 0, TimeSpan.Zero));
    private readonly Agent _actor;

    public UpdateAgentRequestHandlerTests()
    {
        _actor = Agent.Create("actor", "Sam", "sam@example.com", AgentRole.Admin, _clock).Value;
        _claims.Current.Returns(new AgentClaims("actor", "Sam", "sam@example.com", AgentRole.Admin));
        _agents.GetBySubjectAsync("actor", Arg.Any<CancellationToken>()).Returns(_actor);
    }

    private UpdateAgentRequestHandler Handler() => new(_claims, _agents, _events, UnitOfWorkSubstitute.Create(), _clock);

    private Agent Target(AgentRole role, bool active = true)
    {
        var agent = Agent.Create("target", "Riley", "riley@example.com", role, _clock).Value;
        agent.SetActive(active);
        _agents.GetByIdAsync(agent.Id, Arg.Any<CancellationToken>()).Returns(agent);
        return agent;
    }

    [Fact]
    public async Task Deactivating_an_agent_saves_and_audits_it()
    {
        var target = Target(AgentRole.Agent);
        _agents.CountActiveAdminsLockedAsync(Arg.Any<CancellationToken>()).Returns(1);

        var result = await Handler().HandleAsync(target.Id, new UpdateAgentRequest(false), TestContext.Current.CancellationToken);

        result.Value.IsActive.ShouldBeFalse();
        _agents.Received(1).Update(target);
        _events.Received(1).Add(Arg.Is<AdminEvent>(e =>
            e.Type == AdminEventType.AgentUpdated && e.SubjectId == target.Id && e.ActorId == _actor.Id && e.PayloadJson == "{\"isActive\":false}"));
    }

    [Fact]
    public async Task Reactivating_an_agent_saves_and_audits_it()
    {
        var target = Target(AgentRole.Agent, active: false);

        var result = await Handler().HandleAsync(target.Id, new UpdateAgentRequest(true), TestContext.Current.CancellationToken);

        result.Value.IsActive.ShouldBeTrue();
        _events.Received(1).Add(Arg.Any<AdminEvent>());
    }

    [Fact]
    public async Task The_last_active_admin_cannot_be_deactivated()
    {
        var target = Target(AgentRole.Admin);
        _agents.CountActiveAdminsLockedAsync(Arg.Any<CancellationToken>()).Returns(1);

        var result = await Handler().HandleAsync(target.Id, new UpdateAgentRequest(false), TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            error => error.Kind.ShouldBe(ResultErrorKind.Conflict),
            error => error.Code.ShouldBe("last-active-admin"));
        _agents.DidNotReceive().Update(Arg.Any<Agent>());
        _events.DidNotReceive().Add(Arg.Any<AdminEvent>());
    }

    [Fact]
    public async Task An_admin_can_be_deactivated_while_another_admin_stays_active()
    {
        var target = Target(AgentRole.Admin);
        _agents.CountActiveAdminsLockedAsync(Arg.Any<CancellationToken>()).Returns(2);

        (await Handler().HandleAsync(target.Id, new UpdateAgentRequest(false), TestContext.Current.CancellationToken)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Setting_the_current_state_again_changes_nothing_and_is_not_audited()
    {
        var target = Target(AgentRole.Agent);

        var result = await Handler().HandleAsync(target.Id, new UpdateAgentRequest(true), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        _agents.DidNotReceive().Update(Arg.Any<Agent>());
        _events.DidNotReceive().Add(Arg.Any<AdminEvent>());
    }

    [Fact]
    public async Task An_unknown_agent_is_not_found()
    {
        var result = await Handler().HandleAsync(Guid.CreateVersion7(), new UpdateAgentRequest(false), TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().Kind.ShouldBe(ResultErrorKind.NotFound);
    }

    [Fact]
    public async Task An_actor_without_an_agent_row_is_asked_to_open_techstrap_first()
    {
        _agents.GetBySubjectAsync("actor", Arg.Any<CancellationToken>()).Returns((Agent?)null);

        var result = await Handler().HandleAsync(Guid.CreateVersion7(), new UpdateAgentRequest(false), TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe("agent-not-provisioned");
    }

    [Fact]
    public async Task A_missing_active_flag_is_a_field_error_and_changes_nothing()
    {
        var target = Target(AgentRole.Agent);

        var result = await Handler().HandleAsync(target.Id, new UpdateAgentRequest(null), TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            error => error.Kind.ShouldBe(ResultErrorKind.Validation),
            error => error.Code.ShouldBe("is-active-required"),
            error => error.Target.ShouldBe("isActive"));
        target.IsActive.ShouldBeTrue();
        _agents.DidNotReceive().Update(Arg.Any<Agent>());
        _events.DidNotReceive().Add(Arg.Any<AdminEvent>());
    }

    [Fact(Timeout = 60_000)]
    public async Task The_admin_lock_is_taken_before_the_actor_is_read()
    {
        var target = Target(AgentRole.Agent);
        _agents.CountActiveAdminsLockedAsync(Arg.Any<CancellationToken>()).Returns(2);

        await Handler().HandleAsync(target.Id, new UpdateAgentRequest(false), TestContext.Current.CancellationToken);

        Received.InOrder(() =>
        {
            _agents.CountActiveAdminsLockedAsync(Arg.Any<CancellationToken>());
            _agents.GetBySubjectAsync("actor", Arg.Any<CancellationToken>());
        });
    }

    [Fact(Timeout = 60_000)]
    public async Task An_actor_deactivated_while_waiting_for_the_lock_is_refused()
    {
        var target = Target(AgentRole.Agent);
        _agents.CountActiveAdminsLockedAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            _actor.SetActive(false); // the other transaction committed while this one waited for the lock
            return 2;
        });

        var result = await Handler().HandleAsync(target.Id, new UpdateAgentRequest(false), TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe("agent-inactive");
        _agents.DidNotReceive().Update(Arg.Any<Agent>());
        _events.DidNotReceive().Add(Arg.Any<AdminEvent>());
    }
}
