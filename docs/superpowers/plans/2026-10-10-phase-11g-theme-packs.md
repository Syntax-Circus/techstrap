# PHASE-11g Theme Packs and Skins (engine) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Five built-in Portal theme packs, an Admin-editable deployment default, and a per-product skin of validated tokens, resolved by one shared function and rendered through the existing CSP-safe style carrier.

**Architecture:** A pure `SkinResolver` in Contracts merges Classic < deployment default pack < product pack < product token overrides, derives the on-colours and inks, and checks contrast. The Portal turns the resolved skin into custom properties on the existing `ts-accent-scope` wrapper plus `data-ts-*` preset attributes, emitting only what differs from Classic so an unskinned page renders byte-identically. The skin travels as validated JSON in one product column and a one-row `site_settings` table.

**Tech Stack:** .NET 10, System.Text.Json source generation (Contracts), EF Core 10 + Npgsql (tool-generated migration), ASP.NET Core controllers, Blazor SSR (Portal), SCSS via AspNetCore.SassCompiler, xUnit v3 + Shouldly + NSubstitute, Pester 5.

**Spec:** `docs/architecture/PHASE-11g-theme-packs.md` (decision D-053 in `docs/architecture/04-DECISION-LOG.md`).

## Global Constraints

- Branch `feat/phase-11g-skin-engine` (Task 1 is committed as `bf02e2a`; the plan file is committed next). Never commit to `main`. One pull request at the end; the owner merges.
- `dotnet build TechStrap.slnx -c Release` ends with 0 warnings after every task. Tests: `dotnet test --solution TechStrap.CI.slnf -c Release --no-build` needs Docker; per-project filtered runs while iterating.
- Architecture rules (tests in `tests/TechStrap.Architecture.Tests`): route literals only in `PortalRoutes`; Portal components build links only through `PortalLinks`; exactly one `[FromServices] I...Handler` per controller action; handler constructors take only Application interfaces, `TimeProvider`, `ILogger<T>`, `IOptions<T>`; the Worker never references the Api; every documented HTTP entry point in `02-ARCHITECTURE.md` sections 7.1-7.5 exists with the named handler and vice versa.
- Contracts carries no enums (string constants) and DTO changes are trailing optional parameters only, each with a `<param>` doc.
- Portal CSS rules that must keep passing (`tests/TechStrap.Portal.Tests`): no `<style>` element; every `[style]` element has class `ts-accent-scope`; no `url(` in Portal SCSS except `_fonts.scss`; compiled CSS has only `data:image/` or `.woff2` `url()` and no `@import`; every `ts-*` class used in markup has a `.ts-*` rule (presets therefore use `data-ts-*` attribute selectors, never composed class names); no `prefers-color-scheme`; `[data-bs-theme=dark]` stays empty.
- C#: file-scoped namespaces, `sealed` by default, `_camelCase` fields, non-ASCII only as `\u` escapes; docs and SCSS ASCII; new files LF; Edit tool for existing files (never `sed -i`).
- Tests: every awaiting test passes `TestContext.Current.CancellationToken` or carries a `Timeout`; RED output recorded before implementing; the named mutation per task must fail a test; restore it.
- Commits: Conventional Commits, explicit paths only (never `git add -A`, never `-f`), never commit `.superpowers/`, trailers exactly:
  ```
  Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi
  ```
- Never run `docker compose down -v`. No secret in code, tests or logs.

## Rulings carried by this plan (they refine the spec; Task 2 amends the spec and D-053 to match)

1. **`Border` has no contrast rule.** Dividers are decorative and Classic's own border (`#D4D4DC` on white) is 1.5:1; a 3:1 rule would fail today's look. `Focus` keeps the 3:1 rule.
2. **Packs are Contracts data resolved on the server into variables.** There is no SCSS pack mirror and no pack parity test; SCSS holds only preset rules (button, header, shadow, radius, border width, fonts). `data-ts-pack` is not emitted.
3. **Domain stores the skin as opaque validated JSON (`string?`, at most 2000 characters).** The grammar (keys, hex, fonts, enums, contrast) lives once in Contracts (`SkinRules`, `SkinResolver`, `SkinSerializer`) and is applied by the Application handlers; Domain only guards length.
4. **Classic's `Surface` is `#FFFFFF`** (today's cards have no fill), its `Focus` is its `Ink`, and the Portal emits a variable only when it differs from Classic, so a product with no skin renders exactly today's HTML.
5. **A product's accent always wins over a pack's `Brand`** (Brand = skin override, else product accent, else pack brand). The migration seeds the default pack `classic`; the owner picks another in the Admin (11h) or with `PUT api/settings/site`.
6. **Saving a skin is validated against the pack it would use now** (product pack, else the current deployment default). A later default-pack change can make an override fail contrast; at render a failing override is dropped, never written.

## Review Focus

1. **A product with no skin renders the same HTML as before this phase.** Task 6 test `An_unskinned_product_page_is_byte_identical` (golden string captured on `main`) and Task 2 `Classic_with_an_accent_emits_exactly_the_three_accent_properties`.
2. **A hostile token value must never reach `style=` or an attribute.** Task 2 `A_hostile_skin_resolves_to_pack_values_and_reports_problems`; Task 6 `Hostile_skin_values_never_reach_the_page`.
3. **An old client that omits `Skin` must not clear a product's skin.** Task 5 `A_null_skin_leaves_the_stored_skin` and the raw-JSON PUT test.
4. **Neutral pages must be identical for every address.** Task 6 `Neutral_pages_use_only_the_default_pack_and_are_identical_across_hosts`.
5. **A dark pack must stay readable and must not follow the OS.** Task 2 `Every_pack_passes_its_own_contrast_rules`; Task 6 `StyleBuildTests` stay green (no `prefers-color-scheme`).

---

### Task 1: Spec, D-053, amended lines, rows, pins (DONE)

Committed as `bf02e2a`. Nothing to do.

---

### Task 2: Contracts skin model, packs, resolver, rules, serializer

**Files:**
- Create: `src/TechStrap.Contracts/Skins/ProductSkin.cs`, `SkinValues.cs`, `SkinFonts.cs`, `SkinTokens.cs`, `SkinPacks.cs`, `SkinResolver.cs`, `SkinRules.cs`, `SkinCss.cs`, `SkinSerializer.cs`
- Modify: `src/TechStrap.Contracts/Branding/ProductAccent.cs` (add `ReadableOn`)
- Modify: `src/TechStrap.Contracts/README.md` (Stability line, `### 0.4.0` note)
- Modify: `docs/architecture/PHASE-11g-theme-packs.md`, `docs/architecture/04-DECISION-LOG.md` (D-053 rulings 1 to 3 above)
- Test: `tests/TechStrap.Application.Tests/Skins/SkinResolverTests.cs`, `SkinRulesTests.cs`, `SkinSerializerTests.cs`, `SkinCssTests.cs`, `tests/TechStrap.Application.Tests/Products/ProductAccentReadableOnTests.cs` (create)

**Interfaces (produces; every later task uses these exact names):**
```csharp
namespace TechStrap.Contracts.Skins;

public sealed record ProductSkin(
    string? Pack = null, string? Background = null, string? Surface = null, string? Ink = null, string? Muted = null,
    string? Border = null, string? Brand = null, string? Chrome = null, string? Focus = null,
    string? HeadingFont = null, string? BodyFont = null, string? Radius = null, int? BorderWidth = null,
    string? Shadow = null, string? Button = null, string? Header = null)
{ public bool IsEmpty { get; } }                       // true when every field is null

public static class SkinValues
{
    public const string Light = "light", Dark = "dark";
    public const string RadiusSquare = "square", RadiusSoft = "soft", RadiusRound = "round";
    public const string ShadowNone = "none", ShadowSoft = "soft", ShadowHard = "hard";
    public const string ButtonFlat = "flat", ButtonBevel = "bevel", ButtonOutline = "outline";
    public const string HeaderPlain = "plain", HeaderSolid = "solid", HeaderBand = "band";
    public static bool IsRadius(string? v); public static bool IsShadow(string? v);
    public static bool IsButton(string? v); public static bool IsHeader(string? v);
    public static string RadiusRem(string radius);     // square "0", soft ".25rem", round ".75rem"
}

public static class SkinFonts
{
    public const string PlexSans = "plex-sans", Nunito = "nunito", Atkinson = "atkinson", SourceSerif = "source-serif", Pixelify = "pixelify";
    public static IReadOnlyList<string> All { get; }
    public static bool IsKnown(string? key);
    public static string Stack(string key);            // CSS font-family value with single-quoted names and a generic fallback
}

public sealed record SkinTokens(                         // a complete set (a pack)
    string Background, string Surface, string Ink, string Muted, string Border, string Brand, string Chrome, string Focus,
    string HeadingFont, string BodyFont, string Radius, int BorderWidth, string Shadow, string Button, string Header);

public sealed record SkinPack(string Key, string Name, string Scheme, SkinTokens Tokens);

public static class SkinPacks
{
    public const string DefaultKey = "classic";
    public static IReadOnlyList<SkinPack> All { get; }  // classic, slate, paper, contrast, midnight
    public static SkinPack Classic { get; }
    public static bool IsKnown(string? key);
    public static SkinPack? Find(string? key);
}

public sealed record ResolvedSkin(
    string Pack, string Scheme, SkinTokens Tokens, bool BrandIsExplicit, string OnBrand, string BrandInk, string OnChrome);

public sealed record SkinProblem(string Code, string Target);     // Code: skin-invalid | skin-contrast-invalid; Target: the token or pair, e.g. "ink/background"
public sealed record SkinResolution(ResolvedSkin Skin, IReadOnlyList<SkinProblem> Problems);

public static class SkinResolver
{
    public static SkinResolution Resolve(string? deploymentDefaultPack, ProductSkin? productSkin, string? productAccent);
}

public static class SkinRules
{
    public const int MaxJsonLength = 2000;
    public static IReadOnlyList<SkinProblem> Validate(ProductSkin skin);   // format only: keys, hex, enums, 1..4
}

public static class SkinSerializer
{
    public static string? Serialize(ProductSkin? skin);                    // null for null/empty; compact camelCase, nulls omitted
    public static bool TryDeserialize(string? json, out ProductSkin? skin); // strict: unknown member, bad JSON or > MaxJsonLength -> false
}

public static class SkinCss
{
    public static IReadOnlyList<KeyValuePair<string, string>> Properties(ResolvedSkin skin);   // only what differs from Classic; accent trio iff BrandIsExplicit
    public static IReadOnlyList<KeyValuePair<string, string>> Attributes(ResolvedSkin skin);   // data-ts-shadow, data-ts-button, data-ts-header iff not the Classic value
}

// ProductAccent (existing class)
public static string ReadableOn(string foregroundHex, string backgroundHex);   // fg itself when >= 4.5:1, else stepped toward black (light bg) or white (dark bg), at most 24 steps; TryDerive's AccentInk equals ReadableOn(accent, "#FFFFFF")
```

