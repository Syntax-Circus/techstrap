using System.Security.Claims;
using TechStrap.Api.Options;
using TechStrap.Api.Security;
using TechStrap.Application.Agents;
using TechStrap.Domain.Agents;

namespace TechStrap.Api.Tests.Auth;

public sealed class ClaimsCurrentAgentClaimsTests
{
    private static readonly AgentAccessOptions Options = new();

    private static ClaimsPrincipal Principal(params (string Type, string Value)[] claims) =>
        new(new ClaimsIdentity(claims.Select(claim => new Claim(claim.Type, claim.Value)), authenticationType: "test"));

    [Fact]
    public void An_agent_group_member_gets_the_Agent_role_with_identity_claims()
    {
        var claims = ClaimsCurrentAgentClaims.FromPrincipal(
            Principal(("sub", "abc"), ("email", "sam@example.com"), ("name", "Sam Whitfield"), ("groups", "techstrap-agents")), Options);

        claims.ShouldBe(new AgentClaims("abc", "Sam Whitfield", "sam@example.com", AgentRole.Agent));
    }

    [Fact]
    public void An_admin_group_member_gets_the_Admin_role_even_without_the_agent_group()
    {
        ClaimsCurrentAgentClaims.FromPrincipal(Principal(("sub", "abc"), ("groups", "techstrap-admins")), Options)!
            .Role.ShouldBe(AgentRole.Admin);
    }

    [Theory]
    [InlineData("[\"other\",\"TechStrap-Agents\"]")]
    [InlineData("other, techstrap-agents")]
    [InlineData("other techstrap-agents")]
    public void Groups_are_read_from_repeated_claims_json_arrays_and_delimited_lists(string value)
    {
        ClaimsCurrentAgentClaims.FromPrincipal(Principal(("sub", "abc"), ("groups", "unrelated"), ("groups", value)), Options)!
            .Role.ShouldBe(AgentRole.Agent);
    }

    [Fact]
    public void A_configured_claim_type_is_used_instead_of_groups()
    {
        var options = new AgentAccessOptions { GroupClaimType = "roles" };

        ClaimsCurrentAgentClaims.FromPrincipal(Principal(("sub", "abc"), ("roles", "techstrap-admins")), options)!
            .Role.ShouldBe(AgentRole.Admin);
        ClaimsCurrentAgentClaims.FromPrincipal(Principal(("sub", "abc"), ("groups", "techstrap-admins")), options).ShouldBeNull();
    }

    [Fact]
    public void Preferred_username_is_the_name_when_no_name_claim_is_present()
    {
        ClaimsCurrentAgentClaims.FromPrincipal(Principal(("sub", "abc"), ("preferred_username", "sam"), ("groups", "techstrap-agents")), Options)!
            .Name.ShouldBe("sam");
    }

    [Fact]
    public void No_group_no_subject_or_an_anonymous_principal_gives_no_agent()
    {
        ClaimsCurrentAgentClaims.FromPrincipal(Principal(("sub", "abc"), ("groups", "someone-else")), Options).ShouldBeNull();
        ClaimsCurrentAgentClaims.FromPrincipal(Principal(("groups", "techstrap-agents")), Options).ShouldBeNull();
        ClaimsCurrentAgentClaims.FromPrincipal(new ClaimsPrincipal(new ClaimsIdentity()), Options).ShouldBeNull();
    }
}
