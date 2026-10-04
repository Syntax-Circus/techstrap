using System.Text.RegularExpressions;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Settings.Products;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.ApiKeys;

namespace TechStrap.Admin.Tests.Components;

/// <summary>The key list and the create flow. The plaintext goes straight into the dialog (Review Focus 2) and is in no row, no status message and no other part of the page.</summary>
public sealed class ApiKeysPanelTests : AdminComponentTest
{
    private const string Secret = "tsk_live_0123456789abcdef0123456789abcdef";

    private readonly IProductsClient _products = Substitute.For<IProductsClient>();
    private readonly Guid _activeId = Guid.Parse("eeeeeeee-0000-0000-0000-000000000001");

    public ApiKeysPanelTests()
    {
        _products.ListApiKeysAsync(TestData.OrbitlyId, Arg.Any<CancellationToken>()).Returns(_ => TestData.Ok<IReadOnlyList<ProductApiKeyDto>>(
        [
            TestData.ApiKey("tsk_old1", ApiKeyKinds.Public, "Old widget", created: TestData.Now.AddDays(-30), revoked: TestData.Now.AddDays(-2)),
            TestData.ApiKey("tsk_ab12", ApiKeyKinds.Trusted, "Billing server", _activeId, TestData.Now.AddDays(-3), lastUsed: TestData.Now.AddMinutes(-5)),
            TestData.ApiKey("tsk_cd34", ApiKeyKinds.Public, null, created: TestData.Now.AddDays(-1)),
        ]));
        _products.CreateApiKeyAsync(TestData.OrbitlyId, Arg.Any<CreateProductApiKeyRequest>(), Arg.Any<CancellationToken>())
            .Returns(call => TestData.Ok(new CreateProductApiKeyResponse(TestData.ApiKey("tsk_new9", call.Arg<CreateProductApiKeyRequest>().Kind!, call.Arg<CreateProductApiKeyRequest>().Label), Secret)));
        Services.AddSingleton(_products);
    }

    private IRenderedComponent<ApiKeysPanel> RenderPanel() =>
        Render<ApiKeysPanel>(p => p.Add(c => c.ProductId, TestData.OrbitlyId).Add(c => c.ProductName, "Orbitly"));

    private IReadOnlyList<CreateProductApiKeyRequest> Creates() =>
        [.. _products.ReceivedCalls().Where(c => c.GetMethodInfo().Name == nameof(IProductsClient.CreateApiKeyAsync)).Select(c => (CreateProductApiKeyRequest)c.GetArguments()[1]!)];

    private static void Choose(IRenderedComponent<ApiKeysPanel> cut, string kind) => cut.Find("#ts-key-kind").Change(kind);

    private static void Submit(IRenderedComponent<ApiKeysPanel> cut) => cut.Find("form.ts-key-create").Submit();

    // ---- the list -----------------------------------------------------------------------------------------------

    [Fact]
    public void Keys_are_listed_newest_first_with_label_kind_prefix_and_last_used()
    {
        var cut = RenderPanel();

        var rows = cut.FindAll("tbody tr");
        rows.Select(r => r.QuerySelector("code")!.TextContent).ShouldBe(["tsk_cd34", "tsk_ab12", "tsk_old1"]);
        rows[0].Children[0].TextContent.ShouldBe("No label");
        rows[1].Children[0].TextContent.ShouldBe("Billing server");
        rows[1].Children[4].TextContent.ShouldBe("5 min ago");
        rows[0].Children[4].TextContent.Trim().ShouldBe("Never used");
    }

    [Fact]
    public void Trusted_and_public_badges_are_distinct_in_words_and_in_class()
    {
        var cut = RenderPanel();

        var badges = cut.FindAll("tbody .ts-kind").Select(b => (b.TextContent, b.ClassName)).ToList();
        badges.ShouldContain(("Trusted", "ts-kind ts-kind--trusted"));
        badges.ShouldContain(("Public", "ts-kind ts-kind--public"));
    }

