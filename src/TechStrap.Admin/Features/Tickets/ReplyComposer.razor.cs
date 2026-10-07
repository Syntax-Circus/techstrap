using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Logging;
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Components.Ui;
using TechStrap.Admin.Features.Kb;
using TechStrap.Admin.Features.Live;
using TechStrap.Admin.Features.Shell;
using TechStrap.Contracts.Intake;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Admin.Features.Tickets;

/// <summary>
/// The public reply / internal note composer. The segmented control, not the text, decides the mode; each mode keeps its own draft in <see cref="DraftStore"/>;
/// and the mode never changes by itself (not after a send, not after a conflict reload). Every write sends the ticket's current RowVersion. On ANY failure the text and
/// the picked files stay exactly as they were, so a retry rebuilds the same request; only an accepted send clears them. Sending is disabled while a request is in flight.
/// </summary>
public sealed partial class ReplyComposer : IDisposable
{
    private readonly List<IBrowserFile> _files = [];
    private readonly List<string> _fileProblems = [];
    private readonly string _id = Guid.NewGuid().ToString("N")[..8];
    private ComposerDraft _draft = new();
    private Guid _draftTicket;
    private Guid _draftProduct;
    private ElementReference _text;
    private string? _error;
    private bool _disposed;
    private bool _filesDropped;
    private bool _pickerOpen;
    private bool _composing;
    private Guid _composingTicket;
    private DateTimeOffset _composingSentAt;

    [Inject]
    private ITicketsClient Tickets { get; set; } = default!;

    [Inject]
    private ILogger<ReplyComposer> Logger { get; set; } = default!;

    [Inject]
    private DraftStore Drafts { get; set; } = default!;

    [Inject]
    private StatusMessageService StatusMessages { get; set; } = default!;

    [Inject]
    private ShortcutService Shortcuts { get; set; } = default!;

    [Inject]
    private ITicketLiveClient LiveClient { get; set; } = default!;

    [Inject]
    private TimeProvider Time { get; set; } = default!;

    [Parameter, EditorRequired]
    public Guid TicketId { get; set; }

    [Parameter, EditorRequired]
    public string TicketNumber { get; set; } = string.Empty;

    [Parameter, EditorRequired]
    public string RequesterEmail { get; set; } = string.Empty;

    /// <summary>The ticket's product: the article picker offers its published articles and the shared ones. A ticket that moves to another product changes it.</summary>
    [Parameter, EditorRequired]
    public Guid ProductId { get; set; }

    /// <summary>The ticket's current RowVersion, replaced by the page after every write and every reload.</summary>
    [Parameter, EditorRequired]
    public uint RowVersion { get; set; }

    /// <summary>Raised once when the API accepted the message; the page applies <see cref="AgentMessageResponse.Ticket"/> and refreshes.</summary>
    [Parameter]
    public EventCallback<AgentMessageResponse> OnSent { get; set; }

    /// <summary>Raised when the API answered 409 <c>concurrency-conflict</c>; the page shows the conflict banner.</summary>
    [Parameter]
    public EventCallback OnConflict { get; set; }

    /// <summary>Raised when the API answered that the ticket no longer exists.</summary>
    [Parameter]
    public EventCallback OnGone { get; set; }

    /// <summary>The explicit message of the last attempt, else the "check the timeline" notice that survives mode switches and a closed screen until the next send.</summary>
    private string? ShownError => _error ?? _draft.UncertainSend switch
    {
        ComposerMode.PublicReply => _filesDropped ? TicketCopy.ReplyUncertainFilesGone : ReplyComposerCopy.ReplyUncertain,
        ComposerMode.InternalNote => ReplyComposerCopy.NoteUncertain,
        _ => null,
    };

    /// <summary>A write for this ticket is in flight, started by this composer or by one that has since been closed.</summary>
    private bool Sending => _draft.InFlight;

    private bool IsPublic => _draft.Mode == ComposerMode.PublicReply;

    private string TextId => $"ts-composer-text-{_id}";

    private string FilesId => $"ts-composer-files-{_id}";

    private string StatusId => $"ts-composer-status-{_id}";

    private string PickerId => $"ts-composer-picker-{_id}";

    private string LinkedLabelId => $"ts-composer-linked-{_id}";

