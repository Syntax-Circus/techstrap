using Bunit;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Admin.Auth;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Account;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.Agents;
using TechStrap.Contracts.Products;

namespace TechStrap.Admin.Tests.Components;

/// <summary>My settings, opened by a plain agent: alert toggles that always send the full set, the keyboard switch and the theme (kept in the browser), and the public display name.</summary>
public sealed class NotificationPreferencesPageTests : AdminPageTest
{
    private readonly IAgentsClient _agents = Substitute.For<IAgentsClient>();
    private readonly IProductsClient _products = Substitute.For<IProductsClient>();
    private readonly Guid _orbitly = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private readonly Guid _nimbus = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");
    private readonly Guid _acme = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000003");

    public NotificationPreferencesPageTests()
    {
        AsAgent();
        _agents.GetNotificationPreferencesAsync(Arg.Any<CancellationToken>()).Returns(_ => TestData.Ok<IReadOnlyList<NotificationPreferenceDto>>(
        [
            new NotificationPreferenceDto(_orbitly, "Orbitly", false),
            new NotificationPreferenceDto(_nimbus, "Nimbus", true),
            new NotificationPreferenceDto(_acme, "Acme", false),
        ]));
        _agents.UpdateNotificationPreferencesAsync(Arg.Any<UpdateNotificationPreferencesRequest>(), Arg.Any<CancellationToken>()).Returns(TestData.Ok());
        _products.ListAsync(Arg.Any<CancellationToken>()).Returns(TestData.Ok<IReadOnlyList<ProductDto>>(
        [
            TestData.ProductDetail("Orbitly", active: false),
            TestData.ProductDetail("Nimbus", id: _nimbus, key: "nimbus", prefix: "NIM") with { Branding = TestData.ProductDetail("Nimbus Cloud").Branding },
        ]));
        Services.AddSingleton(_agents);
        Services.AddSingleton(_products);
    }

    protected override AgentSession CreateSession(bool admin)
    {
        _agents.GetMeAsync(Arg.Any<CancellationToken>()).Returns(_ => TestData.Ok(TestData.Me(admin)));
        var session = new AgentSession(_agents);
        session.EnsureLoadedAsync(CancellationToken.None).GetAwaiter().GetResult();
        return session;
    }

    private IReadOnlyList<UpdateNotificationPreferencesRequest> Saves() =>
        [.. _agents.ReceivedCalls().Where(c => c.GetMethodInfo().Name == nameof(IAgentsClient.UpdateNotificationPreferencesAsync)).Select(c => (UpdateNotificationPreferencesRequest)c.GetArguments()[0]!)];

    private static AngleSharp.Dom.IElement Toggle(IRenderedComponent<NotificationPreferencesPage> cut, string product) =>
        cut.FindAll(".ts-toggle-list li").Single(li => li.TextContent.Contains($"New tickets in {product}")).QuerySelector("input")!;

    private static string? Revision(IRenderedComponent<NotificationPreferencesPage> cut) =>
        cut.FindAll(".ts-toggle-list li").Select(li => li.GetAttribute("data-revision")).Distinct().Single();

    private static bool IsOn(AngleSharp.Dom.IElement toggle) => toggle.HasAttribute("checked");

    // ---- a plain agent can use it --------------------------------------------------------------------------------

    [Fact]
    public void A_plain_agent_gets_the_page_not_the_no_access_page()
    {
        var cut = Render<NotificationPreferencesPage>();

        cut.Find("h1").TextContent.ShouldBe("My settings");
        cut.FindAll("section.ts-no-access").ShouldBeEmpty();
        Session.IsAdmin.ShouldBeFalse();
    }

    // ---- alerts: the full set ------------------------------------------------------------------------------------

    [Fact]
    public void Each_active_product_has_a_toggle_showing_what_is_saved()
    {
        var cut = Render<NotificationPreferencesPage>();

        IsOn(Toggle(cut, "Orbitly")).ShouldBeFalse();
        IsOn(Toggle(cut, "Nimbus")).ShouldBeTrue();
        IsOn(Toggle(cut, "Acme")).ShouldBeFalse();
        cut.Find("#ts-alerts-heading").TextContent.ShouldBe("Email alerts");
    }

