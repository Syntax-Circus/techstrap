using Bunit;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Settings.Tags;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.Tags;

namespace TechStrap.Admin.Tests.Components;

/// <summary>The tag list with its ticket counts, create, and inline rename and recolour. Delete has its own class (Review Focus 5).</summary>
public sealed class TagsPageTests : AdminPageTest
{
    private readonly ITagsClient _tags = Substitute.For<ITagsClient>();

    public TagsPageTests()
    {
        _tags.ListSummaryAsync(Arg.Any<CancellationToken>()).Returns(_ => TestData.Ok<IReadOnlyList<TagSummaryDto>>(
        [
            TestData.TagSummary("urgent", 12, id: Guid.Parse("bbbbbbbb-0000-0000-0000-000000000010")),
            TestData.TagSummary("bug", 1, id: Guid.Parse("bbbbbbbb-0000-0000-0000-000000000011")),
            TestData.TagSummary("Billing issue", 0, "billing-issue", "#1D4ED8", Guid.Parse("bbbbbbbb-0000-0000-0000-000000000012")),
        ]));
        _tags.CreateAsync(Arg.Any<CreateTagRequest>(), Arg.Any<CancellationToken>()).Returns(call =>
            TestData.Ok(new TagDto(Guid.NewGuid(), call.Arg<CreateTagRequest>().Slug!, call.Arg<CreateTagRequest>().Name!, call.Arg<CreateTagRequest>().Colour!.ToUpperInvariant())));
        _tags.UpdateAsync(Arg.Any<Guid>(), Arg.Any<UpdateTagRequest>(), Arg.Any<CancellationToken>()).Returns(call =>
            TestData.Ok(new TagDto(call.Arg<Guid>(), "x", call.Arg<UpdateTagRequest>().Name!, call.Arg<UpdateTagRequest>().Colour!.ToUpperInvariant())));
        Services.AddSingleton(_tags);
    }

    private IReadOnlyList<CreateTagRequest> Creates() =>
        [.. _tags.ReceivedCalls().Where(c => c.GetMethodInfo().Name == nameof(ITagsClient.CreateAsync)).Select(c => (CreateTagRequest)c.GetArguments()[0]!)];

    private IReadOnlyList<UpdateTagRequest> Updates() =>
        [.. _tags.ReceivedCalls().Where(c => c.GetMethodInfo().Name == nameof(ITagsClient.UpdateAsync)).Select(c => (UpdateTagRequest)c.GetArguments()[1]!)];

    private static string Value(IRenderedComponent<TagsPage> cut, string id) => cut.Find($"#{id}").GetAttribute("value")!;

    private static AngleSharp.Dom.IElement Row(IRenderedComponent<TagsPage> cut, string slug) => cut.Find($"tr[data-tag='{slug}']");

    // ---- the list ------------------------------------------------------------------------------------------------

    [Fact]
    public void Tags_are_listed_by_name_with_a_chip_the_slug_and_the_ticket_count()
    {
        var cut = RenderPage();

        cut.FindAll("tbody tr").Select(r => r.Children[0].TextContent.Trim()).ShouldBe(["Billing issue", "bug", "urgent"]);
        Row(cut, "urgent").QuerySelector(".ts-tag-count")!.TextContent.ShouldBe("12");
        Row(cut, "bug").QuerySelector(".ts-tag-count")!.TextContent.ShouldBe("1");
        Row(cut, "billing-issue").QuerySelector(".ts-tag-count")!.TextContent.ShouldBe("0");
        Row(cut, "urgent").QuerySelector("code")!.TextContent.ShouldBe("urgent");
        _tags.Received(1).ListSummaryAsync(Arg.Any<CancellationToken>());
        _tags.DidNotReceive().ListAsync(Arg.Any<CancellationToken>());
    }

    private IRenderedComponent<TagsPage> RenderPage() => Render<TagsPage>();

    [Fact]
    public void No_tags_is_a_plain_empty_state_and_the_create_form_is_still_there()
    {
        _tags.ListSummaryAsync(Arg.Any<CancellationToken>()).Returns(TestData.Ok<IReadOnlyList<TagSummaryDto>>([]));

        var cut = RenderPage();

        cut.Find(".ts-state-heading").TextContent.ShouldBe("No tags yet");
        cut.Find("form.ts-tag-create").ShouldNotBeNull();
    }

