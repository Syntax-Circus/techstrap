using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Components.Ui;
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
    private ElementReference _text;
    private string? _error;
    private bool _sending;
    private bool _disposed;
    private bool _filesDropped;

    [Inject]
    private ITicketsClient Tickets { get; set; } = default!;

    [Inject]
    private DraftStore Drafts { get; set; } = default!;

    [Inject]
    private StatusMessageService StatusMessages { get; set; } = default!;

    [Inject]
    private ShortcutService Shortcuts { get; set; } = default!;

    [Parameter, EditorRequired]
    public Guid TicketId { get; set; }

    [Parameter, EditorRequired]
    public string TicketNumber { get; set; } = string.Empty;

    [Parameter, EditorRequired]
    public string RequesterEmail { get; set; } = string.Empty;

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
        ComposerMode.PublicReply => ReplyComposerCopy.ReplyUncertain,
        ComposerMode.InternalNote => ReplyComposerCopy.NoteUncertain,
        _ => null,
    };

    private bool IsPublic => _draft.Mode == ComposerMode.PublicReply;

    private string TextId => $"ts-composer-text-{_id}";

    private string FilesId => $"ts-composer-files-{_id}";

    private string StatusId => $"ts-composer-status-{_id}";

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
        if (_draftTicket != TicketId)
        {
            _draftTicket = TicketId;
            _draft = Drafts.Get(TicketId);
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

    private void RemoveFile(IBrowserFile file) => _files.Remove(file);

    /// <param name="statusAfterOverride">"Send and solve" passes Solved; otherwise the status choice in the draft applies.</param>
    private async Task SubmitAsync(string? statusAfterOverride = null)
    {
        if (_sending)
        {
            return;
        }

        var mode = _draft.Mode;
        var text = mode == ComposerMode.PublicReply ? _draft.PublicText : _draft.NoteText;
        if (string.IsNullOrWhiteSpace(text))
        {
            _error = mode == ComposerMode.PublicReply ? ReplyComposerCopy.EmptyReply : ReplyComposerCopy.EmptyNote;
            return;
        }

        _sending = true;
        _error = null;
        _draft.UncertainSend = null;
        StateHasChanged();

        // The write is never cancelled by the screen closing: the server may commit it, and a cancelled call would leave the draft looking unsent.
        // Everything below the await works on the captured draft, so it still settles the store when this component is gone.
        var draft = _draft;
        var ticketId = TicketId;
        var number = TicketNumber;
        try
        {
            var result = mode == ComposerMode.PublicReply
                ? await Tickets.ReplyAsync(ticketId, new AddAgentReplyRequest(text, [], StatusAfterFor(statusAfterOverride), RowVersion), Attachments(), CancellationToken.None)
                : await Tickets.AddNoteAsync(ticketId, new AddInternalNoteRequest(text, RowVersion), CancellationToken.None);
            await HandleResultAsync(mode, draft, number, result);
        }
        finally
        {
            _sending = false;
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

    private static bool IsUncertain(string code) =>
        code is ApiErrorCodes.ApiTimeout or ApiErrorCodes.ApiUnavailable or ApiErrorCodes.UnexpectedResponse or ApiErrorCodes.ApiError;

    // Runs after the write finished, possibly after this component was disposed: the shared draft is settled first, and the UI callbacks only run while alive.
    private async Task HandleResultAsync(ComposerMode mode, ComposerDraft draft, string number, Result<AgentMessageResponse> result)
    {
        if (result.IsSuccess)
        {
            // Only an accepted send clears anything, and only the mode that was sent.
            if (mode == ComposerMode.PublicReply)
            {
                draft.PublicText = string.Empty;
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
                draft.NoteText = string.Empty;
                StatusMessages.Show(ReplyComposerCopy.NoteAdded(number));
            }

            if (!_disposed)
            {
                await OnSent.InvokeAsync(result.Value);
            }

            return;
        }

        var error = result.Errors[0];
        if (IsUncertain(error.Code))
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

        if (error.Code == ApiErrorCodes.ConcurrencyConflict)
        {
            _error = ReplyComposerCopy.Conflict;
            await OnConflict.InvokeAsync();
        }
        else if (error.Kind == ResultErrorKind.NotFound)
        {
            await OnGone.InvokeAsync();
        }
        else if (error.Code == ApiErrorCodes.TicketClosed)
        {
            _error = error.Message;
        }
        else
        {
            _error = $"{(mode == ComposerMode.PublicReply ? ReplyComposerCopy.ReplyFailed : ReplyComposerCopy.NoteFailed)} {error.Message}";
        }
    }

    private async Task OnShortcutAsync(ShortcutAction action)
    {
        if (_sending)
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

    public void Dispose()
    {
        _disposed = true;
        Shortcuts.Pressed -= OnShortcutAsync;

        // The files' handles die with this component's input element; the next composer for the ticket says so. A write still in flight is left to finish.
        if (_files.Count > 0)
        {
            _draft.FilesDropped = true;
        }
    }
}