    [Fact]
    public void A_toggle_sends_the_full_set_of_products_with_that_one_changed_then_a_status_message()
    {
        var cut = Render<NotificationPreferencesPage>();

        Toggle(cut, "Orbitly").Change(true);

        var sent = Saves().ShouldHaveSingleItem();
        sent.Preferences.ShouldBe(
        [
            new NotificationPreferenceUpdateDto(_orbitly, true),
            new NotificationPreferenceUpdateDto(_nimbus, true),
            new NotificationPreferenceUpdateDto(_acme, false),
        ]);
        _agents.Received(1).UpdateNotificationPreferencesAsync(Arg.Any<UpdateNotificationPreferencesRequest>(), Arg.Is<CancellationToken>(t => !t.CanBeCanceled));
        StatusMessages.Current.ShouldBe("Saved your alert settings");
    }

    [Fact]
    public void A_second_toggle_carries_the_first_change_so_the_set_is_always_complete()
    {
        var cut = Render<NotificationPreferencesPage>();

        Toggle(cut, "Orbitly").Change(true);
        Toggle(cut, "Nimbus").Change(false);

        Saves()[1].Preferences.ShouldBe(
        [
            new NotificationPreferenceUpdateDto(_orbitly, true),
            new NotificationPreferenceUpdateDto(_nimbus, false),
            new NotificationPreferenceUpdateDto(_acme, false),
        ]);
    }

    [Fact]
    public void While_a_save_runs_every_toggle_is_inert_so_two_saves_never_overlap()
    {
        var gate = new TaskCompletionSource<Result>();
        _agents.UpdateNotificationPreferencesAsync(Arg.Any<UpdateNotificationPreferencesRequest>(), Arg.Any<CancellationToken>()).Returns(gate.Task);
        var cut = Render<NotificationPreferencesPage>();

        Toggle(cut, "Orbitly").Change(true);
        cut.FindAll(".ts-toggle-list input").ShouldAllBe(i => i.HasAttribute("disabled"));
        Toggle(cut, "Acme").Change(true);

        Saves().Count.ShouldBe(1);
        gate.SetResult(TestData.Ok());
        cut.WaitForAssertion(() => cut.FindAll(".ts-toggle-list input").ShouldAllBe(i => !i.HasAttribute("disabled")));
        IsOn(Toggle(cut, "Orbitly")).ShouldBeTrue();
    }

    [Fact]
    public void A_toggle_that_is_swallowed_while_a_save_runs_draws_the_checkbox_again_from_what_is_saved()
    {
        var gate = new TaskCompletionSource<Result>();
        _agents.UpdateNotificationPreferencesAsync(Arg.Any<UpdateNotificationPreferencesRequest>(), Arg.Any<CancellationToken>()).Returns(gate.Task);
        var cut = Render<NotificationPreferencesPage>();
        Toggle(cut, "Orbitly").Change(true);
        var before = Revision(cut);

        Toggle(cut, "Acme").Change(true);

        // The list items are keyed by this revision, and the page shows it as data-revision (as the ticket sidebar does). A new one makes Blazor throw the items away and build new ones,
        // so the checkbox the agent just ticked is drawn again from what is saved and the browser cannot keep a tick that was never saved.
        Revision(cut).ShouldNotBe(before);
        Saves().Count.ShouldBe(1);
        gate.SetResult(TestData.Ok());
        cut.WaitForAssertion(() => cut.FindAll(".ts-toggle-list input").ShouldAllBe(i => !i.HasAttribute("disabled")));
        IsOn(Toggle(cut, "Acme")).ShouldBeFalse();
    }

