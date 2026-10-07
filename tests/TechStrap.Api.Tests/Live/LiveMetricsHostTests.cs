using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using TechStrap.Api.Tests.Auth;
using TechStrap.Api.Tests.Hosting;
using TechStrap.Api.Tests.Tickets;
using TechStrap.Infrastructure.Live;
using TechStrap.Tests.Shared;

namespace TechStrap.Api.Tests.Live;

/// <summary>
/// The metrics at the host: the connected-agents gauge follows real hub connections (distinct agents), and TechStrap's meter is registered with the observability package in the
/// Api and the Worker, so an exporter would actually read it. A meter nobody registers has instruments that nothing ever collects.
/// </summary>
/// <remarks>The registration tests set process environment variables (the exporter options bind before host settings exist), so the class runs in the non-parallel <see cref="ProcessEnvironmentCollection"/>.</remarks>
[Collection(ProcessEnvironmentCollection.Name)]
public sealed class LiveMetricsHostTests(TestPostgres postgres)
{
    private static async Task UntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(HubTestSupport.Patience);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, TestContext.Current.CancellationToken);
        while (!condition())
        {
            await Task.Delay(50, linked.Token);
        }
    }

    [Fact(Timeout = 120000)]
    public async Task The_connected_agents_gauge_counts_distinct_agents_as_hub_connections_come_and_go()
    {
        var ct = TestContext.Current.CancellationToken;
        var database = await ApiTestDatabase.CreateAsync(postgres);
        await using var factory = new ApiFactory(settings: new Dictionary<string, string?>(database.Settings) { ["TECHSTRAP_PORTAL_PUBLIC_URL"] = "https://help.test" });
        await TicketTestData.SeedAsync(factory, ct);
        var metrics = factory.Services.GetRequiredService<TechStrapMetrics>();
        await using var samOne = HubTestSupport.Connect(factory, HubTestSupport.AgentToken("sam", "Sam"));
        await using var samTwo = HubTestSupport.Connect(factory, HubTestSupport.AgentToken("sam", "Sam"));
        await using var kim = HubTestSupport.Connect(factory, HubTestSupport.AgentToken("kim", "Kim"));
        metrics.ConnectedAgents.ShouldBe(0);

        await samOne.StartAsync(ct);
        await samTwo.StartAsync(ct);
        await UntilAsync(() => metrics.ConnectedAgents == 1);
        await kim.StartAsync(ct);
        await UntilAsync(() => metrics.ConnectedAgents == 2);

        await samOne.StopAsync(ct);
        await Task.Delay(200, ct);
        metrics.ConnectedAgents.ShouldBe(2);
        await samTwo.StopAsync(ct);
        await UntilAsync(() => metrics.ConnectedAgents == 1);
        await kim.StopAsync(ct);
        await UntilAsync(() => metrics.ConnectedAgents == 0);
    }

    [Fact]
    public async Task The_api_and_the_worker_register_the_meter_so_an_enabled_exporter_reads_it_and_it_is_unread_otherwise()
    {
        await using var probe = new OtlpProbe();
        bool apiPlain, workerPlain;
        await using (var plainApi = new ApiFactory())
        {
            apiPlain = plainApi.Services.GetRequiredService<TechStrapMetrics>().IsObserved;
        }

        await using (var plainWorker = new WorkerFactory())
        {
            workerPlain = plainWorker.Services.GetRequiredService<TechStrapMetrics>().IsObserved;
        }

        bool apiExported = false, workerExported = false;
        await OtlpLeakTests.WithOtlpAsync(probe, async () =>
        {
            // One host at a time: a meter provider subscribes to every meter of that name in the process, so a second host alive beside the first would be observed on the first one's registration.
            await using (var api = new ApiFactory())
            {
                apiExported = api.Services.GetRequiredService<TechStrapMetrics>().IsObserved;
            }

            await using (var worker = new WorkerFactory())
            {
                workerExported = worker.Services.GetRequiredService<TechStrapMetrics>().IsObserved;
            }
        });

        apiPlain.ShouldBeFalse();
        workerPlain.ShouldBeFalse();
        apiExported.ShouldBeTrue("the Api must pass the meter name to AddSyntaxCircusObservability");
        workerExported.ShouldBeTrue("the Worker must pass the meter name to AddSyntaxCircusObservability");
    }
}
