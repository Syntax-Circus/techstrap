using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;

namespace TechStrap.Client.Tests.Registration;

public sealed class AddTechStrapClientTests
{
    private static ServiceProvider Build(Action<IServiceCollection> register)
    {
        var services = new ServiceCollection();
        register(services);
        return services.BuildServiceProvider();
    }

    private static ServiceProvider Valid() => Build(services => services.AddTechStrapClient(o =>
    {
        o.BaseAddress = new Uri("https://support.example.com/");
        o.ApiKey = "sk_live_0123456789abcdef";
    }));

    private static List<HttpMessageHandler> Chain(ServiceProvider provider)
    {
        var chain = new List<HttpMessageHandler>();
        var handler = provider.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler(TechStrapClientDefaults.HttpClientName);
        while (handler is not null)
        {
            chain.Add(handler);
            handler = (handler as DelegatingHandler)?.InnerHandler;
        }

        return chain;
    }

    [Fact]
    public void The_delegate_overload_configures_the_options()
    {
        using var provider = Valid();

        var options = provider.GetRequiredService<IOptions<TechStrapClientOptions>>().Value;

        options.BaseAddress.ShouldBe(new Uri("https://support.example.com/"));
        options.MaxAttempts.ShouldBe(3);
    }

    [Fact]
    public void The_configuration_overload_binds_all_six_keys()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["BaseAddress"] = "https://support.example.com/",
            ["ApiKey"] = "sk_live_0123456789abcdef",
            ["Timeout"] = "00:01:00",
            ["MaxAttempts"] = "5",
            ["RetryBaseDelay"] = "00:00:01",
            ["MaxRetryDelay"] = "00:00:10",
        }).Build();
        using var provider = Build(services => services.AddTechStrapClient(configuration));

        var options = provider.GetRequiredService<IOptions<TechStrapClientOptions>>().Value;

        options.BaseAddress.ShouldBe(new Uri("https://support.example.com/"));
        options.ApiKey.ShouldBe("sk_live_0123456789abcdef");
        options.Timeout.ShouldBe(TimeSpan.FromMinutes(1));
        options.MaxAttempts.ShouldBe(5);
        options.RetryBaseDelay.ShouldBe(TimeSpan.FromSeconds(1));
        options.MaxRetryDelay.ShouldBe(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void Missing_configuration_keys_keep_their_defaults_and_a_junk_address_is_reported_by_the_validator()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["BaseAddress"] = "not a url",
            ["ApiKey"] = "sk_live_0123456789abcdef",
        }).Build();
        using var provider = Build(services => services.AddTechStrapClient(configuration));

        var failure = Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IOptions<TechStrapClientOptions>>().Value);

        failure.Message.ShouldContain("BaseAddress");
        failure.Message.ShouldNotContain("sk_live_0123456789abcdef");
    }

    [Fact]
    public void Invalid_options_fail_on_first_access_not_at_registration()
    {
        using var provider = Build(services => services.AddTechStrapClient(_ => { }));

        var failure = Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IOptions<TechStrapClientOptions>>().Value);

        failure.Message.ShouldContain("BaseAddress");
        failure.Message.ShouldContain("ApiKey");
    }

    [Fact]
    public void Registration_has_no_logging_handler_on_the_named_client()
    {
        using var provider = Valid();

        var names = Chain(provider).Select(handler => handler.GetType().Name).ToList();

        names.ShouldContain(nameof(ApiKeyHandler));
        names.ShouldNotContain(name => name.StartsWith("Logging", StringComparison.Ordinal));
    }

    [Fact]
    public void Primary_handler_does_not_follow_redirects()
    {
        using var provider = Valid();

        var primary = Chain(provider)[^1].ShouldBeOfType<SocketsHttpHandler>();

        primary.AllowAutoRedirect.ShouldBeFalse();
    }

    [Fact]
    public void The_named_client_has_the_base_address_and_no_client_side_timeout()
    {
        using var provider = Valid();

        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(TechStrapClientDefaults.HttpClientName);

        client.BaseAddress.ShouldBe(new Uri("https://support.example.com/"));
        client.Timeout.ShouldBe(Timeout.InfiniteTimeSpan);
    }

    [Fact]
    public void Calling_it_twice_registers_one_handler_and_one_validator()
    {
        var services = new ServiceCollection();
        services.AddTechStrapClient(o => o.BaseAddress = new Uri("https://support.example.com/"));
        services.AddTechStrapClient(o => o.ApiKey = "sk_live_0123456789abcdef");
        using var provider = services.BuildServiceProvider();

        services.Count(d => d.ServiceType == typeof(IValidateOptions<TechStrapClientOptions>)).ShouldBe(1);
        Chain(provider).Count(handler => handler is ApiKeyHandler).ShouldBe(1);
        provider.GetRequiredService<IOptions<TechStrapClientOptions>>().Value.ApiKey.ShouldBe("sk_live_0123456789abcdef");
    }

    [Fact]
    public void It_returns_the_service_collection_for_chaining()
    {
        var services = new ServiceCollection();

        services.AddTechStrapClient(_ => { }).ShouldBeSameAs(services);
    }
}
