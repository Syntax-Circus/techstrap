using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using SyntaxCircus.Common;
using TechStrap.Contracts.Products;
using TechStrap.Portal.Clients;
using TechStrap.Portal.Tests.Api;

namespace TechStrap.Portal.Tests.Clients;

public sealed class ApiConnectionTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static readonly string Token = new('t', TicketToken.Length);

    private static PublicProductDto Product() => new("paperplane", "Paperplane", null, "#F59E0B", "#000000", "#9D6507");

    private static TicketToken ValidToken()
    {
        TicketToken.TryParse(Token, out var token).ShouldBeTrue();
        return token;
    }

    [Fact]
    public async Task A_get_returns_the_json_body_on_success()
    {
        using var api = ApiHarness.Create();
        api.Stub.OnJson(HttpMethod.Get, "/api/thing", Product());

        var result = await api.Get<ApiConnection>().GetAsync<PublicProductDto>("api/thing", Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(Product());
    }

    [Fact]
    public async Task A_get_is_retried_on_503_and_then_succeeds()
    {
        using var api = ApiHarness.Create();
        var calls = 0;
        api.Stub.On(HttpMethod.Get, "/api/thing", _ => ++calls == 1 ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : StubApiHandler.JsonResponse(HttpStatusCode.OK, Product()));

        var result = await api.Get<ApiConnection>().GetAsync<PublicProductDto>("api/thing", Ct);

        result.IsSuccess.ShouldBeTrue();
        api.Stub.Count(HttpMethod.Get, "/api/thing").ShouldBe(2);
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task A_get_is_retried_twice_on_a_retryable_status_so_three_calls_in_all(HttpStatusCode status)
    {
        using var api = ApiHarness.Create();
        api.Stub.OnStatus(HttpMethod.Get, "/api/thing", status);

        var result = await api.Get<ApiConnection>().GetAsync<PublicProductDto>("api/thing", Ct);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe(status == HttpStatusCode.RequestTimeout ? ApiErrorCodes.ApiError : ApiErrorCodes.ApiUnavailable);
        api.Stub.Count(HttpMethod.Get, "/api/thing").ShouldBe(1 + ApiClientRegistration.ReadRetryCount);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task A_get_is_not_retried_on_any_other_status(HttpStatusCode status)
    {
        using var api = ApiHarness.Create();
        api.Stub.OnStatus(HttpMethod.Get, "/api/thing", status);

        await api.Get<ApiConnection>().GetAsync<PublicProductDto>("api/thing", Ct);

        api.Stub.Count(HttpMethod.Get, "/api/thing").ShouldBe(1);
    }

    [Fact]
    public async Task A_get_is_retried_on_a_transport_failure()
    {
        using var api = ApiHarness.Create();
        var calls = 0;
        api.Stub.On(HttpMethod.Get, "/api/thing", _ => ++calls == 1 ? throw new HttpRequestException("connection refused") : StubApiHandler.JsonResponse(HttpStatusCode.OK, Product()));

        var result = await api.Get<ApiConnection>().GetAsync<PublicProductDto>("api/thing", Ct);

        result.IsSuccess.ShouldBeTrue();
        api.Stub.Count(HttpMethod.Get, "/api/thing").ShouldBe(2);
    }

    // Review Focus 4: a duplicate ticket or reply must never come from a retry.
    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task A_write_is_never_retried_on_503(string verb)
    {
        using var api = ApiHarness.Create();
        var method = new HttpMethod(verb);
        api.Stub.OnStatus(method, "/api/thing", HttpStatusCode.ServiceUnavailable);

        var result = await api.Get<ApiConnection>().SendAsync<PublicProductDto>(method, "api/thing", new { note = "x" }, Ct);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe(ApiErrorCodes.ApiUnavailable);
        api.Stub.Count(method, "/api/thing").ShouldBe(1);
    }

    [Fact]
    public async Task A_write_with_a_prepared_body_is_never_retried_on_503_or_a_transport_failure()
    {
        using var api = ApiHarness.Create();
        api.Stub.OnStatus(HttpMethod.Post, "/api/thing", HttpStatusCode.BadGateway);
        api.Stub.On(HttpMethod.Put, "/api/other", _ => throw new HttpRequestException("connection reset"));

        var bad = await api.Get<ApiConnection>().SendContentAsync<PublicProductDto>(HttpMethod.Post, "api/thing", new StringContent("a"), Ct);
        var dropped = await api.Get<ApiConnection>().SendContentAsync<PublicProductDto>(HttpMethod.Put, "api/other", new StringContent("a"), Ct);

        bad.Errors.ShouldHaveSingleItem().Code.ShouldBe(ApiErrorCodes.ApiUnavailable);
        dropped.Errors.ShouldHaveSingleItem().Code.ShouldBe(ApiErrorCodes.ApiUnavailable);
        api.Stub.Count(HttpMethod.Post, "/api/thing").ShouldBe(1);
        api.Stub.Count(HttpMethod.Put, "/api/other").ShouldBe(1);
    }

    [Fact]
    public async Task A_write_without_a_body_in_the_answer_is_a_plain_result()
    {
        using var api = ApiHarness.Create();
        api.Stub.OnStatus(HttpMethod.Post, "/api/thing", HttpStatusCode.Accepted);

        var result = await api.Get<ApiConnection>().SendAsync(HttpMethod.Post, "api/thing", new { email = "a@b.example" }, Ct);

        result.IsSuccess.ShouldBeTrue();
        api.Stub.Requests.ShouldHaveSingleItem().Body!.ShouldContain("a@b.example");
    }

    // Carried ruling: the page shows the message as is, so it is a fixed string and never carries exception text, a host or a port.
    [Fact]
    public async Task A_transport_failure_message_never_carries_exception_text_a_host_or_a_port()
    {
        using var api = ApiHarness.Create();
        api.Stub.On(HttpMethod.Post, "/api/thing", _ => throw new HttpRequestException("No connection could be made because the target machine actively refused it (10.1.2.3:5432)"));
        api.Stub.On(HttpMethod.Put, "/api/thing", _ => throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 30 seconds elapsing. internal-api.corp:8443", new TimeoutException()));

        var refused = await api.Get<ApiConnection>().SendAsync(HttpMethod.Post, "api/thing", new { }, Ct);
        var timedOut = await api.Get<ApiConnection>().SendAsync(HttpMethod.Put, "api/thing", new { }, Ct);

        foreach (var error in new[] { refused.Errors.Single(), timedOut.Errors.Single() })
        {
            error.Code.ShouldBe(ApiErrorCodes.ApiUnavailable);
            error.Message.ShouldBe(ProblemCopy.ApiUnavailable);
        }
    }

    [Fact]
    public async Task Cancellation_by_the_caller_propagates_and_is_never_a_result()
    {
        using var api = ApiHarness.Create();
        api.Stub.OnJson(HttpMethod.Get, "/api/thing", Product());
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        // The read client's retry handler throws OperationCanceledException itself; the write client's HttpClient throws its subclass TaskCanceledException.
        (await Should.ThrowAsync<Exception>(() => api.Get<ApiConnection>().GetAsync<PublicProductDto>("api/thing", cancelled.Token))).ShouldBeAssignableTo<OperationCanceledException>();
        (await Should.ThrowAsync<Exception>(() => api.Get<ApiConnection>().SendAsync(HttpMethod.Put, "api/thing", new { }, cancelled.Token))).ShouldBeAssignableTo<OperationCanceledException>();
    }

    [Fact]
    public async Task A_success_with_a_body_that_is_not_json_is_an_unexpected_response()
    {
        using var api = ApiHarness.Create();
        api.Stub.On(HttpMethod.Get, "/api/thing", _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>proxy error</html>") });

        var result = await api.Get<ApiConnection>().GetAsync<PublicProductDto>("api/thing", Ct);

        var error = result.Errors.ShouldHaveSingleItem();
        error.Code.ShouldBe(ApiErrorCodes.UnexpectedResponse);
        error.Message.ShouldBe(ProblemCopy.UnexpectedResponse);
    }

    [Fact]
    public async Task A_success_with_a_json_null_is_an_unexpected_response()
    {
        using var api = ApiHarness.Create();
        api.Stub.On(HttpMethod.Get, "/api/thing", _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("null", System.Text.Encoding.UTF8, "application/json") });

        var result = await api.Get<ApiConnection>().GetAsync<PublicProductDto>("api/thing", Ct);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe(ApiErrorCodes.UnexpectedResponse);
    }

    [Fact]
    public async Task A_400_through_the_pipeline_keeps_every_field_code_target_and_message_and_is_not_retried()
    {
        using var api = ApiHarness.Create();
        api.Stub.On(HttpMethod.Post, "/api/thing", _ => StubApiHandler.ValidationProblem(
        [
            ("email", "email-invalid", "That email address does not look right."),
            ("subject", "subject-required", "Add a subject."),
        ]));

        var result = await api.Get<ApiConnection>().SendContentAsync<PublicProductDto>(HttpMethod.Post, "api/thing", new StringContent("a"), Ct);

        result.Errors.Count.ShouldBe(2);
        result.Errors.ShouldAllBe(e => e.Kind == ResultErrorKind.Validation);
        result.Errors.Single(e => e.Target == "email").Code.ShouldBe("email-invalid");
        result.Errors.Single(e => e.Target == "subject").Message.ShouldBe("Add a subject.");
        api.Stub.Count(HttpMethod.Post, "/api/thing").ShouldBe(1);
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestEntityTooLarge, ApiErrorCodes.PayloadTooLarge)]
    [InlineData(HttpStatusCode.UnsupportedMediaType, ApiErrorCodes.UnsupportedMediaType)]
    [InlineData(HttpStatusCode.TooManyRequests, ApiErrorCodes.RateLimited)]
    public async Task A_413_a_415_and_a_429_on_a_write_each_get_their_own_code_and_are_not_retried(HttpStatusCode status, string code)
    {
        using var api = ApiHarness.Create();
        api.Stub.OnStatus(HttpMethod.Post, "/api/thing", status);

        var result = await api.Get<ApiConnection>().SendAsync<PublicProductDto>(HttpMethod.Post, "api/thing", new { }, Ct);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe(code);
        api.Stub.Count(HttpMethod.Post, "/api/thing").ShouldBe(1);
    }

    // Review Focus 4: the visitor's address, not the Portal's container, reaches the API on every call, read or write.
    [Fact]
    public async Task Reads_and_writes_both_forward_the_visitors_address()
    {
        using var api = ApiHarness.Create("198.51.100.77");
        api.Stub.OnJson(HttpMethod.Get, "/api/thing", Product()).OnJson(HttpMethod.Post, "/api/thing", Product());
        var connection = api.Get<ApiConnection>();

        (await connection.GetAsync<PublicProductDto>("api/thing", Ct)).IsSuccess.ShouldBeTrue();
        (await connection.SendAsync<PublicProductDto>(HttpMethod.Post, "api/thing", new { }, Ct)).IsSuccess.ShouldBeTrue();

        api.Stub.Requests.Select(r => r.ForwardedFor).ShouldBe(["198.51.100.77", "198.51.100.77"]);
        api.Stub.AssertEveryCallBore("198.51.100.77");
    }

    [Fact]
    public async Task An_ipv4_mapped_ipv6_address_is_forwarded_as_ipv4()
    {
        using var api = ApiHarness.Create("::ffff:203.0.113.5");
        api.Stub.OnJson(HttpMethod.Get, "/api/thing", Product());

        await api.Get<ApiConnection>().GetAsync<PublicProductDto>("api/thing", Ct);

        api.Stub.Requests.ShouldHaveSingleItem().ForwardedFor.ShouldBe("203.0.113.5");
    }

    [Fact]
    public async Task The_assertion_helper_fails_when_a_call_lacks_the_address_or_when_there_was_no_call()
    {
        using var api = ApiHarness.Create(clientIp: null);
        api.Stub.OnJson(HttpMethod.Get, "/api/thing", Product());

        Should.Throw<InvalidOperationException>(() => api.Stub.AssertEveryCallBore("203.0.113.9")).Message.ShouldContain("made no API call");

        await api.Get<ApiConnection>().GetAsync<PublicProductDto>("api/thing", Ct);

        Should.Throw<InvalidOperationException>(() => api.Stub.AssertEveryCallBore("203.0.113.9")).Message.ShouldContain("expected the visitor's address");
    }

    [Fact]
    public void Both_named_clients_forward_the_visitors_address_and_only_the_read_client_retries()
    {
        using var api = ApiHarness.Create();
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

        read.ShouldContain("ForwardedClientIpHandler");
        write.ShouldContain("ForwardedClientIpHandler");

        // The default HttpClient logging writes every request header at Trace, and a customer call carries X-Ticket-Token.
        read.ShouldNotContain(name => name.Contains("Logging", StringComparison.Ordinal));
        write.ShouldNotContain(name => name.Contains("Logging", StringComparison.Ordinal));

        // An allowlist of handler types: the write chain is exactly the forwarded-IP handler, the read chain adds only the retry handler.
        write.ShouldNotContain("ResilienceHandler");
        read.Except(write).ShouldBe(["ResilienceHandler"]);
    }

    [Fact]
    public void The_clients_use_the_api_base_address_and_the_read_and_write_timeouts()
    {
        using var api = ApiHarness.Create();
        var factory = api.Get<IHttpClientFactory>();

        factory.CreateClient(ApiClientNames.Read).BaseAddress.ShouldBe(new Uri("http://api.test/"));
        factory.CreateClient(ApiClientNames.Write).BaseAddress.ShouldBe(new Uri("http://api.test/"));
        factory.CreateClient(ApiClientNames.Read).Timeout.ShouldBe(TimeSpan.FromSeconds(ApiClientRegistration.ReadTimeoutSeconds));
        factory.CreateClient(ApiClientNames.Write).Timeout.ShouldBe(TimeSpan.FromSeconds(ApiClientRegistration.WriteTimeoutSeconds));
    }

    // Review Focus 1: the ticket token is a header on the one request that needs it, on reads and writes, and nowhere else.
    [Fact]
    public async Task A_read_and_a_write_with_a_token_send_it_in_the_ticket_header_and_not_in_the_address()
    {
        using var api = ApiHarness.Create();
        api.Stub.OnJson(HttpMethod.Get, "/api/customer/ticket", Product()).OnJson(HttpMethod.Post, "/api/customer/ticket/replies", Product(), HttpStatusCode.Created);
        var connection = api.Get<ApiConnection>();

        (await connection.GetAsync<PublicProductDto>("api/customer/ticket", ValidToken(), Ct)).IsSuccess.ShouldBeTrue();
        (await connection.SendAsync<PublicProductDto>(HttpMethod.Post, "api/customer/ticket/replies", new { body = "hi" }, ValidToken(), Ct)).IsSuccess.ShouldBeTrue();
        (await connection.SendContentAsync<PublicProductDto>(HttpMethod.Post, "api/customer/ticket/replies", new StringContent("hi"), ValidToken(), Ct)).IsSuccess.ShouldBeTrue();

        api.Stub.Requests.Count.ShouldBe(3);
        api.Stub.Requests.ShouldAllBe(r => r.TicketToken == Token);
        api.Stub.Requests.ShouldAllBe(r => !r.Path.Contains(Token) && !r.Query.Contains(Token));
        api.Stub.Requests.Where(r => r.Body is not null).ShouldAllBe(r => !r.Body!.Contains(Token));
    }

    [Fact]
    public async Task A_call_without_a_token_sends_no_ticket_header()
    {
        using var api = ApiHarness.Create();
        api.Stub.OnJson(HttpMethod.Get, "/api/thing", Product()).OnJson(HttpMethod.Post, "/api/thing", Product());
        var connection = api.Get<ApiConnection>();

        await connection.GetAsync<PublicProductDto>("api/thing", Ct);
        await connection.SendAsync<PublicProductDto>(HttpMethod.Post, "api/thing", new { }, Ct);

        api.Stub.Requests.ShouldAllBe(r => r.TicketToken == null);
    }

    [Fact]
    public async Task A_token_is_never_carried_over_to_the_next_call_on_the_same_connection()
    {
        using var api = ApiHarness.Create();
        api.Stub.OnJson(HttpMethod.Get, "/api/thing", Product());
        var connection = api.Get<ApiConnection>();

        await connection.GetAsync<PublicProductDto>("api/thing", ValidToken(), Ct);
        await connection.GetAsync<PublicProductDto>("api/thing", Ct);

        api.Stub.Requests.Select(r => r.TicketToken).ShouldBe([Token, null]);
    }

    [Fact]
    public async Task A_retried_read_sends_the_token_on_every_attempt()
    {
        using var api = ApiHarness.Create();
        api.Stub.OnStatus(HttpMethod.Get, "/api/customer/ticket", HttpStatusCode.ServiceUnavailable);

        await api.Get<ApiConnection>().GetAsync<PublicProductDto>("api/customer/ticket", ValidToken(), Ct);

        api.Stub.Requests.Count.ShouldBe(1 + ApiClientRegistration.ReadRetryCount);
        api.Stub.Requests.ShouldAllBe(r => r.TicketToken == Token);
    }
}
