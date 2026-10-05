using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using TechStrap.Admin.Components.Layout;
using TechStrap.Admin.Components.Ui;
using TechStrap.Admin.Features.Shell;
using TechStrap.Admin.Tests.Support;

namespace TechStrap.Admin.Tests.Components;

/// <summary>
/// Review Focus 5, local time. The zone comes from the browser once per circuit; the prerender and a circuit that has no zone show UTC; an unknown zone is UTC; the offset belongs to the instant
/// (daylight saving); the machine-readable <c>datetime</c> stays UTC.
/// </summary>
public sealed class LocalTimeServiceTests : AdminComponentTest
{
    private LocalTimeService Service => Services.GetRequiredService<LocalTimeService>();

    private void BrowserZone(string? zone) => Tz.Setup<string?>("zone", _ => true).SetResult(zone);

    [Fact]
    public void Before_the_zone_is_loaded_the_service_is_utc()
    {
        Service.Zone.ShouldBe(TimeZoneInfo.Utc);
    }

    [Fact]
    public async Task Loading_reads_the_browser_zone_once_and_announces_it()
    {
        BrowserZone("Europe/London");
        var service = Service;
        var changes = 0;
        service.Changed += () => changes++;

        await Task.WhenAll(service.LoadAsync(), service.LoadAsync());
        await service.LoadAsync();

        service.Zone.Id.ShouldBe("Europe/London");
        Tz.VerifyInvoke("zone", 1);
        changes.ShouldBe(1);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Mars/Olympus_Mons")]
    [InlineData("../../etc/passwd")]
    [InlineData("Europe\\London")]
    [InlineData("Europe/London; DROP")]
    [InlineData("Europe/London\n")]
    [InlineData("Eastern Standard Time")]
    public async Task A_missing_unknown_or_oddly_shaped_zone_is_utc(string? zone)
    {
        BrowserZone(zone);
        var service = Service;

        await service.LoadAsync();

        service.Zone.ShouldBe(TimeZoneInfo.Utc);
    }

    [Fact]
    public async Task A_zone_name_longer_than_any_iana_name_is_utc()
    {
        BrowserZone("Europe/" + new string('a', 80));
        var service = Service;

        await service.LoadAsync();

        service.Zone.ShouldBe(TimeZoneInfo.Utc);
    }

    [Fact]
    public async Task A_script_that_fails_leaves_utc_and_never_throws()
    {
        Tz.Setup<string?>("zone", _ => true).SetException(new JSException("no Intl"));
        var service = Service;

        await service.LoadAsync();

        service.Zone.ShouldBe(TimeZoneInfo.Utc);
    }

    [Fact]
    public async Task A_disconnected_circuit_is_not_an_error()
    {
        Tz.Setup<string?>("zone", _ => true).SetException(new JSDisconnectedException("The circuit is gone."));
        var service = Service;

        await service.LoadAsync();

        service.Zone.ShouldBe(TimeZoneInfo.Utc);
    }

    [Theory]
    [InlineData("Europe/London", "2026-01-15T12:00:00Z", "2026-01-15 12:00 Europe/London, 2026-01-15 12:00 UTC")]
    [InlineData("Europe/London", "2026-07-15T12:00:00Z", "2026-07-15 13:00 Europe/London, 2026-07-15 12:00 UTC")]
    [InlineData("Europe/London", "2026-03-29T00:59:00Z", "2026-03-29 00:59 Europe/London, 2026-03-29 00:59 UTC")]
    [InlineData("Europe/London", "2026-03-29T01:00:00Z", "2026-03-29 02:00 Europe/London, 2026-03-29 01:00 UTC")]
    [InlineData("Europe/London", "2026-10-25T00:59:00Z", "2026-10-25 01:59 Europe/London, 2026-10-25 00:59 UTC")]
    [InlineData("Europe/London", "2026-10-25T01:00:00Z", "2026-10-25 01:00 Europe/London, 2026-10-25 01:00 UTC")]
    [InlineData("America/New_York", "2026-03-08T06:59:00Z", "2026-03-08 01:59 America/New_York, 2026-03-08 06:59 UTC")]
    [InlineData("America/New_York", "2026-03-08T07:00:00Z", "2026-03-08 03:00 America/New_York, 2026-03-08 07:00 UTC")]
    [InlineData("America/New_York", "2026-11-01T05:30:00Z", "2026-11-01 01:30 America/New_York, 2026-11-01 05:30 UTC")]
    [InlineData("America/New_York", "2026-11-01T06:30:00Z", "2026-11-01 01:30 America/New_York, 2026-11-01 06:30 UTC")]
    [InlineData("Asia/Kolkata", "2026-10-04T11:55:00Z", "2026-10-04 17:25 Asia/Kolkata, 2026-10-04 11:55 UTC")]
    [InlineData("Etc/UTC", "2026-10-04T11:55:00Z", "2026-10-04 11:55 UTC")]
    public async Task The_offset_belongs_to_the_instant_so_daylight_saving_is_the_zones_own(string zone, string utc, string expected)
    {
        BrowserZone(zone);
        var service = Service;
        await service.LoadAsync();

        var when = DateTimeOffset.Parse(utc, System.Globalization.CultureInfo.InvariantCulture);

        TicketDisplay.Absolute(when, service.Zone).ShouldBe(expected);
    }

    [Fact]
    public async Task The_two_one_thirties_of_the_new_york_fall_back_are_two_different_instants_in_the_cell()
    {
        BrowserZone("America/New_York");
        var first = new DateTimeOffset(2026, 11, 1, 5, 30, 0, TimeSpan.Zero);
        var cut = Render<RelativeTime>(p => p.Add(c => c.When, first));
        await cut.InvokeAsync(() => Service.LoadAsync());
        var second = Render<RelativeTime>(p => p.Add(c => c.When, first.AddHours(1)));

        cut.Find("time").GetAttribute("title").ShouldBe("2026-11-01 01:30 America/New_York, 2026-11-01 05:30 UTC");
        second.Find("time").GetAttribute("title").ShouldBe("2026-11-01 01:30 America/New_York, 2026-11-01 06:30 UTC");
        cut.Find("time").GetAttribute("datetime").ShouldNotBe(second.Find("time").GetAttribute("datetime"));
    }

    [Fact]
    public async Task The_layout_asks_the_browser_for_the_zone_once_and_after_the_preferences()
    {
        this.AddAgentShell();
        var order = new List<string>();
        Preferences.Setup<StoredPreferences>("load", _ =>
        {
            order.Add("preferences");
            return true;
        }).SetResult(new StoredPreferences(SingleKeyShortcuts: true, Theme: "auto"));
        Tz.Setup<string?>("zone", _ =>
        {
            order.Add("zone");
            return true;
        }).SetResult("Europe/London");

        var cut = Render<MainLayout>(p => p.SignedIn().Add(l => l.Body, (RenderFragment)(b => { })));
        await cut.InvokeAsync(() => Task.CompletedTask);

        Tz.VerifyInvoke("zone", 1);
        order.ShouldBe(["preferences", "zone"]);
        Service.Zone.Id.ShouldBe("Europe/London");
    }

    [Fact]
    public async Task A_time_cell_shows_utc_until_the_zone_arrives_then_draws_again_in_local_time()
    {
        // 23:30 UTC on 1 July is half past eleven the next morning in Auckland (NZST, UTC+12): the date is the one that moves.
        var when = new DateTimeOffset(2026, 7, 1, 23, 30, 0, TimeSpan.Zero);
        BrowserZone("Pacific/Auckland");
        var cut = Render<RelativeTime>(p => p.Add(c => c.When, when));
        var time = cut.Find("time");
        time.TextContent.ShouldBe("2026-07-01");
        time.GetAttribute("title").ShouldBe("2026-07-01 23:30 UTC");

        await cut.InvokeAsync(() => Service.LoadAsync());

        cut.WaitForAssertion(() => cut.Find("time").TextContent.ShouldBe("2026-07-02"));
        cut.Find("time").GetAttribute("title").ShouldBe("2026-07-02 11:30 Pacific/Auckland, 2026-07-01 23:30 UTC");
        cut.Find("time").GetAttribute("datetime").ShouldBe("2026-07-01T23:30:00.0000000Z");
    }

    [Fact]
    public async Task A_message_in_the_timeline_follows_the_zone_too()
    {
        var when = new DateTimeOffset(2026, 7, 1, 23, 30, 0, TimeSpan.Zero);
        BrowserZone("Europe/London");
        var cut = Render<TintedEntry>(p => p
            .Add(e => e.Kind, EntryKind.PublicReply)
            .Add(e => e.Author, "Sam")
            .Add(e => e.When, when));
        cut.Find(".ts-entry-time time").TextContent.ShouldBe("2026-07-01");

        await cut.InvokeAsync(() => Service.LoadAsync());

        cut.WaitForAssertion(() => cut.Find(".ts-entry-time time").TextContent.ShouldBe("2026-07-02"));
        cut.Find(".ts-entry-time time").GetAttribute("title")!.ShouldContain("UTC");
    }
}