Pack data (exact; all contrast-checked, `Brand` shown is the pack's own and is overridden by a product accent):

| Pack | Scheme | Background | Surface | Ink | Muted | Border | Brand | Chrome | Focus | Heading font | Body font | Radius | Border w | Shadow | Button | Header |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| classic | light | #FFFFFF | #FFFFFF | #1B1B22 | #4A4A57 | #D4D4DC | #1D4FA8 | #1B1B22 | #1B1B22 | plex-sans | plex-sans | soft | 1 | none | flat | plain |
| slate | light | #F8FAFC | #FFFFFF | #0F172A | #475569 | #CBD5E1 | #334155 | #0F172A | #0F172A | plex-sans | plex-sans | soft | 1 | soft | flat | solid |
| paper | light | #FBF7EF | #FFFDF8 | #2B2118 | #5C4F42 | #DDD0BC | #8A3B12 | #3B2A1E | #2B2118 | source-serif | nunito | soft | 1 | none | flat | band |
| contrast | light | #FFFFFF | #FFFFFF | #000000 | #333333 | #000000 | #0033A0 | #000000 | #000000 | atkinson | atkinson | square | 3 | none | outline | solid |
| midnight | dark | #0F1420 | #181F2E | #F1F5F9 | #A9B4C6 | #2B364A | #6EA8FF | #0A0E17 | #FFD166 | plex-sans | plex-sans | soft | 1 | soft | flat | solid |

Font stacks: `plex-sans` `'IBM Plex Sans', system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif`; `nunito` `'Nunito', system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif`; `atkinson` `'Atkinson Hyperlegible', system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif`; `source-serif` `'Source Serif 4', Georgia, 'Times New Roman', serif`; `pixelify` `'Pixelify Sans', system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif`.

Resolver algorithm (exact):
1. `defaultPack = SkinPacks.Find(deploymentDefaultPack) ?? Classic`; `pack = SkinPacks.Find(productSkin?.Pack) ?? defaultPack`. An unknown `productSkin.Pack` adds problem `("skin-invalid","pack")` and uses `defaultPack`.
2. Start from `pack.Tokens`. Apply each non-null override only when it passes `SkinRules` for that field (hex `^#[0-9A-Fa-f]{6}$` normalised to upper case; font in `SkinFonts`; enums known; `BorderWidth` 1 to 4); each failing override adds `("skin-invalid", <fieldName camel>)` and is ignored.
3. `Brand` = valid `productSkin.Brand`, else valid `productAccent`, else pack brand. `BrandIsExplicit` is true when it came from either of the first two.
4. Contrast pairs on the candidate tokens: `ink/background >= 4.5`, `ink/surface >= 4.5`, `muted/background >= 4.5`, `focus/background >= 3.0` (via `ProductAccent.ContrastRatio`). For each failing pair, revert to the pack value every member of the pair that was overridden, add `("skin-contrast-invalid", "<a>/<b>")`, then re-evaluate once. (A pack itself never fails; a test proves it.)
5. Derive: `OnBrand = ProductAccent.TryDerive(brand).OnAccent`, `BrandInk = ProductAccent.ReadableOn(brand, background)`, `OnChrome = ProductAccent.TryDerive(chrome).OnAccent`.

`SkinCss.Properties` order and names: when `BrandIsExplicit`: `--ts-accent` (brand), `--ts-on-accent`, `--ts-accent-ink` first, in that order (exactly today's trio). Then, each only when it differs from Classic's token: `--p-bg` (background), `--ts-surface`, `--p-ink`, `--p-ink2` (muted), `--p-line` (border), `--ts-chrome`, `--ts-on-chrome` (when chrome differs), `--ts-focus`, `--ts-radius` (`SkinValues.RadiusRem`), `--ts-border-w` (`{n}px`), `--ts-font-heading` and `--ts-font-body` (`SkinFonts.Stack`). `Attributes`: `data-ts-shadow`, `data-ts-button`, `data-ts-header` when not `none`/`flat`/`plain`.

- [ ] **Step 1: Write the failing tests**

`tests/TechStrap.Application.Tests/Skins/SkinResolverTests.cs`:

```csharp
using TechStrap.Contracts.Branding;
using TechStrap.Contracts.Skins;

namespace TechStrap.Application.Tests.Skins;

public sealed class SkinResolverTests
{
    [Fact]
    public void Precedence_is_classic_then_default_pack_then_product_pack_then_overrides()
    {
        var none = SkinResolver.Resolve(null, null, null).Skin;
        none.Pack.ShouldBe("classic");
        none.Tokens.Background.ShouldBe("#FFFFFF");

        var defaultSlate = SkinResolver.Resolve("slate", null, null).Skin;
        defaultSlate.Tokens.Background.ShouldBe("#F8FAFC");

        var productPaper = SkinResolver.Resolve("slate", new ProductSkin(Pack: "paper"), null).Skin;
        productPaper.Pack.ShouldBe("paper");
        productPaper.Tokens.Background.ShouldBe("#FBF7EF");

        var overridden = SkinResolver.Resolve("slate", new ProductSkin(Pack: "paper", Background: "#fff8e4"), null);
        overridden.Skin.Tokens.Background.ShouldBe("#FFF8E4");
        overridden.Skin.Tokens.Ink.ShouldBe("#2B2118");
        overridden.Problems.ShouldBeEmpty();
    }

    [Fact]
    public void A_product_accent_wins_over_the_pack_brand_and_a_brand_override_wins_over_the_accent()
    {
        SkinResolver.Resolve("slate", null, "#F59E0B").Skin.Tokens.Brand.ShouldBe("#F59E0B");
        SkinResolver.Resolve("slate", new ProductSkin(Brand: "#112233"), "#F59E0B").Skin.Tokens.Brand.ShouldBe("#112233");
        SkinResolver.Resolve("slate", null, null).Skin.BrandIsExplicit.ShouldBeFalse();
        SkinResolver.Resolve("slate", null, "#F59E0B").Skin.BrandIsExplicit.ShouldBeTrue();
    }

    [Fact]
    public void Every_pack_passes_its_own_contrast_rules()
    {
        foreach (var pack in SkinPacks.All)
        {
            var t = pack.Tokens;
            ProductAccent.ContrastRatio(t.Ink, t.Background).ShouldBeGreaterThanOrEqualTo(4.5, pack.Key);
            ProductAccent.ContrastRatio(t.Ink, t.Surface).ShouldBeGreaterThanOrEqualTo(4.5, pack.Key);
            ProductAccent.ContrastRatio(t.Muted, t.Background).ShouldBeGreaterThanOrEqualTo(4.5, pack.Key);
            ProductAccent.ContrastRatio(t.Focus, t.Background).ShouldBeGreaterThanOrEqualTo(3.0, pack.Key);
            SkinResolver.Resolve(pack.Key, null, null).Problems.ShouldBeEmpty(pack.Key);
        }
    }

    [Fact]
    public void A_low_contrast_override_is_reported_and_dropped_to_the_pack_value()
    {
        var result = SkinResolver.Resolve("classic", new ProductSkin(Ink: "#EEEEEE"), null);

        result.Problems.ShouldContain(new SkinProblem("skin-contrast-invalid", "ink/background"));
        result.Skin.Tokens.Ink.ShouldBe("#1B1B22");
    }

    [Fact]
    public void A_hostile_skin_resolves_to_pack_values_and_reports_problems()
    {
        var hostile = new ProductSkin(
            Pack: "<script>", Background: "red;} body{display:none", Ink: "#GGGGGG", HeadingFont: "Comic Sans'; x", Radius: "50%",
            BorderWidth: 99, Shadow: "0 0 9px red", Button: "javascript:alert(1)", Header: "none");

        var result = SkinResolver.Resolve("classic", hostile, null);

        result.Skin.Tokens.ShouldBe(SkinPacks.Classic.Tokens);
        result.Problems.Select(p => p.Code).Distinct().ShouldBe(["skin-invalid"]);
        result.Problems.Count.ShouldBe(9);
    }

    [Fact]
    public void The_derived_colours_follow_the_background_and_the_brand()
    {
        var midnight = SkinResolver.Resolve("midnight", null, null).Skin;
        midnight.OnBrand.ShouldBe("#000000");
        ProductAccent.ContrastRatio(midnight.BrandInk, midnight.Tokens.Background).ShouldBeGreaterThanOrEqualTo(4.5);

        var classic = SkinResolver.Resolve("classic", null, "#F59E0B").Skin;
        ProductAccent.TryDerive("#F59E0B", out var accent).ShouldBeTrue();
        classic.OnBrand.ShouldBe(accent.OnAccent);
        classic.BrandInk.ShouldBe(accent.AccentInk);
    }
}
```

`tests/TechStrap.Application.Tests/Products/ProductAccentReadableOnTests.cs`:

```csharp
using TechStrap.Contracts.Branding;

namespace TechStrap.Application.Tests.Products;

public sealed class ProductAccentReadableOnTests
{
    [Theory]
    [InlineData("#F59E0B")]
    [InlineData("#1D4FA8")]
    [InlineData("#FFFF00")]
    [InlineData("#7C3AED")]
    [InlineData("#000000")]
    public void On_white_it_is_exactly_the_existing_accent_ink(string accent)
    {
        ProductAccent.TryDerive(accent, out var colors).ShouldBeTrue();

        ProductAccent.ReadableOn(accent, "#FFFFFF").ShouldBe(colors.AccentInk);
    }

    [Fact]
    public void On_a_dark_background_it_lightens_until_readable()
    {
        var ink = ProductAccent.ReadableOn("#1D4FA8", "#0F1420");

        ProductAccent.ContrastRatio(ink, "#0F1420").ShouldBeGreaterThanOrEqualTo(4.5);
        ink.ShouldNotBe("#1D4FA8");
    }

    [Fact]
    public void A_colour_that_already_reads_is_returned_as_entered_in_upper_case()
    {
        ProductAccent.ReadableOn("#6EA8FF", "#0F1420").ShouldBe("#6EA8FF");
    }
}
```

`SkinRulesTests.cs`: theory over `SkinRules.Validate`: valid full skin -> empty; `Background: "#abc"` -> `("skin-invalid","background")`; `BorderWidth: 0` and `5` invalid, `1` and `4` valid; `Pack: "nope"` invalid; font unknown invalid; `Header: "BAND"` invalid (case-sensitive). `SkinSerializerTests.cs`: round-trip of a full skin and of `new ProductSkin(Brand: "#112233")` (`{"brand":"#112233"}` exactly); empty skin serialises to `null`; `{"background":"#FFFFFF","extra":1}` -> `TryDeserialize` false; `"not json"` false; a 2001-character string false; `null` and `""` -> true with null skin. `SkinCssTests.cs`: `Classic_with_an_accent_emits_exactly_the_three_accent_properties` (resolve `("classic", null, "#F59E0B")`, `Properties` equals the three pairs `--ts-accent:#F59E0B`, `--ts-on-accent:#000000`, `--ts-accent-ink:#9D6507`, in order; `Attributes` empty); `Classic_with_nothing_emits_nothing`; `Midnight_emits_its_variables_and_presets` (contains `--p-bg:#0F1420`, `--ts-chrome:#0A0E17`, `--ts-focus:#FFD166`, attribute `data-ts-shadow=soft`, `data-ts-header=solid`, and no `--ts-accent` because the brand is not explicit); `A_font_and_radius_override_emit_their_variables` (`--ts-radius:0` for square, `--ts-font-heading` equals `SkinFonts.Stack("source-serif")`).

- [ ] **Step 2: Run to verify failure**

Run: `dotnet build tests/TechStrap.Application.Tests -c Release 2>&1 | grep -E "error|Build succeeded" | head -5`
Expected: compile errors (`TechStrap.Contracts.Skins` and `ProductAccent.ReadableOn` do not exist).

- [ ] **Step 3: Implement**

`ProductAccent.ReadableOn` (inside the existing class; reuse its private `TryParse`, `Contrast`, `Format`, `Scale`):

```csharp
    /// <summary>
    /// The foreground as text on <paramref name="backgroundHex"/>: the colour itself (upper case) when it reaches 4.5:1, otherwise stepped toward black on a light background or toward white on a dark one,
    /// at most 24 steps, then black or white. On white this is exactly <see cref="ProductAccentColors.AccentInk"/>. Throws <see cref="ArgumentException"/> for a malformed colour.
    /// </summary>
    public static string ReadableOn(string foregroundHex, string backgroundHex)
    {
        if (!TryParse(foregroundHex, out var foreground))
        {
            throw new ArgumentException("Not a #RRGGBB colour.", nameof(foregroundHex));
        }

        if (!TryParse(backgroundHex, out var background))
        {
            throw new ArgumentException("Not a #RRGGBB colour.", nameof(backgroundHex));
        }

        if (Contrast(foreground, background) >= MinimumTextContrast)
        {
            return Format(foreground);
        }

        var towardWhite = Luminance(background) < 0.5;
        for (var k = 1; k < DarkenDenominator; k++)
        {
            var stepped = towardWhite
                ? (Lighten(foreground.R, k), Lighten(foreground.G, k), Lighten(foreground.B, k))
                : (Scale(foreground.R, k), Scale(foreground.G, k), Scale(foreground.B, k));
            if (Contrast(stepped, background) >= MinimumTextContrast)
            {
                return Format(stepped);
            }
        }

        return towardWhite ? White : Black;
    }

    private static int Lighten(int channel, int k) => ((channel * (DarkenDenominator - k)) + (255 * k) + DarkenRoundingOffset) / DarkenDenominator;
```

Then make `TryDerive` compute `AccentInk` through the same code path only if the result is byte-identical: keep `DarkenUntilReadableOnWhite` as is (the `On_white...` test proves equality; do not refactor `TryDerive`).

Create the `Skins` files with the exact interfaces above. `ProductSkin.IsEmpty => this == new ProductSkin();`. `SkinSerializer` uses a source-generated context:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TechStrap.Contracts.Skins;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    WriteIndented = false)]
[JsonSerializable(typeof(ProductSkin))]
internal sealed partial class SkinJsonContext : JsonSerializerContext;

