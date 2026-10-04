using Microsoft.AspNetCore.Components;
using SyntaxCircus.Common;
using TechStrap.Admin.Auth;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Shell;
using TechStrap.Contracts.Agents;
using TechStrap.Contracts.Products;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Admin.Features.Tickets;

/// <summary>
/// Status, assignee, priority, product and tags. Optimistic-free (PHASE-07 spec): a change shows "Saving..." and disables the controls, then the page re-renders from the
/// <see cref="TicketStateDto"/> the API returned. A failure shows the API's message under the control and puts the previous value back; a 409 conflict raises the banner and changes
/// nothing. Every write sends the RowVersion the page currently holds, never a remembered one, and only one write runs at a time so two writes can never race on one version.
/// Which status changes are legal is the API's decision: a refused transition comes back as a message, not as a hidden option.
/// </summary>
public sealed partial class TicketSidebar : IDisposable
{
    private static int _nextId;

    private static readonly string[] SelectableStatuses =
        [TicketStatuses.Open, TicketStatuses.Pending, TicketStatuses.Solved, TicketStatuses.Closed];

    private static readonly string[] PriorityOptions =
        [TicketPriorities.Low, TicketPriorities.Normal, TicketPriorities.High, TicketPriorities.Urgent];

    private readonly int _id = Interlocked.Increment(ref _nextId);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Dictionary<SidebarField, string> _errors = [];
    private readonly Dictionary<SidebarField, int> _revisions = [];
    private readonly HashSet<SidebarField> _uncertain = [];
    private ElementReference _assignee;
    private SidebarField? _saving;
    private bool _busy;

    [Inject]
    private ITicketsClient Tickets { get; set; } = default!;

    [Inject]
    private AgentSession Session { get; set; } = default!;

    [Inject]
    private ShortcutService Shortcuts { get; set; } = default!;

    /// <summary>The ticket as the page holds it, including the current RowVersion and the lookups.</summary>
    [Parameter, EditorRequired]
    public TicketDetailViewModel Ticket { get; set; } = default!;

    /// <summary>Raised with the API's returned state after an accepted change; the page replaces its model from it.</summary>
    [Parameter]
    public EventCallback<TicketStateDto> OnState { get; set; }

    /// <summary>Raised on 409 <c>concurrency-conflict</c>; the page shows the conflict banner.</summary>
    [Parameter]
    public EventCallback OnConflict { get; set; }

    /// <summary>Raised when the agent asks to reload after a write whose outcome is unknown; the page reloads silently and shows the same banner a conflict does.</summary>
    [Parameter]
    public EventCallback OnReload { get; set; }

    /// <summary>Raised when the API answered that the ticket no longer exists.</summary>
    [Parameter]
    public EventCallback OnGone { get; set; }

    private IEnumerable<string> StatusOptions =>
        SelectableStatuses.Contains(Ticket.Status) ? SelectableStatuses : [Ticket.Status, .. SelectableStatuses];

    private IEnumerable<AgentListItemDto> AgentOptions => Ticket.Lookups.Agents;

    // The current product stays selectable even when it is no longer in the active-products list the API returns to agents.
    private IEnumerable<ProductDto> ProductOptions => Ticket.Lookups.Products.Any(p => p.Id == Ticket.ProductId)
        ? Ticket.Lookups.Products
        : [.. Ticket.Lookups.Products, CurrentProductOnly()];

    private bool CanAssignToMe => Session.Agent is { } me && Ticket.AssigneeId != me.Id;

    protected override void OnInitialized() => Shortcuts.Pressed += OnShortcutAsync;

    private ProductDto CurrentProductOnly() =>
        new(Ticket.ProductId, string.Empty, Ticket.ProductName, string.Empty, IsActive: false, new ProductBrandingDto(Ticket.ProductName, null, string.Empty, string.Empty, string.Empty, null, null), Version: 0);

    private string IdOf(SidebarField field) => $"ts-sidebar-{field.ToString().ToLowerInvariant()}-{_id}";

    private string KeyOf(SidebarField field) => $"{field}-{_revisions.GetValueOrDefault(field)}";

    private bool IsSaving(SidebarField field) => _saving == field;

    private string? ErrorOf(SidebarField field) => _errors.GetValueOrDefault(field);

    private bool IsUncertain(SidebarField field) => _uncertain.Contains(field);

    private static string? Text(ChangeEventArgs e) => e.Value as string;

