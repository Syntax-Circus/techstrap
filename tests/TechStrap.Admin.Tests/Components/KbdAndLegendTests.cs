using Bunit;
using TechStrap.Admin.Components.Ui;

namespace TechStrap.Admin.Tests.Components;

public sealed class KbdAndLegendTests : BunitContext
{
    [Fact]
    public void Kbd_renders_a_keycap()
    {
        var cut = Render<Kbd>(p => p.AddChildContent("j"));

        cut.Find("kbd.ts-kbd").TextContent.ShouldBe("j");
    }

    [Fact]
    public void The_legend_names_all_three_tints_with_their_meaning()
    {
        var cut = Render<TintLegend>();

        var items = cut.FindAll(".ts-legend span").Select(s => s.TextContent.Trim()).ToList();
        items.ShouldBe(["Customer (white)", "Public reply (canary)", "Internal note (pink)"]);
        cut.Find(".ts-legend-note i").ClassList.ShouldContain("ts-legend-swatch--note");
    }
}
