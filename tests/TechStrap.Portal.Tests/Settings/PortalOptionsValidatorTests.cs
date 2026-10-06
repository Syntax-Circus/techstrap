using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;
using TechStrap.Portal.Settings;

namespace TechStrap.Portal.Tests.Settings;

/// <summary>P09-T01: the Portal fails at start, naming the key, when a required setting is missing or malformed.</summary>
public sealed class PortalOptionsValidatorTests
{
    private static ValidateOptionsResult Validate(PortalOptions options, string environment = "Production")
    {
        var host = Substitute.For<IHostEnvironment>();
        host.EnvironmentName.Returns(environment);
        return new PortalOptionsValidator(host).Validate(null, options);
    }

    private static PortalOptions Valid() => new() { ApiBaseUrl = "http://api/", PublicUrl = "https://support.example.com" };

    [Fact]
    public void A_complete_set_of_settings_is_valid_in_Production()
    {
        Validate(Valid()).Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("api")]
    [InlineData("/api/")]
    [InlineData("ftp://api/")]
    [InlineData("javascript:alert(1)")]
    [InlineData("http://user:pw@api/")]
    [InlineData("http://api/?x=1")]
    [InlineData("http://api/#frag")]
    [InlineData("http://api/?")]
    [InlineData("http://api/#")]
    public void The_api_address_must_be_an_absolute_http_or_https_url_and_the_failure_names_the_key(string value)
    {
        var options = Valid();
        options.ApiBaseUrl = value;

        var result = Validate(options);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("Api:BaseUrl");
        result.FailureMessage.ShouldContain("API__BASEURL");
    }

    [Theory]
    [InlineData("http://api/")]
    [InlineData("https://api.example.com/")]
    [InlineData("http://api")]
    public void An_http_or_https_api_address_is_valid(string value)
    {
        var options = Valid();
        options.ApiBaseUrl = value;

        Validate(options).Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData("Production", "")]
    [InlineData("Production", "  ")]
    [InlineData("Staging", "")]
    [InlineData("Staging", "  ")]
    public void The_public_url_is_required_outside_Development(string environment, string value)
    {
        var options = Valid();
        options.PublicUrl = value;

        var result = Validate(options, environment);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("TECHSTRAP_PORTAL_PUBLIC_URL");
    }

    [Fact]
    public void The_public_url_may_be_blank_in_Development()
    {
        var options = Valid();
        options.PublicUrl = "";

        Validate(options, "Development").Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData("support.example.com")]
    [InlineData("/relative")]
    [InlineData("ftp://support.example.com")]
    [InlineData("https://support.example.com/?x=1")]
    [InlineData("https://support.example.com/#top")]
    [InlineData("https://support.example.com?")]
    [InlineData("https://user:pw@support.example.com")]
    public void A_public_url_that_is_not_an_absolute_http_base_is_refused_in_every_environment(string value)
    {
        foreach (var environment in new[] { "Development", "Production" })
        {
            var options = Valid();
            options.PublicUrl = value;

            var result = Validate(options, environment);

            result.Failed.ShouldBeTrue($"{value} in {environment}");
            result.FailureMessage.ShouldContain("TECHSTRAP_PORTAL_PUBLIC_URL");
        }
    }

    [Theory]
    [InlineData("https://support.example.com")]
    [InlineData("https://support.example.com/")]
    [InlineData("http://localhost:8082")]
    [InlineData("https://example.com/portal")]
    public void A_public_url_base_is_valid(string value)
    {
        var options = Valid();
        options.PublicUrl = value;

        Validate(options).Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("paperplane")]
    [InlineData("paper-plane-2")]
    public void The_default_product_is_optional_and_a_slug_when_given(string? value)
    {
        var options = Valid();
        options.DefaultProduct = value;

        Validate(options).Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData("Paper")]
    [InlineData("paper plane")]
    [InlineData("paper/plane")]
    [InlineData("-paper")]
    [InlineData("paper-")]
    [InlineData("paper--plane")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void A_default_product_that_is_not_a_slug_is_refused_and_the_failure_names_the_key(string value)
    {
        var options = Valid();
        options.DefaultProduct = value;

        var result = Validate(options);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("TECHSTRAP_PORTAL_DEFAULT_PRODUCT");
    }

    [Fact]
    public void Every_failure_is_reported_at_once()
    {
        var result = Validate(new PortalOptions { ApiBaseUrl = "", PublicUrl = "", DefaultProduct = "Bad Key" });

        result.Failures.ShouldNotBeNull();
        result.Failures.Count().ShouldBe(3);
    }

    [Fact]
    public void The_normalised_addresses_drop_a_trailing_slash_from_the_public_url_and_add_one_to_the_api_address()
    {
        var options = new PortalOptions { ApiBaseUrl = "http://api", PublicUrl = "https://support.example.com/" };

        options.ApiBaseUri.ToString().ShouldBe("http://api/");
        options.PublicBaseUrl.ShouldBe("https://support.example.com");
        new PortalOptions { PublicUrl = "" }.PublicBaseUrl.ShouldBe("");
    }
}
