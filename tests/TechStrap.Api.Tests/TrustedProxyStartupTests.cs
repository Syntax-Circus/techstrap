namespace TechStrap.Api.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TrustedProxyEnvironmentCollection
{
    public const string Name = "TrustedProxy environment variables";
}

/// <summary>
/// Production must fail to start without trusted-proxy configuration, and start with it. TrustedProxy
/// binds eagerly, so the positive case sets a process environment variable; these tests therefore
/// run alone (DisableParallelization) so the variable cannot leak into other hosts.
/// </summary>
[Collection(TrustedProxyEnvironmentCollection.Name)]
public sealed class TrustedProxyStartupTests
{
    private const string TrustedNetworkVariable = "TrustedProxy__TrustedNetworks__0";

    [Fact]
    public async Task Production_without_trusted_proxy_configuration_fails_startup()
    {
        Environment.SetEnvironmentVariable(TrustedNetworkVariable, null);
        await using var factory = new ApiFactory(environment: "Production");

        var exception = Record.Exception(() => factory.CreateClient());

        exception.ShouldNotBeNull();
        exception.ToString().ShouldContain("TrustedProxy");
    }

    [Fact]
    public async Task Production_with_a_trusted_network_starts()
    {
        Environment.SetEnvironmentVariable(TrustedNetworkVariable, "10.20.30.0/24");
        try
        {
            await using var factory = new ApiFactory(environment: "Production");
            using var client = factory.CreateClient();

            var response = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);

            response.IsSuccessStatusCode.ShouldBeTrue();
        }
        finally
        {
            Environment.SetEnvironmentVariable(TrustedNetworkVariable, null);
        }
    }

    [Fact]
    public async Task Development_uses_the_checked_in_default_network_and_starts()
    {
        Environment.SetEnvironmentVariable(TrustedNetworkVariable, null);
        await using var factory = new ApiFactory(environment: "Development");
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);

        response.IsSuccessStatusCode.ShouldBeTrue();
    }
}
