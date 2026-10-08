using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Networking;
using NSubstitute;
using TechStrap.Client.Maui.Tests.Infrastructure;

namespace TechStrap.Client.Maui.Tests.Registration;

public sealed class AddTechStrapMauiTests
{
    private static void ConfigureClient(TechStrapClientOptions o)
    {
        o.BaseAddress = new Uri("https://h/");
        o.ApiKey = "k";
    }

    private static ServiceCollection WithFakes(EssentialsFakes? fakes = null)
    {
        fakes ??= EssentialsFakes.Default();
        var services = new ServiceCollection();
        services.AddSingleton(fakes.AppInfo);
        services.AddSingleton(fakes.DeviceInfo);
        services.AddSingleton(fakes.Connectivity);
        services.AddSingleton(fakes.Display);
        services.AddSingleton(fakes.Battery);
        return services;
    }

    [Fact]
    public void AddTechStrapMaui_resolves_the_full_graph()
    {
        var services = WithFakes();
        services.AddTechStrapMaui(ConfigureClient);
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IMauiTicketSubmitter>().ShouldNotBeNull();
        provider.GetRequiredService<IDeviceContextCollector>().ShouldBeOfType<MauiDeviceContextCollector>();
        provider.GetRequiredService<ITechStrapClient>().ShouldNotBeNull();
    }

    [Fact]
    public void An_app_registered_IDeviceInfo_wins()
    {
        var fakes = EssentialsFakes.Default();
        var services = WithFakes(fakes);
        services.AddTechStrapMaui(ConfigureClient);
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IDeviceInfo>().ShouldBeSameAs(fakes.DeviceInfo);
    }

    [Fact]
    public void An_app_registered_collector_wins()
    {
        var custom = Substitute.For<IDeviceContextCollector>();
        var services = new ServiceCollection();
        services.AddSingleton(custom);
        services.AddTechStrapMaui(ConfigureClient);
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IDeviceContextCollector>().ShouldBeSameAs(custom);
    }

    [Fact]
    public void Defaults_are_registered_lazily()
    {
        var services = new ServiceCollection();
        services.AddTechStrapMaui(ConfigureClient);

        // Each default is a factory: registering never captured a static instance.
        foreach (var type in new[] { typeof(IAppInfo), typeof(IDeviceInfo), typeof(IConnectivity), typeof(IDeviceDisplay), typeof(IBattery) })
        {
            var descriptor = services.Single(d => d.ServiceType == type);
            descriptor.ImplementationInstance.ShouldBeNull();
            descriptor.ImplementationFactory.ShouldNotBeNull();
        }

        using var provider = services.BuildServiceProvider();

        // Resolving the collector builds it from the static defaults without reading a value.
        provider.GetRequiredService<IDeviceContextCollector>().ShouldNotBeNull();

        // Reading a value on plain net10.0 hits the reference assembly: the failure shows registration never read it.
        var deviceInfo = provider.GetRequiredService<IDeviceInfo>();
        var thrown = Should.Throw<Exception>(() => _ = deviceInfo.Model);
        thrown.GetType().Name.ShouldContain("NotImplementedInReferenceAssembly");
    }

    [Fact]
    public void AddTechStrapMaui_is_idempotent()
    {
        var services = WithFakes();
        services.AddTechStrapMaui(ConfigureClient);
        services.AddTechStrapMaui(ConfigureClient);

        services.Count(d => d.ServiceType == typeof(IMauiTicketSubmitter)).ShouldBe(1);
        services.Count(d => d.ServiceType == typeof(IDeviceContextCollector)).ShouldBe(1);
        services.Count(d => d.ServiceType == typeof(ITechStrapClient)).ShouldBe(1);
    }

    [Fact]
    public void The_overload_without_client_options_requires_AddTechStrapClient()
    {
        var services = WithFakes();
        services.AddTechStrapMaui();
        using var provider = services.BuildServiceProvider();

        var thrown = Should.Throw<InvalidOperationException>(() => provider.GetRequiredService<IMauiTicketSubmitter>());
        thrown.Message.ShouldContain("AddTechStrapClient");
    }

    [Fact]
    public void The_overload_without_client_options_works_after_AddTechStrapClient()
    {
        var services = WithFakes();
        services.AddTechStrapClient(ConfigureClient);
        services.AddTechStrapMaui();
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IMauiTicketSubmitter>().ShouldNotBeNull();
    }

    [Fact]
    public void Options_configure_is_applied()
    {
        var services = WithFakes();
        services.AddTechStrapMaui(ConfigureClient, o => o.IncludeDisplay = true);
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOptions<DeviceContextOptions>>().Value.IncludeDisplay.ShouldBeTrue();
    }

    [Fact]
    public void Options_configure_is_applied_on_the_overload_without_client_options()
    {
        var services = WithFakes();
        services.AddTechStrapMaui(o => o.IncludeBattery = true);
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOptions<DeviceContextOptions>>().Value.IncludeBattery.ShouldBeTrue();
    }
}
