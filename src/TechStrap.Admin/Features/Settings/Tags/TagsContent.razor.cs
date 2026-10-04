using Microsoft.AspNetCore.Components;
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Shell;
using TechStrap.Contracts.Tags;

namespace TechStrap.Admin.Features.Settings.Tags;

/// <summary>
/// The tag list with its ticket counts (Admin only), create, rename and recolour, and delete. Deleting an unused tag is a medium-tier confirmation and sends no force flag. Deleting a tag that is in use is
/// irreversible: the dialog shows the count ("12 tickets"), asks for the tag's name to be typed, and only then sends <c>force=true</c>. A 409 <c>tag-in-use</c> (someone tagged a ticket after the list was read) refreshes
/// the counts and keeps the dialog open, so the next confirmation is the typed one. Writes use <see cref="CancellationToken.None"/> and never retry; an unknown outcome says so and offers a reload.
/// </summary>
public sealed partial class TagsContent : IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Dictionary<string, string> _createErrors = [];
    private readonly Dictionary<string, string> _editErrors = [];

    // Tags whose delete ended with an unknown outcome: held (a second ask shows the uncertain copy and sends nothing) until the list has been read again successfully.
    private readonly HashSet<Guid> _uncertainDeletes = [];
    private IReadOnlyList<TagRowViewModel> _rows = [];
    private Guid _deletingId;
    private Guid _editing;
    private string _newName = string.Empty;
    private string _newSlug = string.Empty;
    private string _newColour = TagsCopy.DefaultColour;
    private string _editName = string.Empty;
    private string _editColour = string.Empty;
    private string? _error;
    private string? _rowError;
    private string? _createFormError;
    private string? _deleteError;
    private bool _slugEdited;
    private bool _loading = true;
    private bool _creating;
    private bool _busy;
    private bool _rowUncertain;
    private bool _deleteBusy;
    private bool _deleteUncertain;
    private bool _disposed;
    private int _loadId;

    [Inject]
    private ITagsClient Tags { get; set; } = default!;

    [Inject]
    private StatusMessageService StatusMessages { get; set; } = default!;

    // The row being deleted, looked up fresh each time so a refreshed count (and so a refreshed dialog) is always the one confirmed.
    private TagRowViewModel? Deleting => _deletingId == Guid.Empty ? null : _rows.FirstOrDefault(r => r.Id == _deletingId);

    protected override async Task OnInitializedAsync()
    {
        await LoadAsync();
    }

    // True when this call read the list and it is now on screen. Only the latest load may change the screen: a slow answer that was overtaken is ignored.
    private async Task<bool> LoadAsync()
    {
        var loadId = ++_loadId;
        _loading = true;
        _error = null;
        try
        {
            var result = await Tags.ListSummaryAsync(_lifetime.Token);
            if (_lifetime.IsCancellationRequested || loadId != _loadId)
            {
                return false;
            }

            if (result.IsSuccess)
            {
                _rows = Sorted(result.Value.Select(TagRowViewModel.From));
                _uncertainDeletes.Clear();
                return true;
            }

            _error = $"{TagsCopy.LoadFailed} {result.Errors[0].Message}";
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

    private static IReadOnlyList<TagRowViewModel> Sorted(IEnumerable<TagRowViewModel> rows) =>
        [.. rows.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Slug, StringComparer.Ordinal)];

    private async Task ReloadAsync()
    {
        _rowError = null;
        _rowUncertain = false;
        await LoadAsync();
    }

    // ---- create --------------------------------------------------------------------------------------------------

    private void OnNewNameInput(ChangeEventArgs e)
    {
        _newName = e.Value?.ToString() ?? string.Empty;
        if (!_slugEdited)
        {
            _newSlug = TagForm.SlugFrom(_newName);
        }
    }

    private void OnNewSlugInput(ChangeEventArgs e)
    {
        _newSlug = e.Value?.ToString() ?? string.Empty;
        _slugEdited = true;
    }

    private void OnNewColourInput(ChangeEventArgs e) => _newColour = e.Value?.ToString() ?? string.Empty;

    private void CheckCreate(string field)
    {
        var message = field switch
        {
            ApiFields.Name => TagForm.CheckName(_newName),
            ApiFields.Slug => TagForm.CheckSlug(_newSlug),
            _ => TagForm.CheckColour(_newColour),
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
        foreach (var field in new[] { ApiFields.Name, ApiFields.Slug, ApiFields.Colour })
        {
            CheckCreate(field);
        }

        if (_createErrors.Count > 0)
        {
            return;
        }

        _creating = true;
        try
        {
            var result = await Tags.CreateAsync(new CreateTagRequest(_newSlug.Trim(), _newName.Trim(), _newColour.Trim()), CancellationToken.None);
            if (_disposed)
            {
                return;
            }

            if (result.IsFailure)
            {
                ShowFailure(result.Errors, _createErrors, create: true);
                return;
            }

            var tag = result.Value;
            _rows = Sorted([.. _rows, new TagRowViewModel(tag.Id, tag.Slug, tag.Name, tag.Colour, 0)]);
            _newName = string.Empty;
            _newSlug = string.Empty;
            _newColour = TagsCopy.DefaultColour;
            _slugEdited = false;
            _createErrors.Clear();
            StatusMessages.Show(TagsCopy.Created(tag.Name));
        }
        finally
        {
            _creating = false;
        }
    }

    // A 409 tag-slug-taken is a field error on the slug; a 400 names its field; an unknown outcome or anything else is said once, above the form or the list.
    private void ShowFailure(IReadOnlyList<ResultError> errors, Dictionary<string, string> fieldErrors, bool create)
    {
        var first = errors[0];
        if (create && first.Code == ApiErrorCodes.TagSlugTaken)
        {
            fieldErrors[ApiFields.Slug] = TagsCopy.SlugTaken;
            return;
        }

        if (ApiErrorCodes.IsUncertainWrite(first.Code))
        {
            _rowError = TagsCopy.SaveUncertain;
            _rowUncertain = true;
            return;
        }

        foreach (var error in errors)
        {
            // The edit form shows only the name and the colour: an error for any other field goes above the list, never into a field error that would block Save.
            if (error.Target is ApiFields.Name or ApiFields.Colour || (create && error.Target is ApiFields.Slug))
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

    private void BeginEdit(TagRowViewModel row)
    {
        _rowError = null;
        _rowUncertain = false;
        _editErrors.Clear();
        _editing = row.Id;
        _editName = row.Name;
        _editColour = row.Colour;
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

        Set(_editErrors, ApiFields.Name, TagForm.CheckName(_editName));
        Set(_editErrors, ApiFields.Colour, TagForm.CheckColour(_editColour));
        if (_editErrors.Count > 0)
        {
            return;
        }

        _busy = true;
        _rowError = null;
        try
        {
            var result = await Tags.UpdateAsync(row.Id, new UpdateTagRequest(_editName.Trim(), _editColour.Trim()), CancellationToken.None);
            if (_disposed)
            {
                return;
            }

            if (result.IsSuccess)
            {
                _rows = Sorted(_rows.Select(r => r.Id == row.Id ? r.With(result.Value) : r));
                _editing = Guid.Empty;
                StatusMessages.Show(TagsCopy.Saved(result.Value.Name));
            }
            else if (result.Errors[0].Code == ApiErrorCodes.TagNotFound)
            {
                _editing = Guid.Empty;
                StatusMessages.Show(TagsCopy.TagGone);
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

    private void AskDelete(TagRowViewModel row)
    {
        _rowError = null;
        _deletingId = row.Id;
        _deleteUncertain = _uncertainDeletes.Contains(row.Id);
        _deleteError = _deleteUncertain ? TagsCopy.DeleteUncertain : null;
    }

    private void CancelDelete()
    {
        if (!_deleteBusy)
        {
            _deletingId = Guid.Empty;
            _deleteError = null;
            _deleteUncertain = false;
        }
    }

    private async Task ConfirmDeleteAsync()
    {
        if (_deleteBusy || Deleting is not { } tag || _uncertainDeletes.Contains(tag.Id))
        {
            return;
        }

        // force is sent only for a tag the list shows as in use, and the dialog (typed name) has already been satisfied for it: an unused tag is never deleted with force.
        var force = tag.TicketCount > 0;
        _deleteBusy = true;
        _deleteError = null;
        _deleteUncertain = false;
        StateHasChanged();
        try
        {
            var result = await Tags.DeleteAsync(tag.Id, force, CancellationToken.None);
            if (_disposed)
            {
                return;
            }

            if (result.IsSuccess)
            {
                _rows = [.. _rows.Where(r => r.Id != tag.Id)];
                _deletingId = Guid.Empty;
                StatusMessages.Show(force ? TagsCopy.DeletedFromTickets(tag.Name, tag.TicketCount) : TagsCopy.Deleted(tag.Name));
                return;
            }

            await ShowDeleteFailureAsync(result.Errors[0], tag.Id);
        }
        finally
        {
            _deleteBusy = false;
        }
    }

    private async Task ShowDeleteFailureAsync(ResultError error, Guid tagId)
    {
        if (error.Code == ApiErrorCodes.TagInUse)
        {
            // Tickets were tagged after the list was read: show the new count, and the next confirmation is the typed one.
            if (!await LoadAsync())
            {
                // The list could not be read again: a medium confirm must never stay open under in-use copy. The load error is on the page.
                _deletingId = Guid.Empty;
            }
            else if (Deleting is null)
            {
                StatusMessages.Show(TagsCopy.TagGone);
            }
            else if (Deleting is { TicketCount: > 0 })
            {
                _deleteError = TagsCopy.NowInUse;
            }
            else
            {
                _deleteError = TagsCopy.DeleteFailed(error.Message);
            }
        }
        else if (error.Code == ApiErrorCodes.TagNotFound)
        {
            _deletingId = Guid.Empty;
            StatusMessages.Show(TagsCopy.TagGone);
            await LoadAsync();
        }
        else if (ApiErrorCodes.IsUncertainWrite(error.Code))
        {
            _deleteError = TagsCopy.DeleteUncertain;
            _deleteUncertain = true;
            _uncertainDeletes.Add(tagId);
        }
        else
        {
            _deleteError = TagsCopy.DeleteFailed(error.Message);
        }
    }

    private async Task ReloadAfterUncertainAsync()
    {
        _deletingId = Guid.Empty;
        _deleteError = null;
        _deleteUncertain = false;
        await LoadAsync();
    }

    public void Dispose()
    {
        _disposed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
