using TechStrap.Portal.Seo;
using TechStrap.Portal.Tests.Forms;
using TechStrap.Portal.Tests.Kb;

namespace TechStrap.Portal.Tests.Seo;

/// <summary>
/// The contact and lost-link pages are not indexed, but a site audit still flags a page with no meta description. They get one and nothing else: the <c>noindex</c> stays a header, and no robots tag or canonical
/// is added beside it.
/// </summary>
public sealed class FormPageDescriptionHostTests
{
    [Theory(Timeout = 30_000)]
    [InlineData("/p/paperplane/contact", "Contact Paperplane support")]
    [InlineData("/p/paperplane/contact?type=FeatureRequest", "Contact Paperplane support")]
    [InlineData("/p/paperplane/lost-link", "New ticket link: Paperplane")]
    [InlineData("/p/paperplane/lost-link?sent=1", "New ticket link: Paperplane")]
    public async Task A_form_page_has_one_description_in_range_and_stays_unindexed(string path, string title)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = FormTestKit.Factory();
        using var client = FormTestKit.Client(factory);

        using var response = await client.GetAsync(path, ct);
        var dom = KbTestKit.Parse(await response.Content.ReadAsStringAsync(ct));

        var description = dom.QuerySelectorAll("meta[name=description]").ShouldHaveSingleItem().GetAttribute("content");
        description.ShouldNotBeNull().Length.ShouldBeInRange(SeoText.DescriptionMinLength, SeoText.DescriptionMaxLength);
        description.ShouldContain("Paperplane");
        dom.Title.ShouldBe(title);
        response.Headers.GetValues("X-Robots-Tag").ShouldBe(["noindex"]);
        dom.QuerySelector("meta[name=robots]").ShouldBeNull();
        dom.QuerySelector("link[rel=canonical]").ShouldBeNull();
    }
}
