using Bunit;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Settings.Products;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.ApiKeys;

namespace TechStrap.Admin.Tests.Components;

/// <summary>Review Focus 5 for API keys: a revoke never fires without its confirmation and never fires twice. The confirmation names the key and says apps using it will stop working.</summary>
public sealed class RevokeApiKeyTests : AdminComponentTest
{
    private readonly IProductsClient _products = Substitute.For<IProductsClient>();
    private readonly Guid _billingId = Guid.Parse("eeeeeeee-0000-0000-0000-000000000001");
    private readonly Guid _widgetId = Guid.Parse("eeeeeeee-0000-0000-0000-000000000002");

    public RevokeApiKeyTests()
    {
        _products.ListApiKeysAsync(TestData.OrbitlyId, Arg.Any<CancellationToken>()).Returns(_ => TestData.Ok<IReadOnlyList<ProductApiKeyDto>>(
        [
            TestData.ApiKey("tsk_ab12", ApiKeyKinds.Trusted, "Billing server", _billingId, TestData.Now.AddDays(-3)),
            TestData.ApiKey("tsk_cd34", ApiKeyKinds.Public, null, _widgetId, TestData.Now.AddDays(-2)),
        ]));
        _products.RevokeApiKeyAsync(TestData.OrbitlyId, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(_ => TestData.Ok());
        Services.AddSingleton(_products);
    }

    private IRenderedComponent<ApiKeysPanel> RenderPanel() =>
        Render<ApiKeysPanel>(p => p.Add(c => c.ProductId, TestData.OrbitlyId).Add(c => c.ProductName, "Orbitly"));

    private int Revokes() => _products.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(IProductsClient.RevokeApiKeyAsync));

    private static void AskToRevoke(IRenderedComponent<ApiKeysPanel> cut, string prefix) =>
        cut.FindAll("tbody tr").Single(r => r.QuerySelector("code")!.TextContent == prefix).QuerySelector("button.ts-revoke")!.Click();

    private static AngleSharp.Dom.IElement RevokeDialog(IRenderedComponent<ApiKeysPanel> cut) =>
        cut.FindAll("dialog").Single(d => d.QuerySelector("h2")!.TextContent.StartsWith("Revoke ", StringComparison.Ordinal));

    private static AngleSharp.Dom.IElement Confirm(IRenderedComponent<ApiKeysPanel> cut) =>
        RevokeDialog(cut).QuerySelector(".ts-dialog-actions button:not(.btn-outline-secondary)")!;

    [Fact]
    public void Revoke_asks_first_naming_the_key_and_saying_apps_using_it_will_stop_working_and_calls_nothing()
    {
        var cut = RenderPanel();

        AskToRevoke(cut, "tsk_ab12");

        var dialog = RevokeDialog(cut);
        dialog.QuerySelector("h2")!.TextContent.ShouldBe("Revoke Billing server?");
        dialog.TextContent.ShouldContain("Billing server (tsk_ab12), a Trusted key for Orbitly.");
        dialog.TextContent.ShouldContain("Apps using this key will stop working.");
        dialog.TextContent.ShouldContain("This can't be undone.");
        dialog.QuerySelector("input").ShouldBeNull("a medium confirmation: no typed text");
        Confirm(cut).TextContent.ShouldBe("Revoke key");
        Confirm(cut).ClassName!.ShouldContain("btn-danger");
        Dialogs.VerifyInvoke("open", 1);
        Revokes().ShouldBe(0);
    }

    [Fact]
    public void A_key_without_a_label_is_named_by_its_prefix()
    {
        var cut = RenderPanel();

        AskToRevoke(cut, "tsk_cd34");

        RevokeDialog(cut).QuerySelector("h2")!.TextContent.ShouldBe("Revoke tsk_cd34?");
    }

