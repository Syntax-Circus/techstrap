using Microsoft.JSInterop;
using NSubstitute;
using TechStrap.Admin.Features.Shell;

namespace TechStrap.Admin.Tests.Components;

/// <summary>The load, the setters and the disposal racing each other, with the script module under the control of the test.</summary>
public sealed class PreferencesServiceLifecycleTests
{
    private readonly IJSRuntime _js = Substitute.For<IJSRuntime>();
    private readonly IJSObjectReference _module = Substitute.For<IJSObjectReference>();
    private readonly ShortcutService _shortcuts;
    private readonly TaskCompletionSource<StoredPreferences> _load = new();

    public PreferencesServiceLifecycleTests()
    {
        _shortcuts = new ShortcutService(_js);
        _module.InvokeAsync<StoredPreferences>("load", Arg.Any<object?[]?>()).Returns(_ => new ValueTask<StoredPreferences>(_load.Task));
        _module.InvokeAsync<bool>("save", Arg.Any<object?[]?>()).Returns(new ValueTask<bool>(true));
        _js.InvokeAsync<IJSObjectReference>("import", Arg.Any<object?[]?>()).Returns(new ValueTask<IJSObjectReference>(_module));
    }

    [Fact]
    public async Task A_choice_made_while_the_stored_values_are_loading_wins_over_them()
    {
        var service = new PreferencesService(_js, _shortcuts);
        var loading = service.LoadAsync();

        await service.SetThemeAsync(ThemeChoice.Dark);
        await service.SetSingleKeyShortcutsAsync(false);
        _load.SetResult(new StoredPreferences(SingleKeyShortcuts: true, Theme: "light"));
        await loading.WaitAsync(TimeSpan.FromSeconds(5), Xunit.TestContext.Current.CancellationToken);

        service.Theme.ShouldBe(ThemeChoice.Dark);
        service.SingleKeyShortcuts.ShouldBeFalse();
        service.IsLoaded.ShouldBeTrue();
    }

    [Fact]
    public async Task A_load_and_a_setter_that_overlap_import_the_module_once()
    {
        var service = new PreferencesService(_js, _shortcuts);
        var loading = service.LoadAsync();

        await service.SetThemeAsync(ThemeChoice.Light);
        _load.SetResult(new StoredPreferences(true, "auto"));
        await loading.WaitAsync(TimeSpan.FromSeconds(5), Xunit.TestContext.Current.CancellationToken);

        await _js.Received(1).InvokeAsync<IJSObjectReference>("import", Arg.Any<object?[]?>());
    }

    [Fact]
    public async Task Disposing_while_the_import_is_in_flight_disposes_the_module_and_writes_nothing()
    {
        var import = new TaskCompletionSource<IJSObjectReference>();
        _js.InvokeAsync<IJSObjectReference>("import", Arg.Any<object?[]?>()).Returns(_ => new ValueTask<IJSObjectReference>(import.Task));
        var service = new PreferencesService(_js, _shortcuts);
        var changes = 0;
        service.Changed += () => changes++;
        var loading = service.LoadAsync();

        var disposing = service.DisposeAsync().AsTask();
        import.SetResult(_module);
        await disposing.WaitAsync(TimeSpan.FromSeconds(5), Xunit.TestContext.Current.CancellationToken);
        await loading.WaitAsync(TimeSpan.FromSeconds(5), Xunit.TestContext.Current.CancellationToken);

        await _module.Received(1).DisposeAsync();
        await _module.DidNotReceive().InvokeAsync<StoredPreferences>("load", Arg.Any<object?[]?>());
        service.IsLoaded.ShouldBeFalse();
        changes.ShouldBe(0);
    }

    [Fact]
    public async Task Disposing_while_the_stored_values_are_loading_keeps_them_out_of_the_state()
    {
        var service = new PreferencesService(_js, _shortcuts);
        var changes = 0;
        service.Changed += () => changes++;
        var loading = service.LoadAsync();
        await service.DisposeAsync();

        _load.SetResult(new StoredPreferences(SingleKeyShortcuts: false, Theme: "dark"));
        await loading.WaitAsync(TimeSpan.FromSeconds(5), Xunit.TestContext.Current.CancellationToken);

        service.Theme.ShouldBe(ThemeChoice.Auto);
        _shortcuts.SingleKeyEnabled.ShouldBeTrue();
        changes.ShouldBe(0);
        await _module.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task A_setter_that_finishes_after_disposal_does_not_announce()
    {
        var save = new TaskCompletionSource<bool>();
        _module.InvokeAsync<bool>("save", Arg.Any<object?[]?>()).Returns(_ => new ValueTask<bool>(save.Task));
        var service = new PreferencesService(_js, _shortcuts);
        var changes = 0;
        service.Changed += () => changes++;
        var setting = service.SetThemeAsync(ThemeChoice.Dark);
        await service.DisposeAsync();

        save.SetResult(true);
        await setting.WaitAsync(TimeSpan.FromSeconds(5), Xunit.TestContext.Current.CancellationToken);

        changes.ShouldBe(0);
    }
}
