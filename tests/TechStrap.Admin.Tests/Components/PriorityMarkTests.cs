using Bunit;
using TechStrap.Admin.Components.Ui;

namespace TechStrap.Admin.Tests.Components;

public sealed class PriorityMarkTests : BunitContext
{
    [Theory]
    [InlineData(PriorityLevel.Urgent, "Urgent", "ts-priority--urgent")]
    [InlineData(PriorityLevel.High, "High", "ts-priority--high")]
    [InlineData(PriorityLevel.Normal, "Normal", "ts-priority--normal")]
    [InlineData(PriorityLevel.Low, "Low", "ts-priority--low")]
    public void Priority_is_a_marker_class_plus_a_word(PriorityLevel level, string word, string cssClass)
    {
        var cut = Render<PriorityMark>(p => p.Add(m => m.Level, level));

        var mark = cut.Find("span.ts-priority");
        mark.TextContent.ShouldBe(word);
        mark.ClassList.ShouldContain(cssClass);
    }
}