    [Fact]
    public void Cancel_and_Esc_close_the_confirmation_and_make_no_call()
    {
        var cut = RenderPanel();

        AskToRevoke(cut, "tsk_ab12");
        RevokeDialog(cut).QuerySelector("button.btn-outline-secondary")!.Click();
        AskToRevoke(cut, "tsk_ab12");
        RevokeDialog(cut).TriggerEvent("oncancel", EventArgs.Empty);

        Revokes().ShouldBe(0);
        Dialogs.VerifyInvoke("close", 2);
        cut.FindAll("tr.ts-key--revoked").ShouldBeEmpty();
    }

    [Fact]
    public void Confirming_revokes_that_key_once_with_no_cancellation_marks_the_row_revoked_and_says_so()
    {
        var cut = RenderPanel();
        AskToRevoke(cut, "tsk_ab12");

        Confirm(cut).Click();

        _products.Received(1).RevokeApiKeyAsync(TestData.OrbitlyId, _billingId, Arg.Is<CancellationToken>(t => !t.CanBeCanceled));
        Revokes().ShouldBe(1);
        var row = cut.Find("tr.ts-key--revoked");
        row.QuerySelector("code")!.TextContent.ShouldBe("tsk_ab12");
        row.QuerySelector(".ts-pill")!.TextContent.ShouldBe("Revoked");
        row.QuerySelectorAll("button").ShouldBeEmpty();
        StatusMessages.Current.ShouldBe("Revoked Billing server");
        Dialogs.VerifyInvoke("close", 1);
    }

    [Fact]
    public void A_revoked_key_cannot_be_revoked_again_from_the_list()
    {
        var cut = RenderPanel();
        AskToRevoke(cut, "tsk_ab12");
        Confirm(cut).Click();

        cut.FindAll("tbody tr").Single(r => r.QuerySelector("code")!.TextContent == "tsk_ab12").QuerySelectorAll("button.ts-revoke").ShouldBeEmpty();
        Revokes().ShouldBe(1);
    }

