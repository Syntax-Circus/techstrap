using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Admin.Auth;
using TechStrap.Admin.Clients;
using TechStrap.Contracts.Agents;

namespace TechStrap.Admin.Tests.Support;

/// <summary>
/// Builds the <see cref="AgentSession"/> a component test needs, over a substitute <see cref="IAgentsClient"/>. This is the only test code that
/// knows how a session is constructed and loaded, so a change to <see cref="AgentSession"/> touches one file.
/// </summary>
internal static class AgentSessions
{
    public static readonly Guid SamId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    /// <summary>A session whose <c>GET /api/agents/me</c> succeeded for Sam, an Agent, or Ada, an Admin.</summary>
    public static async Task<AgentSession> SignedInAsync(bool admin = false)
    {
        var agents = Substitute.For<IAgentsClient>();
        var me = new AgentDto(
            admin ? Guid.Parse("22222222-2222-2222-2222-222222222222") : SamId,
            admin ? "Ada Admin" : "Sam Ortiz",
            admin ? "ada@example.com" : "sam@example.com",
            admin ? AgentRoles.Admin : AgentRoles.Agent,
            IsActive: true,
            PublicDisplayName: null,
            LastSeenAt: null);
        agents.GetMeAsync(Arg.Any<CancellationToken>()).Returns(Result<AgentDto>.Success(me));
        var session = new AgentSession(agents);
        await session.EnsureLoadedAsync(CancellationToken.None);
        return session;
    }

    /// <summary>
    /// The same as <see cref="SignedInAsync"/> for a constructor, where there is nothing to await. The substitute client answers synchronously, so the
    /// load has completed by the time this returns.
    /// </summary>
    public static AgentSession SignedIn(bool admin = false) => SignedInAsync(admin).GetAwaiter().GetResult();
}
