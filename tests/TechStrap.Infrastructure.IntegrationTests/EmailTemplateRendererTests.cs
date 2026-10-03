using Microsoft.Extensions.Options;
using TechStrap.Application.Email;
using TechStrap.Infrastructure.Email;

namespace TechStrap.Infrastructure.IntegrationTests;

public sealed class EmailTemplateRendererTests
{
    private static readonly EmailBranding Orbitly = new("Orbitly", "https://cdn.orbitly.example/logo.png", "#7C3AED", "support@orbitly.example", "help@orbitly.example");
    private static readonly EmailBranding Amber = new("Paperplane", null, "#F59E0B", null, null);
    private static readonly TicketConfirmationEmail Model = new("ORB-38", "Can't sign in", "Ann", "https://help.example/t/abc", null);

    private static EmailTemplateRenderer Renderer(bool showPoweredBy = true) =>
        new(Options.Create(new EmailBrandingOptions { ShowPoweredBy = showPoweredBy }));

    [Fact]
    public void The_confirmation_uses_the_product_name_number_and_link()
    {
        var email = Renderer().RenderTicketConfirmation(Model, Orbitly);

        email.Subject.ShouldBe("[ORB-38] We've got your request: Can't sign in");
        email.Text.ShouldContain("ORB-38");
        email.Text.ShouldContain("https://help.example/t/abc");
        email.Html.ShouldContain("Orbitly");
        email.Html.ShouldContain("href=\"https://help.example/t/abc\"");
        email.From.ShouldBe("Orbitly <support@orbitly.example>");
        email.ReplyTo.ShouldBe("help@orbitly.example");
    }

    [Fact]
    public void A_product_without_addresses_leaves_from_and_reply_to_to_the_installation_default()
    {
        var email = Renderer().RenderTicketConfirmation(Model, Amber);

        email.From.ShouldBeNull();
        email.ReplyTo.ShouldBeNull();
    }

    [Fact]
    public void The_accent_rule_from_phase_02_is_applied()
    {
        var html = Renderer().RenderTicketConfirmation(Model, Amber).Html;

        html.ShouldContain("#F59E0B");
        html.ShouldContain("#000000");
        html.ShouldContain("#9D6507");
    }

    [Fact]
    public void Customer_content_is_escaped_in_html_and_plain_in_text()
    {
        var email = Renderer().RenderTicketConfirmation(Model with { Subject = "<script>alert(1)</script>", RequesterName = "<b>Ann</b>" }, Orbitly);

        email.Html.ShouldNotContain("<script>");
        email.Html.ShouldContain("&lt;script&gt;");
        email.Html.ShouldNotContain("<b>Ann</b>");
        email.Text.ShouldNotContain("&lt;");
    }

    [Fact]
    public void Powered_by_links_to_github_in_html_and_is_a_bare_url_in_text()
    {
        var email = Renderer().RenderTicketConfirmation(Model, Orbitly);

        email.Html.ShouldContain($"href=\"{EmailBrandingOptions.PoweredByUrl}\"");
        email.Html.ShouldContain("Powered by TechStrap");
        email.Text.ShouldContain($"Powered by TechStrap: {EmailBrandingOptions.PoweredByUrl}");
    }

    [Fact]
    public void Powered_by_is_omitted_when_the_setting_is_false()
    {
        var email = Renderer(showPoweredBy: false).RenderTicketConfirmation(Model, Orbitly);

        email.Html.ShouldNotContain("TechStrap");
        email.Text.ShouldNotContain("TechStrap");
    }

    [Fact]
    public void The_agent_public_name_is_shown_when_present_and_the_model_cannot_carry_an_email()
    {
        Renderer().RenderTicketConfirmation(Model with { AgentPublicName = "Sam from Orbitly Support" }, Orbitly)
            .Text.ShouldContain("Sam from Orbitly Support");
        typeof(TicketConfirmationEmail).GetProperties().Select(property => property.Name)
            .ShouldNotContain(name => name.Contains("Email", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Two_products_render_their_own_branding()
    {
        var orbitly = Renderer().RenderTicketConfirmation(Model, Orbitly).Html;
        var paperplane = Renderer().RenderTicketConfirmation(Model, Amber).Html;

        orbitly.ShouldContain("#7C3AED");
        orbitly.ShouldContain("https://cdn.orbitly.example/logo.png");
        paperplane.ShouldContain("Paperplane");
        paperplane.ShouldNotContain("Orbitly");
        paperplane.ShouldNotContain("<img");
    }
}
