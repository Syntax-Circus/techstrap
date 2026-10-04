using System.Text.Json;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Application.DeadLetters;
using TechStrap.Application.Persistence;
using TechStrap.Domain.Outbox;

namespace TechStrap.Application.Tests.DeadLetters;

public sealed class ListDeadLettersRequestHandlerTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private readonly IEmailOutboxStore _store = Substitute.For<IEmailOutboxStore>();

    private static EmailOutboxItem Row(string address, string payload, Guid? ticketId, Guid? productId, int attempts, string? lastError, DateTimeOffset createdAt) =>
        EmailOutboxItem.Restore(
            Guid.CreateVersion7(), "ticket-confirmation", address, payload, productId, ticketId, OutboxStatus.DeadLettered, attempts,
            createdAt, null, null, lastError, createdAt, null);

    [Fact]
    public async Task A_page_maps_masked_recipient_ids_attempts_and_error_and_never_the_payload()
    {
        var ticketId = Guid.CreateVersion7();
        var productId = Guid.CreateVersion7();
        var created = new DateTimeOffset(2026, 10, 3, 9, 0, 0, TimeSpan.Zero);
        var first = Row("ann@example.com", "{\"portalLink\":\"https://portal.test/t/SECRETLINK\"}", ticketId, productId, 5, "smtp-permanent", created);
        var second = Row("bob@example.com", "{\"portalLink\":\"https://portal.test/t/OTHERLINK\"}", null, null, 5, null, created.AddMinutes(-1));
        _store.ListDeadLettersAsync(1, 25, Ct).Returns(new PagedResult<EmailOutboxItem>([first, second], 1, 25, 2));

        var result = await new ListDeadLettersRequestHandler(_store).HandleAsync(1, 25, Ct);

        result.IsSuccess.ShouldBeTrue();
        var page = result.Value;
        page.Items.Count.ShouldBe(2);
        var dto = page.Items[0];
        dto.Id.ShouldBe(first.Id);
        dto.Kind.ShouldBe("ticket-confirmation");
        dto.Recipient.ShouldBe("a***@example.com");
        dto.TicketId.ShouldBe(ticketId);
        dto.ProductId.ShouldBe(productId);
        dto.Attempts.ShouldBe(5);
        dto.LastError.ShouldBe("smtp-permanent");
        dto.CreatedAt.ShouldBe(created);
        page.Items[1].Recipient.ShouldBe("b***@example.com");
        page.Items[1].LastError.ShouldBeNull();

        var json = JsonSerializer.Serialize(page);
        json.ShouldNotContain("SECRETLINK");
        json.ShouldNotContain("OTHERLINK");
        json.ShouldNotContain("portalLink");
        json.ShouldNotContain("ann@example.com");
        json.ShouldNotContain("bob@example.com");
    }

    [Fact]
    public async Task Paging_values_come_from_the_store_result()
    {
        _store.ListDeadLettersAsync(7, 1000, Ct).Returns(new PagedResult<EmailOutboxItem>([], 7, 100, 3));

        var result = await new ListDeadLettersRequestHandler(_store).HandleAsync(7, 1000, Ct);

        result.Value.Page.ShouldBe(7);
        result.Value.PageSize.ShouldBe(100);
        result.Value.TotalCount.ShouldBe(3);
        result.Value.Items.ShouldBeEmpty();
    }
}