    [Fact]
    public void A_failed_load_shows_the_api_message_and_retry_loads_again()
    {
        _tags.ListSummaryAsync(Arg.Any<CancellationToken>()).Returns(
            TestData.Fail<IReadOnlyList<TagSummaryDto>>("api-error", "The API is unavailable."),
            TestData.Ok<IReadOnlyList<TagSummaryDto>>([TestData.TagSummary("bug")]));
        var cut = RenderPage();

        cut.Find(".ts-state--error").TextContent.ShouldContain("Couldn't load the tags. The API is unavailable.");
        cut.Find(".ts-state--error button").Click();

        cut.WaitForAssertion(() => cut.FindAll("tbody tr").Count.ShouldBe(1));
    }

    [Fact]
    public void A_plain_agent_gets_the_no_access_page_and_the_api_is_not_asked()
    {
        AsAgent();

        var cut = RenderPage();

        cut.Find("section.ts-no-access").ShouldNotBeNull();
        cut.FindAll("form.ts-tag-create").ShouldBeEmpty();
        _tags.ReceivedCalls().ShouldBeEmpty();
    }

    // ---- create --------------------------------------------------------------------------------------------------

    [Fact]
    public void The_slug_follows_the_name_until_the_agent_edits_it()
    {
        var cut = RenderPage();

        cut.Find("#ts-tag-name").Input("Billing issue");
        Value(cut, "ts-tag-slug").ShouldBe("billing-issue");

        cut.Find("#ts-tag-slug").Input("billing");
        cut.Find("#ts-tag-name").Input("Billing problem");
        Value(cut, "ts-tag-slug").ShouldBe("billing");
    }

    [Fact]
    public void Creating_sends_the_trimmed_values_adds_the_tag_with_no_tickets_and_says_so()
    {
        var cut = RenderPage();
        cut.Find("#ts-tag-name").Input("  Refund  ");
        cut.Find("#ts-tag-colour").Input("#0f766e");

        cut.Find("form.ts-tag-create").Submit();

        Creates().ShouldHaveSingleItem().ShouldBe(new CreateTagRequest("refund", "Refund", "#0f766e"));
        _tags.Received(1).CreateAsync(Arg.Any<CreateTagRequest>(), Arg.Is<CancellationToken>(t => !t.CanBeCanceled));
        Row(cut, "refund").QuerySelector(".ts-tag-count")!.TextContent.ShouldBe("0");
        StatusMessages.Current.ShouldBe("Created the tag Refund");
        Value(cut, "ts-tag-name").ShouldBe(string.Empty);
        Value(cut, "ts-tag-slug").ShouldBe(string.Empty);
        Value(cut, "ts-tag-colour").ShouldBe("#4B5563");
    }

    [Theory]
    [InlineData("red")]
    [InlineData("#12345")]
    [InlineData("#GGGGGG")]
    [InlineData("")]
    public void A_colour_that_is_not_hash_and_six_hex_digits_is_refused_with_the_shared_constant_and_never_sent(string colour)
    {
        var cut = RenderPage();
        cut.Find("#ts-tag-name").Input("Refund");
        cut.Find("#ts-tag-colour").Input(colour);

        cut.Find("form.ts-tag-create").Submit();

        cut.Find("#ts-tag-colour-error").TextContent.ShouldBe("Use a colour like #1D4ED8: a # and six hex digits.");
        Creates().ShouldBeEmpty();
    }

    [Fact]
    public void A_missing_name_or_a_bad_slug_blocks_the_create_with_a_field_error_each()
    {
        var cut = RenderPage();
        cut.Find("#ts-tag-slug").Input("Bad Slug");

        cut.Find("form.ts-tag-create").Submit();

        cut.Find("#ts-tag-name-error").TextContent.ShouldBe("Enter a name.");
        cut.Find("#ts-tag-slug-error").TextContent.ShouldBe("Use lower-case letters, numbers and single hyphens, up to 40 characters.");
        Creates().ShouldBeEmpty();
    }

