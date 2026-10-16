using System.Net;
using System.Text;
using TechStrap.Api.Tests;
using TechStrap.Contracts.Intake;

namespace TechStrap.Client.Tests.Integration;

[Trait("Integration", "Docker")]
public sealed class AttachmentAgainstTheApiTests(TestPostgres postgres)
{
    private static readonly SubmitTicketRequest Sample = new("ada@example.com", "Ada", "Help", "Please help", "u-1", new Dictionary<string, string> { ["screen"] = "Home" }, "FeatureRequest");

    [Theory(Timeout = 120_000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Multipart_upload_survives_a_lost_response_without_duplicate_rows(bool trusted)
    {
        await using var api = await ApiHarness.StartAsync(postgres);
        var lost = new LoseFirstResponseHandler();
        using var sdk = SdkHost.ForTestServer(api.Factory, trusted ? api.Seed.OrbitlyTrusted : api.Seed.OrbitlyPublic, lost);
        var opens = 0;
        var file = new TicketAttachment("trace.log", "text/plain", _ =>
        {
            opens++;
            return ValueTask.FromResult<Stream>(new MemoryStream("SDK content"u8.ToArray()));
        });
        var result = await sdk.Client.SubmitTicketAsync(Sample, [file], "lost-response-upload", TestContext.Current.CancellationToken);
        result.IsSuccess.ShouldBeTrue();
        opens.ShouldBe(2);
        lost.IdempotencyKeys.ShouldBe(["lost-response-upload", "lost-response-upload"]);
        (await api.Database.ScalarAsync<long>("SELECT count(*) FROM tickets")).ShouldBe(1);
        (await api.Database.ScalarAsync<long>("SELECT count(*) FROM attachments")).ShouldBe(1);
        (await api.Database.ScalarAsync<long>("SELECT count(*) FROM email_outbox WHERE kind = 'ticket-confirmation'")).ShouldBe(1);
        (await api.Database.ScalarAsync<string>("SELECT type FROM tickets")).ShouldBe("FeatureRequest");
        (await api.Database.ScalarAsync<bool>("SELECT metadata_trusted FROM tickets")).ShouldBe(trusted);
        var token = result.Value.ViewUrl!.Split('/').Last();
        var id = await api.Database.ScalarAsync<Guid>("SELECT id FROM attachments");
        using var client = api.Factory.CreateClient();
        using var download = new HttpRequestMessage(HttpMethod.Get, $"/api/customer/attachments/{id}");
        download.Headers.Add(Contracts.Http.HeaderNames.TicketToken, token);
        using var response = await client.SendAsync(download, TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        Encoding.UTF8.GetString(await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken)).ShouldBe("SDK content");
    }

    [Fact(Timeout = 120_000)]
    public async Task Real_socket_upload_stores_bytes_without_requiring_seekable_sources()
    {
        await using var api = await ApiHarness.StartAsync(postgres, kestrel: true);
        using var sdk = SdkHost.ForKestrel(api.Factory, api.Seed.OrbitlyTrusted);
        var attachment = new TicketAttachment("trace.log", "text/plain", _ => ValueTask.FromResult<Stream>(new NonSeekableStream("socket upload"u8.ToArray())));
        var result = await sdk.Client.SubmitTicketAsync(Sample, [attachment], TestContext.Current.CancellationToken);
        result.IsSuccess.ShouldBeTrue();
        (await api.Database.ScalarAsync<long>("SELECT size FROM attachments")).ShouldBe(13);
    }

    [Fact(Timeout = 120_000)]
    public async Task Real_socket_local_failure_is_safe_and_not_retried()
    {
        await using var api = await ApiHarness.StartAsync(postgres, kestrel: true);
        using var sdk = SdkHost.ForKestrel(api.Factory, api.Seed.OrbitlyTrusted);
        var opens = 0;
        var attachment = new TicketAttachment("trace.log", "text/plain", _ => { opens++; throw new IOException("private-path"); });
        var result = await sdk.Client.SubmitTicketAsync(Sample, [attachment], TestContext.Current.CancellationToken);
        result.Errors[0].Code.ShouldBe("attachment-read-failed");
        result.Errors[0].Message.ShouldNotContain("private-path");
        opens.ShouldBe(1);
        (await api.Database.ScalarAsync<long>("SELECT count(*) FROM tickets")).ShouldBe(0);
    }

    private sealed class NonSeekableStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }
}
