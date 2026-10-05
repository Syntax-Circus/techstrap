using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
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
    private ILogger<MainLayout> Logger { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [Inject]
    private PreferencesService Preferences { get; set; } = default!;

    [Inject]
    private LocalTimeService LocalTime { get; set; } = default!;

    /// <summary>
    /// The page has <c>&lt;base href="/"&gt;</c>, so a bare <c>#main</c> would resolve to the home page. The link names the current address with the fragment replaced.
    /// <c>main</c> has <c>tabindex="-1"</c>, so following the fragment also moves focus.
    /// </summary>
    private string SkipLinkHref
    {
        get
        {
            var uri = Navigation.Uri;
            var hash = uri.IndexOf('#', StringComparison.Ordinal);
            return (hash < 0 ? uri : uri[..hash]) + "#main";
        }
    }

    protected override void OnInitialized()
    {
        Shortcuts.Pressed += OnShortcutAsync;
        Navigation.LocationChanged += OnLocationChanged;
    }

    private void OnLocationChanged(object? sender, Microsoft.AspNetCore.Components.Routing.LocationChangedEventArgs e) => _ = InvokeAsync(StateHasChanged);

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            // The stored preferences first: they set the theme and whether single-key shortcuts act. A script or storage failure is handled inside them, but anything else is caught below.
            try
            {
                await Preferences.LoadAsync();

                // The browser's time zone, so every time is drawn again in local time. It never throws for a script failure, and until it arrives every time is UTC.
                await LocalTime.LoadAsync();
                await Shortcuts.StartAsync();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The layout sits outside every error boundary, so an exception out of OnAfterRenderAsync would end the circuit. Without the listener the keyboard layer
                // is off but every page still works; it stays off for this circuit (this runs on the first render only). Only the type is logged, never the message.
                Logger.LogWarning("The keyboard shortcuts could not be started ({ExceptionType}).", ex.GetType().Name);
            }
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

    public void Dispose()
    {
        Shortcuts.Pressed -= OnShortcutAsync;
        Navigation.LocationChanged -= OnLocationChanged;
    }
}
