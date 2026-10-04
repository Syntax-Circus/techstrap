using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SyntaxCircus.Blazor.Auth;
using TechStrap.Admin.Options;

namespace TechStrap.Admin.Tests.Options;

public sealed class AdminOptionsTests
{
    private static AuthOptions ValidAuth() => new()
    {
        Authority = "https://auth.example.com/application/o/techstrap-admin/",
        ClientId = "techstrap-admin",
        ClientSecret = "secret",
    };

    private static AdminAuthOptionsValidator Auth(string environment = "Production") => new(new FakeEnvironment(environment));

    [Fact]
    public void A_complete_oidc_configuration_is_valid() => Auth().Validate(null, ValidAuth()).Succeeded.ShouldBeTrue();

    [Theory]
    [InlineData("", "AUTH__AUTHORITY")]
    [InlineData("not a url", "AUTH__AUTHORITY")]
    [InlineData("/relative/path", "AUTH__AUTHORITY")]
    [InlineData("ftp://auth.example.com/", "AUTH__AUTHORITY")]
    public void A_missing_or_malformed_authority_names_its_variable(string authority, string variable)
    {
        var options = ValidAuth();
        options.Authority = authority;

        var result = Auth().Validate(null, options);

        result.Failed.ShouldBeTrue();
        result.FailureMessage!.ShouldContain(variable);
    }

    [Fact]
    public void An_http_authority_is_allowed_in_development_only()
    {
        var options = ValidAuth();
        options.Authority = "http://localhost:9000/application/o/techstrap-admin/";

        Auth("Development").Validate(null, options).Succeeded.ShouldBeTrue();
        Auth("Production").Validate(null, options).FailureMessage!.ShouldContain("https");
    }

    [Theory]
    [InlineData(nameof(AuthOptions.ClientId), "AUTH__CLIENTID")]
    [InlineData(nameof(AuthOptions.ClientSecret), "AUTH__CLIENTSECRET")]
    public void A_blank_client_setting_names_its_variable(string property, string variable)
    {
        var options = ValidAuth();
        typeof(AuthOptions).GetProperty(property)!.SetValue(options, "  ");

        Auth().Validate(null, options).FailureMessage!.ShouldContain(variable);
    }

    [Fact]
    public void The_scopes_must_keep_openid_and_offline_access_because_the_api_token_is_refreshed_with_the_refresh_token()
    {
        var options = ValidAuth();
        options.Scopes = ["openid", "profile", "email"];

        Auth().Validate(null, options).FailureMessage!.ShouldContain("offline_access");
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("api", false)]
    [InlineData("http://api/", true)]
    [InlineData("https://api.example.com", true)]
    public void The_api_base_url_must_be_absolute(string baseUrl, bool valid)
    {
        var result = new AdminApiOptionsValidator().Validate(null, new ApiOptions { BaseUrl = baseUrl });

        result.Succeeded.ShouldBe(valid);
        if (!valid)
        {
            result.FailureMessage!.ShouldContain("API__BASEURL");
        }
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(30, true)]
    [InlineData(300, true)]
    [InlineData(301, false)]
    public void The_api_timeout_is_bounded(int seconds, bool valid) =>
        new AdminApiOptionsValidator().Validate(null, new ApiOptions { BaseUrl = "http://api/", TimeoutSeconds = seconds }).Succeeded.ShouldBe(valid);

    [Fact]
    public void The_group_keys_default_to_the_api_defaults_and_must_differ_and_not_be_blank()
    {
        var validator = new AgentGroupOptionsValidator();

        validator.Validate(null, new AgentGroupOptions()).Succeeded.ShouldBeTrue();
        validator.Validate(null, new AgentGroupOptions { AdminGroup = "TECHSTRAP-AGENTS" }).FailureMessage!.ShouldContain("different groups");
        validator.Validate(null, new AgentGroupOptions { AgentGroup = " " }).FailureMessage!.ShouldContain("TECHSTRAP_AGENT_GROUP");
        validator.Validate(null, new AgentGroupOptions { AdminGroup = "" }).FailureMessage!.ShouldContain("TECHSTRAP_ADMIN_GROUP");
        validator.Validate(null, new AgentGroupOptions { GroupClaimType = "" }).FailureMessage!.ShouldContain("TECHSTRAP_GROUP_CLAIM_TYPE");
    }

    private sealed class FakeEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "TechStrap.Admin";

        public string ContentRootPath { get; set; } = string.Empty;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
