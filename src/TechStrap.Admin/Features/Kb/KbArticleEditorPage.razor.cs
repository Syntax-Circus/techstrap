using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.Extensions.Options;
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Shell;
using TechStrap.Admin.Options;
using TechStrap.Contracts.Kb;

namespace TechStrap.Admin.Features.Kb;

/// <summary>
/// Writes an article or edits one (any agent). Nothing on this page loses what the agent typed: a failed, refused or uncertain write leaves the form exactly as it was, and only a successful save
/// or an explicit Reload replaces the model. The update carries the <c>Version</c> the article was loaded with, so a stale save is a 409 and never an overwrite: the form is kept, a banner says so, and
/// saving stays off until the agent reloads. A write whose outcome is unknown is held (nothing more is sent) until a reload. Writes use <see cref="CancellationToken.None"/>: a save that is on its
/// way is never abandoned because the agent left the page, and what finishes after the page is gone changes nothing on it. Every load takes the next <c>_loadId</c> and only the latest may change
/// the screen. Unsaved changes are guarded twice: <c>NavigationLock</c> asks before an in-app move and the browser asks before the tab closes. Publishing needs the article saved first (it publishes what is
/// stored, not what is on screen), and an edit to an archived article makes it a draft again (the answer says so).
/// </summary>
public sealed partial class KbArticleEditorPage : IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Dictionary<string, string> _errors = [];
    private KbArticleEditorViewModel _model = new();
    private KbArticleEditorViewModel.Snapshot _saved = new KbArticleEditorViewModel().Take();
    private KbEditorLookups _lookups = KbEditorLookups.Empty;
    private string? _loadedFor;
    private string? _loadError;
    private string? _formError;
    private string? _uncertainNote;
    private string? _leaveTarget;
    private bool _creating;
    private bool _loading;
    private bool _busy;
    private bool _saving;
    private bool _publishing;
    private bool _archiving;
    private bool _archiveOpen;
    private bool _reloadOpen;
    private bool _conflict;
    private bool _gone;
    private bool _uploadingImage;
    private bool _slugEdited;
    private bool _allowLeave;
    private bool _disposed;
    private int _loadId;
    private int _epoch;

    [Inject]
    private KbArticleEditorPresenter Presenter { get; set; } = default!;

    [Inject]
    private IKbClient Kb { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [Inject]
    private StatusMessageService StatusMessages { get; set; } = default!;

    [Inject]
    private IOptions<PortalUrlOptions> Portal { get; set; } = default!;

    /// <summary>The route segment: an article id, or absent on <c>/kb/new</c>.</summary>
    [Parameter]
    public Guid? Id { get; set; }

    private string Heading => _creating ? KbEditorCopy.NewTitle : string.IsNullOrWhiteSpace(_saved.Title) ? KbCopy.Heading : _saved.Title;

    private bool IsDirty => !_model.Take().Equals(_saved);

    // A picture on its way holds every write: a save, publish or archive that answered meanwhile would replace the model and drop the picture the agent is about to add.
    private bool Held => _busy || _uploadingImage || _conflict || _uncertainNote is not null || _gone;

    // A new article can always be created; an existing one is saved once something changed. A create whose outcome is unknown may have made the article: no second create until the agent has looked at the list.
    private bool CanSave => !Held && (_creating || IsDirty);

    // Publish works on what is stored: the form must be saved, and complete.
    private bool CanPublish => !Held && !_creating && !IsDirty && _model.FirstMissingForPublish() is null;

    private bool CanArchive => !Held && !_creating && !IsDirty;

    private string? PublishHint =>
        _creating || _model.IsPublished ? null
        : IsDirty ? KbEditorCopy.SaveFirst
        : _model.FirstMissingForPublish() is { } missing ? KbEditorCopy.PublishNeeds(missing) : null;

    // Only a published article has an address on the portal, and it is the stored one: the category on screen may not be saved yet.
    private string? PortalUrl => _model.IsPublished && !_creating
        ? Portal.Value.ArticleUrl(_lookups.PortalProductKey(_model.ProductId), _lookups.CategorySlug(_saved.CategoryId), _model.Slug)
        : null;

    protected override async Task OnParametersSetAsync()
    {
        var key = Id?.ToString() ?? string.Empty;
        if (_loadedFor == key)
        {
            return;
        }

        _loadedFor = key;

        // A load or a write that is still running belongs to the previous article (or to the new-article form): it must change nothing here.
        _epoch++;
        _busy = false;
        _saving = false;
        _publishing = false;
        _archiving = false;
        _archiveOpen = false;
        _reloadOpen = false;
        _allowLeave = false;
        ResetMessages();
        _creating = Id is null;
        _gone = false;
        _slugEdited = false;
        _model = new KbArticleEditorViewModel();
        _saved = _model.Take();
        await LoadAsync();
    }

    private void ResetMessages()
    {
        _conflict = false;
        _uncertainNote = null;
        _formError = null;
        _loadError = null;
        _errors.Clear();
    }

    private async Task LoadAsync()
    {
        // Only the latest load may change the screen: a slow answer that was overtaken (a reload, another article) is ignored.
        var loadId = ++_loadId;
        _loading = true;
        _loadError = null;

        // The form (and a picture upload inside it) is replaced by the load, so its hold ends here.
        _uploadingImage = false;
        try
        {
            var result = await Presenter.LoadAsync(Id, _lifetime.Token);
            if (_lifetime.IsCancellationRequested || loadId != _loadId)
            {
                return;
            }

            if (result.IsFailure)
            {
                _gone = result.Errors[0].Code == ApiErrorCodes.KbArticleNotFound;
                _loadError = _gone ? null : $"{KbEditorCopy.LoadFailed} {result.Errors[0].Message}";
                return;
            }

            _lookups = result.Value.Lookups;
            _model = result.Value.Article is { } article ? KbArticleEditorViewModel.From(article) : new KbArticleEditorViewModel();
            _saved = _model.Take();
            _errors.Clear();
        }
        finally
        {
            if (loadId == _loadId)
            {
                _loading = false;
            }
        }
    }

    // Reload replaces the form with the stored article: when the form holds unsaved edits the agent confirms first, so one click never discards a draft.
    private async Task AskReload()
    {
        if (IsDirty)
        {
            _reloadOpen = true;
            return;
        }

        await ReloadAsync();
    }

    private void CancelReload() => _reloadOpen = false;

    private async Task ConfirmReloadAsync()
    {
        _reloadOpen = false;
        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        ResetMessages();
        await LoadAsync();
    }

    // ---- editing -------------------------------------------------------------------------------------------------

    private void OnTitleInput(string value)
    {
        _model.Title = value;
        if (_creating && !_slugEdited)
        {
            _model.Slug = KbArticleEditorViewModel.SlugFrom(value);
        }
    }

    private void OnSlugInput(string value)
    {
        _model.Slug = value;
        _slugEdited = true;
    }

    private void OnSummaryInput(ChangeEventArgs e) => _model.Summary = e.Value?.ToString() ?? string.Empty;

    private void OnBodyChanged(string value) => _model.Body = value;

    private void OnUploadingChanged(bool uploading) => _uploadingImage = uploading;

    // The preview says whether the API could render the text: a refusal marks the body field, and a later success clears only that mark.
    private void OnBodyTooComplex(bool tooComplex)
    {
        if (tooComplex)
        {
            _errors[ApiFields.Body] = KbEditorCopy.BodyTooComplex;
        }
        else if (_errors.TryGetValue(ApiFields.Body, out var current) && current == KbEditorCopy.BodyTooComplex)
        {
            _errors.Remove(ApiFields.Body);
        }
    }

    private void OnCategoryChanged(ChangeEventArgs e)
    {
        _model.CategoryId = Guid.TryParse(e.Value as string, out var id) ? id : null;
        _errors.Remove(ApiFields.Category);
    }

    private void OnProductChanged(ChangeEventArgs e)
    {
        _model.ProductId = Guid.TryParse(e.Value as string, out var id) ? id : null;

        // A category that the new choice may not use (a shared article may use only a shared category) is dropped, so the form never offers a combination the API refuses.
        if (_model.CategoryId is { } category && _lookups.CategoriesFor(_model.ProductId).All(c => c.Id != category))
        {
            _model.CategoryId = null;
        }

        _errors.Remove(ApiFields.Category);
    }

    private void CheckField(string field)
    {
        var message = _model.Check(field, _creating);
        if (message is null)
        {
            _errors.Remove(field);
        }
        else
        {
            _errors[field] = message;
        }
    }

    private bool CheckAll()
    {
        foreach (var field in new[] { ApiFields.Title, ApiFields.Slug, ApiFields.Summary, ApiFields.Body })
        {
            CheckField(field);
        }

        return _errors.Count == 0;
    }

    // ---- save ----------------------------------------------------------------------------------------------------

    private async Task SaveAsync()
    {
        if (!CanSave)
        {
            return;
        }

        _formError = null;
        if (!CheckAll())
        {
            return;
        }

        var epoch = _epoch;
        var creating = _creating;
        var wasArchived = _model.Status == KbArticleStatuses.Archived;
        _busy = true;
        _saving = true;
        try
        {
            var result = creating
                ? await Kb.CreateAsync(_model.ToCreateRequest(), CancellationToken.None)
                : await Kb.UpdateAsync(_model.Id, _model.ToUpdateRequest(), CancellationToken.None);
            if (_disposed)
            {
                return;
            }

            if (epoch != _epoch)
            {
                // The agent moved to another article while this was on its way. The write is done and is never abandoned, but the screen is not its any more: say so once, change nothing else.
                if (result.IsSuccess)
                {
                    StatusMessages.Show(creating ? KbEditorCopy.Created(result.Value.Title) : KbEditorCopy.Saved(result.Value.Title));
                }

                return;
            }

            if (result.IsFailure)
            {
                ShowSaveFailure(result.Errors, creating);
                return;
            }

            ApplySaved(result.Value, creating, wasArchived);
        }
        finally
        {
            if (epoch == _epoch)
            {
                _busy = false;
                _saving = false;
            }
        }
    }

    private void ApplySaved(KbArticleDto article, bool created, bool wasArchived)
    {
        _model = KbArticleEditorViewModel.From(article);
        _saved = _model.Take();
        _errors.Clear();
        if (created)
        {
            StatusMessages.Show(KbEditorCopy.Created(article.Title));
            _creating = false;

            // The new address is this article: the page already holds it, so the parameter change that follows must not load it again. The form is saved, so the leave guard stays quiet.
            _loadedFor = article.Id.ToString();
            Navigation.NavigateTo($"/kb/{article.Id}", new NavigationOptions { ReplaceHistoryEntry = true });
            return;
        }

        StatusMessages.Show(wasArchived && article.Status == KbArticleStatuses.Draft ? KbEditorCopy.ArchivedReopened : KbEditorCopy.Saved(article.Title));
    }

    private void ShowSaveFailure(IReadOnlyList<ResultError> errors, bool creating)
    {
        var first = errors[0];
        if (WriteOutcomes.Classify(first) == WriteOutcome.Conflict)
        {
            _conflict = true;
        }
        else if (first.Code == ApiErrorCodes.KbArticleNotFound)
        {
            _gone = true;
        }
        else if (first.Code == ApiErrorCodes.KbSlugTaken)
        {
            _errors[ApiFields.Slug] = KbEditorCopy.SlugTaken;
        }
        else if (first.Code == ApiErrorCodes.KbBodyTooComplex)
        {
            _errors[ApiFields.Body] = KbEditorCopy.BodyTooComplex;
        }
        else if (first.Code == ApiErrorCodes.KbCategoryScopeMismatch)
        {
            _errors[ApiFields.Category] = KbEditorCopy.CategoryScopeMismatch;
        }
        else if (first.Code == ApiErrorCodes.KbCategoryNotFound)
        {
            // The category was deleted meanwhile: the field says why the save was refused, and the agent picks another.
            _errors[ApiFields.Category] = KbEditorCopy.CategoryGone;
        }
        else if (ApiErrorCodes.IsUncertainWrite(first.Code))
        {
            _uncertainNote = creating ? KbEditorCopy.CreateUncertain : KbEditorCopy.SaveUncertain;
        }
        else
        {
            MapFieldErrors(errors, creating);
        }
    }

    // A 400 names the field in kebab-case. Anything the form has no field for is shown once, above the form, in the API's words.
    private void MapFieldErrors(IReadOnlyList<ResultError> errors, bool creating)
    {
        foreach (var error in errors)
        {
            var target = FieldOf(error.Target);
            if (target is ApiFields.Title or ApiFields.Summary or ApiFields.Body or ApiFields.Category || (creating && target is ApiFields.Slug))
            {
                _errors.TryAdd(target, error.Message);
            }
            else
            {
                _formError ??= error.Message;
            }
        }
    }

    // The API names a category error by the request property (categoryId) and a publish error by the field (category): both are the category select.
    private static string? FieldOf(string? target) => target is ApiFields.CategoryId or "category-id" ? ApiFields.Category : target;

    // ---- publish and archive -------------------------------------------------------------------------------------

    private async Task PublishAsync()
    {
        if (!CanPublish)
        {
            return;
        }

        _formError = null;
        var epoch = _epoch;
        var id = _model.Id;
        var version = _model.Version;
        var title = _model.Title;
        _busy = true;
        _publishing = true;
        try
        {
            var result = await Kb.PublishAsync(id, version, CancellationToken.None);
            if (_disposed)
            {
                return;
            }

            if (epoch != _epoch)
            {
                if (result.IsSuccess)
                {
                    StatusMessages.Show(KbEditorCopy.Published(title));
                }

                return;
            }

            if (result.IsFailure)
            {
                await ShowPublishFailureAsync(result.Errors[0]);
                return;
            }

            ApplyServer(result.Value);
            StatusMessages.Show(KbEditorCopy.Published(title));
        }
        finally
        {
            if (epoch == _epoch)
            {
                _busy = false;
                _publishing = false;
            }
        }
    }

    private async Task ShowPublishFailureAsync(ResultError error)
    {
        if (WriteOutcomes.Classify(error) == WriteOutcome.Conflict)
        {
            _conflict = true;
        }
        else if (error.Code == ApiErrorCodes.KbBodyTooComplex)
        {
            _errors[ApiFields.Body] = KbEditorCopy.BodyTooComplex;
        }
        else if (error.Code == ApiErrorCodes.KbPublishIncomplete)
        {
            // The API names the field it missed (title, slug, body or category); the form said so already when it could, this is the case where another agent emptied it meanwhile.
            _formError = KbEditorCopy.PublishNeeds(FieldOf(error.Target) ?? string.Empty);
            if (FieldOf(error.Target) is { } target && target is ApiFields.Title or ApiFields.Body or ApiFields.Category)
            {
                _errors[target] = _formError;
            }
        }
        else if (error.Code == ApiErrorCodes.KbArticleNotFound)
        {
            _gone = true;
        }
        else if (error.Code == ApiErrorCodes.ArticleAlreadyPublished)
        {
            StatusMessages.Show(KbEditorCopy.AlreadyPublished);
            await RefreshAfterWriteAsync(_epoch);
        }
        else if (ApiErrorCodes.IsUncertainWrite(error.Code))
        {
            _uncertainNote = KbEditorCopy.PublishUncertain;
        }
        else
        {
            _formError = error.Message;
        }
    }

    private void AskArchive()
    {
        if (CanArchive)
        {
            _archiveOpen = true;
        }
    }

    private void CancelArchive()
    {
        if (!_archiving)
        {
            _archiveOpen = false;
        }
    }

    private async Task ConfirmArchiveAsync()
    {
        if (_archiving || _creating || _conflict || _uncertainNote is not null)
        {
            return;
        }

        var epoch = _epoch;
        var id = _model.Id;
        var version = _model.Version;
        var title = _model.Title;
        _archiving = true;
        try
        {
            var result = await Kb.ArchiveAsync(id, version, CancellationToken.None);
            if (_disposed)
            {
                return;
            }

            if (epoch != _epoch)
            {
                if (result.IsSuccess)
                {
                    StatusMessages.Show(KbEditorCopy.Archived(title));
                }

                return;
            }

            _archiveOpen = false;
            if (result.IsFailure)
            {
                await ShowArchiveFailureAsync(result.Errors[0]);
                return;
            }

            ApplyServer(result.Value);
            StatusMessages.Show(KbEditorCopy.Archived(title));
        }
        finally
        {
            if (epoch == _epoch)
            {
                _archiving = false;
            }
        }
    }

    private async Task ShowArchiveFailureAsync(ResultError error)
    {
        if (WriteOutcomes.Classify(error) == WriteOutcome.Conflict)
        {
            _conflict = true;
        }
        else if (error.Code == ApiErrorCodes.KbArticleNotFound)
        {
            _gone = true;
        }
        else if (error.Code == ApiErrorCodes.ArticleAlreadyArchived)
        {
            StatusMessages.Show(KbEditorCopy.AlreadyArchived);
            await RefreshAfterWriteAsync(_epoch);
        }
        else if (ApiErrorCodes.IsUncertainWrite(error.Code))
        {
            _uncertainNote = KbEditorCopy.ArchiveUncertain;
        }
        else
        {
            _formError = error.Message;
        }
    }

    // The article as the API says it is now (what a publish or an archive answered): the form and the saved snapshot both take it, so the version the next write sends is the new one.
    private void ApplyServer(KbArticleDto article)
    {
        _model = KbArticleEditorViewModel.From(article);
        _saved = _model.Take();
        _errors.Clear();
    }

    // Someone else already did what the agent asked for (published, archived): the screen is read again. If that read fails the form is held until a reload.
    private async Task RefreshAfterWriteAsync(int epoch)
    {
        var loadId = ++_loadId;
        var result = await Presenter.ReloadAsync(_model.Id, _lifetime.Token);
        if (_lifetime.IsCancellationRequested || epoch != _epoch || loadId != _loadId)
        {
            return;
        }

        if (result.IsSuccess)
        {
            _model = KbArticleEditorViewModel.From(result.Value);
            _saved = _model.Take();
            _errors.Clear();
        }
        else if (result.Errors[0].Code == ApiErrorCodes.KbArticleNotFound)
        {
            _gone = true;
        }
        else
        {
            _uncertainNote = KbEditorCopy.ReloadFailed;
        }
    }

    // ---- leaving -------------------------------------------------------------------------------------------------

    private Task OnBeforeNavigationAsync(LocationChangingContext context)
    {
        if (_allowLeave || !IsDirty)
        {
            return Task.CompletedTask;
        }

        context.PreventNavigation();
        _leaveTarget = context.TargetLocation;
        return Task.CompletedTask;
    }

    private Task LeaveAsync()
    {
        var target = _leaveTarget;
        _leaveTarget = null;
        _allowLeave = true;
        if (target is not null)
        {
            Navigation.NavigateTo(target);
        }

        return Task.CompletedTask;
    }

    private Task StayAsync()
    {
        _leaveTarget = null;
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _disposed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
