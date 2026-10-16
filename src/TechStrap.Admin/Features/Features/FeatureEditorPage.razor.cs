using Microsoft.AspNetCore.Components;
using TechStrap.Admin.Clients;
using TechStrap.Contracts.Features;
using TechStrap.Contracts.Paging;
using TechStrap.Contracts.Products;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Admin.Features.Features;

public partial class FeatureEditorPage : IDisposable
{
    [Parameter] public Guid? Id { get; set; }
    [SupplyParameterFromQuery(Name = "productId")] public Guid? ProductId { get; set; }
    private readonly CancellationTokenSource _lifetime = new();
    private FeatureDto? _feature;
    private IReadOnlyList<ProductDto> _products = [];
    private IReadOnlyList<LinkedFeatureTicketDto> _tickets = [];
    private string _product = "", _slug = "", _title = "", _description = "", _progress = FeatureProgressStatuses.UnderConsideration;
    private string? _error, _notice, _linksError;
    private bool _loading, _busy, _held, _tooComplex, _uploading;
    private int _epoch;
    private bool Dirty => _feature is not null && (_title != _feature.Title || _description != _feature.DescriptionMarkdown || _progress != _feature.ProgressStatus);
    protected override Task OnParametersSetAsync() => LoadAsync();
    private async Task LoadAsync()
    {
        var epoch = ++_epoch;
        _busy = false;
        _loading = true;
        _error = null;
        _notice = null;
        _held = true;
        try
        {
            var products = await Products.ListAsync(_lifetime.Token);
            if (epoch != _epoch) return;
            if (products.IsSuccess) _products = products.Value;
            if (Id is { } id)
            {
                var result = await Features.GetAsync(id, _lifetime.Token);
                if (epoch != _epoch) return;
                if (!result.IsSuccess) { _feature = null; _error = "The feature could not be loaded. Reload to try again."; return; }
                Apply(result.Value);
                await LoadLinksAsync(id);
            }
            else
            {
                _feature = null;
                _product = ProductId?.ToString() ?? "";
                _slug = _title = _description = "";
                _held = false;
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        finally { if (epoch == _epoch) _loading = false; }
    }
    private void Apply(FeatureDto feature)
    {
        _feature = feature;
        _title = feature.Title;
        _description = feature.DescriptionMarkdown;
        _progress = feature.ProgressStatus;
        _held = false;
        _tooComplex = false;
    }
    private void SetDescription(string value) => _description = value;
    private void SetTooComplex(bool value) => _tooComplex = value;
    private void SetUploading(bool value) => _uploading = value;
    private async Task SaveAsync()
    {
        if (_busy || _held || _tooComplex || _uploading) return;
        if (string.IsNullOrWhiteSpace(_title) || string.IsNullOrWhiteSpace(_description)) { _error = "Enter a public title and description."; return; }
        if (Id is null && (!Guid.TryParse(_product, out _) || string.IsNullOrWhiteSpace(_slug))) { _error = "Choose a product and enter a URL slug."; return; }
        var epoch = _epoch;
        _busy = true;
        _error = _notice = null;
        try
        {
            var result = Id is { } id
                ? await Features.UpdateAsync(id, new(_title, _description, _progress, _feature!.Version), _lifetime.Token)
                : await Features.CreateAsync(new(Guid.Parse(_product), _slug, _title, _description), _lifetime.Token);
            if (epoch != _epoch) return;
            if (!result.IsSuccess) { FailedWrite(result.Errors); return; }
            Apply(result.Value);
            _notice = "Feature saved.";
            if (Id is null) Navigation.NavigateTo($"/features/{result.Value.Id}");
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        finally { if (epoch == _epoch) _busy = false; }
    }
    private async Task ChangePublicationAsync()
    {
        if (_busy || _held || Dirty || _feature is null || _tooComplex || _uploading) return;
        var epoch = _epoch;
        _busy = true;
        _error = _notice = null;
        try
        {
            var result = await Features.PublishAsync(_feature.Id, _feature.Version, _feature.PublicationState != FeaturePublicationStates.Published, _lifetime.Token);
            if (epoch != _epoch) return;
            if (result.IsSuccess) { Apply(result.Value); _notice = _feature.PublicationState == FeaturePublicationStates.Published ? "Feature published." : "Feature unpublished."; }
            else FailedWrite(result.Errors);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        finally { if (epoch == _epoch) _busy = false; }
    }
    private void FailedWrite(IReadOnlyList<SyntaxCircus.Common.ResultError> errors)
    {
        var error = errors[0];
        _held = error.Code == ApiErrorCodes.ConcurrencyConflict || ApiErrorCodes.IsUncertainWrite(error.Code);
        _error = _held ? "The feature may have changed. Reload the saved feature before making another change." : string.Join(" ", errors.Select(e => e.Message));
    }
    private async Task LoadLinksAsync(Guid id)
    {
        var epoch = _epoch;
        var result = await Features.TicketsAsync(id, _lifetime.Token);
        if (Id != id || epoch != _epoch) return;
        _linksError = result.IsSuccess ? null : "Linked requests could not be loaded. Reload to try again.";
        _tickets = result.IsSuccess ? result.Value : [];
    }
    private async Task UnlinkAsync(LinkedFeatureTicketDto ticket)
    {
        if (_busy || _held || ticket.Status == TicketStatuses.Closed) return;
        var epoch = _epoch;
        _busy = true;
        try
        {
            var result = await Features.LinkTicketAsync(ticket.Id, null, ticket.Version, _lifetime.Token);
            if (epoch != _epoch) return;
            if (result.IsSuccess) await LoadLinksAsync(_feature!.Id);
            else FailedWrite(result.Errors);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        finally { if (epoch == _epoch) _busy = false; }
    }
    public void Dispose() { _epoch++; _lifetime.Cancel(); _lifetime.Dispose(); }
}