    [Fact]
    public void A_revoked_key_renders_as_revoked_with_no_revoke_button_and_an_active_one_has_one()
    {
        var cut = RenderPanel();

        var revoked = cut.Find("tr.ts-key--revoked");
        revoked.QuerySelector("code")!.TextContent.ShouldBe("tsk_old1");
        revoked.QuerySelector(".ts-pill")!.TextContent.ShouldBe("Revoked");
        revoked.QuerySelectorAll("button").ShouldBeEmpty();
        cut.FindAll("tbody tr:not(.ts-key--revoked) button.ts-revoke").Count.ShouldBe(2);
        cut.FindAll("tbody tr:not(.ts-key--revoked) .ts-pill").ShouldAllBe(p => p.TextContent == "Active");
    }

    [Fact]
    public void No_keys_is_an_empty_state_that_explains_the_two_kinds()
    {
        _products.ListApiKeysAsync(TestData.OrbitlyId, Arg.Any<CancellationToken>()).Returns(TestData.Ok<IReadOnlyList<ProductApiKeyDto>>([]));

        var cut = RenderPanel();

        cut.Find(".ts-state-heading").TextContent.ShouldBe("No API keys yet");
        var empty = cut.Find(".ts-state--empty");
        empty.TextContent.ShouldContain("Trusted: server-side only, may set external user ref and trusted metadata");
        empty.TextContent.ShouldContain("Public: safe to embed in a client app, create-only, rate limited, metadata treated as untrusted");
        cut.FindAll("table").ShouldBeEmpty();
    }

    [Fact]
    public void A_failed_load_shows_the_api_message_and_retry_loads_again()
    {
        _products.ListApiKeysAsync(TestData.OrbitlyId, Arg.Any<CancellationToken>()).Returns(
            TestData.Fail<IReadOnlyList<ProductApiKeyDto>>("api-error", "The API is unavailable."),
            TestData.Ok<IReadOnlyList<ProductApiKeyDto>>([TestData.ApiKey()]));
        var cut = RenderPanel();

        cut.Find(".ts-state--error").TextContent.ShouldContain("Couldn't load the API keys. The API is unavailable.");
        cut.Find(".ts-state--error button").Click();

        cut.WaitForAssertion(() => cut.FindAll("tbody tr").Count.ShouldBe(1));
    }

    [Fact]
    public void The_list_never_holds_more_than_the_prefix()
    {
        var cut = RenderPanel();

        cut.Markup.ShouldNotContain("tsk_live");
        cut.FindAll("dialog input#ts-key-secret").ShouldBeEmpty();
    }

    // ---- creating -----------------------------------------------------------------------------------------------

    [Fact]
    public void A_kind_must_be_chosen_and_both_kinds_are_explained_beside_the_choice()
    {
        var cut = RenderPanel();

        cut.Find("form.ts-key-create button[type=submit]").HasAttribute("disabled").ShouldBeTrue();
        cut.Find("#ts-key-kind").QuerySelectorAll("option").Select(o => o.TextContent).ShouldBe(["Choose a kind", "Trusted", "Public"]);
        cut.Find("form.ts-key-create .ts-kind-explained").TextContent.ShouldContain("Trusted: server-side only");
        Submit(cut);
        Creates().ShouldBeEmpty();
        cut.Find("#ts-key-kind-error").TextContent.ShouldBe("Choose a kind.");
    }

