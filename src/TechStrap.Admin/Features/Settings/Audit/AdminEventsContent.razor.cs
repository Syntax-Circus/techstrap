using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using TechStrap.Admin.Clients;
using TechStrap.Contracts.Agents;

namespace TechStrap.Admin.Features.Settings.Audit;

/// <summary>
/// The audit log (Admin only, read-only): admin events newest first, 25 to a page, filtered by what changed (subject type) and who did it (an agent), both kept in the query string so a view can be linked.
/// The first page fixes an <c>asOf</c> time and every later page of the same filter carries it, so events recorded meanwhile do not shift the pages. Unknown filter values in the address are dropped, never sent.
/// Each event is shown as one sentence from <see cref="AdminEventSummaryFactory"/>; a payload is never rendered.
/// </summary>
public sealed partial class AdminEventsContent : IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private IReadOnlyList<AdminEventRowViewModel> _rows = [];
    private IReadOnlyList<AgentListItemDto> _agents = [];
    private (string? Subject, Guid? Actor, int Page)? _loadedFor;
    private (string? Subject, Guid? Actor)? _filterOfAsOf;
    private DateTimeOffset? _asOf;
    private string? _subject;
    private Guid? _actor;
    private string? _error;
    private string _announcement = string.Empty;
    private int _page = 1;
    private int _total;
    private bool _loading = true;
    private bool _agentsLoaded;
    private int _loadId;

    [Inject]
    private IAdminEventsClient Events { get; set; } = default!;

    [Inject]
    private IAgentsClient AgentsClient { get; set; } = default!;

    [Inject]
    private ILogger<AdminEventsContent> Logger { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [Inject]
    private TimeProvider Time { get; set; } = default!;

    [SupplyParameterFromQuery(Name = AuditCopy.SubjectKey)]
    public string? Subject { get; set; }

    [SupplyParameterFromQuery(Name = AuditCopy.ActorKey)]
    public string? Actor { get; set; }

    [SupplyParameterFromQuery(Name = AuditCopy.PageKey)]
    public string? PageNumber { get; set; }

    protected override async Task OnParametersSetAsync()
    {
        _subject = AuditFilters.CanonicalSubject(Subject);
        _actor = Guid.TryParse(Actor, out var actor) ? actor : null;
        _page = int.TryParse(PageNumber, NumberStyles.None, CultureInfo.InvariantCulture, out var page) && page > 1 ? page : 1;

        // Both reads start now, but only the events are awaited: they render the moment they arrive and never wait for the names of the filter, which draw when their own read finishes.
        if (!_agentsLoaded)
        {
            _agentsLoaded = true;
            _ = LoadAgentsAsync();
        }

        var key = (_subject, _actor, _page);
        if (_loadedFor != key)
        {
            _loadedFor = key;
            await LoadAsync();
        }
    }

    // The filter's actor list, read in the background. If it cannot be read the filter simply has no names to offer; the log itself still works.
    private async Task LoadAgentsAsync()
    {
        try
        {
            var result = await AgentsClient.ListAllAsync(_lifetime.Token);
            if (_lifetime.IsCancellationRequested || !result.IsSuccess)
            {
                return;
            }

            _agents = [.. result.Value.OrderBy(a => a.Name ?? a.DisplayLabel, StringComparer.OrdinalIgnoreCase)];
            await InvokeAsync(StateHasChanged);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // The page went away while the names were on their way.
        }
        catch (Exception ex)
        {
            // Nothing awaits this read, so a failure must not go unobserved. The filter simply keeps no names; only the type is logged, never the message.
            Logger.LogWarning("The agent names for the audit filter could not be read ({ExceptionType}).", ex.GetType().Name);
        }
    }

    private async Task LoadAsync()
    {
        // The first page of a filter fixes "as of". Later pages of the same filter reuse it, so a new event cannot push rows onto the next page while the admin reads.
        var filter = (_subject, _actor);
        if (_page == 1 || _asOf is null || _filterOfAsOf != filter)
        {
            _asOf = Time.GetUtcNow();
            _filterOfAsOf = filter;
        }

        // Only the latest load may change the screen: a slow answer for an earlier filter or page is ignored.
        var loadId = ++_loadId;
        var redirecting = false;
        _loading = true;
        _error = null;
        try
        {
            var result = await Events.ListAsync(new AdminEventFilter(_subject, _actor, _asOf), _page, AuditCopy.PageSize, _lifetime.Token);
            if (_lifetime.IsCancellationRequested || loadId != _loadId)
            {
                return;
            }

            if (result.IsSuccess)
            {
                _rows = [.. result.Value.Items.Select(AdminEventRowViewModel.From)];
                _total = result.Value.TotalCount;
                _announcement = AuditCopy.EventCount(_total);

                // A page past the end (an old address, or the log is shorter than it was): go to the last page that has rows instead of saying there are none.
                var lastPage = Math.Max(1, (_total + AuditCopy.PageSize - 1) / AuditCopy.PageSize);
                if (_rows.Count == 0 && _total > 0 && _page > lastPage)
                {
                    redirecting = true;
                    Navigation.NavigateTo(AuditFilters.Uri(_subject, _actor, lastPage), replace: true);
                }
            }
            else
            {
                _error = $"{AuditCopy.LoadFailed} {result.Errors[0].Message}";
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

    // Refresh starts again from now: a new "as of" for the page on screen.
    private async Task RefreshAsync()
    {
        _asOf = null;
        await LoadAsync();
    }

    private void OnSubjectChanged(ChangeEventArgs e) =>
        Navigation.NavigateTo(AuditFilters.Uri(AuditFilters.CanonicalSubject(e.Value?.ToString()), _actor, 1));

    private void OnActorChanged(ChangeEventArgs e) =>
        Navigation.NavigateTo(AuditFilters.Uri(_subject, Guid.TryParse(e.Value?.ToString(), out var actor) ? actor : null, 1));

    private void OnPageChanged(int page) => Navigation.NavigateTo(AuditFilters.Uri(_subject, _actor, page));

    public void Dispose()
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
