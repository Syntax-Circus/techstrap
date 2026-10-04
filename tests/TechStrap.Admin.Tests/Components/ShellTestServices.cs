using System.Security.Claims;
using Bunit;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Admin.Auth;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Options;
using TechStrap.Contracts.Agents;

namespace TechStrap.Admin.Tests.Components;

/// <summary>What the shell (MainLayout, AgentGate, NoAccessPage, sign-out form) needs in a bUnit test: authorization services, the session and its client, the group options.</summary>
internal static class ShellTestServices
{
    /// <summary>Registers the shell services with a substitute <see cref="IAgentsClient"/> that accepts the agent, and returns it so a test can change the answer.</summary>
    public static IAgentsClient AddAgentShell(this BunitContext context, string role = AgentRoles.Agent)
    {
        var agents = Substitute.For<IAgentsClient>();
        agents.GetMeAsync(Arg.Any<CancellationToken>()).Returns(Result<AgentDto>.Success(new AgentDto(Guid.NewGuid(), "Sam", "sam@orbitly.test", role, true, null, null)));
        context.AddAuthorization().SetAuthorized("Sam Agent");
        context.Services.AddSingleton(agents);
        context.Services.AddSingleton<AgentSession>();
        context.Services.AddSingleton(Microsoft.Extensions.Options.Options.Create(new AgentGroupOptions()));
        context.Services.AddSingleton<AntiforgeryStateProvider, NoTokenAntiforgeryStateProvider>();
        return agents;
    }

    /// <summary>The cascading authentication state a router would provide, for a signed-in agent or an anonymous visitor.</summary>
    public static ComponentParameterCollectionBuilder<TComponent> SignedIn<TComponent>(this ComponentParameterCollectionBuilder<TComponent> parameters, bool signedIn = true)
        where TComponent : Microsoft.AspNetCore.Components.IComponent =>
        parameters.AddCascadingValue(Task.FromResult(new AuthenticationState(
            signedIn ? new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "s1"), new Claim("name", "Sam Agent")], "Test", "name", "roles")) : new ClaimsPrincipal(new ClaimsIdentity()))));

    private sealed class NoTokenAntiforgeryStateProvider : AntiforgeryStateProvider
    {
        public override AntiforgeryRequestToken? GetAntiforgeryToken() => null;
    }
}
