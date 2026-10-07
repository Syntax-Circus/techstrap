using TechStrap.Portal.Routing;

namespace TechStrap.Portal.Tests.Routing;

/// <summary>
/// A link to a place on the current page (the error summary, the skip link, "jump to your reply") is a root-relative address plus a fragment, never a bare <c>#fragment</c>: under the document's <c>base href="/"</c>
/// a bare fragment is the home page (D-045 09d addendum). The query string is kept only for the parameters the page itself reads.
/// </summary>
public sealed class PageLinksTests
{
    private const string Host = "http://localhost";

    [Theory]
    [InlineData("/p/paperplane/contact", "email", "/p/paperplane/contact#email")]
    [InlineData("/p/paperplane/lost-link", "email", "/p/paperplane/lost-link#email")]
    [InlineData("/t/AbC-_0123456789AbC-_0123456789AbC-_01234567", "body", "/t/AbC-_0123456789AbC-_0123456789AbC-_01234567#body")]
    [InlineData("/", "main", "/#main")]
    public void The_link_is_the_current_path_plus_the_fragment(string path, string fragment, string expected)
    {
        PageLinks.ToFragment(Host + path, fragment, keepQuery: false).ShouldBe(expected);
        PageLinks.ToFragment(Host + path, fragment, keepQuery: true).ShouldBe(expected);
    }

    [Fact]
    public void The_host_the_port_a_fragment_already_in_the_address_and_the_query_never_leak_into_it_without_keepQuery()
    {
        PageLinks.ToFragment("https://support.example.com:8443/p/paperplane/contact?subject=Jam&token=x#old", "main", keepQuery: false).ShouldBe("/p/paperplane/contact#main");
    }

    [Theory]
    [InlineData("/p/paperplane/contact?subject=Printer%20jam&name=Ada&email=ada%40example.com", "/p/paperplane/contact?subject=Printer%20jam&name=Ada&email=ada%40example.com#main")]
    [InlineData("/p/paperplane/contact?subject=Jam&utm_source=mail&Website=spam&ref=1", "/p/paperplane/contact?subject=Jam#main")]
    [InlineData("/p/paperplane/contact?utm=1", "/p/paperplane/contact#main")]
    [InlineData("/p/paperplane/contact/received?ref=CfDJ8abc", "/p/paperplane/contact/received?ref=CfDJ8abc#main")]
    [InlineData("/p/paperplane/contact/received?ref=CfDJ8abc&subject=x", "/p/paperplane/contact/received?ref=CfDJ8abc#main")]
    [InlineData("/p/paperplane/lost-link?sent=1&x=2", "/p/paperplane/lost-link?sent=1#main")]
    [InlineData("/p/paperplane/kb/search?q=reset%20password&page=2&utm=3", "/p/paperplane/kb/search?q=reset%20password&page=2#main")]
    [InlineData("/p/paperplane/kb/accounts?page=2&utm=3", "/p/paperplane/kb/accounts?page=2#main")]
    [InlineData("/p/paperplane/kb/accounts/reset-password?page=2&utm=3", "/p/paperplane/kb/accounts/reset-password#main")]
    [InlineData("/p/paperplane/kb?page=2&utm=3", "/p/paperplane/kb#main")]
    [InlineData("/p/paperplane?utm=3", "/p/paperplane#main")]
    [InlineData("/t/AbC-_0123456789AbC-_0123456789AbC-_01234567?x=1", "/t/AbC-_0123456789AbC-_0123456789AbC-_01234567#main")]
    public void With_keepQuery_only_the_parameters_the_page_reads_stay_in_the_order_they_came(string current, string expected)
    {
        PageLinks.ToFragment(Host + current, "main", keepQuery: true).ShouldBe(expected);
    }

    [Theory]
    [InlineData("/p/paperplane/kb/search?q=paper+jam", "/p/paperplane/kb/search?q=paper+jam#main")]
    [InlineData("/p/paperplane/contact?subject=Printer+jam&utm_source=x", "/p/paperplane/contact?subject=Printer+jam#main")]
    [InlineData("/p/paperplane/kb/search?PAGE=2&q=a%20b&x=1", "/p/paperplane/kb/search?PAGE=2&q=a%20b#main")]
    public void The_kept_query_is_the_address_bars_own_spelling_byte_for_byte(string current, string expected)
    {
        PageLinks.ToFragment(Host + current, "main", keepQuery: true).ShouldBe(expected);
    }

    [Fact]
    public void A_kept_value_is_escaped_so_it_cannot_add_a_parameter_a_fragment_or_a_tag()
    {
        var link = PageLinks.ToFragment(Host + "/p/paperplane/kb/search?q=a%26b%3Dc%23d%22%3E%3Cscript%3E", "main", keepQuery: true);

        link.ShouldBe("/p/paperplane/kb/search?q=a%26b%3Dc%23d%22%3E%3Cscript%3E#main");
        link.Count(c => c == '#').ShouldBe(1);
        link.ShouldNotContain("<");
        link.ShouldNotContain("\"");
    }

    [Fact]
    public void A_path_with_a_capital_or_an_escape_is_still_the_pages_own_address()
    {
        PageLinks.ToFragment(Host + "/p/PaperPlane/Contact?subject=x", "main", keepQuery: true).ShouldBe("/p/PaperPlane/Contact?subject=x#main");
        PageLinks.ToFragment(Host + "/p/paperplane/kb/a%20b", "main", keepQuery: true).ShouldBe("/p/paperplane/kb/a%20b#main");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void A_blank_fragment_is_refused(string fragment)
    {
        Should.Throw<ArgumentException>(() => PageLinks.ToFragment(Host + "/", fragment, keepQuery: false));
    }
}
