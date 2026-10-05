using Bunit;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Admin.Auth;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Account;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.Agents;

namespace TechStrap.Admin.Tests.Components;

/// <summary>PHASE-07 T23: the optional public display name with its live preview, saved on blur or Enter, with the two rules checked before anything is sent.</summary>
public sealed class PublicDisplayNameFieldTests : AdminPageTest
{
    private readonly IAgentsClient _agents = Substitute.For<IAgentsClient>();
    private string? _savedName;

    public PublicDisplayNameFieldTests()
    {
        AsAgent();
        _agents.UpdateMyProfileAsync(Arg.Any<UpdateMyProfileRequest>(), Arg.Any<CancellationToken>()).Returns(TestData.Ok());
        Services.AddSingleton(_agents);
    }

    protected override AgentSession CreateSession(bool admin)
    {
        _agents.GetMeAsync(Arg.Any<CancellationToken>()).Returns(_ => TestData.Ok(TestData.Me(admin, _savedName)));
        var session = new AgentSession(_agents);
        session.EnsureLoadedAsync(CancellationToken.None).GetAwaiter().GetResult();
        return session;
    }

    private IRenderedComponent<PublicDisplayNameField> RenderField(string? product = "Orbitly") =>
        Render<PublicDisplayNameField>(p => p.Add(c => c.ProductDisplayName, product));

    private IReadOnlyList<UpdateMyProfileRequest> Saves() =>
        [.. _agents.ReceivedCalls().Where(c => c.GetMethodInfo().Name == nameof(IAgentsClient.UpdateMyProfileAsync)).Select(c => (UpdateMyProfileRequest)c.GetArguments()[0]!)];

    private static string Preview(IRenderedComponent<PublicDisplayNameField> cut) => cut.Find("#ts-public-name-preview").TextContent;

    // ---- the preview ---------------------------------------------------------------------------------------------

    [Fact]
    public void The_preview_starts_as_the_first_name_from_the_first_product_and_the_helper_says_the_email_is_never_shown()
    {
        var cut = RenderField();

        Preview(cut).ShouldBe("Customers see: Sam from Orbitly Support");
        cut.Find("#ts-public-name-help").TextContent.ShouldBe("Customers never see your email address.");
        cut.Find("label[for=ts-public-name]").TextContent.ShouldBe("Public display name (optional)");
        cut.Find("#ts-public-name").GetAttribute("value").ShouldBe(string.Empty);
    }

    [Fact]
    public void Typing_changes_the_preview_live_and_clearing_returns_to_the_default()
    {
        var cut = RenderField();

        cut.Find("#ts-public-name").Input("Samantha");
        Preview(cut).ShouldBe("Customers see: Samantha from Orbitly Support");

        cut.Find("#ts-public-name").Input("");
        Preview(cut).ShouldBe("Customers see: Sam from Orbitly Support");
        Saves().ShouldBeEmpty("typing alone never saves");
    }

    [Fact]
    public void A_saved_name_fills_the_field_and_the_preview()
    {
        _savedName = "Sammy";

        var cut = RenderField();

        cut.Find("#ts-public-name").GetAttribute("value").ShouldBe("Sammy");
        Preview(cut).ShouldBe("Customers see: Sammy from Orbitly Support");
    }

    // ---- saving --------------------------------------------------------------------------------------------------

