using TechStrap.Hosting.Security;

namespace TechStrap.Api.Tests.Hosting;

public sealed class CspBuilderTests
{
    [Fact]
    public void Directives_are_written_in_the_order_they_were_added_separated_by_semicolons()
    {
        var policy = new CspBuilder()
            .Directive("default-src", "'self'")
            .Directive("img-src", "'self'", "https:", "data:")
            .Directive("upgrade-insecure-requests")
            .Build();

        policy.ShouldBe("default-src 'self'; img-src 'self' https: data:; upgrade-insecure-requests");
    }

    [Fact]
    public void Directive_replaces_and_Allow_adds_without_repeating_a_source()
    {
        var policy = new CspBuilder()
            .Directive("form-action", "'self'")
            .Allow("form-action", "https://idp.test", "'self'")
            .Allow("form-action", "https://idp.test")
            .Allow("img-src", "https:")
            .Directive("img-src", "data:")
            .Build();

        policy.ShouldBe("form-action 'self' https://idp.test; img-src data:");
    }

    [Theory]
    [InlineData("https://idp.test; script-src *")]
    [InlineData("https://idp.test;script-src")]
    [InlineData(";")]
    [InlineData("https://idp.test, https://evil.test")]
    [InlineData("https://idp.test script-src")]
    [InlineData("https://idp.test\nscript-src *")]
    [InlineData("https://idp.test\t")]
    [InlineData("")]
    [InlineData("https://b\u00FCcher.example")]
    [InlineData("https://idp.test\u00A0")]
    [InlineData("https://idp.test\u200B")]
    [InlineData("https://idp.test\u007F")]
    public void A_source_that_could_end_the_directive_or_start_another_is_refused(string source)
    {
        Should.Throw<ArgumentException>(() => new CspBuilder().Directive("form-action", source));
        Should.Throw<ArgumentException>(() => new CspBuilder().Allow("form-action", source));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Script-Src")]
    [InlineData("script src")]
    [InlineData("script-src;")]
    [InlineData("1script")]
    [InlineData("script-src\n")]
    [InlineData("script-src\r\n")]
    [InlineData("scr\u00EFpt-src")]
    public void A_directive_name_that_is_not_lower_case_words_is_refused(string name)
    {
        Should.Throw<ArgumentException>(() => new CspBuilder().Directive(name, "'self'"));
    }

    [Theory]
    [InlineData("https://idp.test/application/o/techstrap-admin/", "https://idp.test")]
    [InlineData("https://idp.test:8443/realms/x", "https://idp.test:8443")]
    [InlineData("HTTPS://IDP.TEST:443/", "https://idp.test")]
    [InlineData("http://localhost:9000/application/o/x/", "http://localhost:9000")]
    [InlineData("  https://idp.test/a  ", "https://idp.test")]
    public void OriginOf_keeps_the_scheme_host_and_a_non_default_port_and_drops_the_path(string url, string expected)
    {
        TechStrapCsp.OriginOf(url).ShouldBe(expected);
    }

    [Fact]
    public void OriginOf_writes_a_non_ASCII_host_as_punycode()
    {
        TechStrapCsp.OriginOf("https://b\u00FCcher.example:8443/realms/x").ShouldBe("https://xn--bcher-kva.example:8443");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/application/o/x/")]
    [InlineData("idp.test")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,x")]
    [InlineData("ftp://idp.test/")]
    [InlineData("https://user:pass@idp.test/")]
    [InlineData("https://@idp.test/")]
    [InlineData("https://:@idp.test/")]
    [InlineData("https://idp.test@evil.test/")]
    public void OriginOf_refuses_anything_that_is_not_an_absolute_http_or_https_URL_without_user_info(string? url)
    {
        TechStrapCsp.OriginOf(url).ShouldBeNull();
    }

    [Fact]
    public void The_blazor_policy_is_exactly_the_decided_one()
    {
        TechStrapCsp.ForBlazorApp().ShouldBe(
            "default-src 'self'; script-src 'self'; style-src 'self'; style-src-attr 'unsafe-inline'; img-src 'self' https: data:; connect-src 'self'; "
            + "font-src 'self'; object-src 'none'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'");
    }

    [Fact]
    public void The_blazor_policy_adds_the_identity_provider_origin_to_form_action_and_ignores_unusable_entries()
    {
        var policy = TechStrapCsp.ForBlazorApp(["https://idp.test/application/o/techstrap-admin/", null, "", "javascript:alert(1)", "https://idp.test/other"]);

        policy.ShouldEndWith("form-action 'self' https://idp.test");
    }

    [Fact]
    public void Loopback_logo_images_are_allowed_only_when_asked_for()
    {
        TechStrapCsp.ForBlazorApp().ShouldNotContain("localhost");
        TechStrapCsp.ForBlazorApp(allowLoopbackImages: true)
            .ShouldContain("img-src 'self' https: data: http://localhost:* http://127.0.0.1:*;");
    }

    [Fact]
    public void The_api_policy_allows_nothing()
    {
        TechStrapCsp.ForApi().ShouldBe("default-src 'none'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'");
    }
}
