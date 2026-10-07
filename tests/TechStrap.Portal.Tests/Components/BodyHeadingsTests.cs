using TechStrap.Portal.Components;

namespace TechStrap.Portal.Tests.Components;

/// <summary>A body an author wrote may carry an <c>h1</c> (a Markdown line that starts with <c>#</c>); the page's own title is its one <c>h1</c>, so the body's is shown as an <c>h2</c> and nothing else changes.</summary>
public sealed class BodyHeadingsTests
{
    [Fact]
    public void An_h1_becomes_an_h2_and_nothing_else_changes()
    {
        BodyHeadings.DemoteTitle("<h1>Title</h1><h2>Sub</h2><p>Text with h1 in it and &lt;h1&gt;.</p>").ShouldBe("<h2>Title</h2><h2>Sub</h2><p>Text with h1 in it and &lt;h1&gt;.</p>");
    }

    [Fact]
    public void Every_h1_is_demoted_and_the_other_levels_are_left_alone()
    {
        BodyHeadings.DemoteTitle("<h1>A</h1><h1>B</h1><h3>C</h3><h6>D</h6>").ShouldBe("<h2>A</h2><h2>B</h2><h3>C</h3><h6>D</h6>");
    }

    [Theory]
    [InlineData("")]
    [InlineData("<p>No headings.</p>")]
    [InlineData("<h2>Already h2</h2>")]
    [InlineData("<h10>Not a heading</h10>")]
    public void A_body_without_an_h1_is_returned_byte_for_byte(string html)
    {
        BodyHeadings.DemoteTitle(html).ShouldBeSameAs(html);
    }
}
