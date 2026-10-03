using Microsoft.EntityFrameworkCore;
using SyntaxCircus.Common;
using TechStrap.Application.Persistence;
using TechStrap.Application.Tickets;
using TechStrap.Domain.Tickets;
using TechStrap.Infrastructure.Persistence.Mapping;
using TechStrap.Infrastructure.Persistence.Records;

namespace TechStrap.Infrastructure.Persistence.Repositories;

internal sealed class TicketRepository(TechStrapDbContext context) : ITicketRepository
{
    private static readonly TicketStatus[] ActiveStatuses = [TicketStatus.New, TicketStatus.Open, TicketStatus.Pending];
    private static readonly TicketStatus[] OpenViewStatuses = [TicketStatus.New, TicketStatus.Open];

    /// <summary>An exact ticket number is the strongest possible match; this beats any text rank (ranks are below 1).</summary>
    private const float ExactNumberScore = 10f;

    public async Task<Ticket?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        (await context.Set<TicketRecord>().Include(t => t.Tags).FirstOrDefaultAsync(t => t.Id == id, cancellationToken))?.ToDomain();

    public async Task<Ticket?> GetByNumberAsync(string number, CancellationToken cancellationToken)
    {
        if (!TicketNumber.TryParse(number, out var parsed))
        {
            return null;
        }

        var stored = parsed.ToString();
        return (await context.Set<TicketRecord>().Include(t => t.Tags).FirstOrDefaultAsync(t => t.Number == stored, cancellationToken))?.ToDomain();
    }

    public void Add(Ticket ticket)
    {
        context.Set<TicketRecord>().Add(ticket.ToRecord());
        StagePending(ticket);
    }

    public void Update(Ticket ticket)
    {
        var record = context.FindLoaded<TicketRecord>(ticket.Id);
        ticket.CopyTo(record);

        var entry = context.Entry(record);
        if (entry.State != EntityState.Added)
        {
            // The check is against the version the caller's Domain object carries, not the one this scope loaded (D-026).
            context.ApplyOriginalVersion(record, ticket.Version);

            // An UPDATE is what checks xmin. When the copy changed no column (a follow-up only adds an event to its Closed parent) the
            // row would not take part in the transaction, so two concurrent changes could both succeed: touch the row so it does.
            if (entry.State == EntityState.Unchanged && (ticket.PendingEvents.Count > 0 || ticket.PendingMessages.Count > 0))
            {
                entry.Property(t => t.LastActivityAt).IsModified = true;
            }
        }

        StagePending(ticket);
    }

    /// <summary>
    /// Agent queue (D-024). Agent-only: with search text it matches internal-note bodies too, and the summaries carry the requester
    /// email and last activity time, so this must never serve a customer.
    /// </summary>
    public async Task<PagedResult<TicketSummary>> ListAsync(TicketQuery query, CancellationToken cancellationToken)
    {
        var page = Paging.NormalizePage(query.Page);
        var pageSize = Paging.NormalizePageSize(query.PageSize);
        var offset = Paging.Offset(page, pageSize);

        var tickets = ViewQuery(context.Set<TicketRecord>().AsNoTracking(), query);
        if (query.ProductId is { } productId)
        {
            tickets = tickets.Where(t => t.ProductId == productId);
        }

        if (query.Status is { } status)
        {
            tickets = tickets.Where(t => t.Status == status);
        }

        if (query.Priority is { } priority)
        {
            tickets = tickets.Where(t => t.Priority == priority);
        }

        if (query.AssigneeId is { } assigneeId)
        {
            tickets = tickets.Where(t => t.AssigneeId == assigneeId);
        }

        if (query.RequesterId is { } requesterId)
        {
            tickets = tickets.Where(t => t.RequesterId == requesterId);
        }

        if (query.TagId is { } tagId)
        {
            tickets = tickets.Where(t => t.Tags.Any(link => link.TagId == tagId));
        }

        var text = SearchText.Normalize(query.SearchText);
        var total = 0;
        List<TicketSummary> rows;
        if (string.IsNullOrEmpty(text))
        {
            total = await tickets.CountAsync(cancellationToken);
            rows = await tickets
                .Join(context.Set<RequesterRecord>(), t => t.RequesterId, r => r.Id, (t, r) => new { Ticket = t, r.Email })
                .OrderByDescending(x => x.Ticket.LastActivityAt).ThenByDescending(x => x.Ticket.Id)
                .Skip(offset).Take(pageSize)
                .Select(x => new TicketSummary(
                    x.Ticket.Id, x.Ticket.Number, x.Ticket.Subject, x.Ticket.Status, x.Ticket.Priority, x.Ticket.ProductId, x.Ticket.RequesterId, x.Email,
                    x.Ticket.AssigneeId, x.Ticket.IsSpam, x.Ticket.CreatedAt, x.Ticket.LastActivityAt))
                .ToListAsync(cancellationToken);
        }
        else
        {
            var ranked = Rank(tickets, text);
            total = await ranked.CountAsync(cancellationToken);
            rows = await ranked
                .Join(context.Set<RequesterRecord>(), x => x.Ticket.RequesterId, r => r.Id, (x, r) => new { x.Ticket, x.Score, r.Email })
                .OrderByDescending(x => x.Score).ThenByDescending(x => x.Ticket.LastActivityAt).ThenByDescending(x => x.Ticket.Id)
                .Skip(offset).Take(pageSize)
                .Select(x => new TicketSummary(
                    x.Ticket.Id, x.Ticket.Number, x.Ticket.Subject, x.Ticket.Status, x.Ticket.Priority, x.Ticket.ProductId, x.Ticket.RequesterId, x.Email,
                    x.Ticket.AssigneeId, x.Ticket.IsSpam, x.Ticket.CreatedAt, x.Ticket.LastActivityAt))
                .ToListAsync(cancellationToken);
        }

        return new PagedResult<TicketSummary>(rows, page, pageSize, total);
    }

