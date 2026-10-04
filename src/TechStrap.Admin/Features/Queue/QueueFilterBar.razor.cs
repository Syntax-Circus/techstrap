using Microsoft.AspNetCore.Components;
using TechStrap.Contracts.Products;
using TechStrap.Contracts.Tags;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Admin.Features.Queue;

/// <summary>
/// Search and the four filters. The search box waits <see cref="QueueDefaults.SearchDebounce"/> after the last keystroke (Enter commits at once);
/// a select commits immediately. Every commit reports a new <see cref="QueueFilter"/> on page 1 and the owner navigates, so the URL stays the single source of truth.
/// </summary>
public sealed partial class QueueFilterBar : IDisposable
{

    private ElementReference _search;
    private ITimer? _timer;
    private string _text = string.Empty;
    private string? _appliedSearch;

    [Inject]
    private TimeProvider Time { get; set; } = default!;

    [Parameter, EditorRequired]
    public QueueFilter Filter { get; set; } = default!;

    [Parameter]
    public IReadOnlyList<ProductDto> Products { get; set; } = [];

    [Parameter]
    public IReadOnlyList<TagDto> Tags { get; set; } = [];

    [Parameter, EditorRequired]
    public EventCallback<QueueFilter> OnChanged { get; set; }

    [Parameter]
    public EventCallback OnRefresh { get; set; }

    /// <summary>The <c>/</c> shortcut: put the cursor in the search box.</summary>
    public ValueTask FocusSearchAsync() => _search.FocusAsync();

    protected override void OnParametersSet()
    {
        // Follow the URL (clear filters, back button) unless the user is mid-word: a pending timer means the text is newer than the filter.
        if (_timer is null && Filter.Search != _appliedSearch)
        {
            _text = Filter.Search ?? string.Empty;
        }

        _appliedSearch = Filter.Search;
    }

    private void OnSearchInput(ChangeEventArgs e)
    {
        _text = e.Value as string ?? string.Empty;
        _timer?.Dispose();
        _timer = Time.CreateTimer(_ => _ = InvokeAsync(CommitSearchAsync), null, QueueDefaults.SearchDebounce, Timeout.InfiniteTimeSpan);
    }

    private Task SubmitSearchAsync() => CommitSearchAsync();

    private async Task CommitSearchAsync()
    {
        _timer?.Dispose();
        _timer = null;
        var search = string.IsNullOrWhiteSpace(_text) ? null : _text.Trim();
        if (search != Filter.Search)
        {
            await OnChanged.InvokeAsync(Filter with { Search = search, Page = 1 });
        }
    }

    private Task OnProductChangedAsync(ChangeEventArgs e) => Commit(Filter with { ProductId = ParseGuid(e), Page = 1 });

    private Task OnStatusChangedAsync(ChangeEventArgs e) => Commit(Filter with { Status = ParseText(e), Page = 1 });

    private Task OnPriorityChangedAsync(ChangeEventArgs e) => Commit(Filter with { Priority = ParseText(e), Page = 1 });

    private Task OnTagChangedAsync(ChangeEventArgs e) => Commit(Filter with { TagId = ParseGuid(e), Page = 1 });

    private Task ClearAsync() => Commit(Filter.Cleared());

    private Task Commit(QueueFilter filter) => OnChanged.InvokeAsync(filter);

    private static Guid? ParseGuid(ChangeEventArgs e) => Guid.TryParse(e.Value as string, out var id) ? id : null;

    private static string? ParseText(ChangeEventArgs e) => e.Value is string { Length: > 0 } text ? text : null;

    public void Dispose() => _timer?.Dispose();
}
