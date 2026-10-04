using TechStrap.Contracts.Tickets;
using TechStrap.Domain.Tickets;

namespace TechStrap.Application.Tickets;

internal static class TicketDtoMapper
{
    public static TicketStateDto ToDto(this TicketState state) =>
        new(
            state.Id, state.Number, state.Status.ToWire(), state.Priority.ToWire(), state.ProductId, state.AssigneeId,
            state.IsSpam, state.TagIds, state.LastActivityAt, state.Version);

    /// <summary>The enum name, which is the wire name for status, priority, event type, author type, visibility and channel.</summary>
    public static string ToWire(this TicketStatus status) => status.ToString();

    public static string ToWire<TEnum>(this TEnum value)
        where TEnum : struct, Enum => value.ToString();

    public static AttachmentDto ToDto(this Attachment attachment) =>
        new(attachment.Id, attachment.FileName, attachment.ContentType, attachment.Size);
}