    [Fact]
    public void Blur_saves_the_trimmed_name_once_with_no_cancellation_shows_the_confirmation_and_asks_the_session_again()
    {
        var cut = RenderField();
        cut.Find("#ts-public-name").Input("  Samantha  ");

        cut.Find("#ts-public-name").Blur();

        Saves().ShouldHaveSingleItem().PublicDisplayName.ShouldBe("Samantha");
        _agents.Received(1).UpdateMyProfileAsync(Arg.Any<UpdateMyProfileRequest>(), Arg.Is<CancellationToken>(t => !t.CanBeCanceled));
        cut.Find("p[role=status]").TextContent.ShouldBe("Saved.");
        cut.Find("#ts-public-name").GetAttribute("value").ShouldBe("Samantha");
        _agents.Received(2).GetMeAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Enter_saves_and_the_blur_that_follows_does_not_save_a_second_time()
    {
        var cut = RenderField();
        cut.Find("#ts-public-name").Input("Samantha");

        cut.Find("#ts-public-name").KeyDown(Key.Enter);
        cut.Find("#ts-public-name").Blur();

        Saves().Count.ShouldBe(1);
    }

    [Fact]
    public void Retyping_the_committed_name_after_an_uncertain_save_says_to_reload_instead_of_doing_nothing()
    {
        _savedName = "Sam";
        _agents.UpdateMyProfileAsync(Arg.Any<UpdateMyProfileRequest>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail(ApiErrorCodes.ApiTimeout, "TechStrap took too long to answer. Try again."));
        var cut = RenderField();
        cut.Find("#ts-public-name").Input("Samantha");
        cut.Find("#ts-public-name").Blur();
        cut.Find("[role=alert]").TextContent.ShouldContain("The save may have gone through.");

        // The agent types the old name back to undo it. "Sam" may be what is stored, or "Samantha" may be: silence would hide that, and sending would repeat a write that may have landed.
        cut.Find("#ts-public-name").Input("Sam");
        cut.Find("#ts-public-name").Blur();

        cut.Find("[role=alert]").TextContent.ShouldBe("The save may have gone through. Reload the page to see what is saved before you try again.");
        Saves().Count.ShouldBe(1);
    }

    [Fact]
    public void Retyping_the_committed_name_when_nothing_is_uncertain_still_does_nothing()
    {
        _savedName = "Sam";
        var cut = RenderField();
        cut.Find("#ts-public-name").Input("Samantha");
        cut.Find("#ts-public-name").Input("Sam");

        cut.Find("#ts-public-name").Blur();

        Saves().ShouldBeEmpty();
        cut.FindAll("[role=alert]").ShouldBeEmpty();
    }

    [Fact]
    public void Blur_without_a_change_saves_nothing_and_the_field_is_optional()
    {
        var cut = RenderField();

        cut.Find("#ts-public-name").Blur();
        cut.Find("#ts-public-name").Input("   ");
        cut.Find("#ts-public-name").Blur();

        Saves().ShouldBeEmpty();
        cut.FindAll("[role=alert]").ShouldBeEmpty();
    }

    [Fact]
    public void Clearing_a_saved_name_sends_null()
    {
        _savedName = "Sammy";
        var cut = RenderField();
        cut.Find("#ts-public-name").Input("");

        cut.Find("#ts-public-name").Blur();

        Saves().ShouldHaveSingleItem().PublicDisplayName.ShouldBeNull();
    }

    [Fact]
    public void The_session_keeps_the_agent_and_stays_ready_while_it_reloads_so_the_field_does_not_flicker()
    {
        var cut = RenderField();
        cut.Find("#ts-public-name").Input("Samantha");
        var gate = new TaskCompletionSource<Result<AgentDto>>();
        _agents.GetMeAsync(Arg.Any<CancellationToken>()).Returns(gate.Task);

        cut.Find("#ts-public-name").Blur();

        Session.State.ShouldBe(AgentSessionState.Ready);
        Session.Agent.ShouldNotBeNull();
        cut.Find("#ts-public-name").GetAttribute("value").ShouldBe("Samantha");
        gate.SetResult(TestData.Ok(TestData.Me(admin: false, "Samantha")));
        cut.WaitForAssertion(() => Session.Agent!.PublicDisplayName.ShouldBe("Samantha"));
        Session.State.ShouldBe(AgentSessionState.Ready);
    }

    // ---- the two rules are checked before anything is sent -------------------------------------------------------

    [Fact]
    public void A_name_over_60_characters_or_with_an_at_sign_shows_a_field_error_and_sends_nothing()
    {
        var cut = RenderField();

        cut.Find("#ts-public-name").Input(new string('x', 61));
        cut.Find("#ts-public-name").Blur();
        cut.Find("#ts-public-name-error").TextContent.ShouldBe("Use 60 characters or fewer.");

        cut.Find("#ts-public-name").Input("sam@example.com");
        cut.Find("#ts-public-name").KeyDown(Key.Enter);
        cut.Find("#ts-public-name-error").TextContent.ShouldBe("Use a name without @, so it can't be mistaken for an email address.");

        Saves().ShouldBeEmpty();
        cut.Find("#ts-public-name").GetAttribute("aria-invalid").ShouldBe("true");
    }

    [Fact]
    public void Exactly_60_characters_is_accepted()
    {
        var cut = RenderField();
        cut.Find("#ts-public-name").Input(new string('x', 60));

        cut.Find("#ts-public-name").Blur();

        Saves().ShouldHaveSingleItem().PublicDisplayName!.Length.ShouldBe(60);
    }

    // ---- what the API says ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData(ApiErrorCodes.PublicDisplayNameTooLong)]
    [InlineData(ApiErrorCodes.PublicDisplayNameInvalid)]
    public void A_field_error_from_the_api_appears_at_the_field_and_the_text_stays(string code)
    {
        _agents.UpdateMyProfileAsync(Arg.Any<UpdateMyProfileRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure(new ResultError(code, "The server says no.", ResultErrorKind.Validation, "public-display-name")));
        var cut = RenderField();
        cut.Find("#ts-public-name").Input("Samantha");

        cut.Find("#ts-public-name").Blur();

        cut.Find("#ts-public-name-error").TextContent.ShouldBe("The server says no.");
        cut.Find("#ts-public-name").GetAttribute("value").ShouldBe("Samantha");
        cut.FindAll("p[role=status]").ShouldBeEmpty();
        _agents.Received(1).GetMeAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Another_failure_says_nothing_changed_and_a_lost_answer_never_does()
    {
        _agents.UpdateMyProfileAsync(Arg.Any<UpdateMyProfileRequest>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Fail("boom", "The API refused."), TestData.Fail(ApiErrorCodes.ApiTimeout, "TechStrap took too long to answer. Try again."));
        var cut = RenderField();
        cut.Find("#ts-public-name").Input("Samantha");

        cut.Find("#ts-public-name").Blur();
        cut.Find("#ts-public-name-error").TextContent.ShouldBe("Couldn't save your public display name. Nothing was changed. The API refused.");

        cut.Find("#ts-public-name").Blur();
        var lost = cut.Find("#ts-public-name-error").TextContent;
        lost.ShouldBe("The save may have gone through. Reload the page to see what is saved before you try again.");
        lost.ShouldNotContain("Nothing was changed");
        Saves().Count.ShouldBe(2);
    }

    [Fact]
    public void A_save_that_finishes_after_the_field_is_gone_touches_nothing_and_is_not_cancelled()
    {
        var gate = new TaskCompletionSource<Result>();
        _agents.UpdateMyProfileAsync(Arg.Any<UpdateMyProfileRequest>(), Arg.Any<CancellationToken>()).Returns(gate.Task);
        var cut = RenderField();
        cut.Find("#ts-public-name").Input("Samantha");
        cut.Find("#ts-public-name").Blur();

        cut.Instance.Dispose();
        gate.SetResult(TestData.Ok());

        _agents.Received(1).UpdateMyProfileAsync(Arg.Any<UpdateMyProfileRequest>(), Arg.Is<CancellationToken>(t => !t.CanBeCanceled));
        _agents.Received(1).GetMeAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void After_a_lost_answer_the_next_blur_or_Enter_does_not_resend_the_same_value_until_the_text_changes()
    {
        _agents.UpdateMyProfileAsync(Arg.Any<UpdateMyProfileRequest>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Fail(ApiErrorCodes.ApiTimeout, "TechStrap took too long to answer. Try again."), TestData.Ok());
        var cut = RenderField();
        cut.Find("#ts-public-name").Input("Samantha");
        cut.Find("#ts-public-name").Blur();

        cut.Find("#ts-public-name").Blur();
        cut.Find("#ts-public-name").KeyDown(Key.Enter);

        Saves().Count.ShouldBe(1, "the value may already be stored; asking again sends nothing");
        cut.Find("#ts-public-name-error").TextContent.ShouldBe("The save may have gone through. Reload the page to see what is saved before you try again.");

        cut.Find("#ts-public-name").Input("Sammy");
        cut.Find("#ts-public-name").Blur();

        Saves().Select(r => r.PublicDisplayName).ShouldBe(["Samantha", "Sammy"]);
    }

    [Fact]
    public void While_saving_the_input_is_read_only_and_busy_never_disabled_so_focus_stays()
    {
        var gate = new TaskCompletionSource<Result>();
        _agents.UpdateMyProfileAsync(Arg.Any<UpdateMyProfileRequest>(), Arg.Any<CancellationToken>()).Returns(gate.Task);
        var cut = RenderField();
        cut.Find("#ts-public-name").Input("Samantha");

        cut.Find("#ts-public-name").KeyDown(Key.Enter);

        var input = cut.Find("#ts-public-name");
        input.HasAttribute("disabled").ShouldBeFalse();
        input.HasAttribute("readonly").ShouldBeTrue();
        input.GetAttribute("aria-busy").ShouldBe("true");
        gate.SetResult(TestData.Ok());
        cut.WaitForAssertion(() => cut.Find("#ts-public-name").HasAttribute("readonly").ShouldBeFalse());
        cut.Find("#ts-public-name").HasAttribute("aria-busy").ShouldBeFalse();
    }
}
