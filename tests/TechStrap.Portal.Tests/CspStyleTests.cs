using System.Text.RegularExpressions;
using TechStrap.Portal.Tests.Components;
using TechStrap.Portal.Tests.Forms;
using TechStrap.Tests.Shared;

namespace TechStrap.Portal.Tests;

/// <summary>
/// The Portal's side of Review Focus 3 (the Admin's <c>CspStyleTests</c> already runs its compiled-CSS checks over the Portal too): the policy is unchanged (<c>script-src 'self'</c>, <c>style-src 'self'</c>), so the new
/// styles add no <c>url()</c> the policy does not allow, no <c>@import</c>, and no markup the policy would block: no <c>style</c> element, no <c>style</c> attribute but the product accent's own, no inline script and no
/// event-handler attribute, on the pages that got new markup in 09d.
/// </summary>
public sealed partial class CspStyleTests
{
    private static readonly CompiledCss Css = CompiledCss.Load("TechStrap.Portal");

    [GeneratedRegex(@"url\(\s*(?<q>[""']?)(?<target>.*?)\k<q>\s*\)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex UrlReference();

    [Fact]
    public void The_compiled_portal_css_references_only_same_origin_fonts_and_data_images_and_imports_nothing()
    {
        var targets = UrlReference().Matches(Css.Text).Select(m => m.Groups["target"].Value).ToList();

        targets.ShouldNotBeEmpty("the fonts and Bootstrap's icons are url() references");
        targets.ShouldAllBe(t => t.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase) || t.EndsWith(".woff2", StringComparison.OrdinalIgnoreCase));
        Css.Text.ShouldNotContain("@import");
        Css.Text.ShouldNotContain("javascript:");
        Css.Text.ShouldNotContain("expression(");
        Css.Text.ShouldNotContain("-moz-binding");
    }

    [Fact]
    public void The_portals_own_stylesheets_add_no_url_at_all_except_the_font_faces()
    {
        foreach (var file in Directory.EnumerateFiles(RepositoryRoot.Combine("src", "TechStrap.Portal", "Styles"), "*.scss", SearchOption.TopDirectoryOnly).Where(f => !f.EndsWith("_fonts.scss", StringComparison.Ordinal)))
        {
            File.ReadAllText(file).ShouldNotContain("url(", customMessage: file);
        }
    }

    public static TheoryData<string> Pages => ["/", "/p/paperplane", "/p/paperplane/contact", "/p/paperplane/contact/received", "/p/paperplane/lost-link", "/t/AbC-_0123456789AbC-_0123456789AbC-_01234567"];

    [Theory]
    [MemberData(nameof(Pages))]
    public async Task A_page_has_no_style_element_no_inline_script_no_event_handler_and_one_style_attribute_the_accent_scope(string path)
    {
        await using var factory = Tickets.TicketTestKit.Factory();
        using var client = FormTestKit.Client(factory);

        var html = await client.GetStringAsync(path, TestContext.Current.CancellationToken);

        var dom = PageKit.Parse(html);
        dom.QuerySelectorAll("style").ShouldBeEmpty();
        dom.QuerySelectorAll("script:not([src])").ShouldBeEmpty();
        dom.QuerySelectorAll("[style]").ShouldAllBe(e => e.ClassList.Contains("ts-accent-scope"));
        dom.All.SelectMany(e => e.Attributes).Where(a => a.Name.StartsWith("on", StringComparison.OrdinalIgnoreCase)).ShouldBeEmpty();
    }
}
