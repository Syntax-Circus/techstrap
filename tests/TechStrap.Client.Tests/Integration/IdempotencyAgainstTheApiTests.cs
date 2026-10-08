using TechStrap.Api.Tests;
using TechStrap.Contracts.Intake;

namespace TechStrap.Client.Tests.Integration;

[Trait("Integration", "Docker")]
public sealed class IdempotencyAgainstTheApiTests(TestPostgres postgres)
{
    private const int TestTimeout = 120_000;

    private static readonly SubmitTicketRequest Sample = new("ada@example.com", "Ada", "Help", "Please help", null, null);

    [Fact(Timeout = TestTimeout)]
    public async Task A_lost_response_is_retried_with_the_same_key_and_the_ticket_is_created_once()
    {
        await using var api = await ApiHarness.StartAsync(postgres);
        var lost = new LoseFirstResponseHandler();
        using var sdk = SdkHost.ForTestServer(api.Factory, api.Seed.OrbitlyTrusted, lost);

        var result = await sdk.Client.SubmitTicketAsync(Sample, null, Xunit.TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.TicketNumber.ShouldBe("ORB-1");
        lost.IdempotencyKeys.Count.ShouldBe(2);
        lost.IdempotencyKeys[0].ShouldNotBeNullOrWhiteSpace();
        lost.IdempotencyKeys[1].ShouldBe(lost.IdempotencyKeys[0]);
        (await api.Database.ScalarAsync<long>("SELECT count(*) FROM tickets")).ShouldBe(1);
    }

    [Fact(Timeout = TestTimeout)]
    public async Task Submitting_once_with_a_lost_response_makes_one_attempt_without_a_key_and_reports_api_unavailable()
    {
        await using var api = await ApiHarness.StartAsync(postgres);
        var lost = new LoseFirstResponseHandler();
        using var sdk = SdkHost.ForTestServer(api.Factory, api.Seed.OrbitlyTrusted, lost);

        var result = await sdk.Client.SubmitTicketOnceAsync(Sample, Xunit.TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe(TechStrapClientErrorCodes.ApiUnavailable);
        lost.IdempotencyKeys.ShouldBe([null]);
        (await api.Database.ScalarAsync<long>("SELECT count(*) FROM tickets")).ShouldBe(1);
    }

    [Fact(Timeout = TestTimeout)]
    public async Task A_supplied_key_replayed_in_a_second_call_returns_the_first_ticket()
    {
        await using var api = await ApiHarness.StartAsync(postgres);
        using var sdk = SdkHost.ForTestServer(api.Factory, api.Seed.OrbitlyTrusted);

        var first = await sdk.Client.SubmitTicketAsync(Sample, "order-4711", Xunit.TestContext.Current.CancellationToken);
        var second = await sdk.Client.SubmitTicketAsync(Sample, "order-4711", Xunit.TestContext.Current.CancellationToken);

        first.IsSuccess.ShouldBeTrue();
        second.IsSuccess.ShouldBeTrue();
        second.Value.TicketNumber.ShouldBe(first.Value.TicketNumber);
        (await api.Database.ScalarAsync<long>("SELECT count(*) FROM tickets")).ShouldBe(1);
    }
}
