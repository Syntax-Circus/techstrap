using Microsoft.AspNetCore.Components;
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Shell;

namespace TechStrap.Admin.Features.Ops.DeadLetters;

/// <summary>
/// The failed emails (Admin only): each row shows the masked recipient, the kind of email, a link to its ticket, the tries, the last error as a plain category and when it was created. Retry needs no confirmation
/// (it only puts the email back in the queue); Discard is a medium-tier confirmation. After either the list is read again and the navigation badge follows the total, so a retry that fails again shows up with its
/// new error. Writes use <see cref="CancellationToken.None"/> and never retry; an unknown outcome says so and offers a reload. One action runs at a time.
/// </summary>
public sealed partial class DeadLettersContent : IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private IReadOnlyList<DeadLetterRowViewModel> _rows = [];
    private DeadLetterRowViewModel? _discarding;
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
    private IDeadLettersClient DeadLetters { get; set; } = default!;

    [Inject]
    private FailedEmailCounter Failed { get; set; } = default!;

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

    private async Task LoadAsync()
    {
        // Only the latest load may change the screen: a slow answer for an earlier page is ignored.
        var loadId = ++_loadId;
        var redirecting = false;
        _loading = true;
        _error = null;
        try
        {
            var result = await DeadLetters.ListAsync(_page, DeadLettersCopy.PageSize, _lifetime.Token);
            if (_lifetime.IsCancellationRequested || loadId != _loadId)
            {
                return;
            }

            if (result.IsSuccess)
            {
                _rows = [.. result.Value.Items.Select(DeadLetterRowViewModel.From)];
                _total = result.Value.TotalCount;

                // The list already holds the total, so the badge in the navigation follows it without a second call.
                Failed.Set(_total);

                // A page past the end (the last row of the last page was discarded, or an old address): go to the last page that has rows instead of saying there are none.
                var lastPage = Math.Max(1, (_total + DeadLettersCopy.PageSize - 1) / DeadLettersCopy.PageSize);
                if (_rows.Count == 0 && _total > 0 && _page > lastPage)
                {
                    redirecting = true;
                    Navigation.NavigateTo(lastPage <= 1 ? "/ops/dead-letters" : $"/ops/dead-letters?page={lastPage}", replace: true);
                }
            }
            else
            {
                _error = $"{DeadLettersCopy.LoadFailed} {result.Errors[0].Message}";
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

    private void OnPageChanged(int page) => Navigation.NavigateTo(page <= 1 ? "/ops/dead-letters" : $"/ops/dead-letters?page={page}");

    // ---- retry: no confirmation, once ----------------------------------------------------------------------------

    private async Task RetryAsync(DeadLetterRowViewModel row)
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        _rowError = null;
        _rowUncertain = false;
        try
        {
            var result = await DeadLetters.RetryAsync(row.Id, CancellationToken.None);
            if (_disposed)
            {
                return;
            }

            if (result.IsSuccess)
            {
                StatusMessages.Show(DeadLettersCopy.Retried(row.Kind));
                await LoadAsync();
            }
            else
            {
                await ShowRowFailureAsync(result.Errors[0]);
            }
        }
        finally
        {
            _busy = false;
        }
    }

    // ---- discard: a confirmation ---------------------------------------------------------------------------------

    private void AskDiscard(DeadLetterRowViewModel row)
    {
        _dialogError = null;
        _uncertain = false;
        _rowError = null;
        _discarding = row;
    }

    private void CancelDiscard()
    {
        if (!_busy)
        {
            _discarding = null;
            _dialogError = null;
            _uncertain = false;
        }
    }

    private async Task ConfirmDiscardAsync()
    {
        if (_busy || _discarding is not { } row)
        {
            return;
        }

        _busy = true;
        _dialogError = null;
        _uncertain = false;
        StateHasChanged();
        try
        {
            var result = await DeadLetters.DiscardAsync(row.Id, CancellationToken.None);
            if (_disposed)
            {
                return;
            }

            if (result.IsSuccess)
            {
                _discarding = null;
                StatusMessages.Show(DeadLettersCopy.Discarded(row.Kind));
                await LoadAsync();
                return;
            }

            await ShowDiscardFailureAsync(result.Errors[0]);
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task ShowDiscardFailureAsync(ResultError error)
    {
        if (error.Code is ApiErrorCodes.OutboxNotFound or ApiErrorCodes.OutboxNotDeadLettered)
        {
            _discarding = null;
            StatusMessages.Show(error.Code == ApiErrorCodes.OutboxNotFound ? DeadLettersCopy.Gone : DeadLettersCopy.AlreadyHandled);
            await LoadAsync();
        }
        else if (ApiErrorCodes.IsUncertainWrite(error.Code))
        {
            _dialogError = DeadLettersCopy.DiscardUncertain;
            _uncertain = true;
        }
        else
        {
            _dialogError = DeadLettersCopy.DiscardFailed(error.Message);
        }
    }

    private async Task ShowRowFailureAsync(ResultError error)
    {
        if (error.Code is ApiErrorCodes.OutboxNotFound or ApiErrorCodes.OutboxNotDeadLettered)
        {
            StatusMessages.Show(error.Code == ApiErrorCodes.OutboxNotFound ? DeadLettersCopy.Gone : DeadLettersCopy.AlreadyHandled);
            await LoadAsync();
        }
        else if (ApiErrorCodes.IsUncertainWrite(error.Code))
        {
            _rowError = DeadLettersCopy.RetryUncertain;
            _rowUncertain = true;
        }
        else
        {
            _rowError = DeadLettersCopy.RetryFailed(error.Message);
        }
    }

    private async Task ReloadAfterUncertainAsync()
    {
        _discarding = null;
        _dialogError = null;
        _uncertain = false;
        await ReloadAsync();
    }

    public void Dispose()
    {
        _disposed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
