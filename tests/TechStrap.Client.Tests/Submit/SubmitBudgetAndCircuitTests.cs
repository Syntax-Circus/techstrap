using System.Net;
using Microsoft.Extensions.Options;
using TechStrap.Client.Tests.Infrastructure;

namespace TechStrap.Client.Tests.Submit;

public sealed class SubmitBudgetAndCircuitTests
{
    [Fact(Timeout = 10_000)]
    public async Task A_hanging_server_ends_as_api_unavailable_when_the_total_timeout_passes()
    {
        using var fixture = new ClientFixture(o => o.Timeout = TimeSpan.FromSeconds(10), fakeTime: true);
        fixture.Stub.Hang();

        var call = fixture.Client.SubmitTicketAsync(ClientFixture.Request(), null, Xunit.TestContext.Current.CancellationToken);
        await fixture.Stub.Received.WaitAsync(Xunit.TestContext.Current.CancellationToken);
        call.IsCompleted.ShouldBeFalse();
        fixture.Time!.Advance(TimeSpan.FromSeconds(11));
        var result = await call;

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe(TechStrapClientErrorCodes.ApiUnavailable);
    }

    [Fact(Timeout = 10_000)]
    public async Task A_hanging_server_on_the_once_path_also_ends_as_api_unavailable()
    {
        using var fixture = new ClientFixture(o => o.Timeout = TimeSpan.FromSeconds(10), fakeTime: true);
        fixture.Stub.Hang();

        var call = fixture.Client.SubmitTicketOnceAsync(ClientFixture.Request(), Xunit.TestContext.Current.CancellationToken);
        await fixture.Stub.Received.WaitAsync(Xunit.TestContext.Current.CancellationToken);
        fixture.Time!.Advance(TimeSpan.FromSeconds(11));
        var result = await call;

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe(TechStrapClientErrorCodes.ApiUnavailable);
    }

    [Fact(Timeout = 10_000)]
    public async Task An_open_circuit_skips_the_handler_until_the_break_duration_passes()
    {
        using var fixture = new ClientFixture(o => o.MaxAttempts = 1, fakeTime: true);
        fixture.Stub.Respond(HttpStatusCode.ServiceUnavailable, times: ResilienceDefaults.CircuitMinimumThroughput);

        for (var i = 0; i < ResilienceDefaults.CircuitMinimumThroughput; i++)
        {
            (await fixture.Client.SubmitTicketAsync(ClientFixture.Request(), null, Xunit.TestContext.Current.CancellationToken)).IsFailure.ShouldBeTrue();
        }

        fixture.Stub.Requests.Count.ShouldBe(ResilienceDefaults.CircuitMinimumThroughput);

        var blocked = await fixture.Client.SubmitTicketAsync(ClientFixture.Request(), null, Xunit.TestContext.Current.CancellationToken);

        blocked.Errors.ShouldHaveSingleItem().Code.ShouldBe(TechStrapClientErrorCodes.ApiUnavailable);
        fixture.Stub.Requests.Count.ShouldBe(ResilienceDefaults.CircuitMinimumThroughput);

        fixture.Time!.Advance(ResilienceDefaults.CircuitBreakDuration + TimeSpan.FromSeconds(1));
        fixture.Stub.Created();
        var recovered = await fixture.Client.SubmitTicketAsync(ClientFixture.Request(), null, Xunit.TestContext.Current.CancellationToken);

        recovered.IsSuccess.ShouldBeTrue();
        fixture.Stub.Requests.Count.ShouldBe(ResilienceDefaults.CircuitMinimumThroughput + 1);
    }

    [Fact(Timeout = 10_000)]
    public async Task A_caller_cancellation_propagates_and_is_not_a_result()
    {
        using var fixture = new ClientFixture();
        fixture.Stub.Hang();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Xunit.TestContext.Current.CancellationToken);

        var call = fixture.Client.SubmitTicketAsync(ClientFixture.Request(), null, cts.Token);
        await fixture.Stub.Received.WaitAsync(Xunit.TestContext.Current.CancellationToken);
        await cts.CancelAsync();

        var failure = await Should.ThrowAsync<OperationCanceledException>(() => call);
        failure.CancellationToken.ShouldBe(cts.Token);
    }

    [Fact(Timeout = 10_000)]
    public async Task A_cancelled_token_throws_and_sends_nothing()
    {
        using var fixture = new ClientFixture();
        fixture.Stub.Created();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Xunit.TestContext.Current.CancellationToken);
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => fixture.Client.SubmitTicketAsync(ClientFixture.Request(), null, cts.Token));
        await Should.ThrowAsync<OperationCanceledException>(() => fixture.Client.SubmitTicketOnceAsync(ClientFixture.Request(), cts.Token));

        fixture.Stub.Requests.ShouldBeEmpty();
    }

    [Fact(Timeout = 10_000)]
    public async Task A_null_request_throws()
    {
        using var fixture = new ClientFixture();

        await Should.ThrowAsync<ArgumentNullException>(() => fixture.Client.SubmitTicketAsync(null!, null, Xunit.TestContext.Current.CancellationToken));
        await Should.ThrowAsync<ArgumentNullException>(() => fixture.Client.SubmitTicketOnceAsync(null!, Xunit.TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = 10_000)]
    public async Task Invalid_options_fail_on_first_use()
    {
        using var fixture = new ClientFixture(o => o.ApiKey = null);

        var failure = await Should.ThrowAsync<OptionsValidationException>(() => fixture.Client.SubmitTicketAsync(ClientFixture.Request(), null, Xunit.TestContext.Current.CancellationToken));

        failure.Message.ShouldContain("ApiKey");
        fixture.Stub.Requests.ShouldBeEmpty();
    }
}
