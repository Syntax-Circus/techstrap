using Bunit;
using TechStrap.Admin.Components.Ui;

namespace TechStrap.Admin.Tests.Components;

public sealed class StatusStampTests : BunitContext
{
    public static TheoryData<StampStatus, StampVariant, string> Labels() => new()
    {
        { StampStatus.New, StampVariant.Queue, "New" },
        { StampStatus.Open, StampVariant.Queue, "Open" },
        { StampStatus.Pending, StampVariant.Queue, "Pending" },
        { StampStatus.Solved, StampVariant.Queue, "Solved" },
        { StampStatus.Closed, StampVariant.Queue, "Closed" },
        { StampStatus.Spam, StampVariant.Queue, "Spam?" },
        { StampStatus.New, StampVariant.Ticket, "New" },
        { StampStatus.Open, StampVariant.Ticket, "Open" },
        { StampStatus.Pending, StampVariant.Ticket, "Pending" },
        { StampStatus.Solved, StampVariant.Ticket, "Solved" },
        { StampStatus.Closed, StampVariant.Ticket, "Closed" },
        { StampStatus.Spam, StampVariant.Ticket, "Spam" },
    };

    [Theory]
    [MemberData(nameof(Labels))]
    public void Status_is_a_word_plus_a_shape_class_never_colour_alone(StampStatus status, StampVariant variant, string label)
    {
        var cut = Render<StatusStamp>(p => p.Add(s => s.Status, status).Add(s => s.Variant, variant));

        var stamp = cut.Find("span.ts-stamp");
        stamp.TextContent.ShouldBe(label);
        stamp.ClassList.ShouldContain($"ts-stamp--{status.ToString().ToLowerInvariant()}");
        stamp.ClassList.ShouldContain($"ts-stamp--{variant.ToString().ToLowerInvariant()}");
    }

    [Fact]
    public void The_queue_variant_is_the_default()
    {
        var cut = Render<StatusStamp>(p => p.Add(s => s.Status, StampStatus.Open));

        cut.Find("span.ts-stamp").ClassList.ShouldContain("ts-stamp--queue");
    }

    [Fact]
    public void Stamp_down_animation_applies_to_the_ticket_variant_only()
    {
        var ticket = Render<StatusStamp>(p => p.Add(s => s.Status, StampStatus.Solved).Add(s => s.Variant, StampVariant.Ticket).Add(s => s.Animate, true));
        var queue = Render<StatusStamp>(p => p.Add(s => s.Status, StampStatus.Solved).Add(s => s.Variant, StampVariant.Queue).Add(s => s.Animate, true));
        var still = Render<StatusStamp>(p => p.Add(s => s.Status, StampStatus.Solved).Add(s => s.Variant, StampVariant.Ticket));

        ticket.Find("span.ts-stamp").ClassList.ShouldContain("ts-stamp--pop");
        queue.Find("span.ts-stamp").ClassList.ShouldNotContain("ts-stamp--pop");
        still.Find("span.ts-stamp").ClassList.ShouldNotContain("ts-stamp--pop");
    }
}
