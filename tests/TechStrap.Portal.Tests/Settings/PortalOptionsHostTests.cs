using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TechStrap.Portal.Settings;
using TechStrap.Tests.Shared;

namespace TechStrap.Portal.Tests.Settings;

/// <summary>P09-T01 at the host: the settings are bound from their flat and sectioned keys, and a bad one stops the start.</summary>
public sealed class PortalOptionsHostTests
{
    [Fact]
    public async Task The_settings_are_bound_from_their_keys()
    {
        await using var factory = new PortalFactory("Production", new Dictionary<string, string?>
        {
            [PortalOptions.ApiBaseUrlKey] = "http://api",
            [PortalOptions.PublicUrlKey] = "https://support.example.com/",
            [PortalOptions.DefaultProductKey] = "paperplane",
        });
        using var client = factory.CreateClient();

        var options = factory.Services.GetRequiredService<IOptions<PortalOptions>>().Value;

        options.ApiBaseUri.ToString().ShouldBe("http://api/");
        options.PublicBaseUrl.ShouldBe("https://support.example.com");
        options.DefaultProduct.ShouldBe("paperplane");
    }

    [Theory]
    [InlineData("Production", PortalOptions.PublicUrlKey, "", PortalOptions.PublicUrlKey)]
    [InlineData("Production", PortalOptions.ApiBaseUrlKey, "", "API__BASEURL")]
    [InlineData("Development", PortalOptions.ApiBaseUrlKey, "", "API__BASEURL")]
    [InlineData("Development", PortalOptions.DefaultProductKey, "Not A Slug", PortalOptions.DefaultProductKey)]
    public async Task A_missing_or_malformed_setting_fails_the_start_and_names_the_key(string environment, string key, string value, string expectedInMessage)
    {
        await using var factory = new PortalFactory(environment, new Dictionary<string, string?> { [key] = value });

        var failure = StartupFailure.Capture(factory, () => factory.LogSink.Events);

        failure.Message.ShouldContain(expectedInMessage);
    }

    [Fact]
    public async Task The_public_url_may_be_blank_in_Development()
    {
        await using var factory = new PortalFactory("Development", new Dictionary<string, string?> { [PortalOptions.PublicUrlKey] = "" });

        Should.NotThrow(() => factory.CreateClient().Dispose());
    }
}
