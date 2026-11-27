namespace TechStrap.WebhookCatcher;

/// <summary>One delivery the Worker sent here instead of to <paramref name="OriginalHost"/>.</summary>
public sealed record CapturedRequest(DateTimeOffset ReceivedAt, string OriginalHost, string? EventId, string ContentType, string Body);

/// <summary>The newest deliveries, in memory only: restarting the catcher forgets them.</summary>
public sealed class CaptureStore
{
    public const int Capacity = 200;
    public const int MaxBodyBytes = 65536;

    private readonly Lock _gate = new();
    private readonly Queue<CapturedRequest> _requests = new();

    public void Add(CapturedRequest request)
    {
        lock (_gate)
        {
            _requests.Enqueue(request);
            if (_requests.Count > Capacity)
            {
                _requests.Dequeue();
            }
        }
    }

    /// <summary>A copy of what is held, newest first.</summary>
    public IReadOnlyList<CapturedRequest> Snapshot()
    {
        lock (_gate)
        {
            return _requests.Reverse().ToArray();
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _requests.Clear();
        }
    }
}
