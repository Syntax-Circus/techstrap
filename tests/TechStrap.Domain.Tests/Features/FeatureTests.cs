using Microsoft.Extensions.Time.Testing;
using TechStrap.Domain.Features;

namespace TechStrap.Domain.Tests.Features;

public sealed class FeatureTests
{
    private readonly FakeTimeProvider _clock = new();
    [Fact]

    public void A_feature_starts_private_and_under_consideration()
    {
        var f = Feature.Create(Guid.NewGuid(), "dark-mode", "Dark mode", "Details", _clock).Value;
        f.PublicationState.ShouldBe(FeaturePublicationState.Draft);
        f.ProgressStatus.ShouldBe(FeatureProgressStatus.UnderConsideration);
    }
    [Fact]

    public void Publishing_and_unpublishing_preserve_identity_and_progress()
    {
        var p = Guid.NewGuid();
        var f = Feature.Create(p, "dark-mode", "Dark mode", "Details", _clock).Value;
        f.Update("Better title", "Updated", FeatureProgressStatus.Planned, _clock).IsSuccess.ShouldBeTrue();
        f.SetPublished(true, _clock);
        f.SetPublished(false, _clock);
        f.PublicationState.ShouldBe(FeaturePublicationState.Draft);
        f.ProgressStatus.ShouldBe(FeatureProgressStatus.Planned);
        f.ProductId.ShouldBe(p);
        f.Slug.ShouldBe("dark-mode");
    }
    [Fact]

    public void Invalid_edits_do_not_modify_content()
    {
        var f = Feature.Create(Guid.NewGuid(), "dark-mode", "Dark mode", "Details", _clock).Value;
        f.Update(" ", "Changed", FeatureProgressStatus.Shipped, _clock).IsFailure.ShouldBeTrue();
        f.Title.ShouldBe("Dark mode");
        f.ProgressStatus.ShouldBe(FeatureProgressStatus.UnderConsideration);
    }
    [Fact]

    public void Slug_title_and_description_are_required()
    {
        Feature.Create(Guid.NewGuid(), "Bad Slug", "Title", "Body", _clock).IsFailure.ShouldBeTrue();
        Feature.Create(Guid.NewGuid(), "valid", "Title", " ", _clock).IsFailure.ShouldBeTrue();
    }
}
