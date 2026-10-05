using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Application.Knowledge;

namespace TechStrap.Application.Tests.Knowledge;

public sealed class UploadKbImageRequestHandlerTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private readonly KbFixture _kb = new();
    private readonly IKbImageStore _store = Substitute.For<IKbImageStore>();
    private readonly IKbImageUrls _urls = Substitute.For<IKbImageUrls>();

    private UploadKbImageRequestHandler Handler() => new(_kb.Claims, _kb.Agents, _store, _urls);

    [Fact]
    public async Task The_response_carries_the_stored_key_and_the_absolute_url_for_its_file_name()
    {
        var image = new IncomingKbImage(3, new MemoryStream([1, 2, 3]));
        _store.SaveAsync(image, Ct).Returns(Result<StoredKbImage>.Success(new StoredKbImage("kb-images/abc.png", "abc.png", "image/png", 3)));
        _urls.UrlFor("abc.png").Returns("https://api.example.com/kb-images/abc.png");

        var result = await Handler().HandleAsync(image, Ct);

        result.Value.Key.ShouldBe("kb-images/abc.png");
        result.Value.Url.ShouldBe("https://api.example.com/kb-images/abc.png");
    }

    [Fact]
    public async Task A_refused_image_returns_the_stores_error_and_builds_no_url()
    {
        var image = new IncomingKbImage(3, new MemoryStream([1, 2, 3]));
        _store.SaveAsync(image, Ct).Returns(Result<StoredKbImage>.Failure(new ResultError("kb-image-type-not-allowed", "No.", ResultErrorKind.Validation, "file")));

        var result = await Handler().HandleAsync(image, Ct);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe("kb-image-type-not-allowed");
        _urls.DidNotReceiveWithAnyArgs().UrlFor(default!);
    }

    [Fact]
    public async Task A_deactivated_agent_is_refused_and_nothing_is_stored()
    {
        var image = new IncomingKbImage(3, new MemoryStream([1, 2, 3]));
        _kb.Agent.SetActive(false);

        var result = await Handler().HandleAsync(image, Ct);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe("agent-inactive");
        await _store.DidNotReceiveWithAnyArgs().SaveAsync(default!, Ct);
        _urls.DidNotReceiveWithAnyArgs().UrlFor(default!);
    }

    [Fact]
    public async Task A_caller_with_no_agent_claims_is_refused_and_nothing_is_stored()
    {
        var image = new IncomingKbImage(3, new MemoryStream([1, 2, 3]));
        _kb.Claims.Current.Returns((TechStrap.Application.Agents.AgentClaims?)null);

        var result = await Handler().HandleAsync(image, Ct);

        result.IsFailure.ShouldBeTrue();
        await _store.DidNotReceiveWithAnyArgs().SaveAsync(default!, Ct);
    }

    [Theory]
    [InlineData("0123456789abcdef0123456789abcdef.png", true)]
    [InlineData("0123456789abcdef0123456789abcdef.jpg", true)]
    [InlineData("0123456789abcdef0123456789abcdef.gif", true)]
    [InlineData("0123456789abcdef0123456789abcdef.webp", true)]
    [InlineData("0123456789abcdef0123456789abcdef.jpeg", false)]
    [InlineData("0123456789abcdef0123456789abcdef.svg", false)]
    [InlineData("../0123456789abcdef0123456789abcdef.png", false)]
    [InlineData("0123456789abcdef0123456789abcdef.png\n", false)]
    [InlineData(null, false)]
    public void Only_the_exact_name_shape_the_store_writes_is_valid(string? name, bool valid) =>
        KbImageName.IsValid(name).ShouldBe(valid);
}
