using NSubstitute;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace TechStrap.Client.Tests.Registration;

public sealed class ClientRegistrationTests
{
    private const string Key = "sk_live_0123456789abcdef";

    private static IConfiguration Config(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value))).Build();

    [Fact]
    public void The_client_is_a_singleton()
    {
        var services = new ServiceCollection();
        services.AddTechStrapClient(o =>
        {
            o.BaseAddress = new Uri("https://support.example.com/");
            o.ApiKey = Key;
        });
        using var provider = services.BuildServiceProvider();

        var descriptor = services.Single(d => d.ServiceType == typeof(ITechStrapClient));

        descriptor.Lifetime.ShouldBe(ServiceLifetime.Singleton);
        provider.GetRequiredService<ITechStrapClient>().ShouldBeSameAs(provider.GetRequiredService<ITechStrapClient>());
    }

    [Fact]
    public void Calling_it_twice_registers_the_client_and_the_time_provider_once()
    {
        var services = new ServiceCollection();
        services.AddTechStrapClient(_ => { });
        services.AddTechStrapClient(_ => { });

        services.Count(d => d.ServiceType == typeof(ITechStrapClient)).ShouldBe(1);
        services.Count(d => d.ServiceType == typeof(TimeProvider)).ShouldBe(1);
    }

    [Fact]
    public void A_time_provider_registered_by_the_host_is_kept()
    {
        var services = new ServiceCollection();
        var custom = Substitute.For<TimeProvider>();
        services.AddSingleton(custom);
        services.AddTechStrapClient(_ => { });
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<TimeProvider>().ShouldBeSameAs(custom);
    }

    [Fact]
    public void A_client_the_host_registered_first_wins()
    {
        var services = new ServiceCollection();
        var fake = Substitute.For<ITechStrapClient>();
        services.AddSingleton(fake);
        services.AddTechStrapClient(_ => { });
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<ITechStrapClient>().ShouldBeSameAs(fake);
    }

    [Fact]
    public void The_named_client_caps_the_buffered_response_size()
    {
        var services = new ServiceCollection();
        services.AddTechStrapClient(o =>
        {
            o.BaseAddress = new Uri("https://support.example.com/");
            o.ApiKey = Key;
        });
        using var provider = services.BuildServiceProvider();

        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(TechStrapClientDefaults.HttpClientName);

        client.MaxResponseContentBufferSize.ShouldBe(TechStrapClientDefaults.MaxResponseBytes);
    }

    [Fact]
    public void The_configuration_overload_registers_the_client_too()
    {
        var configuration = Config(("BaseAddress", "https://support.example.com/"), ("ApiKey", Key));
        var services = new ServiceCollection();
        services.AddTechStrapClient(configuration);
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<ITechStrapClient>().ShouldNotBeNull();
    }

    [Theory]
    [InlineData("Timeout", "30s")]
    [InlineData("MaxAttempts", "three")]
    [InlineData("RetryBaseDelay", "half a second")]
    [InlineData("MaxRetryDelay", "5s")]
    public void An_unparsable_setting_fails_loudly_at_registration_and_names_the_key(string name, string value)
    {
        var configuration = Config(("BaseAddress", "https://support.example.com/"), ("ApiKey", Key), (name, value));

        var failure = Should.Throw<OptionsValidationException>(() => new ServiceCollection().AddTechStrapClient(configuration));

        failure.Message.ShouldContain(name);
        failure.Message.ShouldNotContain(Key);
    }

    [Theory]
    [InlineData("/just/a/path")]
    [InlineData("not a uri")]
    public void An_unparsable_base_address_fails_at_registration_and_names_the_key_not_the_value(string value)
    {
        var configuration = Config(("BaseAddress", value), ("ApiKey", Key));

        var failure = Should.Throw<OptionsValidationException>(() => new ServiceCollection().AddTechStrapClient(configuration));

        failure.Message.ShouldContain("BaseAddress");
        failure.Message.ShouldNotContain(value);
        failure.Message.ShouldNotContain(Key);
    }
}
