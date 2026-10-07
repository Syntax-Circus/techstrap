using TechStrap.Contracts.Live;

namespace TechStrap.Admin.Features.Live;

/// <summary>One line of the presence bar: who, whether they are typing a reply, and the words to show. A feature-local view model: the Contracts DTO is never drawn directly.</summary>
public sealed record PresenceViewModel(Guid AgentId, string Name, bool IsReplying, string Text);

/// <summary>The words of the presence bar.</summary>
public static class PresenceCopy
{
    public const string Label = "Who else is on this ticket";

    /// <summary>Used when a viewer has no name at all (the server falls back to the email, so this is a last resort).</summary>
    public const string AnotherAgent = "Another agent";

    public const string Viewing = "is viewing";
    public const string Replying = "is replying";
}

/// <summary>
/// Builds the presence bar's lines from what the hub reports (D-046). The hub reports every agent on the ticket, the signed-in one included, so the agent is removed by id here: nobody sees themselves.
/// Repliers come first, then viewers, each group by name. An agent listed twice is one line and "replying" wins. A "replying" hint the hub has not refreshed within its lease is shown as "viewing" when
/// <paramref name="composingExpired"/> says so (the hub does not push the lapse).
/// </summary>
public static class PresenceViewModelFactory
{
    public static IReadOnlyList<PresenceViewModel> Create(TicketPresenceDto? presence, Guid? selfAgentId, bool composingExpired = false)
    {
        if (presence is null)
        {
            return [];
        }

        return
        [
            .. presence.Viewers
                .Where(viewer => selfAgentId is not { } self || viewer.AgentId != self)
                .GroupBy(viewer => viewer.AgentId)
                .Select(group => group.OrderByDescending(viewer => IsComposing(viewer)).First())
                .Select(viewer =>
                {
                    var name = string.IsNullOrWhiteSpace(viewer.DisplayName) ? PresenceCopy.AnotherAgent : viewer.DisplayName.Trim();
                    var replying = IsComposing(viewer) && !composingExpired;
                    return new PresenceViewModel(viewer.AgentId, name, replying, $"{name} {(replying ? PresenceCopy.Replying : PresenceCopy.Viewing)}");
                })
                .OrderByDescending(model => model.IsReplying)
                .ThenBy(model => model.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(model => model.AgentId),
        ];
    }

    /// <summary>True when any viewer other than <paramref name="selfAgentId"/> is replying, so the page knows whether a lease timer is needed.</summary>
    public static bool AnyoneReplying(TicketPresenceDto? presence, Guid? selfAgentId) =>
        presence?.Viewers.Any(viewer => IsComposing(viewer) && (selfAgentId is not { } self || viewer.AgentId != self)) == true;

    private static bool IsComposing(TicketViewerDto viewer) => viewer.State == TicketPresenceStates.Composing;
}
