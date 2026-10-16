using Microsoft.AspNetCore.Components;
using TechStrap.Admin.Clients;
using TechStrap.Contracts.Features;
using TechStrap.Contracts.Paging;
using TechStrap.Contracts.Products;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Admin.Features.Features;

public partial class FeatureTicketLinkPanel : IDisposable
{
    [Parameter] public Guid TicketId { get; set; }
    [Parameter] public Guid ProductId { get; set; }
    [Parameter] public Guid? FeatureId { get; set; }
    [Parameter] public uint RowVersion { get; set; }
    [Parameter] public EventCallback<TicketStateDto> OnChanged { get; set; }
    [Parameter] public EventCallback OnReload { get; set; }
    private readonly CancellationTokenSource _lifetime = new();
    private IReadOnlyList<FeatureSummaryDto> _choices = [];
    private FeatureDto? _linked;
    private string _search = "", _selected = "";
    private string? _error;
    private bool _busy, _held, _loading;
    private Guid? _loadedTicket, _loadedFeature;
    private uint _loadedVersion;
    private int _total, _loadId, _epoch;
    protected override async Task OnParametersSetAsync()
    {
        if (_loadedTicket == TicketId && _loadedFeature == FeatureId && _loadedVersion == RowVersion) return;
        var epoch = ++_epoch;
        _busy = false;
        _loadedTicket = TicketId; _loadedFeature = FeatureId; _loadedVersion = RowVersion;
        _held = false;
        await SearchAsync();
        if (epoch != _epoch) return;
        _linked = null;
        if (FeatureId is { } id)
        {
            var result = await Features.GetAsync(id, _lifetime.Token);
            if (epoch == _epoch && FeatureId == id && result.IsSuccess) _linked = result.Value;
        }
    }
    private async Task SearchAsync()
    {
        var load = ++_loadId;
        _loading = true;
        try
        {
            var result = await Features.ListAsync(ProductId, _search, null, 1, _lifetime.Token);
            if (load != _loadId) return;
            if (result.IsSuccess) { _error = null; _choices = result.Value.Items; _total = result.Value.TotalCount; }
            else _error = "Features could not be loaded. Reload to try again.";
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        finally { if (load == _loadId) _loading = false; }
    }
    private Task LinkAsync() => Guid.TryParse(_selected, out var id) ? ChangeAsync(id) : Task.CompletedTask;
    private async Task ChangeAsync(Guid? featureId)
    {
        if (_busy || _held || _loading) return;
        var epoch = _epoch;
        _busy = true;
        _error = null;
        try
        {
            var result = await Features.LinkTicketAsync(TicketId, featureId, RowVersion, _lifetime.Token);
            if (epoch != _epoch) return;
            if (result.IsSuccess) await OnChanged.InvokeAsync(result.Value);
            else
            {
                var error = result.Errors[0];
                _held = error.Code == ApiErrorCodes.ConcurrencyConflict || ApiErrorCodes.IsUncertainWrite(error.Code);
                _error = _held ? "The ticket may have changed. Reload the ticket before changing its link again." : error.Message;
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        finally { if (epoch == _epoch) _busy = false; }
    }
    private async Task ReloadAsync() { await OnReload.InvokeAsync(); await SearchAsync(); }
    public void Dispose() { _epoch++; _loadId++; _lifetime.Cancel(); _lifetime.Dispose(); }
}
