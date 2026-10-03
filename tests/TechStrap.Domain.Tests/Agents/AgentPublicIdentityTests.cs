using Microsoft.Extensions.Time.Testing;
using TechStrap.Domain.Agents;

namespace TechStrap.Domain.Tests.Agents;

/// <summary>D-024: what a customer sees for an agent.</summary>
public sealed class AgentPublicIdentityTests
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 2, 9, 0, 0, TimeSpan.Zero));

    private Agent AgentNamed(string? name) => Agent.Create("oidc|a", name, "a@example.com", AgentRole.Agent, _clock).Value;

    [Fact]
    public void Without_an_override_the_first_word_of_the_name_is_used()
    {
        AgentPublicIdentity.Resolve(AgentNamed("Sam W."), "Orbitly").ShouldBe("Sam from Orbitly Support");
    }

    [Fact]
    public void An_override_replaces_the_first_name_and_keeps_the_suffix()
    {
        var agent = AgentNamed("Sam W.");
        agent.SetPublicDisplayName("Samantha").IsSuccess.ShouldBeTrue();

        AgentPublicIdentity.Resolve(agent, "Orbitly").ShouldBe("Samantha from Orbitly Support");
    }

    [Fact]
    public void A_whitespace_override_clears_it_and_falls_back_to_the_default()
    {
        var agent = AgentNamed("Sam W.");
        agent.SetPublicDisplayName("Samantha");

        agent.SetPublicDisplayName("   ").IsSuccess.ShouldBeTrue();

        agent.PublicDisplayName.ShouldBeNull();
        AgentPublicIdentity.Resolve(agent, "Orbitly").ShouldBe("Sam from Orbitly Support");
    }

    [Fact]
    public void An_agent_with_no_name_and_no_override_is_just_the_product_support()
    {
        AgentPublicIdentity.Resolve(AgentNamed(null), "Orbitly").ShouldBe("Orbitly Support");
    }

    [Fact]
    public void The_override_is_trimmed()
    {
        var agent = AgentNamed("Sam W.");
        agent.SetPublicDisplayName("  Sam W.  ");

        agent.PublicDisplayName.ShouldBe("Sam W.");
    }

    [Theory]
    [InlineData("sam@example.com")]
    [InlineData("Sam\tW")]
    [InlineData("Sam\nW")]
    public void An_email_like_or_control_character_override_is_rejected(string value)
    {
        var agent = AgentNamed("Sam W.");

        var result = agent.SetPublicDisplayName(value);

        result.Error!.Code.ShouldBe("public-display-name-invalid");
        agent.PublicDisplayName.ShouldBeNull();
    }

    [Fact]
    public void An_override_over_sixty_characters_is_rejected_and_sixty_is_accepted()
    {
        var agent = AgentNamed("Sam W.");

        agent.SetPublicDisplayName(new string('a', 61)).Error!.Code.ShouldBe("public-display-name-too-long");
        agent.SetPublicDisplayName(new string('a', 60)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void The_resolved_name_never_contains_the_agent_email()
    {
        var agent = AgentNamed("Sam W.");

        AgentPublicIdentity.Resolve(agent, "Orbitly").ShouldNotContain("@");
    }

    [Theory]
    [InlineData("sam@example.com")]
    [InlineData("sam@example.com Smith")]
    public void An_email_like_idp_name_is_never_used_as_the_public_name(string name)
    {
        AgentPublicIdentity.Resolve(AgentNamed(name), "Orbitly").ShouldBe("Orbitly Support");
    }
}
