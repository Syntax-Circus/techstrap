using Microsoft.Maui.Storage;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Contracts.Intake;

namespace TechStrap.Client.Maui.Tests.Submit;

public sealed class MauiAttachmentTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("stable-upload")]
    public async Task Draft_files_are_forwarded_with_merged_metadata_and_key(string? key)
    {
        var client = Substitute.For<ITechStrapClient>();
        var collector = Substitute.For<IDeviceContextCollector>();
        collector.Collect().Returns(new Dictionary<string, string> { [TicketMetadataKeys.AppName] = "Test app" });
        var file = new TicketAttachment("trace.log", "text/plain", _ => ValueTask.FromResult<Stream>(new MemoryStream([65])));
        var files = new[] { file };
        var expected = Result<SubmitTicketResponse>.Success(new("ORB-1", null, []));
        client.SubmitTicketAsync(Arg.Any<SubmitTicketRequest>(), files, Arg.Any<CancellationToken>()).Returns(expected);
        client.SubmitTicketAsync(Arg.Any<SubmitTicketRequest>(), files, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(expected);
        var draft = new MauiTicketDraft("Crash", "Details", "a@example.com", IdempotencyKey: key, Type: "FeatureRequest") { Attachments = files };

        var result = await new MauiTicketSubmitter(client, collector).SubmitAsync(draft, TestContext.Current.CancellationToken);

        result.ShouldBe(expected);
        if (key is null)
            await client.Received(1).SubmitTicketAsync(Arg.Is<SubmitTicketRequest>(r => r.Metadata![TicketMetadataKeys.AppName] == "Test app" && r.Type == "FeatureRequest"), files, TestContext.Current.CancellationToken);
        else
            await client.Received(1).SubmitTicketAsync(Arg.Is<SubmitTicketRequest>(r => r.Metadata![TicketMetadataKeys.AppName] == "Test app"), files, key, TestContext.Current.CancellationToken);
    }

    [Fact]
    public void File_result_adapter_preserves_name_and_explicit_or_selected_content_type()
    {
        var selected = new FileResult("trace.log", "text/plain");
        var attachment = selected.ToTicketAttachment();
        attachment.FileName.ShouldBe("trace.log");
        attachment.ContentType.ShouldBe("text/plain");
        selected.ToTicketAttachment("application/octet-stream").ContentType.ShouldBe("application/octet-stream");
    }

    [Fact]
    public async Task Canceled_file_open_never_calls_the_platform()
    {
        var attachment = new FileResult("trace.log", "text/plain").ToTicketAttachment();
        using var source = new CancellationTokenSource();
        source.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(() => attachment.OpenReadAsync(source.Token).AsTask());
    }
}
