using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TechStrap.Tests.Shared;

namespace TechStrap.Portal.Tests;

/// <summary>
/// <c>WebApplicationFactory</c> lets the app's own thread run on to <c>app.Run()</c> while the test's thread finishes building the host. These tests make the test's thread lose that race every time, which a
/// loaded CI runner does now and then.
/// </summary>
public sealed class LoopbackKestrelTests
{
    [Fact]
    public async Task The_host_starts_when_the_app_is_already_running_before_the_factory_continues()
    {
        await using var factory = new AppStartsFirstFactory();
        factory.UseLoopbackKestrel();

        factory.StartServer();

        var address = new Uri(factory.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single());
        address.Host.ShouldBe("127.0.0.1");
        address.Port.ShouldBeGreaterThan(0);
        using var client = factory.CreateClient();
        client.BaseAddress!.Port.ShouldBe(address.Port);
        using var response = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);
        response.IsSuccessStatusCode.ShouldBeTrue();
    }

    private sealed class AppStartsFirstFactory : PortalFactory
    {
        protected override IHost CreateHost(IHostBuilder builder) => base.CreateHost(new WaitsForTheAppToStart(builder));
    }

    /// <summary>Does not give the built host back until the app has started it.</summary>
    private sealed class WaitsForTheAppToStart(IHostBuilder inner) : IHostBuilder
    {
        public IDictionary<object, object> Properties => inner.Properties;

        public IHost Build()
        {
            var host = inner.Build();
            var started = host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStarted;
            started.WaitHandle.WaitOne(TimeSpan.FromSeconds(30)).ShouldBeTrue("the app never started its own host");
            return host;
        }

        public IHostBuilder ConfigureHostConfiguration(Action<Microsoft.Extensions.Configuration.IConfigurationBuilder> configureDelegate)
        {
            inner.ConfigureHostConfiguration(configureDelegate);
            return this;
        }

        public IHostBuilder ConfigureAppConfiguration(Action<HostBuilderContext, Microsoft.Extensions.Configuration.IConfigurationBuilder> configureDelegate)
        {
            inner.ConfigureAppConfiguration(configureDelegate);
            return this;
        }

        public IHostBuilder ConfigureServices(Action<HostBuilderContext, IServiceCollection> configureDelegate)
        {
            inner.ConfigureServices(configureDelegate);
            return this;
        }

        public IHostBuilder UseServiceProviderFactory<TContainerBuilder>(IServiceProviderFactory<TContainerBuilder> factory)
            where TContainerBuilder : notnull
        {
            inner.UseServiceProviderFactory(factory);
            return this;
        }

        public IHostBuilder UseServiceProviderFactory<TContainerBuilder>(Func<HostBuilderContext, IServiceProviderFactory<TContainerBuilder>> factory)
            where TContainerBuilder : notnull
        {
            inner.UseServiceProviderFactory(factory);
            return this;
        }

        public IHostBuilder ConfigureContainer<TContainerBuilder>(Action<HostBuilderContext, TContainerBuilder> configureDelegate)
        {
            inner.ConfigureContainer(configureDelegate);
            return this;
        }
    }
}
