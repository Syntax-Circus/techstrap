using System.Globalization;
using Microsoft.AspNetCore.Components;
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

        // Both reads start now and are awaited together: the events never wait for the names of the filter. A failed agent list only means the filter has no names; the stale-load guard in LoadAsync still applies.
        var agents = Task.CompletedTask;
        if (!_agentsLoaded)
        {
            _agentsLoaded = true;
            agents = LoadAgentsAsync();
        }

        var events = Task.CompletedTask;
        var key = (_subject, _actor, _page);
        if (_loadedFor != key)
        {
            _loadedFor = key;
            events = LoadAsync();
        }

        await Task.WhenAll(agents, events);
    }

    // The filter's actor list. If it cannot be read the filter simply has no names to offer; the log itself still works.
    private async Task LoadAgentsAsync()
    {
        var result = await AgentsClient.ListAllAsync(_lifetime.Token);
        if (!_lifetime.IsCancellationRequested && result.IsSuccess)
        {
            _agents = [.. result.Value.OrderBy(a => a.Name ?? a.DisplayLabel, StringComparer.OrdinalIgnoreCase)];
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
