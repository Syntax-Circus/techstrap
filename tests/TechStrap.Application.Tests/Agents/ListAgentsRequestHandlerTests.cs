using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Application.Agents;
using TechStrap.Application.Persistence;
using TechStrap.Domain.Agents;

namespace TechStrap.Application.Tests.Agents;

public sealed class ListAgentsRequestHandlerTests
{
    private readonly ICurrentAgentClaims _claims = Substitute.For<ICurrentAgentClaims>();
    private readonly IAgentRepository _agents = Substitute.For<IAgentRepository>();
    private readonly Agent _sam = Agent.Create("s", "Sam", "sam@example.com", AgentRole.Admin, new FakeTimeProvider()).Value;

    private ListAgentsRequestHandler Handler() => new(_claims, _agents);

    [Fact]
    public async Task An_agent_sees_active_agents_with_assignment_fields_only()
    {
        _claims.Current.Returns(new AgentClaims("s", "Sam", "sam@example.com", AgentRole.Agent));
        _agents.ListAsync(true, 2, 10, Arg.Any<CancellationToken>()).Returns(new PagedResult<Agent>([_sam], 2, 10, 11));

        var page = (await Handler().HandleAsync(2, 10, TestContext.Current.CancellationToken)).Value;

        page.ShouldSatisfyAllConditions(
            p => p.Page.ShouldBe(2),
            p => p.TotalCount.ShouldBe(11),
            p => p.Items.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
                item => item.DisplayLabel.ShouldBe("Sam"),
                item => item.Email.ShouldBeNull(),
                item => item.Role.ShouldBeNull(),
                item => item.IsActive.ShouldBeNull()));
    }

    [Fact]
    public async Task An_admin_sees_every_agent_with_email_role_and_status()
    {
        _claims.Current.Returns(new AgentClaims("s", "Sam", "sam@example.com", AgentRole.Admin));
        _agents.ListAsync(false, 1, 25, Arg.Any<CancellationToken>()).Returns(new PagedResult<Agent>([_sam], 1, 25, 1));

        var item = (await Handler().HandleAsync(1, 25, TestContext.Current.CancellationToken)).Value.Items.ShouldHaveSingleItem();

        item.Email.ShouldBe("sam@example.com");
        item.Role.ShouldBe("Admin");
        item.IsActive.ShouldBe(true);
    }
}
