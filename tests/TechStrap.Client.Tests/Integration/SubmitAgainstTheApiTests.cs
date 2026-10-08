using TechStrap.Api.Startup;
using TechStrap.Api.Tests;
using SyntaxCircus.Common;
using TechStrap.Contracts.Intake;

namespace TechStrap.Client.Tests.Integration;

[Trait("Integration", "Docker")]
public sealed class SubmitAgainstTheApiTests(TestPostgres postgres)
{
    private const int TestTimeout = 120_000;

    private static readonly SubmitTicketRequest Sample = new("ada@example.com", "Ada", "Help", "Please help", "u-1", new Dictionary<string, string> { ["plan"] = "pro" });

    [Fact(Timeout = TestTimeout)]
    public async Task A_trusted_key_creates_a_ticket_with_trusted_metadata_through_the_api_channel()
    {
        await using var api = await ApiHarness.StartAsync(postgres);
        using var sdk = SdkHost.ForTestServer(api.Factory, api.Seed.OrbitlyTrusted);

        var result = await sdk.Client.SubmitTicketAsync(Sample, Xunit.TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.TicketNumber.ShouldBe("ORB-1");
        result.Value.ViewUrl.ShouldNotBeNull().ShouldStartWith("https://help.test/t/");
        result.Value.Warnings.ShouldBeEmpty();
        (await api.Database.ScalarAsync<bool>("SELECT metadata_trusted FROM tickets WHERE number = 'ORB-1'")).ShouldBeTrue();
        (await api.Database.ScalarAsync<string>("SELECT external_user_ref FROM requesters")).ShouldBe("u-1");
        (await api.Database.ScalarAsync<long>("SELECT count(*) FROM tickets WHERE channel = 'Api'")).ShouldBe(1);
    }

    [Fact(Timeout = TestTimeout)]
    public async Task A_public_key_drops_the_external_ref_with_a_warning_and_untrusted_metadata()
    {
        await using var api = await ApiHarness.StartAsync(postgres);
        using var sdk = SdkHost.ForTestServer(api.Factory, api.Seed.OrbitlyPublic);

        var result = await sdk.Client.SubmitTicketAsync(Sample, Xunit.TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Warnings.ShouldBe(["external-user-ref-ignored"]);
        (await api.Database.ScalarAsync<bool>("SELECT metadata_trusted FROM tickets WHERE number = 'ORB-1'")).ShouldBeFalse();
        (await api.Database.ScalarAsync<long>("SELECT count(*) FROM requesters WHERE external_user_ref IS NOT NULL")).ShouldBe(0);
    }

    [Theory(Timeout = TestTimeout)]
    [InlineData("revoked")]
    [InlineData("unknown")]
    [InlineData("deactivated-product")]
    public async Task A_key_the_api_rejects_is_invalid_api_key_and_creates_nothing(string which)
    {
        await using var api = await ApiHarness.StartAsync(postgres);
        var key = which switch
        {
            "revoked" => api.Seed.OrbitlyRevoked,
            "deactivated-product" => api.Seed.DormantTrusted,
            _ => "tsp_unknown_key_one_aaaaaaaa",
        };
        using var sdk = SdkHost.ForTestServer(api.Factory, key);

        var result = await sdk.Client.SubmitTicketAsync(Sample, Xunit.TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe(TechStrapClientErrorCodes.InvalidApiKey);
        (await api.Database.ScalarAsync<long>("SELECT count(*) FROM tickets")).ShouldBe(0);
    }

    [Fact(Timeout = TestTimeout)]
    public async Task An_invalid_email_is_a_validation_error_targeting_email()
    {
        await using var api = await ApiHarness.StartAsync(postgres);
        using var sdk = SdkHost.ForTestServer(api.Factory, api.Seed.OrbitlyTrusted);

        var result = await sdk.Client.SubmitTicketAsync(Sample with { Email = "not-an-email" }, Xunit.TestContext.Current.CancellationToken);

        var error = result.Errors.ShouldHaveSingleItem();
        error.Kind.ShouldBe(ResultErrorKind.Validation);
        error.Target.ShouldBe("email");
        error.Code.ShouldBe("email-invalid");
        (await api.Database.ScalarAsync<long>("SELECT count(*) FROM tickets")).ShouldBe(0);
    }

    [Fact(Timeout = TestTimeout)]
    public async Task An_oversized_body_is_payload_too_large()
    {
        await using var api = await ApiHarness.StartAsync(postgres, kestrel: true);
        using var sdk = SdkHost.ForKestrel(api.Factory, api.Seed.OrbitlyTrusted);
        var oversized = Sample with { Body = new string('x', (int)IntakeRequestLimits.JsonBodyBytes + 1) };

        var result = await sdk.Client.SubmitTicketAsync(oversized, Xunit.TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe(TechStrapClientErrorCodes.PayloadTooLarge);
        (await api.Database.ScalarAsync<long>("SELECT count(*) FROM tickets")).ShouldBe(0);
    }

    [Fact(Timeout = TestTimeout)]
    public async Task The_second_submission_over_the_trusted_limit_is_rate_limited()
    {
        await using var api = await ApiHarness.StartAsync(postgres, extraSettings: new Dictionary<string, string?> { ["RateLimiting:Intake:TrustedKeyPermitLimit"] = "1" });
        using var sdk = SdkHost.ForTestServer(api.Factory, api.Seed.OrbitlyTrusted);

        (await sdk.Client.SubmitTicketAsync(Sample, Xunit.TestContext.Current.CancellationToken)).IsSuccess.ShouldBeTrue();
        var second = await sdk.Client.SubmitTicketAsync(Sample, Xunit.TestContext.Current.CancellationToken);

        second.Errors.ShouldHaveSingleItem().Code.ShouldBe(TechStrapClientErrorCodes.RateLimited);
        (await api.Database.ScalarAsync<long>("SELECT count(*) FROM tickets")).ShouldBe(1);
    }
}