    private Task OnStatusChangedAsync(ChangeEventArgs e) =>
        Text(e) is { Length: > 0 } status && status != Ticket.Status
            ? ApplyAsync(SidebarField.Status, ct => Tickets.ChangeStatusAsync(Ticket.Id, new ChangeTicketStatusRequest(status, Ticket.RowVersion), ct))
            : Revert(SidebarField.Status);

    private Task OnAssigneeChangedAsync(ChangeEventArgs e)
    {
        Guid? assignee = Guid.TryParse(Text(e), out var id) ? id : null;
        return assignee == Ticket.AssigneeId
            ? Revert(SidebarField.Assignee)
            : ApplyAsync(SidebarField.Assignee, ct => Tickets.AssignAsync(Ticket.Id, new AssignTicketRequest(assignee, Ticket.RowVersion), ct));
    }

    private Task AssignToMeAsync() =>
        Session.Agent is { } me
            ? ApplyAsync(SidebarField.Assignee, ct => Tickets.AssignAsync(Ticket.Id, new AssignTicketRequest(me.Id, Ticket.RowVersion), ct))
            : Task.CompletedTask;

    private Task OnPriorityChangedAsync(ChangeEventArgs e) =>
        Text(e) is { Length: > 0 } priority && priority != Ticket.Priority
            ? ApplyAsync(SidebarField.Priority, ct => Tickets.ChangePriorityAsync(Ticket.Id, new ChangeTicketPriorityRequest(priority, Ticket.RowVersion), ct))
            : Revert(SidebarField.Priority);

    private Task OnProductChangedAsync(ChangeEventArgs e) =>
        Guid.TryParse(Text(e), out var product) && product != Ticket.ProductId
            ? ApplyAsync(SidebarField.Product, ct => Tickets.MoveProductAsync(Ticket.Id, new MoveTicketProductRequest(product, Ticket.RowVersion), ct))
            : Revert(SidebarField.Product);

    private Task AddTagAsync(Guid tagId) =>
        ApplyAsync(SidebarField.Tags, ct => Tickets.AddTagAsync(Ticket.Id, new AddTicketTagRequest(tagId, Ticket.RowVersion), ct));

    private Task RemoveTagAsync(Guid tagId) =>
        ApplyAsync(SidebarField.Tags, ct => Tickets.RemoveTagAsync(Ticket.Id, tagId, Ticket.RowVersion, ct));

    /// <summary>The user picked what is already there (or nothing usable): put the select back to the model's value.</summary>
    private Task Revert(SidebarField field)
    {
        _revisions[field] = _revisions.GetValueOrDefault(field) + 1;
        return Task.CompletedTask;
    }

    private async Task ApplyAsync(SidebarField field, Func<CancellationToken, Task<Result<TicketStateDto>>> send)
    {
        if (_busy)
        {
            await Revert(field);
            return;
        }

        _busy = true;
        _saving = field;
        _errors.Remove(field);
        _uncertain.Remove(field);
        StateHasChanged();
        try
        {
            var result = await send(_lifetime.Token);
            if (result.IsSuccess)
            {
                await OnState.InvokeAsync(result.Value);
            }
            else
            {
                await HandleFailureAsync(field, result.Errors[0]);
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // The screen was closed mid-request.
        }
        finally
        {
            _busy = false;
            _saving = null;
        }

        // Always redraw the selects from the model: after a success the new value, after a failure the old one.
        await Revert(field);
    }

    private async Task HandleFailureAsync(SidebarField field, ResultError error)
    {
        if (error.Code == ApiErrorCodes.ConcurrencyConflict)
        {
            await OnConflict.InvokeAsync();
        }
        else if (error.Kind == ResultErrorKind.NotFound)
        {
            await OnGone.InvokeAsync();
        }
        else if (error.Code is ApiErrorCodes.ApiTimeout or ApiErrorCodes.ApiUnavailable or ApiErrorCodes.UnexpectedResponse or ApiErrorCodes.ApiError)
        {
            // The write may have been saved before the answer was lost: never a bare "try again", the agent reloads to see the current state.
            _errors[field] = SidebarCopy.ChangeUncertain;
            _uncertain.Add(field);
        }
        else
        {
            // Includes ticket-closed and invalid-status-transition: the API's own sentence says why.
            _errors[field] = error.Message;
        }
    }

    private async Task ReloadAsync()
    {
        _errors.Clear();
        _uncertain.Clear();
        await OnReload.InvokeAsync();
    }

    private async Task OnShortcutAsync(ShortcutAction action)
    {
        if (action == ShortcutAction.FocusAssignee)
        {
            await _assignee.FocusAsync();
        }
    }

    public void Dispose()
    {
        Shortcuts.Pressed -= OnShortcutAsync;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
