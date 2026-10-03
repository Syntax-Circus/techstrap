using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using TechStrap.Application.Agents;
using TechStrap.Application.Persistence;
using TechStrap.Application.Tickets;
using TechStrap.Domain.Agents;

namespace TechStrap.Application.Tests.Tickets;

public sealed class CountTicketViewsRequestHandlerTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private readonly ICurrentAgentClaims _claims = Substitute.For<ICurrentAgentClaims>();
    private readonly IAgentRepository _agents = Substitute.For<IAgentRepository>();
    private readonly ITicketRepository _tickets = Substitute.For<ITicketRepository>();

    [Fact]
    public async Task The_counts_for_the_signed_in_agent_are_mapped()
    {
        var sam = Agent.Create("sam", "Sam", "sam@example.com", AgentRole.Agent, new FakeTimeProvider()).Value;
        _claims.Current.Returns(new AgentClaims("sam", "Sam", "sam@example.com", AgentRole.Agent));
        _agents.GetBySubjectAsync("sam", Arg.Any<CancellationToken>()).Returns(sam);
        _tickets.CountViewsAsync(sam.Id, Arg.Any<CancellationToken>()).Returns(new TicketViewCounts(1, 2, 3, 4, 5, 6));

        var result = await new CountTicketViewsRequestHandler(_claims, _agents, _tickets).HandleAsync(Ct);

        result.Value.ShouldSatisfyAllConditions(
            r => r.Unassigned.ShouldBe(1), r => r.Mine.ShouldBe(2), r => r.Open.ShouldBe(3),
            r => r.Pending.ShouldBe(4), r => r.All.ShouldBe(5), r => r.Spam.ShouldBe(6));
    }

    [Fact]
    public async Task An_unprovisioned_agent_is_refused()
    {
        _claims.Current.Returns(new AgentClaims("sam", "Sam", "sam@example.com", AgentRole.Agent));
        _agents.GetBySubjectAsync("sam", Arg.Any<CancellationToken>()).Returns((Agent?)null);

        var result = await new CountTicketViewsRequestHandler(_claims, _agents, _tickets).HandleAsync(Ct);

        result.IsSuccess.ShouldBeFalse();
        await _tickets.DidNotReceiveWithAnyArgs().CountViewsAsync(default, Ct);
    }
}
