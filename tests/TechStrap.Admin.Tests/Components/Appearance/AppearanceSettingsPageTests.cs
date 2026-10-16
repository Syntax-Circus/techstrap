using Bunit;
using Microsoft.AspNetCore.Components;
using TechStrap.Admin.Components.Ui;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Settings.Appearance;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.Settings;

namespace TechStrap.Admin.Tests.Components.Appearance;

public sealed class AppearanceSettingsPageTests : AdminComponentTest
{
    private readonly IAppearanceSettingsClient _settings = Substitute.For<IAppearanceSettingsClient>();
    public AppearanceSettingsPageTests()
    {
        Services.AddSingleton(_settings);
        _settings.GetAsync(Arg.Any<CancellationToken>()).Returns(Result<SiteSettingsDto>.Success(new("slate", 17)));
    }
    [Fact]
    public void Loading_shows_saved_pack_without_making_a_write()
    {
        var cut = Render<AppearanceSettingsContent>();
        cut.WaitForAssertion(() => cut.Find("#site-default-pack").GetAttribute("value").ShouldBe("slate"));
        cut.FindAll("#site-default-pack option").Count.ShouldBe(5);
        cut.Find("button[type='submit']").HasAttribute("disabled").ShouldBeTrue();
        _settings.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(IAppearanceSettingsClient.UpdateAsync)).ShouldBe(0);
    }
    [Fact]
    public void Saving_a_changed_pack_carries_the_loaded_version_and_resets_dirty_state()
    {
        _settings.UpdateAsync(new("midnight", 17), Arg.Any<CancellationToken>()).Returns(Result<SiteSettingsDto>.Success(new("midnight", 19)));
        var cut = Render<AppearanceSettingsContent>();
        cut.WaitForElement("#site-default-pack").Change("midnight");
        cut.Find("form").Submit();
        cut.WaitForAssertion(() => cut.Find("[role='status']").TextContent.ShouldContain("saved"));
        cut.Find("button[type='submit']").HasAttribute("disabled").ShouldBeTrue();
        _settings.ReceivedCalls().Where(c => c.GetMethodInfo().Name == nameof(IAppearanceSettingsClient.UpdateAsync)).Single().GetArguments()[0].ShouldBe(new UpdateSiteSettingsRequest("midnight", 17));
    }
    [Theory]
    [InlineData("concurrency-conflict", ResultErrorKind.Conflict)]
    [InlineData("api-timeout", ResultErrorKind.Failure)]
    public void Failed_writes_preserve_draft_and_hold_save_until_an_explicit_reload(string code, ResultErrorKind kind)
    {
        _settings.UpdateAsync(Arg.Any<UpdateSiteSettingsRequest>(), Arg.Any<CancellationToken>()).Returns(Result<SiteSettingsDto>.Failure(new ResultError(code, "Changed.", kind)));
        var cut = Render<AppearanceSettingsContent>();
        cut.WaitForElement("#site-default-pack").Change("paper");
        cut.Find("form").Submit();
        cut.WaitForAssertion(() => cut.Find("[role='alert']").TextContent.ShouldContain("Reload"));
        cut.Find("#site-default-pack").GetAttribute("value").ShouldBe("paper");
        cut.Find("button[type='submit']").HasAttribute("disabled").ShouldBeTrue();
    }
    [Fact]
    public void Failed_read_does_not_show_a_fabricated_default_or_allow_saving()
    {
        _settings.GetAsync(Arg.Any<CancellationToken>()).Returns(Result<SiteSettingsDto>.Failure(new ResultError("api-unavailable", "Unavailable.", ResultErrorKind.Failure)));
        var cut = Render<AppearanceSettingsContent>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("could not be loaded"));
        cut.FindAll("#site-default-pack").ShouldBeEmpty();
    }
    [Fact]
    public void Invalid_pack_is_never_sent_to_the_api()
    {
        var cut = Render<AppearanceSettingsContent>();
        cut.WaitForElement("#site-default-pack").Change("untrusted");
        cut.Find("form").Submit();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Choose one of the five"));
        _settings.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(IAppearanceSettingsClient.UpdateAsync)).ShouldBe(0);
    }
    [Fact]
    public void A_plain_agent_cannot_render_the_settings_form_or_read_settings()
    {
        var cut = Render<AppearanceSettingsPage>();
        cut.FindAll("#site-default-pack").ShouldBeEmpty();
        _settings.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task A_delayed_retry_read_updates_the_form_after_it_completes()
    {
        _settings.GetAsync(Arg.Any<CancellationToken>()).Returns(Result<SiteSettingsDto>.Failure(new ResultError("api-unavailable", "Unavailable.", ResultErrorKind.Failure)));
        var cut = Render<AppearanceSettingsContent>();
        cut.WaitForElement("[role='alert']");
        var read = new TaskCompletionSource<Result<SiteSettingsDto>>(TaskCreationOptions.RunContinuationsAsynchronously);
        _settings.GetAsync(Arg.Any<CancellationToken>()).Returns(read.Task);
        var retry = cut.FindAll("button").Single(b => b.TextContent == "Reload saved settings").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());
        read.SetResult(Result<SiteSettingsDto>.Success(new("paper", 19)));
        await retry;
        cut.WaitForAssertion(() => cut.Find("#site-default-pack").GetAttribute("value").ShouldBe("paper"));
    }

    [Fact]
    public async Task A_dirty_theme_requires_explicit_discard_before_navigation()
    {
        var cut = Render<AppearanceSettingsContent>();
        cut.WaitForElement("#site-default-pack").Change("paper");
        var navigation = Services.GetRequiredService<NavigationManager>();
        await cut.InvokeAsync(() => navigation.NavigateTo("/settings/products"));
        var dialog = cut.FindComponent<ConfirmDialog>();
        dialog.Instance.Open.ShouldBeTrue();
        dialog.Find("button.btn-outline-secondary").Click();
        cut.Find("#site-default-pack").GetAttribute("value").ShouldBe("paper");
        navigation.Uri.ShouldNotContain("/settings/products");
        await cut.InvokeAsync(() => navigation.NavigateTo("/settings/products"));
        dialog.Find("button.btn-danger").Click();
        navigation.Uri.ShouldEndWith("/settings/products");
    }

    [Fact]
    public void Failed_reload_preserves_the_draft_and_the_uncertain_write_hold()
    {
        _settings.UpdateAsync(Arg.Any<UpdateSiteSettingsRequest>(), Arg.Any<CancellationToken>()).Returns(Result<SiteSettingsDto>.Failure(new ResultError("api-timeout", "Unknown outcome.", ResultErrorKind.Failure)));
        var cut = Render<AppearanceSettingsContent>();
        cut.WaitForElement("#site-default-pack").Change("paper");
        cut.Find("form").Submit();
        _settings.GetAsync(Arg.Any<CancellationToken>()).Returns(Result<SiteSettingsDto>.Failure(new ResultError("api-unavailable", "Unavailable.", ResultErrorKind.Failure)));
        cut.FindAll("button").First(b => b.TextContent == "Reload saved settings").Click();
        var dialog = cut.FindComponent<ConfirmDialog>();
        dialog.Instance.Open.ShouldBeTrue();
        dialog.Find("button.btn-danger").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("could not be loaded"));
        cut.Find("#site-default-pack").GetAttribute("value").ShouldBe("paper");
        cut.Find("form").Submit();
        _settings.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(IAppearanceSettingsClient.UpdateAsync)).ShouldBe(1);
        _settings.GetAsync(Arg.Any<CancellationToken>()).Returns(Result<SiteSettingsDto>.Success(new("midnight", 20)));
        cut.FindAll("button").First(b => b.TextContent == "Reload saved settings").Click();
        dialog.Find("button.btn-danger").Click();
        cut.WaitForAssertion(() => cut.Find("#site-default-pack").GetAttribute("value").ShouldBe("midnight"));
        cut.Find("#site-default-pack").Change("paper");
        _settings.UpdateAsync(new("paper", 20), Arg.Any<CancellationToken>()).Returns(Result<SiteSettingsDto>.Success(new("paper", 21)));
        cut.Find("form").Submit();
        cut.WaitForAssertion(() => cut.Find("[role='status']").TextContent.ShouldContain("saved"));
    }

}