    /// <summary>
    /// A ticket is kept when its subject, one of its messages (public or internal: ticket search is agent-only) or its exact number
    /// matches, and the score adds the subject rank (weight A), the best message rank (weight B) and a boost for an exact number, so
    /// a subject match outranks a body match.
    /// </summary>
    private IQueryable<RankedTicket> Rank(IQueryable<TicketRecord> tickets, string text)
    {
        var number = TicketNumber.TryParse(text, out var parsed) ? parsed.ToString() : null;
        var messages = context.Set<MessageRecord>();
        return tickets
            .Where(t => t.SearchVector.Matches(EF.Functions.WebSearchToTsQuery(FullTextSearch.Config, text))
                || messages.Any(m => m.TicketId == t.Id && m.SearchVector.Matches(EF.Functions.WebSearchToTsQuery(FullTextSearch.Config, text)))
                || t.Number == number)
            .Select(t => new RankedTicket
            {
                Ticket = t,
                Score =
                    (t.SearchVector.Matches(EF.Functions.WebSearchToTsQuery(FullTextSearch.Config, text))
                        ? t.SearchVector.Rank(EF.Functions.WebSearchToTsQuery(FullTextSearch.Config, text))
                        : 0f)
                    + (messages
                        .Where(m => m.TicketId == t.Id && m.SearchVector.Matches(EF.Functions.WebSearchToTsQuery(FullTextSearch.Config, text)))
                        .Max(m => (float?)m.SearchVector.Rank(EF.Functions.WebSearchToTsQuery(FullTextSearch.Config, text))) ?? 0f)
                    + (t.Number == number ? ExactNumberScore : 0f),
            });
    }

    private sealed class RankedTicket
    {
        public TicketRecord Ticket { get; set; } = null!;

        public float Score { get; set; }
    }

    public async Task<IReadOnlyList<Message>> GetMessagesAsync(Guid ticketId, bool publicOnly, CancellationToken cancellationToken)
    {
        var messages = context.Set<MessageRecord>().AsNoTracking().Where(m => m.TicketId == ticketId);
        if (publicOnly)
        {
            messages = messages.Where(m => m.Visibility == MessageVisibility.Public);
        }

        var records = await messages.OrderBy(m => m.CreatedAt).ThenBy(m => m.Id).ToListAsync(cancellationToken);
        return [.. records.Select(m => m.ToDomain())];
    }

    public async Task<IReadOnlyList<TicketEvent>> GetEventsAsync(Guid ticketId, CancellationToken cancellationToken)
    {
        var records = await context.Set<TicketEventRecord>().AsNoTracking()
            .Where(e => e.TicketId == ticketId)
            .OrderBy(e => e.OccurredAt).ThenBy(e => e.Id)
            .ToListAsync(cancellationToken);
        return [.. records.Select(e => e.ToDomain())];
    }

    public async Task<Attachment?> GetAttachmentAsync(Guid ticketId, Guid attachmentId, bool publicOnly, CancellationToken cancellationToken) =>
        (await VisibleAttachments(ticketId, publicOnly).FirstOrDefaultAsync(a => a.Id == attachmentId, cancellationToken))?.ToDomain();

