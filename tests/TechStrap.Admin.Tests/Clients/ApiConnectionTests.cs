using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using SyntaxCircus.Blazor.Auth;
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Contracts.Tickets;
using TechStrap.Tests.Shared.AdminHost;

namespace TechStrap.Admin.Tests.Clients;

public sealed class ApiConnectionTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static TicketStateDto State() => new(Guid.NewGuid(), "ORB-1", "Open", "Normal", Guid.NewGuid(), null, false, [], DateTimeOffset.UtcNow, 7);

    [Fact]
    public async Task A_get_is_retried_on_503_and_then_succeeds()
    {
        await using var api = await ApiHarness.CreateAsync();
        var calls = 0;
        api.Stub.On(HttpMethod.Get, "/api/thing", _ => ++calls == 1 ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : StubApiHandler.JsonResponse(HttpStatusCode.OK, State()));

        var result = await api.Get<ApiConnection>().GetAsync<TicketStateDto>("api/thing", Ct);

        result.IsSuccess.ShouldBeTrue();
        api.Stub.Count(HttpMethod.Get, "/api/thing").ShouldBe(2);
    }

    // Review Focus 5: a duplicate reply or note must never come from a retry.
    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task A_post_is_never_retried_on_503(string verb)
    {
        await using var api = await ApiHarness.CreateAsync();
        var method = new HttpMethod(verb);
        api.Stub.OnStatus(method, "/api/thing", HttpStatusCode.ServiceUnavailable);

        var result = await api.Get<ApiConnection>().SendAsync<TicketStateDto>(method, "api/thing", new { note = "x" }, Ct);

        result.IsFailure.ShouldBeTrue();
        result.Errors[0].Code.ShouldBe(ApiErrorCodes.ApiUnavailable);
        api.Stub.Count(method, "/api/thing").ShouldBe(1);
    }

    [Fact]
    public async Task A_transport_failure_on_a_write_is_one_attempt_and_a_result_not_an_exception()
    {
        await using var api = await ApiHarness.CreateAsync();
        api.Stub.On(HttpMethod.Post, "/api/thing", _ => throw new HttpRequestException("connection refused"));

        var result = await api.Get<ApiConnection>().SendAsync(HttpMethod.Post, "api/thing", new { }, Ct);

        result.Errors[0].Code.ShouldBe(ApiErrorCodes.ApiUnavailable);
        api.Stub.Count(HttpMethod.Post, "/api/thing").ShouldBe(1);
    }

    // Carried ruling: the gate shows the message raw, so it is a fixed string and never carries exception text, a host or a port.
    [Fact]
    public async Task A_transport_failure_message_never_carries_exception_text_a_host_or_a_port()
    {
        await using var api = await ApiHarness.CreateAsync();
        api.Stub.On(HttpMethod.Post, "/api/thing", _ => throw new HttpRequestException("No connection could be made because the target machine actively refused it (10.1.2.3:5432)"));
        api.Stub.On(HttpMethod.Put, "/api/thing", _ => throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 30 seconds elapsing. internal-api.corp:8443", new TimeoutException()));

        var refused = await api.Get<ApiConnection>().SendAsync(HttpMethod.Post, "api/thing", new { }, Ct);
        var timedOut = await api.Get<ApiConnection>().SendAsync(HttpMethod.Put, "api/thing", new { }, Ct);

        foreach (var message in new[] { refused.Errors[0].Message, timedOut.Errors[0].Message })
        {
            message.ShouldNotBeNullOrWhiteSpace();
            message.ShouldNotContain("10.1.2.3");
            message.ShouldNotContain("5432");
            message.ShouldNotContain("8443");
            message.ShouldNotContain("internal-api");
            message.ShouldNotContain("HttpClient");
            message.ShouldNotContain("refused");
        }
    }

    [Fact]
    public async Task A_timeout_the_caller_did_not_ask_for_is_a_failure()
    {
        await using var api = await ApiHarness.CreateAsync();
        api.Stub.On(HttpMethod.Post, "/api/thing", _ => throw new TaskCanceledException("timed out", new TimeoutException()));

        var result = await api.Get<ApiConnection>().SendAsync(HttpMethod.Post, "api/thing", new { }, Ct);

        result.Errors[0].Code.ShouldBe(ApiErrorCodes.ApiTimeout);
    }

    [Fact]
    public async Task Cancellation_by_the_caller_propagates_and_is_never_a_result()
    {
        await using var api = await ApiHarness.CreateAsync();
        api.Stub.OnJson(HttpMethod.Get, "/api/thing", State());
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => api.Get<ApiConnection>().GetAsync<TicketStateDto>("api/thing", cancelled.Token));
        await Should.ThrowAsync<OperationCanceledException>(() => api.Get<ApiConnection>().SendAsync(HttpMethod.Put, "api/thing", new { }, cancelled.Token));
    }

    [Fact]
    public async Task A_validation_failure_yields_the_codes_from_errorCodes_with_their_fields()
    {
        await using var api = await ApiHarness.CreateAsync();
        api.Stub.On(HttpMethod.Put, "/api/thing", _ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(
                """{"type":"validation-failed","title":"One or more validation errors occurred.","status":400,"detail":"A row version is required.","errors":{"rowVersion":["A row version is required."],"":["Pick a status."]},"errorCodes":{"rowVersion":["row-version-required"],"":["status-invalid"]}}""",
                System.Text.Encoding.UTF8,
                "application/problem+json"),
        });

        var result = await api.Get<ApiConnection>().SendAsync<TicketStateDto>(HttpMethod.Put, "api/thing", new { }, Ct);

        result.Errors.Count.ShouldBe(2);
        result.Errors.ShouldAllBe(e => e.Kind == ResultErrorKind.Validation);
        var rowVersion = result.Errors.Single(e => e.Code == ApiErrorCodes.RowVersionRequired);
        rowVersion.Target.ShouldBe("rowVersion");
        rowVersion.Message.ShouldBe("A row version is required.");
        var general = result.Errors.Single(e => e.Code == "status-invalid");
        general.Target.ShouldBeNull();
        general.Message.ShouldBe("Pick a status.");
    }

    [Theory]
    [InlineData(409, "concurrency-conflict", ResultErrorKind.Conflict)]
    [InlineData(409, "ticket-closed", ResultErrorKind.Conflict)]
    [InlineData(409, "invalid-status-transition", ResultErrorKind.Conflict)]
    [InlineData(404, "ticket-not-found", ResultErrorKind.NotFound)]
    [InlineData(403, "agent-inactive", ResultErrorKind.Forbidden)]
    [InlineData(401, "unauthenticated", ResultErrorKind.Unauthenticated)]
    [InlineData(500, "unexpected", ResultErrorKind.Failure)]
    public async Task A_problem_keeps_the_api_code_and_maps_the_status_to_a_kind(int status, string type, ResultErrorKind kind)
    {
        await using var api = await ApiHarness.CreateAsync();
        api.Stub.OnProblem(HttpMethod.Delete, "/api/thing", (HttpStatusCode)status, type, "Something specific the agent should read.");

        var result = await api.Get<ApiConnection>().SendAsync(HttpMethod.Delete, "api/thing", null, Ct);

        result.Errors.ShouldHaveSingleItem().ShouldBe(new ResultError(type, "Something specific the agent should read.", kind));
    }

    [Theory]
    [InlineData(401, ApiErrorCodes.Unauthenticated, ResultErrorKind.Unauthenticated)]
    [InlineData(403, ApiErrorCodes.Forbidden, ResultErrorKind.Forbidden)]
    [InlineData(404, ApiErrorCodes.NotFound, ResultErrorKind.NotFound)]
    public async Task An_answer_without_a_body_still_gets_a_code_and_a_message(int status, string code, ResultErrorKind kind)
    {
        await using var api = await ApiHarness.CreateAsync();
        api.Stub.OnStatus(HttpMethod.Post, "/api/thing", (HttpStatusCode)status);

        var result = await api.Get<ApiConnection>().SendAsync(HttpMethod.Post, "api/thing", new { }, Ct);

        var error = result.Errors.ShouldHaveSingleItem();
        error.Code.ShouldBe(code);
        error.Kind.ShouldBe(kind);
        error.Message.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task A_success_with_a_body_that_is_not_json_is_an_unexpected_response()
    {
        await using var api = await ApiHarness.CreateAsync();
        api.Stub.On(HttpMethod.Get, "/api/thing", _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>proxy error</html>") });

        var result = await api.Get<ApiConnection>().GetAsync<TicketStateDto>("api/thing", Ct);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe(ApiErrorCodes.UnexpectedResponse);
    }

    // Review Focus 2: no request leaves a circuit without its token, and the token is added by the handler pipeline, not by the caller.
    [Fact]
    public async Task Reads_and_writes_both_carry_the_bearer_token_of_the_signed_in_agent()
    {
        await using var api = await ApiHarness.CreateAsync(AdminTestPrincipal.Admin);
        api.Stub.OnJson(HttpMethod.Get, "/api/thing", State()).OnJson(HttpMethod.Post, "/api/thing", State());
        var connection = api.Get<ApiConnection>();

        (await connection.GetAsync<TicketStateDto>("api/thing", Ct)).IsSuccess.ShouldBeTrue();
        (await connection.SendAsync<TicketStateDto>(HttpMethod.Post, "api/thing", new { }, Ct)).IsSuccess.ShouldBeTrue();

        api.Stub.Requests.Select(r => r.Authorization).ShouldBe(["Bearer " + AdminTestPrincipal.Admin.AccessToken, "Bearer " + AdminTestPrincipal.Admin.AccessToken]);
    }

    [Fact]
    public async Task Both_named_clients_run_the_auth_handler_and_the_forwarded_ip_handler_and_only_the_read_client_retries()
    {
        await using var api = await ApiHarness.CreateAsync();
        var factory = api.Get<IHttpMessageHandlerFactory>();

        string[] Chain(string name)
        {
            var names = new List<string>();
            for (var handler = factory.CreateHandler(name) as DelegatingHandler; handler is not null; handler = handler.InnerHandler as DelegatingHandler)
            {
                names.Add(handler.GetType().Name);
            }

            return [.. names];
        }

        var read = Chain(ApiClientNames.Read);
        var write = Chain(ApiClientNames.Write);

        foreach (var chain in new[] { read, write })
        {
            chain.ShouldContain(nameof(ApiAuthHandler));
            chain.ShouldContain("ForwardedClientIpHandler");
        }

        read.ShouldContain(name => name.Contains("Resilience", StringComparison.OrdinalIgnoreCase));
        write.ShouldNotContain(name => name.Contains("Resilience", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task The_clients_use_the_api_base_address_and_timeout_from_the_options()
    {
        await using var api = await ApiHarness.CreateAsync();
        api.Stub.OnJson(HttpMethod.Get, "/api/thing", State());

        await api.Get<ApiConnection>().GetAsync<TicketStateDto>("api/thing", Ct);

        api.Get<IHttpClientFactory>().CreateClient(ApiClientNames.Read).BaseAddress.ShouldBe(new Uri("http://api.test/"));
        api.Get<IHttpClientFactory>().CreateClient(ApiClientNames.Write).Timeout.ShouldBe(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void A_query_string_skips_empty_values_and_escapes_the_rest()
    {
        var id = Guid.Parse("11111111-2222-3333-4444-555555555555");

        ApiUri.Build("api/tickets", ("view", "Unassigned"), ("search", "a b&c"), ("status", null), ("tag", ""), ("tagId", id), ("page", 2))
            .ShouldBe("api/tickets?view=Unassigned&search=a%20b%26c&tagId=11111111-2222-3333-4444-555555555555&page=2");
        ApiUri.Build("api/tags").ShouldBe("api/tags");
    }
}
