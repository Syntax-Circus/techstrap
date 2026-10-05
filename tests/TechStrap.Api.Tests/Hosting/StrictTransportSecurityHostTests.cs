using System.Net;

namespace TechStrap.Api.Tests.Hosting;

/// <summary>
/// <c>Strict-Transport-Security</c> is sent outside Development only: a browser that was sent it for localhost would refuse plain http on that host for the length of the policy.
/// A Production host will not start without trusted-proxy configuration, which can only be given through the process environment (it binds before host settings exist), so the
/// class runs in the non-parallel <see cref="ProcessEnvironmentCollection"/>.
/// </summary>
[Collection(ProcessEnvironmentCollection.Name)]
public sealed class StrictTransportSecurityHostTests
{
    private const string TrustedNetworkVariable = "TrustedProxy__TrustedNetworks__0";

    private static async Task<bool> SendsHstsAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return response.Headers.Contains("Strict-Transport-Security");
    }

    [Theory]
    [InlineData("Development", false)]
    [InlineData("Production", true)]
    public async Task The_Admin_sends_Strict_Transport_Security_only_outside_Development(string environment, bool expected)
    {
        Environment.SetEnvironmentVariable(TrustedNetworkVariable, "10.20.30.0/24");
        try
        {
            await using var factory = new AdminFactory(environment);
            using var client = factory.CreateClient();
            (await SendsHstsAsync(client, "/signin")).ShouldBe(expected);
        }
        finally
        {
            Environment.SetEnvironmentVariable(TrustedNetworkVariable, null);
        }
    }

    [Theory]
    [InlineData("Development", false)]
    [InlineData("Production", true)]
    public async Task The_Portal_sends_Strict_Transport_Security_only_outside_Development(string environment, bool expected)
    {
        Environment.SetEnvironmentVariable(TrustedNetworkVariable, "10.20.30.0/24");
        try
        {
            await using var factory = new PortalFactory(environment);
            using var client = factory.CreateClient();
            (await SendsHstsAsync(client, "/")).ShouldBe(expected);
        }
        finally
        {
            Environment.SetEnvironmentVariable(TrustedNetworkVariable, null);
        }
    }
}
