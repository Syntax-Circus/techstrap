using System.Net;
using System.Text.Json;
using SyntaxCircus.Common;
using TechStrap.Contracts.Tickets;
using TechStrap.Portal.Clients;
using TechStrap.Portal.Tests.Api;

namespace TechStrap.Portal.Tests.Clients;

/// <summary>
/// P09-T02, the customer's calls. The token is the <c>X-Ticket-Token</c> header of each request and nowhere else (Review Focus 1). A view is a read (retried); a reply is a write (never retried, multipart);
/// the lost-link request carries no token and no product (the API route takes none). Every call forwards the visitor's address (Review Focus 5).
/// </summary>
public sealed class CustomerTicketClientTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static readonly string Text = "Zk9_-" + new string('q', TicketToken.Length - 5);
    private const string TicketPath = "/api/customer/ticket";
    private const string ReplyPath = "/api/customer/ticket/replies";
    private const string LinkPath = "/api/customer/access-link";

    private static TicketToken Token()
    {
        TicketToken.TryParse(Text, out var token).ShouldBeTrue();
        return token;
    }

    private static CustomerTicketDto Ticket() => new(
        "PAP-42", "paperplane", "Printer jam", "Open", new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero),
        [new CustomerMessageDto(Guid.Parse("11111111-2222-3333-4444-555555555555"), "Agent", "Sam from Paperplane Support", "<p>Hello</p>", new DateTimeOffset(2026, 10, 1, 10, 0, 0, TimeSpan.Zero), [])]);

    [Fact]
    public async Task A_view_sends_the_token_in_the_header_through_the_read_client_and_returns_the_ticket_with_its_product_key()
    {
        using var api = ApiHarness.Create();
        api.Stub.OnJson(HttpMethod.Get, TicketPath, Ticket());

        var result = await api.Get<ICustomerTicketClient>().GetAsync(Token(), Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ProductKey.ShouldBe("paperplane");
        result.Value.Messages.ShouldHaveSingleItem().AuthorDisplayName.ShouldBe("Sam from Paperplane Support");
        var sent = api.Stub.Requests.ShouldHaveSingleItem();
        sent.TicketToken.ShouldBe(Text);
        sent.Client.ShouldBe(ApiClientNames.Read);
        sent.Query.ShouldBeEmpty();
        sent.Path.ShouldNotContain(Text);
        api.Stub.AssertEveryCallBore(ApiHarness.DefaultClientIp);
    }

    [Fact]
    public async Task A_view_that_the_api_refuses_is_the_uniform_not_found_whatever_the_reason()
    {
        using var api = ApiHarness.Create();
        api.Stub.OnProblem(HttpMethod.Get, TicketPath, HttpStatusCode.NotFound, "token-expired", "This token expired yesterday.");

        var result = await api.Get<ICustomerTicketClient>().GetAsync(Token(), Ct);

        result.Errors.ShouldHaveSingleItem().ShouldBe(new ResultError(ApiErrorCodes.NotFound, ProblemCopy.NotFound, ResultErrorKind.NotFound));
    }

    [Fact]
    public async Task A_view_is_retried_on_a_503_and_every_attempt_carries_the_token()
    {
        using var api = ApiHarness.Create();
        api.Stub.OnStatus(HttpMethod.Get, TicketPath, HttpStatusCode.ServiceUnavailable);

        var result = await api.Get<ICustomerTicketClient>().GetAsync(Token(), Ct);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe(ApiErrorCodes.ApiUnavailable);
        api.Stub.Count(HttpMethod.Get, TicketPath).ShouldBe(1 + ApiClientRegistration.ReadRetryCount);
        api.Stub.Requests.ShouldAllBe(r => r.TicketToken == Text);
    }

    [Fact]
    public async Task A_reply_is_a_multipart_post_through_the_write_client_with_the_token_header_only()
    {
        using var api = ApiHarness.Create();
        api.Stub.OnJson(HttpMethod.Post, ReplyPath, new CustomerReplyResponse("PAP-42", Guid.NewGuid(), false, null), HttpStatusCode.Created);
        var file = new AttachmentUpload("shot.png", "image/png", () => new MemoryStream([1, 2, 3]));

        var result = await api.Get<ICustomerTicketClient>().ReplyAsync(Token(), new CustomerReply("Still broken.", [file]), Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.FollowUpCreated.ShouldBeFalse();
        var sent = api.Stub.Requests.ShouldHaveSingleItem();
        sent.Method.ShouldBe(HttpMethod.Post);
        sent.Client.ShouldBe(ApiClientNames.Write);
        sent.TicketToken.ShouldBe(Text);
        sent.ContentType.ShouldNotBeNull().ShouldStartWith("multipart/form-data");
        var body = sent.Body.ShouldNotBeNull();
        body.ShouldContain("name=Body");
        body.ShouldContain("Still broken.");
        body.ShouldContain("name=Attachments; filename=shot.png");
        body.ShouldNotContain(Text, Case.Sensitive, "the token is a header, never a form field");
        sent.Path.ShouldNotContain(Text);
        sent.Query.ShouldBeEmpty();
        api.Stub.AssertEveryCallBore(ApiHarness.DefaultClientIp);
    }

    [Fact]
    public async Task A_reply_on_a_closed_ticket_returns_the_follow_up_link_untouched()
    {
        using var api = ApiHarness.Create();
        const string Link = "https://help.example.com/t/AbC-_0123456789AbC-_0123456789AbC-_01234567";
        api.Stub.OnJson(HttpMethod.Post, ReplyPath, new CustomerReplyResponse("PAP-43", Guid.NewGuid(), true, Link), HttpStatusCode.Created);

        var result = await api.Get<ICustomerTicketClient>().ReplyAsync(Token(), new CustomerReply("Back again.", []), Ct);

        result.Value.FollowUpCreated.ShouldBeTrue();
        result.Value.FollowUpViewUrl.ShouldBe(Link);
    }

    [Fact]
    public async Task A_409_is_the_reply_conflict_error_with_its_own_copy()
    {
        using var api = ApiHarness.Create();
        api.Stub.OnProblem(HttpMethod.Post, ReplyPath, HttpStatusCode.Conflict, "reply-conflict", "Your reply could not be saved. Please try again.");

        var result = await api.Get<ICustomerTicketClient>().ReplyAsync(Token(), new CustomerReply("x", []), Ct);

        result.Errors.ShouldHaveSingleItem().ShouldBe(new ResultError(ApiErrorCodes.ReplyConflict, ProblemCopy.ReplyConflict, ResultErrorKind.Conflict));
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestEntityTooLarge, ApiErrorCodes.PayloadTooLarge)]
    [InlineData(HttpStatusCode.UnsupportedMediaType, ApiErrorCodes.UnsupportedMediaType)]
    [InlineData(HttpStatusCode.NotFound, ApiErrorCodes.NotFound)]
    [InlineData(HttpStatusCode.TooManyRequests, ApiErrorCodes.RateLimited)]
    public async Task A_reply_failure_is_mapped_by_the_status_alone(HttpStatusCode status, string code)
    {
        using var api = ApiHarness.Create();
        api.Stub.OnProblem(HttpMethod.Post, ReplyPath, status, "x", "detail");

        var result = await api.Get<ICustomerTicketClient>().ReplyAsync(Token(), new CustomerReply("x", []), Ct);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe(code);
    }

    [Fact]
    public async Task A_reply_is_never_retried()
    {
        using var api = ApiHarness.Create();
        api.Stub.OnStatus(HttpMethod.Post, ReplyPath, HttpStatusCode.ServiceUnavailable);

        var result = await api.Get<ICustomerTicketClient>().ReplyAsync(Token(), new CustomerReply("x", []), Ct);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe(ApiErrorCodes.ApiUnavailable);
        api.Stub.Count(HttpMethod.Post, ReplyPath).ShouldBe(1);
    }

    [Fact]
    public async Task A_lost_link_request_is_a_json_post_through_the_write_client_with_no_token_and_no_product()
    {
        using var api = ApiHarness.Create();
        api.Stub.OnStatus(HttpMethod.Post, LinkPath, HttpStatusCode.Accepted);

        var result = await api.Get<ICustomerTicketClient>().RequestAccessLinkAsync("ada@example.com", Ct);

        result.IsSuccess.ShouldBeTrue();
        var sent = api.Stub.Requests.ShouldHaveSingleItem();
        sent.Client.ShouldBe(ApiClientNames.Write);
        sent.TicketToken.ShouldBeNull();
        sent.ContentType.ShouldNotBeNull().ShouldStartWith("application/json");
        JsonDocument.Parse(sent.Body!).RootElement.GetProperty("email").GetString().ShouldBe("ada@example.com");
        api.Stub.AssertEveryCallBore(ApiHarness.DefaultClientIp);
    }

    [Fact]
    public async Task A_malformed_address_is_a_field_error_with_the_apis_code_and_a_429_is_rate_limited()
    {
        using var api = ApiHarness.Create();
        api.Stub.On(HttpMethod.Post, LinkPath, _ => StubApiHandler.ValidationProblem("email", "email-invalid", "Enter a valid email address."));

        var invalid = await api.Get<ICustomerTicketClient>().RequestAccessLinkAsync("not an address", Ct);
        api.Stub.OnProblem(HttpMethod.Post, LinkPath, HttpStatusCode.TooManyRequests, "rate-limited", "slow down");
        var limited = await api.Get<ICustomerTicketClient>().RequestAccessLinkAsync("ada@example.com", Ct);

        invalid.Errors.ShouldHaveSingleItem().ShouldBe(new ResultError("email-invalid", "Enter a valid email address.", ResultErrorKind.Validation, "email"));
        limited.Errors.ShouldHaveSingleItem().Code.ShouldBe(ApiErrorCodes.RateLimited);
    }

    [Fact]
    public async Task An_attachment_is_opened_through_the_read_client_with_the_token_header_and_the_id_in_the_api_path()
    {
        using var api = ApiHarness.Create();
        var id = Guid.Parse("11111111-2222-3333-4444-555555555555");
        api.Stub.OnFile(HttpMethod.Get, $"/api/customer/attachments/{id}", "file text"u8.ToArray(), "text/plain", "log.txt");

        var result = await api.Get<ICustomerTicketClient>().OpenAttachmentAsync(Token(), id, Ct);

        result.IsSuccess.ShouldBeTrue();
        await using var download = result.Value;
        using var reader = new StreamReader(download.Body);
        (await reader.ReadToEndAsync(Ct)).ShouldBe("file text");
        download.FileName.ShouldBe("log.txt");
        var sent = api.Stub.Requests.ShouldHaveSingleItem();
        sent.TicketToken.ShouldBe(Text);
        sent.Client.ShouldBe(ApiClientNames.Read);
        sent.Query.ShouldBeEmpty();
        sent.Path.ShouldNotContain(Text);
        api.Stub.AssertEveryCallBore(ApiHarness.DefaultClientIp);
    }

    [Fact]
    public async Task An_attachment_the_api_refuses_is_the_uniform_not_found()
    {
        using var api = ApiHarness.Create();
        var id = Guid.NewGuid();
        api.Stub.OnProblem(HttpMethod.Get, $"/api/customer/attachments/{id}", HttpStatusCode.NotFound, "attachment-not-found", "No such attachment on this ticket.");

        var result = await api.Get<ICustomerTicketClient>().OpenAttachmentAsync(Token(), id, Ct);

        result.Errors.ShouldHaveSingleItem().ShouldBe(new ResultError(ApiErrorCodes.NotFound, ProblemCopy.NotFound, ResultErrorKind.NotFound));
    }

    [Fact]
    public async Task A_lost_link_request_is_not_retried()
    {
        using var api = ApiHarness.Create();
        api.Stub.OnStatus(HttpMethod.Post, LinkPath, HttpStatusCode.BadGateway);

        await api.Get<ICustomerTicketClient>().RequestAccessLinkAsync("ada@example.com", Ct);

        api.Stub.Count(HttpMethod.Post, LinkPath).ShouldBe(1);
    }
}
