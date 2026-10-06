using System.Net;
using SyntaxCircus.Common;
using TechStrap.Contracts.Intake;
using TechStrap.Portal.Clients;
using TechStrap.Portal.Tests.Api;

namespace TechStrap.Portal.Tests.Clients;

/// <summary>
/// P09-T02, the contact form's call: a multipart POST to the product's public intake endpoint. It goes through the write client (a retry could duplicate a ticket), forwards the visitor's address, sends no
/// ticket token, passes the honeypot through to the API (D-045 addendum) and maps every answer to a Result.
/// </summary>
public sealed class PublicTicketClientTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private const string Path = "/api/public/products/paperplane/tickets";

    private static NewTicketRequest Request(string? website = null, params AttachmentUpload[] files) =>
        new("ada@example.com", "Ada Lovelace", "Printer jam", "It jams every time.", website, files);

    private static SubmitTicketResponse Created() => new("PAP-42", null, []);

    [Fact]
    public async Task A_submission_is_a_multipart_post_through_the_write_client_with_every_field_and_file()
    {
        using var api = ApiHarness.Create();
        api.Stub.OnJson(HttpMethod.Post, Path, Created(), HttpStatusCode.Created);
        var file = new AttachmentUpload("log.txt", "text/plain", () => new MemoryStream("the log"u8.ToArray()));

        var result = await api.Get<IPublicTicketClient>().SubmitAsync("paperplane", Request(null, file), Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.TicketNumber.ShouldBe("PAP-42");
        var sent = api.Stub.Requests.ShouldHaveSingleItem();
        sent.Method.ShouldBe(HttpMethod.Post);
        sent.Client.ShouldBe(ApiClientNames.Write);
        sent.ContentType.ShouldNotBeNull().ShouldStartWith("multipart/form-data");
        sent.TicketToken.ShouldBeNull("a visitor with no ticket sends no token");
        sent.Query.ShouldBeEmpty();
        var body = sent.Body.ShouldNotBeNull();
        body.ShouldContain("name=Email");
        body.ShouldContain("ada@example.com");
        body.ShouldContain("name=Name");
        body.ShouldContain("Ada Lovelace");
        body.ShouldContain("name=Subject");
        body.ShouldContain("Printer jam");
        body.ShouldContain("name=Body");
        body.ShouldContain("It jams every time.");
        body.ShouldContain("name=Attachments; filename=log.txt");
        body.ShouldContain("the log");
        body.ShouldNotContain("name=Website", Case.Sensitive, "no honeypot value, no field");
        api.Stub.AssertEveryCallBore(ApiHarness.DefaultClientIp);
    }

    [Fact]
    public async Task A_filled_honeypot_is_passed_through_to_the_api_and_the_answer_is_the_apis_own()
    {
        using var api = ApiHarness.Create();
        api.Stub.OnJson(HttpMethod.Post, Path, Created(), HttpStatusCode.Created);

        var result = await api.Get<IPublicTicketClient>().SubmitAsync("paperplane", Request("http://spam.example"), Ct);

        result.IsSuccess.ShouldBeTrue();
        var body = api.Stub.Requests.ShouldHaveSingleItem().Body.ShouldNotBeNull();
        body.ShouldContain("name=Website");
        body.ShouldContain("http://spam.example");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Paperplane")]
    [InlineData("../admin")]
    [InlineData("paperplane/tickets")]
    [InlineData("paperplane?x=1")]
    public async Task A_key_that_is_not_a_slug_is_not_found_and_no_call_is_made(string? key)
    {
        using var api = ApiHarness.Create();
        api.Stub.OnJson(HttpMethod.Post, Path, Created(), HttpStatusCode.Created);

        var result = await api.Get<IPublicTicketClient>().SubmitAsync(key!, Request(), Ct);

        result.Errors.ShouldHaveSingleItem().ShouldBe(new ResultError(ApiErrorCodes.NotFound, ProblemCopy.NotFound, ResultErrorKind.NotFound));
        api.Stub.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Field_errors_keep_the_apis_codes_and_targets()
    {
        using var api = ApiHarness.Create();
        api.Stub.On(HttpMethod.Post, Path, _ => StubApiHandler.ValidationProblem(
            [("email", "email-invalid", "Enter a valid email address."), ("attachments", "attachments-too-many", "Attach at most 5 files.")]));

        var result = await api.Get<IPublicTicketClient>().SubmitAsync("paperplane", Request(), Ct);

        result.Errors.Select(e => (e.Code, e.Target)).ShouldBe([("email-invalid", "email"), ("attachments-too-many", "attachments")]);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, ApiErrorCodes.NotFound)]
    [InlineData(HttpStatusCode.RequestEntityTooLarge, ApiErrorCodes.PayloadTooLarge)]
    [InlineData(HttpStatusCode.UnsupportedMediaType, ApiErrorCodes.UnsupportedMediaType)]
    [InlineData(HttpStatusCode.TooManyRequests, ApiErrorCodes.RateLimited)]
    [InlineData(HttpStatusCode.InternalServerError, ApiErrorCodes.ApiUnavailable)]
    public async Task Every_other_status_is_mapped_by_the_status_alone(HttpStatusCode status, string code)
    {
        using var api = ApiHarness.Create();
        api.Stub.OnProblem(HttpMethod.Post, Path, status, "x", "Npgsql host=10.0.0.5");

        var result = await api.Get<IPublicTicketClient>().SubmitAsync("paperplane", Request(), Ct);

        var error = result.Errors.ShouldHaveSingleItem();
        error.Code.ShouldBe(code);
        error.Message.ShouldNotContain("Npgsql");
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task A_submission_is_never_retried_whatever_the_failure(HttpStatusCode status)
    {
        using var api = ApiHarness.Create();
        api.Stub.OnStatus(HttpMethod.Post, Path, status);

        var result = await api.Get<IPublicTicketClient>().SubmitAsync("paperplane", Request(), Ct);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe(ApiErrorCodes.ApiUnavailable);
        api.Stub.Count(HttpMethod.Post, Path).ShouldBe(1, "a retry could create the ticket twice");
    }

    [Fact]
    public async Task A_transport_failure_is_one_call_and_api_unavailable()
    {
        using var api = ApiHarness.Create();
        api.Stub.On(HttpMethod.Post, Path, _ => throw new HttpRequestException("No connection could be made (10.1.2.3:5432)"));

        var result = await api.Get<IPublicTicketClient>().SubmitAsync("paperplane", Request(), Ct);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe(ApiErrorCodes.ApiUnavailable);
        api.Stub.Count(HttpMethod.Post, Path).ShouldBe(1);
    }

    [Fact]
    public async Task The_files_are_not_opened_for_a_key_that_is_refused_before_the_call()
    {
        using var api = ApiHarness.Create();
        var opened = false;
        var file = new AttachmentUpload("a.txt", "text/plain", () =>
        {
            opened = true;
            return new MemoryStream();
        });

        await api.Get<IPublicTicketClient>().SubmitAsync("Not A Slug", Request(null, file), Ct);

        opened.ShouldBeFalse();
    }
}
