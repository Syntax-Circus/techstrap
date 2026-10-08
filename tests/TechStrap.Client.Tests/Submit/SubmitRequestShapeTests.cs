using System.Text.Json;
using TechStrap.Client.Tests.Infrastructure;
using TechStrap.Contracts.Http;
using TechStrap.Contracts.Intake;

namespace TechStrap.Client.Tests.Submit;

public sealed class SubmitRequestShapeTests
{
    [Fact(Timeout = 10_000)]
    public async Task The_request_is_a_POST_to_the_intake_route()
    {
        using var fixture = new ClientFixture();
        fixture.Stub.Created();

        await fixture.Client.SubmitTicketAsync(ClientFixture.Request(), Xunit.TestContext.Current.CancellationToken);

        // Seeing the request here also proves the stub replaced the default SocketsHttpHandler as the primary handler.
        var sent = fixture.Stub.Requests.ShouldHaveSingleItem();
        sent.Method.ShouldBe(HttpMethod.Post);
        sent.Uri.ShouldBe(new Uri(ClientFixture.BaseAddress, IntakeRoutes.Tickets));
    }

    [Theory(Timeout = 10_000)]
    [InlineData("https://h/support")]
    [InlineData("https://h/support/")]
    public async Task A_base_address_path_is_kept_with_or_without_a_trailing_slash(string baseAddress)
    {
        using var fixture = new ClientFixture(options => options.BaseAddress = new Uri(baseAddress));
        fixture.Stub.Created();

        await fixture.Client.SubmitTicketAsync(ClientFixture.Request(), Xunit.TestContext.Current.CancellationToken);

        fixture.Stub.Requests.ShouldHaveSingleItem().Uri.ShouldBe(new Uri("https://h/support/" + IntakeRoutes.Tickets));
    }

    [Fact(Timeout = 10_000)]
    public async Task Submitting_with_only_a_cancellation_token_compiles_and_generates_a_key()
    {
        using var fixture = new ClientFixture();
        fixture.Stub.Created();
        var ct = Xunit.TestContext.Current.CancellationToken;

        await fixture.Client.SubmitTicketAsync(ClientFixture.Request(), ct);

        fixture.Stub.Requests.ShouldHaveSingleItem().Header(HeaderNames.IdempotencyKey).ShouldNotBeNullOrWhiteSpace();
    }

    [Fact(Timeout = 10_000)]
    public async Task The_api_key_is_a_header_and_the_body_is_web_json_that_round_trips()
    {
        using var fixture = new ClientFixture();
        fixture.Stub.Created();
        var request = new SubmitTicketRequest("ada@example.com", "Ada", "Subject", "Body", "user-7", new Dictionary<string, string> { ["plan"] = "pro" });

        await fixture.Client.SubmitTicketAsync(request, "abc123", Xunit.TestContext.Current.CancellationToken);

        var sent = fixture.Stub.Requests.ShouldHaveSingleItem();
        sent.Header(HeaderNames.ApiKey).ShouldBe(ClientFixture.ApiKey);
        sent.Content!.Headers.ContentType!.MediaType.ShouldBe("application/json");
        sent.Body!.ShouldContain("\"email\"", Case.Sensitive);
        var round = JsonSerializer.Deserialize<SubmitTicketRequest>(sent.Body!, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        round.ShouldNotBeNull();
        round.Email.ShouldBe("ada@example.com");
        round.ExternalUserRef.ShouldBe("user-7");
        round.Metadata!["plan"].ShouldBe("pro");
    }

    [Fact(Timeout = 10_000)]
    public async Task The_idempotency_key_is_only_in_the_header()
    {
        using var fixture = new ClientFixture();
        fixture.Stub.Created();

        await fixture.Client.SubmitTicketAsync(ClientFixture.Request(), "idem-key-1", Xunit.TestContext.Current.CancellationToken);

        var sent = fixture.Stub.Requests.ShouldHaveSingleItem();
        sent.Header(HeaderNames.IdempotencyKey).ShouldBe("idem-key-1");
        sent.Body!.ShouldNotContain("idem-key-1");
        sent.Body!.ShouldNotContain(ClientFixture.ApiKey);
        sent.Uri!.ToString().ShouldNotContain("idem-key-1");
        sent.Uri.ToString().ShouldNotContain(ClientFixture.ApiKey);
    }
}
