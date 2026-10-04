using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using TechStrap.Admin.Auth;
using TechStrap.Admin.Options;

namespace TechStrap.Admin.Tests.Support;

/// <summary>
/// The setup the settings pages need on top of <see cref="AdminComponentTest"/>: a signed-in session (an Admin unless the test calls <see cref="AsAgent"/> before it renders),
/// the services <c>NoAccessPage</c> needs when <c>AdminOnly</c> refuses a plain agent.
/// The session is built on first use, so a test says what it needs before the first render (services cannot be added once something has been resolved).
/// </summary>
public abstract class AdminPageTest : AdminComponentTest
{
    private bool _admin = true;

    protected AdminPageTest()
    {
        Services.AddSingleton(Microsoft.Extensions.Options.Options.Create(new AgentGroupOptions()));
        Services.AddSingleton<AntiforgeryStateProvider, NoTokenAntiforgeryStateProvider>();
        Services.AddSingleton(_ => CreateSession(_admin));
    }

    protected AgentSession Session => Services.GetRequiredService<AgentSession>();

    /// <summary>Builds the session on first use. A test that needs to see the calls the session makes (a reload) overrides this to build it over its own client.</summary>
    protected virtual AgentSession CreateSession(bool admin) => AgentSessions.SignedIn(admin);

    /// <summary>The page is opened by a plain Agent: <c>AdminOnly</c> refuses it and nothing may call the API.</summary>
    protected void AsAgent() => _admin = false;

    private sealed class NoTokenAntiforgeryStateProvider : AntiforgeryStateProvider
    {
        public override AntiforgeryRequestToken? GetAntiforgeryToken() => null;
    }
}
