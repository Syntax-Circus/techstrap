using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TechStrap.Api.Options;

namespace TechStrap.Api.Tests.Auth;

public sealed class AgentAccessOptionsTests
{
    [Fact]
    public void Defaults_match_the_documented_env_example_values()
    {
        var options = new AgentAccessOptions();

        options.AgentGroup.ShouldBe("techstrap-agents");
        options.AdminGroup.ShouldBe("techstrap-admins");
        options.GroupClaimType.ShouldBe("groups");
    }

    [Theory]
    [InlineData("TECHSTRAP_AGENT_GROUP", "")]
    [InlineData("TECHSTRAP_ADMIN_GROUP", " ")]
    [InlineData("TECHSTRAP_GROUP_CLAIM_TYPE", "")]
    [InlineData("TECHSTRAP_ADMIN_GROUP", "techstrap-agents")]
    public async Task A_blank_or_duplicate_group_setting_stops_the_api_from_starting(string key, string value)
    {
        await using var factory = new ApiFactory(settings: new Dictionary<string, string?> { [key] = value });

        Should.Throw<OptionsValidationException>(() => factory.CreateClient());
    }

    [Fact]
    public async Task Surrounding_whitespace_in_group_settings_is_ignored()
    {
        await using var factory = new ApiFactory(settings: new Dictionary<string, string?> { ["TECHSTRAP_AGENT_GROUP"] = " techstrap-agents ", ["TECHSTRAP_GROUP_CLAIM_TYPE"] = " groups " });

        using var client = factory.CreateClient();
        var options = factory.Services.GetRequiredService<IOptions<AgentAccessOptions>>().Value;

        options.AgentGroup.ShouldBe("techstrap-agents");
        options.GroupClaimType.ShouldBe("groups");
    }

    [Fact]
    public async Task Outside_development_missing_jwt_audiences_stop_the_api_from_starting()
    {
        await using var factory = new ApiFactory(
            environment: "Production",
            settings: new Dictionary<string, string?> { ["Authentication:JwtBearer:Audiences:0"] = "" });

        Should.Throw<OptionsValidationException>(() => factory.CreateClient());
    }

    [Fact]
    public async Task Outside_development_a_missing_jwt_authority_stops_the_api_from_starting()
    {
        await using var factory = new ApiFactory(
            environment: "Production",
            settings: new Dictionary<string, string?> { ["Authentication:JwtBearer:Authority"] = "" });

        Should.Throw<OptionsValidationException>(() => factory.CreateClient());
    }
}