    public async Task<IReadOnlyList<Attachment>> GetAttachmentsAsync(Guid ticketId, bool publicOnly, CancellationToken cancellationToken)
    {
        var records = await VisibleAttachments(ticketId, publicOnly)
            .OrderBy(a => a.CreatedAt).ThenBy(a => a.Id)
            .ToListAsync(cancellationToken);
        return [.. records.Select(a => a.ToDomain())];
    }

    public async Task<IReadOnlyList<Ticket>> ListSolvedBeforeAsync(DateTimeOffset solvedBefore, int limit, CancellationToken cancellationToken)
    {
        var records = await SolvedBeforeQuery(context.Set<TicketRecord>().Include(t => t.Tags), solvedBefore)
            .Take(Paging.NormalizeBatchSize(limit))
            .ToListAsync(cancellationToken);
        return [.. records.Select(t => t.ToDomain())];
    }

    public async Task<IReadOnlyList<Guid>> ListTicketIdsWithTagAsync(Guid tagId, CancellationToken cancellationToken) =>
        await context.Set<TicketTagRecord>().AsNoTracking()
            .Where(link => link.TagId == tagId)
            .Select(link => link.TicketId)
            .OrderBy(id => id)
            .ToListAsync(cancellationToken);

    public void AddAccessToken(TicketAccessToken token) => context.Set<TicketAccessTokenRecord>().Add(token.ToRecord());

    public void UpdateAccessToken(TicketAccessToken token) => token.CopyTo(context.FindLoaded<TicketAccessTokenRecord>(token.Id));

    public async Task<TicketAccessToken?> GetAccessTokenByHashAsync(string tokenHash, CancellationToken cancellationToken) =>
        (await context.Set<TicketAccessTokenRecord>().FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken))?.ToDomain();

    /// <summary>The queue view predicate. Internal so the query-plan tests can check the SQL EF emits for it.</summary>
    internal static IQueryable<TicketRecord> ViewQuery(IQueryable<TicketRecord> tickets, TicketQuery query) => query.View switch
    {
        TicketView.Unassigned => tickets.Where(t => !t.IsSpam && t.AssigneeId == null && ActiveStatuses.Contains(t.Status)),
        TicketView.Mine => query.AgentId is { } agentId
            ? tickets.Where(t => !t.IsSpam && t.AssigneeId == agentId && ActiveStatuses.Contains(t.Status))
            : tickets.Where(_ => false),
        TicketView.Open => tickets.Where(t => !t.IsSpam && OpenViewStatuses.Contains(t.Status)),
        TicketView.Pending => tickets.Where(t => !t.IsSpam && t.Status == TicketStatus.Pending),
        TicketView.All => tickets.Where(t => !t.IsSpam),
        TicketView.Spam => tickets.Where(t => t.IsSpam),
        _ => throw new ArgumentOutOfRangeException(nameof(query), query.View, "Unknown ticket view."),
    };

    /// <summary>The auto-close query, oldest solved first. Internal so the query-plan tests can check the SQL EF emits for it.</summary>
    internal static IQueryable<TicketRecord> SolvedBeforeQuery(IQueryable<TicketRecord> tickets, DateTimeOffset solvedBefore) =>
        tickets.Where(t => t.Status == TicketStatus.Solved && t.SolvedAt < solvedBefore).OrderBy(t => t.SolvedAt).ThenBy(t => t.Id);

    /// <summary>Attachments of the ticket; with <paramref name="publicOnly"/> only those whose parent message is customer-visible (D-024).</summary>
    private IQueryable<AttachmentRecord> VisibleAttachments(Guid ticketId, bool publicOnly)
    {
        var attachments = context.Set<AttachmentRecord>().AsNoTracking().Where(a => a.TicketId == ticketId);
        if (!publicOnly)
        {
            return attachments;
        }

        var messages = context.Set<MessageRecord>();
        return attachments.Where(a => messages.Any(m => m.Id == a.MessageId && m.Visibility == MessageVisibility.Public));
    }

    /// <summary>Stages the ticket's new messages (with attachments) and events so they are written with the ticket in one commit.</summary>
    private void StagePending(Ticket ticket)
    {
        foreach (var message in ticket.PendingMessages)
        {
            context.Set<MessageRecord>().Add(message.ToRecord());
            foreach (var attachment in message.NewAttachments)
            {
                context.Set<AttachmentRecord>().Add(attachment.ToRecord());
            }
        }

        foreach (var ticketEvent in ticket.PendingEvents)
        {
            context.Set<TicketEventRecord>().Add(ticketEvent.ToRecord());
        }

        ticket.AcceptChanges();
    }
}
