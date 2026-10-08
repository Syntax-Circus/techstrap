using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;
using TechStrap.Client.Tests.Infrastructure;
using TechStrap.Contracts.Http;

namespace TechStrap.Client.Tests.Registration;

/// <summary>A host-wide <c>ConfigureHttpClientDefaults</c> handler (the Aspire ServiceDefaults template adds a standard resilience handler) must not join the SDK's named client: it would retry a call that has no key.</summary>
public sealed class HostDefaultsTests
{
    private sealed class CountingHandler : DelegatingHandler
    {
        public int Seen { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Seen++;
            return base.SendAsync(request, cancellationToken);
        }
    }

    private static (ServiceProvider Provider, StubHandler Stub, CountingHandler Counting) Build()
    {
        var stub = new StubHandler();
        var counting = new CountingHandler();
        var services = new ServiceCollection();
        services.ConfigureHttpClientDefaults(builder => builder.AddStandardResilienceHandler());
        services.AddTechStrapClient(options =>
        {
            options.BaseAddress = ClientFixture.BaseAddress;
            options.ApiKey = ClientFixture.ApiKey;
            options.RetryBaseDelay = TimeSpan.FromMilliseconds(1);
            options.MaxRetryDelay = TimeSpan.FromMilliseconds(1);
        });
        services.AddHttpClient(TechStrapClientDefaults.HttpClientName)
            .AddHttpMessageHandler(() => counting)
            .ConfigurePrimaryHttpMessageHandler(() => stub);
        return (services.BuildServiceProvider(), stub, counting);
    }

    [Fact]
    public async Task A_host_default_handler_does_not_retry_a_submit_that_has_no_key()
    {
        var (provider, stub, _) = Build();
        using var _ = provider;
        stub.Respond(HttpStatusCode.ServiceUnavailable, times: 5);

        var result = await provider.GetRequiredService<ITechStrapClient>().SubmitTicketOnceAsync(ClientFixture.Request(), Xunit.TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        stub.Requests.Count.ShouldBe(1);
        stub.Requests[0].Header(HeaderNames.IdempotencyKey).ShouldBeNull();
    }

    [Fact]
    public async Task A_host_default_handler_does_not_retry_a_keyed_500()
    {
        var (provider, stub, _) = Build();
        using var _ = provider;
        stub.Respond(HttpStatusCode.InternalServerError, times: 5);

        var result = await provider.GetRequiredService<ITechStrapClient>().SubmitTicketAsync(ClientFixture.Request(), Xunit.TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        stub.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public void The_resolved_chain_has_the_key_handler_and_no_host_default_handler()
    {
        var (provider, _, counting) = Build();
        using var _ = provider;
        var builder = provider.GetRequiredService<HttpMessageHandlerBuilder>();
        builder.Name = TechStrapClientDefaults.HttpClientName;
        foreach (var action in provider.GetRequiredService<IOptionsMonitor<HttpClientFactoryOptions>>().Get(TechStrapClientDefaults.HttpClientName).HttpMessageHandlerBuilderActions)
        {
            action(builder);
        }

        var handlers = builder.AdditionalHandlers;

        handlers.OfType<ApiKeyHandler>().ShouldHaveSingleItem();
        handlers.Any(handler => handler.GetType().Name == "ResilienceHandler").ShouldBeFalse();
        handlers.ShouldContain(counting);
        handlers.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_handler_registered_on_the_named_client_after_the_registration_still_sees_requests()
    {
        var (provider, stub, counting) = Build();
        using var _ = provider;
        stub.Created();

        var result = await provider.GetRequiredService<ITechStrapClient>().SubmitTicketAsync(ClientFixture.Request(), Xunit.TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        counting.Seen.ShouldBe(1);
    }
}
