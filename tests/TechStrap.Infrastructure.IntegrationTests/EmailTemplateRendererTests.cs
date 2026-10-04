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

    private static AgentReplyEmail Reply(bool solved = false, string? requester = "Ann") =>
        new("ORB-42", "Printer jam", requester, "https://help.test/t/abc", "Sam from Orbitly Support", Guid.NewGuid(), solved);

    [Fact]
    public void An_agent_reply_shows_the_public_name_the_body_and_the_link()
    {
        var email = Renderer().RenderAgentReply(Reply(), "<p>Try <strong>this</strong></p>", Orbitly);

        email.Subject.ShouldBe("[ORB-42] Re: Printer jam");
        email.Html.ShouldContain("Sam from Orbitly Support");
        email.Html.ShouldContain("<strong>this</strong>");
        email.Html.ShouldContain("https://help.test/t/abc");
        email.Text.ShouldContain("Try this");
        email.Text.ShouldContain("https://help.test/t/abc");
        email.Html.ShouldNotContain("solved");
        email.Text.ShouldNotContain("solved");
    }

    [Fact]
    public void A_send_and_solve_reply_says_it_is_solved()
    {
        var email = Renderer().RenderAgentReply(Reply(solved: true), "<p>Done</p>", Orbitly);

        email.Html.ShouldContain("marked this request as solved. Reply within 7 days if you need anything else.");
        email.Text.ShouldContain("We've marked this request as solved. Reply within 7 days if you need anything else.");
    }

    [Fact]
    public void The_reply_body_is_inserted_as_given_but_model_fields_are_encoded()
    {
        var email = Renderer().RenderAgentReply(Reply(requester: "<b>Ann</b>"), "<p>Try <em>this</em> &amp; that</p>", Orbitly);

        email.Html.ShouldNotContain("<b>Ann</b>");
        email.Html.ShouldContain("&lt;b&gt;Ann&lt;/b&gt;");
        email.Html.ShouldContain("<p>Try <em>this</em> &amp; that</p>");
    }

    [Fact]
    public void A_solved_notice_has_the_reopen_window_and_link()
    {
        var email = Renderer().RenderTicketSolved(new("ORB-42", "Printer jam", "Ann", "https://help.test/t/abc", 7), Orbitly);

        email.Subject.ShouldBe("[ORB-42] Solved: Printer jam");
        email.Html.ShouldContain("reply within 7 days");
        email.Html.ShouldContain("href=\"https://help.test/t/abc\"");
        email.Text.ShouldContain("reply within 7 days");
        email.Text.ShouldContain("https://help.test/t/abc");
        email.From.ShouldBe("Orbitly <support@orbitly.example>");
    }

    [Fact]
    public void An_assignment_alert_links_into_admin_only_when_configured()
    {
        var withLink = Renderer().RenderTicketAssigned(new("ORB-42", "Printer jam", "Orbitly", "Mia", "https://admin.test/tickets/ORB-42"), Orbitly);
        var without = Renderer().RenderTicketAssigned(new("ORB-42", "Printer jam", "Orbitly", null, null), Orbitly);

        withLink.Subject.ShouldBe("[ORB-42] Assigned to you: Printer jam");
        withLink.Html.ShouldContain("href=\"https://admin.test/tickets/ORB-42\"");
        withLink.Html.ShouldContain("Open in TechStrap");
        withLink.Html.ShouldContain("Assigned by Mia");
        withLink.Text.ShouldContain("https://admin.test/tickets/ORB-42");
        without.Html.ShouldNotContain("<a ");
        without.Html.ShouldContain("Open TechStrap to work on it.");
        without.Text.ShouldContain("Open TechStrap to work on it.");
        without.Html.ShouldNotContain("Assigned by");
        withLink.Html.ShouldNotContain("Powered by");
        without.Html.ShouldNotContain("Powered by");
        withLink.Text.ShouldNotContain("Powered by");
    }

    [Fact]
    public void An_agent_reply_uses_the_reopen_days_it_carries()
    {
        var email = Renderer().RenderAgentReply(Reply(solved: true) with { ReopenDays = 10 }, "<p>Done</p>", Orbitly);

        email.Html.ShouldContain("Reply within 10 days");
        email.Text.ShouldContain("Reply within 10 days");
    }

    [Fact]
    public void An_agent_reply_queued_before_06b_falls_back_to_the_default_reopen_days()
    {
        var email = Renderer().RenderAgentReply(Reply(solved: true) with { ReopenDays = 0 }, "<p>Done</p>", Orbitly);

        email.Text.ShouldContain("Reply within 7 days");
        email.Html.ShouldContain("Reply within 7 days");
    }

    [Fact]
    public void A_one_day_reopen_window_is_singular_in_the_reply_and_solved_emails()
    {
        var reply = Renderer().RenderAgentReply(Reply(solved: true) with { ReopenDays = 1 }, "<p>Done</p>", Orbitly);
        var solved = Renderer().RenderTicketSolved(new("ORB-42", "Printer jam", "Ann", "https://help.test/t/abc", 1), Orbitly);

        reply.Html.ShouldContain("Reply within 1 day if");
        reply.Text.ShouldContain("Reply within 1 day if");
        reply.Text.ShouldNotContain("1 days");
        solved.Html.ShouldContain("reply within 1 day,");
        solved.Text.ShouldContain("reply within 1 day,");
        solved.Text.ShouldNotContain("1 days");
    }

    [Fact]
    public void A_new_ticket_alert_has_subject_content_and_no_powered_by()
    {
        var model = new NewTicketAlertEmail("ORB-42", "Printer jam", "Orbitly", "Ann <ann@x.test>", false, "https://admin.test/tickets/ORB-42");
        var email = Renderer().RenderNewTicketAlert(model, Orbitly);
        var followUp = Renderer().RenderNewTicketAlert(model with { IsFollowUp = true, AdminLink = "javascript:alert(1)" }, Orbitly);

        email.Subject.ShouldBe("[ORB-42] New ticket: Printer jam");
        email.Html.ShouldContain("Printer jam");
        email.Html.ShouldContain("Orbitly");
        email.Html.ShouldContain("Ann &lt;ann@x.test&gt;");
        email.Html.ShouldContain("href=\"https://admin.test/tickets/ORB-42\"");
        email.Text.ShouldContain("https://admin.test/tickets/ORB-42");
        email.Html.ShouldNotContain("Powered by");
        email.Text.ShouldNotContain("Powered by");
        followUp.Subject.ShouldBe("[ORB-42] New follow-up: Printer jam");
        followUp.Html.ShouldNotContain("<a ");
    }

    [Fact]
    public void A_customer_reply_alert_has_subject_content_and_no_powered_by()
    {
        var model = new CustomerReplyAlertEmail("ORB-42", "Printer jam", "Orbitly", true, "https://admin.test/tickets/ORB-42");
        var email = Renderer().RenderCustomerReplyAlert(model, Orbitly);
        var plain = Renderer().RenderCustomerReplyAlert(model with { Reopened = false, AdminLink = null }, Orbitly);

        email.Subject.ShouldBe("[ORB-42] Customer replied: Printer jam");
        email.Html.ShouldContain("reopened");
        email.Html.ShouldContain("href=\"https://admin.test/tickets/ORB-42\"");
        email.Html.ShouldNotContain("Powered by");
        email.Text.ShouldNotContain("Powered by");
        plain.Html.ShouldNotContain("reopened");
        plain.Html.ShouldNotContain("<a ");
    }

    [Fact]
    public void An_access_links_email_has_a_button_per_link_encodes_fields_and_follows_powered_by()
    {
        var model = new AccessLinksEmail("<b>Ann</b>", [
            new AccessLinkEntry("ORB-1", "<i>One</i>", "https://help.test/t/a?x=1&y=2"),
            new AccessLinkEntry("ORB-2", "Two", "https://help.test/t/b")]);
        var email = Renderer().RenderAccessLinks(model, Orbitly);

        email.Subject.ShouldBe("Your request links");
        email.Html.ShouldContain("Orbitly");
        email.Html.ShouldContain("href=\"https://help.test/t/a?x=1&amp;y=2\"");
        email.Html.ShouldContain("href=\"https://help.test/t/b\"");
        email.Html.ShouldContain("&lt;b&gt;Ann&lt;/b&gt;");
        email.Html.ShouldContain("&lt;i&gt;One&lt;/i&gt;");
        email.Html.ShouldNotContain("<b>Ann</b>");
        email.Html.ShouldNotContain("<i>One</i>");
        System.Text.RegularExpressions.Regex.Matches(email.Html, "display:inline-block").Count.ShouldBe(2);
        email.Html.ShouldContain("Or open this link");
        email.Text.ShouldContain("ORB-1");
        email.Text.ShouldContain("https://help.test/t/b");
        email.Html.ShouldContain("Powered by TechStrap");
        Renderer(showPoweredBy: false).RenderAccessLinks(model, Orbitly).Html.ShouldNotContain("TechStrap");
    }

    [Fact]
    public void Html_to_text_keeps_paragraphs_lists_and_link_targets()
    {
        var text = HtmlText.ToPlainText("<p>a</p><ul><li>b</li></ul><p><a href=\"https://x.test\">x</a> &amp; y</p>");
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        lines.ShouldContain("a");
        lines.ShouldContain(line => line.Contains('b') && !line.Contains('a'));
        lines.ShouldContain("x (https://x.test) & y");
    }

    [Fact]
    public void Reply_and_solved_emails_carry_the_plain_link_fallback_in_the_accent_ink()
    {
        var reply = Renderer().RenderAgentReply(Reply(), "<p>x</p>", Amber).Html;
        var solved = Renderer().RenderTicketSolved(new("ORB-42", "Printer jam", "Ann", "https://help.test/t/abc", 7), Amber).Html;

        foreach (var html in new[] { reply, solved })
        {
            html.ShouldContain("Or open this link: <a href=\"https://help.test/t/abc\" style=\"color:#9D6507;\">https://help.test/t/abc</a>");
        }
    }

    [Fact]
    public void Html_to_text_survives_extreme_nesting()
    {
        var html = new string('x', 0) + string.Concat(Enumerable.Repeat("<div>", 10_000)) + "innermost" + string.Concat(Enumerable.Repeat("</div>", 10_000));

        HtmlText.ToPlainText(html).ShouldContain("innermost");
    }
}
