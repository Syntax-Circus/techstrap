using Bunit;
using Microsoft.JSInterop;
using Microsoft.JSInterop.Infrastructure;
using NSubstitute;
using TechStrap.Admin.Features.Shell;
using TechStrap.Admin.Tests.Support;

namespace TechStrap.Admin.Tests.Components;

public sealed class ShortcutServiceTests : AdminComponentTest
{
    private static KeyPress Key(string key, bool ctrl = false, bool typing = false, bool onBody = true, string? scope = null, bool alt = false) =>
        new(key, ctrl, Meta: false, alt, typing, onBody, scope);

    [Theory]
    [InlineData("j", ShortcutAction.MoveDown)]
    [InlineData("J", ShortcutAction.MoveDown)]
    [InlineData("k", ShortcutAction.MoveUp)]
    [InlineData("ArrowDown", ShortcutAction.MoveDown)]
    [InlineData("ArrowUp", ShortcutAction.MoveUp)]
    [InlineData("Enter", ShortcutAction.OpenSelected)]
    [InlineData("/", ShortcutAction.FocusSearch)]
    [InlineData("r", ShortcutAction.Reply)]
    [InlineData("n", ShortcutAction.Note)]
    [InlineData("e", ShortcutAction.FocusAssignee)]
    [InlineData("u", ShortcutAction.NotSpam)]
    [InlineData("U", ShortcutAction.NotSpam)]
    [InlineData("?", ShortcutAction.Help)]
    [InlineData("Escape", ShortcutAction.Escape)]
    public void A_single_key_maps_to_its_action_when_the_user_is_not_typing(string key, ShortcutAction expected) =>
        ShortcutService.Map(Key(key), singleKeyEnabled: true).ShouldBe(expected);

    [Theory]
    [InlineData("j")]
    [InlineData("/")]
    [InlineData("r")]
    [InlineData("u")]
    [InlineData("Enter")]
    [InlineData("Escape")]
    public void While_typing_no_single_key_is_a_shortcut(string key) =>
        ShortcutService.Map(Key(key, typing: true), singleKeyEnabled: true).ShouldBeNull();

    [Theory]
    [InlineData("j")]
    [InlineData("/")]
    [InlineData("ArrowDown")]
    public void With_single_key_shortcuts_off_only_escape_and_ctrl_enter_still_work(string key)
    {
        ShortcutService.Map(Key(key), singleKeyEnabled: false).ShouldBeNull();
        ShortcutService.Map(Key("Escape"), singleKeyEnabled: false).ShouldBe(ShortcutAction.Escape);
        ShortcutService.Map(Key("Enter", ctrl: true, typing: true, scope: ShortcutService.ComposerScope), singleKeyEnabled: false).ShouldBe(ShortcutAction.Send);
    }

    [Fact]
    public void Enter_and_the_arrows_are_left_alone_when_something_interactive_has_focus()
    {
        ShortcutService.Map(Key("Enter", onBody: false), singleKeyEnabled: true).ShouldBeNull();
        ShortcutService.Map(Key("ArrowDown", onBody: false), singleKeyEnabled: true).ShouldBeNull();
        ShortcutService.Map(Key("j", onBody: false), singleKeyEnabled: true).ShouldBe(ShortcutAction.MoveDown);
    }

    [Fact]
    public void Ctrl_enter_sends_only_from_inside_the_composer()
    {
        ShortcutService.Map(Key("Enter", ctrl: true, typing: true, scope: ShortcutService.ComposerScope), true).ShouldBe(ShortcutAction.Send);
        ShortcutService.Map(Key("Enter", ctrl: true, typing: true, scope: null), true).ShouldBeNull();
        ShortcutService.Map(Key("Enter", ctrl: true, typing: true, scope: "queue"), true).ShouldBeNull();
        ShortcutService.Map(Key("j", ctrl: true), true).ShouldBeNull();
    }

    [Fact]
    public void An_alt_chord_is_never_a_shortcut() =>
        ShortcutService.Map(Key("j", alt: true), singleKeyEnabled: true).ShouldBeNull();

    [Fact]
    public async Task Every_subscriber_hears_a_recognised_shortcut_in_order()
    {
        var heard = new List<string>();
        ShortcutService.Pressed += action => { heard.Add($"first:{action}"); return Task.CompletedTask; };
        ShortcutService.Pressed += action => { heard.Add($"second:{action}"); return Task.CompletedTask; };

        await PressAsync("j");

        heard.ShouldBe(["first:MoveDown", "second:MoveDown"]);
    }

