using Microsoft.Extensions.Time.Testing;
using TechStrap.Domain.Agents;

namespace TechStrap.Domain.Tests.Agents;

public sealed class AgentTests
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 2, 9, 0, 0, TimeSpan.Zero));

    private Agent NewAgent(AgentRole role = AgentRole.Agent) =>
        Agent.Create("oidc|sam", "Sam Whitfield", "Sam@Example.com", role, _clock).Value;

    [Fact]
    public void A_new_agent_is_active_with_a_lower_cased_email()
    {
        var agent = NewAgent();

        agent.IsActive.ShouldBeTrue();
        agent.Email.ShouldBe("sam@example.com");
        agent.Role.ShouldBe(AgentRole.Agent);
        agent.PublicDisplayName.ShouldBeNull();
    }

    [Fact]
    public void A_subject_and_a_valid_email_are_required()
    {
        Agent.Create("", "Sam", "sam@example.com", AgentRole.Agent, _clock).Error!.Code.ShouldBe("oidc-subject-required");
        Agent.Create("s", "Sam", "nope", AgentRole.Agent, _clock).Error!.Code.ShouldBe("email-invalid");
    }

    [Fact]
    public void The_role_and_the_active_flag_can_change()
    {
        var agent = NewAgent();

        agent.ChangeRole(AgentRole.Admin);
        agent.SetActive(false);

        agent.Role.ShouldBe(AgentRole.Admin);
        agent.IsActive.ShouldBeFalse();
    }

    [Fact]
    public void Recording_a_sighting_stamps_last_seen_from_the_clock()
    {
        var agent = NewAgent();
        _clock.Advance(TimeSpan.FromMinutes(3));

        agent.RecordSeen(_clock);

        agent.LastSeenAt.ShouldBe(_clock.GetUtcNow());
    }

    [Fact]
    public void Refreshing_the_identity_updates_name_and_email()
    {
        var agent = NewAgent();

        agent.UpdateIdentity("Samantha Whitfield", "samantha@example.com").IsSuccess.ShouldBeTrue();

        agent.Name.ShouldBe("Samantha Whitfield");
        agent.Email.ShouldBe("samantha@example.com");
    }
}
