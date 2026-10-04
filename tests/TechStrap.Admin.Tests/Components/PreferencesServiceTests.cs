using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using TechStrap.Admin.Components.Layout;
using TechStrap.Admin.Features.Shell;
using TechStrap.Admin.Tests.Support;

namespace TechStrap.Admin.Tests.Components;

public sealed class PreferencesServiceTests : AdminComponentTest
{
    private PreferencesService Service => Services.GetRequiredService<PreferencesService>();

    private static object?[] Args(JSRuntimeInvocation call) => [.. call.Arguments];

    [Fact]
    public async Task Loading_reads_the_stored_values_sets_the_shortcut_switch_and_the_theme_choice()
    {
        Preferences.Setup<StoredPreferences>("load", _ => true).SetResult(new StoredPreferences(SingleKeyShortcuts: false, Theme: "dark"));
        var service = Service;

        await service.LoadAsync();

        service.IsLoaded.ShouldBeTrue();
        service.Theme.ShouldBe(ThemeChoice.Dark);
        service.SingleKeyShortcuts.ShouldBeFalse();
        ShortcutService.SingleKeyEnabled.ShouldBeFalse();
    }

    [Fact]
    public async Task Loading_happens_once_however_often_it_is_asked()
    {
        var service = Service;

        await Task.WhenAll(service.LoadAsync(), service.LoadAsync());
        await service.LoadAsync();

        Preferences.VerifyInvoke("load", 1);
    }

    [Theory]
    [InlineData("sepia")]
    [InlineData("")]
    [InlineData(null)]
    public async Task An_unrecognised_stored_theme_is_auto(string? theme)
    {
        Preferences.Setup<StoredPreferences>("load", _ => true).SetResult(new StoredPreferences(true, theme!));
        var service = Service;

        await service.LoadAsync();

        service.Theme.ShouldBe(ThemeChoice.Auto);
    }

    [Fact]
    public async Task A_script_that_fails_leaves_the_defaults_and_never_throws()
    {
        Preferences.Setup<StoredPreferences>("load", _ => true).SetException(new JSException("storage is blocked"));
        var service = Service;

        await service.LoadAsync();

        service.IsLoaded.ShouldBeTrue();
        service.Theme.ShouldBe(ThemeChoice.Auto);
        service.SingleKeyShortcuts.ShouldBeTrue();
        ShortcutService.SingleKeyEnabled.ShouldBeTrue();
    }

    [Fact]
    public async Task Turning_the_shortcuts_off_changes_the_live_switch_stores_the_choice_and_announces_it()
    {
        var service = Service;
        var changes = 0;
        service.Changed += () => changes++;

        await service.SetSingleKeyShortcutsAsync(false);

        ShortcutService.SingleKeyEnabled.ShouldBeFalse();
        service.SingleKeyShortcuts.ShouldBeFalse();
        Args(Preferences.Invocations["save"].Single()).ShouldBe(["singleKeyShortcuts", false]);
        changes.ShouldBe(1);
    }

    [Theory]
    [InlineData(ThemeChoice.Auto, "auto")]
    [InlineData(ThemeChoice.Light, "light")]
    [InlineData(ThemeChoice.Dark, "dark")]
    public async Task Choosing_a_theme_stores_its_lower_case_name(ThemeChoice choice, string stored)
    {
        var service = Service;

        await service.SetThemeAsync(choice);

        service.Theme.ShouldBe(choice);
        Args(Preferences.Invocations["save"].Single()).ShouldBe(["theme", stored]);
    }

    [Fact]
    public async Task A_save_that_fails_keeps_the_choice_for_this_page_and_never_throws()
    {
        Preferences.Setup<bool>("save", _ => true).SetException(new JSException("QuotaExceededError"));
        var service = Service;

        await service.SetThemeAsync(ThemeChoice.Dark);
        await service.SetSingleKeyShortcutsAsync(false);

        service.Theme.ShouldBe(ThemeChoice.Dark);
        ShortcutService.SingleKeyEnabled.ShouldBeFalse();
    }

    [Fact]
    public async Task A_disconnected_circuit_is_not_an_error()
    {
        Preferences.Setup<bool>("save", _ => true).SetException(new JSDisconnectedException("The circuit is gone."));
        var service = Service;

        await service.SetThemeAsync(ThemeChoice.Light);

        service.Theme.ShouldBe(ThemeChoice.Light);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("2")]
    [InlineData("Light,Dark")]
    [InlineData(" dark")]
    public async Task A_stored_theme_that_is_only_numerically_or_loosely_a_theme_is_auto(string theme)
    {
        Preferences.Setup<StoredPreferences>("load", _ => true).SetResult(new StoredPreferences(true, theme));
        var service = Service;

        await service.LoadAsync();

        service.Theme.ShouldBe(ThemeChoice.Auto);
    }

    [Fact]
    public async Task The_stored_theme_names_are_read_case_insensitively()
    {
        Preferences.Setup<StoredPreferences>("load", _ => true).SetResult(new StoredPreferences(true, "LIGHT"));
        var service = Service;

        await service.LoadAsync();

        service.Theme.ShouldBe(ThemeChoice.Light);
    }

    [Fact]
    public async Task An_undefined_theme_value_is_ignored_and_stores_nothing()
    {
        var service = Service;
        await service.SetThemeAsync(ThemeChoice.Dark);

        await service.SetThemeAsync((ThemeChoice)7);

        service.Theme.ShouldBe(ThemeChoice.Dark);
        Preferences.Invocations["save"].Count.ShouldBe(1);
    }

    [Fact]
    public async Task The_layout_loads_the_preferences_once_before_it_starts_the_key_listener()
    {
        this.AddAgentShell();
        var order = new List<string>();
        Preferences.Setup<StoredPreferences>("load", _ =>
        {
            order.Add("load");
            return true;
        }).SetResult(new StoredPreferences(SingleKeyShortcuts: false, Theme: "light"));
        Shortcuts.SetupVoid("register", _ =>
        {
            order.Add("register");
            return true;
        }).SetVoidResult();

        var cut = Render<MainLayout>(p => p.SignedIn().Add(l => l.Body, (RenderFragment)(b => { })));
        await cut.InvokeAsync(() => Task.CompletedTask);

        Preferences.VerifyInvoke("load", 1);
        Shortcuts.VerifyInvoke("register", 1);
        order.ShouldContain("load");
        order.ShouldContain("register");
        order.IndexOf("load").ShouldBeLessThan(order.IndexOf("register"));
        ShortcutService.SingleKeyEnabled.ShouldBeFalse();
        Service.Theme.ShouldBe(ThemeChoice.Light);
    }

    [Fact]
    public async Task With_the_shortcuts_turned_off_a_single_key_does_nothing_but_the_layout_still_answers_modified_keys()
    {
        this.AddAgentShell();
        Preferences.Setup<StoredPreferences>("load", _ => true).SetResult(new StoredPreferences(SingleKeyShortcuts: false, Theme: "auto"));
        Render<MainLayout>(p => p.SignedIn().Add(l => l.Body, (RenderFragment)(b => { })));
        var pressed = new List<ShortcutAction>();
        ShortcutService.Pressed += action =>
        {
            pressed.Add(action);
            return Task.CompletedTask;
        };

        await PressAsync("j");
        await PressAsync("?");
        pressed.ShouldBeEmpty();

        await PressAsync("Enter", ctrl: true, typing: true, scope: "composer");
        await PressAsync("Escape");

        pressed.ShouldBe([ShortcutAction.Send, ShortcutAction.Escape]);
    }
}