    [Fact]
    public void A_duplicate_slug_409_shows_a_field_error_on_the_slug_and_keeps_every_value()
    {
        _tags.CreateAsync(Arg.Any<CreateTagRequest>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Fail<TagDto>(ApiErrorCodes.TagSlugTaken, "A tag with this slug already exists.", ResultErrorKind.Conflict));
        var cut = RenderPage();
        cut.Find("#ts-tag-name").Input("Bug");

        cut.Find("form.ts-tag-create").Submit();

        cut.Find("#ts-tag-slug-error").TextContent.ShouldBe("Another tag already uses this slug.");
        Value(cut, "ts-tag-name").ShouldBe("Bug");
        Value(cut, "ts-tag-slug").ShouldBe("bug");
        StatusMessages.Current.ShouldBeNull();
        cut.FindAll("tbody tr").Count.ShouldBe(3);
    }

    [Theory]
    [InlineData("name", "#ts-tag-name-error")]
    [InlineData("slug", "#ts-tag-slug-error")]
    [InlineData("colour", "#ts-tag-colour-error")]
    public void A_400_names_its_field_in_kebab_case_and_it_appears_there(string target, string selector)
    {
        _tags.CreateAsync(Arg.Any<CreateTagRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result<TagDto>.Failure(new ResultError("x-invalid", "The server says no.", ResultErrorKind.Validation, target)));
        var cut = RenderPage();
        cut.Find("#ts-tag-name").Input("Refund");

        cut.Find("form.ts-tag-create").Submit();

        cut.Find(selector).TextContent.ShouldBe("The server says no.");
    }

    [Fact]
    public void A_double_submit_creates_one_tag()
    {
        var gate = new TaskCompletionSource<Result<TagDto>>();
        _tags.CreateAsync(Arg.Any<CreateTagRequest>(), Arg.Any<CancellationToken>()).Returns(gate.Task);
        var cut = RenderPage();
        cut.Find("#ts-tag-name").Input("Refund");

        cut.Find("form.ts-tag-create").Submit();
        cut.Find("form.ts-tag-create").Submit();

        Creates().Count.ShouldBe(1);
        gate.SetResult(TestData.Ok(new TagDto(Guid.NewGuid(), "refund", "Refund", "#4B5563")));
        cut.WaitForAssertion(() => cut.FindAll("tbody tr").Count.ShouldBe(4));
    }

    [Fact]
    public void A_lost_create_answer_never_claims_nothing_changed_and_offers_a_reload()
    {
        _tags.CreateAsync(Arg.Any<CreateTagRequest>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail<TagDto>(ApiErrorCodes.ApiTimeout, "TechStrap took too long to answer. Try again."));
        var cut = RenderPage();
        cut.Find("#ts-tag-name").Input("Refund");

        cut.Find("form.ts-tag-create").Submit();

        var alert = cut.Find(".ts-conflict[role=alert]");
        alert.TextContent.ShouldContain("The change may have gone through.");
        alert.TextContent.ShouldNotContain("Nothing was changed");
        alert.QuerySelector("button")!.Click();
        _tags.Received(2).ListSummaryAsync(Arg.Any<CancellationToken>());
        Creates().Count.ShouldBe(1);
    }

    // ---- edit ----------------------------------------------------------------------------------------------------

    [Fact]
    public void Edit_changes_the_name_and_colour_in_the_row_with_the_slug_read_only_and_keeps_the_count()
    {
        var cut = RenderPage();
        Row(cut, "urgent").QuerySelector("button.ts-edit")!.Click();

        var editing = cut.Find("tr.ts-tag-editing");
        editing.QuerySelector("code")!.TextContent.ShouldBe("urgent");
        Value(cut, "ts-edit-name").ShouldBe("urgent");
        cut.Find("#ts-edit-name").Input("Urgent now");
        cut.Find("#ts-edit-colour").Input("#1d4ed8");
        cut.Find("button.ts-save").Click();

        Updates().ShouldHaveSingleItem().ShouldBe(new UpdateTagRequest("Urgent now", "#1d4ed8"));
        cut.FindAll("tr.ts-tag-editing").ShouldBeEmpty();
        Row(cut, "urgent").QuerySelector(".ts-tag-count")!.TextContent.ShouldBe("12");
        StatusMessages.Current.ShouldBe("Saved the tag Urgent now");
    }

