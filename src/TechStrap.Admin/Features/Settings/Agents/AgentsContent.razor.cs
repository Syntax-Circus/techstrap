using Microsoft.AspNetCore.Components;
using SyntaxCircus.Common;
using TechStrap.Admin.Auth;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Shell;

namespace TechStrap.Admin.Features.Settings.Agents;

/// <summary>
/// The agents (Admin only): who has access, with the role as a read-only badge (roles come from the identity provider's groups, D-041) and one action, activate or deactivate. Activating needs no
/// confirmation; deactivating is a medium-tier confirmation that names the agent. A 409 <c>last-active-admin</c> is shown inside the dialog, in the API's words, and changes nothing. Writes use
/// <see cref="CancellationToken.None"/>, never retry, and say so when the outcome is unknown. When an admin deactivates their own account the session is asked again, so the shell shows what the API now says.
/// </summary>
public sealed partial class AgentsContent : IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();

    // Agents whose activate or deactivate ended with an unknown outcome: held (asking again shows the uncertain copy and sends nothing) until the list has been read again successfully.
    private readonly UncertainMarks _uncertainIds = new();
    private IReadOnlyList<AgentRowViewModel> _rows = [];
    private AgentRowViewModel? _deactivating;
    private string? _error;
    private string? _rowError;
    private string? _dialogError;
    private int _page = 1;
    private int _total;
    private int _loadedPage;
    private int _loadId;
    private bool _loading = true;
    private bool _busy;
    private bool _uncertain;
    private bool _rowUncertain;
    private bool _disposed;

    [Inject]
    private IAgentsClient Agents { get; set; } = default!;

    [Inject]
    private AgentSession Session { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [Inject]
    private StatusMessageService StatusMessages { get; set; } = default!;

    [SupplyParameterFromQuery(Name = "page")]
    public string? PageNumber { get; set; }

    protected override async Task OnParametersSetAsync()
    {
        _page = int.TryParse(PageNumber, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var page) && page > 1 ? page : 1;
        if (_loadedPage != _page)
        {
            _loadedPage = _page;
            await LoadAsync();
        }
    }

    private bool IsSelf(AgentRowViewModel row) => Session.Agent?.Id == row.Id;

    private async Task LoadAsync()
    {
        // Only the latest load may change the screen: a slow answer for an earlier page is ignored.
        var loadId = ++_loadId;
        var redirecting = false;
        _loading = true;
        _error = null;
        try
        {
            var result = await Agents.ListPageAsync(_page, AgentsCopy.PageSize, _lifetime.Token);
            if (_lifetime.IsCancellationRequested || loadId != _loadId)
            {
                return;
            }

            if (result.IsSuccess)
            {
                _rows = [.. result.Value.Items.Select(AgentRowViewModel.From)];
                _total = result.Value.TotalCount;
                _uncertainIds.ReleaseForLoad(loadId);

                // A page past the end (an old address, or the last row of the last page went away): go to the last page that has rows instead of saying there are none.
                var lastPage = Math.Max(1, (_total + AgentsCopy.PageSize - 1) / AgentsCopy.PageSize);
                if (_rows.Count == 0 && _total > 0 && _page > lastPage)
                {
                    redirecting = true;
                    Navigation.NavigateTo(lastPage <= 1 ? "/settings/agents" : $"/settings/agents?page={lastPage}", replace: true);
                }
            }
            else
            {
                _error = $"{AgentsCopy.LoadFailed} {result.Errors[0].Message}";
            }
        }
        finally
        {
            if (loadId == _loadId && !redirecting)
            {
                // While redirecting to the last page the next load is already due: keep the loading state, never the empty one.
                _loading = false;
            }
        }
    }

    private async Task ReloadAsync()
    {
        _rowError = null;
        _rowUncertain = false;
        await LoadAsync();
    }

    private void OnPageChanged(int page) => Navigation.NavigateTo(page <= 1 ? "/settings/agents" : $"/settings/agents?page={page}");

    // ---- activate: no confirmation -------------------------------------------------------------------------------

    private async Task ActivateAsync(AgentRowViewModel row)
    {
        if (_busy)
        {
            return;
        }

        if (_uncertainIds.Contains(row.Id))
        {
            _rowError = AgentsCopy.ActivateUncertain(row.DisplayName);
            _rowUncertain = true;
            return;
        }

        _busy = true;
        _rowError = null;
        _rowUncertain = false;
        try
        {
            var result = await Agents.SetActiveAsync(row.Id, true, CancellationToken.None);
            if (_disposed)
            {
                return;
            }

            if (result.IsSuccess)
            {
                Replace(row, result.Value);
                StatusMessages.Show(AgentsCopy.Activated(row.DisplayName));
            }
            else if (result.Errors[0].Code == ApiErrorCodes.AgentNotFound)
            {
                StatusMessages.Show(AgentsCopy.AgentGone);
                await LoadAsync();
            }
            else if (ApiErrorCodes.IsUncertainWrite(result.Errors[0].Code))
            {
                _rowError = AgentsCopy.ActivateUncertain(row.DisplayName);
                _rowUncertain = true;
                _uncertainIds.Add(row.Id, _loadId);
            }
            else
            {
                _rowError = AgentsCopy.ActivateFailed(row.DisplayName, result.Errors[0].Message);
            }
        }
        finally
        {
            _busy = false;
        }
    }

    // ---- deactivate: a confirmation naming the agent -------------------------------------------------------------

    private void AskDeactivate(AgentRowViewModel row)
    {
        _rowError = null;
        _deactivating = row;
        _uncertain = _uncertainIds.Contains(row.Id);
        _dialogError = _uncertain ? AgentsCopy.DeactivateUncertain : null;
    }

    private void CancelDeactivate()
    {
        if (!_busy)
        {
            _deactivating = null;
            _dialogError = null;
            _uncertain = false;
        }
    }

    private async Task ConfirmDeactivateAsync()
    {
        if (_busy || _deactivating is not { } row || _uncertainIds.Contains(row.Id))
        {
            return;
        }

        _busy = true;
        _dialogError = null;
        _uncertain = false;
        StateHasChanged();
        try
        {
            var result = await Agents.SetActiveAsync(row.Id, false, CancellationToken.None);
            if (_disposed)
            {
                return;
            }

            if (result.IsSuccess)
            {
                Replace(row, result.Value);
                _deactivating = null;
                StatusMessages.Show(AgentsCopy.Deactivated(row.DisplayName));
                if (IsSelf(row))
                {
                    // The API now refuses this account: ask again, so the shell shows the no-access page instead of a working app.
                    await Session.ReloadAsync(CancellationToken.None);
                }

                return;
            }

            await ShowDeactivateFailureAsync(result.Errors[0], row.Id);
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task ShowDeactivateFailureAsync(ResultError error, Guid id)
    {
        if (error.Code == ApiErrorCodes.AgentNotFound)
        {
            _deactivating = null;
            StatusMessages.Show(AgentsCopy.AgentGone);
            await LoadAsync();
        }
        else if (ApiErrorCodes.IsUncertainWrite(error.Code))
        {
            _dialogError = AgentsCopy.DeactivateUncertain;
            _uncertain = true;
            _uncertainIds.Add(id, _loadId);
        }
        else if (error.Code == ApiErrorCodes.LastActiveAdmin)
        {
            // The API's own sentence says what to do (make another admin active first); nothing was changed.
            _dialogError = error.Message;
        }
        else
        {
            _dialogError = AgentsCopy.DeactivateFailed(error.Message);
        }
    }

    private async Task ReloadAfterUncertainAsync()
    {
        _deactivating = null;
        _dialogError = null;
        _uncertain = false;
        await LoadAsync();
    }

    private void Replace(AgentRowViewModel row, TechStrap.Contracts.Agents.AgentDto agent) =>
        _rows = [.. _rows.Select(r => r.Id == row.Id ? r.With(agent) : r)];

    public void Dispose()
    {
        _disposed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
