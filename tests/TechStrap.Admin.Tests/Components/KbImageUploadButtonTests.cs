using Bunit;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Kb;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.Kb;

namespace TechStrap.Admin.Tests.Components;

/// <summary>The picture button: a wrong type or an oversize picture is refused before anything is sent, and a picture the API refuses never changes the article (Review Focus 2, the Admin half; PHASE-08 T18).</summary>
public sealed class KbImageUploadButtonTests : AdminComponentTest
{
    private readonly IKbClient _kb = Substitute.For<IKbClient>();
    private readonly List<KbUploadedImage> _uploaded = [];

    public KbImageUploadButtonTests()
    {
        _kb.UploadImageAsync(Arg.Any<KbImageFile>(), Arg.Any<CancellationToken>()).Returns(TestData.Ok(new KbImageUploadResponse("kb-images/a.png", "https://api.example/kb-images/a.png")));
        Services.AddSingleton(_kb);
    }

    private IRenderedComponent<KbImageUploadButton> RenderButton(bool disabled = false) =>
        Render<KbImageUploadButton>(p => p.Add(b => b.Disabled, disabled).Add(b => b.OnUploaded, image => _uploaded.Add(image)));

    private static void Pick(IRenderedComponent<KbImageUploadButton> cut, string name, int bytes = 4, string type = "image/png") =>
        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromBinary(new byte[bytes], name, null, type));

    private int Uploads() => _kb.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(IKbClient.UploadImageAsync));

    [Fact]
    public void A_picture_is_uploaded_once_and_reported_with_its_address_and_an_alt_text_from_its_file_name()
    {
        var cut = RenderButton();

        Pick(cut, "login-screen.PNG", bytes: 8);

        cut.WaitForAssertion(() => _uploaded.ShouldBe([new KbUploadedImage("login screen", "https://api.example/kb-images/a.png")]));
        Uploads().ShouldBe(1);
        var file = (KbImageFile)_kb.ReceivedCalls().Single(c => c.GetMethodInfo().Name == nameof(IKbClient.UploadImageAsync)).GetArguments()[0]!;
        file.FileName.ShouldBe("login-screen.PNG");
        file.ContentType.ShouldBe("image/png");
        using var stream = file.OpenRead();
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        copy.Length.ShouldBe(8);
        cut.FindAll("[role=alert]").ShouldBeEmpty();
    }

    [Fact]
    public void The_upload_is_a_write_that_is_never_cancelled_by_the_screen()
    {
        var cut = RenderButton();

        Pick(cut, "a.png");

        cut.WaitForAssertion(() => Uploads().ShouldBe(1));
        var token = (CancellationToken)_kb.ReceivedCalls().Single(c => c.GetMethodInfo().Name == nameof(IKbClient.UploadImageAsync)).GetArguments()[1]!;
        token.CanBeCanceled.ShouldBeFalse();
    }

    [Theory]
    [InlineData("drawing.svg", "image/svg+xml")]
    [InlineData("notes.pdf", "application/pdf")]
    [InlineData("noextension", "image/png")]
    [InlineData("shot.png.exe", "image/png")]
    public void A_file_that_is_not_a_png_jpeg_gif_or_webp_is_refused_before_anything_is_sent(string name, string type)
    {
        var cut = RenderButton();

        Pick(cut, name, type: type);

        cut.Find("[role=alert]").TextContent.ShouldBe("Use a PNG, JPEG, GIF or WebP picture.");
        Uploads().ShouldBe(0);
        _uploaded.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("a.png")]
    [InlineData("a.JPG")]
    [InlineData("a.jpeg")]
    [InlineData("a.gif")]
    [InlineData("a.webp")]
    public void The_four_picture_types_are_accepted_by_extension(string name)
    {
        var cut = RenderButton();

        Pick(cut, name);

        cut.WaitForAssertion(() => _uploaded.Count.ShouldBe(1));
    }

    [Fact]
    public void A_picture_over_the_limit_is_refused_before_anything_is_sent_and_the_message_names_the_limit()
    {
        var cut = RenderButton();

        Pick(cut, "huge.png", bytes: (int)KbLimits.MaxImageBytes + 1);

        cut.Find("[role=alert]").TextContent.ShouldBe("This picture is larger than 5 MB.");
        Uploads().ShouldBe(0);
        _uploaded.ShouldBeEmpty();
    }

    [Fact]
    public void A_picture_exactly_at_the_limit_is_sent()
    {
        var cut = RenderButton();

        Pick(cut, "edge.png", bytes: (int)KbLimits.MaxImageBytes);

        cut.WaitForAssertion(() => _uploaded.Count.ShouldBe(1));
    }

    [Fact]
    public void The_input_only_offers_the_four_types()
    {
        var cut = RenderButton();

        cut.Find("input[type=file]").GetAttribute("accept").ShouldBe(".png,.jpg,.jpeg,.gif,.webp");
        cut.Find("label.ts-kb-image-label").TextContent.ShouldBe("Add image");
    }

    [Theory]
    [InlineData(ApiErrorCodes.KbImageTypeNotAllowed, "Use a PNG, JPEG, GIF or WebP picture.")]
    [InlineData(ApiErrorCodes.KbImageTooLarge, "This picture is larger than 5 MB.")]
    [InlineData(ApiErrorCodes.RequestTooLarge, "This picture is larger than 5 MB.")]
    [InlineData(ApiErrorCodes.ApiTimeout, "The upload may not have finished. Nothing was added to the article; pick the picture again.")]
    [InlineData(ApiErrorCodes.ApiUnavailable, "The upload may not have finished. Nothing was added to the article; pick the picture again.")]
    public void A_picture_the_api_refuses_or_may_not_have_stored_says_so_and_adds_nothing(string code, string message)
    {
        _kb.UploadImageAsync(Arg.Any<KbImageFile>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail<KbImageUploadResponse>(code, "from the API"));
        var cut = RenderButton();

        Pick(cut, "a.png");

        cut.WaitForAssertion(() => cut.Find("[role=alert]").TextContent.ShouldBe(message));
        _uploaded.ShouldBeEmpty();
    }

    [Fact]
    public void Any_other_failure_shows_the_api_message_after_a_fixed_sentence()
    {
        _kb.UploadImageAsync(Arg.Any<KbImageFile>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail<KbImageUploadResponse>("validation-failed", "The file is missing."));
        var cut = RenderButton();

        Pick(cut, "a.png");

        cut.WaitForAssertion(() => cut.Find("[role=alert]").TextContent.ShouldBe("Couldn't upload the picture. Nothing was added to the article. The file is missing."));
    }

    [Fact]
    public void An_upload_that_throws_says_it_may_not_have_finished_and_never_shows_the_exception_text()
    {
        _kb.UploadImageAsync(Arg.Any<KbImageFile>(), Arg.Any<CancellationToken>()).Returns<Task<Result<KbImageUploadResponse>>>(_ => throw new IOException("C:\\Users\\agent\\secret.png"));
        var cut = RenderButton();

        Pick(cut, "a.png");

        cut.WaitForAssertion(() => cut.Find("[role=alert]").TextContent.ShouldContain("may not have finished"));
        cut.Markup.ShouldNotContain("secret.png");
        _uploaded.ShouldBeEmpty();
    }

    [Fact]
    public async Task While_it_uploads_the_agent_is_told_and_the_input_is_off_and_afterwards_the_same_picture_can_be_picked_again()
    {
        var pending = new TaskCompletionSource<Result<KbImageUploadResponse>>();
        _kb.UploadImageAsync(Arg.Any<KbImageFile>(), Arg.Any<CancellationToken>()).Returns(_ => pending.Task);
        var cut = RenderButton();

        // The file input waits for the handler to finish, and the handler waits for the answer: the pick runs on its own thread.
        var picking = Task.Run(() => Pick(cut, "a.png"), Xunit.TestContext.Current.CancellationToken);

        cut.WaitForAssertion(() => cut.Find("[role=status]").TextContent.ShouldBe("Uploading the image\u2026"));
        cut.Find("input[type=file]").HasAttribute("disabled").ShouldBeTrue();
        var firstInput = cut.FindComponent<InputFile>().Instance;

        pending.SetResult(TestData.Ok(new KbImageUploadResponse("kb-images/a.png", "https://api.example/kb-images/a.png")));
        await picking;

        cut.WaitForAssertion(() => cut.FindAll("[role=status]").ShouldBeEmpty());
        cut.Find("input[type=file]").HasAttribute("disabled").ShouldBeFalse();
        cut.FindComponent<InputFile>().Instance.ShouldNotBeSameAs(firstInput);
    }

    [Fact]
    public async Task A_picture_that_finishes_after_the_screen_is_gone_changes_nothing()
    {
        var pending = new TaskCompletionSource<Result<KbImageUploadResponse>>();
        _kb.UploadImageAsync(Arg.Any<KbImageFile>(), Arg.Any<CancellationToken>()).Returns(_ => pending.Task);
        var cut = RenderButton();
        var picking = Task.Run(() => Pick(cut, "a.png"), Xunit.TestContext.Current.CancellationToken);
        cut.WaitForAssertion(() => Uploads().ShouldBe(1));

        cut.Instance.Dispose();
        pending.SetResult(TestData.Ok(new KbImageUploadResponse("kb-images/a.png", "https://api.example/kb-images/a.png")));
        await picking;
        await cut.InvokeAsync(() => { });

        _uploaded.ShouldBeEmpty();
    }

    [Fact]
    public void A_disabled_button_cannot_be_used()
    {
        var cut = RenderButton(disabled: true);

        cut.Find("input[type=file]").HasAttribute("disabled").ShouldBeTrue();
    }
}
