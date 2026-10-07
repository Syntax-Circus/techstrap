using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TechStrap.Admin.Features.Live;
using TechStrap.Admin.Options;

namespace TechStrap.Admin.Tests.Live;

/// <summary>The kill switch <c>LiveUpdates:Enabled</c>: on by default; off, the Admin gets a client that does nothing and offers no connection, so nothing live is drawn.</summary>
public sealed class LiveRegistrationTests
{
    [Fact(Timeout = 30000)]
    public async Task The_default_is_the_real_client_and_each_scope_gets_its_own()
    {
        Xunit.TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var factory = new AdminFactory();
        await using var first = factory.Services.CreateAsyncScope();
        await using var second = factory.Services.CreateAsyncScope();

        var one = first.ServiceProvider.GetRequiredService<ITicketLiveClient>();

        one.ShouldBeOfType<SignalRTicketLiveClient>();
        one.IsEnabled.ShouldBeTrue();
        first.ServiceProvider.GetRequiredService<ITicketLiveClient>().ShouldBeSameAs(one);
        second.ServiceProvider.GetRequiredService<ITicketLiveClient>().ShouldNotBeSameAs(one);
        Should.Throw<InvalidOperationException>(() => factory.Services.GetService<ITicketLiveClient>()).Message.ShouldContain("scoped", Case.Insensitive);
    }

    [Fact(Timeout = 30000)]
    public async Task Switched_off_the_admin_gets_the_null_client()
    {
        Xunit.TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        using var factory = new AdminFactory(settings: new Dictionary<string, string?> { ["LiveUpdates:Enabled"] = "false" });
        await using var scope = factory.Services.CreateAsyncScope();

        var client = scope.ServiceProvider.GetRequiredService<ITicketLiveClient>();

        client.ShouldBeOfType<NullTicketLiveClient>();
        client.IsEnabled.ShouldBeFalse();
        client.State.ShouldBe(LiveConnectionState.Disconnected);
    }

    [Fact(Timeout = 30000)]
    public async Task The_null_client_does_nothing_and_never_throws()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var client = new NullTicketLiveClient();
        var raised = 0;
        client.StateChanged += _ => raised++;
        client.TicketChanged += _ => raised++;
        client.PresenceChanged += _ => raised++;

        await client.StartAsync(ct);
        (await client.JoinTicketAsync(Guid.NewGuid(), ct)).ShouldBeNull();
        await client.LeaveTicketAsync(Guid.NewGuid(), ct);
        await client.SetComposingAsync(Guid.NewGuid(), true, ct);

        raised.ShouldBe(0);
        client.State.ShouldBe(LiveConnectionState.Disconnected);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("True", true)]
    [InlineData("false", false)]
    [InlineData("FALSE", false)]
    public void The_switch_reads_a_boolean_in_any_case(string value, bool expected)
    {
        using var factory = new AdminFactory(settings: new Dictionary<string, string?> { ["LiveUpdates:Enabled"] = value });

        factory.Services.GetRequiredService<IOptions<LiveUpdatesOptions>>().Value.Enabled.ShouldBe(expected);
    }

    [Fact]
    public void The_switch_is_on_when_the_setting_is_absent()
    {
        new LiveUpdatesOptions().Enabled.ShouldBeTrue();
        LiveUpdatesOptions.SectionName.ShouldBe("LiveUpdates");
    }

    [Fact]
    public void A_value_that_is_not_a_boolean_stops_the_start()
    {
        var failure = Should.Throw<Exception>(() =>
        {
            using var factory = new AdminFactory(settings: new Dictionary<string, string?> { ["LiveUpdates:Enabled"] = "maybe" });
            _ = factory.Services;
        });

        failure.ToString().ShouldContain("LiveUpdates");
    }

    [Fact]
    public void The_hub_address_is_the_api_base_address_plus_the_hub_path_even_under_a_base_path()
    {
        HubLiveConnectionFactory.HubUri("http://api/").ToString().ShouldBe("http://api/hubs/tickets");
        HubLiveConnectionFactory.HubUri("http://api").ToString().ShouldBe("http://api/hubs/tickets");
        HubLiveConnectionFactory.HubUri("https://example.test/techstrap/api/").ToString().ShouldBe("https://example.test/techstrap/api/hubs/tickets");
    }
}
