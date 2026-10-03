using System.Globalization;
using Microsoft.Extensions.Time.Testing;
using TechStrap.Contracts.Agents;
using TechStrap.Domain.Agents;

namespace TechStrap.Application.Tests.Agents;

/// <summary>Admin previews the customer-facing name with the Contracts format; customers see AgentPublicIdentity (D-024).</summary>
public sealed class AgentPublicNameParityTests
{
    private static Agent NewAgent(string? name, string? publicDisplayName)
    {
        var agent = Agent.Create("sub-1", name, "sam@example.com", AgentRole.Agent, new FakeTimeProvider()).Value;
        agent.SetPublicDisplayName(publicDisplayName).IsSuccess.ShouldBeTrue();
        return agent;
    }

    [Theory]
    [InlineData("Sam Whitfield", null, "Sam")]
    [InlineData("Riley Chen", "Ry", "Ry")]
    public void The_contract_format_produces_the_domain_name(string name, string? publicDisplayName, string given)
    {
        var agent = NewAgent(name, publicDisplayName);

        string.Format(CultureInfo.InvariantCulture, AgentPublicName.Format, given, "Orbitly")
            .ShouldBe(AgentPublicIdentity.Resolve(agent, "Orbitly"));
    }

    [Fact]
    public void The_fallback_format_produces_the_domain_name_when_no_name_is_known()
    {
        var agent = NewAgent(null, null);

        string.Format(CultureInfo.InvariantCulture, AgentPublicName.FallbackFormat, "Orbitly")
            .ShouldBe(AgentPublicIdentity.Resolve(agent, "Orbitly"));
    }
}
