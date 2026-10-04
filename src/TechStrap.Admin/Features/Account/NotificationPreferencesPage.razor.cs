using Microsoft.AspNetCore.Components;
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Shell;
using TechStrap.Contracts.Agents;

namespace TechStrap.Admin.Features.Account;

/// <summary>
/// My settings (any agent): which products email the agent about new tickets, the single-key shortcut switch and the theme (both kept in this browser through <see cref="PreferencesService"/>), and the
/// public display name. Every alert toggle sends the full set of products, so the API never has to guess about the ones left out, then shows a status message. A failed save puts the toggle back
/// and says nothing changed; a lost answer says the outcome is unknown and offers a reload. Toggles are inert while a save runs, so two saves never overlap. Writes use <see cref="CancellationToken.None"/>.
/// </summary>
public sealed partial class NotificationPreferencesPage : IDisposable
{
    private static readonly (ThemeChoice Value, string Label)[] ThemeChoices =
    [
        (ThemeChoice.Auto, MySettingsCopy.ThemeAuto),
        (ThemeChoice.Light, MySettingsCopy.ThemeLight),
        (ThemeChoice.Dark, MySettingsCopy.ThemeDark),
    ];

    private readonly CancellationTokenSource _lifetime = new();
    private IReadOnlyList<NotificationPreferenceDto> _prefs = [];
    private string? _prefsError;
    private string? _saveError;
    private string? _productDisplayName;
    private int _version;
    private int _loadId;
    private bool _loadingPrefs = true;
    private bool _saving;
    private bool _saveUncertain;
    private bool _disposed;

    [Inject]
    private IAgentsClient Agents { get; set; } = default!;

    [Inject]
    private IProductsClient Products { get; set; } = default!;

    [Inject]
    private PreferencesService Preferences { get; set; } = default!;

    [Inject]
    private StatusMessageService StatusMessages { get; set; } = default!;

    private static (ThemeChoice Value, string Label)[] Themes => ThemeChoices;

    protected override async Task OnInitializedAsync()
    {
        Preferences.Changed += OnPreferencesChanged;
        await Task.WhenAll(LoadPreferencesAsync(), LoadProductAsync());
    }

    private void OnPreferencesChanged() => _ = InvokeAsync(StateHasChanged);

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            // The browser's stored choices exist only on the client, so they are read after the first interactive render (the service loads once and never throws).
            await Preferences.LoadAsync();
        }
    }

    private async Task LoadPreferencesAsync()
    {
        // Only the latest load may change the screen: a slow answer that was overtaken (a reload while the first read runs) is ignored.
        var loadId = ++_loadId;
        _loadingPrefs = true;
        _prefsError = null;
        _saveError = null;
        _saveUncertain = false;
        try
        {
            var result = await Agents.GetNotificationPreferencesAsync(_lifetime.Token);
            if (_lifetime.IsCancellationRequested || loadId != _loadId)
            {
                return;
            }

            if (result.IsSuccess)
            {
                _prefs = result.Value;
                _version++;
            }
            else
            {
                _prefsError = $"{MySettingsCopy.AlertsLoadFailed} {result.Errors[0].Message}";
            }
        }
        finally
        {
            if (loadId == _loadId)
            {
                _loadingPrefs = false;
            }
        }
    }

    // The preview uses the first active product (by name, so the choice is stable). A failed lookup only means the generic preview.
    private async Task LoadProductAsync()
    {
        var result = await Products.ListAsync(_lifetime.Token);
        if (_lifetime.IsCancellationRequested || result.IsFailure)
        {
            return;
        }

        _productDisplayName = result.Value.Where(p => p.IsActive).OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).Select(p => p.Branding.DisplayName).FirstOrDefault();
    }

    private async Task ToggleAsync(NotificationPreferenceDto changed, bool value)
    {
        if (_saving || _saveUncertain)
        {
            // Swallowed: a new key makes Blazor draw the checkbox again from what is saved, so the click does not leave a tick the page never saved.
            _version++;
            return;
        }

        _saving = true;
        _saveError = null;
        try
        {
            // The full set, with the one change: nothing is left for the API to guess.
            var next = _prefs.Select(p => p.ProductId == changed.ProductId ? p with { NotifyNewTicket = value } : p).ToList();
            var request = new UpdateNotificationPreferencesRequest([.. next.Select(p => new NotificationPreferenceUpdateDto(p.ProductId, p.NotifyNewTicket))]);
            var result = await Agents.UpdateNotificationPreferencesAsync(request, CancellationToken.None);
            if (_disposed)
            {
                return;
            }

            if (result.IsSuccess)
            {
                _prefs = next;
                StatusMessages.Show(MySettingsCopy.AlertsSaved);
                return;
            }

            ShowSaveFailure(result.Errors[0]);
        }
        finally
        {
            _saving = false;
        }
    }

    private void ShowSaveFailure(ResultError error)
    {
        // A new key makes Blazor draw the checkbox again from what is saved, so a toggle the agent clicked does not stay ticked on screen when nothing was changed.
        _version++;
        if (ApiErrorCodes.IsUncertainWrite(error.Code))
        {
            _saveError = MySettingsCopy.AlertsUncertain;
            _saveUncertain = true;
        }
        else
        {
            _saveError = MySettingsCopy.AlertsFailed(error.Message);
        }
    }

    private Task OnKeyboardChangedAsync(ChangeEventArgs e) => Preferences.SetSingleKeyShortcutsAsync(e.Value is true);

    private Task OnThemeChangedAsync(ThemeChoice theme) => Preferences.SetThemeAsync(theme);

    public void Dispose()
    {
        Preferences.Changed -= OnPreferencesChanged;
        _disposed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
