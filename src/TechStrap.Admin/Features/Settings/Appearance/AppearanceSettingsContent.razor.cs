using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using TechStrap.Admin.Clients;
using TechStrap.Contracts.Settings;
using TechStrap.Contracts.Skins;

namespace TechStrap.Admin.Features.Settings.Appearance;

/// <summary>Edits the versioned deployment default without discarding a draft on a failed or uncertain write.</summary>
public sealed partial class AppearanceSettingsContent : IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private SiteSettingsDto? _saved;
    private string _pack = SkinPacks.DefaultKey;
    private string? _error;
    private string? _notice;
    private bool _loading;
    private bool _busy;
    private bool _held;
    private bool _disposed;
    private bool _reloadConfirm;
    private bool _leaveConfirm;
    private bool _allowLeave;
    private string? _destination;
    private int _loadId;

    [Inject] private IAppearanceSettingsClient Settings { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;

    private bool Dirty => _saved is not null && _pack != _saved.DefaultPack;
    private bool CanSave => Dirty && !_busy && !_loading && !_held && SkinPacks.IsKnown(_pack);

    protected override Task OnInitializedAsync() => LoadAsync();

    private async Task LoadAsync()
    {
        var loadId = ++_loadId;
        _loading = true;
        _notice = null;
        try
        {
            var result = await Settings.GetAsync(_lifetime.Token);
            if (_disposed || loadId != _loadId) return;
            if (result.IsSuccess)
            {
                _saved = result.Value;
                _pack = result.Value.DefaultPack;
                _held = false;
                _error = null;
            }
            else
            {
                _error = "Appearance settings could not be loaded. Reload to try again; your changes are still here.";
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        finally { if (!_disposed && loadId == _loadId) _loading = false; }
    }

    private void OnPackChanged(ChangeEventArgs e)
    {
        if (_busy || _loading) return;
        _pack = e.Value?.ToString() ?? string.Empty;
        _notice = null;
        if (!SkinPacks.IsKnown(_pack)) _error = "Choose one of the five available theme packs.";
        else if (!_held) _error = null;
    }

    private async Task SaveAsync()
    {
        if (_busy || _loading || _held || _saved is null || !Dirty) return;
        if (!SkinPacks.IsKnown(_pack)) { _error = "Choose one of the five available theme packs."; return; }
        var request = new UpdateSiteSettingsRequest(_pack, _saved.Version);
        _busy = true;
        _error = _notice = null;
        try
        {
            // A dispatched write can commit even when the circuit leaves; do not retry or cancel it on navigation.
            var result = await Settings.UpdateAsync(request, CancellationToken.None);
            if (_disposed) return;
            if (result.IsSuccess)
            {
                _saved = result.Value;
                _pack = result.Value.DefaultPack;
                _notice = "Default portal theme saved.";
            }
            else
            {
                var error = result.Errors[0];
                _held = error.Code == ApiErrorCodes.ConcurrencyConflict || ApiErrorCodes.IsUncertainWrite(error.Code);
                _error = _held
                    ? "The settings may have changed. Reload saved settings before making another change; your theme choice is still here."
                    : error.Message;
            }
        }
        finally { if (!_disposed) _busy = false; }
    }

    private Task RequestReload()
    {
        if (_busy || _loading) return Task.CompletedTask;
        if (Dirty)
        {
            _reloadConfirm = true;
            return Task.CompletedTask;
        }
        return LoadAsync();
    }

    private void OnBeforeNavigation(LocationChangingContext context)
    {
        if (!Dirty || _allowLeave) return;
        context.PreventNavigation();
        _destination = context.TargetLocation;
        _leaveConfirm = true;
    }

    private async Task ConfirmDiscardAsync()
    {
        if (_reloadConfirm)
        {
            _reloadConfirm = false;
            await LoadAsync();
        }
        else if (_leaveConfirm && _destination is not null)
        {
            _leaveConfirm = false;
            _allowLeave = true;
            Navigation.NavigateTo(_destination);
        }
    }

    private void CancelDiscard()
    {
        _reloadConfirm = _leaveConfirm = false;
        _destination = null;
    }

    public void Dispose()
    {
        _disposed = true;
        _loadId++;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
