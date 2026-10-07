using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SyntaxCircus.Common;
using TechStrap.Application.Live;
using TechStrap.Contracts.Live;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Application.Tests.Live;

public sealed class RelayTicketChangeHandlerTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private readonly ITicketChangeBroadcaster _broadcaster = Substitute.For<ITicketChangeBroadcaster>();
    private readonly RelayTicketChangeHandler _handler;

    public RelayTicketChangeHandlerTests() =>
        _handler = new RelayTicketChangeHandler(_broadcaster, NullLogger<RelayTicketChangeHandler>.Instance);

    private static TicketChangedDto Valid(string kind = TicketChangeKinds.Updated, string eventType = TicketEventTypes.StatusChanged) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "ORB-42", Guid.NewGuid(), eventType, Guid.NewGuid(), new DateTimeOffset(2026, 10, 7, 9, 0, 0, TimeSpan.Zero), kind);

    private static RelayTicketChangeRequest Request(TicketChangedDto dto) =>
        new(JsonSerializer.Serialize(dto, JsonSerializerOptions.Web));

    [Fact]
    public async Task A_valid_change_is_forwarded_once_with_every_field()
    {
        var dto = Valid();

        var result = await _handler.HandleAsync(Request(dto), Ct);

        result.IsSuccess.ShouldBeTrue();
        await _broadcaster.Received(1).PublishAsync(Arg.Any<TicketChange>(), Arg.Any<CancellationToken>());
        await _broadcaster.Received(1).PublishAsync(TicketChange.From(dto), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_cancellation_token_reaches_the_broadcaster()
    {
        using var source = new CancellationTokenSource();

        await _handler.HandleAsync(Request(Valid()), source.Token);

        await _broadcaster.Received(1).PublishAsync(Arg.Any<TicketChange>(), source.Token);
    }

    [Theory]
    [InlineData(TicketChangeKinds.Created)]
    [InlineData(TicketChangeKinds.Updated)]
    public async Task Both_ordinary_kinds_are_accepted(string kind)
    {
        (await _handler.HandleAsync(Request(Valid(kind)), Ct)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task A_resync_needs_no_ticket()
    {
        var resync = new TicketChangedDto(Guid.NewGuid(), Guid.Empty, string.Empty, Guid.Empty, string.Empty, null, DateTimeOffset.UnixEpoch, TicketChangeKinds.Resync);

        var result = await _handler.HandleAsync(Request(resync), Ct);

        result.IsSuccess.ShouldBeTrue();
        await _broadcaster.Received(1).PublishAsync(Arg.Is<TicketChange>(change => change.Kind == TicketChangeKinds.Resync), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("42")]
    [InlineData("{\"kind\":\"Updated\"}")]
    [InlineData("{\"eventId\":\"nope\"}")]
    public async Task A_malformed_payload_is_a_failure_result_and_is_never_forwarded(string payload)
    {
        var result = await _handler.HandleAsync(new RelayTicketChangeRequest(payload), Ct);

        result.IsFailure.ShouldBeTrue();
        result.Errors[0].Kind.ShouldBe(ResultErrorKind.Validation);
        await _broadcaster.DidNotReceiveWithAnyArgs().PublishAsync(default!, Ct);
    }

    [Fact]
    public async Task A_null_payload_is_a_failure_result()
    {
        var result = await _handler.HandleAsync(new RelayTicketChangeRequest(null!), Ct);

        result.IsFailure.ShouldBeTrue();
        await _broadcaster.DidNotReceiveWithAnyArgs().PublishAsync(default!, Ct);
    }

    [Fact]
    public async Task An_unknown_kind_is_refused()
    {
        var result = await _handler.HandleAsync(Request(Valid(kind: "Deleted")), Ct);

        result.IsFailure.ShouldBeTrue();
        await _broadcaster.DidNotReceiveWithAnyArgs().PublishAsync(default!, Ct);
    }

    [Fact]
    public async Task An_unknown_event_type_is_refused()
    {
        var result = await _handler.HandleAsync(Request(Valid(eventType: "Exploded")), Ct);

        result.IsFailure.ShouldBeTrue();
        await _broadcaster.DidNotReceiveWithAnyArgs().PublishAsync(default!, Ct);
    }

    [Theory]
    [InlineData("ticket")]
    [InlineData("event")]
    [InlineData("product")]
    [InlineData("number")]
    [InlineData("time")]
    public async Task An_ordinary_change_with_a_missing_field_is_refused(string missing)
    {
        var valid = Valid();
        var broken = missing switch
        {
            "ticket" => valid with { TicketId = Guid.Empty },
            "event" => valid with { EventId = Guid.Empty },
            "product" => valid with { ProductId = Guid.Empty },
            "number" => valid with { TicketNumber = " " },
            _ => valid with { OccurredAt = default },
        };

        var result = await _handler.HandleAsync(Request(broken), Ct);

        result.IsFailure.ShouldBeTrue();
        await _broadcaster.DidNotReceiveWithAnyArgs().PublishAsync(default!, Ct);
    }

    [Fact]
    public async Task A_ticket_number_that_is_far_too_long_is_refused()
    {
        var result = await _handler.HandleAsync(Request(Valid() with { TicketNumber = new string('X', 65) }), Ct);

        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task An_oversize_payload_is_refused_before_it_is_parsed()
    {
        var oversize = new string(' ', TicketLiveLimits.MaxChangePayloadBytes + 1);

        var result = await _handler.HandleAsync(new RelayTicketChangeRequest(oversize), Ct);

        result.IsFailure.ShouldBeTrue();
        result.Errors[0].Code.ShouldBe("ticket-change-too-large");
        await _broadcaster.DidNotReceiveWithAnyArgs().PublishAsync(default!, Ct);
    }

    [Fact]
    public async Task The_size_cap_counts_bytes_not_characters()
    {
        // 1,100 two-byte characters are 2,200 bytes: over the cap although only 1,100 characters.
        var payload = new string((char)0xE9, 1100);

        var result = await _handler.HandleAsync(new RelayTicketChangeRequest(payload), Ct);

        result.Errors[0].Code.ShouldBe("ticket-change-too-large");
    }

    [Fact]
    public async Task A_valid_payload_exactly_at_the_cap_is_still_parsed()
    {
        var json = JsonSerializer.Serialize(Valid(), JsonSerializerOptions.Web);
        var padded = json + new string(' ', TicketLiveLimits.MaxChangePayloadBytes - json.Length);

        var result = await _handler.HandleAsync(new RelayTicketChangeRequest(padded), Ct);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task A_broadcaster_that_throws_is_a_failure_result_not_an_exception()
    {
        _broadcaster.PublishAsync(Arg.Any<TicketChange>(), Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("hub is down"));

        var result = await _handler.HandleAsync(Request(Valid()), Ct);

        result.IsFailure.ShouldBeTrue();
        result.Errors[0].Code.ShouldBe("ticket-change-relay-failed");
    }

    [Fact]
    public async Task A_cancelled_relay_still_cancels()
    {
        using var source = new CancellationTokenSource();
        await source.CancelAsync();
        _broadcaster.PublishAsync(Arg.Any<TicketChange>(), Arg.Any<CancellationToken>()).ThrowsAsync(new OperationCanceledException(source.Token));

        await Should.ThrowAsync<OperationCanceledException>(() => _handler.HandleAsync(Request(Valid()), source.Token));
    }
}
