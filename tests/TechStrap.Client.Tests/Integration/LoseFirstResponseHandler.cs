using System.Collections.Concurrent;
using TechStrap.Contracts.Http;

namespace TechStrap.Client.Tests.Integration;

/// <summary>
/// Sits below the client's own handlers. It forwards the first request to the server, so the ticket is created, then drops the response and fails the way a lost connection does; later requests pass through.
/// It records the Idempotency-Key of every request it sees (null when there was none).
/// </summary>
internal sealed class LoseFirstResponseHandler : DelegatingHandler
{
    private readonly ConcurrentQueue<string?> _keys = new();
    private int _calls;

    public IReadOnlyList<string?> IdempotencyKeys => [.. _keys];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        _keys.Enqueue(request.Headers.TryGetValues(HeaderNames.IdempotencyKey, out var values) ? values.Single() : null);
        var response = await base.SendAsync(request, cancellationToken);
        if (Interlocked.Increment(ref _calls) == 1)
        {
            response.Dispose();
            throw new HttpRequestException("The response was lost.");
        }

        return response;
    }
}
