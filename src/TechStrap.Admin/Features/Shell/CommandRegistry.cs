using Microsoft.AspNetCore.Components;
using TechStrap.Admin.Components.Ui;
using TechStrap.Admin.Features.Queue;

namespace TechStrap.Admin.Features.Shell;

/// <summary>One line in the command palette.</summary>
/// <param name="Id">Stable and unique, for example <c>go-queue-mine</c>; it is the <c>@key</c> of the line and what tests name.</param>
/// <param name="Label">What the agent reads and types against: "Queue: Mine", "Reply to requester".</param>
/// <param name="Group">The kind of command (<see cref="PaletteCopy"/>), shown after the label and matched by the filter too.</param>
/// <param name="RunAsync">What the command does. The palette is closed before it runs, so a command that moves focus finds the page and not the dialog.</param>
/// <param name="Keys">The shortcut that does the same, when there is one, shown beside the label (UX-BRIEF-admin).</param>
/// <param name="AdminOnly">Never shown to, and never run for, an agent who is not an Admin. Hiding is not access control (the API refuses every admin call), but a plain agent never sees a page they cannot use.</param>
public sealed record PaletteCommand(string Id, string Label, string Group, Func<Task> RunAsync, string? Keys = null, bool AdminOnly = false);

/// <summary>
/// Every command the palette can offer (UX-BRIEF-admin, command palette). The navigation commands are built in; a screen that has commands of its own (a ticket) registers them with
/// <see cref="Register"/> while it is on screen and disposes the registration when it leaves. <see cref="Available"/> is the one place that decides who sees what: an admin command is
/// never in the list for an agent who is not an Admin. Scoped: one per circuit.
/// </summary>
public sealed class CommandRegistry(NavigationManager navigation)
{
    private readonly List<Registration> _registrations = [];

    /// <summary>The commands this agent may see, in a stable order: the built-in navigation, then the admin pages, then what screens registered.</summary>
    public IReadOnlyList<PaletteCommand> Available(bool isAdmin)
    {
        List<PaletteCommand> all = [.. BuiltIn(), .. _registrations.SelectMany(r => r.Commands)];
        return [.. all.Where(command => isAdmin || !command.AdminOnly)];
    }

    /// <summary>Adds a screen's commands until the returned registration is disposed. Registering again from the same screen means disposing the earlier registration first.</summary>
    public IDisposable Register(IReadOnlyList<PaletteCommand> commands)
    {
        var registration = new Registration(this, commands);
        _registrations.Add(registration);
        return registration;
    }

    /// <summary>The commands whose label or group contains every word of <paramref name="query"/> (case-insensitive). Blank is everything.</summary>
    public static IReadOnlyList<PaletteCommand> Filter(IReadOnlyList<PaletteCommand> commands, string? query)
    {
        var terms = (query ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return terms.Length == 0
            ? commands
            : [.. commands.Where(command => terms.All(term =>
                command.Label.Contains(term, StringComparison.OrdinalIgnoreCase) || command.Group.Contains(term, StringComparison.OrdinalIgnoreCase)))];
    }

    private IEnumerable<PaletteCommand> BuiltIn()
    {
        foreach (var view in QueueViews.All)
        {
            var path = $"/queue/{QueueViews.Slug(view)}";
            yield return new PaletteCommand($"go-queue-{QueueViews.Slug(view)}", $"{ShellCopy.QueueLink}: {view}", PaletteCopy.GoToGroup, () => Go(path));
        }

        yield return new PaletteCommand("go-my-settings", ShellCopy.MySettingsLink, PaletteCopy.GoToGroup, () => Go("/account/notifications"));
        yield return new PaletteCommand("go-products", ShellCopy.ProductsLink, PaletteCopy.AdminGroup, () => Go("/settings/products"), AdminOnly: true);
        yield return new PaletteCommand("go-agents", ShellCopy.AgentsLink, PaletteCopy.AdminGroup, () => Go("/settings/agents"), AdminOnly: true);
        yield return new PaletteCommand("go-tags", ShellCopy.TagsLink, PaletteCopy.AdminGroup, () => Go("/settings/tags"), AdminOnly: true);
        yield return new PaletteCommand("go-audit", ShellCopy.AuditLink, PaletteCopy.AdminGroup, () => Go("/settings/audit"), AdminOnly: true);
        yield return new PaletteCommand("go-failed-emails", ShellCopy.FailedEmailsLink, PaletteCopy.AdminGroup, () => Go("/ops/dead-letters"), AdminOnly: true);
    }

    private Task Go(string path)
    {
        navigation.NavigateTo(path);
        return Task.CompletedTask;
    }

    private sealed class Registration(CommandRegistry owner, IReadOnlyList<PaletteCommand> commands) : IDisposable
    {
        public IReadOnlyList<PaletteCommand> Commands { get; } = commands;

        public void Dispose() => owner._registrations.Remove(this);
    }
}