    [Fact]
    public void A_slow_first_read_of_the_alerts_never_replaces_the_answer_of_a_later_reload()
    {
        var older = new TaskCompletionSource<Result<IReadOnlyList<NotificationPreferenceDto>>>();
        var newer = new TaskCompletionSource<Result<IReadOnlyList<NotificationPreferenceDto>>>();
        _agents.GetNotificationPreferencesAsync(Arg.Any<CancellationToken>()).Returns(older.Task, newer.Task);
        var cut = Render<NotificationPreferencesPage>();
        _agents.Received(1).GetNotificationPreferencesAsync(Arg.Any<CancellationToken>());

        // A reload while the first read is still running (the retry button of the error state, or the reload after a lost answer).
        // (No button is on screen while a read runs, so the second read is started the way the retry button would.)
        _ = cut.InvokeAsync(() => (Task)typeof(NotificationPreferencesPage).GetMethod("LoadPreferencesAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(cut.Instance, null)!);
        _agents.Received(2).GetNotificationPreferencesAsync(Arg.Any<CancellationToken>());
        newer.SetResult(TestData.Ok<IReadOnlyList<NotificationPreferenceDto>>([new NotificationPreferenceDto(_nimbus, "Nimbus", true)]));
        cut.WaitForAssertion(() =>
        {
            cut.Render(); // forced: the second read was started outside the component's own event handling, so nothing else would redraw it
            cut.FindAll(".ts-toggle-list li").Count.ShouldBe(1);
        });
        older.SetResult(TestData.Ok<IReadOnlyList<NotificationPreferenceDto>>(
            [new NotificationPreferenceDto(_orbitly, "Orbitly", false), new NotificationPreferenceDto(_nimbus, "Nimbus", true), new NotificationPreferenceDto(_acme, "Acme", false)]));
        cut.Render();

        cut.FindAll(".ts-toggle-list li").Count.ShouldBe(1);
        cut.FindAll(".ts-loading").ShouldBeEmpty();
    }

    [Fact]
    public void A_failed_save_puts_the_toggle_back_says_nothing_changed_and_the_next_save_does_not_carry_the_failed_change()
    {
        _agents.UpdateNotificationPreferencesAsync(Arg.Any<UpdateNotificationPreferencesRequest>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Fail("boom", "The API refused."), TestData.Ok());
        var cut = Render<NotificationPreferencesPage>();

        Toggle(cut, "Orbitly").Change(true);

        cut.Find(".ts-conflict[role=alert]").TextContent.ShouldContain("Couldn't save your alert settings. Nothing was changed. The API refused.");
        IsOn(Toggle(cut, "Orbitly")).ShouldBeFalse();
        StatusMessages.Current.ShouldBeNull();

        Toggle(cut, "Acme").Change(true);

        Saves()[1].Preferences.ShouldBe(
        [
            new NotificationPreferenceUpdateDto(_orbitly, false),
            new NotificationPreferenceUpdateDto(_nimbus, true),
            new NotificationPreferenceUpdateDto(_acme, true),
        ]);
        cut.FindAll(".ts-conflict").ShouldBeEmpty();
    }

    [Fact]
    public void A_lost_answer_never_claims_nothing_changed_blocks_more_toggles_and_a_reload_shows_what_is_saved()
    {
        _agents.UpdateNotificationPreferencesAsync(Arg.Any<UpdateNotificationPreferencesRequest>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Fail(ApiErrorCodes.ApiTimeout, "TechStrap took too long to answer. Try again."));
        var cut = Render<NotificationPreferencesPage>();
        Toggle(cut, "Orbitly").Change(true);

        var alert = cut.Find(".ts-conflict[role=alert]");
        alert.TextContent.ShouldContain("Couldn't confirm that your alert settings were saved. Reload to see what is saved.");
        alert.TextContent.ShouldNotContain("Nothing was changed");
        cut.FindAll(".ts-toggle-list input").ShouldAllBe(i => i.HasAttribute("disabled"));

        alert.QuerySelector("button")!.Click();

        _agents.Received(2).GetNotificationPreferencesAsync(Arg.Any<CancellationToken>());
        cut.FindAll(".ts-conflict").ShouldBeEmpty();
        cut.FindAll(".ts-toggle-list input").ShouldAllBe(i => !i.HasAttribute("disabled"));
        Saves().Count.ShouldBe(1, "a write is never retried");
    }

    [Fact]
    public void A_failed_load_shows_the_api_message_and_retry_loads_again()
    {
        _agents.GetNotificationPreferencesAsync(Arg.Any<CancellationToken>()).Returns(
            TestData.Fail<IReadOnlyList<NotificationPreferenceDto>>("api-error", "The API is unavailable."),
            TestData.Ok<IReadOnlyList<NotificationPreferenceDto>>([new NotificationPreferenceDto(_orbitly, "Orbitly", false)]));
        var cut = Render<NotificationPreferencesPage>();

        cut.Find(".ts-state--error").TextContent.ShouldContain("Couldn't load your alert settings. The API is unavailable.");
        cut.Find(".ts-state--error button").Click();

        cut.WaitForAssertion(() => cut.FindAll(".ts-toggle-list li").Count.ShouldBe(1));
    }

    [Fact]
    public void No_active_products_is_a_plain_empty_state()
    {
        _agents.GetNotificationPreferencesAsync(Arg.Any<CancellationToken>()).Returns(TestData.Ok<IReadOnlyList<NotificationPreferenceDto>>([]));

        var cut = Render<NotificationPreferencesPage>();

        cut.Find(".ts-state-heading").TextContent.ShouldBe("No active products yet");
    }

    // ---- keyboard and theme: this browser ------------------------------------------------------------------------

    [Fact]
    public void The_keyboard_switch_turns_the_single_key_layer_off_and_remembers_it_in_the_browser()
    {
        var cut = Render<NotificationPreferencesPage>();
        cut.WaitForAssertion(() => cut.Find("#ts-keyboard").HasAttribute("checked").ShouldBeTrue());
        ShortcutService.SingleKeyEnabled.ShouldBeTrue();

        cut.Find("#ts-keyboard").Change(false);

        ShortcutService.SingleKeyEnabled.ShouldBeFalse();
        Preferences.Invocations["save"].Single().Arguments.ShouldBe(["singleKeyShortcuts", false]);
        cut.Find("label[for=ts-keyboard]").TextContent.ShouldBe("Keyboard shortcuts");
    }

    [Fact]
    public void The_theme_offers_auto_light_and_dark_with_auto_chosen_and_choosing_one_saves_it_in_the_browser()
    {
        var cut = Render<NotificationPreferencesPage>();

        cut.FindAll("input[name=ts-theme]").Select(i => i.GetAttribute("value")).ShouldBe(["auto", "light", "dark"]);
        cut.WaitForAssertion(() => cut.Find("#ts-theme-auto").HasAttribute("checked").ShouldBeTrue());

        cut.Find("#ts-theme-dark").Change("dark");

        Preferences.Invocations["save"].Single().Arguments.ShouldBe(["theme", "dark"]);
    }

    // ---- the public display name's preview uses the first active product ----------------------------------------

    [Fact]
    public void The_preview_uses_the_first_active_product_and_the_agents_first_name()
    {
        var cut = Render<NotificationPreferencesPage>();

        cut.Find("#ts-public-name-preview").TextContent.ShouldBe("Customers see: Sam from Nimbus Cloud Support");
    }

    [Fact]
    public void When_no_product_is_active_the_preview_shows_a_placeholder_for_it()
    {
        _products.ListAsync(Arg.Any<CancellationToken>()).Returns(TestData.Ok<IReadOnlyList<ProductDto>>([TestData.ProductDetail("Orbitly", active: false)]));

        var cut = Render<NotificationPreferencesPage>();

        cut.Find("#ts-public-name-preview").TextContent.ShouldBe("Customers see: Sam from [product] Support");
    }

    [Fact]
    public void A_product_list_that_cannot_be_read_only_means_the_placeholder_preview()
    {
        _products.ListAsync(Arg.Any<CancellationToken>()).Returns(TestData.Fail<IReadOnlyList<ProductDto>>("api-error", "Down."));

        var cut = Render<NotificationPreferencesPage>();

        cut.Find("#ts-public-name-preview").TextContent.ShouldBe("Customers see: Sam from [product] Support");
        cut.Find(".ts-toggle-list").ShouldNotBeNull();
    }
}
