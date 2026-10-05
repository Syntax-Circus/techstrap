using System.Text.RegularExpressions;
using Bunit;
using TechStrap.Admin.Components.Ui;
using TechStrap.Tests.Shared;

namespace TechStrap.Admin.Tests;

/// <summary>
/// A wide table scrolls inside its own region, and the region is a named, keyboard-reachable box (WCAG 2.1.1; axe: scrollable-region-focusable). The component is the one place that
/// says so, so the site test makes sure no page puts a ledger table anywhere else.
/// </summary>
public sealed partial class ScrollRegionSiteTests : BunitContext
{
    [GeneratedRegex(@"<table[^>]*\bts-ledger\b")]
    private static partial Regex LedgerTable();

    [Fact]
    public void The_region_is_a_named_focusable_box_that_holds_what_it_is_given()
    {
        var cut = Render<ScrollRegion>(p => p.Add(r => r.Label, "Tickets").AddChildContent("<table class=\"table\"></table>"));

        var region = cut.Find("div.ts-scroll");
        region.GetAttribute("role").ShouldBe("region");
        region.GetAttribute("aria-label").ShouldBe("Tickets");
        region.GetAttribute("tabindex").ShouldBe("0");
        region.ClassList.ShouldContain("table-responsive");
        region.QuerySelector("table").ShouldNotBeNull();
    }

    [Fact]
    public void Every_ledger_table_in_the_admin_sits_directly_inside_a_scroll_region_and_names_its_role()
    {
        var admin = RepositoryRoot.Combine("src", "TechStrap.Admin");
        var separator = Path.DirectorySeparatorChar;
        var files = Directory.EnumerateFiles(admin, "*.razor", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{separator}obj{separator}") && !file.Contains($"{separator}bin{separator}"))
            .ToList();

        var tables = 0;
        var unwrapped = new List<string>();
        var withoutRole = new List<string>();
        foreach (var file in files)
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (!LedgerTable().IsMatch(lines[i]))
                {
                    continue;
                }

                tables++;
                if (!lines[i].Contains("role=\"table\"", StringComparison.Ordinal))
                {
                    withoutRole.Add($"{Path.GetRelativePath(admin, file).Replace('\\', '/')}:{i + 1}");
                }

                if (i == 0 || !lines[i - 1].Contains("<ScrollRegion ", StringComparison.Ordinal))
                {
                    unwrapped.Add($"{Path.GetRelativePath(admin, file).Replace('\\', '/')}:{i + 1}");
                }
            }
        }

        tables.ShouldBe(8);
        unwrapped.ShouldBeEmpty();
        withoutRole.ShouldBeEmpty();
    }
}
