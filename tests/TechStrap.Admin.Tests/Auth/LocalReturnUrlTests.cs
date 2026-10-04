using TechStrap.Admin.Auth;

namespace TechStrap.Admin.Tests.Auth;

public sealed class LocalReturnUrlTests
{
    [Theory]
    [InlineData("/", "/")]
    [InlineData("/queue/mine?page=2", "/queue/mine?page=2")]
    [InlineData("/tickets/ORB-42", "/tickets/ORB-42")]
    public void A_local_path_is_kept(string input, string expected) => LocalReturnUrl.Sanitize(input).ShouldBe(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("https://evil.example/")]
    [InlineData("//evil.example/")]
    [InlineData("/\\evil.example")]
    [InlineData("\\evil.example")]
    [InlineData("tickets/ORB-42")]
    [InlineData("/ok\r\nSet-Cookie: x=1")]
    [InlineData("/signin?returnUrl=/")]
    [InlineData("/SignOut")]
    public void Anything_else_becomes_the_home_page(string? input) => LocalReturnUrl.Sanitize(input).ShouldBe("/");
}
