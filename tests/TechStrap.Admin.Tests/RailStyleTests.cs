using TechStrap.Tests.Shared;

namespace TechStrap.Admin.Tests;

/// <summary>The rail's admin group and the failed-email badge use brand tokens only, so both themes get a legible badge without a new colour.</summary>
public sealed class RailStyleTests
{
    private static readonly CompiledCss Css = CompiledCss.Load("TechStrap.Admin");

    [Fact]
    public void The_failed_email_badge_uses_the_selection_and_ink_tokens()
    {
        var badge = Css.Declarations(".ts-rail-badge");

        badge["color"].ShouldBe("var(--ink)");
        badge["background"].ShouldBe("var(--sel)");
        badge["border"].ShouldBe("1px solid var(--rule-strong)");
    }

    [Fact]
    public void The_admin_group_is_set_apart_by_a_dashed_rule()
    {
        Css.Declarations(".ts-rail-group")["border-top"].ShouldBe("1px dashed var(--rule-strong)");
    }
}
