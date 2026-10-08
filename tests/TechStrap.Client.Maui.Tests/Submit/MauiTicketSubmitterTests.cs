using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Contracts.Intake;

namespace TechStrap.Client.Maui.Tests.Submit;

public sealed class MauiTicketSubmitterTests
{
    private static readonly Result<SubmitTicketResponse> Ok = Result<SubmitTicketResponse>.Success(new SubmitTicketResponse("ORB-1", null, []));

    private readonly ITechStrapClient _client = Substitute.For<ITechStrapClient>();
    private readonly IDeviceContextCollector _collector = Substitute.For<IDeviceContextCollector>();
    private SubmitTicketRequest? _sent;

    public MauiTicketSubmitterTests()
    {
        _collector.Collect().Returns(new Dictionary<string, string>());
        _client.SubmitTicketAsync(Arg.Do<SubmitTicketRequest>(r => _sent = r), Arg.Any<CancellationToken>()).Returns(Ok);
        _client.SubmitTicketAsync(Arg.Do<SubmitTicketRequest>(r => _sent = r), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Ok);
    }

    private MauiTicketSubmitter Submitter() => new(_client, _collector);

    private static MauiTicketDraft Draft(IReadOnlyDictionary<string, string>? metadata = null, string? key = null) =>
        new("Cannot log in", "It says no.", "pat@example.com", "Pat", metadata, key);

    [Fact(Timeout = 10_000)]
    public async Task The_request_maps_subject_message_email_and_name()
    {
        await Submitter().SubmitAsync(Draft(), Xunit.TestContext.Current.CancellationToken);

        _sent.ShouldNotBeNull();
        _sent.Subject.ShouldBe("Cannot log in");
        _sent.Body.ShouldBe("It says no.");
        _sent.Email.ShouldBe("pat@example.com");
        _sent.Name.ShouldBe("Pat");
        _sent.ExternalUserRef.ShouldBeNull();
    }

    [Fact(Timeout = 10_000)]
    public async Task Collected_metadata_is_sent()
    {
        _collector.Collect().Returns(new Dictionary<string, string> { [TicketMetadataKeys.AppName] = "Puppies Plus", [TicketMetadataKeys.OsPlatform] = "Android" });

        await Submitter().SubmitAsync(Draft(), Xunit.TestContext.Current.CancellationToken);

        _sent!.Metadata.ShouldNotBeNull();
        _sent.Metadata[TicketMetadataKeys.AppName].ShouldBe("Puppies Plus");
        _sent.Metadata[TicketMetadataKeys.OsPlatform].ShouldBe("Android");
    }

    [Fact(Timeout = 10_000)]
    public async Task Disabled_device_context_sends_only_app_metadata()
    {
        var app = new Dictionary<string, string> { ["screen"] = "checkout" };

        await Submitter().SubmitAsync(Draft(app), Xunit.TestContext.Current.CancellationToken);

        _sent!.Metadata.ShouldNotBeNull();
        _sent.Metadata.ShouldBe(app);
        _sent.Metadata.Keys.ShouldAllBe(k => !TicketMetadataKeys.All.Contains(k));
    }

    [Fact(Timeout = 10_000)]
    public async Task A_supplied_idempotency_key_uses_the_keyed_overload()
    {
        var token = Xunit.TestContext.Current.CancellationToken;

        await Submitter().SubmitAsync(Draft(key: "k-1"), token);

        await _client.Received(1).SubmitTicketAsync(Arg.Any<SubmitTicketRequest>(), "k-1", token);
        await _client.DidNotReceive().SubmitTicketAsync(Arg.Any<SubmitTicketRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact(Timeout = 10_000)]
    public async Task Without_a_key_the_unkeyed_overload_is_used()
    {
        await Submitter().SubmitAsync(Draft(), Xunit.TestContext.Current.CancellationToken);

        await _client.Received(1).SubmitTicketAsync(Arg.Any<SubmitTicketRequest>(), Arg.Any<CancellationToken>());
        await _client.DidNotReceive().SubmitTicketAsync(Arg.Any<SubmitTicketRequest>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact(Timeout = 10_000)]
    public async Task A_local_metadata_failure_sends_nothing()
    {
        var app = Enumerable.Range(0, IntakeLimits.MaxMetadataKeys + 1).ToDictionary(i => $"k{i}", _ => "v");

        var result = await Submitter().SubmitAsync(Draft(app), Xunit.TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeFalse();
        result.Errors[0].Code.ShouldBe(TechStrapMauiErrorCodes.MetadataInvalid);
        result.Errors[0].Kind.ShouldBe(ResultErrorKind.Validation);
        _client.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact(Timeout = 10_000)]
    public async Task The_client_result_is_returned_unchanged()
    {
        var failure = Result<SubmitTicketResponse>.Failure(new ResultError("rate-limited", "Slow down.", ResultErrorKind.Validation));
        _client.SubmitTicketAsync(Arg.Any<SubmitTicketRequest>(), Arg.Any<CancellationToken>()).Returns(failure);

        var result = await Submitter().SubmitAsync(Draft(), Xunit.TestContext.Current.CancellationToken);

        result.ShouldBeSameAs(failure);
    }

    [Fact(Timeout = 10_000)]
    public async Task Cancellation_token_is_forwarded()
    {
        using var source = CancellationTokenSource.CreateLinkedTokenSource(Xunit.TestContext.Current.CancellationToken);

        await Submitter().SubmitAsync(Draft(), source.Token);

        await _client.Received(1).SubmitTicketAsync(Arg.Any<SubmitTicketRequest>(), source.Token);
    }

    [Fact(Timeout = 10_000)]
    public async Task A_null_draft_throws()
    {
        await Should.ThrowAsync<ArgumentNullException>(() => Submitter().SubmitAsync(null!, Xunit.TestContext.Current.CancellationToken));
    }
}
