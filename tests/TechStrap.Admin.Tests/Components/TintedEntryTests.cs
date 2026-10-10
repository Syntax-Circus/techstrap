using Bunit;
using TechStrap.Admin.Components.Ui;

namespace TechStrap.Admin.Tests.Components;

/// <summary>The carbon tint code (BRAND.md section 12): white customer, canary public reply, pink dashed notched internal note. Color is never the only cue.</summary>
public sealed class TintedEntryTests : BunitContext
{
    private IRenderedComponent<TintedEntry> Render(EntryKind kind) =>
        Render<TintedEntry>(p => p
            .Add(e => e.Kind, kind)
            .Add(e => e.Author, "Dana Whitfield")
            .Add(e => e.Time, "08:41")
            .AddChildContent("<p>The backup exits with code 17.</p>"));

    [Fact]
    public void A_customer_message_is_white_and_labelled_customer()
    {
        var cut = Render(EntryKind.Customer);

        var entry = cut.Find("article.ts-entry");
        entry.ClassList.ShouldContain("ts-entry--customer");
        cut.Find(".ts-entry-head").TextContent.ShouldContain("customer");
        cut.Find(".ts-entry-head strong").TextContent.ShouldBe("Dana Whitfield");
        cut.Find(".ts-entry-time").TextContent.ShouldBe("08:41");
        cut.Find(".ts-entry-body p").TextContent.ShouldBe("The backup exits with code 17.");
        cut.FindAll(".ts-entry-label").ShouldBeEmpty();
    }

    [Fact]
    public void A_public_reply_is_canary_and_labelled_agent_reply()
    {
        var cut = Render(EntryKind.PublicReply);

        cut.Find("article.ts-entry").ClassList.ShouldContain("ts-entry--public");
        cut.Find(".ts-entry-head").TextContent.ShouldContain("agent reply");
        cut.FindAll(".ts-entry-label").ShouldBeEmpty();
    }

    [Fact]
    public void An_internal_note_carries_the_INTERNAL_NOTE_label_so_pink_is_never_the_only_cue()
    {
        var cut = Render(EntryKind.InternalNote);

        cut.Find("article.ts-entry").ClassList.ShouldContain("ts-entry--note");
        cut.Find(".ts-entry-label").TextContent.ShouldBe("INTERNAL NOTE");
        cut.Find(".ts-entry-head").TextContent.ShouldNotContain("customer");
    }

    [Fact]
    public void Each_kind_gets_exactly_one_tint_class()
    {
        foreach (var kind in Enum.GetValues<EntryKind>())
        {
            var classes = Render(kind).Find("article.ts-entry").ClassList.Where(c => c.StartsWith("ts-entry--", StringComparison.Ordinal)).ToList();

            classes.Count.ShouldBe(1, kind.ToString());
        }
    }
}
