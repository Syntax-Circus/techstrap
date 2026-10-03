using Microsoft.Extensions.Time.Testing;
using TechStrap.Domain.Tickets;

namespace TechStrap.Domain.Tests.Tickets;

public sealed class TagTests
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 2, 9, 0, 0, TimeSpan.Zero));

    [Fact]
    public void A_tag_normalises_its_colour()
    {
        var tag = Tag.Create("billing", "Billing", "#aa00ff", _clock).Value;

        tag.Colour.ShouldBe("#AA00FF");
        tag.Slug.ShouldBe("billing");
    }

    [Theory]
    [InlineData("Billing")]
    [InlineData("bill ing")]
    [InlineData("")]
    public void A_non_slug_is_rejected(string slug)
    {
        Tag.Create(slug, "Billing", "#AA00FF", _clock).Error!.Code.ShouldBe("slug-invalid");
    }

    [Fact]
    public void A_bad_colour_is_rejected()
    {
        Tag.Create("billing", "Billing", "purple", _clock).Error!.Code.ShouldBe("colour-invalid");
    }

    [Fact]
    public void Updating_changes_name_and_colour_but_never_the_slug()
    {
        var tag = Tag.Create("billing", "Billing", "#AA00FF", _clock).Value;

        tag.Update("Payments", "#00AA00").IsSuccess.ShouldBeTrue();

        tag.Name.ShouldBe("Payments");
        tag.Colour.ShouldBe("#00AA00");
        tag.Slug.ShouldBe("billing");
    }
}
