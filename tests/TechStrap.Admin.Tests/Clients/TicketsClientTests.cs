using System.Net;
using System.Text.Json;
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Contracts.Paging;
using TechStrap.Contracts.Tickets;
using TechStrap.Tests.Shared.AdminHost;

namespace TechStrap.Admin.Tests.Clients;

public sealed class TicketsClientTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static readonly Guid TicketId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");

    private static TicketStateDto State(uint rowVersion = 8) => new(TicketId, "ORB-1", "Pending", "High", Guid.NewGuid(), null, false, [], DateTimeOffset.UtcNow, rowVersion);

    private static async Task<(ApiHarness Api, ITicketsClient Client)> StartAsync()
    {
        var api = await ApiHarness.CreateAsync();
        return (api, api.Get<ITicketsClient>());
    }

    private static JsonElement Body(ApiHarness api) => JsonDocument.Parse(api.Stub.Requests.Last().Body!).RootElement;

    [Fact]
    public async Task List_sends_every_filter_and_the_paging_as_the_query_string()
    {
        var (api, client) = await StartAsync();
        await using var _ = api;
        api.Stub.OnJson(HttpMethod.Get, "/api/tickets", new PagedResponse<TicketSummaryDto>([], 2, 25, 0));
        var productId = Guid.NewGuid();

        var result = await client.ListAsync(new ListTicketsRequest(TicketViews.Unassigned, productId, null, TicketPriorities.High, null, null, null, "login loop", 2, 25), Ct);

        result.IsSuccess.ShouldBeTrue();
        api.Stub.Requests.ShouldHaveSingleItem().Query.ShouldBe($"?view=Unassigned&productId={productId}&priority=High&search=login%20loop&page=2&pageSize=25");
    }

    [Fact]
    public async Task Get_accepts_an_id_or_a_number_and_a_404_keeps_the_ticket_not_found_code()
    {
        var (api, client) = await StartAsync();
        await using var _ = api;
        api.Stub.OnProblem(HttpMethod.Get, "/api/tickets/ORB-404", HttpStatusCode.NotFound, ApiErrorCodes.TicketNotFound, "No such ticket.");

        var result = await client.GetAsync("ORB-404", Ct);

        result.Errors.ShouldHaveSingleItem().ShouldBe(new ResultError(ApiErrorCodes.TicketNotFound, "No such ticket.", ResultErrorKind.NotFound));
    }

    [Fact]
    public async Task Counts_are_read_from_the_counts_route()
    {
        var (api, client) = await StartAsync();
        await using var _ = api;
        api.Stub.OnJson(HttpMethod.Get, "/api/tickets/counts", new TicketViewCountsResponse(1, 2, 3, 4, 5, 6));

        (await client.GetCountsAsync(Ct)).Value.Spam.ShouldBe(6);
    }

    [Fact]
    public async Task Each_state_change_uses_its_verb_route_and_sends_the_row_version_in_the_body()
    {
        var (api, client) = await StartAsync();
        await using var _ = api;
        var assignee = Guid.NewGuid();
        var product = Guid.NewGuid();
        var tag = Guid.NewGuid();
        foreach (var (method, suffix) in new[]
        {
            (HttpMethod.Put, "status"), (HttpMethod.Put, "assignee"), (HttpMethod.Put, "priority"),
            (HttpMethod.Put, "product"), (HttpMethod.Post, "tags"), (HttpMethod.Put, "spam"),
        })
        {
            api.Stub.OnJson(method, $"/api/tickets/{TicketId}/{suffix}", State());
        }

        (await client.ChangeStatusAsync(TicketId, new ChangeTicketStatusRequest(TicketStatuses.Solved, 7), Ct)).IsSuccess.ShouldBeTrue();
        Body(api).GetProperty("rowVersion").GetUInt32().ShouldBe(7u);
        Body(api).GetProperty("status").GetString().ShouldBe("Solved");

        (await client.AssignAsync(TicketId, new AssignTicketRequest(assignee, 8), Ct)).IsSuccess.ShouldBeTrue();
        Body(api).GetProperty("assigneeId").GetGuid().ShouldBe(assignee);

        (await client.AssignAsync(TicketId, new AssignTicketRequest(null, 9), Ct)).IsSuccess.ShouldBeTrue();
        Body(api).GetProperty("assigneeId").ValueKind.ShouldBe(JsonValueKind.Null);

        (await client.ChangePriorityAsync(TicketId, new ChangeTicketPriorityRequest(TicketPriorities.Urgent, 10), Ct)).IsSuccess.ShouldBeTrue();
        (await client.MoveProductAsync(TicketId, new MoveTicketProductRequest(product, 11), Ct)).IsSuccess.ShouldBeTrue();
        (await client.AddTagAsync(TicketId, new AddTicketTagRequest(tag, 12), Ct)).IsSuccess.ShouldBeTrue();
        (await client.SetSpamAsync(TicketId, new MarkTicketSpamRequest(false, 13), Ct)).IsSuccess.ShouldBeTrue();
        Body(api).GetProperty("isSpam").GetBoolean().ShouldBeFalse();

        api.Stub.Requests.Select(r => $"{r.Method} {r.Path}").ShouldBe(
        [
            $"PUT /api/tickets/{TicketId}/status", $"PUT /api/tickets/{TicketId}/assignee", $"PUT /api/tickets/{TicketId}/assignee",
            $"PUT /api/tickets/{TicketId}/priority", $"PUT /api/tickets/{TicketId}/product", $"POST /api/tickets/{TicketId}/tags", $"PUT /api/tickets/{TicketId}/spam",
        ]);
    }

    [Fact]
    public async Task Removing_a_tag_sends_the_row_version_in_the_query_and_no_body()
    {
        var (api, client) = await StartAsync();
        await using var _ = api;
        var tag = Guid.NewGuid();
        api.Stub.OnJson(HttpMethod.Delete, $"/api/tickets/{TicketId}/tags/{tag}", State());

        var result = await client.RemoveTagAsync(TicketId, tag, 21, Ct);

        result.Value.RowVersion.ShouldBe(8u);
        var request = api.Stub.Requests.ShouldHaveSingleItem();
        request.Query.ShouldBe("?rowVersion=21");
        request.Body.ShouldBeNull();
    }

    [Fact]
    public async Task A_stale_row_version_is_a_conflict_with_the_concurrency_code_and_the_call_is_not_retried()
    {
        var (api, client) = await StartAsync();
        await using var _ = api;
        api.Stub.OnProblem(HttpMethod.Put, $"/api/tickets/{TicketId}/status", HttpStatusCode.Conflict, ApiErrorCodes.ConcurrencyConflict, "The ticket changed.");

        var result = await client.ChangeStatusAsync(TicketId, new ChangeTicketStatusRequest(TicketStatuses.Open, 1), Ct);

        result.Errors.ShouldHaveSingleItem().ShouldBe(new ResultError(ApiErrorCodes.ConcurrencyConflict, "The ticket changed.", ResultErrorKind.Conflict));
        api.Stub.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_note_is_json_and_a_reply_is_multipart_with_the_fields_and_files_the_api_binds()
    {
        var (api, client) = await StartAsync();
        await using var _ = api;
        var response = new AgentMessageResponse(
            new MessageDto(Guid.NewGuid(), "Agent", Guid.NewGuid(), "Sam", "Public", "<p>hi</p>", DateTimeOffset.UtcNow, [], []), State());
        api.Stub.OnJson(HttpMethod.Post, $"/api/tickets/{TicketId}/notes", response, HttpStatusCode.Created);
        api.Stub.OnJson(HttpMethod.Post, $"/api/tickets/{TicketId}/replies", response, HttpStatusCode.Created);
        var article = Guid.NewGuid();
        var opened = 0;

        (await client.AddNoteAsync(TicketId, new AddInternalNoteRequest("heads up", 5), Ct)).IsSuccess.ShouldBeTrue();
        Body(api).GetProperty("body").GetString().ShouldBe("heads up");

        var reply = await client.ReplyAsync(
            TicketId,
            new AddAgentReplyRequest("Thanks!", [article], TicketStatuses.Solved, 6),
            [new ReplyAttachment("log.txt", "text/plain", () => { opened++; return new MemoryStream("file-bytes"u8.ToArray()); })],
            Ct);

        reply.Value.Ticket.RowVersion.ShouldBe(8u);
        var sent = api.Stub.Requests.Last();
        sent.ContentType.ShouldStartWith("multipart/form-data");
        sent.Body.ShouldNotBeNull();
        sent.Body.ShouldContain("name=Body");
        sent.Body.ShouldContain("Thanks!");
        sent.Body.ShouldContain("name=LinkedArticleIds");
        sent.Body.ShouldContain(article.ToString());
        sent.Body.ShouldContain("name=StatusAfter");
        sent.Body.ShouldContain("Solved");
        sent.Body.ShouldContain("name=RowVersion");
        sent.Body.ShouldContain("name=Attachments; filename=log.txt");
        sent.Body.ShouldContain("file-bytes");
        opened.ShouldBe(1);
    }

    [Fact]
    public async Task A_failed_reply_can_be_sent_again_because_the_files_are_opened_for_each_attempt()
    {
        var (api, client) = await StartAsync();
        await using var _ = api;
        api.Stub.OnProblem(HttpMethod.Post, $"/api/tickets/{TicketId}/replies", HttpStatusCode.ServiceUnavailable, "unavailable", "Down.");
        var opened = 0;
        ReplyAttachment[] files = [new("a.png", "image/png", () => { opened++; return new MemoryStream([1, 2, 3]); })];

        (await client.ReplyAsync(TicketId, new AddAgentReplyRequest("x", null, null, 1), files, Ct)).IsFailure.ShouldBeTrue();
        (await client.ReplyAsync(TicketId, new AddAgentReplyRequest("x", null, null, 1), files, Ct)).IsFailure.ShouldBeTrue();

        opened.ShouldBe(2);
        api.Stub.Count(HttpMethod.Post, $"/api/tickets/{TicketId}/replies").ShouldBe(2);
    }

    [Fact]
    public async Task Delete_and_erase_are_bodyless_and_a_403_keeps_the_admin_code()
    {
        var (api, client) = await StartAsync();
        await using var _ = api;
        var requester = Guid.NewGuid();
        api.Stub.OnStatus(HttpMethod.Delete, $"/api/tickets/{TicketId}", HttpStatusCode.NoContent);
        api.Stub.OnProblem(HttpMethod.Post, $"/api/requesters/{requester}/erase", HttpStatusCode.Forbidden, ApiErrorCodes.AdminAccessRequired, "Admins only.");

        (await client.DeleteAsync(TicketId, Ct)).IsSuccess.ShouldBeTrue();
        var erase = await api.Get<IRequestersClient>().EraseAsync(requester, Ct);

        erase.Errors.ShouldHaveSingleItem().Code.ShouldBe(ApiErrorCodes.AdminAccessRequired);
        api.Stub.Requests.ShouldAllBe(r => r.Body == null);
    }
}
