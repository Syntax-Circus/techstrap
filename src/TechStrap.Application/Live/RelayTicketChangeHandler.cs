using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SyntaxCircus.Common;
using TechStrap.Contracts.Live;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Application.Live;

public interface IRelayTicketChangeHandler
{
    Task<Result> HandleAsync(RelayTicketChangeRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// The Api's end of the Postgres relay (D-018): validates a payload that arrived on the NOTIFY channel and forwards it to the broadcaster. Anything can
/// write to a channel, so the payload is untrusted: too big, not JSON, or not a valid change is a failure Result, never an exception, and is never forwarded.
/// A forwarding failure is a Result too, so one bad moment never ends the listener's loop.
/// </summary>
public sealed class RelayTicketChangeHandler(ITicketChangeBroadcaster broadcaster, ILogger<RelayTicketChangeHandler> logger) : IRelayTicketChangeHandler
{
    private const int TicketNumberMaxLength = 64;

    private static readonly HashSet<string> Kinds = [TicketChangeKinds.Created, TicketChangeKinds.Updated, TicketChangeKinds.Resync];

    private static readonly HashSet<string> EventTypes = new(
        typeof(TicketEventTypes).GetFields().Where(field => field.IsLiteral).Select(field => (string)field.GetRawConstantValue()!), StringComparer.Ordinal);

    public async Task<Result> HandleAsync(RelayTicketChangeRequest request, CancellationToken cancellationToken)
    {
        var payload = request.Payload;
        if (payload is null)
        {
            return Result.Failure(LiveErrors.ChangeInvalid());
        }

        // Count bytes, not characters, and do it before parsing: the cap is what keeps a stray writer from making the API parse a large value.
        if (Encoding.UTF8.GetByteCount(payload) > TicketLiveLimits.MaxChangePayloadBytes)
        {
            return Result.Failure(LiveErrors.ChangeTooLarge());
        }

        if (string.IsNullOrWhiteSpace(payload))
        {
            return Result.Failure(LiveErrors.ChangeInvalid());
        }

        var change = Parse(payload);
        if (change is null)
        {
            return Result.Failure(LiveErrors.ChangeInvalid());
        }

        try
        {
            await broadcaster.PublishAsync(change, cancellationToken);
            return Result.Success();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Type name only: an exception message may carry data (log redaction rule).
            logger.LogWarning("Relaying a ticket change failed ({ExceptionType}).", exception.GetType().Name);
            return Result.Failure(LiveErrors.RelayFailed());
        }
    }

    private static TicketChange? Parse(string payload)
    {
        TicketChangedDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<TicketChangedDto>(payload, JsonSerializerOptions.Web);
        }
        catch (JsonException)
        {
            return null;
        }

        if (dto is null || dto.Kind is null || !Kinds.Contains(dto.Kind))
        {
            return null;
        }

        // A resync names no ticket, so its ids are not checked; anything else must be a complete change.
        if (dto.Kind == TicketChangeKinds.Resync)
        {
            return dto.EventId == Guid.Empty ? null : TicketChange.Resync(dto.EventId, dto.OccurredAt);
        }

        var complete = dto.EventId != Guid.Empty
            && dto.TicketId != Guid.Empty
            && dto.ProductId != Guid.Empty
            && !string.IsNullOrWhiteSpace(dto.TicketNumber)
            && dto.TicketNumber.Length <= TicketNumberMaxLength
            && dto.EventType is not null && EventTypes.Contains(dto.EventType)
            && dto.OccurredAt != default;
        return complete ? TicketChange.From(dto) : null;
    }
}
