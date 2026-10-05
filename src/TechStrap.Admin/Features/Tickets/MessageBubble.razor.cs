using Microsoft.AspNetCore.Components;

namespace TechStrap.Admin.Features.Tickets;

/// <summary>One message in the timeline, in the carbon tint code: customer, public reply or internal note. Its body is the only HTML the Admin renders from the API.</summary>
public sealed partial class MessageBubble
{
    [Parameter, EditorRequired]
    public MessageViewModel Message { get; set; } = default!;
}