    [Fact]
    public void Cancel_leaves_the_row_as_it_was_and_sends_nothing()
    {
        var cut = RenderPage();
        Row(cut, "urgent").QuerySelector("button.ts-edit")!.Click();
        cut.Find("#ts-edit-name").Input("Changed");

        cut.Find("button.ts-cancel").Click();

        cut.FindAll("tr.ts-tag-editing").ShouldBeEmpty();
        Updates().ShouldBeEmpty();
        Row(cut, "urgent").TextContent.ShouldContain("urgent");
    }

    [Fact]
    public void An_invalid_edit_is_refused_before_it_is_sent_and_a_server_error_stays_in_the_row()
    {
        var cut = RenderPage();
        Row(cut, "urgent").QuerySelector("button.ts-edit")!.Click();
        cut.Find("#ts-edit-colour").Input("nope");
        cut.Find("button.ts-save").Click();
        cut.Find("#ts-edit-colour-error").TextContent.ShouldBe("Use a colour like #1D4ED8: a # and six hex digits.");
        Updates().ShouldBeEmpty();

        _tags.UpdateAsync(Arg.Any<Guid>(), Arg.Any<UpdateTagRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result<TagDto>.Failure(new ResultError("name-too-long", "The server says no.", ResultErrorKind.Validation, "name")));
        cut.Find("#ts-edit-colour").Input("#1D4ED8");
        cut.Find("button.ts-save").Click();

        cut.Find("#ts-edit-name-error").TextContent.ShouldBe("The server says no.");
        cut.FindAll("tr.ts-tag-editing").Count.ShouldBe(1);
    }

    [Fact]
    public void Editing_a_tag_that_is_gone_says_so_and_reloads_the_list()
    {
        _tags.UpdateAsync(Arg.Any<Guid>(), Arg.Any<UpdateTagRequest>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail<TagDto>(ApiErrorCodes.TagNotFound, "No such tag.", ResultErrorKind.NotFound));
        var cut = RenderPage();
        Row(cut, "urgent").QuerySelector("button.ts-edit")!.Click();

        cut.Find("button.ts-save").Click();

        StatusMessages.Current.ShouldBe("That tag no longer exists.");
        _tags.Received(2).ListSummaryAsync(Arg.Any<CancellationToken>());
        cut.FindAll("tr.ts-tag-editing").ShouldBeEmpty();
    }

    [Fact]
    public void A_slow_list_that_was_overtaken_by_a_reload_does_not_replace_the_newer_list()
    {
        var slow = new TaskCompletionSource<Result<IReadOnlyList<TagSummaryDto>>>();
        _tags.ListSummaryAsync(Arg.Any<CancellationToken>()).Returns(slow.Task, Task.FromResult(TestData.Ok<IReadOnlyList<TagSummaryDto>>([TestData.TagSummary("fresh", 2), TestData.TagSummary("newer", 3)])));
        _tags.CreateAsync(Arg.Any<CreateTagRequest>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail<TagDto>(ApiErrorCodes.ApiTimeout, "TechStrap took too long to answer. Try again."));
        var cut = RenderPage();
        cut.Find("#ts-tag-name").Input("Refund");
        cut.Find("form.ts-tag-create").Submit();
        cut.Find(".ts-conflict[role=alert] button").Click();
        cut.FindAll("tbody tr").Count.ShouldBe(2);

        slow.SetResult(TestData.Ok<IReadOnlyList<TagSummaryDto>>([TestData.TagSummary("stale", 1)]));
        cut.FindComponent<TagsContent>().Render();

        cut.FindAll("tbody tr").Select(r => r.Children[0].TextContent.Trim()).ShouldBe(["fresh", "newer"]);
    }

    [Fact]
    public void A_400_for_the_slug_while_editing_goes_above_the_list_and_does_not_block_the_next_save()
    {
        _tags.UpdateAsync(Arg.Any<Guid>(), Arg.Any<UpdateTagRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result<TagDto>.Failure(new ResultError("x-invalid", "The slug is wrong.", ResultErrorKind.Validation, "slug")));
        var cut = RenderPage();
        Row(cut, "urgent").QuerySelector("button.ts-edit")!.Click();

        cut.Find("button.ts-save").Click();

        cut.Find(".ts-conflict[role=alert]").TextContent.ShouldContain("The slug is wrong.");
        cut.Find("button.ts-save").Click();
        Updates().Count.ShouldBe(2);
    }
}
