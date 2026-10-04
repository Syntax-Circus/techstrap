using TechStrap.Tests.Shared;

namespace TechStrap.Admin.Tests;

/// <summary>Caller-controlled text (metadata values, file names, message bodies) must wrap inside the ticket layout instead of breaking the 280px side column.</summary>
public sealed class TicketStyleTests
{
    private static readonly CompiledCss Css = CompiledCss.Load("TechStrap.Admin");

    [Theory]
    [InlineData(".ts-side dd")]
    [InlineData(".ts-metadata dd")]
    [InlineData(".ts-attachments a")]
    [InlineData(".ts-message-body")]
    public void Long_unbroken_text_wraps_anywhere(string selector)
    {
        Css.Declarations(selector)["overflow-wrap"].ShouldBe("anywhere");
    }

    [Theory]
    [InlineData(".ts-side dd")]
    [InlineData(".ts-metadata dd")]
    [InlineData(".ts-attachments a")]
    [InlineData(".ts-conversation")]
    public void Grid_and_flex_children_that_hold_the_text_can_shrink(string selector)
    {
        Css.Declarations(selector)["min-width"].ShouldBe("0");
    }

    [Fact]
    public void A_wide_message_body_scrolls_inside_itself()
    {
        Css.Declarations(".ts-message-body")["overflow-x"].ShouldBe("auto");
    }
}
