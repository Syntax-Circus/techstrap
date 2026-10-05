using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Application.Content;
using TechStrap.Application.Knowledge;
using TechStrap.Contracts.Kb;

namespace TechStrap.Application.Tests.Knowledge;

public sealed class RenderKbPreviewRequestHandlerTests
{
    private readonly IKbContentRenderer _renderer = Substitute.For<IKbContentRenderer>();

    private RenderKbPreviewRequestHandler Handler() => new(_renderer);

    [Fact]
    public async Task The_preview_is_the_output_of_the_shared_kb_renderer()
    {
        _renderer.Render("# Hi").Returns("<h1>Hi</h1>\n");

        var result = await Handler().HandleAsync(new KbPreviewRequest("# Hi"), TestContext.Current.CancellationToken);

        result.Value.Html.ShouldBe("<h1>Hi</h1>\n");
        _renderer.Received(1).Render("# Hi");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task An_empty_source_previews_as_empty_html_without_rendering(string? body)
    {
        var result = await Handler().HandleAsync(new KbPreviewRequest(body), TestContext.Current.CancellationToken);

        result.Value.Html.ShouldBeEmpty();
        _renderer.DidNotReceiveWithAnyArgs().Render(default!);
    }

    [Fact]
    public async Task A_source_at_the_limit_is_rendered()
    {
        var body = new string('a', KbLimits.MaxPreviewChars);
        _renderer.Render(body).Returns("<p>ok</p>");

        var result = await Handler().HandleAsync(new KbPreviewRequest(body), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task A_body_over_the_complexity_cap_is_a_validation_error_on_body_and_is_never_rendered()
    {
        _renderer.IsTooComplex("| a |").Returns(true);

        var result = await Handler().HandleAsync(new KbPreviewRequest("| a |"), TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            error => error.Kind.ShouldBe(ResultErrorKind.Validation),
            error => error.Code.ShouldBe("kb-body-too-complex"),
            error => error.Target.ShouldBe("body"));
        _renderer.DidNotReceiveWithAnyArgs().Render(default!);
    }

    [Fact]
    public async Task A_cancelled_preview_is_not_rendered()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => Handler().HandleAsync(new KbPreviewRequest("# Hi"), cts.Token));

        _renderer.DidNotReceiveWithAnyArgs().Render(default!);
    }

    [Fact]
    public async Task A_source_over_the_limit_is_a_validation_error_on_body_and_is_never_rendered()
    {
        var result = await Handler().HandleAsync(new KbPreviewRequest(new string('a', KbLimits.MaxPreviewChars + 1)), TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            error => error.Kind.ShouldBe(ResultErrorKind.Validation),
            error => error.Code.ShouldBe("body-too-long"),
            error => error.Target.ShouldBe("body"));
        _renderer.DidNotReceiveWithAnyArgs().Render(default!);
    }
}
