using TechStrap.Admin.Components.Ui;
using TechStrap.Contracts.Agents;
using TechStrap.Contracts.Products;
using TechStrap.Contracts.Tags;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Admin.Features.Tickets;

/// <summary>
/// The reference data the ticket screen resolves ids against: the API's events and state responses carry ids only (no product, agent or tag names).
/// Agents see only active agents and active products, so every lookup has a fallback for an id that is no longer in the list.
/// </summary>
public sealed record TicketLookups(IReadOnlyList<ProductDto> Products, IReadOnlyList<AgentListItemDto> Agents, IReadOnlyList<TagDto> Tags)
{
    public const string UnknownProduct = "another product";
    public const string UnknownAgent = "another agent";
    public const string UnknownTag = "a deleted tag";

    public static TicketLookups Empty { get; } = new([], [], []);

    public string ProductName(Guid? id) => Products.FirstOrDefault(p => p.Id == id)?.Name ?? UnknownProduct;

    public string AgentName(Guid? id) => Agents.FirstOrDefault(a => a.Id == id)?.DisplayLabel ?? UnknownAgent;

    public TagDto? Tag(Guid id) => Tags.FirstOrDefault(t => t.Id == id);

    public string TagName(Guid? id) => id is { } tagId && Tag(tagId) is { } tag ? tag.Name : UnknownTag;
}

public enum TimelineEntryKind
{
    Message,
    Event,
}

/// <summary>One line of the single chronological stream (UX-BRIEF-admin): a message or a change, never both.</summary>
public sealed record TimelineEntryViewModel(Guid Id, TimelineEntryKind Kind, DateTimeOffset At, string Actor, string Text, MessageViewModel? Message);

public sealed record MessageViewModel(
    Guid Id,
    EntryKind Kind,
    string Author,
    DateTimeOffset At,
    string BodyHtml,
    IReadOnlyList<AttachmentDto> Attachments,
    IReadOnlyList<LinkedArticleDto> Articles);

public sealed record MetadataItem(string Key, string Value);

/// <param name="Trusted">True only when the ticket came in on a trusted API key (<c>MetadataTrusted</c>). Everything else is labeled untrusted.</param>
/// <param name="Readable">False when the stored JSON could not be read; the panel then says so instead of showing nothing.</param>
public sealed record MetadataViewModel(bool Trusted, bool Readable, IReadOnlyList<MetadataItem> Items);

/// <summary>Everything the ticket screen draws. Writes replace the status fields from the API's returned <see cref="TicketStateDto"/> through <see cref="WithState"/>.</summary>
public sealed record TicketDetailViewModel(
    Guid Id,
    string Number,
    string Subject,
    string Status,
    string Priority,
    bool IsSpam,
    Guid ProductId,
    string ProductName,
    Guid? AssigneeId,
    string? AssigneeName,
    IReadOnlyList<TicketTagDto> Tags,
    TicketRequesterDto Requester,
    string Channel,
    DateTimeOffset CreatedAt,
    Guid? ParentTicketId,
    string? ParentNumber,
    MetadataViewModel? Metadata,
    uint RowVersion,
    IReadOnlyList<TimelineEntryViewModel> Timeline,
    TicketLookups Lookups)
{
    /// <summary>Closed tickets are read-only: no composer, no actions (a customer reply starts a follow-up).</summary>
    public bool IsClosed => Status == TicketStatuses.Closed;

    public StampStatus Stamp => TicketDisplay.Stamp(Status, IsSpam);

    /// <summary>Who wrote in: the requester's name, or their address when they gave none.</summary>
    public string Caller => string.IsNullOrWhiteSpace(Requester.Name) ? Requester.Email : Requester.Name;

    /// <summary>
    /// The model after a write: status, priority, product, assignee, tags, spam flag and RowVersion all come from the response, so the display
    /// can never run ahead of what the server accepted. Names are resolved from the lookups; an id the lookups lack keeps the name already shown.
    /// </summary>
    public TicketDetailViewModel WithState(TicketStateDto state)
    {
        var assigneeName = state.AssigneeId is null
            ? null
            : state.AssigneeId == AssigneeId
                ? AssigneeName
                : Lookups.AgentName(state.AssigneeId);
        var productName = state.ProductId == ProductId ? ProductName : Lookups.ProductName(state.ProductId);
        var tags = state.TagIds
            .Select(id => Tags.FirstOrDefault(t => t.Id == id) ?? (Lookups.Tag(id) is { } tag ? new TicketTagDto(tag.Id, tag.Name, tag.Colour) : null))
            .OfType<TicketTagDto>()
            .ToList();
        return this with
        {
            Status = state.Status,
            Priority = state.Priority,
            IsSpam = state.IsSpam,
            ProductId = state.ProductId,
            ProductName = productName,
            AssigneeId = state.AssigneeId,
            AssigneeName = assigneeName,
            Tags = tags,
            RowVersion = state.RowVersion,
        };
    }
}