    private string SectionCss => IsPublic ? "ts-composer ts-composer--public" : "ts-composer ts-composer--note";

    private bool HasUnsentText => !string.IsNullOrWhiteSpace(_draft.PublicText) || !string.IsNullOrWhiteSpace(_draft.NoteText);

    private static string Accept => string.Join(',', IntakeLimits.AllowedExtensions);

    private string Text
    {
        get => IsPublic ? _draft.PublicText : _draft.NoteText;
        set
        {
            if (IsPublic)
            {
                _draft.PublicText = value;
            }
            else
            {
                _draft.NoteText = value;
            }

            if (string.IsNullOrEmpty(value))
            {
                StopComposing();
            }
            else
            {
                NoteTyping();
            }
        }
    }

    private string StatusAfter
    {
        get => _draft.StatusAfter;
        set => _draft.StatusAfter = value;
    }

    protected override void OnInitialized() => Shortcuts.Pressed += OnShortcutAsync;

    protected override void OnParametersSet()
    {
        // The hint belongs to the ticket it was sent for: moving to another ticket clears it first.
        if (_draftTicket != TicketId)
        {
            StopComposing();
        }

        // A ticket that moves to another product can no longer link that product's own articles (the API refuses them): those chips go and the shared ones stay.
        if (_draftTicket == TicketId && _draftProduct != Guid.Empty && _draftProduct != ProductId)
        {
            _draft.LinkedArticles.RemoveAll(a => !a.IsShared);
        }

        _draftProduct = ProductId;
        if (_draftTicket != TicketId)
        {
            _draftTicket = TicketId;
            _draft.Changed -= OnDraftChanged;
            _draft = Drafts.Get(TicketId);
            _draft.Changed += OnDraftChanged;
            _error = null;
            _fileProblems.Clear();

            // Files never travel between composers or tickets: their handles die with the input element that produced them.
            _files.Clear();
            _filesDropped = _draft.FilesDropped;
            _draft.FilesDropped = false;
        }
    }

    private string ModeCss(ComposerMode mode) => mode == _draft.Mode ? "btn btn-primary" : "btn btn-outline-secondary";

    private string PressedValue(ComposerMode mode) => mode == _draft.Mode ? "true" : "false";

    private void SetMode(ComposerMode mode)
    {
        _draft.Mode = mode;
        _error = null;
    }

    private Task OnFilesPickedAsync(InputFileChangeEventArgs e)
    {
        // Each pick replaces the list: the browser invalidates the handles of the previous pick when the input changes.
        _fileProblems.Clear();
        _files.Clear();
        _filesDropped = false;
        foreach (var file in e.GetMultipleFiles(Math.Max(1, e.FileCount)))
        {
            if (_files.Count >= IntakeLimits.MaxFiles)
            {
                _fileProblems.Add(string.Format(ReplyComposerCopy.TooManyFiles, IntakeLimits.MaxFiles));
                break;
            }

            if (!IntakeLimits.AllowedExtensions.Contains(Path.GetExtension(file.Name), StringComparer.OrdinalIgnoreCase))
            {
                _fileProblems.Add(string.Format(ReplyComposerCopy.FileTypeNotAllowed, file.Name));
            }
            else if (file.Size > IntakeLimits.MaxFileBytes)
            {
                _fileProblems.Add(string.Format(ReplyComposerCopy.FileTooLarge, file.Name, TicketDisplay.FileSize(IntakeLimits.MaxFileBytes)));
            }
            else
            {
                _files.Add(file);
            }
        }

        return Task.CompletedTask;
    }

    // A write settled (possibly one started by a composer that is gone): show its outcome. Raised on any thread.
    private void OnDraftChanged() => _ = InvokeAsync(StateHasChanged);

    private void RemoveFile(IBrowserFile file) => _files.Remove(file);

    private void TogglePicker() => _pickerOpen = !_pickerOpen;

    // The picker offers published articles only; the draft keeps the choice, in the order it was made, never twice and never beyond the limit the API enforces.
    private void AddArticle(ArticleChoice article)
    {
        if (_draft.LinkedArticles.Count < TicketOperationLimits.MaxLinkedArticles && _draft.LinkedArticles.All(a => a.Id != article.Id))
        {
            _draft.LinkedArticles.Add(article);
        }
    }

