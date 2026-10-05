using System.Globalization;
using Microsoft.AspNetCore.Components;
using SyntaxCircus.Common;
using TechStrap.Admin.Auth;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Shell;
using TechStrap.Contracts.Kb;
using TechStrap.Contracts.Products;

namespace TechStrap.Admin.Features.Kb;

/// <summary>
/// The categories (any agent): the list in sort order with the product each belongs to, create, rename, re-describe and re-order (the sort order is saved through the update, with the version the list was read
/// with), and delete (Admin only: the button is drawn only for an admin, and the API answers 403 to anyone else, which the dialog reports). A delete is blocked by the API while articles are in the category (409
/// <c>kb-category-in-use</c>): the dialog says so and cannot be confirmed. Writes use <see cref="CancellationToken.None"/> and never retry; an unknown outcome says so and offers a reload, and a delete whose outcome is
/// unknown is held (<see cref="UncertainMarks"/>) until a later read of the list. Every load takes the next <c>_loadId</c> and only the latest may change the screen.
/// </summary>
public sealed partial class KbCategoriesPage : IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Dictionary<string, string> _createErrors = [];
    private readonly Dictionary<string, string> _editErrors = [];
    private readonly UncertainMarks _uncertainDeletes = new();
    private IReadOnlyList<KbCategoryRowViewModel> _rows = [];
    private IReadOnlyList<ProductDto> _products = [];
    private Guid? _newProductId;
    private Guid _deletingId;
    private Guid _editing;
    private string _newName = string.Empty;
    private string _newSlug = string.Empty;
    private string _newDescription = string.Empty;
    private string _newSortOrder = KbCategoryForm.SortOrderStep.ToString(CultureInfo.InvariantCulture);
    private string _editName = string.Empty;
    private string _editDescription = string.Empty;
    private string _editSortOrder = string.Empty;
    private string? _error;
    private string? _rowError;
    private string? _createFormError;
    private string? _deleteError;
    private bool _slugEdited;
    private bool _sortOrderTouched;
    private bool _loading = true;
    private bool _creating;
    private bool _busy;
    private bool _rowReload;
    private bool _deleteBusy;
    private bool _deleteBlocked;
    private bool _deleteUncertain;
    private bool _disposed;
    private int _loadId;

    [Inject]
    private IKbClient Kb { get; set; } = default!;

    [Inject]
    private IProductsClient ProductsClient { get; set; } = default!;

    [Inject]
    private AgentSession Session { get; set; } = default!;

    [Inject]
    private StatusMessageService StatusMessages { get; set; } = default!;

    // The row being deleted, looked up fresh each time so a refreshed list is always what is confirmed.
    private KbCategoryRowViewModel? Deleting => _deletingId == Guid.Empty ? null : _rows.FirstOrDefault(r => r.Id == _deletingId);

    protected override async Task OnInitializedAsync()
    {
        Session.Changed += OnSessionChanged;
        await LoadAsync();
    }

    private void OnSessionChanged()
    {
        if (!_disposed)
        {
            _ = InvokeAsync(StateHasChanged);
        }
    }

    // True when this call read the list and it is now on screen. Only the latest load may change the screen: a slow answer that was overtaken is ignored.
    private async Task<bool> LoadAsync()
    {
        var loadId = ++_loadId;
        _loading = true;
        _error = null;
        try
        {
            var categories = Kb.ListCategoriesAsync(_lifetime.Token);
            var products = ProductsClient.ListAsync(_lifetime.Token);
            await Task.WhenAll(categories, products);
            if (_lifetime.IsCancellationRequested || loadId != _loadId)
            {
                return false;
            }

            if (categories.Result.IsFailure || products.Result.IsFailure)
            {
                var failure = categories.Result.IsFailure ? categories.Result.Errors[0] : products.Result.Errors[0];
                _error = $"{KbCategoriesCopy.LoadFailed} {failure.Message}";
                return false;
            }

            _products = products.Result.Value;
            _rows = KbCategoryRowViewModel.Sorted(categories.Result.Value.Select(c => KbCategoryRowViewModel.From(c, _products)));
            _uncertainDeletes.ReleaseForLoad(loadId);

            // A form the agent has not started keeps offering the next free place in the order.
            if (!_sortOrderTouched && _newName.Length == 0)
            {
                _newSortOrder = KbCategoryForm.NextSortOrder(_rows).ToString(CultureInfo.InvariantCulture);
            }

            return true;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            return false;
        }
        finally
        {
            if (loadId == _loadId)
            {
                _loading = false;
            }
        }
    }

    private async Task ReloadAsync()
    {
        _rowError = null;
        _rowReload = false;
        await LoadAsync();
    }

    // ---- create --------------------------------------------------------------------------------------------------

    private void OnNewSortOrderInput(ChangeEventArgs e)
    {
        _newSortOrder = e.Value?.ToString() ?? string.Empty;
        _sortOrderTouched = true;
    }

    private void OnNewProductChanged(ChangeEventArgs e) => _newProductId = Guid.TryParse(e.Value as string, out var id) ? id : null;

    private void OnNewNameInput(ChangeEventArgs e)
    {
        _newName = e.Value?.ToString() ?? string.Empty;
        if (!_slugEdited)
        {
            _newSlug = KbCategoryForm.SlugFrom(_newName);
        }
    }

    private void OnNewSlugInput(ChangeEventArgs e)
    {
        _newSlug = e.Value?.ToString() ?? string.Empty;
        _slugEdited = true;
    }

    private void CheckCreate(string field)
    {
        var message = field switch
        {
            ApiFields.Name => KbCategoryForm.CheckName(_newName),
            ApiFields.Slug => KbCategoryForm.CheckSlug(_newSlug),
            ApiFields.Description => KbCategoryForm.CheckDescription(_newDescription),
            _ => KbCategoryForm.CheckSortOrder(_newSortOrder),
        };
        Set(_createErrors, field, message);
    }

    private static void Set(Dictionary<string, string> errors, string field, string? message)
    {
        if (message is null)
        {
            errors.Remove(field);
        }
        else
        {
            errors[field] = message;
        }
    }

    private async Task CreateAsync()
    {
        if (_creating)
        {
            return;
        }

        _createFormError = null;
        foreach (var field in new[] { ApiFields.Name, ApiFields.Slug, ApiFields.Description, ApiFields.SortOrder })
        {
            CheckCreate(field);
        }

        if (_createErrors.Count > 0 || !KbCategoryForm.TryParseSortOrder(_newSortOrder, out var sortOrder))
        {
            return;
        }

        _creating = true;
        try
        {
            var request = new CreateKbCategoryRequest(_newProductId, _newSlug.Trim(), _newName.Trim(), Blank(_newDescription), sortOrder);
            var result = await Kb.CreateCategoryAsync(request, CancellationToken.None);
            if (_disposed)
            {
                return;
            }

            if (result.IsFailure)
            {
                ShowFailure(result.Errors, _createErrors, create: true);
                return;
            }

            var category = result.Value;
            _rows = KbCategoryRowViewModel.Sorted([.. _rows, KbCategoryRowViewModel.From(category, _products)]);
            _newName = string.Empty;
            _newSlug = string.Empty;
            _newDescription = string.Empty;
            _newSortOrder = KbCategoryForm.NextSortOrder(_rows).ToString(CultureInfo.InvariantCulture);
            _slugEdited = false;
            _sortOrderTouched = false;
            _createErrors.Clear();
            StatusMessages.Show(KbCategoriesCopy.Created(category.Name));
        }
        finally
        {
            _creating = false;
        }
    }

    private static string? Blank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // A slug taken or reserved is a field error on the slug; a 400 names its field; an unknown outcome or a stale version is said once, above the list.
    private void ShowFailure(IReadOnlyList<ResultError> errors, Dictionary<string, string> fieldErrors, bool create)
    {
        var first = errors[0];
        if (create && first.Code == ApiErrorCodes.KbCategorySlugTaken)
        {
            fieldErrors[ApiFields.Slug] = KbCategoriesCopy.SlugTaken;
            return;
        }

        if (create && first.Code == ApiErrorCodes.KbCategoryReservedSlug)
        {
            fieldErrors[ApiFields.Slug] = KbCategoriesCopy.SlugReserved;
            return;
        }

        if (WriteOutcomes.Classify(first) == WriteOutcome.Conflict)
        {
            _rowError = KbCategoriesCopy.SaveConflict;
            _rowReload = true;
            return;
        }

        if (ApiErrorCodes.IsUncertainWrite(first.Code))
        {
            _rowError = KbCategoriesCopy.SaveUncertain;
            _rowReload = true;
            return;
        }

        foreach (var error in errors)
        {
            // The edit form shows only name, description and sort order: an error for any other field goes above the list, never into a field error that would block Save.
            if (error.Target is ApiFields.Name or ApiFields.Description || (create && error.Target is ApiFields.Slug))
            {
                fieldErrors.TryAdd(error.Target, error.Message);
            }
            else if (create)
            {
                _createFormError ??= error.Message;
            }
            else
            {
                _rowError ??= error.Message;
            }
        }
    }

    // ---- edit ----------------------------------------------------------------------------------------------------

    private void BeginEdit(KbCategoryRowViewModel row)
    {
        _rowError = null;
        _rowReload = false;
        _editErrors.Clear();
        _editing = row.Id;
        _editName = row.Name;
        _editDescription = row.Description ?? string.Empty;
        _editSortOrder = row.SortOrder.ToString(CultureInfo.InvariantCulture);
    }

    private void CancelEdit()
    {
        _editing = Guid.Empty;
        _editErrors.Clear();
    }

    private async Task SaveEditAsync()
    {
        if (_busy || _rows.FirstOrDefault(r => r.Id == _editing) is not { } row)
        {
            return;
        }

        Set(_editErrors, ApiFields.Name, KbCategoryForm.CheckName(_editName));
        Set(_editErrors, ApiFields.Description, KbCategoryForm.CheckDescription(_editDescription));
        Set(_editErrors, ApiFields.SortOrder, KbCategoryForm.CheckSortOrder(_editSortOrder));
        if (_editErrors.Count > 0 || !KbCategoryForm.TryParseSortOrder(_editSortOrder, out var sortOrder))
        {
            return;
        }

        _busy = true;
        _rowError = null;
        _rowReload = false;
        try
        {
            var result = await Kb.UpdateCategoryAsync(row.Id, new UpdateKbCategoryRequest(_editName.Trim(), Blank(_editDescription), sortOrder, row.Version), CancellationToken.None);
            if (_disposed)
            {
                return;
            }

            if (result.IsSuccess)
            {
                _rows = KbCategoryRowViewModel.Sorted(_rows.Select(r => r.Id == row.Id ? KbCategoryRowViewModel.From(result.Value, _products) : r));
                _editing = Guid.Empty;
                StatusMessages.Show(KbCategoriesCopy.Saved(result.Value.Name));
            }
            else if (result.Errors[0].Code == ApiErrorCodes.KbCategoryNotFound)
            {
                _editing = Guid.Empty;
                StatusMessages.Show(KbCategoriesCopy.CategoryGone);
                await LoadAsync();
            }
            else
            {
                ShowFailure(result.Errors, _editErrors, create: false);
            }
        }
        finally
        {
            _busy = false;
        }
    }

    // ---- delete --------------------------------------------------------------------------------------------------

    private void AskDelete(KbCategoryRowViewModel row)
    {
        _rowError = null;
        _deletingId = row.Id;
        _deleteUncertain = _uncertainDeletes.Contains(row.Id);
        _deleteBlocked = _deleteUncertain;
        _deleteError = _deleteUncertain ? KbCategoriesCopy.DeleteUncertain : null;
    }

    private void CancelDelete()
    {
        if (!_deleteBusy)
        {
            _deletingId = Guid.Empty;
            _deleteError = null;
            _deleteBlocked = false;
            _deleteUncertain = false;
        }
    }

    private async Task ConfirmDeleteAsync()
    {
        if (_deleteBusy || Deleting is not { } category || _uncertainDeletes.Contains(category.Id))
        {
            return;
        }

        _deleteBusy = true;
        _deleteError = null;
        _deleteUncertain = false;
        StateHasChanged();
        try
        {
            var result = await Kb.DeleteCategoryAsync(category.Id, CancellationToken.None);
            if (_disposed)
            {
                return;
            }

            if (result.IsSuccess)
            {
                _rows = [.. _rows.Where(r => r.Id != category.Id)];
                _deletingId = Guid.Empty;
                StatusMessages.Show(KbCategoriesCopy.Deleted(category.Name));
                return;
            }

            await ShowDeleteFailureAsync(result.Errors[0], category.Id);
        }
        finally
        {
            _deleteBusy = false;
        }
    }

    private async Task ShowDeleteFailureAsync(ResultError error, Guid categoryId)
    {
        if (error.Code == ApiErrorCodes.KbCategoryInUse)
        {
            // Articles are in the category: the dialog stays open to say so and cannot be confirmed (the API would answer the same again).
            _deleteError = KbCategoriesCopy.DeleteInUse(error.Message);
            _deleteBlocked = true;
        }
        else if (error.Code == ApiErrorCodes.KbCategoryNotFound)
        {
            _deletingId = Guid.Empty;
            StatusMessages.Show(KbCategoriesCopy.CategoryGone);
            await LoadAsync();
        }
        else if (error.Kind == ResultErrorKind.Forbidden)
        {
            _deleteError = KbCategoriesCopy.DeleteForbidden;
            _deleteBlocked = true;
        }
        else if (ApiErrorCodes.IsUncertainWrite(error.Code))
        {
            _deleteError = KbCategoriesCopy.DeleteUncertain;
            _deleteUncertain = true;
            _deleteBlocked = true;
            _uncertainDeletes.Add(categoryId, _loadId);
        }
        else
        {
            _deleteError = KbCategoriesCopy.DeleteFailed(error.Message);
        }
    }

    private async Task ReloadAfterUncertainAsync()
    {
        _deletingId = Guid.Empty;
        _deleteError = null;
        _deleteBlocked = false;
        _deleteUncertain = false;
        await LoadAsync();
    }

    public void Dispose()
    {
        _disposed = true;
        Session.Changed -= OnSessionChanged;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
