using Microsoft.AspNetCore.Components;
using TechStrap.Admin.Features.Shell;

namespace TechStrap.Admin.Components.Layout;

/// <summary>
/// The frame around every page: rail, content, status bar, and the keyboard layer's two global jobs: start the key listener once per
/// circuit, and answer the shortcuts no page owns (help, and "/" from a screen that has no search box).
/// </summary>
public partial class MainLayout : IDisposable
{
    private const string QueuePath = "queue";

    private bool _helpOpen;

    [Inject]
    private ShortcutService Shortcuts { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    protected override void OnInitialized() => Shortcuts.Pressed += OnShortcutAsync;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await Shortcuts.StartAsync();
        }
    }

    private Task OnShortcutAsync(ShortcutAction action)
    {
        switch (action)
        {
            case ShortcutAction.Help:
                _helpOpen = true;
                return InvokeAsync(StateHasChanged);
            case ShortcutAction.FocusSearch when !IsOnQueue():
                Navigation.NavigateTo("/" + QueuePath);
                break;
        }

        return Task.CompletedTask;
    }

    private bool IsOnQueue()
    {
        var path = Navigation.ToBaseRelativePath(Navigation.Uri);
        var end = path.IndexOfAny(['?', '#']);
        path = end < 0 ? path : path[..end];
        return path.Length == 0 || path.StartsWith(QueuePath, StringComparison.OrdinalIgnoreCase);
    }

    private void CloseHelp() => _helpOpen = false;

    public void Dispose() => Shortcuts.Pressed -= OnShortcutAsync;
}
