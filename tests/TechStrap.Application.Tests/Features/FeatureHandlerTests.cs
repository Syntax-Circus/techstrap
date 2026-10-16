using NSubstitute;
using TechStrap.Application.Features;
using TechStrap.Application.Persistence;
using TechStrap.Application.Tests.Knowledge;
using TechStrap.Application.Tests.Support;
using TechStrap.Contracts.Features;
using TechStrap.Domain.Features;

namespace TechStrap.Application.Tests.Features;

public sealed class FeatureHandlerTests
{
    private readonly KbFixture _f = new();
    private readonly IFeatureRepository _features = Substitute.For<IFeatureRepository>();

    private FeatureService Handler() =>
        new(_f.Claims, _f.Agents, _f.Products, _features, _f.Renderer, Substitute.For<IAdminEventRepository>(), UnitOfWorkSubstitute.Create(), _f.Clock);

    private Feature Stored(FeaturePublicationState state = FeaturePublicationState.Draft)
    {
        var f = Feature.Restore(Guid.NewGuid(), _f.Orbitly.Id, "dark-mode", "Dark mode", "# Details", state, FeatureProgressStatus.Planned, _f.Clock.GetUtcNow(), _f.Clock.GetUtcNow(), 5);
        _features.GetAsync(f.Id, Arg.Any<CancellationToken>()).Returns(f);
        return f;
    }
    [Fact]

    public async Task Stale_updates_do_not_change_a_feature()
    {
        var f = Stored();
        var r = await Handler().UpdateAsync(f.Id, new("New", "Body", "Shipped", 4), TestContext.Current.CancellationToken);
        r.IsFailure.ShouldBeTrue();
        f.Title.ShouldBe("Dark mode");
    }
    [Fact]

    public async Task Public_detail_hides_drafts_even_if_repository_returns_one()
    {
        var f = Stored();
        _f.Products.GetByKeyAsync("orbitly", Arg.Any<CancellationToken>()).Returns(_f.Orbitly);
        _features.GetBySlugAsync(f.ProductId, f.Slug, Arg.Any<CancellationToken>()).Returns(f);
        var r = await Handler().GetPublicAsync("orbitly", f.Slug, TestContext.Current.CancellationToken);
        r.IsFailure.ShouldBeTrue();
    }
    [Fact]

    public async Task Public_detail_uses_the_sanitized_renderer()
    {
        var f = Stored(FeaturePublicationState.Published);
        _f.Products.GetByKeyAsync("orbitly", Arg.Any<CancellationToken>()).Returns(_f.Orbitly);
        _features.GetBySlugAsync(f.ProductId, f.Slug, Arg.Any<CancellationToken>()).Returns(f);
        _f.Renderer.Render(f.DescriptionMarkdown).Returns("<h1>Details</h1>");
        var r = await Handler().GetPublicAsync("orbitly", f.Slug, TestContext.Current.CancellationToken);
        r.Value.DescriptionHtml.ShouldBe("<h1>Details</h1>");
    }
    [Fact]

    public async Task Unknown_product_cannot_create_a_feature()
    {
        var r = await Handler().CreateAsync(new(Guid.NewGuid(), "dark-mode", "Dark mode", "Details"), TestContext.Current.CancellationToken);
        r.Errors[0].Code.ShouldBe("product-not-found");
    }
    [Fact]

    public async Task Unpublishing_keeps_progress_and_removes_publication()
    {
        var f = Stored(FeaturePublicationState.Published);
        var r = await Handler().SetPublishedAsync(f.Id, false, 5, TestContext.Current.CancellationToken);
        r.Value.PublicationState.ShouldBe("Draft");
        r.Value.ProgressStatus.ShouldBe("Planned");
    }
    [Fact]

    public async Task Unknown_progress_values_are_rejected()
    {
        var f = Stored();
        var r = await Handler().UpdateAsync(f.Id, new("Title", "Body", "999", 5), TestContext.Current.CancellationToken);
        r.IsFailure.ShouldBeTrue();
        f.ProgressStatus.ShouldBe(FeatureProgressStatus.Planned);
    }
    [Fact]
    public async Task Duplicate_slugs_are_conflicts_without_staging_new_content()
    {
        _features.SlugTakenAsync(_f.Orbitly.Id, "dark-mode", Arg.Any<CancellationToken>()).Returns(true);
        var result = await Handler().CreateAsync(new(_f.Orbitly.Id, "dark-mode", "Dark mode", "Details"), TestContext.Current.CancellationToken);
        result.Errors[0].Code.ShouldBe("feature-slug-taken");
        result.Errors[0].Kind.ShouldBe(SyntaxCircus.Common.ResultErrorKind.Conflict);
        _features.DidNotReceive().Add(Arg.Any<Feature>());
    }
}