    [Fact]
    public async Task Nothing_is_raised_while_typing_or_when_the_layer_is_off()
    {
        var heard = new List<ShortcutAction>();
        ShortcutService.Pressed += action => { heard.Add(action); return Task.CompletedTask; };

        await PressAsync("j", typing: true);
        ShortcutService.SingleKeyEnabled = false;
        await PressAsync("j");

        heard.ShouldBeEmpty();
    }

    [Fact]
    public async Task Starting_imports_the_module_and_registers_the_listener_once()
    {
        await ShortcutService.StartAsync();
        await ShortcutService.StartAsync();

        Shortcuts.VerifyInvoke("register", 1);
    }

    [Fact]
    public async Task Disposing_unregisters_the_listener_once_and_releases_the_dotnet_reference()
    {
        await ShortcutService.StartAsync();
        var reference = (DotNetObjectReference<ShortcutService>)Shortcuts.Invocations["register"].Single().Arguments[0]!;

        await ShortcutService.DisposeAsync();

        Shortcuts.VerifyInvoke("unregister", 1);
        Should.Throw<ObjectDisposedException>(() => reference.Value);
    }

    [Fact]
    public async Task The_dotnet_reference_is_released_even_when_unregister_fails()
    {
        DotNetObjectReference<ShortcutService>? reference = null;
        var module = Substitute.For<IJSObjectReference>();
        module.InvokeAsync<IJSVoidResult>("register", Arg.Any<object?[]?>()).Returns(call =>
        {
            reference = (DotNetObjectReference<ShortcutService>)call.Arg<object?[]>()[0]!;
            return ValueTask.FromResult<IJSVoidResult>(null!);
        });
        module.InvokeAsync<IJSVoidResult>("unregister", Arg.Any<object?[]?>()).Returns<ValueTask<IJSVoidResult>>(_ => throw new JSException("boom"));
        var js = Substitute.For<IJSRuntime>();
        js.InvokeAsync<IJSObjectReference>("import", Arg.Any<object?[]?>()).Returns(ValueTask.FromResult(module));
        var service = new ShortcutService(js);
        await service.StartAsync();

        await Should.ThrowAsync<JSException>(async () => await service.DisposeAsync());

        Should.Throw<ObjectDisposedException>(() => reference!.Value);
    }

    [Fact]
    public async Task Concurrent_starts_import_and_register_once()
    {
        var gate = new TaskCompletionSource<IJSObjectReference>();
        var module = Substitute.For<IJSObjectReference>();
        module.InvokeAsync<IJSVoidResult>("register", Arg.Any<object?[]?>()).Returns(ValueTask.FromResult<IJSVoidResult>(null!));
        var js = Substitute.For<IJSRuntime>();
        js.InvokeAsync<IJSObjectReference>("import", Arg.Any<object?[]?>()).Returns(new ValueTask<IJSObjectReference>(gate.Task));
        var service = new ShortcutService(js);

        var first = service.StartAsync();
        var second = service.StartAsync();
        gate.SetResult(module);
        await Task.WhenAll(first, second);

        await js.Received(1).InvokeAsync<IJSObjectReference>("import", Arg.Any<object?[]?>());
        await module.Received(1).InvokeAsync<IJSVoidResult>("register", Arg.Any<object?[]?>());
    }

    [Fact]
    public async Task Starting_when_the_circuit_is_already_gone_does_not_throw()
    {
        var js = Substitute.For<IJSRuntime>();
        js.InvokeAsync<IJSObjectReference>("import", Arg.Any<object?[]?>()).Returns<ValueTask<IJSObjectReference>>(_ => throw new JSDisconnectedException("gone"));

        await Should.NotThrowAsync(() => new ShortcutService(js).StartAsync());
    }

    [Fact]
    public void The_help_list_never_gives_two_shortcuts_the_same_key()
    {
        var keys = ShortcutCatalog.All.SelectMany(entry => entry.Keys.Split(" / ")).ToList();

        keys.Distinct(StringComparer.OrdinalIgnoreCase).Count().ShouldBe(keys.Count);
    }

    [Fact]
    public void The_help_list_covers_every_key_the_service_maps_except_the_arrow_aliases()
    {
        var listed = ShortcutCatalog.All.SelectMany(entry => entry.Keys.Split(" / ")).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var key in new[] { "j", "k", "Enter", "/", "r", "n", "e", "u", "?", "Esc", "Ctrl+Enter" })
        {
            listed.ShouldContain(key);
        }
    }
}