    [Fact]
    public void Creating_a_key_sends_the_kind_and_label_through_the_write_path_and_opens_the_dialog_with_the_key()
    {
        var cut = RenderPanel();
        Choose(cut, ApiKeyKinds.Trusted);
        cut.Find("#ts-key-label").Input("  CI server  ");

        Submit(cut);

        Creates().ShouldHaveSingleItem().ShouldBe(new CreateProductApiKeyRequest(ApiKeyKinds.Trusted, "CI server"));
        _products.Received(1).CreateApiKeyAsync(TestData.OrbitlyId, Arg.Any<CreateProductApiKeyRequest>(), Arg.Is<CancellationToken>(t => !t.CanBeCanceled));
        cut.Find("dialog input#ts-key-secret").GetAttribute("value").ShouldBe(Secret);
        Dialogs.VerifyInvoke("open", 1);
        cut.FindAll("tbody tr").Count.ShouldBe(4);
        cut.FindAll("tbody code").Select(c => c.TextContent).ShouldContain("tsk_new9");
        StatusMessages.Current.ShouldBe("Created a Trusted key for Orbitly");
        cut.Find("#ts-key-label").GetAttribute("value").ShouldBe(string.Empty);
    }

    [Fact]
    public void The_plaintext_is_in_the_dialog_field_only_and_not_in_the_status_a_row_or_any_other_attribute()
    {
        var cut = RenderPanel();
        Choose(cut, ApiKeyKinds.Public);

        Submit(cut);

        Regex.Matches(cut.Markup, Regex.Escape(Secret)).Count.ShouldBe(1);
        StatusMessages.Current!.ShouldNotContain(Secret);
        cut.FindAll("tbody").Single().TextContent.ShouldNotContain(Secret);
    }

    [Fact]
    public void A_blank_label_goes_as_null_and_an_over_long_one_is_refused_before_it_is_sent()
    {
        var cut = RenderPanel();
        Choose(cut, ApiKeyKinds.Public);
        cut.Find("#ts-key-label").Input("   ");
        Submit(cut);
        Creates().ShouldHaveSingleItem().Label.ShouldBeNull();

        cut.Find("#ts-key-label").Input(new string('x', 101));
        Submit(cut);

        cut.Find("#ts-key-label-error").TextContent.ShouldBe("Use 100 characters or fewer.");
        Creates().Count.ShouldBe(1);
    }

    [Fact]
    public void A_double_submit_creates_one_key()
    {
        var gate = new TaskCompletionSource<Result<CreateProductApiKeyResponse>>();
        _products.CreateApiKeyAsync(TestData.OrbitlyId, Arg.Any<CreateProductApiKeyRequest>(), Arg.Any<CancellationToken>()).Returns(gate.Task);
        var cut = RenderPanel();
        Choose(cut, ApiKeyKinds.Public);

        Submit(cut);
        cut.Find("fieldset").HasAttribute("disabled").ShouldBeTrue();
        Submit(cut);

        Creates().Count.ShouldBe(1);
        gate.SetResult(TestData.Ok(new CreateProductApiKeyResponse(TestData.ApiKey("tsk_new9"), Secret)));
        cut.WaitForAssertion(() => cut.Find("dialog input#ts-key-secret").GetAttribute("value").ShouldBe(Secret));
    }

    [Theory]
    [InlineData(ApiErrorCodes.ApiKeyKindInvalid, "kind", "#ts-key-kind-error")]
    [InlineData("label-too-long", "label", "#ts-key-label-error")]
    public void A_field_error_from_the_api_appears_at_its_field_and_no_dialog_opens(string code, string target, string errorSelector)
    {
        _products.CreateApiKeyAsync(TestData.OrbitlyId, Arg.Any<CreateProductApiKeyRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result<CreateProductApiKeyResponse>.Failure(new ResultError(code, "The server says no.", ResultErrorKind.Validation, target)));
        var cut = RenderPanel();
        Choose(cut, ApiKeyKinds.Public);

        Submit(cut);

        cut.Find(errorSelector).TextContent.ShouldBe("The server says no.");
        Dialogs.VerifyNotInvoke("open");
        cut.FindAll("input#ts-key-secret").ShouldBeEmpty();
    }

