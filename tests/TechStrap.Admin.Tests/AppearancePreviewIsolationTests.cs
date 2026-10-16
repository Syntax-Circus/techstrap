using System.Text.RegularExpressions;
using TechStrap.Tests.Shared;

namespace TechStrap.Admin.Tests;

public sealed partial class AppearancePreviewIsolationTests
{
    [GeneratedRegex(@"(?<selector>[^{}]+)\{(?<body>[^{}]*)\}")]
    private static partial Regex Rules();

    [GeneratedRegex(@",(?![^()]*\))")]
    private static partial Regex SelectorSeparator();

    [Fact]
    public void Every_compiled_preview_selector_is_inside_a_named_preview_scope()
    {
        var css = CompiledCss.Load("TechStrap.Admin");
        var previewRules = Rules().Matches(css.Text).Where(m => m.Groups["selector"].Value.Contains(".ts-appearance-", StringComparison.Ordinal)).ToArray();
        previewRules.Length.ShouldBeGreaterThan(20);
        foreach (var rule in previewRules)
        {
            foreach (var selector in SelectorSeparator().Split(rule.Groups["selector"].Value))
            {
                (selector.StartsWith(".ts-appearance-preview", StringComparison.Ordinal) || selector.StartsWith(".ts-appearance-example", StringComparison.Ordinal))
                    .ShouldBeTrue($"Preview CSS must not style the Admin shell: {selector}");
            }
        }
        // In both Admin themes, the shell still keeps its own typography and button geometry.
        css.Declarations(".btn")["text-transform"].ShouldBe("uppercase");
        css.Declarations(".btn")["min-height"].ShouldBe("34px");
        css.Declarations(":root,[data-bs-theme=light]")["--bs-border-radius"].ShouldBe("0");
        css.Declarations("[data-bs-theme=dark]")["--bs-body-bg"].ShouldBe("#0B1E40");
    }
}
