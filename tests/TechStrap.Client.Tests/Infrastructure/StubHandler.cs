using System.Net;
using System.Text;

namespace TechStrap.Client.Tests.Infrastructure;

/// <summary>What the stub saw for one send: the headers and the body are captured at send time, because the pipeline disposes the request afterwards.</summary>
internal sealed record RecordedRequest(HttpMethod Method, Uri? Uri, IReadOnlyDictionary<string, string[]> Headers, string? Body, HttpRequestMessage Message, HttpContent? Content)
{
    public string? Header(string name) => Headers.TryGetValue(name, out var values) ? string.Join(",", values) : null;
}

/// <summary>A scripted primary handler: each send takes the next step, which returns a response or throws. An empty script fails the test loudly.</summary>
internal sealed class StubHandler : HttpMessageHandler
{
    public const string CreatedBody = """{"ticketNumber":"TS-1001","viewUrl":"https://support.example.com/t/abc","warnings":[]}""";

    private readonly Queue<Func<CancellationToken, Task<HttpResponseMessage>>> _steps = new();
    private readonly TaskCompletionSource _received = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public List<RecordedRequest> Requests { get; } = [];

    /// <summary>Completes when the first request has reached the stub.</summary>
    public Task Received => _received.Task;

    public StubHandler Then(Func<CancellationToken, Task<HttpResponseMessage>> step)
    {
        _steps.Enqueue(step);
        return this;
    }

    public StubHandler Respond(HttpStatusCode status, string? body = null, string mediaType = "application/json", int times = 1)
    {
        for (var i = 0; i < times; i++)
        {
            Then(_ =>
            {
                var response = new HttpResponseMessage(status);
                if (body is not null)
                {
                    response.Content = new StringContent(body, Encoding.UTF8, mediaType);
                }

                return Task.FromResult(response);
            });
        }

        return this;
    }

    public StubHandler Created() => Respond(HttpStatusCode.Created, CreatedBody);

    public StubHandler Throw(Func<Exception> exception, int times = 1)
    {
        for (var i = 0; i < times; i++)
        {
            Then(_ => throw exception());
        }

        return this;
    }

    /// <summary>Never answers until the token is cancelled.</summary>
    public StubHandler Hang() => Then(async ct =>
    {
        await Task.Delay(Timeout.Infinite, ct);
        return new HttpResponseMessage(HttpStatusCode.OK);
    });

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var headers = request.Headers.ToDictionary(h => h.Key, h => h.Value.ToArray(), StringComparer.OrdinalIgnoreCase);
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new RecordedRequest(request.Method, request.RequestUri, headers, body, request, request.Content));
        _received.TrySetResult();

        if (!_steps.TryDequeue(out var step))
        {
            throw new InvalidOperationException("The stub has no scripted step left for this request.");
        }

        return await step(cancellationToken);
    }
}
