using TechStrap.Portal.Seo;

namespace TechStrap.Portal.Tests.Seo;

/// <summary>The description and title rules: an owner-written value is used when long enough, a generated one always lands in 110 to 160 characters.</summary>
public sealed class SeoTextTests
{
    private const string Fallback = "A generated fallback description that is long enough to pass the floor of one hundred and ten characters, as real ones are.";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Too short.")]
    public void Description_BlankOrShortCandidate_UsesFallback(string? candidate) =>
        SeoText.Description(candidate, () => Fallback).ShouldBe(Fallback);

    [Fact]
    public void Description_LongEnoughCandidate_IsKept()
    {
        var candidate = new string('a', 50) + " " + new string('b', 60);
        SeoText.Description(candidate, () => Fallback).ShouldBe(candidate);
    }

    [Fact]
    public void Description_TooLong_CutsOnWordBoundaryWithinLimit()
    {
        var candidate = string.Join(' ', Enumerable.Repeat("wordy", 60));
        var result = SeoText.Description(candidate, () => Fallback);
        result.Length.ShouldBeLessThanOrEqualTo(SeoText.DescriptionMaxLength);
        result.ShouldEndWith("…");
        result.TrimEnd('…').Split(' ').ShouldAllBe(w => w == "wordy");
    }

    [Fact]
    public void Description_NormalizesWhitespaceAndControlCharacters()
    {
        var candidate = "  Reset\tyour   password\r\nand\u0007 sign in again. " + new string('x', 100);
        SeoText.Description(candidate, () => Fallback).ShouldStartWith("Reset your password and sign in again. ");
    }

    [Fact]
    public void Title_CutsToSeventy()
    {
        var title = SeoText.Title(string.Join(' ', Enumerable.Repeat("title", 30)));
        title.Length.ShouldBeLessThanOrEqualTo(SeoText.TitleMaxLength);
        title.ShouldEndWith("…");
        SeoText.Title("  Short   title ").ShouldBe("Short title");
    }

    public static TheoryData<string, string[], int> KbInputs() => new()
    {
        { "Sin Forgiver", ["Account", "Billing", "Troubleshooting"], 24 },
        { "X", [], 0 },
        { "X", [], 1 },
        { "Sin Forgiver", [], 12 },
        { new string('P', 120), ["Account", "Billing"], 5 },
        { "Sin Forgiver", Enumerable.Range(1, 30).Select(i => $"Category number {i}").ToArray(), 300 },
        { "Sin Forgiver", [new string('C', 200)], 3 },
    };

    [Theory]
    [MemberData(nameof(KbInputs))]
    public void KbHomeDescription_AlwaysInRange(string product, string[] categories, int articles)
    {
        var result = SeoText.KbHomeDescription(product, categories, articles);
        result.Length.ShouldBeInRange(SeoText.DescriptionMinLength, SeoText.DescriptionMaxLength);
    }

    [Theory]
    [MemberData(nameof(KbInputs))]
    public void ProductHomeDescription_AlwaysInRange(string product, string[] categories, int _)
    {
        var result = SeoText.ProductHomeDescription(product, categories);
        result.Length.ShouldBeInRange(SeoText.DescriptionMinLength, SeoText.DescriptionMaxLength);
    }

    public static TheoryData<string> ProductNames() => ["X", "Sin Forgiver", "Paperplane", new string('P', 40), new string('P', 120)];

    [Theory]
    [MemberData(nameof(ProductNames))]
    public void FormPageDescriptions_AlwaysInRange(string product)
    {
        SeoText.ContactDescription(product).Length.ShouldBeInRange(SeoText.DescriptionMinLength, SeoText.DescriptionMaxLength);
        SeoText.LostLinkDescription(product).Length.ShouldBeInRange(SeoText.DescriptionMinLength, SeoText.DescriptionMaxLength);
    }

    [Fact]
    public void KbHomeDescription_MentionsProductCountAndCategories()
    {
        var result = SeoText.KbHomeDescription("Sin Forgiver", ["Account", "Billing"], 24);
        result.ShouldContain("Sin Forgiver");
        result.ShouldContain("24");
        result.ShouldContain("Account and Billing");
    }

    [Fact]
    public void Clamp_never_splits_a_surrogate_pair()
    {
        // 68 letters, an emoji (two UTF-16 chars) and more letters with no space, so the cut falls inside the pair.
        var text = new string('a', 68) + "\U0001F600" + new string('b', 20);

        var result = SeoText.Clamp(text, 70);

        result.Length.ShouldBeLessThanOrEqualTo(70);
        result.ToCharArray().Count(char.IsHighSurrogate).ShouldBe(result.ToCharArray().Count(char.IsLowSurrogate));
    }
}