    private void RemoveArticle(ArticleChoice article) => _draft.LinkedArticles.Remove(article);

    /// <param name="statusAfterOverride">"Send and solve" passes Solved; otherwise the status choice in the draft applies.</param>
    private async Task SubmitAsync(string? statusAfterOverride = null)
    {
        if (Sending)
        {
            return;
        }

        StopComposing();

        var mode = _draft.Mode;
        var text = mode == ComposerMode.PublicReply ? _draft.PublicText : _draft.NoteText;
        if (string.IsNullOrWhiteSpace(text))
        {
            _error = mode == ComposerMode.PublicReply ? ReplyComposerCopy.EmptyReply : ReplyComposerCopy.EmptyNote;
            return;
        }

        // The write is never cancelled by the screen closing: the server may commit it, and a cancelled call would leave the draft looking unsent.
        // Everything after the await works on the captured draft and the text that was sent, so it settles the store even when this component is gone.
        var draft = _draft;
        var ticketId = TicketId;
        var number = TicketNumber;
        var rowVersion = RowVersion;
        var statusAfter = StatusAfterFor(statusAfterOverride);
        var files = Attachments();

        // Only a public reply links articles; a note never does, whatever the draft holds. The ids are taken now, so a change made while the send is on its way is not part of it.
        IReadOnlyList<Guid> articleIds = mode == ComposerMode.PublicReply ? [.. draft.LinkedArticles.Select(a => a.Id)] : [];
        _error = null;
        draft.UncertainSend = null;
        draft.InFlightMode = mode;
        draft.InFlight = true;
        StateHasChanged();
        draft.RaiseChanged();

        Result<AgentMessageResponse> result;
        try
        {
            result = mode == ComposerMode.PublicReply
                ? await Tickets.ReplyAsync(ticketId, new AddAgentReplyRequest(text, articleIds, statusAfter, rowVersion), files, CancellationToken.None)
                : await Tickets.AddNoteAsync(ticketId, new AddInternalNoteRequest(text, rowVersion), CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Only the exception type is logged: its message can carry a file name or the requester's address.
            Logger.LogWarning("A send ended with an unmapped {ExceptionType}; treated as uncertain.", ex.GetType().Name);

            // Anything the client did not map (a stale input or a lost circuit while a file stream was read, for instance) leaves the outcome unknown:
            // the server may have committed the write. Settle conservatively and never let the fault reach the renderer.
            draft.UncertainSend = mode;
            Settle(draft);
            return;
        }

        try
        {
            await HandleResultAsync(mode, draft, number, text, articleIds, result);
        }
        finally
        {
            Settle(draft);
        }
    }

    private static void Settle(ComposerDraft draft)
    {
        if (draft.InFlight)
        {
            draft.InFlight = false;
            draft.RaiseChanged();
        }
    }

    // Each attempt reopens the picked files, so a retry after a failure or a conflict sends the same bytes (a stream cannot be read twice).
    // The browser's file name is cleaned first: an empty or odd name would make the multipart body throw.
    private List<ReplyAttachment> Attachments() =>
        _files.Select(file => new ReplyAttachment(AttachmentFileName.Clean(file.Name), file.ContentType, () => file.OpenReadStream(IntakeLimits.MaxFileBytes))).ToList();

    private string? StatusAfterFor(string? statusAfterOverride)
    {
        if (statusAfterOverride is not null)
        {
            return statusAfterOverride;
        }

        return string.IsNullOrEmpty(_draft.StatusAfter) ? null : _draft.StatusAfter;
    }

    // Runs after the write finished, possibly after this component was disposed: the shared draft is settled first, and the UI callbacks only run while alive.
    private async Task HandleResultAsync(ComposerMode mode, ComposerDraft draft, string number, string sentText, IReadOnlyList<Guid> sentArticleIds, Result<AgentMessageResponse> result)
    {
        if (result.IsSuccess)
        {
            // Only an accepted send clears anything, and only the mode that was sent.
            // Compare and clear: text that differs from what was sent is the agent's newer text and stays.
            if (mode == ComposerMode.PublicReply)
            {
                if (draft.PublicText == sentText)
                {
                    draft.PublicText = string.Empty;
                }

                // The articles that were sent are linked now; one the agent added while the send was on its way stays for the next reply.
                draft.LinkedArticles.RemoveAll(a => sentArticleIds.Contains(a.Id));

                draft.FilesDropped = false;
                if (!_disposed)
                {
                    _files.Clear();
                    _fileProblems.Clear();
                }

                StatusMessages.Show(ReplyComposerCopy.ReplySent(number));
            }
            else
            {
                if (draft.NoteText == sentText)
                {
                    draft.NoteText = string.Empty;
                }

                StatusMessages.Show(ReplyComposerCopy.NoteAdded(number));
            }

            Settle(draft);
            if (!_disposed)
            {
                await OnSent.InvokeAsync(result.Value);
            }

            return;
        }

        var error = result.Errors[0];
        var outcome = WriteOutcomes.Classify(error);
        if (outcome == WriteOutcome.Uncertain)
        {
            // The write may have been saved before the answer was lost, so never offer a bare "Try again": the notice lives in the draft and
            // tells the agent to check the timeline, even on the next mount.
            draft.UncertainSend = mode;
            return;
        }

        if (_disposed)
        {
            return;
        }

        if (outcome == WriteOutcome.Conflict)
        {
            _error = ReplyComposerCopy.Conflict;
            await OnConflict.InvokeAsync();
        }
        else if (outcome == WriteOutcome.Gone)
        {
            await OnGone.InvokeAsync();
        }
        else if (outcome == WriteOutcome.Closed)
        {
            _error = error.Message;
        }
        else if (error.Code is ApiErrorCodes.KbArticleNotLinkable or ApiErrorCodes.ArticleNotFound)
        {
            _error = ReplyComposerCopy.ArticleNotLinkable;
        }
        else
        {
            _error = $"{(mode == ComposerMode.PublicReply ? ReplyComposerCopy.ReplyFailed : ReplyComposerCopy.NoteFailed)} {error.Message}";
        }
    }

    private async Task OnShortcutAsync(ShortcutAction action)
    {
        if (Sending)
        {
            return;
        }

        switch (action)
        {
            case ShortcutAction.Reply:
                await FocusAsync(ComposerMode.PublicReply);
                break;
            case ShortcutAction.Note:
                await FocusAsync(ComposerMode.InternalNote);
                break;
            case ShortcutAction.Send:
                await InvokeAsync(async () =>
                {
                    await SubmitAsync();
                    StateHasChanged();
                });
                break;
        }
    }

    private async Task FocusAsync(ComposerMode mode)
    {
        await InvokeAsync(() =>
        {
            SetMode(mode);
            StateHasChanged();
        });
        await _text.FocusAsync();
    }

    /// <summary>
    /// The agent is typing: tell the hub, at most once per <see cref="LiveDefaults.ComposingThrottle"/> (the server's lease is 10 seconds, so a refresh every 4 keeps "replying" alive for the others).
    /// Never awaited and never throws: a hub that is down must not touch the composer.
    /// </summary>
    private void NoteTyping()
    {
        if (!LiveClient.IsEnabled)
        {
            return;
        }

        var now = Time.GetUtcNow();
        if (_composing && now - _composingSentAt < LiveDefaults.ComposingThrottle)
        {
            return;
        }

        _composing = true;
        _composingSentAt = now;
        _composingTicket = TicketId;
        _ = SendComposingAsync(_composingTicket, true);
    }

    /// <summary>Blur, submit, an emptied text box, another ticket and disposal all end the hint, once.</summary>
    private void StopComposing()
    {
        if (!_composing)
        {
            return;
        }

        _composing = false;
        _ = SendComposingAsync(_composingTicket, false);
    }

    private void OnBlur() => StopComposing();

    private async Task SendComposingAsync(Guid ticketId, bool isComposing)
    {
        try
        {
            await LiveClient.SetComposingAsync(ticketId, isComposing);
        }
        catch (Exception ex)
        {
            Logger.LogWarning("The composing hint could not be sent ({ExceptionType}).", ex.GetType().Name);
        }
    }

    public void Dispose()
    {
        _disposed = true;
        StopComposing();
        Shortcuts.Pressed -= OnShortcutAsync;
        _draft.Changed -= OnDraftChanged;

        // The files' handles die with this component's input element; the next composer for the ticket says so. A write still in flight is left to finish.
        if (_files.Count > 0)
        {
            _draft.FilesDropped = true;
        }
    }
}
