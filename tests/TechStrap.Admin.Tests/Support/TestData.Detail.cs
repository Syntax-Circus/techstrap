using TechStrap.Contracts.Agents;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Admin.Tests.Support;

internal static partial class TestData
{
    public static readonly Guid SamAgentId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid AdaAgentId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid RequesterId = Guid.Parse("cccccccc-0000-0000-0000-000000000001");
    public static readonly Guid TicketId = Guid.Parse("dddddddd-0000-0000-0000-000000000042");

    public static AgentListItemDto Agent(string name, Guid id) => new(id, name, name, null, null, null, null);

    public static MessageDto Message(
        string authorType = MessageAuthorTypes.Requester,
        string visibility = MessageVisibilities.Public,
        string bodyHtml = "<p>Hello</p>",
        DateTimeOffset? at = null,
        string? authorName = "Ada Lovelace",
        Guid? id = null,
        IReadOnlyList<AttachmentDto>? attachments = null,
        IReadOnlyList<LinkedArticleDto>? articles = null) => new(
            id ?? Guid.NewGuid(), authorType, null, authorName, visibility, bodyHtml, at ?? Now.AddHours(-3), attachments ?? [], articles ?? []);

    public static TicketEventDto Event(string type, string payload = "{}", DateTimeOffset? at = null, string actorType = "Agent", string? actorName = "Sam Ortiz") =>
        new(Guid.NewGuid(), type, actorType, null, actorName, payload, at ?? Now.AddHours(-2));

    public static TicketDetailDto Detail(
        string number = "ORB-42",
        string status = TicketStatuses.Open,
        string priority = TicketPriorities.Normal,
        bool isSpam = false,
        Guid? assigneeId = null,
        string? assigneeName = null,
        IReadOnlyList<TicketTagDto>? tags = null,
        IReadOnlyList<MessageDto>? messages = null,
        IReadOnlyList<TicketEventDto>? events = null,
        Guid? parentId = null,
        string? metadataJson = null,
        bool metadataTrusted = false,
        uint rowVersion = 7,
        string subject = "Cannot log in") => new(
            TicketId, number, subject, status, priority, OrbitlyId, "Orbitly",
            new TicketRequesterDto(RequesterId, "ada@example.com", "Ada Lovelace", null),
            assigneeId, assigneeName, isSpam, tags ?? [], "Email", parentId, metadataJson, metadataTrusted,
            Now.AddDays(-1), null, null, null, Now.AddMinutes(-5), rowVersion,
            messages ?? [Message()], events ?? []);

    public static TicketStateDto State(
        string status = TicketStatuses.Open,
        string priority = TicketPriorities.Normal,
        Guid? assigneeId = null,
        Guid? productId = null,
        bool isSpam = false,
        IReadOnlyList<Guid>? tagIds = null,
        uint rowVersion = 8) => new(TicketId, "ORB-42", status, priority, productId ?? OrbitlyId, assigneeId, isSpam, tagIds ?? [], Now, rowVersion);
}
