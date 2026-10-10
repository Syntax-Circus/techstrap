using Microsoft.AspNetCore.Components;
using TechStrap.Admin.Clients;
using TechStrap.Contracts.Kb;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Admin.Features.Kb;

/// <summary>
/// Searches the published articles an agent may link from a reply (PHASE-08 T20): the ticket's product plus the shared ones, never a draft or an archived article. The search waits
/// <see cref="KbDefaults.PickerDebounce"/> after the last keystroke, asks for <see cref="KbArticleStatuses.Published"/> only and drops anything else the answer might hold, and reports a choice through
/// <see cref="OnAdd"/>; the selection itself belongs to the composer's draft. Every search takes the next <c>_loadId</c> and only the latest may change the list. Nothing is searched for blank text, and nothing is called until the agent types.
/// </summary>
public sealed partial class ArticlePicker : IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly string _id = Guid.NewGuid().ToString("N")[..8];
    private IReadOnlyList<ArticleChoice> _results = [];
    private ITimer? _timer;
    private string _text = string.Empty;
    private string? _error;
    private string _announcement = string.Empty;
    private bool _loading;
    private bool _searched;
    private bool _disposed;
    private Guid _lastProduct;
    private int _loadId;

    [Inject]
    private IKbClient Kb { get; set; } = default!;

    [Inject]
    private TimeProvider Time { get; set; } = default!;

    /// <summary>The ticket's product: its own articles are offered, and the shared ones.</summary>
    [Parameter, EditorRequired]
    public Guid ProductId { get; set; }

    /// <summary>What the draft already links, so an article cannot be added twice and the limit is known.</summary>
    [Parameter]
    public IReadOnlyList<ArticleChoice> Selected { get; set; } = [];

    [Parameter]
    public bool Disabled { get; set; }

    [Parameter]
    public EventCallback<ArticleChoice> OnAdd { get; set; }

    private string InputId => $"ts-kb-picker-{_id}";

    private bool AtLimit => Selected.Count >= TicketOperationLimits.MaxLinkedArticles;

    private bool IsSelected(ArticleChoice choice) => Selected.Any(s => s.Id == choice.Id);

    protected override void OnParametersSet()
    {
        // A ticket moved to another product must not keep offering the old product's articles, nor show a search for them that is still on its way: the next load id makes that answer stale.
        if (_lastProduct != Guid.Empty && _lastProduct != ProductId)
        {
            _loadId++;
            _results = [];
            _searched = false;
            _loading = false;
            _error = null;
            _announcement = string.Empty;
        }

        _lastProduct = ProductId;
    }

    private void OnInput(ChangeEventArgs e)
    {
        _text = e.Value as string ?? string.Empty;
        _timer?.Dispose();
        _timer = Time.CreateTimer(_ => _ = InvokeAsync(SearchAsync), null, KbDefaults.PickerDebounce, Timeout.InfiniteTimeSpan);
    }

    private async Task SearchAsync()
    {
        _timer?.Dispose();
        _timer = null;
        if (_disposed)
        {
            return;
        }

        var loadId = ++_loadId;
        var text = _text.Trim();
        if (text.Length == 0)
        {
            _results = [];
            _error = null;
            _loading = false;
            _searched = false;
            _announcement = string.Empty;
            StateHasChanged();
            return;
        }

        _loading = true;
        _error = null;
        StateHasChanged();
        try
        {
            var result = await Kb.ListAsync(
                new ListKbArticlesRequest(ProductId, SharedOnly: false, IncludeShared: true, KbArticleStatuses.Published, CategoryId: null, text, Page: 1, KbDefaults.PickerPageSize),
                _lifetime.Token);
            if (_disposed || loadId != _loadId)
            {
                return;
            }

            if (result.IsFailure)
            {
                _error = ArticlePickerCopy.SearchFailed;
                return;
            }

            // Defense in depth: the request asks for published articles, and a row that is not one is never offered, whatever the answer holds.
            _results = [.. result.Value.Items.Where(a => a.Status == KbArticleStatuses.Published).Select(a => new ArticleChoice(a.Id, a.Title, a.ProductId is null))];
            _searched = true;
            _announcement = ArticlePickerCopy.Count(_results.Count);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            return;
        }
        catch (Exception) when (loadId == _loadId && !_disposed)
        {
            // Fixed copy only: an exception message can carry a host or a port.
            _error = ArticlePickerCopy.SearchFailed;
        }
        finally
        {
            if (!_disposed && loadId == _loadId)
            {
                _loading = false;
                StateHasChanged();
            }
        }
    }

    private async Task AddAsync(ArticleChoice choice)
    {
        if (Disabled || AtLimit || IsSelected(choice))
        {
            return;
        }

        await OnAdd.InvokeAsync(choice);
    }

    public void Dispose()
    {
        _disposed = true;
        _timer?.Dispose();
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
