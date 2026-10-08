using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using TechStrap.Contracts.Intake;

namespace TechStrap.Client.Tests.Infrastructure;

/// <summary>Builds the real registration with a stub as the primary handler and 1 ms retry delays. Pass <c>fakeTime</c> to run the pipeline on a <see cref="FakeTimeProvider"/>.</summary>
internal sealed class ClientFixture : IDisposable
{
    public const string ApiKey = "sk_live_0123456789abcdef";
    public static readonly Uri BaseAddress = new("https://support.example.com/");

    private readonly ServiceProvider _provider;

    public ClientFixture(Action<TechStrapClientOptions>? configure = null, bool fakeTime = false)
    {
        var services = new ServiceCollection();
        if (fakeTime)
        {
            Time = new FakeTimeProvider();
            services.AddSingleton<TimeProvider>(Time);
        }

        services.AddTechStrapClient(options =>
        {
            options.BaseAddress = BaseAddress;
            options.ApiKey = ApiKey;
            options.RetryBaseDelay = TimeSpan.FromMilliseconds(1);
            options.MaxRetryDelay = TimeSpan.FromMilliseconds(1);
            configure?.Invoke(options);
        });
        services.AddHttpClient(TechStrapClientDefaults.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => Stub);
        _provider = services.BuildServiceProvider();
    }

    public StubHandler Stub { get; } = new();

    public FakeTimeProvider? Time { get; }

    public ITechStrapClient Client => _provider.GetRequiredService<ITechStrapClient>();

    public static SubmitTicketRequest Request() => new("ada@example.com", "Ada", "Cannot log in", "The login page returns an error.", null, null);

    public void Dispose() => _provider.Dispose();
}