    [Fact]
    public void A_double_click_on_confirm_revokes_once_and_the_dialog_is_inert_while_it_runs()
    {
        var gate = new TaskCompletionSource<Result>();
        _products.RevokeApiKeyAsync(TestData.OrbitlyId, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(gate.Task);
        var cut = RenderPanel();
        AskToRevoke(cut, "tsk_ab12");

        Confirm(cut).Click();
        var dialog = RevokeDialog(cut);
        dialog.QuerySelectorAll("button").ShouldAllBe(b => b.HasAttribute("disabled"));
        dialog.TriggerEvent("oncancel", EventArgs.Empty);
        Confirm(cut).Click();

        Revokes().ShouldBe(1);
        Dialogs.VerifyNotInvoke("close");
        gate.SetResult(TestData.Ok());
        cut.WaitForAssertion(() => cut.FindAll("tr.ts-key--revoked").Count.ShouldBe(1));
    }

    [Fact]
    public void Choosing_a_different_row_names_that_row_and_revokes_that_one()
    {
        var cut = RenderPanel();

        AskToRevoke(cut, "tsk_ab12");
        RevokeDialog(cut).QuerySelector("button.btn-outline-secondary")!.Click();
        AskToRevoke(cut, "tsk_cd34");
        Confirm(cut).Click();

        _products.Received(1).RevokeApiKeyAsync(TestData.OrbitlyId, _widgetId, Arg.Any<CancellationToken>());
        _products.DidNotReceive().RevokeApiKeyAsync(TestData.OrbitlyId, _billingId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void A_failed_revoke_keeps_the_dialog_open_says_nothing_changed_and_leaves_the_row_active()
    {
        _products.RevokeApiKeyAsync(TestData.OrbitlyId, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail("boom", "The API refused."));
        var cut = RenderPanel();
        AskToRevoke(cut, "tsk_ab12");

        Confirm(cut).Click();

        RevokeDialog(cut).QuerySelector("[role=alert]")!.TextContent.ShouldBe("Couldn't revoke the key. Nothing was changed. The API refused.");
        cut.FindAll("tr.ts-key--revoked").ShouldBeEmpty();
        Dialogs.VerifyNotInvoke("close");
        StatusMessages.Current.ShouldBeNull();
    }

    [Fact]
    public void A_lost_answer_never_claims_nothing_changed_blocks_a_second_revoke_and_offers_a_reload()
    {
        _products.RevokeApiKeyAsync(TestData.OrbitlyId, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail(ApiErrorCodes.ApiTimeout, "TechStrap took too long to answer. Try again."));
        var cut = RenderPanel();
        AskToRevoke(cut, "tsk_ab12");
        Confirm(cut).Click();

        var dialog = RevokeDialog(cut);
        dialog.QuerySelector("[role=alert]")!.TextContent.ShouldBe("The revoke may have gone through. Reload the list to check before you try again.");
        dialog.TextContent.ShouldNotContain("Nothing was changed");
        Confirm(cut).HasAttribute("disabled").ShouldBeTrue();
        Confirm(cut).Click();
        Revokes().ShouldBe(1);

        RevokeDialog(cut).QuerySelector(".ts-dialog-recovery button")!.Click();

        _products.Received(2).ListApiKeysAsync(TestData.OrbitlyId, Arg.Any<CancellationToken>());
        Dialogs.VerifyInvoke("close", 1);
    }

    [Fact]
    public void After_a_lost_answer_asking_to_revoke_that_key_again_shows_the_uncertain_copy_and_sends_nothing_until_the_list_is_read_again()
    {
        _products.RevokeApiKeyAsync(TestData.OrbitlyId, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail(ApiErrorCodes.ApiTimeout, "TechStrap took too long to answer. Try again."));
        var cut = RenderPanel();
        AskToRevoke(cut, "tsk_ab12");
        Confirm(cut).Click();
        RevokeDialog(cut).QuerySelector("button.btn-outline-secondary")!.Click();

        AskToRevoke(cut, "tsk_ab12");

        RevokeDialog(cut).QuerySelector("[role=alert]")!.TextContent.ShouldBe("The revoke may have gone through. Reload the list to check before you try again.");
        Confirm(cut).HasAttribute("disabled").ShouldBeTrue();
        Confirm(cut).Click();
        Revokes().ShouldBe(1);

        // Another key is not held, and the held one is free again once the list has been read.
        RevokeDialog(cut).QuerySelector("button.btn-outline-secondary")!.Click();
        AskToRevoke(cut, "tsk_cd34");
        Confirm(cut).Click();
        Revokes().ShouldBe(2);
    }

    [Fact]
    public void A_key_that_is_already_gone_closes_the_dialog_says_so_and_reloads_the_list()
    {
        _products.RevokeApiKeyAsync(TestData.OrbitlyId, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Fail(ApiErrorCodes.ApiKeyNotFound, "No such key.", ResultErrorKind.NotFound));
        var cut = RenderPanel();
        AskToRevoke(cut, "tsk_ab12");

        Confirm(cut).Click();

        StatusMessages.Current.ShouldBe("That key no longer exists.");
        _products.Received(2).ListApiKeysAsync(TestData.OrbitlyId, Arg.Any<CancellationToken>());
        Dialogs.VerifyInvoke("close", 1);
    }

    [Fact]
    public async Task A_revoke_that_finishes_after_the_panel_is_gone_is_not_cancelled_and_reports_nothing()
    {
        var gate = new TaskCompletionSource<Result>();
        _products.RevokeApiKeyAsync(TestData.OrbitlyId, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(gate.Task);
        var cut = RenderPanel();
        AskToRevoke(cut, "tsk_ab12");
        Confirm(cut).Click();

        cut.Instance.Dispose();
        gate.SetResult(TestData.Ok());
        await cut.InvokeAsync(() => { });

        _ = _products.Received(1).RevokeApiKeyAsync(TestData.OrbitlyId, _billingId, Arg.Is<CancellationToken>(t => !t.CanBeCanceled));
        StatusMessages.Current.ShouldBeNull();
    }
}