public static class SkinSerializer
{
    public static string? Serialize(ProductSkin? skin) =>
        skin is null || skin.IsEmpty ? null : JsonSerializer.Serialize(skin, SkinJsonContext.Default.ProductSkin);

    public static bool TryDeserialize(string? json, out ProductSkin? skin)
    {
        skin = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            return true;
        }

        if (json.Length > SkinRules.MaxJsonLength)
        {
            return false;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize(json, SkinJsonContext.Default.ProductSkin);
            skin = parsed is null || parsed.IsEmpty ? null : parsed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
```

`SkinRules.Validate` implements step 2's field checks and returns problems named by the camelCase field (`background`, `surface`, `ink`, `muted`, `border`, `brand`, `chrome`, `focus`, `headingFont`, `bodyFont`, `radius`, `borderWidth`, `shadow`, `button`, `header`, `pack`) with code `skin-invalid`; `SkinResolver` calls it. Pack data is a static array built from the table above with `ProductAccent` never touched at type-init time.

Amend `docs/architecture/PHASE-11g-theme-packs.md` and the D-053 entry: replace "`Border` and `Focus` at least 3:1" with "`Focus` at least 3:1 against `Background` (`Border` is decorative and has no rule)", replace the "mirrored by compiled SCSS keyed on `data-ts-pack`; a parity test" sentence with "resolved on the server into custom properties; SCSS holds only preset rules", and replace "validated in Domain" for the skin with "validated by the Contracts grammar in Application; Domain guards only the JSON length". `README.md`: Stability sentence gains "and 0.4.0"; add under Version notes, above `### 0.3.0`:

```markdown
### 0.4.0

Trailing optional `Skin` parameters (type `TechStrap.Contracts.Skins.ProductSkin`, null means no skin) were added to `ProductDto`, `CreateProductRequest`, `UpdateProductRequest`, `PublicProductDto` and `PublicProductSummaryDto`; null on `UpdateProductRequest` leaves the stored skin unchanged, and an all-null `ProductSkin` clears it. This is source-compatible but changes the constructor and `Deconstruct` signatures: recompile consumers built against 0.3.0. New types: the `TechStrap.Contracts.Skins` namespace, `SiteSettingsDto`, `UpdateSiteSettingsRequest`, `PublicSiteDto`. `ProductAccent.ReadableOn` is new. `TechStrap.Client` and `TechStrap.Client.Maui` move with it.
```

(The Dto and request types named there are created in Task 5; this note is written now so the package notes are in one commit with the model, and Task 5 must match the names.)

- [ ] **Step 4: Build and run**

Run: `dotnet build TechStrap.slnx -c Release 2>&1 | grep -E " warning | error |Build succeeded"`; `dotnet test tests/TechStrap.Application.Tests -c Release --no-build --filter "FullyQualifiedName~Skin|FullyQualifiedName~ProductAccent"`; `pwsh -NoProfile -File scripts/Invoke-ScriptTests.ps1 2>&1 | tail -2`
Expected: 0 warnings, all green, Pester green.

- [ ] **Step 5: Mutation**

In `ProductAccent.ReadableOn` flip `towardWhite` (`Luminance(background) < 0.5` to `>= 0.5`); `On_a_dark_background_it_lightens_until_readable` must fail. Restore.

- [ ] **Step 6: Commit**

```bash
git add src/TechStrap.Contracts/Skins src/TechStrap.Contracts/Branding/ProductAccent.cs src/TechStrap.Contracts/README.md docs/architecture/PHASE-11g-theme-packs.md docs/architecture/04-DECISION-LOG.md tests/TechStrap.Application.Tests/Skins tests/TechStrap.Application.Tests/Products/ProductAccentReadableOnTests.cs
git commit -m "feat(contracts): skin tokens, five packs, SkinResolver, contrast rules and serializer (D-053)"
```

---

### Task 3: Domain: product skin JSON and the site settings aggregate

**Files:**
- Modify: `src/TechStrap.Domain/Rules/DomainLimits.cs`, `src/TechStrap.Domain/Products/Product.cs`
- Create: `src/TechStrap.Domain/Settings/SiteSettings.cs`
- Test: `tests/TechStrap.Domain.Tests/Products/ProductSkinTests.cs`, `tests/TechStrap.Domain.Tests/Settings/SiteSettingsTests.cs` (create)

**Interfaces:**
- Consumes: nothing from Task 2 (Domain cannot reference Contracts).
- Produces:
  ```csharp
  // DomainLimits
  public const int SkinJsonMaxLength = 2000;      // equals SkinRules.MaxJsonLength (parity test in Task 5)
  public const int PackKeyMaxLength = 32;
  // Product
  public string? SkinJson { get; private set; }   // opaque, validated by Application with the Contracts grammar
  public DomainResult SetSkinJson(string? json);  // null or whitespace clears; > SkinJsonMaxLength -> error "skin-too-long", target "skin"
  // Product.Restore(..., string? portalHost = null, bool listedOnLanding = true, string? skinJson = null)
  // SiteSettings
  public sealed class SiteSettings
  {
      public const string DefaultPack = "classic";
      public string DefaultPackKey { get; private set; }
      public uint Version { get; }
      public static SiteSettings Restore(string defaultPackKey, uint version);
      public DomainResult SetDefaultPack(string? key);   // blank or > PackKeyMaxLength -> "skin-pack-invalid", target "default-pack"; stored trimmed and lower-case
  }
  ```

- [ ] **Step 1: Write the failing tests**

```csharp
using TechStrap.Domain.Products;
using TechStrap.Domain.Rules;

namespace TechStrap.Domain.Tests.Products;

public sealed class ProductSkinTests
{
    private static Product New() => Product.Restore(Guid.CreateVersion7(), "orbitly", "Orbitly", "ORB", ProductBranding.Restore("Orbitly", null, "#1F6FEB", null, null), true, 1);

    [Fact]
    public void A_restored_product_has_no_skin() => New().SkinJson.ShouldBeNull();

    [Fact]
    public void Setting_the_skin_stores_the_json_and_blank_clears_it()
    {
        var product = New();

        product.SetSkinJson("{\"pack\":\"slate\"}").IsSuccess.ShouldBeTrue();
        product.SkinJson.ShouldBe("{\"pack\":\"slate\"}");

        product.SetSkinJson("   ").IsSuccess.ShouldBeTrue();
        product.SkinJson.ShouldBeNull();
    }

    [Fact]
    public void The_skin_json_may_be_2000_characters_but_not_2001()
    {
        var product = New();

        product.SetSkinJson(new string('a', 2000)).IsSuccess.ShouldBeTrue();
        var tooLong = product.SetSkinJson(new string('a', 2001));

        tooLong.IsFailure.ShouldBeTrue();
        tooLong.Error!.Code.ShouldBe("skin-too-long");
        tooLong.Error.Target.ShouldBe("skin");
        DomainLimits.SkinJsonMaxLength.ShouldBe(2000);
    }

    [Fact]
    public void Restore_carries_the_stored_skin()
    {
        var product = Product.Restore(Guid.CreateVersion7(), "o", "O", "OO", ProductBranding.Restore("O", null, "#1F6FEB", null, null), true, 1, null, true, "{\"pack\":\"paper\"}");

        product.SkinJson.ShouldBe("{\"pack\":\"paper\"}");
    }
}
```

```csharp
using TechStrap.Domain.Settings;

namespace TechStrap.Domain.Tests.Settings;

public sealed class SiteSettingsTests
{
    [Fact]
    public void The_default_pack_is_stored_trimmed_and_lower_case()
    {
        var settings = SiteSettings.Restore("classic", 1);

        settings.SetDefaultPack("  Slate ").IsSuccess.ShouldBeTrue();

        settings.DefaultPackKey.ShouldBe("slate");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_pack_is_refused(string? key)
    {
        var result = SiteSettings.Restore("classic", 1).SetDefaultPack(key);

        result.Error!.Code.ShouldBe("skin-pack-invalid");
        result.Error.Target.ShouldBe("default-pack");
    }

    [Fact]
    public void A_key_over_32_characters_is_refused() =>
        SiteSettings.Restore("classic", 1).SetDefaultPack(new string('a', 33)).IsFailure.ShouldBeTrue();

    [Fact]
    public void The_seed_value_is_classic() => SiteSettings.DefaultPack.ShouldBe("classic");
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet build tests/TechStrap.Domain.Tests -c Release 2>&1 | grep -E "error|Build succeeded" | head -5`
Expected: compile errors (`SkinJson`, `SetSkinJson`, `SiteSettings`).

- [ ] **Step 3: Implement**

`DomainLimits`: add the two constants with summaries. `Product`: new constructor parameter `string? skinJson` last, property `SkinJson { get; private set; }` with the summary "The product's skin as JSON (D-053): opaque to Domain, validated by the Contracts grammar in Application. Null means no skin.", `Restore(..., string? portalHost = null, bool listedOnLanding = true, string? skinJson = null)`, `Create` passes `null`, and

```csharp
    public DomainResult SetSkinJson(string? json)
    {
        var text = string.IsNullOrWhiteSpace(json) ? null : json.Trim();
        if (text is { Length: > DomainLimits.SkinJsonMaxLength })
        {
            return DomainErrors.Validation("skin-too-long", $"The skin must be at most {DomainLimits.SkinJsonMaxLength} characters.", "skin");
        }

        SkinJson = text;
        return DomainResult.Ok();
    }
```

`SiteSettings` (new file, namespace `TechStrap.Domain.Settings`): private constructor, `Restore`, `SetDefaultPack` with `key?.Trim().ToLowerInvariant()`, blank or longer than `DomainLimits.PackKeyMaxLength` -> `DomainErrors.Validation("skin-pack-invalid", "Choose one of the available theme packs.", "default-pack")`. Whether the key is a *known* pack is checked by the Application handler against `SkinPacks`.

- [ ] **Step 4: Build and run**

Run: `dotnet build TechStrap.slnx -c Release 2>&1 | grep -E " warning | error |Build succeeded"`; `dotnet test tests/TechStrap.Domain.Tests -c Release --no-build`; `dotnet test tests/TechStrap.Application.Tests -c Release --no-build`
Expected: green; existing call sites compile (all new parameters trailing optional).

- [ ] **Step 5: Mutation**

Change the length guard to `>= DomainLimits.SkinJsonMaxLength`; `The_skin_json_may_be_2000_characters_but_not_2001` must fail. Restore.

- [ ] **Step 6: Commit**

```bash
git add src/TechStrap.Domain tests/TechStrap.Domain.Tests
git commit -m "feat(domain): product skin JSON and the SiteSettings aggregate (D-053)"
```

---

### Task 4: Persistence: `skin` column, `site_settings` table, migration

**Files:**
- Modify: `src/TechStrap.Infrastructure/Persistence/Records/ProductRecord.cs`, `Configurations/ProductRecordConfiguration.cs`, `Mapping/ProductMappings.cs`, `TechStrapDbContext.cs`
- Create: `Records/SiteSettingsRecord.cs`, `Configurations/SiteSettingsRecordConfiguration.cs`, `Mapping/SiteSettingsMappings.cs`
- Create: `src/TechStrap.Application/Persistence/ISiteSettingsRepository.cs`, `src/TechStrap.Infrastructure/Persistence/Repositories/SiteSettingsRepository.cs` (place beside the product repository; register where `IProductRepository` is registered)
- Create (tool-generated): `src/TechStrap.Infrastructure/Migrations/<timestamp>_AddSkinAndSiteSettings.cs` + `.Designer.cs`; snapshot updated by the tool
- Modify: `docs/architecture/05-SCHEMA.md`, `docs/architecture/02-ARCHITECTURE.md` (products column list)
- Test: `tests/TechStrap.Infrastructure.IntegrationTests/Settings/SiteSettingsPersistenceTests.cs`, `tests/TechStrap.Infrastructure.IntegrationTests/Products/ProductSkinPersistenceTests.cs` (create; mirror `ProductLandingAndLogoPersistenceTests`)

**Interfaces:**
- Consumes: `Product.SkinJson`, `Product.SetSkinJson`, `Product.Restore(..., skinJson)`, `SiteSettings` (Task 3).
- Produces:
  ```csharp
  // Application.Persistence
  public interface ISiteSettingsRepository
  {
      Task<SiteSettings> GetAsync(CancellationToken cancellationToken);   // never null: the migration seeds the row; a missing row returns SiteSettings.Restore("classic", 0)
      void Update(SiteSettings settings);
  }
  ```
  Columns: `products.skin varchar(2000) null`; table `site_settings(id smallint PK = 1, default_pack varchar(32) not null, xmin concurrency)` with one seeded row `(1, 'classic')` through `InsertData` in the generated migration (no `migrationBuilder.Sql`).

- [ ] **Step 1: Write the failing tests**

```csharp
    [Fact]
    public async Task The_seeded_row_reads_classic_and_an_update_round_trips()
    {
        await using var host = new PersistenceTestHost(Database);

        var first = await host.ReadAsync(sp => sp.GetRequiredService<ISiteSettingsRepository>().GetAsync(Ct));
        first.DefaultPackKey.ShouldBe("classic");

        first.SetDefaultPack("midnight").IsSuccess.ShouldBeTrue();
        await host.WriteAsync(sp => sp.GetRequiredService<ISiteSettingsRepository>().Update(first));

        var second = await host.ReadAsync(sp => sp.GetRequiredService<ISiteSettingsRepository>().GetAsync(Ct));
        second.DefaultPackKey.ShouldBe("midnight");
    }

    [Fact]
    public async Task A_stale_settings_update_is_a_concurrency_conflict()
    {
        await using var host = new PersistenceTestHost(Database);
        var a = await host.ReadAsync(sp => sp.GetRequiredService<ISiteSettingsRepository>().GetAsync(Ct));
        var b = await host.ReadAsync(sp => sp.GetRequiredService<ISiteSettingsRepository>().GetAsync(Ct));
        a.SetDefaultPack("slate");
        await host.WriteAsync(sp => sp.GetRequiredService<ISiteSettingsRepository>().Update(a));

        b.SetDefaultPack("paper");

        await Should.ThrowAsync<Exception>(() => host.WriteAsync(sp => sp.GetRequiredService<ISiteSettingsRepository>().Update(b)));
    }
```

```csharp
    [Fact]
    public async Task The_skin_json_round_trips_and_an_existing_row_reads_null()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        var product = (await host.ReadAsync(sp => sp.GetRequiredService<IProductRepository>().GetByIdAsync(scenario.Product.Id, Ct)))!;
        product.SkinJson.ShouldBeNull();

        product.SetSkinJson("{\"pack\":\"slate\",\"brand\":\"#112233\"}").IsSuccess.ShouldBeTrue();
        await host.WriteAsync(sp => sp.GetRequiredService<IProductRepository>().Update(product));

        var reread = (await host.ReadAsync(sp => sp.GetRequiredService<IProductRepository>().GetByIdAsync(scenario.Product.Id, Ct)))!;
        reread.SkinJson.ShouldBe("{\"pack\":\"slate\",\"brand\":\"#112233\"}");
    }
```

(Open `ProductLandingAndLogoPersistenceTests.cs` and the helpers first; use their real `ReadAsync`/`WriteAsync` shapes. For the concurrency test, copy how the product persistence tests assert a stale update and keep their exception type.)

- [ ] **Step 2: Run to verify failure**

Run: `dotnet build tests/TechStrap.Infrastructure.IntegrationTests -c Release 2>&1 | grep -E "error|Build succeeded" | head -5`
Expected: compile errors (`ISiteSettingsRepository`).

- [ ] **Step 3: Implement**

`ProductRecord.Skin` (`string?`); configuration `HasMaxLength(DomainLimits.SkinJsonMaxLength)`; `ToDomain` passes `record.Skin` as `skinJson`; `CopyTo` writes `record.Skin = product.SkinJson`. `SiteSettingsRecord { short Id; string DefaultPack; uint Version }` with `HasXminConcurrencyToken`, `HasKey(Id)`, `ValueGeneratedNever`, `HasData(new SiteSettingsRecord { Id = 1, DefaultPack = "classic" })`, table `site_settings`, `DefaultPack` max `DomainLimits.PackKeyMaxLength`. `SiteSettingsRepository.GetAsync`: `AsNoTracking`? Follow the product repository's tracking idiom exactly; `Update` attaches/copies and sets the original `Version` like the product repository does. Generate the migration with the `ef-migrate` skill or:

```bash
dotnet ef migrations add AddSkinAndSiteSettings --project src/TechStrap.Infrastructure --startup-project src/TechStrap.Api
dotnet ef migrations has-pending-model-changes --project src/TechStrap.Infrastructure --startup-project src/TechStrap.Api
```

Expected `Up`: `AddColumn<string>("skin", "products", maxLength: 2000, nullable: true)`, `CreateTable("site_settings", ...)`, `InsertData("site_settings", ..., values: new object[] { (short)1, "classic" })`. Docs: `05-SCHEMA.md` entity block gains `varchar skin "nullable, max 2000, versioned JSON of validated tokens (D-053)"` and a `site_settings` entity and migrations row; `02-ARCHITECTURE.md` products row names `skin` and adds a `site_settings` row.

- [ ] **Step 4: Build, test, verify**

Run: `dotnet build TechStrap.slnx -c Release 2>&1 | grep -E " warning | error |Build succeeded"`; `dotnet test tests/TechStrap.Infrastructure.IntegrationTests -c Release --no-build --filter "FullyQualifiedName~SiteSettingsPersistenceTests|FullyQualifiedName~ProductSkinPersistenceTests|FullyQualifiedName~ProductLandingAndLogoPersistenceTests"`; `pwsh -NoProfile -Command "Invoke-Pester -Path scripts/tests/SchemaDocs.Tests.ps1 -Output Minimal"`
Expected: green; migrations check "No changes".

- [ ] **Step 5: Mutation**

Drop `record.Skin = product.SkinJson;` from `CopyTo`; the skin round-trip test must fail. Restore.

- [ ] **Step 6: Commit**

```bash
git add src/TechStrap.Infrastructure src/TechStrap.Application/Persistence/ISiteSettingsRepository.cs docs/architecture/05-SCHEMA.md docs/architecture/02-ARCHITECTURE.md tests/TechStrap.Infrastructure.IntegrationTests
git commit -m "feat(persistence): products.skin, site_settings table with seed and migration AddSkinAndSiteSettings (D-053)"
```

---

### Task 5: Application and Api: skin on product DTOs, site settings routes

**Files:**
- Modify: `src/TechStrap.Contracts/Products/ProductDtos.cs`, `PublicProductDto.cs`, `PublicProductSummaryDto.cs` (trailing `ProductSkin? Skin = null` each, with `<param>` docs)
- Create: `src/TechStrap.Contracts/Settings/SiteSettingsDtos.cs` (`SiteSettingsDto`, `UpdateSiteSettingsRequest`, `PublicSiteDto`)
- Modify: `src/TechStrap.Application/Products/ProductMapping.cs`, `CreateProductRequestHandler.cs`, `UpdateProductRequestHandler.cs`, `GetPublicProductRequestHandler.cs`, `ListPublicProductsRequestHandler.cs`, `ProductErrors.cs`
- Create: `src/TechStrap.Application/Settings/GetSiteSettingsRequestHandler.cs`, `UpdateSiteSettingsRequestHandler.cs`, `GetPublicSiteRequestHandler.cs`, `SettingsErrors.cs`
- Create: `src/TechStrap.Api/Controllers/SiteSettingsController.cs`, `PublicSiteController.cs`
- Modify: `tests/TechStrap.Api.Tests/Auth/AgentAccessCoverageTests.cs` (`AdminOnlyRoutes` +2), `docs/architecture/02-ARCHITECTURE.md` (7.1 rows for the three routes; 11.2 note), `tests/TechStrap.Api.Tests/OpenApiSurfaceTests.cs` if it enumerates routes
- Test: `tests/TechStrap.Application.Tests/Products/ProductSkinHandlerTests.cs`, `tests/TechStrap.Application.Tests/Settings/SiteSettingsHandlerTests.cs`, `tests/TechStrap.Api.Tests/Settings/SiteSettingsEndpointTests.cs`, `tests/TechStrap.Api.Tests/Products/ProductSkinEndpointTests.cs`, `tests/TechStrap.Application.Tests/Skins/SkinLimitsParityTests.cs` (create)

**Interfaces:**
- Consumes: `ProductSkin`, `SkinSerializer`, `SkinRules`, `SkinResolver`, `SkinPacks`; `Product.SkinJson/SetSkinJson`; `ISiteSettingsRepository`, `SiteSettings`.
- Produces:
  ```csharp
  // Contracts
  public sealed record SiteSettingsDto(string DefaultPack, uint Version);
  public sealed record UpdateSiteSettingsRequest(string? DefaultPack, uint Version);
  public sealed record PublicSiteDto(string DefaultPack);
  // ProductDto(..., string? PortalHost = null, bool ListedOnLanding = true, ProductSkin? Skin = null)
  // CreateProductRequest(..., string? PortalHost = null, bool? ListedOnLanding = null, ProductSkin? Skin = null)
  // UpdateProductRequest(..., string? PortalHost = null, bool? ListedOnLanding = null, ProductSkin? Skin = null)
  // PublicProductDto(..., string? PortalHost = null, string? Tagline = null, ProductSkin? Skin = null)
  // PublicProductSummaryDto(..., bool ListedOnLanding = true, ProductSkin? Skin = null)
  // Application handlers (interfaces I<Name>)
  GetSiteSettingsRequestHandler.HandleAsync(CancellationToken)            -> Result<SiteSettingsDto>
  UpdateSiteSettingsRequestHandler.HandleAsync(UpdateSiteSettingsRequest, CancellationToken) -> Result<SiteSettingsDto>
  GetPublicSiteRequestHandler.HandleAsync(CancellationToken)              -> Result<PublicSiteDto>
  // Error codes: skin-invalid (400, target = the token name), skin-contrast-invalid (400, target = pair), skin-pack-unknown (400, target "default-pack"), skin-too-long (400), concurrency-conflict (409)
  // Routes: GET api/settings/site, PUT api/settings/site (Admin); GET api/public/site (Public policy, rate limit "public", Cache-Control public, max-age=300)
  ```
  Semantics: on update, `Skin == null` leaves the stored skin unchanged; `Skin.IsEmpty` clears it; otherwise it is validated and stored through `SkinSerializer.Serialize`. On create, null or empty means none. Save validation: `SkinRules.Validate`, then `SkinResolver.Resolve(currentDefaultPack, skin, branding accent)`; any problem fails with the first problem's code and target. Audit entries: `skin` on `ProductUpdated` (in the `changed` list after `listedOnLanding`); `AdminEventType.SiteSettingsUpdated` with payload `{"defaultPack":"<key>"}`.

- [ ] **Step 1: Write the failing tests**

`ProductSkinHandlerTests.cs` (fixture style of `UpdateProductRequestHandlerTests`: claims, repos, `UnitOfWorkSubstitute`; add a `ISiteSettingsRepository _site` substitute returning `SiteSettings.Restore("classic", 1)` and `IProductLogoUrls`):

```csharp
    [Fact]
    public async Task A_null_skin_leaves_the_stored_skin_and_is_not_audited()
    {
        _product.SetSkinJson("{\"pack\":\"slate\"}");

        var result = await Handler().HandleAsync(_product.Id, new UpdateProductRequest("Orbitly Cloud", SameBranding, true, 7), Ct);

        result.Value.Skin!.Pack.ShouldBe("slate");
        _product.SkinJson.ShouldBe("{\"pack\":\"slate\"}");
        _events.Received(1).Add(Arg.Is<AdminEvent>(e => e.PayloadJson == "{\"changed\":[\"name\"]}"));
    }

    [Fact]
    public async Task A_new_skin_is_stored_as_json_and_audited()
    {
        var request = new UpdateProductRequest("Orbitly", SameBranding, true, 7, null, null, new ProductSkin(Pack: "paper", Brand: "#112233"));

        var result = await Handler().HandleAsync(_product.Id, request, Ct);

        _product.SkinJson.ShouldBe("{\"pack\":\"paper\",\"brand\":\"#112233\"}");
        result.Value.Skin!.Brand.ShouldBe("#112233");
        _events.Received(1).Add(Arg.Is<AdminEvent>(e => e.PayloadJson == "{\"changed\":[\"skin\"]}"));
    }

    [Fact]
    public async Task An_empty_skin_clears_it()
    {
        _product.SetSkinJson("{\"pack\":\"slate\"}");

        await Handler().HandleAsync(_product.Id, new UpdateProductRequest("Orbitly", SameBranding, true, 7, null, null, new ProductSkin()), Ct);

        _product.SkinJson.ShouldBeNull();
    }

    [Theory]
    [InlineData("pack", "nope")]
    public async Task An_unknown_pack_is_skin_invalid_on_the_pack_token(string target, string value)
    {
        var result = await Handler().HandleAsync(_product.Id, new UpdateProductRequest("Orbitly", SameBranding, true, 7, null, null, new ProductSkin(Pack: value)), Ct);

        result.Errors[0].Code.ShouldBe("skin-invalid");
        result.Errors[0].Target.ShouldBe(target);
        _products.DidNotReceive().Update(Arg.Any<Product>());
    }

    [Fact]
    public async Task A_low_contrast_pair_is_skin_contrast_invalid_and_names_the_pair()
    {
        var result = await Handler().HandleAsync(_product.Id, new UpdateProductRequest("Orbitly", SameBranding, true, 7, null, null, new ProductSkin(Ink: "#EEEEEE")), Ct);

        result.Errors[0].Code.ShouldBe("skin-contrast-invalid");
        result.Errors[0].Target.ShouldBe("ink/background");
    }

    [Fact]
    public async Task Contrast_is_judged_against_the_current_default_pack()
    {
        _site.GetAsync(Arg.Any<CancellationToken>()).Returns(SiteSettings.Restore("midnight", 1));

        // Dark ink is fine on Classic but unreadable on Midnight's background.
        var result = await Handler().HandleAsync(_product.Id, new UpdateProductRequest("Orbitly", SameBranding, true, 7, null, null, new ProductSkin(Ink: "#222222")), Ct);

        result.Errors[0].Code.ShouldBe("skin-contrast-invalid");
    }

    [Fact]
    public async Task Create_stores_a_valid_skin_and_none_when_null()
    {
        // two requests through CreateProductRequestHandler: Skin null -> product.SkinJson null; Skin(Pack: "contrast") -> {"pack":"contrast"}
    }
```

(Fill the last test with the create handler fixture; assert both outcomes and the `Skin` in the returned DTO.)

`SiteSettingsHandlerTests.cs`: get returns `("classic", version)`; update with `slate` stores it, audits `SiteSettingsUpdated` payload `{"defaultPack":"slate"}`; update with an unknown key (`"neon"`) is `skin-pack-unknown`; a stale `Version` is `concurrency-conflict`; an agent (non-admin) claims is refused as the other admin handlers refuse; `GetPublicSite` returns the pack only.

`SkinLimitsParityTests.cs`: `SkinRules.MaxJsonLength.ShouldBe(DomainLimits.SkinJsonMaxLength)`; `SkinPacks.All.All(p => p.Key.Length <= DomainLimits.PackKeyMaxLength)`; every pack key is lower-case ASCII letters.

`SiteSettingsEndpointTests.cs` (real Postgres, fixture of `ProductLandingFieldsEndpointTests`): admin `GET api/settings/site` -> `classic`; admin `PUT` `{"defaultPack":"midnight","version":N}` -> 200 and `GET api/public/site` anonymous returns `{"defaultPack":"midnight"}` with `Cache-Control: public, max-age=300`; agent token -> 403 on both admin routes; unknown pack -> 400 with `errorCodes["default-pack"]`; stale version -> 409; anonymous public route has no `Set-Cookie`.

`ProductSkinEndpointTests.cs`: create with `skin` JSON `{"pack":"paper","brand":"#112233"}` -> 201 and `GET api/public/products/{key}` carries `skin.pack`; raw PUT omitting `skin` keeps it (use `PutRawAsync`); raw PUT `"skin":{}` clears it; raw PUT with `"skin":{"background":"red"}` is 400 `errorCodes.background` = `skin-invalid`; an unknown JSON member inside `skin` (`{"shine":"yes"}`) is rejected (400) because the DTO binder disallows unmapped members (assert 400, whichever error shape the framework returns, and that nothing was stored).

- [ ] **Step 2: Run to verify failure**

Run: `dotnet build tests/TechStrap.Application.Tests -c Release 2>&1 | grep -E "error|Build succeeded" | head -5`
Expected: compile errors (the new DTO parameters, handlers).

- [ ] **Step 3: Implement**

Contracts DTOs as in the Interfaces block. `ProductMapping.ToDto(product, logoUrls)`: `Skin = SkinSerializer.TryDeserialize(product.SkinJson, out var skin) ? skin : null`. A shared internal helper `ProductSkins.Prepare(ProductSkin? requested, string? accent, string defaultPack)` in `Application/Products` returns `Result<string?>` (the JSON to store, or the first problem as `ResultError`, kind Validation, target = problem target): `requested.IsEmpty` -> success null; `SkinRules.Validate` problems first, then `SkinResolver.Resolve(defaultPack, requested, accent).Problems` (skin-contrast-invalid only; format problems are already reported); then `SkinSerializer.Serialize(requested)`. `CreateProductRequestHandler` and `UpdateProductRequestHandler` gain `ISiteSettingsRepository siteSettings` (before `TimeProvider`), call `Prepare` with `branding.Value.AccentColour`, and `product.SetSkinJson(json)` only when `request.Skin is not null`; the `skin` change entry is added when the stored JSON differs. `GetPublicProductRequestHandler` and `ListPublicProductsRequestHandler` pass `SkinSerializer.TryDeserialize(product.SkinJson, ...)` into the new `Skin` parameter. New handlers follow the 11f logo handlers' structure (actor via `CurrentAgent.RequireActiveAsync`, unit of work, `AdminAudit.Record` with `AdminEventType.SiteSettingsUpdated`; add that enum value and its wire name beside `ProductUpdated`; check whether an enum-to-string pin test lists every event type and update it). Controllers: `SiteSettingsController` (`[Route("api/settings/site")]`, `[Authorize(Policy = AuthorizationPolicies.Admin)]`, `Get` and `Put`), `PublicSiteController` (`[Route("api/public/site")]`, `[Authorize(Policy = AuthorizationPolicies.Public)]`, `[EnableRateLimiting(PublicRateLimitOptions.PolicyName)]`, copy the cache-control pattern of `PublicProductsController`). Add `"GET api/settings/site"` and `"PUT api/settings/site"` to `AdminOnlyRoutes` in sorted position. `02-ARCHITECTURE.md`: three 7.1 rows with backticked route and handler names (`GetSiteSettingsRequestHandler`, `UpdateSiteSettingsRequestHandler`, `GetPublicSiteRequestHandler`) in the table format of neighbouring rows, decision `D-053`.

- [ ] **Step 4: Build and test**

Run: `dotnet build TechStrap.slnx -c Release 2>&1 | grep -E " warning | error |Build succeeded"`; `dotnet test tests/TechStrap.Application.Tests -c Release --no-build`; `dotnet test tests/TechStrap.Api.Tests -c Release --no-build --filter "FullyQualifiedName~Settings|FullyQualifiedName~Products|FullyQualifiedName~AgentAccessCoverageTests|FullyQualifiedName~OpenApi"`; `dotnet test tests/TechStrap.Architecture.Tests -c Release --no-build`
Expected: green (other tests that construct the extended handlers or DTOs are fixed with named arguments or the new parameters).

- [ ] **Step 5: Mutation**

In `UpdateProductRequestHandler` treat a null `request.Skin` as "clear" (`SetSkinJson(null)`); `A_null_skin_leaves_the_stored_skin_and_is_not_audited` must fail. Restore.

- [ ] **Step 6: Commit**

```bash
git add src/TechStrap.Contracts src/TechStrap.Application src/TechStrap.Api tests/TechStrap.Application.Tests tests/TechStrap.Api.Tests docs/architecture/02-ARCHITECTURE.md
git commit -m "feat(api): product skin on the DTOs and handlers, site settings routes and the public default pack (D-053)"
```

---

### Task 6: Portal rendering: site setting client, resolved skin on the wrapper, presets, fonts

**Files:**
- Create: `src/TechStrap.Portal/Clients/ISiteSettingsClient.cs`, `SiteSettingsClient.cs`; register next to `IPublicProductClient` (find with `grep -rn "IPublicProductClient" src/TechStrap.Portal --include=*.cs`)
- Create: `src/TechStrap.Portal/Products/DefaultPackProvider.cs`, `src/TechStrap.Portal/Products/PortalSkinFactory.cs`; register in `Program.cs`
- Modify: `src/TechStrap.Portal/Products/ProductThemeViewModel.cs` (`Skin`), `ProductPageBase.cs` if it constructs the view model
- Modify: `src/TechStrap.Portal/Components/Layout/PortalLayout.razor` (+ code-behind if absent), `Components/Ui/AccentScope.razor`, `AccentScope.razor.cs`
- Modify: `src/TechStrap.Portal/Components/Pages/Home.razor`, `Home.razor.cs`, `LandingCardViewModel.cs` (per-card scope)
- Create: `src/TechStrap.Portal/Styles/_presets.scss`; modify `Styles/app.scss` (import after `components`), `_theme.scss` (focus and outline-button fallbacks), `_components.scss` / `_layout.scss` only where a preset needs a hook
- Modify: `src/TechStrap.Portal/libman.json`, `Styles/_fonts.scss`
- Test: `tests/TechStrap.Portal.Tests/Skins/SkinRenderingHostTests.cs`, `PresetStyleTests.cs`, `DefaultPackProviderTests.cs`, `tests/TechStrap.Portal.Tests/Fixtures/unskinned-product-home.html` (golden, create), amend `FontHostingTests.cs`

**Interfaces:**
- Consumes: `ProductSkin`, `ResolvedSkin`, `SkinResolver.Resolve(string? defaultPack, ProductSkin? skin, string? accent)`, `SkinCss.Properties`, `SkinCss.Attributes`, `PublicSiteDto`, `PublicProductDto.Skin`, `PublicProductSummaryDto.Skin` (Tasks 2 and 5).
- Produces:
  ```csharp
  public interface ISiteSettingsClient { Task<Result<PublicSiteDto>> GetAsync(CancellationToken cancellationToken); }   // GET api/public/site
  public sealed class DefaultPackProvider   // singleton; TimeProvider snapshot, 60 s TTL, stale-while-revalidate, last good value, else "classic"
  { public ValueTask<string> GetAsync(CancellationToken cancellationToken); }
  public sealed record ProductThemeViewModel(string Key, string DisplayName, string? Accent, string? LogoUrl, ProductSkin? Skin = null);
  // AccentScope parameters: string? Accent (legacy, kept), ResolvedSkin? Skin (new; when set it wins), RenderFragment? ChildContent
  ```
  Rendering contract: `AccentScope` writes `style` from `SkinCss.Properties(skin)` joined as `name:value;` and adds `SkinCss.Attributes(skin)` as attributes; an empty property list means no `style` attribute at all (HTML identical to today). The variable names are exactly those listed in Task 2.

- [ ] **Step 0: Capture the golden before changing anything**

On the Task 5 head, add a throwaway test that renders `/p/paperplane` with the stub product of `ProductHomeHostTests` (`Product(...)` builder, Neutral site setting unreachable) and writes the HTML to `tests/TechStrap.Portal.Tests/Fixtures/unskinned-product-home.html` after normalising the asset fingerprint (`@Assets["css/app.css"]` renders a hashed URL: replace it with `/css/app.css` using a regex such as `/css/app(\.[A-Za-z0-9_-]+)?\.css(\?[^"]*)?` so the golden survives CSS changes). Mark the fixture `CopyToOutputDirectory` like the hostile-upload fixtures. Delete the throwaway writer; keep only the comparer in Step 1.

- [ ] **Step 1: Write the failing tests**

`SkinRenderingHostTests.cs` (fixture: `PortalFactory`, `factory.Api.OnJson`, product stub like `RootPageHostTests`/`LandingPageHostTests`):

```csharp
    [Fact]
    public async Task An_unskinned_product_page_is_byte_identical()
    {
        await using var factory = new PortalFactory();
        factory.Api.OnJson(HttpMethod.Get, "/api/public/site", new PublicSiteDto("classic"));
        factory.Api.OnJson(HttpMethod.Get, "/api/public/products/paperplane", Paperplane());   // no Skin, accent #F59E0B
        using var client = factory.CreateClient();

        var html = Normalise(await client.GetStringAsync("/p/paperplane", Ct));

        html.ShouldBe(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "unskinned-product-home.html")));
    }

    [Fact]
    public async Task The_default_pack_themes_the_product_page_and_the_root()
    {
        await using var factory = new PortalFactory();
        factory.Api.OnJson(HttpMethod.Get, "/api/public/site", new PublicSiteDto("midnight"));
        factory.Api.OnJson(HttpMethod.Get, "/api/public/products/paperplane", Paperplane());
        using var client = factory.CreateClient();

        var product = await client.GetStringAsync("/p/paperplane", Ct);
        var root = await client.GetStringAsync("/", Ct);

        foreach (var html in new[] { product, root })
        {
            html.ShouldContain("--p-bg:#0F1420");
            html.ShouldContain("--ts-focus:#FFD166");
            html.ShouldContain("data-ts-shadow=\"soft\"");
            html.ShouldContain("data-ts-header=\"solid\"");
        }
        product.ShouldContain("--ts-accent:#F59E0B");   // the product's accent still wins
        root.ShouldNotContain("--ts-accent:");           // neutral page: no product, no explicit brand
    }

    [Fact]
    public async Task A_product_override_changes_only_that_product()
    {
        await using var factory = new PortalFactory();
        factory.Api.OnJson(HttpMethod.Get, "/api/public/site", new PublicSiteDto("classic"));
        factory.Api.OnJson(HttpMethod.Get, "/api/public/products/paperplane", Paperplane(new ProductSkin(Pack: "paper", Radius: "square", BorderWidth: 4)));
        factory.Api.OnJson(HttpMethod.Get, "/api/public/products/orbitly", Orbitly());
        using var client = factory.CreateClient();

        var paperplane = await client.GetStringAsync("/p/paperplane", Ct);
        var orbitly = await client.GetStringAsync("/p/orbitly", Ct);

        paperplane.ShouldContain("--p-bg:#FBF7EF");
        paperplane.ShouldContain("--ts-radius:0");
        paperplane.ShouldContain("--ts-border-w:4px");
        orbitly.ShouldNotContain("--p-bg:");
        orbitly.ShouldNotContain("--ts-radius");
    }

    [Fact]
    public async Task Hostile_skin_values_never_reach_the_page()
    {
        await using var factory = new PortalFactory();
        factory.Api.OnJson(HttpMethod.Get, "/api/public/site", new PublicSiteDto("classic"));
        factory.Api.OnJson(HttpMethod.Get, "/api/public/products/paperplane",
            Paperplane(new ProductSkin(Background: "red;}body{display:none", Ink: "url(https://evil.test/x)", HeadingFont: "x';}", Radius: "50%", Shadow: "0 0 9px red", Button: "\"><script>", Header: "javascript:1")));
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/p/paperplane", Ct);

        html.ShouldNotContain("evil.test");
        html.ShouldNotContain("display:none");
        html.ShouldNotContain("<script>alert");
        var styles = Regex.Matches(html, "style=\"([^\"]*)\"").Select(m => WebUtility.HtmlDecode(m.Groups[1].Value)).ToList();
        styles.ShouldAllBe(s => Regex.IsMatch(s, @"^(--[a-z0-9-]+:(#[0-9A-F]{6}|[0-9.]+(rem|px)|0|'[^';]+'(, [^;']+|, '[^';]+')*);?)+$"));
    }

    [Fact]
    public async Task Neutral_pages_use_only_the_default_pack_and_are_identical_across_hosts()
    {
        await using var factory = new PortalFactory();
        factory.Api.OnJson(HttpMethod.Get, "/api/public/site", new PublicSiteDto("slate"));
        using var client = factory.CreateClient();

        client.DefaultRequestHeaders.Host = "portal.test";
        var a = await client.GetStringAsync("/not-found", Ct);
        client.DefaultRequestHeaders.Host = "stranger.example.net";
        var b = await client.GetStringAsync("/not-found", Ct);

        a.ShouldBe(b);
        a.ShouldContain("--p-bg:#F8FAFC");
    }

    [Fact]
    public async Task A_failed_site_setting_falls_back_to_classic_without_an_error_page()
    {
        await using var factory = new PortalFactory();
        factory.Api.OnStatus(HttpMethod.Get, "/api/public/site", HttpStatusCode.ServiceUnavailable);
        factory.Api.OnJson(HttpMethod.Get, "/api/public/products/paperplane", Paperplane());
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/p/paperplane", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldNotContain("--p-bg:");
    }

    [Fact]
    public async Task Each_landing_card_is_scoped_to_its_own_brand()
    {
        // Products mode, two summaries with AccentColour #F59E0B and #7C3AED: each card sits in a ts-accent-scope whose style carries that product's --ts-accent.
    }
```

(Fill `Paperplane(...)`, `Orbitly()` and `Normalise` as small private helpers; build the landing test with the `Products()` settings helper of `LandingPageHostTests`; assert the two accents appear in two different wrappers by matching `<div class="ts-accent-scope" style="--ts-accent:#F59E0B[^"]*"><a class="ts-landing-card" href="/p/orbitly...` style regexes against the real markup.)

`DefaultPackProviderTests.cs` (fake `ISiteSettingsClient`, `FakeTimeProvider`): first call reads once; a second call inside 60 s reads zero times; after 60 s it returns the stale value immediately and refreshes once in the background; a failure keeps the last good value; a cold failure returns `classic`.

`PresetStyleTests.cs`: compiled CSS (follow `StyleBuildTests` for how it reads `wwwroot/css/app.css` after build) contains `[data-ts-button=bevel]`, `[data-ts-button=outline]`, `[data-ts-header=solid]`, `[data-ts-header=band]`, `[data-ts-shadow=soft]`, `[data-ts-shadow=hard]`, `var(--ts-focus,var(--p-ink))`, `var(--ts-font-body`, contains no `prefers-color-scheme`, no `url(` outside `.woff2` and `data:image/`, and the `[data-bs-theme=dark]` block is still empty.

- [ ] **Step 2: Run to verify failure**

Run: `dotnet build tests/TechStrap.Portal.Tests -c Release 2>&1 | grep -E "error|Build succeeded" | head -5`
Expected: compile errors (`ISiteSettingsClient`, `ProductThemeViewModel.Skin`, `PortalFactory` stubs unaffected).

- [ ] **Step 3: Implement**

`SiteSettingsClient`: `api.GetAsync<PublicSiteDto>("api/public/site", ct)` like `PublicProductClient.ListAsync`. `DefaultPackProvider`: copy the snapshot shape of `ProductHostMap` (volatile immutable snapshot, `TimeProvider`, 60 s TTL, one background reload, `ReadTimeout` 30 s linked token); value is validated with `SkinPacks.IsKnown` (unknown -> `classic`). `PortalSkinFactory.Resolve(string defaultPack, ProductSkin? skin, string? accent) => SkinResolver.Resolve(defaultPack, skin, accent).Skin` (a thin wrapper so components never call the resolver with unvalidated input; also the one place a log line for dropped tokens could go, but log only the token names, never values).

`ProductThemeViewModel.From(PublicProductDto product, bool allowLoopbackImages)` passes `product.Skin`. `PortalLayout`: inject `DefaultPackProvider` and `PortalSkinFactory`; `protected override async Task OnInitializedAsync()` reads the pack; `_skin = factory.Resolve(pack, Theme?.Skin, Theme?.Accent)`; markup `<AccentScope Skin="@_skin">` (the layout subscribes to `ProductScope.Changed` already: recompute in the same handler). Because `Theme` is set by the page after the layout initialises, compute `_skin` lazily in `OnParametersSet`/the `Changed` handler, not only in `OnInitializedAsync`; mirror how the layout reads `Theme` today.

`AccentScope.razor`: `<div class="ts-accent-scope" style="@_style" @attributes="_attributes">@ChildContent</div>`; code-behind:

```csharp
    private string? _style;
    private Dictionary<string, object>? _attributes;

    [Parameter] public ResolvedSkin? Skin { get; set; }

    protected override void OnParametersSet()
    {
        if (Skin is not null)
        {
            var properties = SkinCss.Properties(Skin);
            _style = properties.Count == 0 ? null : string.Join(';', properties.Select(p => $"{p.Key}:{p.Value}"));
            var attributes = SkinCss.Attributes(Skin);
            _attributes = attributes.Count == 0 ? null : attributes.ToDictionary(a => a.Key, a => (object)a.Value);
            return;
        }

        _style = ProductAccent.TryDerive(Accent, out var colors)
            ? $"--ts-accent:{colors.Accent};--ts-on-accent:{colors.OnAccent};--ts-accent-ink:{colors.AccentInk}"
            : null;
        _attributes = null;
    }
```

Keep the existing class summary and extend it: values come only from `SkinResolver`/`SkinCss` (validated), never from raw strings.

`Home.razor(.cs)`: `LandingCardViewModel` gains `ProductSkin? Skin` and `string? Accent`; the card is wrapped as `<AccentScope Skin="@card.Resolved">` where `card.Resolved` is `PortalSkinFactory.Resolve(pack, summary.Skin, summary.AccentColour)`; read the pack once per request through `DefaultPackProvider`.

`_presets.scss` (imported last in `app.scss`; every rule keeps today's value as the `var()` fallback; confirm each selector exists with `grep` in `_components.scss`/`_layout.scss` and adjust to the real class names):

```scss
// D-053 presets. The Portal turns a resolved skin into custom properties and data-ts-* attributes on .ts-accent-scope.
// Every value below falls back to what the stylesheet had before skins, so a page with no skin renders unchanged.

.ts-accent-scope {
  --bs-border-radius: var(--ts-radius, .25rem);
  --bs-border-width: var(--ts-border-w, 1px);
}

.ts-portal {
  font-family: var(--ts-font-body, var(--ts-font-sans));
}

h1, h2, h3, h4, h5, h6 {
  font-family: var(--ts-font-heading, var(--ts-font-body, var(--ts-font-sans)));
}

// Surfaces: a card fills with the skin's surface (Classic's surface equals the page, so there is no visible change).
.ts-landing-card,
.ts-kb-card {
  background: var(--ts-surface, var(--p-bg));
  border-radius: var(--ts-radius, .25rem);
  border-width: var(--ts-border-w, 1px);
}

.ts-accent-scope[data-ts-shadow="soft"] :is(.ts-landing-card, .ts-kb-card, .ts-status-banner) {
  box-shadow: 0 1px 3px rgba(0, 0, 0, .18);
}

.ts-accent-scope[data-ts-shadow="hard"] :is(.ts-landing-card, .ts-kb-card, .ts-status-banner) {
  box-shadow: 4px 4px 0 var(--p-ink);
}

.ts-accent-scope[data-ts-button="bevel"] .btn-primary {
  box-shadow: inset 0 -4px 0 rgba(0, 0, 0, .25), inset 0 3px 0 rgba(255, 255, 255, .25);
  border-width: var(--ts-border-w, 1px);

  &:active {
    transform: translateY(2px);
    box-shadow: inset 0 -2px 0 rgba(0, 0, 0, .25), inset 0 2px 0 rgba(255, 255, 255, .25);
  }
}

.ts-accent-scope[data-ts-button="outline"] .btn-primary {
  --bs-btn-color: var(--ts-accent-ink);
  --bs-btn-bg: transparent;
  --bs-btn-border-color: var(--ts-accent-ink);
  --bs-btn-hover-color: var(--ts-on-accent);
  --bs-btn-hover-bg: var(--ts-accent);
  --bs-btn-hover-border-color: var(--ts-accent);
  --bs-btn-active-color: var(--ts-on-accent);
  --bs-btn-active-bg: var(--ts-accent);
  --bs-btn-active-border-color: var(--ts-accent);
  --bs-btn-disabled-color: var(--ts-accent-ink);
  --bs-btn-disabled-bg: transparent;
  --bs-btn-disabled-border-color: var(--ts-accent-ink);
  --bs-border-width: max(2px, var(--ts-border-w, 1px));
}

.ts-accent-scope[data-ts-header="solid"] .ts-product-header {
  background: var(--ts-chrome, #1B1B22);
  color: var(--ts-on-chrome, #FFFFFF);

  a,
  .ts-product-name {
    color: var(--ts-on-chrome, #FFFFFF);
  }
}

.ts-accent-scope[data-ts-header="band"] .ts-product-header {
  border-bottom: 4px solid var(--ts-chrome, #1B1B22);
}
```

`_theme.scss`: focus rule `outline: 3px solid var(--ts-focus, var(--p-ink)) !important;` (today `var(--p-ink)`); `.btn-outline-primary` hover/active text `var(--p-bg)` instead of `#fff` (Classic's `--p-bg` is `#FFFFFF`, so unchanged there). Remove nothing else.

Fonts: add four libman entries (pin the newest published exact versions: check `https://data.jsdelivr.com/v1/packages/npm/@fontsource/<name>` for each; file names follow `files/<family>-latin-<weight>-normal.woff2` plus `LICENSE`): `@fontsource/nunito` (400, 600, 700), `@fontsource/atkinson-hyperlegible` (400, 700), `@fontsource/source-serif-4` (400, 600), `@fontsource/pixelify-sans` (400, 700), destination `wwwroot/fonts/<package-name>`. `_fonts.scss`: one `@include ts-font-face("Nunito", "nunito", "nunito-latin-400-normal", 400);` per face (family names exactly `Nunito`, `Atkinson Hyperlegible`, `Source Serif 4`, `Pixelify Sans`, matching `SkinFonts.Stack`). `FontHostingTests`: add each new woff2 to the served-as-`font/woff2` list. Run `libman restore` through the build the way `StyleBuildTests` already depends on; no binary is committed (`TrackedFiles.Tests.ps1` must stay green).

- [ ] **Step 4: Build and test**

Run: `dotnet build TechStrap.slnx -c Release 2>&1 | grep -E " warning | error |Build succeeded"`; `dotnet test tests/TechStrap.Portal.Tests -c Release --no-build`; `dotnet test tests/TechStrap.Architecture.Tests -c Release --no-build`; `pwsh -NoProfile -File scripts/Invoke-ScriptTests.ps1 2>&1 | tail -2`
Expected: green, including `CspStyleTests`, `ResponsiveStyleTests`, `StyleBuildTests`, `FontHostingTests`, `RootPageHostTests`, `LandingPageHostTests`, `NeutralPagesGuardTests`, and the golden comparison.

- [ ] **Step 5: Mutation**

In `SkinCss.Properties` (Task 2) always emit `--p-bg` even when it equals Classic's; `An_unskinned_product_page_is_byte_identical` must fail. Restore. (This proves the golden guards the "emit only what differs" rule.)

- [ ] **Step 6: Commit**

```bash
git add src/TechStrap.Portal tests/TechStrap.Portal.Tests
git commit -m "feat(portal): resolved skin on the wrapper, default pack setting, presets and pack fonts (D-053)"
```

---

### Task 7: Emails: chrome colour in the header bar

**Files:**
- Modify: `src/TechStrap.Application/Email/EmailBranding.cs`, `DrainEmailOutboxHandler.cs`
- Modify: `src/TechStrap.Infrastructure/Email/EmailTemplateRenderer.cs` (header bar)
- Test: `tests/TechStrap.Infrastructure.IntegrationTests` or `tests/TechStrap.Application.Tests` renderer test file (find with `grep -rl EmailTemplateRenderer tests`), `tests/TechStrap.Application.Tests/Email/DrainEmailOutboxHandlerTests.cs`

**Interfaces:**
- Consumes: `SkinSerializer.TryDeserialize`, `ProductSkin.Chrome` (Task 2); `Product.SkinJson` (Task 3).
- Produces: `EmailBranding(string DisplayName, string? LogoPath, string AccentColour, string? FromAddress, string? ReplyTo, string? ChromeColour = null)`. Renderer rule: when `ChromeColour` passes `ProductAccent.TryDerive`, the header bar uses `Accent`-style fill `chrome` with text `OnAccent` derived from it; otherwise exactly today's accent bar.

- [ ] **Step 1: Write the failing tests**

Renderer (copy the neighbouring renderer test's setup): `A_chrome_colour_fills_the_header_bar_with_a_readable_on_colour` (chrome `#0A0E17` -> html contains `background:#0A0E17;color:#FFFFFF`; the button and links still use the accent); `No_chrome_keeps_the_accent_header_bar` (output byte-equal to a render with `ChromeColour: null`); `A_hostile_chrome_is_ignored` (`"red;}"` and `"#12"` -> accent bar, output contains no `red`); the font stack `Arial,Helvetica,sans-serif` is unchanged. Handler: `A_product_skin_chrome_reaches_the_email_branding` (product with `SetSkinJson("{\"chrome\":\"#0A0E17\"}")`; `_renderer.Received(...)` with `EmailBranding.ChromeColour == "#0A0E17"`), `A_product_without_a_skin_passes_no_chrome`, `A_malformed_skin_json_passes_no_chrome`.

- [ ] **Step 2: Run to verify failure**

Run: `dotnet build tests/TechStrap.Application.Tests -c Release 2>&1 | grep -E "error|Build succeeded" | head -5`
Expected: compile errors (`ChromeColour`).

- [ ] **Step 3: Implement**

`DrainEmailOutboxHandler`: `SkinSerializer.TryDeserialize(product.SkinJson, out var skin)` -> `skin?.Chrome` into the new `EmailBranding` parameter (null when parsing fails). `EmailTemplateRenderer.Layout`: compute `var bar = branding.ChromeColour is { } c && ProductAccent.TryDerive(c, out var chrome) ? (chrome.Accent, chrome.OnAccent) : (colors.Accent, colors.OnAccent);` and use it only for the header bar's `background`/`color`; everything else keeps the accent trio. The value is written from `TryDerive`'s normalised output, never the raw string.

- [ ] **Step 4: Build and test**

Run: `dotnet build TechStrap.slnx -c Release 2>&1 | grep -E " warning | error |Build succeeded"`; `dotnet test tests/TechStrap.Application.Tests -c Release --no-build --filter "FullyQualifiedName~Email"`; `dotnet test tests/TechStrap.Infrastructure.IntegrationTests -c Release --no-build --filter "FullyQualifiedName~Email"`
Expected: green.

- [ ] **Step 5: Mutation**

Write `branding.ChromeColour` raw instead of the `TryDerive` output; `A_hostile_chrome_is_ignored` must fail. Restore.

- [ ] **Step 6: Commit**

```bash
git add src/TechStrap.Application/Email src/TechStrap.Infrastructure/Email tests
git commit -m "feat(email): the product skin's chrome colour fills the email header bar (D-053)"
```

---

### Task 8: Admin interim "Skin (JSON)" field and the dragon-poop sample skin

Purpose: let the owner set and test a skin (the dragon-poop one) on a deployment as soon as 11g is merged and deployed, before the PHASE-11h editor exists. It is deliberately small: one advanced textarea, no live preview.

**Files:**
- Create: `docs/skins/dragon-poop.skin.json`, `docs/skins/README.md`
- Modify: `src/TechStrap.Admin/Clients/ApiFields.cs` (`Skin = "skin"`), `src/TechStrap.Admin/Features/Settings/Products/ProductEditorViewModel.cs`, `ProductFields.cs`, `ProductsCopy.cs`, `ProductEditorContent.razor`, `ProductEditorContent.razor.cs`
- Test: `tests/TechStrap.Admin.Tests/Components/ProductEditorSkinTests.cs` (create), `tests/TechStrap.Application.Tests/Skins/DragonPoopSkinTests.cs` (create), `scripts/tests/RepositoryDocs.Tests.ps1` (one pin)

**Interfaces:**
- Consumes: `ProductSkin`, `SkinSerializer`, `SkinResolver`, `SkinPacks`; `ProductDto.Skin`, `CreateProductRequest.Skin`, `UpdateProductRequest.Skin` (Task 5).
- Produces: view-model members `string SkinJson`, `string OriginalSkinJson`; element id `ts-product-skin` (textarea, 12 rows, `spellcheck="false"`); `ApiFields.Skin = "skin"`; request rules: unchanged text sends `Skin = null` (unchanged); blanked text on a product that had a skin sends `new ProductSkin()` (clear); a changed valid JSON sends the parsed skin; create sends the parsed skin or null.

The sample (exact content of `docs/skins/dragon-poop.skin.json`, derived from dragon-poop's `_tokens.scss`, `_bootstrap-overrides.scss`, `_buttons.scss`; the product's own accent in TechStrap is set to `#63371F` or left to the skin's `brand`):

```json
{
  "background": "#F6E8C2",
  "surface": "#FFF8E4",
  "ink": "#1D120B",
  "muted": "#4A4640",
  "border": "#1D120B",
  "brand": "#63371F",
  "chrome": "#26140C",
  "focus": "#26140C",
  "headingFont": "pixelify",
  "bodyFont": "nunito",
  "radius": "square",
  "borderWidth": 4,
  "shadow": "hard",
  "button": "bevel",
  "header": "solid"
}
```

`docs/skins/README.md` (ASCII) states: what the file is; how to apply it (Admin: product editor, "Skin (JSON)" field, paste, save; or `PUT api/products/{id}` with `"skin": { ... }` and the current `version`); how to clear it (empty the field, or send `"skin": {}`); how to switch the deployment default pack until the 11h page exists (`PUT api/settings/site`); and the **known gaps measured against dragon-poop's own site** (copy from the findings below).

Known gaps to record in that README and in the spec's Known limits (evidence is dragon-poop's repo): the gold focus ring (`#FFCF4A`) fails the 3:1 rule against the parchment background, so the sample uses the wood-dark `#26140C` (dragon-poop uses gold only on dark chrome); text on the brand colour is derived white or black, not dragon-poop's cream `#FFF5D6`; the accent text colour `#B04A17` used for taglines and step titles has no token (links use the derived brand ink); the stepped two-layer heading shadow, the hero sky image, the pixel-art logo and mascot, the parchment "scrap" rotation and the ground and stone-band marketing strips are out of reach; copy and voice ("Off the map", "A rough landing") are not skin data.

- [ ] **Step 1: Write the failing tests**

`DragonPoopSkinTests.cs` (embeds the JSON above as a string constant):

```csharp
    [Fact]
    public void The_sample_parses_strictly_and_resolves_without_problems()
    {
        SkinSerializer.TryDeserialize(Json, out var skin).ShouldBeTrue();
        skin.ShouldNotBeNull();
        SkinRules.Validate(skin).ShouldBeEmpty();

        var result = SkinResolver.Resolve("classic", skin, null);

        result.Problems.ShouldBeEmpty();
        result.Skin.Tokens.Background.ShouldBe("#F6E8C2");
        result.Skin.Tokens.Brand.ShouldBe("#63371F");
        result.Skin.BrandIsExplicit.ShouldBeTrue();
        result.Skin.OnBrand.ShouldBe("#FFFFFF");
        result.Skin.OnChrome.ShouldBe("#FFFFFF");
        SkinCss.Attributes(result.Skin).Select(a => a.Key + "=" + a.Value).ShouldBe(["data-ts-shadow=hard", "data-ts-button=bevel", "data-ts-header=solid"]);
    }

    [Fact]
    public void Gold_focus_on_the_parchment_page_fails_the_contrast_rule_which_is_why_the_sample_does_not_use_it()
    {
        var gold = SkinSerializer.TryDeserialize(Json.Replace("\"focus\": \"#26140C\"", "\"focus\": \"#FFCF4A\""), out var skin) ? skin : null;

        var result = SkinResolver.Resolve("classic", gold, null);

        result.Problems.ShouldContain(new SkinProblem("skin-contrast-invalid", "focus/background"));
    }
```

Plus a Pester pin in the 11g `Describe`: `docs/skins/dragon-poop.skin.json` exists, parses with `ConvertFrom-Json`, has exactly the 15 expected property names, and `docs/skins/README.md` mentions `dragon-poop`, `PUT api/products` and `api/settings/site`.

`ProductEditorSkinTests.cs` (bUnit; `ProductEditorTests` fixture, helpers `RenderEdit`, `Type`, `Save`, `Updates`, `Creates`, `FieldError`):

```csharp
    [Fact]
    public void A_stored_skin_is_shown_as_json_in_the_field()
    {
        _products.GetAsync(TestData.OrbitlyId, Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.ProductDetail(skin: new ProductSkin(Pack: "slate", Brand: "#112233"))));

        var cut = RenderEdit();

        Value(cut, "ts-product-skin").ShouldContain("\"pack\": \"slate\"");
        Value(cut, "ts-product-skin").ShouldContain("\"brand\": \"#112233\"");
    }

    [Fact]
    public void An_untouched_field_sends_no_skin()
    {
        var cut = RenderEdit();
        Type(cut, "ts-product-name", "Orbitly Cloud");
        Save(cut);

        Updates.ShouldHaveSingleItem().Skin.ShouldBeNull();
    }

    [Fact]
    public void Pasted_json_is_sent_as_the_parsed_skin()
    {
        var cut = RenderEdit();
        Type(cut, "ts-product-skin", "{ \"pack\": \"paper\", \"radius\": \"square\" }");
        Save(cut);

        Updates.ShouldHaveSingleItem().Skin.ShouldBe(new ProductSkin(Pack: "paper", Radius: "square"));
    }

    [Fact]
    public void Blanking_a_stored_skin_sends_an_empty_skin_to_clear_it()
    {
        _products.GetAsync(TestData.OrbitlyId, Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.ProductDetail(skin: new ProductSkin(Pack: "slate"))));
        var cut = RenderEdit();
        Type(cut, "ts-product-skin", "   ");
        Save(cut);

        Updates.ShouldHaveSingleItem().Skin.ShouldBe(new ProductSkin());
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("{ \"shine\": \"yes\" }")]
    [InlineData("{ \"background\": \"red\" }")]
    public void Invalid_json_blocks_the_save_at_the_field(string json)
    {
        var cut = RenderEdit();
        Type(cut, "ts-product-skin", json);
        Save(cut);

        FieldError(cut, "ts-product-skin").ShouldNotBeNullOrEmpty();
        Updates.ShouldBeEmpty();
    }

    [Fact]
    public void An_api_contrast_error_is_shown_at_the_skin_field()
    {
        _products.UpdateAsync(TestData.OrbitlyId, Arg.Any<UpdateProductRequest>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Fail(new ResultError("skin-contrast-invalid", "ink on background is too low.", ResultErrorKind.Validation, "ink/background")));
        var cut = RenderEdit();
        Type(cut, "ts-product-skin", "{ \"ink\": \"#EEEEEE\" }");
        Save(cut);

        FieldError(cut, "ts-product-skin").ShouldContain("ink on background");
    }
```

(`TestData.ProductDetail` gains a `skin` optional parameter; `TestData.Fail` may be named differently: use the helper the existing failure tests use. Local validation in the editor uses `SkinSerializer.TryDeserialize`, then `SkinRules.Validate`; an unknown member or malformed JSON is `ProductsCopy.SkinInvalid`, a rules problem reads `ProductsCopy.SkinTokenInvalid(problem.Target)`.)

- [ ] **Step 2: Run to verify failure**

Run: `dotnet build tests/TechStrap.Admin.Tests -c Release 2>&1 | grep -E "error|Build succeeded" | head -5`
Expected: compile errors (`SkinJson`, `ProductDetail(skin:)`).

- [ ] **Step 3: Implement**

`ProductEditorViewModel`: `SkinJson`/`OriginalSkinJson` set in `From` from `SkinSerializer` (pretty JSON using a local `JsonSerializerOptions { WriteIndented = true }` over the strongly typed `ProductSkin` is not source-generated: instead pretty-print by serialising with `SkinSerializer.Serialize`, parsing with `JsonDocument` and writing with `Utf8JsonWriter { Indented = true }`; empty skin -> empty string). `Check(ApiFields.Skin)`: blank is fine; otherwise `SkinSerializer.TryDeserialize` must succeed and `SkinRules.Validate` must be empty. `ToUpdateRequest`/`ToCreateRequest` append `ParsedSkinForUpdate()`/`ParsedSkinForCreate()` per the rules in Interfaces. `ProductFields.All` gains `ApiFields.Skin` last. `MapFieldErrors`: any 400 whose target is one of the skin token names (`background`, `ink`, `ink/background`, `pack`, ...) is shown at the skin field (extend the target list with `SkinTokenNames`), and an Api `skin-too-long` target `skin` maps naturally. Markup: after the Portal host field, a `<details>` titled `ProductsCopy.SkinHeading` ("Appearance (advanced)") containing the textarea with label `ProductsCopy.SkinLabel`, help `ProductsCopy.SkinHelp` (one sentence + "See docs/skins/README.md for a worked example and the list of tokens"), and the usual error slot. The field uses the same `@Field`-style id/`-error` conventions (a textarea variant helper, or inline markup mirroring `Field`).

`docs/skins` files as specified. The spec's Known limits section gains the gaps list (copy of the README text).

- [ ] **Step 4: Build and test**

Run: `dotnet build TechStrap.slnx -c Release 2>&1 | grep -E " warning | error |Build succeeded"`; `dotnet test tests/TechStrap.Admin.Tests -c Release --no-build`; `dotnet test tests/TechStrap.Application.Tests -c Release --no-build --filter "FullyQualifiedName~DragonPoop"`; `pwsh -NoProfile -File scripts/Invoke-ScriptTests.ps1 2>&1 | tail -2`
Expected: green.

- [ ] **Step 5: Mutation**

Make an untouched field send `new ProductSkin()` instead of null; `An_untouched_field_sends_no_skin` must fail. Restore.

- [ ] **Step 6: Commit**

```bash
git add docs/skins src/TechStrap.Admin tests/TechStrap.Admin.Tests tests/TechStrap.Application.Tests/Skins/DragonPoopSkinTests.cs scripts/tests/RepositoryDocs.Tests.ps1
git commit -m "feat(admin): interim Skin (JSON) field and the dragon-poop sample skin (D-053)"
```

---

### Task 9: Docs, pins and close-out

**Files:**
- Modify: `docs/self-hosting/SELF-HOSTING.md` (a "Portal appearance" section: five packs, default set in the Admin or `PUT api/settings/site`, product skins, no env key), `docs/development/PORTAL-APP.md` (theming section: variables, presets, Classic baseline rule), `docs/BRAND.md` (Portal tokens: the pack table and the variable list), `docs/architecture/UX-BRIEF-portal.md` (theming bullet), `docs/security/SECURITY-REVIEW.md` (checklist 4 row for the skin grammar and render-time re-check, plus a finding note with the next free SR number), `docs/architecture/02-ARCHITECTURE.md` (8.2 theming note), `docs/architecture/PHASE-11g-theme-packs.md` (tick T01 to T08, Deliverables, Success Criteria, Boundary Validation, Risks, As-built notes per task), `99-IMPLEMENTATION-ROADMAP.md` and `00-DISCOVERY-INDEX.md` rows ("11g complete (pending merge): T01 to T08"), `docs/superpowers/plans/2026-10-10-phase-11g-theme-packs.md` (`## As built` section at the end)
- Modify: `scripts/tests/RepositoryDocs.Tests.ps1` (11g block: ticks, rows, SELF-HOSTING and security-review phrases)
- No code changes.

- [ ] **Step 1: Pins first (RED)**

Add to the 11g `Describe`:

```powershell
    It 'ticks every 11g task and marks 11g complete pending merge' {
        foreach ($n in 1..9) { $script:Spec | Should -Match ('- \[x\] \*\*P11g-T' + $n.ToString('00') + '\*\*') -Because "P11g-T$($n.ToString('00')) is ticked" }
        $script:Spec | Should -Not -Match '- \[ \] \*\*P11g-T'
        (Get-RepoText 'docs/architecture/99-IMPLEMENTATION-ROADMAP.md') | Should -Match '(?m)^\| 11g \|.*\| D-053 recorded; 11g complete \(pending merge\): T01 to T08'
        (Get-RepoText 'docs/architecture/00-DISCOVERY-INDEX.md') | Should -Match '(?m)^\| 11g \|.*\| D-053 recorded; 11g complete \(pending merge\): T01 to T08'
    }

    It 'documents the packs, the default setting and the skin grammar' {
        $selfHost = Get-RepoText 'docs/self-hosting/SELF-HOSTING.md'
        foreach ($phrase in 'Portal appearance', 'api/settings/site', 'Classic', 'Midnight', 'Contrast') { $selfHost | Should -Match $phrase -Because $phrase }
        $review = Get-RepoText 'docs/security/SECURITY-REVIEW.md'
        foreach ($phrase in 'D-053', 'SkinResolver', 'Hostile_skin_values_never_reach_the_page') { $review | Should -Match $phrase -Because $phrase }
        (Get-RepoText 'docs/development/PORTAL-APP.md') | Should -Match 'data-ts-button'
    }
```

Run `pwsh -NoProfile -File scripts/Invoke-ScriptTests.ps1`; expect these two to fail.

- [ ] **Step 2: Write the docs (GREEN)**

Each edit lists what the code actually does (setting names, defaults, error codes, routes, the Classic-baseline "emit only what differs" rule, the contrast rules and their numbers, the fallback on a failing stored value, the Known limits from the spec). ASCII only; keep each file's line endings (BRAND.md, UX-BRIEF and the roadmap are CRLF: append to line ends or insert whole lines with `\r\n`, as the T01 commit did). In the spec, record As-built differences reported by the task reports (the three rulings at the top of this plan, and anything an implementer deviated on).

- [ ] **Step 3: Verify everything**

Run: `pwsh -NoProfile -File scripts/Invoke-ScriptTests.ps1 2>&1 | tail -3`; `dotnet build TechStrap.slnx -c Release 2>&1 | grep -E " warning | error |Build succeeded"`; `dotnet test --solution TechStrap.CI.slnf -c Release --no-build 2>&1 | tail -12`; `dotnet ef migrations has-pending-model-changes --project src/TechStrap.Infrastructure --startup-project src/TechStrap.Api --configuration Release --no-build 2>&1 | tail -1`; `git diff --check`; a byte scan of every touched ASCII doc (`tr -d '\000-\177' < FILE | wc -c` prints 0).

Compose check (record commands and output in the plan's As built; never `down -v`): `docker compose up -d --wait` (use `TECHSTRAP_MAILPIT_PORT=18025` if 8025 is taken); `curl -s http://127.0.0.1:8080/api/public/site` -> `{"defaultPack":"classic"}`; switch the pack without an Admin token: `docker compose exec postgres psql -U techstrap -d techstrap -c "update site_settings set default_pack = 'midnight'"` (adjust user and database names to the dev compose file); wait 65 seconds (the Portal's 60 s snapshot); `curl -s http://127.0.0.1:8082/ | grep -o -- "--p-bg:#[0-9A-F]*"` -> `--p-bg:#0F1420`; repeat for `slate`, `paper`, `contrast`; set back to `classic`. Say plainly in the As built what was not driven by hand (the Admin editor is PHASE-11h).

- [ ] **Step 4: Commit**

```bash
git add docs scripts/tests/RepositoryDocs.Tests.ps1
git commit -m "docs(11g): self-hosting, portal app, brand, security review and pins for theme packs and skins (D-053)"
```

---

## Self-review notes

- **Spec coverage:** token grammar, resolver, contrast, packs, fonts -> Task 2 (+ fonts in Task 6); storage -> Tasks 3, 4; Api and DTOs -> Task 5; Portal rendering, neutral default, landing scopes, presets -> Task 6; emails -> Task 7; amended documents, pins, security row -> Tasks 1, 9; Success Criteria map to Task 6 tests (default pack switches root/product/ticket pages via the shared layout; override isolation; golden; hostile values; CSP/style/font suites).
- **Type consistency:** `ProductSkin` (Contracts, Tasks 2, 5, 6, 7), `Product.SkinJson` (Domain, Tasks 3, 4, 5, 7), `ResolvedSkin` and `SkinCss` (Tasks 2, 6), `ISiteSettingsRepository.GetAsync/Update` (Tasks 4, 5), `PublicSiteDto(string DefaultPack)` (Tasks 5, 6), `EmailBranding.ChromeColour` (Task 7), `DefaultPackProvider.GetAsync` (Task 6).
- **Known places an implementer must check against the real code:** repository registration site and tracking idiom (Task 4); how `AdminEventType` wire names are pinned (Task 5); `ProductPageBase`/`ProductScope` change notification and where the layout reads `Theme` (Task 6); exact existing selector names for the preset SCSS (Task 6); jsdelivr font package versions (Task 6); the renderer test file location (Task 7). None changes the design.
