using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Application.Agents;
using TechStrap.Application.Persistence;
using TechStrap.Domain.Agents;

namespace TechStrap.Application.Tests.Agents;

public sealed class CurrentAgentTests
{
    private readonly ICurrentAgentClaims _claims = Substitute.For<ICurrentAgentClaims>();
    private readonly IAgentRepository _agents = Substitute.For<IAgentRepository>();

    private static Agent NewAgent(bool active = true)
    {
        var agent = Agent.Create("sub-1", "Sam", "sam@example.com", AgentRole.Admin, new FakeTimeProvider()).Value;
        agent.SetActive(active);
        return agent;
    }

    [Fact]
    public async Task An_active_provisioned_agent_is_returned()
    {
        var agent = NewAgent();
        _claims.Current.Returns(new AgentClaims("sub-1", "Sam", "sam@example.com", AgentRole.Admin));
        _agents.GetBySubjectAsync("sub-1", Arg.Any<CancellationToken>()).Returns(agent);

        var result = await CurrentAgent.RequireActiveAsync(_claims, _agents, TestContext.Current.CancellationToken);

        result.Value.ShouldBeSameAs(agent);
    }

    [Fact]
    public async Task No_agent_claims_is_forbidden()
    {
        var result = await CurrentAgent.RequireActiveAsync(_claims, _agents, TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            error => error.Kind.ShouldBe(ResultErrorKind.Forbidden),
            error => error.Code.ShouldBe(AgentErrors.Codes.AccessRequired));
    }

    [Fact]
    public async Task An_agent_without_a_row_is_told_to_open_techstrap_first()
    {
        _claims.Current.Returns(new AgentClaims("sub-1", "Sam", "sam@example.com", AgentRole.Agent));

        var result = await CurrentAgent.RequireActiveAsync(_claims, _agents, TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe(AgentErrors.Codes.NotProvisioned);
    }

    [Fact]
    public async Task A_deactivated_agent_is_forbidden()
    {
        _claims.Current.Returns(new AgentClaims("sub-1", "Sam", "sam@example.com", AgentRole.Agent));
        _agents.GetBySubjectAsync("sub-1", Arg.Any<CancellationToken>()).Returns(NewAgent(active: false));

        var result = await CurrentAgent.RequireActiveAsync(_claims, _agents, TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            error => error.Kind.ShouldBe(ResultErrorKind.Forbidden),
            error => error.Code.ShouldBe(AgentErrors.Codes.Inactive));
    }
}
