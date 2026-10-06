using TechStrap.Contracts.Tickets;
using TechStrap.Portal.Clients;
using TechStrap.Portal.Tickets;

namespace TechStrap.Portal.Tests.Tickets;

/// <summary>
/// P09-T08 and T23 without a host: the view model the ticket page is built from. The five statuses in the customer's words (UX brief), "You" for the customer's own messages, the API's resolved name for an agent
/// exactly as it came, a neutral label for the system, the Portal's own attachment links built from the real token, and plain text kept as plain text (the page encodes it).
/// </summary>
public sealed class CustomerTicketPresenterTests
{
    private static TicketToken Token()
    {
        TicketToken.TryParse(TicketTestKit.Token, out var token).ShouldBeTrue();
        return token;
    }

    private static CustomerTicketViewModel Present(CustomerTicketDto? ticket = null) => CustomerTicketPresenter.Present(ticket ?? TicketTestKit.Ticket(), Token());

    [Theory]
    [InlineData("New", "Received", null)]
    [InlineData("Open", "In progress", null)]
    [InlineData("Pending", "Waiting for your reply", null)]
    [InlineData("Solved", "Solved", "This ticket is solved. If you reply, it will be reopened.")]
    [InlineData("Closed", "Closed", "This ticket is closed. If you reply, we will start a new follow-up ticket linked to it.")]
    public void Each_status_has_the_customers_wording_and_its_note(string status, string label, string? note)
    {
        var model = Present(TicketTestKit.Ticket(status));

        model.StatusLabel.ShouldBe(label);
        model.StatusNote.ShouldBe(note);
    }

    [Theory]
    [InlineData("closed")]
    [InlineData("CLOSED")]
    public void A_status_is_matched_without_regard_to_case(string status)
    {
        var model = Present(TicketTestKit.Ticket(status));

        model.IsClosed.ShouldBeTrue();
        model.StatusLabel.ShouldBe("Closed");
    }

    [Theory]
    [InlineData("Escalated")]
    [InlineData("")]
    [InlineData(null)]
    public void A_status_this_build_does_not_know_is_in_progress_and_never_an_empty_label(string? status)
    {
        var (label, note) = CustomerTicketPresenter.Status(status);

        label.ShouldBe("In progress");
        note.ShouldBeNull();
    }

    [Fact]
    public void Only_a_closed_ticket_is_closed_and_only_a_solved_one_is_solved()
    {
        Present(TicketTestKit.Ticket("Closed")).IsClosed.ShouldBeTrue();
        Present(TicketTestKit.Ticket("Closed")).IsSolved.ShouldBeFalse();
        Present(TicketTestKit.Ticket("Solved")).IsSolved.ShouldBeTrue();
        Present(TicketTestKit.Ticket("Solved")).IsClosed.ShouldBeFalse();
        Present(TicketTestKit.Ticket("Open")).IsClosed.ShouldBeFalse();
    }

    [Fact]
    public void The_authors_are_you_the_agents_name_as_it_came_and_a_neutral_word_for_the_system()
    {
        var messages = Present().Messages;

        messages.Select(m => m.Author).ShouldBe(["You", "Sam from Paperplane Support", "Update"]);
        messages.Select(m => m.IsCustomer).ShouldBe([true, false, false]);
    }

    [Fact]
    public void An_agent_override_name_is_shown_unchanged()
    {
        var model = Present(TicketTestKit.Ticket(agentName: "Samantha from Paperplane Support"));

        model.Messages[1].Author.ShouldBe("Samantha from Paperplane Support");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_agent_message_with_no_name_is_attributed_to_support(string? name)
    {
        CustomerTicketPresenter.Author("Agent", name).ShouldBe("Support");
    }

    [Fact]
    public void A_name_with_markup_stays_text_for_the_page_to_encode()
    {
        var model = Present(TicketTestKit.Ticket(agentName: "<img src=x onerror=alert(1)>"));

        model.Messages[1].Author.ShouldBe("<img src=x onerror=alert(1)>");
    }

    [Theory]
    [InlineData("Requester", "You")]
    [InlineData("requester", "You")]
    [InlineData("System", "Update")]
    [InlineData("Whatever", "Update")]
    public void The_author_type_decides_the_label_whatever_its_case_and_a_system_or_unknown_one_is_neutral(string type, string label)
    {
        CustomerTicketPresenter.Author(type, "Should not show").ShouldBe(label);
    }

    [Fact]
    public void The_view_model_has_no_agent_email_id_or_avatar_and_no_internal_ids()
    {
        var names = new[] { typeof(CustomerTicketViewModel), typeof(CustomerMessageViewModel), typeof(CustomerAttachmentViewModel) }
            .SelectMany(t => t.GetProperties().Select(p => p.Name)).ToList();

        names.ShouldNotContain(n => n.Contains("Email", StringComparison.OrdinalIgnoreCase) || n.Contains("Avatar", StringComparison.OrdinalIgnoreCase) || n.Contains("AgentId", StringComparison.OrdinalIgnoreCase)
            || n.Contains("AuthorId", StringComparison.OrdinalIgnoreCase) || n.Contains("ProductId", StringComparison.OrdinalIgnoreCase) || n.Contains("Token", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void An_attachment_is_the_portals_own_link_built_from_the_real_token_with_a_readable_size()
    {
        var attachment = Present().Messages[1].Attachments.ShouldHaveSingleItem();

        attachment.Href.ShouldBe($"/t/{TicketTestKit.Token}/attachments/{TicketTestKit.AttachmentId}");
        attachment.Href.ShouldNotContain("[token]");
        attachment.FileName.ShouldBe("log.txt");
        attachment.SizeText.ShouldBe("2 KB");
    }

    [Theory]
    [InlineData(0L, "0 B")]
    [InlineData(1023L, "1023 B")]
    [InlineData(1024L, "1 KB")]
    [InlineData(1536L, "1.5 KB")]
    [InlineData(1_048_575L, "1024 KB")]
    [InlineData(1_048_576L, "1 MB")]
    [InlineData(10_485_760L, "10 MB")]
    [InlineData(-5L, "0 B")]
    public void A_size_is_written_in_bytes_kilobytes_or_megabytes(long bytes, string text) => CustomerTicketPresenter.Size(bytes).ShouldBe(text);

    [Fact]
    public void The_messages_keep_the_apis_order_and_bodies_unchanged()
    {
        var model = Present();

        model.Messages.Select(m => m.Id).ShouldBe(
            [Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"), Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002"), Guid.Parse("aaaaaaaa-0000-0000-0000-000000000003")]);
        model.Messages[1].BodyHtml.ShouldBe("<p>Try <b>this</b> first.</p>");
        model.Number.ShouldBe("PAP-42");
        model.ProductKey.ShouldBe("paperplane");
        model.Subject.ShouldBe("Printer jam");
    }

    [Fact]
    public void A_ticket_with_no_messages_has_an_empty_thread()
    {
        var dto = new CustomerTicketDto("PAP-1", "paperplane", "x", "New", DateTimeOffset.UnixEpoch, []);

        CustomerTicketPresenter.Present(dto, Token()).Messages.ShouldBeEmpty();
    }
}