    [Fact]
    public void A_product_that_is_gone_is_said_so_and_no_dialog_opens()
    {
        _products.CreateApiKeyAsync(TestData.OrbitlyId, Arg.Any<CreateProductApiKeyRequest>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Fail<CreateProductApiKeyResponse>(ApiErrorCodes.ProductNotFound, "No such product.", ResultErrorKind.NotFound));
        var cut = RenderPanel();
        Choose(cut, ApiKeyKinds.Public);

        Submit(cut);

        cut.Find("p.ts-form-error[role=alert]").TextContent.ShouldBe("This product no longer exists.");
        Dialogs.VerifyNotInvoke("open");
    }

    [Fact]
    public async Task A_create_that_finishes_after_the_panel_is_gone_shows_nothing_and_stores_nothing()
    {
        var gate = new TaskCompletionSource<Result<CreateProductApiKeyResponse>>();
        _products.CreateApiKeyAsync(TestData.OrbitlyId, Arg.Any<CreateProductApiKeyRequest>(), Arg.Any<CancellationToken>()).Returns(gate.Task);
        var cut = RenderPanel();
        Choose(cut, ApiKeyKinds.Public);
        Submit(cut);

        cut.Instance.Dispose();
        gate.SetResult(TestData.Ok(new CreateProductApiKeyResponse(TestData.ApiKey("tsk_new9"), Secret)));
        await cut.InvokeAsync(() => { });

        StatusMessages.Current.ShouldBeNull();
        Dialogs.VerifyNotInvoke("open");
        cut.Markup.ShouldNotContain(Secret);
        cut.Markup.ShouldNotContain("tsk_new9");
    }

    [Fact]
    public void After_a_lost_answer_the_form_is_locked_with_its_values_kept_until_the_list_is_reloaded()
    {
        _products.CreateApiKeyAsync(TestData.OrbitlyId, Arg.Any<CreateProductApiKeyRequest>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Fail<CreateProductApiKeyResponse>(ApiErrorCodes.ApiTimeout, "TechStrap took too long to answer. Try again."));
        var cut = RenderPanel();
        Choose(cut, ApiKeyKinds.Public);
        cut.Find("#ts-key-label").Input("CI server");
        Submit(cut);

        cut.Find("fieldset").HasAttribute("disabled").ShouldBeTrue();
        cut.Find("form.ts-key-create button[type=submit]").HasAttribute("disabled").ShouldBeTrue();
        cut.Find("#ts-key-label").GetAttribute("value").ShouldBe("CI server");
        Submit(cut);
        Creates().Count.ShouldBe(1, "a second click must not send a second POST");

        cut.Find(".ts-conflict button").Click();

        cut.Find("fieldset").HasAttribute("disabled").ShouldBeFalse();
        cut.Find("form.ts-key-create button[type=submit]").HasAttribute("disabled").ShouldBeFalse();
    }

    [Fact]
    public void A_lost_answer_says_the_key_may_exist_but_its_secret_cannot_be_shown_and_never_offers_a_bare_retry()
    {
        _products.CreateApiKeyAsync(TestData.OrbitlyId, Arg.Any<CreateProductApiKeyRequest>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Fail<CreateProductApiKeyResponse>(ApiErrorCodes.ApiTimeout, "TechStrap took too long to answer. Try again."));
        var cut = RenderPanel();
        Choose(cut, ApiKeyKinds.Public);

        Submit(cut);

        var alert = cut.Find(".ts-conflict[role=alert]");
        alert.TextContent.ShouldContain("The key may have been created");
        alert.TextContent.ShouldContain("revoke it and create another");
        alert.TextContent.ShouldNotContain("Try again");
        Dialogs.VerifyNotInvoke("open");
        Creates().Count.ShouldBe(1, "a write is never retried");

        alert.QuerySelector("button")!.Click();

        _products.Received(2).ListApiKeysAsync(TestData.OrbitlyId, Arg.Any<CancellationToken>());
        cut.FindAll(".ts-conflict").ShouldBeEmpty();
    }
}
