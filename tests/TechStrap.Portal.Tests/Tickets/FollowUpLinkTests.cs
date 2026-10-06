using TechStrap.Portal.Tickets;

namespace TechStrap.Portal.Tests.Tickets;

/// <summary>
/// Review Focus 1: the follow-up redirect never leaves the site. Only the last path segment of the API's link is used, and only if it is a valid token; the host, the scheme, a query and a fragment are ignored.
/// Anything that is not an absolute http(s) link with such a segment is no token, and the page then shows a generic confirmation instead of redirecting.
/// </summary>
public sealed class FollowUpLinkTests
{
    private const string New = "Zk9_-qqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqq";

    [Theory]
    [InlineData("https://help.example.com/t/" + New)]
    [InlineData("http://localhost:8082/t/" + New)]
    [InlineData("https://help.example.com/t/" + New + "/")]
    [InlineData("https://help.example.com/t/" + New + "?utm=1#top")]
    [InlineData("https://evil.example/anything/" + New)]
    [InlineData("HTTPS://HELP.EXAMPLE.COM/T/" + New)]
    [InlineData("https://help.example.com/base/path/t/" + New)]
    public void The_last_path_segment_is_the_token_whatever_the_host_or_the_query(string url)
    {
        FollowUpLink.TryGetToken(url, out var token).ShouldBeTrue(url);

        token.Value.ShouldBe(New);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/t/" + New)]
    [InlineData(New)]
    [InlineData("//evil.example/t/" + New)]
    [InlineData("javascript:alert(1)")]
    [InlineData("ftp://help.example.com/t/" + New)]
    [InlineData("data:text/plain,x")]
    [InlineData("https://help.example.com/t/short")]
    [InlineData("https://help.example.com/t/" + New + "x")]
    [InlineData("https://help.example.com/")]
    [InlineData("https://help.example.com")]
    [InlineData("https://help.example.com/t/" + New + "/attachments")]
    [InlineData("not a url")]
    public void Anything_else_is_no_token_and_gives_a_default_token(string? url)
    {
        FollowUpLink.TryGetToken(url, out var token).ShouldBeFalse(url);

        token.ShouldBe(default);
    }
}
