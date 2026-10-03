using Microsoft.Extensions.Time.Testing;
using TechStrap.Domain.Rules;
using TechStrap.Domain.Tickets;

namespace TechStrap.Domain.Tests.Tickets;

public sealed class MessageTests
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 2, 9, 0, 0, TimeSpan.Zero));
    private readonly Guid _ticketId = Guid.NewGuid();
    private readonly Guid _authorId = Guid.NewGuid();

    [Fact]
    public void A_public_agent_message_is_visible_to_the_customer()
    {
        var message = Message.Create(_ticketId, AuthorType.Agent, _authorId, MessageVisibility.Public, "<p>Hi</p>", _clock).Value;

        message.IsVisibleToCustomer.ShouldBeTrue();
        message.CreatedAt.ShouldBe(_clock.GetUtcNow());
        message.TicketId.ShouldBe(_ticketId);
    }

    [Fact]
    public void An_internal_note_is_never_visible_to_the_customer()
    {
        Message.Create(_ticketId, AuthorType.Agent, _authorId, MessageVisibility.Internal, "note", _clock).Value
            .IsVisibleToCustomer.ShouldBeFalse();
    }

    [Fact]
    public void A_requester_cannot_write_an_internal_message()
    {
        var result = Message.Create(_ticketId, AuthorType.Requester, _authorId, MessageVisibility.Internal, "x", _clock);

        result.Error!.Code.ShouldBe("internal-message-by-requester");
    }

    [Theory]
    [InlineData(AuthorType.Agent)]
    [InlineData(AuthorType.Requester)]
    public void Agent_and_requester_messages_need_an_author(AuthorType type)
    {
        Message.Create(_ticketId, type, null, MessageVisibility.Public, "x", _clock).Error!.Code.ShouldBe("author-required");
    }

    [Fact]
    public void A_system_message_has_no_author_even_when_one_is_given()
    {
        Message.Create(_ticketId, AuthorType.System, _authorId, MessageVisibility.Internal, "x", _clock).Value
            .AuthorId.ShouldBeNull();
    }

    [Fact]
    public void A_blank_or_oversized_body_is_rejected()
    {
        Message.Create(_ticketId, AuthorType.Agent, _authorId, MessageVisibility.Public, "  ", _clock).Error!.Code.ShouldBe("body-required");
        Message.Create(_ticketId, AuthorType.Agent, _authorId, MessageVisibility.Public, new string('a', DomainLimits.MessageBodyMaxLength + 1), _clock)
            .Error!.Code.ShouldBe("body-too-long");
    }

    [Fact]
    public void The_reserved_email_identifiers_start_empty_and_can_be_stored()
    {
        var message = Message.Create(_ticketId, AuthorType.Requester, _authorId, MessageVisibility.Public, "x", _clock).Value;
        message.MessageId.ShouldBeNull();
        message.InReplyTo.ShouldBeNull();

        message.SetEmailIdentifiers("<abc@mail.example>", "<prev@mail.example>").IsSuccess.ShouldBeTrue();

        message.MessageId.ShouldBe("<abc@mail.example>");
        message.InReplyTo.ShouldBe("<prev@mail.example>");
    }

    [Fact]
    public void An_attachment_added_to_a_message_is_tracked_as_new_and_linked_to_it()
    {
        var message = Message.Create(_ticketId, AuthorType.Agent, _authorId, MessageVisibility.Public, "x", _clock).Value;

        var attachment = message.AddAttachment("log.txt", "text/plain", 120, "tickets/1/log.txt", _clock).Value;

        message.NewAttachments.ShouldHaveSingleItem().ShouldBe(attachment);
        attachment.MessageId.ShouldBe(message.Id);
        attachment.TicketId.ShouldBe(_ticketId);
    }

    [Theory]
    [InlineData("../etc/passwd", "text/plain", 10, "file-name-invalid")]
    [InlineData("a\\b.txt", "text/plain", 10, "file-name-invalid")]
    [InlineData("a.txt", "plain", 10, "content-type-invalid")]
    [InlineData("a.txt", "text/plain", 0, "size-invalid")]
    [InlineData("a.txt", "text/plain", DomainLimits.AttachmentMaxBytes + 1, "size-invalid")]
    public void An_invalid_attachment_is_rejected_and_not_tracked(string name, string type, long size, string code)
    {
        var message = Message.Create(_ticketId, AuthorType.Agent, _authorId, MessageVisibility.Public, "x", _clock).Value;

        message.AddAttachment(name, type, size, "key", _clock).Error!.Code.ShouldBe(code);

        message.NewAttachments.ShouldBeEmpty();
    }
}
