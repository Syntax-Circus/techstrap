using System.Text.Json;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Api.Tests.Tickets;

public sealed class TicketDtoSerializationTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    [Fact]
    public void TicketDetailDto_round_trips_through_json_serializer()
    {
        // Arrange
        var attachments = new[] { new AttachmentDto(Guid.NewGuid(), "file.pdf", "application/pdf", 1024) };
        var linkedArticles = new[] { new LinkedArticleDto(Guid.NewGuid(), "Article Title", "article-slug") };
        var messages = new[]
        {
            new MessageDto(
                Guid.NewGuid(),
                "Agent",
                Guid.NewGuid(),
                "John Doe",
                "Public",
                "<p>Hello</p>",
                DateTimeOffset.UtcNow,
                attachments,
                linkedArticles)
        };
        var events = new[]
        {
            new TicketEventDto(
                Guid.NewGuid(),
                "Created",
                "System",
                null,
                null,
                "{}",
                DateTimeOffset.UtcNow)
        };
        var tags = new[] { new TicketTagDto(Guid.NewGuid(), "urgent", "FF0000") };
        var requester = new TicketRequesterDto(Guid.NewGuid(), "user@example.com", "User Name", null);

        var dto = new TicketDetailDto(
            Guid.NewGuid(),
            "TKT-001",
            "Subject Line",
            "Pending",
            "High",
            Guid.NewGuid(),
            "Product Name",
            requester,
            Guid.NewGuid(),
            "Agent Name",
            false,
            tags,
            "Web",
            null,
            null,
            false,
            DateTimeOffset.UtcNow.AddHours(-1),
            DateTimeOffset.UtcNow.AddMinutes(-30),
            null,
            null,
            DateTimeOffset.UtcNow,
            12345,
            messages,
            events);

        // Act
        var json = JsonSerializer.Serialize(dto, Options);
        var deserialized = JsonSerializer.Deserialize<TicketDetailDto>(json, Options);

        // Assert
        deserialized.ShouldNotBeNull();
        deserialized!.Id.ShouldBe(dto.Id);
        deserialized.Number.ShouldBe(dto.Number);
        deserialized.Subject.ShouldBe(dto.Subject);
        deserialized.Status.ShouldBe(dto.Status);
        deserialized.Priority.ShouldBe(dto.Priority);
        deserialized.ProductId.ShouldBe(dto.ProductId);
        deserialized.ProductName.ShouldBe(dto.ProductName);
        deserialized.Requester.Id.ShouldBe(dto.Requester.Id);
        deserialized.Requester.Email.ShouldBe(dto.Requester.Email);
        deserialized.AssigneeId.ShouldBe(dto.AssigneeId);
        deserialized.AssigneeName.ShouldBe(dto.AssigneeName);
        deserialized.IsSpam.ShouldBe(dto.IsSpam);
        deserialized.Channel.ShouldBe(dto.Channel);
        deserialized.RowVersion.ShouldBe(dto.RowVersion);

        // Assert rowVersion serializes as a JSON number
        json.ShouldContain("\"rowVersion\":12345");

        // Assert status serializes as the string "Pending"
        json.ShouldContain("\"status\":\"Pending\"");
    }

    [Fact]
    public void AgentMessageResponse_round_trips_through_json_serializer()
    {
        // Arrange
        var attachments = new[] { new AttachmentDto(Guid.NewGuid(), "attachment.pdf", "application/pdf", 2048) };
        var linkedArticles = new[] { new LinkedArticleDto(Guid.NewGuid(), "Help Article", "help-article") };

        var message = new MessageDto(
            Guid.NewGuid(),
            "Agent",
            Guid.NewGuid(),
            "Agent Name",
            "Public",
            "<p>Response message</p>",
            DateTimeOffset.UtcNow,
            attachments,
            linkedArticles);

        var ticketState = new TicketStateDto(
            Guid.NewGuid(),
            "TKT-002",
            "Pending",
            "Normal",
            Guid.NewGuid(),
            Guid.NewGuid(),
            false,
            new[] { Guid.NewGuid() },
            DateTimeOffset.UtcNow,
            54321);

        var response = new AgentMessageResponse(message, ticketState);

        // Act
        var json = JsonSerializer.Serialize(response, Options);
        var deserialized = JsonSerializer.Deserialize<AgentMessageResponse>(json, Options);

        // Assert
        deserialized.ShouldNotBeNull();
        deserialized!.Message.Id.ShouldBe(message.Id);
        deserialized.Message.AuthorType.ShouldBe(message.AuthorType);
        deserialized.Message.Visibility.ShouldBe(message.Visibility);
        deserialized.Ticket.Id.ShouldBe(ticketState.Id);
        deserialized.Ticket.Status.ShouldBe(ticketState.Status);
        deserialized.Ticket.Priority.ShouldBe(ticketState.Priority);
        deserialized.Ticket.RowVersion.ShouldBe(ticketState.RowVersion);

        // Assert rowVersion serializes as a JSON number
        json.ShouldContain("\"rowVersion\":54321");

        // Assert status serializes as the string "Pending"
        json.ShouldContain("\"status\":\"Pending\"");
    }
}
