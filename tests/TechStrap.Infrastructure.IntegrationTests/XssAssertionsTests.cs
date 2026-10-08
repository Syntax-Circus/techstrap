using TechStrap.Tests.Shared;

namespace TechStrap.Infrastructure.IntegrationTests;

/// <summary>P12-T05: the shared active-content detector flags what it must and passes encoded text, so a corpus run means something.</summary>
public sealed class XssAssertionsTests
{
    [Theory]
    [InlineData("<script>x</script>")]
    [InlineData("<img src=x onerror=alert(1)>")]
    [InlineData("<a href=\"javascript:x\">")]
    [InlineData("<iframe srcdoc=\"x\">")]
    [InlineData("<div style=\"width:expression(x)\">")]
    [InlineData("<form action=y>")]
    [InlineData("<math>")]
    [InlineData("<template>")]
    public void Active_content_is_flagged(string html)
    {
        XssAssertions.ContainsNoActiveContent(html).ShouldBeFalse();
        Should.Throw<ShouldAssertException>(() => XssAssertions.ShouldHaveNoActiveContent(html, "test"));
    }

    [Theory]
    [InlineData("&lt;script&gt;alert(1)&lt;/script&gt;")]
    [InlineData("<p>onerror=alert(1) is text</p>")]
    [InlineData("<a href=\"https://x.example\" rel=\"noopener noreferrer nofollow\">x</a>")]
    [InlineData("<p>&lt;a href=\"javascript:x\"&gt; and url=javascript: are text</p>")]
    [InlineData("")]
    public void Inert_content_passes(string html)
    {
        XssAssertions.ContainsNoActiveContent(html).ShouldBeTrue();
        XssAssertions.ShouldHaveNoActiveContent(html, "test");
    }

    [Fact]
    public void The_corpus_has_at_least_fifty_vectors_and_no_non_ascii()
    {
        XssCorpus.Vectors.Count.ShouldBeGreaterThanOrEqualTo(50);
        XssCorpus.Vectors.ShouldAllBe(vector => vector.All(character => character < 128));
    }
}
