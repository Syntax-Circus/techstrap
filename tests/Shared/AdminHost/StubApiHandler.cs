using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using TechStrap.Contracts.Agents;

namespace TechStrap.Tests.Shared.AdminHost;

/// <summary>One request the Admin sent to the stub API.</summary>
public sealed record StubApiRequest(HttpMethod Method, string Path, string Query, string? Authorization, string? ContentType, string? Body);

/// <summary>
/// Stands in for the TechStrap API behind the Admin's named HTTP clients (it replaces their primary handler, so the real handler pipeline above it
/// still runs: the auth handler adds the bearer token, the resilience handler retries). It records every request and answers by method and path;
/// an unconfigured request answers 404 with the problem code "stub-not-configured" so a test fails loudly.
/// </summary>
public sealed class StubApiHandler : HttpMessageHandler
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly List<StubApiRequest> _requests = [];
    private readonly List<(HttpMethod Method, string Path, Func<StubApiRequest, HttpResponseMessage> Respond)> _routes = [];
    private readonly object _gate = new();

    /// <summary>Every request received so far, in order.</summary>
    public IReadOnlyList<StubApiRequest> Requests
    {
        get
        {
            lock (_gate)
            {
                return [.. _requests];
            }
        }
    }

    /// <summary>How many requests with this method and path (any query) were received.</summary>
    public int Count(HttpMethod method, string path) => Requests.Count(r => r.Method == method && string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase));

    /// <summary>Answers requests for <paramref name="path"/> (path only, no query) with whatever <paramref name="respond"/> returns. A later route for the same method and path replaces an earlier one.</summary>
    public StubApiHandler On(HttpMethod method, string path, Func<StubApiRequest, HttpResponseMessage> respond)
    {
        lock (_gate)
        {
            _routes.RemoveAll(r => r.Method == method && string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase));
            _routes.Add((method, path, respond));
        }

        return this;
    }

    public StubApiHandler OnJson<T>(HttpMethod method, string path, T body, HttpStatusCode status = HttpStatusCode.OK) =>
        On(method, path, _ => JsonResponse(status, body));

    public StubApiHandler OnStatus(HttpMethod method, string path, HttpStatusCode status) => On(method, path, _ => new HttpResponseMessage(status));

    /// <summary>An RFC 7807 answer in the shape the API produces: <c>type</c> is the error code, <c>detail</c> the message.</summary>
    public StubApiHandler OnProblem(HttpMethod method, string path, HttpStatusCode status, string type, string detail) =>
        On(method, path, _ => Problem(status, type, detail));

    /// <summary>
    /// The default answer to <c>GET /api/agents/me</c>: the token decides who is calling (see <see cref="AdminTestPrincipal.AccessToken"/>).
    /// The agent and the admin are 200 with their role; the outsider is 403 agent-access-required; no token is 401.
    /// </summary>
    public StubApiHandler WithTestAgents() => On(HttpMethod.Get, "/api/agents/me", request =>
    {
        var principal = AdminTestPrincipal.All.FirstOrDefault(p => request.Authorization == "Bearer " + p.AccessToken);
        if (principal is null)
        {
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);
        }

        if (principal == AdminTestPrincipal.Outsider)
        {
            return Problem(HttpStatusCode.Forbidden, "agent-access-required", "Your account is not in an agent group.");
        }

        var role = principal == AdminTestPrincipal.Admin ? AgentRoles.Admin : AgentRoles.Agent;
        return JsonResponse(HttpStatusCode.OK, new AgentDto(Guid.NewGuid(), principal.DisplayName, principal.Email, role, true, null, null));
    });

    public static HttpResponseMessage JsonResponse<T>(HttpStatusCode status, T body) => new(status) { Content = JsonContent.Create(body, options: Json) };

    public static HttpResponseMessage Problem(HttpStatusCode status, string type, string detail) => new(status)
    {
        Content = new StringContent(
            JsonSerializer.Serialize(new { type, title = status.ToString(), status = (int)status, detail }, Json),
            Encoding.UTF8,
            "application/problem+json"),
    };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var seen = new StubApiRequest(
            request.Method,
            request.RequestUri!.AbsolutePath,
            request.RequestUri.Query,
            request.Headers.Authorization?.ToString(),
            request.Content?.Headers.ContentType?.ToString(),
            body);

        Func<StubApiRequest, HttpResponseMessage>? respond;
        lock (_gate)
        {
            _requests.Add(seen);
            respond = _routes.FirstOrDefault(r => r.Method == seen.Method && string.Equals(r.Path, seen.Path, StringComparison.OrdinalIgnoreCase)).Respond;
        }

        return respond is null ? Problem(HttpStatusCode.NotFound, "stub-not-configured", $"{seen.Method} {seen.Path} is not configured in the stub API.") : respond(seen);
    }
}
