using TechStrap.Tests.Shared;

namespace TechStrap.Admin.Tests;

/// <summary>The Admin refuses to start with a clear message when a required setting is missing, instead of failing on the first sign-in (P07-T01).</summary>
public sealed class AdminStartupSettingsTests
{
    [Theory]
    [InlineData("Auth:Authority", "AUTH__AUTHORITY")]
    [InlineData("Auth:ClientId", "AUTH__CLIENTID")]
    [InlineData("Auth:ClientSecret", "AUTH__CLIENTSECRET")]
    [InlineData("Api:BaseUrl", "API__BASEURL")]
    public async Task A_missing_required_setting_stops_the_start_and_names_the_variable(string key, string variable)
    {
        await using var factory = new AdminFactory(settings: new Dictionary<string, string?> { [key] = null });

        var error = StartupFailure.Capture(factory, () => factory.LogSink.Events);

        error.Message.ShouldContain(variable);
    }

    [Fact]
    public async Task Two_identical_group_names_stop_the_start()
    {
        await using var factory = new AdminFactory(settings: new Dictionary<string, string?>
        {
            ["TECHSTRAP_AGENT_GROUP"] = "staff",
            ["TECHSTRAP_ADMIN_GROUP"] = "Staff",
        });

        StartupFailure.Capture(factory, () => factory.LogSink.Events).Message.ShouldContain("different groups");
    }

    [Fact]
    public async Task With_the_required_settings_the_host_starts()
    {
        await using var factory = new AdminFactory();

        using var client = factory.CreateClient();

        (await client.GetAsync("/health/live", TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
    }
}
