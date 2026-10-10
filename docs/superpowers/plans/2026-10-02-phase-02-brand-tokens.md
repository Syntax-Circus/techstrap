# TechStrap PHASE-02 Brand Tokens, Components and Style Guide Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Finish PHASE-02 (tasks P02-T05 follow-ups and P02-T06 to P02-T10): the Carbon Copy v2 token layer, self-hosted fonts, the reusable Admin and Portal components, Development-only style guides, provisional SVG logos, the product-accent helper, and the review loop that closes the phase.

**Architecture:** One shared `assets/brand/scss/_brand-tokens.scss` (Sass maps generated from the BRAND.md tables, imported by relative path from both apps, so there is no copy and no MSBuild link) feeds a per-app `Styles/_tokens.scss` that sets Bootstrap variables before the Bootstrap import. Admin supports Light, Dark and Auto through `data-bs-theme` plus a `prefers-color-scheme` fallback from one redefined Bootstrap `color-mode` mixin; the Portal is light only and takes its accent at runtime from `--ts-accent`, `--ts-on-accent` and `--ts-accent-ink`, derived by one pure function in `TechStrap.Contracts`. Fonts are WOFF2 files restored by libman from the `@fontsource` npm packages (never committed). Components are `.razor` + `.razor.cs` pairs (inline only when trivial), covered by bUnit tests, with compiled-CSS tests that read the BRAND.md tables themselves.

**Tech Stack:** .NET 10, Blazor (Admin: Server, Portal: SSR), Bootstrap 5.3.8 SCSS, AspNetCore.SassCompiler 1.105.1, Microsoft.Web.LibraryManager.Build 3.0.114 (jsdelivr), xUnit v3 + Shouldly + bUnit 2.11.3 + Microsoft.AspNetCore.Mvc.Testing, Pester 6.2.0, Python 3.13 with Pillow, numpy, vtracer 0.6.15, fonttools, brotli, `SyntaxCircus.Blazor.Components` 0.1.3.

**Spec:** `docs/BRAND.md` (owner-approved 2026-10-02, system of record) and `docs/architecture/PHASE-02-brand-and-ux.md` (tasks P02-T05 to P02-T10 and Success Criteria). Also read: `docs/architecture/04-DECISION-LOG.md` D-023 and D-024, `docs/design/mockups/direction-carbon-copy-v2.html` (visual source for components), `D:\dev\SyntaxCircus\_template\docs\RAZOR_COMPONENT_ARCHITECTURE.md` and `_template\AGENT_GUIDE.md`.

## Global Constraints

Every task's requirements include this section. Values are copied from the spec and the repository.

- **Branch and state:** work on `feat/phase-02-brand-and-ux`. P02-T01 to P02-T04 are done; commit `8842aff` did most of P02-T05 (libman Bootstrap 5.3.8, `sasscompiler.json`, placeholder `_tokens.scss`, `TechStrap.{Admin,Portal}.Tests` with `StyleBuildTests`). This plan does not redo them.
- **BRAND.md is the system of record.** If a mockup and BRAND.md disagree, BRAND.md wins. Never invent a color, shadow or radius; never copy hex values out of a mockup; tokens are used by name (`var(--ink)`).
- **Surfaces:** Admin is dense, calm and plain-spoken; Portal is plain, light only and product-led (small radii, Plex Sans, the product's three accent properties, nothing of ours except one "Powered by TechStrap" line). The mascot, the retro window and `--bm-*` tokens appear only on Admin all-caught-up, sign-in, 404 and the style guide.
- **Tint code (hard rule):** white = customer, canary = public reply, pink + dashed edge + notched corner = internal note. Never reused for any other meaning.
- **Stamps:** straight, single 1.5px border in lists; tilted -2deg (spam +2deg) with the 0.35s stamp-down only on the ticket view. `prefers-reduced-motion: reduce` disables all animation and transition.
- **Fonts:** self-hosted WOFF2 in `wwwroot/fonts`, `font-display: swap`, Latin subset, SIL OFL license text shipped beside the files, no Google Fonts or any CDN at runtime. Fallbacks: Sans `system-ui, "Segoe UI", Arial, sans-serif`; Mono `ui-monospace, Consolas, monospace`; Serif `Georgia, serif`.
- **Product accent (BRAND.md section 22):** `--ts-on-accent` is `#FFFFFF` or `#000000`, whichever has the higher WCAG contrast; `--ts-accent-ink` is the accent unchanged when it has at least 4.5:1 on white, otherwise the accent with R, G and B scaled by (1 - 0.04 k), k = 1, 2, 3 ..., until it does. The only validation is a well-formed `#RRGGBB`; no contrast rejection. Test vectors: `#7C3AED` gives `#FFFFFF` / `#7C3AED`; `#F59E0B` gives `#000000` / `#9D6507`; `#0F3D2E` gives `#FFFFFF` / `#0F3D2E`; `#2E9AFF` gives `#000000` / `#2375C2`; `#4B7D87` gives `#FFFFFF` at 4.58:1.
- **Admin theme:** Light, Dark and Auto (default Auto) through Bootstrap's `data-bs-theme`; persistence of the choice is PHASE-07's job. **Portal:** light only.
- **No Domain reference from Admin or Portal** (architecture tests): the UI enums (`StampStatus`, `PriorityLevel`, `EntryKind`) are UI-local presentation types, and `TechStrap.Contracts` stays free of project, package and framework references.
- **Razor rules:** a component is inline only if it has simple parameters and at most one trivial synchronous callback; everything else is `.razor` + `.razor.cs` with all C# in the code-behind. Style-guide pages stay inline (static markup).
- **Build settings:** `TreatWarningsAsErrors`, `EnforceCodeStyleInBuild`, private fields `_camelCase`, constants PascalCase, central package versions (no `Version` attribute in any csproj; `scripts/Check-PackageVersions.ps1` enforces it; this plan adds no NuGet package).
- **Never committed:** compiled CSS (`wwwroot/css/app.css`), Bootstrap vendor files (`Styles/Vendor/`), font binaries (`wwwroot/fonts/`). Dockerfiles assert that the CSS and fonts exist after publish.
- **Voice (BRAND.md section 3):** sentence case, no exclamation marks, no emoji; humor only inside the three Admin brand moments; blocking errors are a plain cause plus a next step.
- **Commits:** Conventional Commits, one per task, test-first. Every commit message ends with exactly these two lines:

```
Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi
```

- **Commands run from the repository root** `D:\dev\SyntaxCircus\techstrap`. Run tests with `dotnet test --project tests/<Project> [--filter-class "*Name"]` (Microsoft.Testing.Platform runner) and Pester with `pwsh -NoProfile -File scripts/Invoke-ScriptTests.ps1 [-Path scripts/tests/<File>]`.

## Review Focus

The failure modes the spec implies but a first implementation is most likely to miss, most likely first. Each is pinned to a test in the task that owns the code.

1. **Accent edge cases.** Pure white (`#FFFFFF`), pure black (`#000000`), mid-gray `#777777` (just misses 4.5:1 on white, so its ink must darken to `#727272` while its on-accent is black), `#767676` (just meets it, ink unchanged) and the worst case `#4B7D87` must all produce readable on-accent and ink, and a malformed value (`#12345`, `red`, empty, `#123456;background:url(x)`) must be rejected without throwing and without reaching a `style` attribute. Pinned by `ProductAccentContrastTests` and `AccentScopeTests` (Tasks 2 and 11).
2. **Dark-mode readability of the pink internal note.** The note's body text and `note-ink` head must reach 4.5:1 on `--pink` in both themes, and the rest of the text pairs of BRAND.md section 12 must too. The check found one pair that does not: `--ink-3` on `--sel` in dark is 4.32:1 (rule added to BRAND.md). Pinned by `TintStyleTests.Internal_note_text_is_readable_in_both_themes` and `TokenContrastTests` (Tasks 8 and 3).
3. **Reduced motion.** With `prefers-reduced-motion: reduce` every animation and transition is off, and the stamp still ends in its correct tilted state because the tilt is the static style, not part of the animation. Pinned by `StampStyleTests.Reduced_motion_...` and `Ticket_stamps_tilt_...` (Task 7).
4. **The style guide leaking into Production.** `/_styleguide` must return 200 in Development and a real 404, with none of its markup, in Production and Staging, in both apps, including when a circuit navigates to it without an HTTP request. Pinned by `StyleGuideEnvironmentTests`, `DevelopmentOnlyTests` and `PortalHostTests` (Tasks 6 and 11).
5. **Fonts failing to load, and libman or jsdelivr being offline in Docker.** A font file that never arrives must leave readable text (system fallback at the end of every stack, `font-display: swap`), and an image built without network access must fail the build instead of shipping without fonts. Pinned by `FontStyleTests`, `FontHostingTests` and the Dockerfile Pester assertions (Task 4).

---

## File Structure

New and changed files, by responsibility.

**Shared brand source**
- `assets/brand/scss/_brand-tokens.scss` (create): Sass maps of every BRAND.md token plus font stacks and the `ts-emit` mixin. One copy, imported by relative path from both apps.
- `assets/brand/scss/_brand-fonts.scss` (create): the `ts-font-face` mixin.
- `assets/brand/{mark,logo,wordmark}.svg` (create, generated): provisional SVGs; copies in `src/TechStrap.Admin/wwwroot/brand/` and `src/TechStrap.Portal/wwwroot/brand/`.
- `scripts/brand/generate-brand-assets.py` (modify): vtracer tracing and the outlined wordmark.

**Contracts**
- `src/TechStrap.Contracts/Branding/ProductAccent.cs` (create): the single accent rule.

**Admin** (`src/TechStrap.Admin`)
- `libman.json` (modify): Bootstrap plus three font libraries. `Styles/_tokens.scss`, `_color-mode.scss`, `_theme.scss`, `_fonts.scss`, `_shell.scss`, `_stamp.scss`, `_priority.scss`, `_tinted-entry.scss`, `_kbd.scss`, `_brand-window.scss`, `_feedback.scss`, `_styleguide.scss`, `_motion.scss`, `app.scss` (create or modify).
- `Components/Ui/`: `DevelopmentOnly`, `StatusStamp`, `PriorityMark`, `TintedEntry`, `TintLegend`, `Kbd`, `BrandWindow`, `AppReconnectModal`, `UiCopy`, and the enums `StampStatus`/`StampVariant`, `PriorityLevel`, `EntryKind`.
- `Components/Layout/MainLayout.razor`, `Components/Pages/StyleGuide.razor`, `Components/Showcase/PaletteSwatches` (+ `PaletteGroup`), `Components/Pages/{Home,NotFound}.razor`, `Components/Routes.razor`, `Components/App.razor`, `Components/_Imports.razor`, `Program.cs` (modify or create).

**Portal** (`src/TechStrap.Portal`)
- `libman.json`, `Styles/_tokens.scss`, `_theme.scss`, `_fonts.scss`, `_components.scss`, `app.scss`.
- `Components/Ui/`: `AccentScope`, `PoweredByFooter`, `PoweredByOptions`, `DevelopmentOnly`; `Components/Layout/PortalLayout.razor`, `Components/Pages/StyleGuide.razor`, `Routes.razor`, `Program.cs`.

**Tests**
- `tests/Shared/` (create, linked into both test projects): `RepositoryRoot`, `BrandTokenTable`, `CssColor`, `CompiledCss`, `FontStyleRules`.
- `tests/TechStrap.Admin.Tests/`, `tests/TechStrap.Portal.Tests/`: style, token, contrast, font, component and host tests.
- `scripts/tests/`: `Libman.Tests.ps1`, `BrandAssets.Tests.ps1`, `TrackedFiles.Tests.ps1`, `Dockerfiles.Tests.ps1` (modify).

**Docs and build**
- `docs/architecture/03-PACKAGE-MAP.md`, `04-DECISION-LOG.md`, `PHASE-02-brand-and-ux.md`, `00-DISCOVERY-INDEX.md`, `99-IMPLEMENTATION-ROADMAP.md`, `docs/BRAND.md`, `assets/brand/README.md`, `.gitignore`, `.dockerignore`, `Dockerfile.admin`, `Dockerfile.portal`.

## Plan Tasks to P02 Task Ids

| Plan task | Delivers | P02 task id |
| --- | --- | --- |
| 1. Package map rows and libman guard | Bootstrap row (5.3.8 via libman, jsdelivr, owning phase 02), Pester guard for every libman library | P02-T05 (follow-up) |
| 2. Product accent helper | `ProductAccent` in Contracts, `ProductAccentContrastTests`, D-025 | P02-T09 |
| 3. Brand tokens | `_brand-tokens.scss`, per-app `_tokens.scss`, light/dark/Auto, accent fallbacks, `StyleBuildTests`, `TokenContrastTests` | P02-T06 |
| 4. Self-hosted fonts | libman font libraries, `@font-face`, font tests, Dockerfile assertions | P02-T06 (fonts) |
| 5. Logo SVGs | vtracer SVGs, outlined wordmark, `BrandAssets.Tests.ps1` | P02-T08 |
| 6. Admin shell and style-guide skeleton | `DevelopmentOnly`, `MainLayout` with the SVG head mark, style guide basics, `StyleGuideEnvironmentTests` | P02-T07, P02-T08 (wiring) |
| 7. Status stamps and priority | `StatusStamp`, `PriorityMark`, motion rules | P02-T07 |
| 8. Tint code and keycaps | `TintedEntry`, `TintLegend`, `Kbd`, tint isolation guards | P02-T07 |
| 9. Retro window and 404 | `BrandWindow`, branded 404, three windows and logos in the guide | P02-T07, P02-T08 |
| 10. Reconnect and error UI | `ReconnectModal` mounted and styled, error view, previews | P02-T07 |
| 11. Portal | `PortalLayout`, `AccentScope`, `PoweredByFooter`, Portal style guide and env test | P02-T07, P02-T09 (sample accents) |
| 12. Review loop and phase close | tracked-files guard, logo-removal result, critique, doc ticks, clean-clone verification | P02-T08 (logo-removal record), P02-T10 |

---

### Task 1: Package map rows and libman guard

**Files:**
- Create: `scripts/tests/Libman.Tests.ps1`
- Modify: `docs/architecture/03-PACKAGE-MAP.md` (new section 4b before `## 5. Published by TechStrap`)

**Interfaces:**
- Consumes: `src/*/libman.json` (existing, Bootstrap only), `.gitignore` (existing `src/*/Styles/Vendor/`).
- Produces: section `## 4b. Front-end libraries restored at build by libman` of the package map. Every later task that adds a libman library adds a row here with the same version; `Libman.Tests.ps1` fails otherwise.

- [ ] **Step 1: Write the failing test**

Create `scripts/tests/Libman.Tests.ps1`:

```powershell
BeforeDiscovery {
    $root = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
    $script:Manifests = @(
        Get-ChildItem -Path (Join-Path $root 'src') -Filter libman.json -Recurse -Depth 2 |
            ForEach-Object { @{ App = $_.Directory.Name; Path = $_.FullName } }
    )
}

BeforeAll {
    $script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
    $script:MapText = Get-Content -LiteralPath (Join-Path $script:RepoRoot 'docs/architecture/03-PACKAGE-MAP.md') -Raw

    function Get-Libraries {
        param([string]$Path)
        $manifest = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
        return [pscustomobject]@{
            Provider  = $manifest.defaultProvider
            Libraries = @($manifest.libraries | ForEach-Object {
                $at = $_.library.LastIndexOf('@')
                [pscustomobject]@{ Name = $_.library.Substring(0, $at); Version = $_.library.Substring($at + 1); Destination = $_.destination }
            })
        }
    }
}

Describe 'libman manifest <App>' -ForEach $script:Manifests {
    BeforeAll {
        $script:Manifest = Get-Libraries -Path $Path
        $script:AppName = $App
    }

    It 'restores from jsdelivr and pins exact versions' {
        $script:Manifest.Provider | Should -Be 'jsdelivr'
        $script:Manifest.Libraries.Count | Should -BeGreaterThan 0
        foreach ($library in $script:Manifest.Libraries) {
            $library.Version | Should -Match '^\d+\.\d+\.\d+$' -Because "$($library.Name) must not float"
        }
    }

    It 'lists every library with the same version in the 4b front-end table of 03-PACKAGE-MAP.md' {
        $section = ($script:MapText -split '(?m)^## 4b\. ')[1]
        $section | Should -Not -BeNullOrEmpty -Because 'section 4b of the package map lists libman libraries'
        $section = ($section -split '(?m)^## ')[0]
        foreach ($library in $script:Manifest.Libraries) {
            $row = ($section -split "`n") | Where-Object { $_ -match ('^\|\s*`' + [regex]::Escape($library.Name) + '`\s*\|') } | Select-Object -First 1
            $row | Should -Not -BeNullOrEmpty -Because "$($library.Name) needs a row in section 4b"
            $row | Should -Match ('\|\s*' + [regex]::Escape($library.Version) + '\s*\|') -Because "$($library.Name) version must match the map"
        }
    }

    It 'restores only into gitignored folders, so no vendor or font file is ever committed' {
        $appDirectory = Join-Path $script:RepoRoot "src/$script:AppName"
        foreach ($library in $script:Manifest.Libraries) {
            $probe = "src/$script:AppName/$($library.Destination)/probe.file"
            & git -C $script:RepoRoot check-ignore --quiet $probe
            $LASTEXITCODE | Should -Be 0 -Because "$probe must be gitignored"
        }
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `pwsh -NoProfile -File scripts/Invoke-ScriptTests.ps1 -Path scripts/tests/Libman.Tests.ps1 -Output Minimal`
Expected: FAIL, `Tests Passed: 4, Failed: 2` (one per app). The failures read `Expected a value, because section 4b of the package map lists libman libraries, but got $null or empty.`

- [ ] **Step 3: Add the package map section**

In `docs/architecture/03-PACKAGE-MAP.md`, insert this block immediately before the line `## 5. Published by TechStrap` (Task 4 appends three rows to the same table):

```markdown
## 4b. Front-end libraries restored at build by libman (npm via jsdelivr)

These are npm packages, not NuGet packages, so `Directory.Packages.props` and `scripts/Check-PackageVersions.ps1` do not cover them (the table header deliberately has no `Package` or `Status` column). `scripts/tests/Libman.Tests.ps1` asserts that every `libman.json` library is pinned exactly, listed here with the same version, and restored only into a gitignored folder. Restored files are never committed.

| Library | Used for | Exact version | Source/release verified | Owning phase |
| --- | --- | --- | --- | --- |
| `bootstrap` | SCSS base for Admin and Portal (`Styles/Vendor/bootstrap`, `scss/**` only) | 5.3.8 | [5.3.8](https://www.npmjs.com/package/bootstrap/v/5.3.8) via jsdelivr, restored in both apps by `Microsoft.Web.LibraryManager.Build` | P02 |

```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `pwsh -NoProfile -File scripts/Invoke-ScriptTests.ps1 -Path scripts/tests/Libman.Tests.ps1 -Output Minimal` then `pwsh -NoProfile -File scripts/Check-PackageVersions.ps1`
Expected: `Tests Passed: 6, Failed: 0` and `Package version check passed: 41 packages match the package map.`

- [ ] **Step 5: Commit**

```bash
git add scripts/tests/Libman.Tests.ps1 docs/architecture/03-PACKAGE-MAP.md
git commit -m "docs: add front-end libraries table and libman guard test" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 2: Product accent helper

**Files:**
- Create: `src/TechStrap.Contracts/Branding/ProductAccent.cs`
- Create: `tests/TechStrap.Portal.Tests/Branding/Wcag.cs`
- Test: `tests/TechStrap.Portal.Tests/Branding/ProductAccentContrastTests.cs`
- Modify: `docs/architecture/04-DECISION-LOG.md` (add D-025)

**Interfaces:**
- Consumes: nothing.
- Produces (used by Tasks 3, 8, 11 and by PHASE-04 `UpdateProductRequestHandler`, PHASE-05 email rendering, PHASE-09 portal theming):
  - `namespace TechStrap.Contracts.Branding`
  - `readonly record struct ProductAccentColors(string Accent, string OnAccent, string AccentInk)` (all uppercase `#RRGGBB`)
  - `static bool ProductAccent.TryDerive(string? value, out ProductAccentColors colors)` (false and `default` for anything but a six-digit hex color; never throws)
  - `static double ProductAccent.ContrastRatio(string foregroundHex, string backgroundHex)` (throws `ArgumentException` for a malformed color)
  - `const double ProductAccent.MinimumTextContrast = 4.5`

**Why Contracts (D-025):** the architecture tests allow `Admin`/`Portal` to reference only Contracts, `Application` only Domain/Contracts, and Api/Worker Application/Infrastructure/Contracts. Contracts is the only project every consumer already references, and the helper needs only the BCL, so the "no project, package or framework reference" rule still holds. The cost is that the helper becomes public surface of the published `TechStrap.Contracts` package; D-025 records that.

- [ ] **Step 1: Write the failing tests**

Create `tests/TechStrap.Portal.Tests/Branding/Wcag.cs` (a deliberately independent WCAG implementation, so the production code is checked against a second one):

```csharp
namespace TechStrap.Portal.Tests.Branding;

/// <summary>
/// An independent WCAG 2.x implementation for tests, deliberately not shared with the production
/// code so the accent rule is checked against a second implementation of the formula.
/// </summary>
internal static class Wcag
{
    public static double Ratio(string foregroundHex, string backgroundHex)
    {
        var a = Luminance(foregroundHex);
        var b = Luminance(backgroundHex);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    private static double Luminance(string hex)
    {
        var r = Channel(Convert.ToInt32(hex.Substring(1, 2), 16));
        var g = Channel(Convert.ToInt32(hex.Substring(3, 2), 16));
        var b = Channel(Convert.ToInt32(hex.Substring(5, 2), 16));
        return (0.2126 * r) + (0.7152 * g) + (0.0722 * b);
    }

    private static double Channel(int value)
    {
        var s = value / 255.0;
        return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
    }
}
```

Create `tests/TechStrap.Portal.Tests/Branding/ProductAccentContrastTests.cs`:

```csharp
using TechStrap.Contracts.Branding;

namespace TechStrap.Portal.Tests.Branding;

/// <summary>
/// The product-accent rule of docs/BRAND.md section 22: on-accent is white or black (whichever contrasts
/// more), accent-ink is the accent darkened until it reaches 4.5:1 on white, and only a malformed value is rejected.
/// </summary>
public sealed class ProductAccentContrastTests
{
    private const string White = "#FFFFFF";
    private const double Aa = 4.5;
    private const int SweepStep = 5;

    // The vectors of BRAND.md section 22, plus the edge cases: pure white, pure black, the mid-gray that just
    // misses AA on white (#777777) and the gray that just meets it (#767676).
    public static TheoryData<string, string, string> Vectors() => new()
    {
        { "#7C3AED", "#FFFFFF", "#7C3AED" },
        { "#F59E0B", "#000000", "#9D6507" },
        { "#0F3D2E", "#FFFFFF", "#0F3D2E" },
        { "#2E9AFF", "#000000", "#2375C2" },
        { "#4B7D87", "#FFFFFF", "#4B7D87" },
        { "#FFFFFF", "#000000", "#707070" },
        { "#000000", "#FFFFFF", "#000000" },
        { "#777777", "#000000", "#727272" },
        { "#767676", "#000000", "#767676" },
    };

    [Theory]
    [MemberData(nameof(Vectors))]
    public void Derives_the_documented_on_accent_and_ink(string accent, string onAccent, string ink)
    {
        ProductAccent.TryDerive(accent, out var colors).ShouldBeTrue();

        colors.Accent.ShouldBe(accent);
        colors.OnAccent.ShouldBe(onAccent);
        colors.AccentInk.ShouldBe(ink);
    }

    [Fact]
    public void The_worst_case_accent_still_reaches_4_58_with_white()
    {
        ProductAccent.TryDerive("#4B7D87", out var colors).ShouldBeTrue();

        Wcag.Ratio(colors.OnAccent, colors.Accent).ShouldBeGreaterThanOrEqualTo(4.58);
    }

    [Fact]
    public void Every_sampled_colour_keeps_on_accent_and_ink_at_or_above_AA()
    {
        var checkedColours = 0;
        for (var r = 0; r <= 255; r += SweepStep)
        {
            for (var g = 0; g <= 255; g += SweepStep)
            {
                for (var b = 0; b <= 255; b += SweepStep)
                {
                    var accent = $"#{r:X2}{g:X2}{b:X2}";

                    ProductAccent.TryDerive(accent, out var colors).ShouldBeTrue(accent);
                    Wcag.Ratio(colors.OnAccent, accent).ShouldBeGreaterThanOrEqualTo(Aa, $"on-accent for {accent}");
                    Wcag.Ratio(colors.AccentInk, White).ShouldBeGreaterThanOrEqualTo(Aa, $"ink for {accent}");
                    checkedColours++;
                }
            }
        }

        checkedColours.ShouldBe(52 * 52 * 52);
    }

    [Fact]
    public void Ink_is_unchanged_when_the_accent_already_meets_AA_on_white()
    {
        ProductAccent.TryDerive("#1D4FA8", out var colors).ShouldBeTrue();

        colors.AccentInk.ShouldBe("#1D4FA8");
    }

    [Fact]
    public void Lowercase_input_is_accepted_and_normalised_to_uppercase()
    {
        ProductAccent.TryDerive("#7c3aed", out var colors).ShouldBeTrue();

        colors.Accent.ShouldBe("#7C3AED");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("#12345")]
    [InlineData("#1234567")]
    [InlineData("#12345678")]
    [InlineData("#FFF")]
    [InlineData("123456")]
    [InlineData("red")]
    [InlineData("#GGGGGG")]
    [InlineData(" #123456")]
    [InlineData("#123456 ")]
    [InlineData("#123456;background:url(x)")]
    public void Rejects_anything_that_is_not_a_six_digit_hex_colour(string? value)
    {
        ProductAccent.TryDerive(value, out var colors).ShouldBeFalse();

        colors.ShouldBe(default);
    }

    [Fact]
    public void ContrastRatio_matches_the_independent_implementation()
    {
        ProductAccent.ContrastRatio("#FFFFFF", "#000000").ShouldBe(21.0, 0.001);
        ProductAccent.ContrastRatio("#777777", "#FFFFFF").ShouldBe(Wcag.Ratio("#777777", "#FFFFFF"), 0.0001);
    }

    [Fact]
    public void ContrastRatio_rejects_malformed_colours()
    {
        Should.Throw<ArgumentException>(() => ProductAccent.ContrastRatio("red", "#FFFFFF"));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test --project tests/TechStrap.Portal.Tests`
Expected: FAIL to compile, `error CS0234: The type or namespace name 'Contracts' does not exist in the namespace 'TechStrap'` (the Contracts assembly is still empty).

- [ ] **Step 3: Write the implementation**

Create `src/TechStrap.Contracts/Branding/ProductAccent.cs`. The darkening uses integer arithmetic (`round(c * (25 - k) / 25)` never lands on a .5 tie), so it matches the BRAND.md vectors exactly:

```csharp
using System.Globalization;

namespace TechStrap.Contracts.Branding;

/// <summary>The three values derived from one product accent color (docs/BRAND.md section 22), all as uppercase <c>#RRGGBB</c>.</summary>
/// <param name="Accent">Fills and borders: the product's color, as entered (normalized to uppercase).</param>
/// <param name="OnAccent">Text on an accent fill: white or black, whichever contrasts more.</param>
/// <param name="AccentInk">The accent as text or an outline on white: the accent itself when it already reaches 4.5:1, otherwise darkened until it does.</param>
public readonly record struct ProductAccentColors(string Accent, string OnAccent, string AccentInk);

/// <summary>
/// The single implementation of the product-accent rule. The portal, the product save validation (PHASE-04) and
/// customer email rendering (PHASE-05) all call this; nothing else may recompute these values.
/// Pure and dependency-free, so it lives in the Contracts leaf that every host already references.
/// </summary>
public static class ProductAccent
{
    /// <summary>WCAG 2.x AA contrast for normal text.</summary>
    public const double MinimumTextContrast = 4.5;

    private const string White = "#FFFFFF";
    private const string Black = "#000000";
    private const int HexLength = 7;
    private const int DarkenDenominator = 25;
    private const int DarkenRoundingOffset = 12;

    /// <summary>
    /// Derives the three accent properties from a <c>#RRGGBB</c> value. Only a malformed value is rejected;
    /// no color is rejected for low contrast because white-or-black on-accent always reaches at least 4.58:1.
    /// </summary>
    public static bool TryDerive(string? value, out ProductAccentColors colors)
    {
        colors = default;
        if (!TryParse(value, out var rgb))
        {
            return false;
        }

        var onAccent = Contrast(rgb, WhiteRgb) >= Contrast(rgb, BlackRgb) ? White : Black;
        colors = new ProductAccentColors(Format(rgb), onAccent, Format(DarkenUntilReadableOnWhite(rgb)));
        return true;
    }

    /// <summary>WCAG contrast ratio (1 to 21) between two <c>#RRGGBB</c> colors. Throws <see cref="ArgumentException"/> for a malformed color.</summary>
    public static double ContrastRatio(string foregroundHex, string backgroundHex)
    {
        if (!TryParse(foregroundHex, out var foreground))
        {
            throw new ArgumentException("Not a #RRGGBB color.", nameof(foregroundHex));
        }

        if (!TryParse(backgroundHex, out var background))
        {
            throw new ArgumentException("Not a #RRGGBB color.", nameof(backgroundHex));
        }

        return Contrast(foreground, background);
    }

    private static readonly (int R, int G, int B) WhiteRgb = (255, 255, 255);
    private static readonly (int R, int G, int B) BlackRgb = (0, 0, 0);

    // Scale each channel of the ORIGINAL color by (1 - 0.04 k), k = 1, 2, 3 ..., until the result reaches 4.5:1 on white.
    // Integer arithmetic: round(c * (25 - k) / 25) never lands on a .5 tie, so (c * (25 - k) + 12) / 25 is exact.
    private static (int R, int G, int B) DarkenUntilReadableOnWhite((int R, int G, int B) accent)
    {
        if (Contrast(accent, WhiteRgb) >= MinimumTextContrast)
        {
            return accent;
        }

        for (var k = 1; k < DarkenDenominator; k++)
        {
            var darkened = (Scale(accent.R, k), Scale(accent.G, k), Scale(accent.B, k));
            if (Contrast(darkened, WhiteRgb) >= MinimumTextContrast)
            {
                return darkened;
            }
        }

        return BlackRgb;
    }

    private static int Scale(int channel, int k) => ((channel * (DarkenDenominator - k)) + DarkenRoundingOffset) / DarkenDenominator;

    private static bool TryParse(string? value, out (int R, int G, int B) rgb)
    {
        rgb = default;
        if (value is not { Length: HexLength } || value[0] != '#')
        {
            return false;
        }

        for (var i = 1; i < HexLength; i++)
        {
            if (!char.IsAsciiHexDigit(value[i]))
            {
                return false;
            }
        }

        rgb = (
            int.Parse(value.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            int.Parse(value.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            int.Parse(value.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        return true;
    }

    private static string Format((int R, int G, int B) rgb) => string.Create(CultureInfo.InvariantCulture, $"#{rgb.R:X2}{rgb.G:X2}{rgb.B:X2}");

    private static double Contrast((int R, int G, int B) a, (int R, int G, int B) b)
    {
        var la = Luminance(a);
        var lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double Luminance((int R, int G, int B) rgb) =>
        (0.2126 * Linear(rgb.R)) + (0.7152 * Linear(rgb.G)) + (0.0722 * Linear(rgb.B));

    private static double Linear(int channel)
    {
        var s = channel / 255.0;
        return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test --project tests/TechStrap.Portal.Tests` then `dotnet test --project tests/TechStrap.Architecture.Tests`
Expected: PASS, `total: 29` in Portal.Tests (the RGB sweep checks 52 x 52 x 52 = 140,608 colors in about a second) and `total: 30` in Architecture.Tests (Contracts still has no project, package or framework reference).

- [ ] **Step 5: Record decision D-025**

In `docs/architecture/04-DECISION-LOG.md`: leave the header's `- **Proposed:**` line unchanged (the owner approved D-025 on 2026-10-02); add this row after the D-024 row of the Index table; and append the section at the end of the file.

```markdown
| D-025 | The product-accent derivation helper lives in `TechStrap.Contracts` | Approved (owner 2026-10-02) | 2026-10-02 | PHASE-02, PHASE-04, PHASE-05, PHASE-09, BRAND.md |
```

```markdown

---

## D-025: The product-accent derivation helper lives in `TechStrap.Contracts`

- **Status:** Approved
- **Date:** 2026-10-02
- **Owner:** Jon Seeley
- **Related artifacts:** `docs/BRAND.md` section 22, PHASE-02 (P02-T09), PHASE-04 (product save validation), PHASE-05 (email rendering), PHASE-09 (portal theming), D-016

### Context
BRAND.md section 22 requires one pure function that turns a product accent (`#RRGGBB`) into `--ts-accent`, `--ts-on-accent` and `--ts-accent-ink`. Three hosts need it: the Portal (runtime properties), the API (reject a malformed accent at save, PHASE-04) and email rendering (PHASE-05, in the Worker or Api). The architecture tests fix the reference direction: Application references only Domain, Contracts and `SyntaxCircus.Common`; Api and Worker reference Application, Infrastructure and Contracts; Admin and Portal reference Contracts only.

### Decision
`TechStrap.Contracts.Branding.ProductAccent` (a static class with `TryDerive` and `ContrastRatio`, plus the `ProductAccentColors` record) lives in `TechStrap.Contracts`. Contracts is the only project every consumer already references, it stays dependency-free (the helper uses only the BCL), and the rule is part of the wire contract: the stored accent and the values derived from it must agree in every host.

### Alternatives Considered
- Domain: Portal and Admin may not reference Domain (architecture tests), so the Portal would need its own copy.
- A new `TechStrap.Branding` project: needs an `AllowedProjectReferences` entry for every consumer and a change to the "ten source projects" test, for roughly 100 lines of code.
- A Portal-local helper plus a second copy in Application: two implementations of one rule, which BRAND.md forbids.

### Consequences
- `TechStrap.Contracts` is published as a NuGet package (PHASE-11), so `ProductAccent` becomes public surface of that package and is covered by its semver promise. It is small and stable; revisit if the surface grows.
- The architecture tests need no change: Contracts still has no project, package or framework reference.
- PHASE-04 calls `ProductAccent.TryDerive` in `UpdateProductRequestHandler`; PHASE-05 calls it in the email renderer; PHASE-09 calls it in the portal product theme.

### Approval
- **Approved by:** Jon Seeley (owner, 2026-10-02 plan review)
- **Approved on:** 2026-10-02
```

- [ ] **Step 6: Commit**

```bash
git add src/TechStrap.Contracts/Branding tests/TechStrap.Portal.Tests/Branding docs/architecture/04-DECISION-LOG.md
git commit -m "feat(contracts): add product accent derivation helper" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 3: Brand tokens, Bootstrap mapping, light/dark/Auto

**Files:**
- Create: `assets/brand/scss/_brand-tokens.scss`
- Create: `src/TechStrap.Admin/Styles/_color-mode.scss`, `src/TechStrap.Admin/Styles/_theme.scss`, `src/TechStrap.Portal/Styles/_theme.scss`
- Modify: `src/TechStrap.Admin/Styles/_tokens.scss`, `src/TechStrap.Admin/Styles/app.scss`, `src/TechStrap.Portal/Styles/_tokens.scss`, `src/TechStrap.Portal/Styles/app.scss`
- Create: `tests/Shared/RepositoryRoot.cs`, `tests/Shared/BrandTokenTable.cs`, `tests/Shared/CssColor.cs`, `tests/Shared/CompiledCss.cs`
- Modify: `tests/TechStrap.Admin.Tests/StyleBuildTests.cs`, `tests/TechStrap.Portal.Tests/StyleBuildTests.cs`, both test csproj files
- Test: `tests/TechStrap.Admin.Tests/TokenContrastTests.cs`
- Modify: `docs/BRAND.md` (one rule paragraph)

**Interfaces:**
- Consumes: `ProductAccent.ContrastRatio(string, string)` (Task 2); `docs/BRAND.md` section 12 tables.
- Produces:
  - Sass: `$ts-light`, `$ts-dark`, `$ts-portal`, `$ts-accent-fallback` maps (keys are token names without `--`), `ts-color($key, $theme: "light")`, `ts-portal-color($key)`, `@mixin ts-emit($map)`, `$ts-font-sans|mono|serif`.
  - CSS: every BRAND.md token as a custom property on `:root,[data-bs-theme=light]`; the dark values on `[data-bs-theme=dark]` and, for Auto, on `:root:not([data-bs-theme=light],[data-bs-theme=dark])` inside `@media (prefers-color-scheme: dark)`; `--ts-accent`, `--ts-on-accent`, `--ts-accent-ink` fallbacks (`#1D4FA8`, `#FFFFFF`, `#1D4FA8`); `--ts-font-sans|mono|serif`.
  - Test support (internal, linked into both test projects): `RepositoryRoot.Find()/Combine(...)`, `BrandTokenTable.Read()` returning `BrandToken(Name, Light, Dark?)` rows parsed from BRAND.md, `CssColor.Normalize/IsColour`, `CompiledCss.Load(app)` with `.Text` and `.Declarations(exactSelector)`.

**Sharing decision (verified in the scratch build):** the shared file is imported by relative path (`@import "../../../assets/brand/scss/brand-tokens";`). Sass resolves it relative to the importing file, so there is no copy and no MSBuild link. The one trap is the Docker build context: `.dockerignore` excludes `assets/`, so Task 4 re-includes `!assets/brand/scss/` (the scratch `docker build -f Dockerfile.admin` passed with it).

- [ ] **Step 1: Write the shared test support**

Create the four files under `tests/Shared/` and link them into both test projects. `BrandTokenTable` reads the tables of `docs/BRAND.md` itself, so the tests compare the CSS with the document, not with a second hand-typed list.

```csharp
namespace TechStrap.Tests.Shared;

/// <summary>Locates the repository root from the test output folder (the folder that holds TechStrap.slnx).</summary>
internal static class RepositoryRoot
{
    public static string Find()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TechStrap.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("TechStrap.slnx not found above " + AppContext.BaseDirectory);
    }

    public static string Combine(params string[] segments) => Path.Combine([Find(), .. segments]);
}
```

```csharp
using System.Text.RegularExpressions;

namespace TechStrap.Tests.Shared;

/// <summary>One color token of docs/BRAND.md section 12. <see cref="Dark"/> is null for tokens with a single value (the portal tokens).</summary>
internal sealed record BrandToken(string Name, string Light, string? Dark);

/// <summary>
/// Reads the token tables of docs/BRAND.md, the system of record, so the tests compare the compiled CSS with the
/// document itself instead of with a second hand-typed copy of the values.
/// </summary>
internal static partial class BrandTokenTable
{
    [GeneratedRegex("`--([a-z0-9-]+)`")]
    private static partial Regex NamePattern();

    public static IReadOnlyList<BrandToken> Read()
    {
        var tokens = new List<BrandToken>();
        foreach (var line in File.ReadLines(RepositoryRoot.Combine("docs", "BRAND.md")))
        {
            if (!line.StartsWith("| `--", StringComparison.Ordinal))
            {
                continue;
            }

            var cells = line.Trim().Trim('|').Split('|').Select(c => c.Trim()).ToArray();
            var names = NamePattern().Matches(cells[0]);
            if (names.Count != 1)
            {
                continue; // the product accent row names three tokens and carries no value
            }

            var colours = cells.Skip(2).Select(c => c.Trim('`')).Where(CssColor.IsColour).Take(2).ToArray();
            if (colours.Length == 0)
            {
                continue;
            }

            tokens.Add(new BrandToken(names[0].Groups[1].Value, colours[0], colours.Length > 1 ? colours[1] : null));
        }

        return tokens;
    }
}
```

```csharp
using System.Text.RegularExpressions;

namespace TechStrap.Tests.Shared;

/// <summary>Compares CSS colors semantically: Sass compressed output rewrites <c>#FFFFFF</c> as <c>#fff</c> and <c>0.14</c> as <c>.14</c>.</summary>
internal static partial class CssColor
{
    [GeneratedRegex(@"^#[0-9A-Fa-f]{3}([0-9A-Fa-f]{3})?$")]
    private static partial Regex HexPattern();

    public static bool IsColour(string value) => HexPattern().IsMatch(value) || value.StartsWith("rgba(", StringComparison.OrdinalIgnoreCase);

    public static string Normalize(string value)
    {
        value = value.Trim();
        if (value.StartsWith('#'))
        {
            var hex = value[1..].ToUpperInvariant();
            return hex.Length == 3 ? "#" + string.Concat(hex.Select(c => new string(c, 2))) : "#" + hex;
        }

        return value.ToLowerInvariant().Replace(" ", string.Empty, StringComparison.Ordinal).Replace("(0.", "(.", StringComparison.Ordinal).Replace(",0.", ",.", StringComparison.Ordinal);
    }
}
```

```csharp
using System.Text.RegularExpressions;

namespace TechStrap.Tests.Shared;

/// <summary>
/// The CSS the build compiled into src/{app}/wwwroot/css/app.css (compressed, never committed), with a minimal rule reader:
/// enough to ask "what does the rule with this exact selector declare", which is all the token tests need.
/// </summary>
internal sealed partial class CompiledCss
{
    [GeneratedRegex(@"(?<selector>[^{}]+)\{(?<body>[^{}]*)\}")]
    private static partial Regex RulePattern();

    private readonly List<(string Selector, string Body)> _rules;

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex CommentPattern();

    private CompiledCss(string text)
    {
        Text = CommentPattern().Replace(text, string.Empty);
        _rules = RulePattern().Matches(Text).Select(m => (m.Groups["selector"].Value.Trim(), m.Groups["body"].Value)).ToList();
    }

    public string Text { get; }

    public static CompiledCss Load(string app)
    {
        var path = RepositoryRoot.Combine("src", app, "wwwroot", "css", "app.css");
        File.Exists(path).ShouldBeTrue($"{path} should be generated by the build");
        return new CompiledCss(File.ReadAllText(path));
    }

    /// <summary>Every declaration of every rule whose selector is exactly <paramref name="selector"/> (Bootstrap and brand rules merged, last wins), colors normalized.</summary>
    public IReadOnlyDictionary<string, string> Declarations(string selector)
    {
        var declarations = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (ruleSelector, body) in _rules.Where(r => r.Selector == selector))
        {
            foreach (var declaration in body.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var colon = declaration.IndexOf(':', StringComparison.Ordinal);
                if (colon > 0)
                {
                    var value = declaration[(colon + 1)..].Trim();
                    declarations[declaration[..colon].Trim()] = CssColor.IsColour(value) ? CssColor.Normalize(value) : value;
                }
            }
        }

        return declarations;
    }
}
```

In both `tests/TechStrap.Admin.Tests/TechStrap.Admin.Tests.csproj` and `tests/TechStrap.Portal.Tests/TechStrap.Portal.Tests.csproj` add a `Compile` item group next to the existing `bunit` one:

```xml
  <ItemGroup>
    <Compile Include="../Shared/*.cs" LinkBase="Shared" />
  </ItemGroup>
```

- [ ] **Step 2: Write the failing tests**

Replace `tests/TechStrap.Admin.Tests/StyleBuildTests.cs`:

```csharp
using System.Text.RegularExpressions;
using TechStrap.Tests.Shared;

namespace TechStrap.Admin.Tests;

/// <summary>The build compiles Styles/app.scss (Bootstrap via libman) to wwwroot/css/app.css; it is never committed.</summary>
public sealed class StyleBuildTests
{
    private const string LightScope = ":root,[data-bs-theme=light]";
    private const string DarkScope = "[data-bs-theme=dark]";
    private const string AutoDarkScope = ":root:not([data-bs-theme=light],[data-bs-theme=dark])";

    private static readonly CompiledCss Css = CompiledCss.Load("TechStrap.Admin");

    [Fact]
    public void Compiled_css_exists_and_contains_bootstrap()
    {
        Css.Text.ShouldContain("--bs-primary");
    }

    [Fact]
    public void The_token_table_of_BRAND_md_was_read()
    {
        // 18 surface + 5 tint + 6 status + 10 brand-moment tokens have a dark value; the 5 portal tokens do not.
        var tokens = BrandTokenTable.Read();

        tokens.Count(t => t.Dark is not null).ShouldBe(39);
        tokens.Count(t => t.Dark is null).ShouldBe(5);
    }

    [Fact]
    public void Every_brand_token_is_a_custom_property_on_root_with_its_light_value()
    {
        var root = Css.Declarations(LightScope);

        foreach (var token in BrandTokenTable.Read())
        {
            root.ShouldContainKey($"--{token.Name}", token.Name);
            root[$"--{token.Name}"].ShouldBe(CssColor.Normalize(token.Light), token.Name);
        }
    }

    [Fact]
    public void Every_dark_brand_token_is_set_under_data_bs_theme_dark()
    {
        var dark = Css.Declarations(DarkScope);

        foreach (var token in BrandTokenTable.Read().Where(t => t.Dark is not null))
        {
            dark.ShouldContainKey($"--{token.Name}", token.Name);
            dark[$"--{token.Name}"].ShouldBe(CssColor.Normalize(token.Dark!), token.Name);
        }
    }

    [Fact]
    public void Auto_theme_follows_prefers_color_scheme_unless_a_theme_is_chosen()
    {
        Regex.IsMatch(Css.Text, @"@media\s*\(prefers-color-scheme:\s*dark\)\{" + Regex.Escape(AutoDarkScope) + @"\{").ShouldBeTrue();
        var auto = Css.Declarations(AutoDarkScope);

        foreach (var token in BrandTokenTable.Read().Where(t => t.Dark is not null))
        {
            auto[$"--{token.Name}"].ShouldBe(CssColor.Normalize(token.Dark!), token.Name);
        }

        auto["--bs-body-bg"].ShouldBe("#0B1E40");
    }

    [Fact]
    public void Bootstrap_variables_follow_the_brand_mapping()
    {
        var light = Css.Declarations(LightScope);

        light["--bs-primary"].ShouldBe("#1D4FA8");
        light["--bs-body-bg"].ShouldBe("#F7F5EE");
        light["--bs-body-color"].ShouldBe("#14213D");
        light["--bs-border-color"].ShouldBe("#C5D0E6");
        light["--bs-success"].ShouldBe("#14702F");
        light["--bs-info"].ShouldBe("#1D5FB8");
        light["--bs-warning"].ShouldBe("#8A5300");
        light["--bs-danger"].ShouldBe("#B3141C");
        light["--bs-secondary"].ShouldBe("#5B6475");
        Css.Declarations(DarkScope)["--bs-body-bg"].ShouldBe("#0B1E40");
        Css.Declarations(DarkScope)["--bs-body-color"].ShouldBe("#E8EFFF");
    }

    [Fact]
    public void The_portal_accent_properties_have_fallbacks_for_the_style_guide()
    {
        var root = Css.Declarations(LightScope);

        root["--ts-accent"].ShouldBe("#1D4FA8");
        root["--ts-on-accent"].ShouldBe("#FFFFFF");
        root["--ts-accent-ink"].ShouldBe("#1D4FA8");
    }

    [Fact]
    public void Corners_are_square_and_shadows_are_hard_offsets()
    {
        var root = Css.Declarations(LightScope);

        root["--bs-border-radius"].ShouldBe("0");
        root["--bs-box-shadow"].ShouldBe("3px 3px 0 var(--shadow)");
        root["--bs-box-shadow-sm"].ShouldBe("2px 2px 0 var(--shadow)");
    }
}
```

Replace `tests/TechStrap.Portal.Tests/StyleBuildTests.cs`:

```csharp
using System.Text.RegularExpressions;
using TechStrap.Tests.Shared;

namespace TechStrap.Portal.Tests;

/// <summary>The build compiles Styles/app.scss (Bootstrap via libman) to wwwroot/css/app.css; it is never committed.</summary>
public sealed class StyleBuildTests
{
    private const string LightScope = ":root,[data-bs-theme=light]";

    private static readonly string[] AdminOnlyTokens =
        ["--paper", "--sheet", "--rail", "--head", "--rule", "--margin", "--canary", "--pink", "--note-ink", "--st-new", "--st-open", "--st-spam", "--bm-plate", "--bm-edge", "--bm-crt", "--bm-led"];

    private static readonly CompiledCss Css = CompiledCss.Load("TechStrap.Portal");

    [Fact]
    public void Compiled_css_exists_and_contains_bootstrap()
    {
        Css.Text.ShouldContain("--bs-primary");
    }

    [Fact]
    public void Every_portal_token_of_BRAND_md_is_a_custom_property_on_root()
    {
        var portalTokens = BrandTokenTable.Read().Where(t => t.Name.StartsWith("p-", StringComparison.Ordinal)).ToList();
        portalTokens.Count.ShouldBe(5);
        var root = Css.Declarations(LightScope);

        foreach (var token in portalTokens)
        {
            root[$"--{token.Name}"].ShouldBe(CssColor.Normalize(token.Light), token.Name);
        }
    }

    [Fact]
    public void The_product_accent_properties_have_documented_fallbacks()
    {
        var root = Css.Declarations(LightScope);

        root["--ts-accent"].ShouldBe("#1D4FA8");
        root["--ts-on-accent"].ShouldBe("#FFFFFF");
        root["--ts-accent-ink"].ShouldBe("#1D4FA8");
    }

    [Fact]
    public void Bootstrap_variables_follow_the_portal_mapping()
    {
        var root = Css.Declarations(LightScope);

        root["--bs-body-bg"].ShouldBe("#FFFFFF");
        root["--bs-body-color"].ShouldBe("#1B1B22");
        root["--bs-secondary-color"].ShouldBe("#4A4A57");
        root["--bs-border-color"].ShouldBe("#D4D4DC");
        root["--bs-light"].ShouldBe("#F5F5F7");
    }

    [Fact]
    public void Primary_controls_take_the_runtime_product_accent()
    {
        Regex.IsMatch(Css.Text, @"--bs-btn-bg:\s*var\(--ts-accent\)").ShouldBeTrue();
        Regex.IsMatch(Css.Text, @"--bs-btn-color:\s*var\(--ts-on-accent\)").ShouldBeTrue();
        Regex.IsMatch(Css.Text, @"a\{color:\s*var\(--ts-accent-ink\)").ShouldBeTrue();
    }

    [Fact]
    public void The_portal_is_light_only_and_carries_no_admin_tokens()
    {
        Css.Declarations("[data-bs-theme=dark]").ShouldBeEmpty();
        Css.Text.ShouldNotContain("prefers-color-scheme");
        Css.Text.ShouldNotContain("color-scheme:dark");
        foreach (var token in AdminOnlyTokens)
        {
            Css.Text.ShouldNotContain(token + ":", customMessage: token);
        }
    }
}
```

Create `tests/TechStrap.Admin.Tests/TokenContrastTests.cs` (pins BRAND.md's "every text pair meets AA in both themes" claim against the compiled CSS):

```csharp
using TechStrap.Contracts.Branding;
using TechStrap.Tests.Shared;

namespace TechStrap.Admin.Tests;

/// <summary>
/// BRAND.md section 12 states that every text-on-background pair meets WCAG AA (4.5:1) in both themes. These tests pin that
/// claim against the compiled CSS, so a token edit that breaks a pair fails the build. One pair is deliberately absent:
/// --ink-3 on --sel in the dark theme is 4.32:1, so tertiary text must not be placed on a selected row (BRAND.md section 12 rule).
/// </summary>
public sealed class TokenContrastTests
{
    private const double Aa = 4.5;

    private static readonly CompiledCss Css = CompiledCss.Load("TechStrap.Admin");

    private static readonly (string Foreground, string Background)[] Pairs =
    [
        ("ink", "paper"), ("ink", "sheet"), ("ink", "rail"), ("ink", "head"), ("ink", "hover"), ("ink", "sel"), ("ink", "canary"), ("ink", "pink"),
        ("ink-2", "paper"), ("ink-2", "sheet"), ("ink-2", "rail"), ("ink-2", "head"), ("ink-2", "hover"), ("ink-2", "sel"), ("ink-2", "canary"), ("ink-2", "pink"),
        ("ink-3", "paper"), ("ink-3", "sheet"), ("ink-3", "rail"), ("ink-3", "head"), ("ink-3", "hover"), ("ink-3", "canary"), ("ink-3", "pink"),
        ("note-ink", "pink"), ("note-ink", "sheet"), ("note-ink", "paper"), ("note-ink", "canary"),
        ("st-new", "paper"), ("st-new", "sheet"), ("st-new", "hover"), ("st-new", "sel"),
        ("st-open", "paper"), ("st-open", "sheet"), ("st-open", "hover"), ("st-open", "sel"),
        ("st-pending", "paper"), ("st-pending", "sheet"), ("st-pending", "hover"), ("st-pending", "sel"),
        ("st-solved", "paper"), ("st-solved", "sheet"), ("st-solved", "hover"), ("st-solved", "sel"),
        ("st-closed", "paper"), ("st-closed", "sheet"), ("st-closed", "hover"), ("st-closed", "sel"),
        ("st-spam", "paper"), ("st-spam", "sheet"), ("st-spam", "hover"), ("st-spam", "sel"),
        ("on-accent", "accent"), ("accent", "paper"), ("accent", "sheet"), ("accent", "head"),
        ("bm-text", "bm-plate"), ("bm-text2", "bm-plate"), ("bm-on-bar", "bm-bar"), ("bm-on-crt", "bm-crt"),
    ];

    public static TheoryData<string, string, string> Cases()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var theme in new[] { "light", "dark" })
        {
            foreach (var (foreground, background) in Pairs)
            {
                data.Add(theme, foreground, background);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Text_pair_meets_AA(string theme, string foreground, string background)
    {
        var scope = theme == "light" ? ":root,[data-bs-theme=light]" : "[data-bs-theme=dark]";
        var tokens = Css.Declarations(scope);

        ProductAccent.ContrastRatio(tokens[$"--{foreground}"], tokens[$"--{background}"])
            .ShouldBeGreaterThanOrEqualTo(Aa, $"--{foreground} on --{background} in the {theme} theme");
    }
}
```

- [ ] **Step 3: Run to verify they fail**

Run: `dotnet test --project tests/TechStrap.Admin.Tests --filter-class "*StyleBuildTests"` and `dotnet test --project tests/TechStrap.Portal.Tests --filter-class "*StyleBuildTests"`
Expected: Admin FAIL, 6 of 8 tests (`Every_brand_token_is_a_custom_property_on_root_with_its_light_value`, `Every_dark_brand_token_...`, `Auto_theme_follows_...`, `Bootstrap_variables_follow_the_brand_mapping`, `The_portal_accent_properties_have_fallbacks_...`, `Corners_are_square_...`); `The_token_table_of_BRAND_md_was_read` already passes (39 tokens with a dark value, 5 without). Portal FAIL on the token, accent and mapping tests.

- [ ] **Step 4: Write the shared token source**

Create `assets/brand/scss/_brand-tokens.scss`. The maps were generated from the BRAND.md tables (39 two-theme tokens, 5 portal tokens); `StyleBuildTests` fails if any value drifts from the document.

```scss
// docs/BRAND.md section 12 is the system of record; this file is its Sass form. A test (StyleBuildTests) compares the
// compiled CSS with the tables in BRAND.md, so a value changed in only one place fails the build.
// Shared by Admin and Portal through a relative @import, so there is one copy and no MSBuild link.

// Admin, light theme (also the :root default).
$ts-light: (
  "paper": #F7F5EE,
  "sheet": #FFFFFF,
  "rail": #EFEBDD,
  "head": #E8EDF8,
  "ink": #14213D,
  "ink-2": #44506B,
  "ink-3": #5B667E,
  "rule": #C5D0E6,
  "rule-strong": #8E9FC4,
  "margin": #D9262E,
  "hover": #EAF0FB,
  "sel": #DDE8FA,
  "overlay": rgba(20,33,61,.06),
  "shadow": rgba(20,33,61,.14),
  "scrim": rgba(20,33,61,.45),
  "accent": #1D4FA8,
  "on-accent": #FFFFFF,
  "focus": #B3141C,
  "canary": #FFF4B0,
  "canary-edge": #C9B23A,
  "pink": #FFE0E3,
  "pink-edge": #D26E7B,
  "note-ink": #7A1022,
  "st-new": #1D5FB8,
  "st-open": #14702F,
  "st-pending": #8A5300,
  "st-solved": #1E5A6B,
  "st-closed": #5B6475,
  "st-spam": #B3141C,
  "bm-plate": #EFDDBB,
  "bm-edge": #0B1F4B,
  "bm-bar": #0B1F4B,
  "bm-on-bar": #EFDDBB,
  "bm-text": #0B1F4B,
  "bm-text2": #434E6C,
  "bm-crt": #3B95E0,
  "bm-on-crt": #0B1F4B,
  "bm-led": #22A447,
  "bm-shadow": #0B1F4B
);

// Admin, dark theme. Same keys as $ts-light.
$ts-dark: (
  "paper": #0B1E40,
  "sheet": #10285A,
  "rail": #08172F,
  "head": #0E2650,
  "ink": #E8EFFF,
  "ink-2": #B7C6E6,
  "ink-3": #9DAECF,
  "rule": #274A87,
  "rule-strong": #4A71B8,
  "margin": #FF6B73,
  "hover": #163670,
  "sel": #1C4286,
  "overlay": rgba(255,255,255,.04),
  "shadow": rgba(0,0,0,.4),
  "scrim": rgba(0,0,0,.6),
  "accent": #8FC0FF,
  "on-accent": #0B1E40,
  "focus": #FFD84A,
  "canary": #3A3A1B,
  "canary-edge": #8C8A3A,
  "pink": #4A1E33,
  "pink-edge": #C46A86,
  "note-ink": #FFC9D6,
  "st-new": #8FC0FF,
  "st-open": #6EE29A,
  "st-pending": #F6C12B,
  "st-solved": #86DCEB,
  "st-closed": #AAB6D0,
  "st-spam": #FF9AA0,
  "bm-plate": #1A2548,
  "bm-edge": #6F84B8,
  "bm-bar": #0A1430,
  "bm-on-bar": #EFDDBB,
  "bm-text": #F1E6CC,
  "bm-text2": #B7C2DE,
  "bm-crt": #6FB4F2,
  "bm-on-crt": #06152E,
  "bm-led": #3DDC6B,
  "bm-shadow": #05070E
);

// Portal tokens (light only in v1). Admin loads them too, for the product-branding preview.
$ts-portal: (
  "p-bg": #FFFFFF,
  "p-soft": #F5F5F7,
  "p-ink": #1B1B22,
  "p-ink2": #4A4A57,
  "p-line": #D4D4DC
);

// Fallback values for the three product-accent properties. The Portal sets the real ones at runtime on its root
// element from ProductAccent.TryDerive (never by recompiling SCSS); these keep every page legible without them.
$ts-accent-fallback: (
  "ts-accent": #1D4FA8,
  "ts-on-accent": #FFFFFF,
  "ts-accent-ink": #1D4FA8
);

@function ts-color($key, $theme: "light") {
  @if $theme == "dark" {
    @return map-get($ts-dark, $key);
  }
  @return map-get($ts-light, $key);
}

@function ts-portal-color($key) {
  @return map-get($ts-portal, $key);
}

@mixin ts-emit($tokens) {
  @each $name, $value in $tokens {
    --#{$name}: #{$value};
  }
}

// Font stacks (docs/BRAND.md section 11). The families are self-hosted (see _brand-fonts.scss); the system fallbacks
// keep text readable if a font file fails to load.
$ts-font-sans: "IBM Plex Sans", system-ui, "Segoe UI", Arial, sans-serif;
$ts-font-mono: "IBM Plex Mono", ui-monospace, Consolas, monospace;
$ts-font-serif: "Source Serif 4", Georgia, serif;
```

- [ ] **Step 5: Write the Admin Sass**

`_tokens.scss` maps tokens to Bootstrap variables before Bootstrap's variables load. Surfaces that must follow the theme at runtime use `var(--sheet)` and friends instead of a compiled color.

```scss
// Admin: maps the brand tokens (docs/BRAND.md section 12) onto Bootstrap's Sass variables.
// Imported by app.scss after Bootstrap's functions and before its variables, so every !default is overridden here.
@import "../../../assets/brand/scss/brand-tokens";

// Semantic colors. TechStrap's primary is the Admin accent; statuses reuse the Bootstrap semantic names.
$primary: ts-color("accent");
$secondary: ts-color("st-closed");
$success: ts-color("st-open");
$info: ts-color("st-new");
$warning: ts-color("st-pending");
$danger: ts-color("st-spam");
$light: ts-color("head");
$dark: ts-color("ink");

// Body and text, light then dark. Bootstrap emits its own [data-bs-theme=dark] block from the -dark variables.
$body-bg: ts-color("paper");
$body-color: ts-color("ink");
$body-emphasis-color: ts-color("ink");
$body-secondary-color: ts-color("ink-2");
$body-tertiary-color: ts-color("ink-3");
$body-secondary-bg: ts-color("head");
$body-tertiary-bg: ts-color("rail");
$border-color: ts-color("rule");
$link-color: ts-color("accent");

$body-bg-dark: ts-color("paper", "dark");
$body-color-dark: ts-color("ink", "dark");
$body-emphasis-color-dark: ts-color("ink", "dark");
$body-secondary-color-dark: ts-color("ink-2", "dark");
$body-tertiary-color-dark: ts-color("ink-3", "dark");
$body-secondary-bg-dark: ts-color("head", "dark");
$body-tertiary-bg-dark: ts-color("rail", "dark");
$border-color-dark: ts-color("rule", "dark");
$link-color-dark: ts-color("accent", "dark");

// Surfaces that must follow the theme at runtime point at the brand custom properties instead of a compiled color.
$card-bg: var(--sheet);
$card-cap-bg: var(--head);
$card-border-color: var(--rule-strong);
$input-bg: var(--sheet);
$input-border-color: var(--rule-strong);
$table-hover-bg: var(--hover);
$table-active-bg: var(--sel);
$modal-backdrop-bg: var(--scrim);
$modal-backdrop-opacity: 1;
$focus-ring-width: 3px;
$focus-ring-color: var(--focus);

// Geometry (BRAND.md section 13): square corners, hard offset shadows, never blurred.
$border-radius: 0;
$border-radius-sm: 0;
$border-radius-lg: 0;
$border-radius-xl: 0;
$border-radius-xxl: 0;
$box-shadow: 3px 3px 0 var(--shadow);
$box-shadow-sm: 2px 2px 0 var(--shadow);
$box-shadow-lg: 4px 4px 0 var(--shadow);

// Typography (BRAND.md section 11): Sans for chrome, Mono for buttons and labels, 14px dense base.
$font-family-sans-serif: $ts-font-sans;
$font-family-monospace: $ts-font-mono;
$font-size-base: .875rem;
$line-height-base: 1.45;
$btn-font-family: $ts-font-mono;
$btn-font-size: .75rem;
$btn-font-weight: 600;
```

`_color-mode.scss` redefines Bootstrap's mixin so every dark rule (Bootstrap's and ours) is emitted for an explicit `data-bs-theme="dark"` and for Auto:

```scss
// Bootstrap's color-mode mixin emits only [data-bs-theme=dark] (color-mode-type "data") or only the media query
// ("media-query"). Admin needs both: an explicit Light/Dark choice through data-bs-theme, and Auto (no attribute, or
// "auto") following prefers-color-scheme. Redefining the mixin after Bootstrap's own makes every Bootstrap dark rule,
// and the brand dark tokens in _theme.scss, work in both ways from one definition.
@mixin color-mode($mode: light, $root: false) {
  [data-bs-theme="#{$mode}"] {
    @content;
  }

  @if $mode == dark {
    @media (prefers-color-scheme: dark) {
      :root:not([data-bs-theme="light"], [data-bs-theme="dark"]) {
        @content;
      }
    }
  }
}
```

`_theme.scss` emits the tokens, base typography, the one focus ring and the button look:

```scss
// Admin theme: brand custom properties for both themes, base typography and focus. Imported after Bootstrap.

:root,
[data-bs-theme="light"] {
  color-scheme: light;
  @include ts-emit($ts-light);
  @include ts-emit($ts-portal);
  @include ts-emit($ts-accent-fallback);
  --ts-font-sans: #{inspect($ts-font-sans)};
  --ts-font-mono: #{inspect($ts-font-mono)};
  --ts-font-serif: #{inspect($ts-font-serif)};
}

// Explicit Dark, and Auto when the OS prefers dark (see _color-mode.scss).
@include color-mode(dark, true) {
  color-scheme: dark;
  @include ts-emit($ts-dark);
}

body {
  font-variant-numeric: tabular-nums;
}

// BRAND.md section 13: a 3px focus ring on every interactive element, never removed. Bootstrap's own focus shadows
// are switched off so there is exactly one ring.
:focus-visible,
.btn:focus-visible,
.btn-check:focus-visible + .btn,
.form-control:focus,
.form-select:focus,
.form-check-input:focus {
  outline: 3px solid var(--focus) !important;
  outline-offset: 1px;
  box-shadow: none !important;
}

// Buttons: Mono 12px 600 uppercase, 2px ink border, ink fill; the secondary button is the sheet-filled alternate.
.btn {
  --bs-btn-border-width: 2px;
  min-height: 34px;
  text-transform: uppercase;
  letter-spacing: .05em;
}

.btn-primary {
  --bs-btn-color: var(--paper);
  --bs-btn-bg: var(--ink);
  --bs-btn-border-color: var(--ink);
  --bs-btn-hover-color: var(--paper);
  --bs-btn-hover-bg: var(--ink-2);
  --bs-btn-hover-border-color: var(--ink-2);
  --bs-btn-active-color: var(--paper);
  --bs-btn-active-bg: var(--ink-2);
  --bs-btn-active-border-color: var(--ink-2);
  --bs-btn-disabled-color: var(--paper);
  --bs-btn-disabled-bg: var(--ink-3);
  --bs-btn-disabled-border-color: var(--ink-3);
}

.btn-secondary {
  --bs-btn-color: var(--ink);
  --bs-btn-bg: var(--sheet);
  --bs-btn-border-color: var(--ink);
  --bs-btn-hover-color: var(--ink);
  --bs-btn-hover-bg: var(--hover);
  --bs-btn-hover-border-color: var(--ink);
  --bs-btn-active-color: var(--ink);
  --bs-btn-active-bg: var(--sel);
  --bs-btn-active-border-color: var(--ink);
  --bs-btn-disabled-color: var(--ink-3);
  --bs-btn-disabled-bg: var(--sheet);
  --bs-btn-disabled-border-color: var(--ink-3);
}

// Admin's own display classes, kept to the type scale of BRAND.md section 11.
.ts-page-title {
  margin: 0;
  font: 600 1.125rem/1.3 var(--ts-font-mono);
}

.ts-label {
  font: 500 .625rem/1.3 var(--ts-font-mono);
  letter-spacing: .08em;
  text-transform: uppercase;
  color: var(--ink-2);
}

.ts-mono {
  font-family: var(--ts-font-mono);
}

.ts-serif {
  font-family: var(--ts-font-serif);
}
```

`app.scss` imports Bootstrap partial by partial, in the same order as Bootstrap's own `bootstrap.scss`, so the mixin override lands before any rule uses it:

```scss
// Token overrides first, then Bootstrap, then the theme. Compiled to wwwroot/css/app.css on every build (never committed).
// Vendor/bootstrap is restored by libman at build time (libman.json) and is not committed either.
// Bootstrap is imported partial by partial (the same list as its bootstrap.scss) so _color-mode.scss can replace the
// color-mode mixin before any rule uses it.
@import "Vendor/bootstrap/scss/functions";
@import "tokens";
@import "Vendor/bootstrap/scss/variables";
@import "Vendor/bootstrap/scss/variables-dark";
@import "Vendor/bootstrap/scss/maps";
@import "Vendor/bootstrap/scss/mixins";
@import "color-mode";
@import "Vendor/bootstrap/scss/utilities";
@import "Vendor/bootstrap/scss/root";
@import "Vendor/bootstrap/scss/reboot";
@import "Vendor/bootstrap/scss/type";
@import "Vendor/bootstrap/scss/images";
@import "Vendor/bootstrap/scss/containers";
@import "Vendor/bootstrap/scss/grid";
@import "Vendor/bootstrap/scss/tables";
@import "Vendor/bootstrap/scss/forms";
@import "Vendor/bootstrap/scss/buttons";
@import "Vendor/bootstrap/scss/transitions";
@import "Vendor/bootstrap/scss/dropdown";
@import "Vendor/bootstrap/scss/button-group";
@import "Vendor/bootstrap/scss/nav";
@import "Vendor/bootstrap/scss/navbar";
@import "Vendor/bootstrap/scss/card";
@import "Vendor/bootstrap/scss/accordion";
@import "Vendor/bootstrap/scss/breadcrumb";
@import "Vendor/bootstrap/scss/pagination";
@import "Vendor/bootstrap/scss/badge";
@import "Vendor/bootstrap/scss/alert";
@import "Vendor/bootstrap/scss/progress";
@import "Vendor/bootstrap/scss/list-group";
@import "Vendor/bootstrap/scss/close";
@import "Vendor/bootstrap/scss/toasts";
@import "Vendor/bootstrap/scss/modal";
@import "Vendor/bootstrap/scss/tooltip";
@import "Vendor/bootstrap/scss/popover";
@import "Vendor/bootstrap/scss/carousel";
@import "Vendor/bootstrap/scss/spinners";
@import "Vendor/bootstrap/scss/offcanvas";
@import "Vendor/bootstrap/scss/placeholders";
@import "Vendor/bootstrap/scss/helpers";
@import "Vendor/bootstrap/scss/utilities/api";

@import "theme";

.shell-placeholder {
  max-width: 40rem;
  margin: 4rem auto;
  padding: 0 1rem;
}
```

- [ ] **Step 6: Write the Portal Sass**

The portal turns dark mode off (`$enable-dark-mode: false`), maps only the `--p-*` tokens, and points primary controls and links at the runtime accent properties.

```scss
// Portal: maps the portal tokens (docs/BRAND.md section 12, "Portal tokens") onto Bootstrap's Sass variables.
// Imported by app.scss after Bootstrap's functions and before its variables, so every !default is overridden here.
// The portal is light only in v1 and plain: it takes none of the Admin carbon, stamp or brand-moment tokens.
@import "../../../assets/brand/scss/brand-tokens";

$enable-dark-mode: false;

// Compile-time fallback for the product accent. At runtime the product's own accent arrives as --ts-accent,
// --ts-on-accent and --ts-accent-ink (see _theme.scss) and replaces this everywhere it matters.
$primary: map-get($ts-accent-fallback, "ts-accent");

$body-bg: ts-portal-color("p-bg");
$body-color: ts-portal-color("p-ink");
$body-emphasis-color: ts-portal-color("p-ink");
$body-secondary-color: ts-portal-color("p-ink2");
$border-color: ts-portal-color("p-line");
$light: ts-portal-color("p-soft");
$link-color: map-get($ts-accent-fallback, "ts-accent-ink");

// Geometry: small radii, neutral product UI (BRAND.md section 13).
$border-radius: .25rem;
$border-radius-sm: .25rem;
$border-radius-lg: .375rem;

// Typography: Plex Sans only, 15px body, 16px inputs and buttons so mobile browsers never zoom.
$font-family-sans-serif: $ts-font-sans;
$font-family-monospace: $ts-font-mono;
$font-size-base: .9375rem;
$input-font-size: 1rem;
$btn-font-size: 1rem;
$btn-font-weight: 600;
$btn-padding-x: 1.25rem;
$btn-padding-y: .6875rem;
```

```scss
// Portal theme: portal custom properties, the product-accent hooks and base focus. Imported after Bootstrap.

:root,
[data-bs-theme="light"] {
  @include ts-emit($ts-portal);
  // Fallbacks. A product's accent is set on .ts-accent-scope (inline style) and overrides these for everything inside it.
  @include ts-emit($ts-accent-fallback);
  --ts-font-sans: #{inspect($ts-font-sans)};
  --ts-font-mono: #{inspect($ts-font-mono)};
}

// Only --ts-accent, --ts-on-accent and --ts-accent-ink may vary per product (BRAND.md section 22). Accent is for fills
// and borders; text and outlines on white use --ts-accent-ink; text on an accent fill uses --ts-on-accent.
a {
  color: var(--ts-accent-ink);
}

.btn-primary {
  --bs-btn-color: var(--ts-on-accent);
  --bs-btn-bg: var(--ts-accent);
  --bs-btn-border-color: var(--ts-accent);
  --bs-btn-hover-color: var(--ts-on-accent);
  --bs-btn-hover-bg: var(--ts-accent);
  --bs-btn-hover-border-color: var(--ts-accent);
  --bs-btn-active-color: var(--ts-on-accent);
  --bs-btn-active-bg: var(--ts-accent);
  --bs-btn-active-border-color: var(--ts-accent);
  --bs-btn-disabled-color: var(--ts-on-accent);
  --bs-btn-disabled-bg: var(--ts-accent);
  --bs-btn-disabled-border-color: var(--ts-accent);
}

.btn-outline-primary {
  --bs-btn-color: var(--ts-accent-ink);
  --bs-btn-border-color: var(--ts-accent-ink);
  --bs-btn-hover-color: #fff;
  --bs-btn-hover-bg: var(--ts-accent-ink);
  --bs-btn-hover-border-color: var(--ts-accent-ink);
  --bs-btn-active-color: #fff;
  --bs-btn-active-bg: var(--ts-accent-ink);
  --bs-btn-active-border-color: var(--ts-accent-ink);
}

.btn {
  min-height: 44px;
}

// Focus: 3px ink ring plus a 5px accent halo (BRAND.md section 18, Portal). One ring only.
:focus-visible,
.btn:focus-visible,
.form-control:focus,
.form-select:focus,
.form-check-input:focus {
  outline: 3px solid var(--p-ink) !important;
  outline-offset: 2px;
  box-shadow: 0 0 0 5px var(--ts-accent) !important;
}

.form-control,
.form-select {
  min-height: 44px;
}

.ts-accent-scope {
  display: block;
}
```

```scss
// Token overrides first, then Bootstrap, then the theme. Compiled to wwwroot/css/app.css on every build (never committed).
// Vendor/bootstrap is restored by libman at build time (libman.json) and is not committed either.
@import "Vendor/bootstrap/scss/functions";
@import "tokens";
@import "Vendor/bootstrap/scss/bootstrap";
@import "theme";

.shell-placeholder {
  max-width: 40rem;
  margin: 4rem auto;
  padding: 0 1rem;
}
```

- [ ] **Step 7: Record the contrast finding in BRAND.md**

In `docs/BRAND.md`, insert before the line `## Carbon tint tokens`:

```markdown
**Contrast exception (found in P02-T06, pinned by `TokenContrastTests`):** `--ink-3` on `--sel` in the dark theme is 4.32:1, below AA. Never place tertiary text (placeholders, "unassigned") on a selected row; use `--ink-2` there. Every other text pair in this section reaches 4.5:1 in both themes.

```

- [ ] **Step 8: Run to verify they pass**

Run: `dotnet test --project tests/TechStrap.Admin.Tests` and `dotnet test --project tests/TechStrap.Portal.Tests`
Expected: PASS (Admin `total: 126`, Portal `total: 34`; the 112 contrast cases are 56 pairs times two themes). If a Sass build error appears, run `dotnet build src/TechStrap.Admin -v n` and read the Sass message; the first build also restores Bootstrap through libman.

- [ ] **Step 9: Commit**

```bash
git add assets/brand/scss src/TechStrap.Admin/Styles src/TechStrap.Portal/Styles tests/Shared tests/TechStrap.Admin.Tests tests/TechStrap.Portal.Tests docs/BRAND.md
git commit -m "feat(ui): map brand tokens to Bootstrap in Admin and Portal" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 4: Self-hosted fonts through libman

**Files:**
- Modify: `src/TechStrap.Admin/libman.json`, `src/TechStrap.Portal/libman.json`, `docs/architecture/03-PACKAGE-MAP.md`, `.gitignore`, `.dockerignore`, `Dockerfile.admin`, `Dockerfile.portal`, `src/TechStrap.Admin/Styles/app.scss`, `src/TechStrap.Portal/Styles/app.scss`, `scripts/tests/Dockerfiles.Tests.ps1`, `tests/Shared/CompiledCss.cs`, both test csproj files
- Create: `assets/brand/scss/_brand-fonts.scss`, `src/TechStrap.Admin/Styles/_fonts.scss`, `src/TechStrap.Portal/Styles/_fonts.scss`
- Create: `tests/Shared/FontStyleRules.cs`, `tests/TechStrap.Admin.Tests/AdminFactory.cs`, `tests/TechStrap.Portal.Tests/PortalFactory.cs`
- Test: `tests/TechStrap.Admin.Tests/FontStyleTests.cs`, `tests/TechStrap.Portal.Tests/FontStyleTests.cs`, `tests/TechStrap.Admin.Tests/FontHostingTests.cs`, `tests/TechStrap.Portal.Tests/FontHostingTests.cs`

**Interfaces:**
- Consumes: `CompiledCss` (Task 3), the font stacks `$ts-font-*` (Task 3).
- Produces: `@mixin ts-font-face($family, $directory, $file, $weight)`; restored files `wwwroot/fonts/<family>/files/*.woff2` and `wwwroot/fonts/<family>/LICENSE`; `CompiledCss.FontFaces()`; test factories `AdminFactory(string environment = "Development")` and `PortalFactory(...)` (internal, `WebApplicationFactory<Program>`, DotEnv off).

**Font decision (verified in the scratch build):** libman restores the WOFF2 files from the `@fontsource` npm packages on jsdelivr, so no binary font is committed. Packages and versions: `@fontsource/ibm-plex-sans@5.3.0` (400, 500, 600; 22 to 24 KB each), `@fontsource/ibm-plex-mono@5.3.0` (400, 500, 600; about 15 KB each), `@fontsource-variable/source-serif-4@5.3.0` (`source-serif-4-latin-opsz-normal.woff2`, 122 KB, weight and optical-size axes; Admin only). Each package's `LICENSE` (the SIL OFL 1.1 text) is restored beside its files, so the license ships with the fonts, including in the published image. The restore needs jsdelivr at build time, exactly like Bootstrap already does; the Dockerfile assertion makes an offline build fail loudly. The scratch publish from a clean tree contained all seven files and three licenses in `wwwroot/fonts`.

- [ ] **Step 1: Write the failing tests**

Add `Microsoft.AspNetCore.Mvc.Testing` to both test csproj files (the version is already central):

```xml
    <PackageReference Include="bunit" />
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" />
```

Extend `tests/Shared/CompiledCss.cs` with `@font-face` parsing:

```diff
@@ -12,4 +12,10 @@ internal sealed partial class CompiledCss
     private static partial Regex RulePattern();
 
+    [GeneratedRegex(@"@font-face\{(?<body>[^}]*)\}")]
+    private static partial Regex FontFacePattern();
+
+    [GeneratedRegex(@"(?<name>[a-z-]+):\s*(?<value>[^;]+)(;|$)")]
+    private static partial Regex DeclarationPattern();
+
     private readonly List<(string Selector, string Body)> _rules;
 
@@ -32,4 +38,11 @@ internal sealed partial class CompiledCss
     }
 
+    /// <summary>Every @font-face block as a name to value map (font-family unquoted).</summary>
+    public IReadOnlyList<IReadOnlyDictionary<string, string>> FontFaces() =>
+        FontFacePattern().Matches(Text)
+            .Select(face => (IReadOnlyDictionary<string, string>)DeclarationPattern().Matches(face.Groups["body"].Value)
+                .ToDictionary(d => d.Groups["name"].Value, d => d.Groups["value"].Value.Trim().Trim('"')))
+            .ToList();
+
     /// <summary>Every declaration of every rule whose selector is exactly <paramref name="selector"/> (Bootstrap and brand rules merged, last wins), colors normalized.</summary>
     public IReadOnlyDictionary<string, string> Declarations(string selector)
```

Create the shared rules and the per-app tests:

```csharp
using System.Text.RegularExpressions;

namespace TechStrap.Tests.Shared;

/// <summary>
/// The font rules of docs/BRAND.md section 11 that both apps share: self-hosted, swap, and a system fallback at the end of
/// every stack so text stays readable when a font file fails to load. Each app passes the families it ships.
/// </summary>
internal static partial class FontStyleRules
{
    [GeneratedRegex(@"url\(""?(?<path>[^)""]+)""?\)")]
    private static partial Regex UrlPattern();

    public static void EveryFontFaceIsSelfHostedAndSwaps(CompiledCss css, string app, params string[] expectedFamilies)
    {
        var faces = css.FontFaces();

        faces.Select(f => f["font-family"]).Distinct().Order().ShouldBe(expectedFamilies.Order());
        var cssDirectory = RepositoryRoot.Combine("src", app, "wwwroot", "css");
        foreach (var face in faces)
        {
            face["font-display"].ShouldBe("swap", face["font-family"]);
            var path = UrlPattern().Match(face["src"]).Groups["path"].Value;
            path.ShouldStartWith("../fonts/", Case.Sensitive, $"{face["font-family"]} must be self-hosted");
            File.Exists(Path.GetFullPath(Path.Combine(cssDirectory, path))).ShouldBeTrue($"{path} should be restored by libman");
        }
    }

    /// <summary>Every url() in the stylesheet is an inline data: URI (Bootstrap's SVG icons) or one of our own ../fonts files.</summary>
    public static void NoCssReferencesAThirdPartyHost(CompiledCss css)
    {
        foreach (Match url in UrlPattern().Matches(css.Text))
        {
            var path = url.Groups["path"].Value;
            (path.StartsWith("data:", StringComparison.Ordinal) || path.StartsWith("../fonts/", StringComparison.Ordinal))
                .ShouldBeTrue($"url({path}) must not leave the app");
        }

        css.Text.ShouldNotContain("@import url", Case.Insensitive);
        css.Text.ShouldNotContain("googleapis", Case.Insensitive);
        css.Text.ShouldNotContain("gstatic", Case.Insensitive);
    }

    public static void LicencesShipBesideTheFonts(string app, params string[] directories)
    {
        foreach (var directory in directories)
        {
            var licence = RepositoryRoot.Combine("src", app, "wwwroot", "fonts", directory, "LICENSE");
            File.Exists(licence).ShouldBeTrue($"{licence} should be restored by libman");
            File.ReadAllText(licence).ShouldContain("SIL Open Font License");
        }
    }
}
```

```csharp
using TechStrap.Tests.Shared;

namespace TechStrap.Admin.Tests;

/// <summary>Fonts are self-hosted (BRAND.md section 11): no CDN, font-display swap, and a system fallback in every stack.</summary>
public sealed class FontStyleTests
{
    private static readonly CompiledCss Css = CompiledCss.Load("TechStrap.Admin");

    [Fact]
    public void Admin_ships_Plex_Sans_Plex_Mono_and_Source_Serif_4_from_its_own_wwwroot()
    {
        FontStyleRules.EveryFontFaceIsSelfHostedAndSwaps(Css, "TechStrap.Admin", "IBM Plex Mono", "IBM Plex Sans", "Source Serif 4");
    }

    [Fact]
    public void Every_font_stack_ends_with_a_system_fallback()
    {
        var root = Css.Declarations(":root,[data-bs-theme=light]");

        root["--ts-font-sans"].ShouldStartWith("\"IBM Plex Sans\"");
        root["--ts-font-sans"].ShouldEndWith("sans-serif");
        root["--ts-font-mono"].ShouldStartWith("\"IBM Plex Mono\"");
        root["--ts-font-mono"].ShouldEndWith("monospace");
        root["--ts-font-serif"].ShouldStartWith("\"Source Serif 4\"");
        root["--ts-font-serif"].ShouldEndWith("serif");
        root["--bs-font-sans-serif"].ShouldStartWith("\"IBM Plex Sans\"");
    }

    [Fact]
    public void The_stylesheet_calls_no_third_party_host()
    {
        FontStyleRules.NoCssReferencesAThirdPartyHost(Css);
    }

    [Fact]
    public void The_OFL_licence_text_ships_beside_every_family()
    {
        FontStyleRules.LicencesShipBesideTheFonts("TechStrap.Admin", "ibm-plex-sans", "ibm-plex-mono", "source-serif-4");
    }
}
```

```csharp
using TechStrap.Tests.Shared;

namespace TechStrap.Portal.Tests;

/// <summary>Fonts are self-hosted (BRAND.md section 11): no CDN, font-display swap, and a system fallback in every stack. The portal uses Plex Sans, plus Plex Mono for the ticket id only.</summary>
public sealed class FontStyleTests
{
    private static readonly CompiledCss Css = CompiledCss.Load("TechStrap.Portal");

    [Fact]
    public void Portal_ships_only_Plex_Sans_and_Plex_Mono_from_its_own_wwwroot()
    {
        FontStyleRules.EveryFontFaceIsSelfHostedAndSwaps(Css, "TechStrap.Portal", "IBM Plex Mono", "IBM Plex Sans");
    }

    [Fact]
    public void Every_font_stack_ends_with_a_system_fallback()
    {
        var root = Css.Declarations(":root,[data-bs-theme=light]");

        root["--ts-font-sans"].ShouldStartWith("\"IBM Plex Sans\"");
        root["--ts-font-sans"].ShouldEndWith("sans-serif");
        root["--ts-font-mono"].ShouldStartWith("\"IBM Plex Mono\"");
        root["--ts-font-mono"].ShouldEndWith("monospace");
        root["--bs-font-sans-serif"].ShouldStartWith("\"IBM Plex Sans\"");
    }

    [Fact]
    public void The_stylesheet_calls_no_third_party_host()
    {
        FontStyleRules.NoCssReferencesAThirdPartyHost(Css);
    }

    [Fact]
    public void The_OFL_licence_text_ships_beside_every_family()
    {
        FontStyleRules.LicencesShipBesideTheFonts("TechStrap.Portal", "ibm-plex-sans", "ibm-plex-mono");
    }
}
```

Create the test factories (the host tests of Tasks 6 and 11 reuse them):

```csharp
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TechStrap.Admin.Tests;

/// <summary>Starts the Admin host in-process in the given environment. A developer's gitignored .env.local must never leak into tests.</summary>
internal sealed class AdminFactory(string environment = "Development") : WebApplicationFactory<TechStrap.Admin.Program>
{
    static AdminFactory()
    {
        Environment.SetEnvironmentVariable("DotEnv__Enabled", "false");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
    }
}
```

```csharp
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TechStrap.Portal.Tests;

/// <summary>Starts the Portal host in-process in the given environment. A developer's gitignored .env.local must never leak into tests.</summary>
internal sealed class PortalFactory(string environment = "Development") : WebApplicationFactory<TechStrap.Portal.Program>
{
    static PortalFactory()
    {
        Environment.SetEnvironmentVariable("DotEnv__Enabled", "false");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
    }
}
```

```csharp
using System.Net;

namespace TechStrap.Admin.Tests;

/// <summary>The libman-restored fonts must be served by the running host, not only exist on disk.</summary>
public sealed class FontHostingTests
{
    public static TheoryData<string> FontFiles() =>
    [
        "ibm-plex-sans/files/ibm-plex-sans-latin-400-normal.woff2",
        "ibm-plex-sans/files/ibm-plex-sans-latin-500-normal.woff2",
        "ibm-plex-sans/files/ibm-plex-sans-latin-600-normal.woff2",
        "ibm-plex-mono/files/ibm-plex-mono-latin-400-normal.woff2",
        "ibm-plex-mono/files/ibm-plex-mono-latin-500-normal.woff2",
        "ibm-plex-mono/files/ibm-plex-mono-latin-600-normal.woff2",
        "source-serif-4/files/source-serif-4-latin-opsz-normal.woff2",
    ];

    [Theory]
    [MemberData(nameof(FontFiles))]
    public async Task Font_file_is_served_as_woff2(string relativePath)
    {
        await using var factory = new AdminFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/fonts/" + relativePath, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, relativePath);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("font/woff2");
    }
}
```

```csharp
using System.Net;

namespace TechStrap.Portal.Tests;

/// <summary>The libman-restored fonts must be served by the running host, not only exist on disk.</summary>
public sealed class FontHostingTests
{
    public static TheoryData<string> FontFiles() =>
    [
        "ibm-plex-sans/files/ibm-plex-sans-latin-400-normal.woff2",
        "ibm-plex-sans/files/ibm-plex-sans-latin-500-normal.woff2",
        "ibm-plex-sans/files/ibm-plex-sans-latin-600-normal.woff2",
        "ibm-plex-mono/files/ibm-plex-mono-latin-400-normal.woff2",
        "ibm-plex-mono/files/ibm-plex-mono-latin-500-normal.woff2",
        "ibm-plex-mono/files/ibm-plex-mono-latin-600-normal.woff2",
    ];

    [Theory]
    [MemberData(nameof(FontFiles))]
    public async Task Font_file_is_served_as_woff2(string relativePath)
    {
        await using var factory = new PortalFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/fonts/" + relativePath, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, relativePath);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("font/woff2");
    }
}
```

Extend `scripts/tests/Dockerfiles.Tests.ps1`:

```diff
@@ -59,4 +59,23 @@ Describe 'compiled CSS assertion' {
 }
 
+Describe 'self-hosted font assertion' {
+    # Fonts are restored by libman (jsdelivr) during publish and never committed, so an image built without network
+    # access must fail the build instead of shipping without fonts. Admin: Sans 3 + Mono 3 + Serif 1 files, 3 licenses.
+    It 'Dockerfile.<Name> fails the build unless <Fonts> WOFF2 files and <Licences> OFL licences are published' -ForEach @(
+        @{ Name = 'admin'; Fonts = 7; Licences = 3 }
+        @{ Name = 'portal'; Fonts = 6; Licences = 2 }
+    ) {
+        $text = Get-DockerfileText -Name $Name
+        $fontCheck = 'test "$(find /app/publish/wwwroot/fonts -name ''*.woff2'' | wc -l)" -eq ' + $Fonts
+        $licenceCheck = 'test "$(find /app/publish/wwwroot/fonts -name LICENSE | wc -l)" -eq ' + $Licences
+        $text.Contains($fontCheck) | Should -BeTrue -Because "Dockerfile.$Name must contain: $fontCheck"
+        $text.Contains($licenceCheck) | Should -BeTrue -Because "Dockerfile.$Name must contain: $licenceCheck"
+    }
+
+    It 'Dockerfile.api and Dockerfile.worker have no font assertion because they serve no static assets' -ForEach 'api', 'worker' {
+        (Get-DockerfileText -Name $_) | Should -Not -Match 'wwwroot/fonts'
+    }
+}
+
 Describe '.dockerignore' {
     It 'keeps secrets, git history and compiled CSS out of the build context' {
@@ -65,6 +84,17 @@ Describe '.dockerignore' {
         $lines | Should -Contain '**/.env'
         $lines | Should -Contain '**/wwwroot/css/app.css'
+        $lines | Should -Contain '**/wwwroot/fonts/'
+        $lines | Should -Contain '**/Styles/Vendor/'
         $lines | Should -Contain '**/bin/'
         $lines | Should -Contain '**/obj/'
     }
 }
+
+Describe '.dockerignore and the shared brand SCSS' {
+    It 'excludes assets/ but re-includes assets/brand/scss/, which Admin and Portal import at build' {
+        $lines = Get-Content -LiteralPath (Join-Path $script:RepoRoot '.dockerignore')
+        $lines | Should -Contain 'assets/'
+        $lines | Should -Contain '!assets/brand/scss/'
+        [array]::IndexOf($lines, '!assets/brand/scss/') | Should -BeGreaterThan ([array]::IndexOf($lines, 'assets/'))
+    }
+}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test --project tests/TechStrap.Admin.Tests --filter-class "*Font*"` and `pwsh -NoProfile -File scripts/Invoke-ScriptTests.ps1 -Path scripts/tests/Dockerfiles.Tests.ps1 -Output Minimal`
Expected: FAIL. `FontStyleTests` fails on the missing `@font-face` blocks, `FontHostingTests` returns 404 for every font, and Pester reports 3 failures (`Dockerfile.admin`, `Dockerfile.portal` font assertion and the `.dockerignore` re-include).

- [ ] **Step 3: Restore the fonts with libman and keep them out of git**

Replace `src/TechStrap.Admin/libman.json`:

```json
{
  "version": "1.0",
  "defaultProvider": "jsdelivr",
  "libraries": [
    {
      "library": "bootstrap@5.3.8",
      "destination": "Styles/Vendor/bootstrap",
      "files": [
        "scss/**"
      ]
    },
    {
      "library": "@fontsource/ibm-plex-sans@5.3.0",
      "destination": "wwwroot/fonts/ibm-plex-sans",
      "files": [
        "files/ibm-plex-sans-latin-400-normal.woff2",
        "files/ibm-plex-sans-latin-500-normal.woff2",
        "files/ibm-plex-sans-latin-600-normal.woff2",
        "LICENSE"
      ]
    },
    {
      "library": "@fontsource/ibm-plex-mono@5.3.0",
      "destination": "wwwroot/fonts/ibm-plex-mono",
      "files": [
        "files/ibm-plex-mono-latin-400-normal.woff2",
        "files/ibm-plex-mono-latin-500-normal.woff2",
        "files/ibm-plex-mono-latin-600-normal.woff2",
        "LICENSE"
      ]
    },
    {
      "library": "@fontsource-variable/source-serif-4@5.3.0",
      "destination": "wwwroot/fonts/source-serif-4",
      "files": [
        "files/source-serif-4-latin-opsz-normal.woff2",
        "LICENSE"
      ]
    }
  ]
}
```

Replace `src/TechStrap.Portal/libman.json` (no Source Serif: the portal does not use it in v1):

```json
{
  "version": "1.0",
  "defaultProvider": "jsdelivr",
  "libraries": [
    {
      "library": "bootstrap@5.3.8",
      "destination": "Styles/Vendor/bootstrap",
      "files": [
        "scss/**"
      ]
    },
    {
      "library": "@fontsource/ibm-plex-sans@5.3.0",
      "destination": "wwwroot/fonts/ibm-plex-sans",
      "files": [
        "files/ibm-plex-sans-latin-400-normal.woff2",
        "files/ibm-plex-sans-latin-500-normal.woff2",
        "files/ibm-plex-sans-latin-600-normal.woff2",
        "LICENSE"
      ]
    },
    {
      "library": "@fontsource/ibm-plex-mono@5.3.0",
      "destination": "wwwroot/fonts/ibm-plex-mono",
      "files": [
        "files/ibm-plex-mono-latin-400-normal.woff2",
        "files/ibm-plex-mono-latin-500-normal.woff2",
        "files/ibm-plex-mono-latin-600-normal.woff2",
        "LICENSE"
      ]
    }
  ]
}
```

Append three rows to the table of section 4b in `docs/architecture/03-PACKAGE-MAP.md`, and extend the paragraph above it as shown:

```diff
@@ -97,9 +97,12 @@ Versions of these are pinned by the owning phase when it starts; they were not v
 ## 4b. Front-end libraries restored at build by libman (npm via jsdelivr)
 
-These are npm packages, not NuGet packages, so `Directory.Packages.props` and `scripts/Check-PackageVersions.ps1` do not cover them. `scripts/tests/Libman.Tests.ps1` asserts that every `libman.json` library is pinned exactly, listed here with the same version, and restored only into a gitignored folder. Restored files are never committed.
+These are npm packages, not NuGet packages, so `Directory.Packages.props` and `scripts/Check-PackageVersions.ps1` do not cover them. `scripts/tests/Libman.Tests.ps1` asserts that every `libman.json` library is pinned exactly, listed here with the same version, and restored only into a gitignored folder. Restored files are never committed, and the Docker build restores them again from jsdelivr (a build without network access fails loudly at the font assertion in `Dockerfile.admin` and `Dockerfile.portal`, never silently without fonts).
 
 | Library | Used for | Exact version | Source/release verified | Owning phase |
 | --- | --- | --- | --- | --- |
 | `bootstrap` | SCSS base for Admin and Portal (`Styles/Vendor/bootstrap`, `scss/**` only) | 5.3.8 | [5.3.8](https://www.npmjs.com/package/bootstrap/v/5.3.8) via jsdelivr, restored in both apps by `Microsoft.Web.LibraryManager.Build` | P02 |
+| `@fontsource/ibm-plex-sans` | IBM Plex Sans, weights 400, 500, 600, Latin subset, WOFF2 (`wwwroot/fonts/ibm-plex-sans`), self-hosted in Admin and Portal. SIL OFL 1.1; the package `LICENSE` is restored beside the files | 5.3.0 | [5.3.0](https://www.npmjs.com/package/@fontsource/ibm-plex-sans/v/5.3.0) via jsdelivr | P02 |
+| `@fontsource/ibm-plex-mono` | IBM Plex Mono, weights 400, 500, 600, Latin subset, WOFF2 (`wwwroot/fonts/ibm-plex-mono`), Admin and Portal (ticket id). SIL OFL 1.1; `LICENSE` restored beside the files | 5.3.0 | [5.3.0](https://www.npmjs.com/package/@fontsource/ibm-plex-mono/v/5.3.0) via jsdelivr | P02 |
+| `@fontsource-variable/source-serif-4` | Source Serif 4, variable weight and optical size (`opsz`), Latin subset, one WOFF2 (`wwwroot/fonts/source-serif-4`), Admin only (message bodies, brand-moment copy). SIL OFL 1.1; `LICENSE` restored beside the file | 5.3.0 | [5.3.0](https://www.npmjs.com/package/@fontsource-variable/source-serif-4/v/5.3.0) via jsdelivr | P02 |
 
 ## 5. Published by TechStrap
```

`.gitignore` and `.dockerignore` (the `!assets/brand/scss/` line keeps the shared Sass inside the Docker build context):

```diff
@@ -41,4 +41,7 @@ src/*/wwwroot/css/app.css.map
 src/*/Styles/Vendor/
 
+# Self-hosted fonts (IBM Plex, Source Serif 4) restored by libman at build (never committed)
+src/*/wwwroot/fonts/
+
 # User-specific files (CodeRush)
 .cr/
```

```diff
@@ -8,4 +8,5 @@
 docs/
 assets/
+!assets/brand/scss/
 scripts/
 local/
@@ -25,2 +26,3 @@ publish/
 tests/
 **/Styles/Vendor/
+**/wwwroot/fonts/
```

- [ ] **Step 4: Write the `@font-face` Sass and the Dockerfile assertions**

```scss
// Self-hosted @font-face (docs/BRAND.md section 11). The WOFF2 files are restored by libman from the @fontsource npm
// packages into wwwroot/fonts (see each app's libman.json) and are never committed; the url() is relative to
// wwwroot/css/app.css. font-display: swap shows the system fallback of the stack immediately, so a missing or slow file
// never leaves text invisible.
@mixin ts-font-face($family, $directory, $file, $weight) {
  @font-face {
    font-family: $family;
    font-style: normal;
    font-weight: $weight;
    font-display: swap;
    src: url("../fonts/#{$directory}/files/#{$file}.woff2") format("woff2");
  }
}
```

```scss
@import "../../../assets/brand/scss/brand-fonts";

@include ts-font-face("IBM Plex Sans", "ibm-plex-sans", "ibm-plex-sans-latin-400-normal", 400);
@include ts-font-face("IBM Plex Sans", "ibm-plex-sans", "ibm-plex-sans-latin-500-normal", 500);
@include ts-font-face("IBM Plex Sans", "ibm-plex-sans", "ibm-plex-sans-latin-600-normal", 600);
@include ts-font-face("IBM Plex Mono", "ibm-plex-mono", "ibm-plex-mono-latin-400-normal", 400);
@include ts-font-face("IBM Plex Mono", "ibm-plex-mono", "ibm-plex-mono-latin-500-normal", 500);
@include ts-font-face("IBM Plex Mono", "ibm-plex-mono", "ibm-plex-mono-latin-600-normal", 600);
// One variable file covers weights 400 and 600 and the optical-size axis (opsz 8 to 60).
@include ts-font-face("Source Serif 4", "source-serif-4", "source-serif-4-latin-opsz-normal", 200 900);
```

```scss
@import "../../../assets/brand/scss/brand-fonts";

// Plex Sans is the portal's only text face; Plex Mono is loaded for the ticket id.
@include ts-font-face("IBM Plex Sans", "ibm-plex-sans", "ibm-plex-sans-latin-400-normal", 400);
@include ts-font-face("IBM Plex Sans", "ibm-plex-sans", "ibm-plex-sans-latin-500-normal", 500);
@include ts-font-face("IBM Plex Sans", "ibm-plex-sans", "ibm-plex-sans-latin-600-normal", 600);
@include ts-font-face("IBM Plex Mono", "ibm-plex-mono", "ibm-plex-mono-latin-400-normal", 400);
@include ts-font-face("IBM Plex Mono", "ibm-plex-mono", "ibm-plex-mono-latin-500-normal", 500);
@include ts-font-face("IBM Plex Mono", "ibm-plex-mono", "ibm-plex-mono-latin-600-normal", 600);
```

Add `@import "fonts";` after `@import "theme";` in both `app.scss` files. Then add the font assertion after the existing CSS assertion in both Dockerfiles (Admin expects 7 WOFF2 files and 3 licenses, Portal 6 and 2):

```diff
@@ -17,4 +17,7 @@ RUN --mount=type=cache,id=techstrap-nuget,target=/root/.nuget/packages \
 # Compiled CSS is generated by the build and never committed; fail the image if it is missing.
 RUN test -f /app/publish/wwwroot/css/app.css
+# Fonts are restored by libman (jsdelivr) during publish and never committed; an image built without network access must fail here, not ship without fonts.
+RUN test "$(find /app/publish/wwwroot/fonts -name '*.woff2' | wc -l)" -eq 7 \
+    && test "$(find /app/publish/wwwroot/fonts -name LICENSE | wc -l)" -eq 3
 
 FROM mcr.microsoft.com/dotnet/aspnet:10.0
```

```diff
@@ -17,4 +17,7 @@ RUN --mount=type=cache,id=techstrap-nuget,target=/root/.nuget/packages \
 # Compiled CSS is generated by the build and never committed; fail the image if it is missing.
 RUN test -f /app/publish/wwwroot/css/app.css
+# Fonts are restored by libman (jsdelivr) during publish and never committed; an image built without network access must fail here, not ship without fonts.
+RUN test "$(find /app/publish/wwwroot/fonts -name '*.woff2' | wc -l)" -eq 6 \
+    && test "$(find /app/publish/wwwroot/fonts -name LICENSE | wc -l)" -eq 2
 
 FROM mcr.microsoft.com/dotnet/aspnet:10.0
```

- [ ] **Step 5: Run to verify everything passes**

Run: `dotnet test --project tests/TechStrap.Admin.Tests`, `dotnet test --project tests/TechStrap.Portal.Tests`, `pwsh -NoProfile -File scripts/Invoke-ScriptTests.ps1 -Output Minimal`
Expected: PASS (Admin `total: 137`, Portal `total: 44`, Pester `Tests Passed: 77`).

Then prove the Docker side. Run: `docker build -f Dockerfile.admin -t techstrap-admin:scratch .`
Expected: the two `RUN test ...` steps complete in under a second and the build ends with `naming to docker.io/library/techstrap-admin:scratch done`. (Without the `.dockerignore` re-include the Sass step fails with `Can't find stylesheet to import`.)

- [ ] **Step 6: Commit**

```bash
git add src/TechStrap.Admin src/TechStrap.Portal assets/brand/scss tests scripts/tests docs/architecture/03-PACKAGE-MAP.md .gitignore .dockerignore Dockerfile.admin Dockerfile.portal
git commit -m "feat(ui): self-host IBM Plex and Source Serif 4 through libman" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 5: Logo SVGs and wordmark

**Files:**
- Modify: `scripts/brand/generate-brand-assets.py`, `assets/brand/README.md`, `docs/BRAND.md`
- Create (generated): `assets/brand/mark.svg`, `assets/brand/logo.svg`, `assets/brand/wordmark.svg`, `src/TechStrap.Admin/wwwroot/brand/{mark,logo,wordmark}.svg`, `src/TechStrap.Portal/wwwroot/brand/mark.svg`
- Test: `scripts/tests/BrandAssets.Tests.ps1`

**Interfaces:**
- Consumes: the restored `src/TechStrap.Admin/wwwroot/fonts/ibm-plex-mono/files/ibm-plex-mono-latin-600-normal.woff2` (Task 4; run `dotnet build src/TechStrap.Admin` first).
- Produces: `brand/mark.svg`, `brand/logo.svg`, `brand/wordmark.svg` URLs in the Admin app and `brand/mark.svg` in the Portal; size budgets 50 KB, 120 KB, 12 KB.

**Tooling decision (verified):** `pip install vtracer` (0.6.15) installs a prebuilt wheel on Windows and works. Settings `color_precision=5, filter_speckle=10, layer_difference=28, path_precision=1` trace the head mark to 31 KB and the full mascot to 79 KB, and a headless-Edge render of both is visually faithful to the PNG. The wordmark is outlined text (fonttools reads the WOFF2 with brotli; the 9 glyphs become one 3 KB path with `fill="currentColor"`), chosen over `<text>` because an SVG loaded through `<img>` cannot load web fonts. The SVGs are marked provisional.

- [ ] **Step 1: Write the failing test**

```powershell
BeforeDiscovery {
    # File, size budget in bytes. Budgets are about 1.5x the traced size so a re-trace that explodes the file fails.
    $script:Svgs = @(
        @{ Name = 'mark.svg'; Budget = 50KB }
        @{ Name = 'logo.svg'; Budget = 120KB }
        @{ Name = 'wordmark.svg'; Budget = 12KB }
    )
}

BeforeAll {
    $script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
    $script:BrandDir = Join-Path $script:RepoRoot 'assets/brand'
}

Describe 'brand SVG <Name>' -ForEach $script:Svgs {
    BeforeAll {
        $script:Path = Join-Path $script:BrandDir $Name
        $script:Budget = $Budget
    }

    It 'exists and is under its size budget' {
        $script:Path | Should -Exist
        (Get-Item -LiteralPath $script:Path).Length | Should -BeLessThan $script:Budget
    }

    It 'is well-formed XML with an svg root and a four-number viewBox' {
        $xml = [xml](Get-Content -LiteralPath $script:Path -Raw)
        $xml.DocumentElement.LocalName | Should -Be 'svg'
        $parts = @($xml.DocumentElement.GetAttribute('viewBox') -split '[ ,]+' | Where-Object { $_ })
        $parts.Count | Should -Be 4
        foreach ($part in $parts) { { [double]$part } | Should -Not -Throw }
        [double]$parts[2] | Should -BeGreaterThan 0
        [double]$parts[3] | Should -BeGreaterThan 0
    }

    It 'carries no script, external reference or embedded raster' {
        $text = Get-Content -LiteralPath $script:Path -Raw
        $text | Should -Not -Match '<script'
        $text | Should -Not -Match 'href\s*='
        $text | Should -Not -Match '<image'
        $text | Should -Not -Match 'data:image'
        $text | Should -Not -Match 'onload|onclick'
    }

    It 'is marked provisional in assets/brand/README.md' {
        (Get-Content -LiteralPath (Join-Path $script:BrandDir 'README.md') -Raw) | Should -Match ('(?s)' + [regex]::Escape($Name) + '.*provisional')
    }
}

Describe 'brand SVG copies in the apps' {
    It '<App> wwwroot/brand/<File> is byte-identical to assets/brand/<File>' -ForEach @(
        @{ App = 'TechStrap.Admin'; File = 'mark.svg' }
        @{ App = 'TechStrap.Admin'; File = 'logo.svg' }
        @{ App = 'TechStrap.Admin'; File = 'wordmark.svg' }
        @{ App = 'TechStrap.Portal'; File = 'mark.svg' }
    ) {
        $copy = Join-Path $script:RepoRoot "src/$App/wwwroot/brand/$File"
        $copy | Should -Exist
        (Get-FileHash -LiteralPath $copy).Hash | Should -Be (Get-FileHash -LiteralPath (Join-Path $script:BrandDir $File)).Hash
    }

    It 'the Portal carries only the head mark (the 16px Powered-by mark), never the logo or wordmark' {
        Get-ChildItem -LiteralPath (Join-Path $script:RepoRoot 'src/TechStrap.Portal/wwwroot/brand') -File | ForEach-Object Name | Should -Be @('mark.svg')
    }
}

Describe 'wordmark' {
    It 'is outlined text filled with currentColor, so it needs no font at runtime and follows the theme' {
        $text = Get-Content -LiteralPath (Join-Path $script:BrandDir 'wordmark.svg') -Raw
        $text | Should -Not -Match '<text'
        $text | Should -Not -Match 'font-family'
        $text | Should -Match 'fill="currentColor"'
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `pwsh -NoProfile -File scripts/Invoke-ScriptTests.ps1 -Path scripts/tests/BrandAssets.Tests.ps1 -Output Minimal`
Expected: FAIL, `Tests Passed: 0, Failed: 18`.

- [ ] **Step 3: Extend the generator**

Install the tools once: `pip install pillow numpy vtracer fonttools brotli`, and run `dotnet build src/TechStrap.Admin` so the Plex Mono file exists. Replace `scripts/brand/generate-brand-assets.py` (the PNG pipeline is unchanged; the tracing, wordmark and app copies are new):

```python
"""Generate TechStrap brand assets (logo, mark, favicons, app icons, SVGs) from the source artwork.

Usage:  python scripts/brand/generate-brand-assets.py
Needs:  pip install pillow numpy vtracer fonttools brotli
        and the restored IBM Plex Mono font (run `dotnet build src/TechStrap.Admin` once; libman restores it)

Input:  assets/brand/source/logo-source.png (1254x1254 RGB on white)
Output: assets/brand/*.png, assets/brand/favicon.ico,
        assets/brand/{mark,logo,wordmark}.svg (provisional auto-traces, see the README),
        and byte-identical copies in src/TechStrap.Admin/wwwroot/brand and src/TechStrap.Portal/wwwroot/brand

Re-run after replacing the source artwork. If the artwork changes shape, re-tune HEAD_POLYGON
(source-pixel coordinates outlining the monitor head, excluding hands, arms and body).
"""

import re
import shutil
import tempfile
from pathlib import Path

import numpy as np
import vtracer
from fontTools.pens.boundsPen import BoundsPen
from fontTools.pens.svgPathPen import SVGPathPen
from fontTools.pens.transformPen import TransformPen
from fontTools.ttLib import TTFont
from PIL import Image, ImageChops, ImageDraw, ImageFilter

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "assets" / "brand" / "source" / "logo-source.png"
OUT = ROOT / "assets" / "brand"

# Background removal: near-white pixels connected to the image border become transparent.
BACKGROUND_MIN_CHANNEL = 228
FRINGE_RADIUS_FILTER = 5          # MaxFilter size used to find anti-aliased edge pixels
FRINGE_OPAQUE_AT = 60             # min channel value treated as fully opaque on the fringe
FLOOD_SEED_STEP = 7

# Monitor-head outline in source coordinates (measured against the bezel and side-box outlines).
HEAD_POLYGON = [
    (300, 25), (1120, 25), (1120, 492), (1000, 538), (940, 559),
    (892, 590), (360, 538), (330, 533), (300, 526),
]

# vtracer settings tuned so the head mark traces to about 30 KB and the full mascot to about 80 KB.
TRACE_OPTIONS = dict(
    colormode="color",
    hierarchical="stacked",
    mode="spline",
    filter_speckle=10,
    color_precision=5,
    layer_difference=28,
    corner_threshold=60,
    length_threshold=5.0,
    path_precision=1,
)

# The wordmark is the text "TechStrap" in IBM Plex Mono SemiBold, converted to outlines so it needs no font at runtime.
WORDMARK_TEXT = "TechStrap"
WORDMARK_FONT = ROOT / "src" / "TechStrap.Admin" / "wwwroot" / "fonts" / "ibm-plex-mono" / "files" / "ibm-plex-mono-latin-600-normal.woff2"

# Copies served by the apps (the apps cannot read assets/). The Portal gets only the 16px Powered-by head mark.
APP_COPIES = {
    ROOT / "src" / "TechStrap.Admin" / "wwwroot" / "brand": ["mark.svg", "logo.svg", "wordmark.svg"],
    ROOT / "src" / "TechStrap.Portal" / "wwwroot" / "brand": ["mark.svg"],
}

MARK_PADDING = 16
LOGO_PADDING = 24
ICO_SIZES = [16, 32, 48, 64]
APPLE_TOUCH_SIZE = 180
APPLE_TOUCH_BACKGROUND = (255, 255, 255, 255)
APPLE_TOUCH_MARK_SCALE = 0.86


def remove_background(image: Image.Image) -> Image.Image:
    rgb = np.asarray(image.convert("RGB")).astype(np.float32)
    near_white = rgb.min(axis=2) >= BACKGROUND_MIN_CHANNEL

    # copy(): an array-backed image does not keep floodfill writes.
    fill = Image.fromarray((near_white * 255).astype(np.uint8)).copy()
    width, height = fill.size
    seeds = [(x, y) for x in range(0, width, FLOOD_SEED_STEP) for y in (0, height - 1)]
    seeds += [(x, y) for y in range(0, height, FLOOD_SEED_STEP) for x in (0, width - 1)]
    for seed in seeds:
        if fill.getpixel(seed) == 255:
            ImageDraw.floodfill(fill, seed, 128)
    background = np.asarray(fill) == 128

    grown = np.asarray(
        Image.fromarray((background * 255).astype(np.uint8)).filter(ImageFilter.MaxFilter(FRINGE_RADIUS_FILTER))
    ) > 0
    fringe = grown & ~background

    alpha = np.full(background.shape, 255.0, dtype=np.float32)
    alpha[background] = 0
    fringe_alpha = np.clip((255 - rgb.min(axis=2)) / (255 - FRINGE_OPAQUE_AT) * 255, 0, 255)
    alpha[fringe] = fringe_alpha[fringe]

    # Un-premultiply fringe colors against the white they were composited on.
    a = (alpha / 255.0)[..., None]
    safe = np.where(a > 0.02, a, 1)
    unmixed = np.clip((rgb - 255 * (1 - a)) / safe, 0, 255)
    colour = np.where(fringe[..., None], unmixed, rgb)

    return Image.fromarray(np.dstack([colour, alpha]).astype(np.uint8), "RGBA")


def square(image: Image.Image, padding: int) -> Image.Image:
    trimmed = image.crop(image.getbbox())
    width, height = trimmed.size
    side = max(width, height) + padding * 2
    canvas = Image.new("RGBA", (side, side), (0, 0, 0, 0))
    canvas.paste(trimmed, ((side - width) // 2, (side - height) // 2), trimmed)
    return canvas


def head_mark(logo: Image.Image) -> Image.Image:
    mask = Image.new("L", logo.size, 0)
    ImageDraw.Draw(mask).polygon(HEAD_POLYGON, fill=255)
    head = logo.copy()
    head.putalpha(ImageChops.multiply(head.getchannel("A"), mask))
    return square(head, MARK_PADDING)


def resized(image: Image.Image, size: int) -> Image.Image:
    return image.resize((size, size), Image.LANCZOS)


def trace_svg(image: Image.Image, destination: Path, title: str) -> None:
    """Auto-trace a transparent RGBA image to SVG with vtracer and make it embeddable (viewBox, no fixed size)."""
    with tempfile.TemporaryDirectory() as folder:
        source = Path(folder) / "trace-input.png"
        traced = Path(folder) / "trace-output.svg"
        image.save(source)
        vtracer.convert_image_to_svg_py(str(source), str(traced), **TRACE_OPTIONS)
        text = traced.read_text(encoding="utf-8")

    opening = re.search(r'<svg[^>]*width="(\d+)"[^>]*height="(\d+)"[^>]*>', text)
    if opening is None:
        raise RuntimeError("vtracer output has no sized <svg> element")
    width, height = opening.group(1), opening.group(2)
    body = text[opening.end():]
    destination.write_text(
        f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {width} {height}">\n<title>{title}</title>\n{body}',
        encoding="utf-8",
    )


def wordmark_svg() -> str:
    """Outline WORDMARK_TEXT from the Plex Mono font file; the fill is currentColor so the wordmark follows the theme."""
    if not WORDMARK_FONT.exists():
        raise SystemExit(f"Missing {WORDMARK_FONT}. Run `dotnet build src/TechStrap.Admin` so libman restores the fonts.")

    font = TTFont(WORDMARK_FONT)
    glyph_set = font.getGlyphSet()
    cmap = font.getBestCmap()
    advances = font["hmtx"]
    commands = []
    bounds = BoundsPen(glyph_set)
    x = 0
    for character in WORDMARK_TEXT:
        name = cmap[ord(character)]
        flip = (1, 0, 0, -1, x, 0)  # font units point up, SVG points down
        pen = SVGPathPen(glyph_set, ntos=lambda value: f"{value:.0f}")
        glyph_set[name].draw(TransformPen(pen, flip))
        glyph_set[name].draw(TransformPen(bounds, flip))
        commands.append(pen.getCommands())
        x += advances[name][0]

    left, top, right, bottom = bounds.bounds
    return (
        f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="{left:.0f} {top:.0f} {right - left:.0f} {bottom - top:.0f}">\n'
        f"<title>{WORDMARK_TEXT}</title>\n"
        f'<path fill="currentColor" d="{" ".join(commands)}"/>\n</svg>\n'
    )


def main() -> None:
    logo = remove_background(Image.open(SOURCE))
    logo_square = square(logo, LOGO_PADDING)
    mark = head_mark(logo)

    logo_square.save(OUT / "logo.png", optimize=True)
    resized(logo_square, 512).save(OUT / "logo-512.png", optimize=True)
    mark.save(OUT / "mark.png", optimize=True)
    resized(mark, 512).save(OUT / "mark-512.png", optimize=True)

    resized(mark, 16).save(OUT / "favicon-16.png", optimize=True)
    resized(mark, 32).save(OUT / "favicon-32.png", optimize=True)
    resized(mark, 256).save(OUT / "favicon.ico", sizes=[(s, s) for s in ICO_SIZES])
    resized(mark, 192).save(OUT / "icon-192.png", optimize=True)
    resized(mark, 512).save(OUT / "icon-512.png", optimize=True)

    apple = Image.new("RGBA", (APPLE_TOUCH_SIZE, APPLE_TOUCH_SIZE), APPLE_TOUCH_BACKGROUND)
    inner = int(APPLE_TOUCH_SIZE * APPLE_TOUCH_MARK_SCALE)
    offset = (APPLE_TOUCH_SIZE - inner) // 2
    apple.alpha_composite(resized(mark, inner), (offset, offset))
    apple.convert("RGB").save(OUT / "apple-touch-icon.png", optimize=True)

    trace_svg(mark, OUT / "mark.svg", "TechStrap mascot head")
    trace_svg(logo_square, OUT / "logo.svg", "TechStrap mascot")
    (OUT / "wordmark.svg").write_text(wordmark_svg(), encoding="utf-8")

    for folder, names in APP_COPIES.items():
        folder.mkdir(parents=True, exist_ok=True)
        for name in names:
            shutil.copyfile(OUT / name, folder / name)

    for path in sorted(OUT.glob("*.*")):
        print(f"{path.relative_to(ROOT)}  {path.stat().st_size:>8} bytes")


if __name__ == "__main__":
    main()
```

- [ ] **Step 4: Document the files**

`assets/brand/README.md` and BRAND.md section 15 mark the SVGs provisional:

```diff
@@ -6,5 +6,6 @@ Do not edit the generated files by hand; replace the source and re-run:
 
 ```sh
-pip install pillow numpy
+pip install pillow numpy vtracer fonttools brotli
+dotnet build src/TechStrap.Admin   # once: libman restores the IBM Plex Mono file the wordmark is outlined from
 python scripts/brand/generate-brand-assets.py
 ```
@@ -18,4 +19,8 @@ python scripts/brand/generate-brand-assets.py
 | `apple-touch-icon.png` | iOS home screen, 180 px, opaque white background |
 | `icon-192.png`, `icon-512.png` | Web app manifest icons |
+| `mark.svg`, `logo.svg` | **Provisional.** Auto-traced SVGs (vtracer) of the head mark and the full mascot, 5 color levels, about 31 KB and 79 KB. Used by the Admin layout, brand moments and style guide |
+| `wordmark.svg` | **Provisional.** The text `TechStrap` in IBM Plex Mono SemiBold, converted to outlines (no font needed at runtime), `fill="currentColor"` so it follows the theme |
+
+The SVGs are provisional until the owner supplies hand-drawn vector artwork: the traced edges are approximations of the PNG source, and colours are quantised, so they differ slightly from the brand palette in `docs/BRAND.md`. `scripts/brand/generate-brand-assets.py` also copies `mark.svg`, `logo.svg` and `wordmark.svg` into `src/TechStrap.Admin/wwwroot/brand/` and `mark.svg` into `src/TechStrap.Portal/wwwroot/brand/` (the apps cannot read `assets/`); `scripts/tests/BrandAssets.Tests.ps1` fails when a copy drifts.
 
 The favicon and app icons use the head mark because the full figure is unreadable below about 64 px.
```

```diff
@@ -384,5 +384,5 @@ This code **never changes meaning and is never reused** for decoration, status,
 
 - **Mascot:** a retro beige CRT in a jockstrap with tube socks, thumbs up. Two forms: the **head mark** (`assets/brand/mark*.png`: Admin rail at 36px, favicon, avatars, 16px portal footer, 96px inside the retro window) and the **full figure** (`assets/brand/logo*.png`: README, style guide and sign-in only; unreadable below about 64px). The 404 uses the head mark.
-- **Current format is PNG.** Auto-traced SVGs (cleaner scaling, themeable) are pending in task P02-T08; until then use the PNGs generated by `scripts/brand/generate-brand-assets.py`. Never redraw or recolour the mascot by hand.
+- **Formats.** The product uses the SVGs `assets/brand/mark.svg` (head mark) and `assets/brand/logo.svg` (full figure), auto-traced by `scripts/brand/generate-brand-assets.py` with vtracer, and `assets/brand/wordmark.svg` (the text `TechStrap` in IBM Plex Mono SemiBold, outlined, `currentColor`). **All three SVGs are provisional** (P02-T08): the traces approximate the PNG source and quantize colors, and the owner may supply hand-drawn vectors later. PNGs remain for favicons, app icons, README and social images. Never redraw or recolor the mascot by hand.
 - The mascot never appears on working screens (queue, ticket, composer, forms, settings), in the portal body, in emails, or beside an error. Product logos in the portal are the product's own.
 - No stock photography or stock illustration, no decorative blobs or hero art. Screenshots (README, docs) show the real app.
```

- [ ] **Step 5: Generate and verify**

Run: `python scripts/brand/generate-brand-assets.py` then `pwsh -NoProfile -File scripts/Invoke-ScriptTests.ps1 -Path scripts/tests/BrandAssets.Tests.ps1 -Output Minimal`
Expected: the listing shows `mark.svg 31199 bytes`, `logo.svg 78898 bytes`, `wordmark.svg 3041 bytes`; the existing PNGs and `favicon.ico` are byte-identical (`git status` shows only the new files); Pester `Tests Passed: 18`.

Look at the result: `msedge --headless --screenshot` of an HTML page that shows `assets/brand/mark.svg` next to `assets/brand/mark.png` (the scratch build did this; the two are visually indistinguishable at 400 px).

- [ ] **Step 6: Commit**

```bash
git add scripts/brand/generate-brand-assets.py scripts/tests/BrandAssets.Tests.ps1 assets/brand src/TechStrap.Admin/wwwroot/brand src/TechStrap.Portal/wwwroot/brand docs/BRAND.md
git commit -m "feat(brand): trace head mark and mascot to SVG and add the outlined wordmark" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 6: Admin shell, Development-only guard and style-guide skeleton

**Files:**
- Create: `src/TechStrap.Admin/Components/Ui/DevelopmentOnly.razor`, `DevelopmentOnly.razor.cs`
- Create: `src/TechStrap.Admin/Components/Layout/MainLayout.razor`
- Create: `src/TechStrap.Admin/Components/Pages/StyleGuide.razor`, `src/TechStrap.Admin/Components/Showcase/PaletteGroup.cs`, `PaletteSwatches.razor`, `PaletteSwatches.razor.cs`
- Create: `src/TechStrap.Admin/Styles/_shell.scss`, `_styleguide.scss`, `_feedback.scss`, `_motion.scss`
- Modify: `src/TechStrap.Admin/Components/_Imports.razor`, `Routes.razor`, `Pages/Home.razor`, `Pages/NotFound.razor`, `Styles/app.scss`, `tests/TechStrap.Admin.Tests/AdminFactory.cs`
- Test: `tests/TechStrap.Admin.Tests/Components/DevelopmentOnlyTests.cs`, `Components/MainLayoutTests.cs`, `StyleGuideEnvironmentTests.cs`, `LayoutHostTests.cs`

**Interfaces:**
- Consumes: `AdminFactory` (Task 4), `PaletteGroup`-free token names (Task 3), `brand/mark.svg` (Task 5), `SyntaxCircus.Blazor.Components.Feedback.GlobalErrorBoundary` (verified against package 0.1.3: parameters `BoundaryName`, `Title`, `Description`, `RetryLabel`, `HomeHref`, `HomeLabel`, `CssClass`, required `ChildContent`; it renders `GlobalErrorView` with the class you pass, `role="alert"`, an `h1`, and a `data-global-error-actions` div).
- Produces:
  - `DevelopmentOnly` (parameter `RenderFragment? ChildContent`): renders its content in Development only; otherwise renders nothing and calls `NavigationManager.NotFound()`.
  - `MainLayout` (`LayoutComponentBase`): `.ts-shell` grid, `.ts-rail` with `a.ts-brand > img[src="brand/mark.svg"]`, `main.ts-main` wrapping `@Body` in a `GlobalErrorBoundary` with `CssClass="ts-error"`.
  - Route `/_styleguide` (page title `TechStrap style guide`), sections labeled `sg-type`, `sg-palette`, `sg-buttons`, `sg-forms`, `sg-tables`, `sg-alerts`, `sg-states` (later tasks add `sg-stamps`, `sg-tints`, `sg-keys`, `sg-windows`, `sg-reconnect`).
  - Sass classes `.ts-page-title`, `.ts-label`, `.ts-mono`, `.ts-serif` (Task 3), `.ts-sg-*`, `.ts-error`.

**Gating decision (verified):** the style-guide page is wrapped in `DevelopmentOnly`, which calls `NavigationManager.NotFound()` outside Development. In static SSR that produces a real 404 through the router's `NotFoundPage` (the scratch test for Production and Staging passes, and the body contains none of the guide). It also covers an interactive circuit navigating to the route, which never makes an HTTP request, so no middleware is needed. `Production` refuses to start without a trusted network, so the test factory sets `TRUSTEDPROXY__TRUSTEDNETWORKS__0` as an environment variable (that option is bound before a factory can override configuration).

- [ ] **Step 1: Write the failing tests**

```csharp
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using TechStrap.Admin.Components.Ui;

namespace TechStrap.Admin.Tests.Components;

public sealed class DevelopmentOnlyTests : BunitContext
{
    private sealed class FakeEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "TechStrap.Admin";

        public string ContentRootPath { get; set; } = string.Empty;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    [Fact]
    public void Renders_its_content_in_Development()
    {
        Services.AddSingleton<IHostEnvironment>(new FakeEnvironment(Environments.Development));

        var cut = Render<DevelopmentOnly>(p => p.AddChildContent("<p id=\"inside\">style guide</p>"));

        cut.Find("#inside").TextContent.ShouldBe("style guide");
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("")]
    public void Renders_nothing_and_signals_not_found_outside_Development(string environment)
    {
        Services.AddSingleton<IHostEnvironment>(new FakeEnvironment(environment));
        var navigation = Services.GetRequiredService<NavigationManager>();
        var notFound = 0;
        navigation.OnNotFound += (_, _) => notFound++;

        var cut = Render<DevelopmentOnly>(p => p.AddChildContent("<p id=\"inside\">style guide</p>"));

        cut.FindAll("#inside").ShouldBeEmpty();
        cut.Markup.Trim().ShouldBeEmpty();
        notFound.ShouldBe(1);
    }
}
```

```csharp
using System.Net;

namespace TechStrap.Admin.Tests;

/// <summary>The style guide is a development tool: it exists in Development and is indistinguishable from any unknown page elsewhere.</summary>
public sealed class StyleGuideEnvironmentTests
{
    [Fact]
    public async Task Style_guide_returns_200_in_Development()
    {
        await using var factory = new AdminFactory("Development");
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/_styleguide", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldContain("TechStrap style guide");
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task Style_guide_returns_404_and_leaks_nothing_outside_Development(string environment)
    {
        await using var factory = new AdminFactory(environment);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/_styleguide", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        html.ShouldNotContain("TechStrap style guide");
        html.ShouldNotContain("ts-sg-");
    }
}
```

```csharp
using Bunit;
using Microsoft.AspNetCore.Components;
using TechStrap.Admin.Components.Layout;

namespace TechStrap.Admin.Tests.Components;

public sealed class MainLayoutTests : BunitContext
{
    [Fact]
    public void Header_shows_the_SVG_head_mark_and_no_mascot_copy()
    {
        var cut = Render<MainLayout>(p => p.Add(l => l.Body, b => b.AddMarkupContent(0, "<p id=\"page\">page</p>")));

        var mark = cut.Find(".ts-brand img");
        mark.GetAttribute("src").ShouldBe("brand/mark.svg");
        mark.GetAttribute("alt").ShouldBe(string.Empty);
        cut.Find(".ts-brand span").TextContent.ShouldContain("TechStrap");
    }

    [Fact]
    public void Page_content_renders_inside_main_under_an_error_boundary()
    {
        var cut = Render<MainLayout>(p => p.Add(l => l.Body, b => b.AddMarkupContent(0, "<p id=\"page\">page</p>")));

        cut.Find("main.ts-main #page").TextContent.ShouldBe("page");
        cut.Find("nav[aria-label='Admin navigation']").ShouldNotBeNull();
    }

    [Fact]
    public void A_page_that_throws_shows_the_plain_error_view_instead_of_crashing_the_shell()
    {
        RenderFragment throwing = builder =>
        {
            builder.OpenComponent<Throwing>(0);
            builder.CloseComponent();
        };

        var cut = Render<MainLayout>(p => p.Add(l => l.Body, throwing));

        var error = cut.Find("section.ts-error");
        error.QuerySelector("h1")!.TextContent.ShouldBe("Couldn't load this screen.");
        error.TextContent.ShouldNotContain("InvalidOperationException");
        cut.Find(".ts-brand").ShouldNotBeNull();
    }

    private sealed class Throwing : ComponentBase
    {
        protected override void BuildRenderTree(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder) =>
            throw new InvalidOperationException("boom");
    }
}
```

```csharp
using System.Net;

namespace TechStrap.Admin.Tests;

public sealed class LayoutHostTests
{
    [Fact]
    public async Task Home_page_uses_the_layout_with_the_head_mark_and_the_mark_is_served_as_SVG()
    {
        await using var factory = new AdminFactory();
        using var client = factory.CreateClient();

        var home = await client.GetAsync("/", TestContext.Current.CancellationToken);
        var html = await home.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        home.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain("TechStrap Admin");
        html.ShouldContain("src=\"brand/mark.svg\"");
        html.ShouldContain("ts-shell");

        var mark = await client.GetAsync("/brand/mark.svg", TestContext.Current.CancellationToken);
        mark.StatusCode.ShouldBe(HttpStatusCode.OK);
        mark.Content.Headers.ContentType?.MediaType.ShouldBe("image/svg+xml");
    }
}
```

Update the factory so Production and Staging can start:

```diff
@@ -4,5 +4,5 @@ using Microsoft.AspNetCore.Mvc.Testing;
 namespace TechStrap.Admin.Tests;
 
-/// <summary>Starts the Admin host in-process in the given environment. A developer's gitignored .env.local must never leak into tests.</summary>
+/// <summary>Starts the Admin host in-process in the given environment. A developer's gitignored .env.local must never leak into tests, and Production needs a trusted network to start.</summary>
 internal sealed class AdminFactory(string environment = "Development") : WebApplicationFactory<TechStrap.Admin.Program>
 {
@@ -10,4 +10,7 @@ internal sealed class AdminFactory(string environment = "Development") : WebAppl
     {
         Environment.SetEnvironmentVariable("DotEnv__Enabled", "false");
+
+        // Production refuses to start without trusted proxies, and that option is bound before the factory can override it.
+        Environment.SetEnvironmentVariable("TRUSTEDPROXY__TRUSTEDNETWORKS__0", "192.0.2.0/24");
     }
 
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test --project tests/TechStrap.Admin.Tests`
Expected: FAIL to compile, `error CS0234: The type or namespace name 'Ui' does not exist in the namespace 'TechStrap.Admin.Components'`.

- [ ] **Step 3: Write the guard component**

```razor
@if (_isDevelopment)
{
    @ChildContent
}
```

```csharp
using Microsoft.AspNetCore.Components;

namespace TechStrap.Admin.Components.Ui;

/// <summary>
/// Renders its content in the Development environment only. Anywhere else it renders nothing and reports the page as not
/// found, so the style guide cannot be reached, or told apart from any other unknown address, in production.
/// </summary>
public partial class DevelopmentOnly
{
    private bool _isDevelopment;

    [Inject]
    private IHostEnvironment HostEnvironment { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    protected override void OnInitialized()
    {
        _isDevelopment = HostEnvironment.IsDevelopment();
        if (!_isDevelopment)
        {
            Navigation.NotFound();
        }
    }
}
```

- [ ] **Step 4: Write the layout, routes and pages**

`_Imports.razor` adds the feedback, layout and UI namespaces; `Routes.razor` sets the default layout; the placeholder pages become `section`s so there is a single `main`:

```razor
@using Microsoft.AspNetCore.Components.Routing
@using Microsoft.AspNetCore.Components.Web
@using SyntaxCircus.Blazor.Components.Feedback
@using TechStrap.Admin
@using TechStrap.Admin.Components
@using TechStrap.Admin.Components.Layout
@using TechStrap.Admin.Components.Ui
```

```razor
@inherits LayoutComponentBase

<div class="ts-shell">
    <nav class="ts-rail" aria-label="Admin navigation">
        <a class="ts-brand" href="/">
            <img src="brand/mark.svg" alt="" width="36" height="36" />
            <span>TechStrap<small>Call log / admin</small></span>
        </a>
        <a class="ts-rail-link" href="/">Home</a>
    </nav>
    <main class="ts-main">
        <GlobalErrorBoundary BoundaryName="admin UI"
                             CssClass="ts-error"
                             Title="Couldn't load this screen."
                             Description="Something went wrong while drawing it. Try again, and tell an admin if it keeps happening."
                             HomeLabel="Back to the start">
            @Body
        </GlobalErrorBoundary>
    </main>
</div>
```

```razor
<Router AppAssembly="typeof(Program).Assembly" NotFoundPage="typeof(Pages.NotFound)">
    <Found Context="routeData">
        <RouteView RouteData="routeData" DefaultLayout="typeof(Layout.MainLayout)" />
    </Found>
</Router>
```

```razor
@page "/"

<PageTitle>TechStrap Admin</PageTitle>
<section class="shell-placeholder">
    <h1>TechStrap Admin</h1>
    <p>This host is a placeholder shell. The real interface arrives in a later phase.</p>
</section>
```

```razor
@page "/not-found"

<PageTitle>Not found</PageTitle>
<section class="shell-placeholder">
    <h1>Page not found</h1>
    <p><a href="/">Back to the start</a></p>
</section>
```

- [ ] **Step 5: Write the style-guide page and palette component**

`PaletteSwatches` is paired because it owns a lookup of token names; the page itself stays inline (static markup).

```csharp
namespace TechStrap.Admin.Components.Showcase;

/// <summary>The token groups of docs/BRAND.md section 12 that the style guide shows as swatches.</summary>
public enum PaletteGroup
{
    Surface,
    Tints,
    Status,
    BrandMoment,
    Portal,
}
```

```razor
<ul class="ts-sg-swatches">
    @foreach (var token in Tokens)
    {
        <li>
            <span class="ts-sg-swatch" style="background: var(--@token)" aria-hidden="true"></span>
            <code>--@token</code>
        </li>
    }
</ul>
```

```csharp
using Microsoft.AspNetCore.Components;

namespace TechStrap.Admin.Components.Showcase;

/// <summary>Swatches for one token group. Each swatch paints the live custom property, so it follows the active theme.</summary>
public partial class PaletteSwatches
{
    private static readonly IReadOnlyDictionary<PaletteGroup, string[]> TokensByGroup = new Dictionary<PaletteGroup, string[]>
    {
        [PaletteGroup.Surface] = ["paper", "sheet", "rail", "head", "ink", "ink-2", "ink-3", "rule", "rule-strong", "margin", "hover", "sel", "overlay", "shadow", "scrim", "accent", "on-accent", "focus"],
        [PaletteGroup.Tints] = ["canary", "canary-edge", "pink", "pink-edge", "note-ink"],
        [PaletteGroup.Status] = ["st-new", "st-open", "st-pending", "st-solved", "st-closed", "st-spam"],
        [PaletteGroup.BrandMoment] = ["bm-plate", "bm-edge", "bm-bar", "bm-on-bar", "bm-text", "bm-text2", "bm-crt", "bm-on-crt", "bm-led", "bm-shadow"],
        [PaletteGroup.Portal] = ["p-bg", "p-soft", "p-ink", "p-ink2", "p-line"],
    };

    [Parameter, EditorRequired]
    public PaletteGroup Group { get; set; }

    private string[] Tokens => TokensByGroup[Group];
}
```

```razor
@page "/_styleguide"
@using TechStrap.Admin.Components.Showcase

<PageTitle>TechStrap style guide</PageTitle>
<DevelopmentOnly>
    <div class="ts-sg">
        <header class="ts-sg-header">
            <h1 class="ts-page-title">TechStrap style guide</h1>
            <p class="ts-sg-note">Development only. Tokens, components and states of docs/BRAND.md in the active theme. Set <code>data-bs-theme</code> on the html element to <code>light</code> or <code>dark</code>, or remove it for Auto.</p>
        </header>

        <section class="ts-sg-section" aria-labelledby="sg-type">
            <h2 id="sg-type" class="ts-label">Type scale</h2>
            <table class="table ts-sg-table">
                <thead><tr><th scope="col">Role</th><th scope="col">Sample</th></tr></thead>
                <tbody>
                    <tr><td>Page title (Mono 18 / 600)</td><td><span class="ts-page-title">Unassigned</span></td></tr>
                    <tr><td>Ticket problem line (Sans 16 / 600)</td><td><span class="ts-sg-problem">Backup job fails with exit code 17 after 2.4 update</span></td></tr>
                    <tr><td>Admin base text (Sans 14 / 400)</td><td>Tickets are logged, triaged and answered here.</td></tr>
                    <tr><td>Field value (Sans 13 / 500)</td><td><span class="ts-sg-field">Dana Whitfield</span></td></tr>
                    <tr><td>Message body (Serif 15 / 400)</td><td><span class="ts-serif ts-sg-message">Since upgrading on Tuesday our nightly backup exits with code 17.</span></td></tr>
                    <tr><td>Ticket id (Mono 13 / 500)</td><td><span class="ts-mono ts-sg-id">ACME-142</span></td></tr>
                    <tr><td>Timestamp, hint, tag (Mono 11 to 12)</td><td><span class="ts-mono ts-sg-hint">2026-10-02 08:41</span></td></tr>
                    <tr><td>Label (Mono 10, uppercase)</td><td><span class="ts-label">Requester</span></td></tr>
                </tbody>
            </table>
        </section>

        <section class="ts-sg-section" aria-labelledby="sg-palette">
            <h2 id="sg-palette" class="ts-label">Palette</h2>
            <h3 class="ts-sg-sub">Surface, ink and structure</h3>
            <PaletteSwatches Group="PaletteGroup.Surface" />
            <h3 class="ts-sg-sub">Carbon tints</h3>
            <PaletteSwatches Group="PaletteGroup.Tints" />
            <h3 class="ts-sg-sub">Status and priority</h3>
            <PaletteSwatches Group="PaletteGroup.Status" />
            <h3 class="ts-sg-sub">Brand moments</h3>
            <PaletteSwatches Group="PaletteGroup.BrandMoment" />
            <h3 class="ts-sg-sub">Portal</h3>
            <PaletteSwatches Group="PaletteGroup.Portal" />
        </section>

        <section class="ts-sg-section" aria-labelledby="sg-buttons">
            <h2 id="sg-buttons" class="ts-label">Buttons</h2>
            <div class="ts-sg-row">
                <button type="button" class="btn btn-primary">Send reply</button>
                <button type="button" class="btn btn-secondary">Cancel</button>
                <button type="button" class="btn btn-primary" disabled>Disabled</button>
                <button type="button" class="btn btn-danger">Delete</button>
                <a class="btn btn-secondary" href="/_styleguide">Link button</a>
            </div>
        </section>

        <section class="ts-sg-section" aria-labelledby="sg-forms">
            <h2 id="sg-forms" class="ts-label">Forms</h2>
            <form class="ts-sg-form" onsubmit="return false">
                <div class="mb-3">
                    <label class="form-label ts-label" for="sg-subject">Subject</label>
                    <input id="sg-subject" class="form-control" type="text" placeholder="Short summary" />
                </div>
                <div class="mb-3">
                    <label class="form-label ts-label" for="sg-status">Status</label>
                    <select id="sg-status" class="form-select">
                        <option>New</option>
                        <option>Open</option>
                        <option>Pending</option>
                    </select>
                </div>
                <div class="mb-3">
                    <label class="form-label ts-label" for="sg-body">Reply</label>
                    <textarea id="sg-body" class="form-control ts-serif" rows="3"></textarea>
                </div>
                <div class="form-check mb-3">
                    <input id="sg-check" class="form-check-input" type="checkbox" />
                    <label class="form-check-label" for="sg-check">Email me when this changes</label>
                </div>
                <div class="mb-3">
                    <label class="form-label ts-label" for="sg-invalid">Email (invalid)</label>
                    <input id="sg-invalid" class="form-control is-invalid" type="email" value="not-an-email" aria-describedby="sg-invalid-msg" />
                    <div id="sg-invalid-msg" class="invalid-feedback">Enter an email address like name@example.com.</div>
                </div>
            </form>
        </section>

        <section class="ts-sg-section" aria-labelledby="sg-tables">
            <h2 id="sg-tables" class="ts-label">Tables</h2>
            <table class="table table-hover ts-sg-table">
                <thead><tr><th scope="col">No.</th><th scope="col">Subject</th><th scope="col">Requester</th><th scope="col" class="text-end">Last</th></tr></thead>
                <tbody>
                    <tr><td class="ts-mono">ACME-142</td><td>Backup job fails with exit code 17</td><td>Dana Whitfield</td><td class="ts-mono text-end">12m</td></tr>
                    <tr class="table-active"><td class="ts-mono">ORB-38</td><td>Cannot reset my password</td><td>Lee Park</td><td class="ts-mono text-end">1h</td></tr>
                    <tr><td class="ts-mono">PXF-9</td><td>Export is missing the last row</td><td>Mo Adeyemi</td><td class="ts-mono text-end">3h</td></tr>
                </tbody>
            </table>
        </section>

        <section class="ts-sg-section" aria-labelledby="sg-alerts">
            <h2 id="sg-alerts" class="ts-label">Alerts</h2>
            <div class="alert alert-info" role="status">Ticket ACME-142 was assigned to Sam.</div>
            <div class="alert alert-success" role="status">Reply sent.</div>
            <div class="alert alert-warning" role="alert">3 emails failed to send. Review them.</div>
            <div class="alert alert-danger" role="alert">Could not save. The server did not respond. Your changes are still here; try again.</div>
        </section>

        <section class="ts-sg-section" aria-labelledby="sg-states">
            <h2 id="sg-states" class="ts-label">Loading, empty and error states</h2>
            <div class="ts-sg-states">
                <div class="card">
                    <div class="card-header ts-label">Loading</div>
                    <div class="card-body" aria-busy="true">
                        <p class="placeholder-glow mb-1"><span class="placeholder col-8"></span></p>
                        <p class="placeholder-glow mb-1"><span class="placeholder col-12"></span></p>
                        <p class="placeholder-glow mb-0"><span class="placeholder col-5"></span></p>
                    </div>
                </div>
                <div class="card">
                    <div class="card-header ts-label">Empty</div>
                    <div class="card-body ts-empty">No tickets match these filters. Clear a filter to see more.</div>
                </div>
                <div class="card">
                    <div class="card-header ts-label">Error</div>
                    <div class="card-body">
                        <div class="alert alert-danger mb-2" role="alert">Could not load tickets. The server did not respond.</div>
                        <button type="button" class="btn btn-secondary">Try again</button>
                    </div>
                </div>
            </div>
        </section>
    </div>
</DevelopmentOnly>
```

- [ ] **Step 6: Write the shell, guide, feedback and motion Sass**

```scss
// Admin shell (BRAND.md section 14): a 208px left rail plus the main area. Under 820px the rail becomes a horizontal strip.

.ts-shell {
  display: grid;
  grid-template-columns: 208px minmax(0, 1fr);
  min-height: 100vh;
}

.ts-rail {
  display: flex;
  flex-direction: column;
  gap: 2px;
  padding: 12px 0;
  background: var(--rail);
  border-right: 1px solid var(--rule-strong);
}

.ts-brand {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 0 14px 12px;
  margin-bottom: 8px;
  color: var(--ink);
  text-decoration: none;
  border-bottom: 1px dashed var(--rule-strong);

  img {
    width: 36px;
    height: 36px;
  }

  span {
    font: 600 .875rem/1.1 var(--ts-font-mono);
  }

  small {
    display: block;
    font: 400 .625rem var(--ts-font-mono);
    letter-spacing: .04em;
    text-transform: uppercase;
    color: var(--ink-3);
  }
}

.ts-rail-link {
  display: flex;
  justify-content: space-between;
  gap: 8px;
  padding: 6px 14px;
  color: var(--ink);
  text-decoration: none;
  border-left: 3px solid transparent;

  &:hover {
    background: var(--hover);
  }

  &[aria-current="page"] {
    background: var(--sel);
    border-left-color: var(--margin);
    font-weight: 600;
  }
}

.ts-main {
  min-width: 0;
}

@media (max-width: 820px) {
  .ts-shell {
    grid-template-columns: minmax(0, 1fr);
    min-height: 0;
  }

  .ts-rail {
    flex-direction: row;
    align-items: center;
    overflow-x: auto;
    padding: 6px 8px;
    border-right: 0;
    border-bottom: 1px solid var(--rule-strong);
  }

  .ts-brand {
    padding: 0 8px 0 0;
    margin: 0;
    border: 0;

    img {
      width: 28px;
      height: 28px;
    }

    span {
      display: none;
    }
  }

  .ts-rail-link {
    white-space: nowrap;
    border-left: 0;
    border-bottom: 3px solid transparent;
  }
}
```

```scss
// Style guide page (/_styleguide, Development only). Layout only; every color comes from the brand tokens.

.ts-sg {
  max-width: 1100px;
  padding: 16px;
}

.ts-sg-header {
  margin-bottom: 8px;
}

.ts-sg-note {
  margin: 4px 0 0;
  font: 400 .75rem var(--ts-font-mono);
  color: var(--ink-2);
}

.ts-sg-section {
  margin-top: 28px;
  padding-top: 8px;
  border-top: 2px solid var(--rule-strong);
}

.ts-sg-sub {
  margin: 14px 0 6px;
  font: 600 .8125rem var(--ts-font-sans);
}

.ts-sg-row {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  align-items: center;
}

.ts-sg-table {
  background: var(--sheet);

  th {
    background: var(--head);
    border-bottom: 2px solid var(--rule-strong);
    font: 500 .625rem var(--ts-font-mono);
    letter-spacing: .08em;
    text-transform: uppercase;
    color: var(--ink-2);
  }

  td {
    vertical-align: middle;
    border-bottom-color: var(--rule);
  }
}

.ts-sg-problem {
  font: 600 1rem/1.3 var(--ts-font-sans);
}

.ts-sg-field {
  font: 500 .8125rem var(--ts-font-sans);
}

.ts-sg-message {
  font-size: .9375rem;
  line-height: 1.55;
}

.ts-sg-id {
  font-size: .8125rem;
  font-weight: 500;
}

.ts-sg-hint {
  font-size: .75rem;
}

.ts-sg-swatches {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(150px, 1fr));
  gap: 6px 12px;
  margin: 0;
  padding: 0;
  list-style: none;

  li {
    display: flex;
    align-items: center;
    gap: 8px;
    min-width: 0;
  }

  code {
    font-size: .6875rem;
    color: var(--ink-2);
  }
}

.ts-sg-swatch {
  flex: none;
  width: 28px;
  height: 20px;
  border: 1px solid var(--rule-strong);
}

.ts-sg-form {
  max-width: 420px;
}

.ts-sg-states {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(240px, 1fr));
  gap: 12px;
}

.ts-empty {
  color: var(--ink-2);
}
```

```scss
// Styles for the SyntaxCircus.Blazor.Components error view. The package ships no CSS and exposes the class we pass
// (CssClass="ts-error") plus stable data attributes. Plain, calm and legible: errors never carry the mascot or humor.
.ts-error {
  max-width: 560px;
  margin: 24px 16px;
  padding: 16px;
  background: var(--sheet);
  border: 2px solid var(--st-spam);
  box-shadow: 3px 3px 0 var(--shadow);

  h1 {
    margin: 0 0 8px;
    font: 600 1.125rem/1.3 var(--ts-font-mono);
  }

  p {
    margin: 0 0 12px;
  }

  [data-global-error-actions] {
    display: flex;
    flex-wrap: wrap;
    gap: 8px;
    align-items: center;
  }

  button {
    min-height: 34px;
    padding: 6px 14px;
    font: 600 .75rem var(--ts-font-mono);
    letter-spacing: .05em;
    text-transform: uppercase;
    color: var(--paper);
    background: var(--ink);
    border: 2px solid var(--ink);
    border-radius: 0;
  }
}
```

```scss
// BRAND.md section 17: motion is minimal and always optional. prefers-reduced-motion disables every animation and
// transition. State is never conveyed by motion alone, and each animated element has a correct static end state.
@media (prefers-reduced-motion: reduce) {
  *,
  *::before,
  *::after {
    animation: none !important;
    transition: none !important;
  }
}
```

Add these imports to `app.scss`, in this order, after `@import "fonts";` (and before the `.shell-placeholder` rule):

```scss
@import "shell";
@import "feedback";
@import "styleguide";
@import "motion";
```

- [ ] **Step 7: Run to verify they pass**

Run: `dotnet test --project tests/TechStrap.Admin.Tests` and `dotnet test --project tests/TechStrap.Api.Tests --filter-class "*ShellHostTests"`
Expected: PASS (`StyleGuideEnvironmentTests` 3, `DevelopmentOnlyTests` 4, `MainLayoutTests` 3, `LayoutHostTests` 1; the existing `ShellHostTests` still pass 2 of 2, so the Home heading `TechStrap Admin` and `.shell-placeholder` survived).

- [ ] **Step 8: Commit**

```bash
git add src/TechStrap.Admin tests/TechStrap.Admin.Tests
git commit -m "feat(admin): add the shell layout, the Development-only style guide and the head mark" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 7: Status stamps and priority marks

**Files:**
- Create: `src/TechStrap.Admin/Components/Ui/StampStatus.cs`, `StatusStamp.razor`, `StatusStamp.razor.cs`, `PriorityLevel.cs`, `PriorityMark.razor`, `PriorityMark.razor.cs`
- Create: `src/TechStrap.Admin/Styles/_stamp.scss`, `_priority.scss`
- Modify: `src/TechStrap.Admin/Styles/app.scss`, `src/TechStrap.Admin/Components/Pages/StyleGuide.razor`
- Test: `tests/TechStrap.Admin.Tests/Components/StatusStampTests.cs`, `Components/PriorityMarkTests.cs`, `StampStyleTests.cs`, `StyleGuideContentTests.cs`

**Interfaces:**
- Consumes: `CompiledCss` (Task 3), `AdminFactory` (Task 4), the `--st-*` tokens and `.ts-sg-*` classes.
- Produces (used by PHASE-07 queue and ticket screens):
  - `enum StampStatus { New, Open, Pending, Solved, Closed, Spam }`, `enum StampVariant { Queue, Ticket }` (UI-local; Admin may not reference Domain)
  - `<StatusStamp Status="StampStatus" Variant="StampVariant.Queue" Animate="false" />` rendering `span.ts-stamp.ts-stamp--{status}.ts-stamp--{variant}[.ts-stamp--pop]` whose text is the status word (`Spam?` in the queue, `Spam` on the ticket)
  - `enum PriorityLevel { Urgent, High, Normal, Low }`, `<PriorityMark Level="PriorityLevel" />` rendering `span.ts-priority.ts-priority--{level}` with the word
  - Custom property `--ts-tilt` (0deg, -2deg on the ticket variant, 2deg for ticket spam), keyframes `ts-thud`.

- [ ] **Step 1: Write the failing tests**

```csharp
using Bunit;
using TechStrap.Admin.Components.Ui;

namespace TechStrap.Admin.Tests.Components;

public sealed class StatusStampTests : BunitContext
{
    public static TheoryData<StampStatus, StampVariant, string> Labels() => new()
    {
        { StampStatus.New, StampVariant.Queue, "New" },
        { StampStatus.Open, StampVariant.Queue, "Open" },
        { StampStatus.Pending, StampVariant.Queue, "Pending" },
        { StampStatus.Solved, StampVariant.Queue, "Solved" },
        { StampStatus.Closed, StampVariant.Queue, "Closed" },
        { StampStatus.Spam, StampVariant.Queue, "Spam?" },
        { StampStatus.New, StampVariant.Ticket, "New" },
        { StampStatus.Open, StampVariant.Ticket, "Open" },
        { StampStatus.Pending, StampVariant.Ticket, "Pending" },
        { StampStatus.Solved, StampVariant.Ticket, "Solved" },
        { StampStatus.Closed, StampVariant.Ticket, "Closed" },
        { StampStatus.Spam, StampVariant.Ticket, "Spam" },
    };

    [Theory]
    [MemberData(nameof(Labels))]
    public void Status_is_a_word_plus_a_shape_class_never_colour_alone(StampStatus status, StampVariant variant, string label)
    {
        var cut = Render<StatusStamp>(p => p.Add(s => s.Status, status).Add(s => s.Variant, variant));

        var stamp = cut.Find("span.ts-stamp");
        stamp.TextContent.ShouldBe(label);
        stamp.ClassList.ShouldContain($"ts-stamp--{status.ToString().ToLowerInvariant()}");
        stamp.ClassList.ShouldContain($"ts-stamp--{variant.ToString().ToLowerInvariant()}");
    }

    [Fact]
    public void The_queue_variant_is_the_default()
    {
        var cut = Render<StatusStamp>(p => p.Add(s => s.Status, StampStatus.Open));

        cut.Find("span.ts-stamp").ClassList.ShouldContain("ts-stamp--queue");
    }

    [Fact]
    public void Stamp_down_animation_applies_to_the_ticket_variant_only()
    {
        var ticket = Render<StatusStamp>(p => p.Add(s => s.Status, StampStatus.Solved).Add(s => s.Variant, StampVariant.Ticket).Add(s => s.Animate, true));
        var queue = Render<StatusStamp>(p => p.Add(s => s.Status, StampStatus.Solved).Add(s => s.Variant, StampVariant.Queue).Add(s => s.Animate, true));
        var still = Render<StatusStamp>(p => p.Add(s => s.Status, StampStatus.Solved).Add(s => s.Variant, StampVariant.Ticket));

        ticket.Find("span.ts-stamp").ClassList.ShouldContain("ts-stamp--pop");
        queue.Find("span.ts-stamp").ClassList.ShouldNotContain("ts-stamp--pop");
        still.Find("span.ts-stamp").ClassList.ShouldNotContain("ts-stamp--pop");
    }
}
```

```csharp
using Bunit;
using TechStrap.Admin.Components.Ui;

namespace TechStrap.Admin.Tests.Components;

public sealed class PriorityMarkTests : BunitContext
{
    [Theory]
    [InlineData(PriorityLevel.Urgent, "Urgent", "ts-priority--urgent")]
    [InlineData(PriorityLevel.High, "High", "ts-priority--high")]
    [InlineData(PriorityLevel.Normal, "Normal", "ts-priority--normal")]
    [InlineData(PriorityLevel.Low, "Low", "ts-priority--low")]
    public void Priority_is_a_marker_class_plus_a_word(PriorityLevel level, string word, string cssClass)
    {
        var cut = Render<PriorityMark>(p => p.Add(m => m.Level, level));

        var mark = cut.Find("span.ts-priority");
        mark.TextContent.ShouldBe(word);
        mark.ClassList.ShouldContain(cssClass);
    }
}
```

`StampStyleTests` pins the stamp rules of BRAND.md and the reduced-motion rule (Review Focus 3):

```csharp
using System.Text.RegularExpressions;
using TechStrap.Tests.Shared;

namespace TechStrap.Admin.Tests;

/// <summary>BRAND.md sections 13, 17 and 18: straight single-border stamps in lists, tilted ones on the ticket view, and no motion when the user asks for none.</summary>
public sealed class StampStyleTests
{
    private static readonly CompiledCss Css = CompiledCss.Load("TechStrap.Admin");

    [Theory]
    [InlineData("new", "st-new")]
    [InlineData("open", "st-open")]
    [InlineData("pending", "st-pending")]
    [InlineData("solved", "st-solved")]
    [InlineData("closed", "st-closed")]
    [InlineData("spam", "st-spam")]
    public void Each_status_stamp_takes_its_status_token(string status, string token)
    {
        Css.Declarations($".ts-stamp--{status}")["--ts-stamp-color"].ShouldBe($"var(--{token})");
    }

    [Fact]
    public void Queue_stamps_are_straight_with_a_single_border()
    {
        var queue = Css.Declarations(".ts-stamp--queue");

        queue["border-width"].ShouldBe("1.5px");
        queue.ShouldNotContainKey("transform");
        queue.ShouldNotContainKey("box-shadow");
    }

    [Fact]
    public void Ticket_stamps_tilt_minus_two_degrees_with_an_inner_ring_and_spam_tilts_the_other_way()
    {
        var ticket = Css.Declarations(".ts-stamp--ticket");

        ticket["--ts-tilt"].ShouldBe("-2deg");
        ticket["transform"].ShouldBe("rotate(var(--ts-tilt))");
        ticket["box-shadow"].ShouldContain("inset");
        Css.Declarations(".ts-stamp--ticket.ts-stamp--spam")["--ts-tilt"].ShouldBe("2deg");
    }

    [Fact]
    public void Spam_has_a_double_three_pixel_border()
    {
        var spam = Css.Declarations(".ts-stamp--spam");

        spam["border-style"].ShouldBe("double");
        spam["border-width"].ShouldBe("3px");
    }

    [Fact]
    public void Stamp_down_runs_for_0_35s_and_ends_at_the_tilt()
    {
        Css.Declarations(".ts-stamp--pop")["animation"].ShouldBe("ts-thud .35s ease-out");
        Regex.IsMatch(Css.Text, @"@keyframes ts-thud\{0%\{transform:rotate\(var\(--ts-tilt\)\) scale\(1\.6\);opacity:0\}70%\{[^}]*scale\(0?\.95\)[^}]*\}100%\{transform:rotate\(var\(--ts-tilt\)\) scale\(1\)\}\}").ShouldBeTrue();
    }

    [Fact]
    public void Reduced_motion_switches_off_every_animation_and_transition()
    {
        Regex.IsMatch(
            Css.Text,
            @"@media\s*\(prefers-reduced-motion:\s*reduce\)\{\*,\*::before,\*::after\{animation:\s*none\s*!important;transition:\s*none\s*!important\}\}").ShouldBeTrue();
    }
}
```

`StyleGuideContentTests` states what the guide must show; later tasks add rows to it. It uses AngleSharp, which arrives transitively with bUnit:

```csharp
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using TechStrap.Tests.Shared;

namespace TechStrap.Admin.Tests;

/// <summary>What the Development style guide must show (docs/PHASE-02 task P02-T07). Each component task adds its section here.</summary>
public sealed class StyleGuideContentTests : IAsyncLifetime
{
    private AdminFactory _factory = default!;
    private IDocument _page = default!;

    public async ValueTask InitializeAsync()
    {
        _factory = new AdminFactory("Development");
        using var client = _factory.CreateClient();
        var html = await client.GetStringAsync("/_styleguide", TestContext.Current.CancellationToken);
        _page = await new HtmlParser().ParseDocumentAsync(html, TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    private IElement Section(string headingId) =>
        _page.QuerySelector($"section[aria-labelledby='{headingId}']") ?? throw new InvalidOperationException($"No section labeled by #{headingId}");

    [Theory]
    [InlineData("sg-type")]
    [InlineData("sg-palette")]
    [InlineData("sg-buttons")]
    [InlineData("sg-forms")]
    [InlineData("sg-tables")]
    [InlineData("sg-alerts")]
    [InlineData("sg-states")]
    [InlineData("sg-stamps")]
    public void Section_is_present(string id)
    {
        Section(id).QuerySelector($"h2#{id}").ShouldNotBeNull(id);
    }

    [Fact]
    public void All_five_statuses_plus_spam_show_in_both_variants_and_the_four_priorities_show()
    {
        foreach (var status in new[] { "new", "open", "pending", "solved", "closed", "spam" })
        {
            Section("sg-stamps").QuerySelectorAll($".ts-stamp--queue.ts-stamp--{status}").Length.ShouldBe(1, $"queue {status}");
            Section("sg-stamps").QuerySelectorAll($".ts-stamp--ticket.ts-stamp--{status}").Length.ShouldBe(1, $"ticket {status}");
        }

        foreach (var level in new[] { "urgent", "high", "normal", "low" })
        {
            Section("sg-stamps").QuerySelectorAll($".ts-priority--{level}").Length.ShouldBe(1, level);
        }
    }

    [Fact]
    public void The_palette_lists_every_brand_token_with_a_swatch()
    {
        var shown = Section("sg-palette").QuerySelectorAll("code").Select(c => c.TextContent.TrimStart('-')).ToHashSet();

        foreach (var token in BrandTokenTable.Read())
        {
            shown.ShouldContain(token.Name);
        }
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test --project tests/TechStrap.Admin.Tests`
Expected: FAIL to compile, `error CS0246: The type or namespace name 'StampStatus' could not be found`.

- [ ] **Step 3: Write the components**

```csharp
namespace TechStrap.Admin.Components.Ui;

/// <summary>
/// What a status stamp says. This is a presentation enum owned by the Admin UI (Admin may not reference Domain). It is the five
/// ticket statuses plus Spam, which is a flag on a ticket in the domain but gets its own double-border stamp (docs/BRAND.md section 18).
/// </summary>
public enum StampStatus
{
    New,
    Open,
    Pending,
    Solved,
    Closed,
    Spam,
}

/// <summary>Where the stamp is shown: straight and single-bordered in lists, tilted on the ticket view.</summary>
public enum StampVariant
{
    Queue,
    Ticket,
}
```

```razor
<span class="@CssClass">@Label</span>
```

```csharp
using Microsoft.AspNetCore.Components;

namespace TechStrap.Admin.Components.Ui;

/// <summary>
/// A rubber-stamp status badge. The status is always the word itself plus a shape class, never color alone.
/// Queue stamps are straight with one border; ticket stamps are tilted and may play the stamp-down animation.
/// </summary>
public partial class StatusStamp
{
    private const string Base = "ts-stamp";

    [Parameter, EditorRequired]
    public StampStatus Status { get; set; }

    [Parameter]
    public StampVariant Variant { get; set; } = StampVariant.Queue;

    /// <summary>Play the stamp-down animation (ticket variant only), used when the status has just changed.</summary>
    [Parameter]
    public bool Animate { get; set; }

    private string CssClass
    {
        get
        {
            var css = $"{Base} {Base}--{Status.ToString().ToLowerInvariant()} {Base}--{Variant.ToString().ToLowerInvariant()}";
            return Animate && Variant == StampVariant.Ticket ? $"{css} {Base}--pop" : css;
        }
    }

    // The queue shows "Spam?" (a suggestion to confirm); the ticket view shows the settled "Spam".
    private string Label => Status == StampStatus.Spam && Variant == StampVariant.Queue ? "Spam?" : Status.ToString();
}
```

```csharp
namespace TechStrap.Admin.Components.Ui;

/// <summary>Ticket priority as the Admin UI shows it (a presentation enum; Admin may not reference Domain).</summary>
public enum PriorityLevel
{
    Urgent,
    High,
    Normal,
    Low,
}
```

```razor
<span class="@CssClass">@Level</span>
```

```csharp
using Microsoft.AspNetCore.Components;

namespace TechStrap.Admin.Components.Ui;

/// <summary>A square marker plus the priority word (never color alone). Urgent uses the spam red, High the pending amber, Normal and Low the secondary ink; Low's marker is dashed.</summary>
public partial class PriorityMark
{
    [Parameter, EditorRequired]
    public PriorityLevel Level { get; set; }

    private string CssClass => $"ts-priority ts-priority--{Level.ToString().ToLowerInvariant()}";
}
```

- [ ] **Step 4: Write the Sass**

The animation ends at the same tilt as the static style, so with reduced motion (Task 6's `_motion.scss`) the stamp is already in its final position.

```scss
// Status stamps (BRAND.md section 18). Straight and single-bordered in lists; tilted with an inner ring on the ticket view.
// The stamp-down animation ends at the tilt, so with prefers-reduced-motion the static state is already correct.

.ts-stamp {
  --ts-stamp-color: var(--ink);
  --ts-tilt: 0deg;
  display: inline-block;
  padding: 3px 6px 2px;
  font: 600 .6875rem/1 var(--ts-font-mono);
  letter-spacing: .08em;
  text-transform: uppercase;
  white-space: nowrap;
  color: var(--ts-stamp-color);
  background: transparent;
  border: 2px solid var(--ts-stamp-color);
  border-radius: 3px 6px 3px 5px / 5px 3px 6px 3px;
}

.ts-stamp--new { --ts-stamp-color: var(--st-new); }
.ts-stamp--open { --ts-stamp-color: var(--st-open); }
.ts-stamp--pending { --ts-stamp-color: var(--st-pending); }
.ts-stamp--solved { --ts-stamp-color: var(--st-solved); }
.ts-stamp--closed { --ts-stamp-color: var(--st-closed); }

.ts-stamp--spam {
  --ts-stamp-color: var(--st-spam);
  border-style: double;
  border-width: 3px;
}

// Queue: 10px, one 1.5px border, no inner ring, no tilt.
.ts-stamp--queue {
  padding: 2px 5px 1px;
  font-size: .625rem;
  letter-spacing: .07em;
  border-width: 1.5px;
}

.ts-stamp--queue.ts-stamp--spam {
  border-width: 3px;
}

// Ticket view: 11px, 2px border plus an inner hairline ring, tilted -2deg (spam +2deg).
.ts-stamp--ticket {
  --ts-tilt: -2deg;
  transform: rotate(var(--ts-tilt));
  box-shadow: inset 0 0 0 1px var(--sheet), inset 0 0 0 2px var(--ts-stamp-color);
}

.ts-stamp--ticket.ts-stamp--spam {
  --ts-tilt: 2deg;
}

.ts-stamp--pop {
  animation: ts-thud .35s ease-out;
}

@keyframes ts-thud {
  0% {
    transform: rotate(var(--ts-tilt)) scale(1.6);
    opacity: 0;
  }

  70% {
    transform: rotate(var(--ts-tilt)) scale(.95);
    opacity: 1;
  }

  100% {
    transform: rotate(var(--ts-tilt)) scale(1);
  }
}
```

```scss
// Priority marker (BRAND.md section 12): a square marker plus the word. Urgent = spam red, High = pending amber,
// Normal and Low = secondary ink; Low has a dashed marker.
.ts-priority {
  font: 600 .6875rem var(--ts-font-mono);
  letter-spacing: .04em;
  text-transform: uppercase;
  color: var(--ink-2);

  &::before {
    content: "";
    display: inline-block;
    width: 8px;
    height: 8px;
    margin-right: 5px;
    border: 2px solid var(--ink-2);
  }
}

.ts-priority--urgent {
  color: var(--st-spam);

  &::before {
    background: var(--st-spam);
    border-color: var(--st-spam);
  }
}

.ts-priority--high {
  color: var(--st-pending);

  &::before {
    background: var(--st-pending);
    border-color: var(--st-pending);
  }
}

.ts-priority--low::before {
  border-style: dashed;
}
```

Add to `app.scss` after `@import "shell";`:

```scss
@import "stamp";
@import "priority";
```

- [ ] **Step 5: Add the stamps section to the style guide**

Insert before the `sg-buttons` section of `StyleGuide.razor`:

```razor
        <section class="ts-sg-section" aria-labelledby="sg-stamps">
            <h2 id="sg-stamps" class="ts-label">Status stamps and priority</h2>
            <p class="ts-sg-note">Queue stamps are straight with one border. Ticket stamps are tilted with an inner ring; the stamp-down animation plays only when the status changes. Spam has a double border. Status is always the word plus the shape, never colour alone.</p>
            <table class="table ts-sg-table">
                <thead><tr><th scope="col">Status</th><th scope="col">Queue</th><th scope="col">Ticket</th></tr></thead>
                <tbody>
                    <tr><td>New</td><td><StatusStamp Status="StampStatus.New" /></td><td><StatusStamp Status="StampStatus.New" Variant="StampVariant.Ticket" /></td></tr>
                    <tr><td>Open</td><td><StatusStamp Status="StampStatus.Open" /></td><td><StatusStamp Status="StampStatus.Open" Variant="StampVariant.Ticket" /></td></tr>
                    <tr><td>Pending</td><td><StatusStamp Status="StampStatus.Pending" /></td><td><StatusStamp Status="StampStatus.Pending" Variant="StampVariant.Ticket" /></td></tr>
                    <tr><td>Solved</td><td><StatusStamp Status="StampStatus.Solved" /></td><td><StatusStamp Status="StampStatus.Solved" Variant="StampVariant.Ticket" /></td></tr>
                    <tr><td>Closed</td><td><StatusStamp Status="StampStatus.Closed" /></td><td><StatusStamp Status="StampStatus.Closed" Variant="StampVariant.Ticket" /></td></tr>
                    <tr><td>Spam</td><td><StatusStamp Status="StampStatus.Spam" /></td><td><StatusStamp Status="StampStatus.Spam" Variant="StampVariant.Ticket" /></td></tr>
                </tbody>
            </table>
            <div class="ts-sg-row">
                <PriorityMark Level="PriorityLevel.Urgent" />
                <PriorityMark Level="PriorityLevel.High" />
                <PriorityMark Level="PriorityLevel.Normal" />
                <PriorityMark Level="PriorityLevel.Low" />
            </div>
        </section>
```

- [ ] **Step 6: Run to verify they pass**

Run: `dotnet test --project tests/TechStrap.Admin.Tests`
Expected: PASS (`total: 187`). If `Stamp_down_runs_for_0_35s_and_ends_at_the_tilt` fails, the compressed CSS prints `scale(0.95)` with a leading zero inside functions; the test's regex accepts both.

- [ ] **Step 7: Commit**

```bash
git add src/TechStrap.Admin tests/TechStrap.Admin.Tests
git commit -m "feat(admin): add StatusStamp and PriorityMark with reduced-motion-safe stamp-down" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 8: Carbon tint code, legend and keycaps

**Files:**
- Create: `src/TechStrap.Admin/Components/Ui/EntryKind.cs`, `TintedEntry.razor`, `TintedEntry.razor.cs`, `TintLegend.razor`, `Kbd.razor`
- Create: `src/TechStrap.Admin/Styles/_tinted-entry.scss`, `_kbd.scss`
- Modify: `src/TechStrap.Admin/Styles/app.scss`, `src/TechStrap.Admin/Styles/_styleguide.scss`, `src/TechStrap.Admin/Components/Pages/StyleGuide.razor`, `tests/TechStrap.Admin.Tests/StyleGuideContentTests.cs`
- Test: `tests/TechStrap.Admin.Tests/Components/TintedEntryTests.cs`, `Components/KbdAndLegendTests.cs`, `TintStyleTests.cs`

**Interfaces:**
- Consumes: `ProductAccent.ContrastRatio` (Task 2), `CompiledCss`, `RepositoryRoot` (Task 3).
- Produces (used by the PHASE-07 ticket timeline and composer):
  - `enum EntryKind { Customer, PublicReply, InternalNote }`
  - `<TintedEntry Kind="EntryKind" Author="string" Time="string">body</TintedEntry>` rendering `article.ts-entry.ts-entry--{customer|public|note}` with `.ts-entry-head` (`strong` author, role word `customer` or `agent reply`, or the `.ts-entry-label` text `INTERNAL NOTE`), `.ts-entry-time`, `.ts-entry-body`
  - `<TintLegend />` (`.ts-legend`, three labeled swatches), `<Kbd>key</Kbd>` (`kbd.ts-kbd`)
  - Guard tests: tint tokens (`--canary`, `--pink`, `--note-ink`) may be used only by `_tinted-entry.scss`; `--bm-*` only by `_brand-window.scss`. PHASE-07 extends the allowed list consciously when the composer, avatar and status bar land.

- [ ] **Step 1: Write the failing tests**

```csharp
using Bunit;
using TechStrap.Admin.Components.Ui;

namespace TechStrap.Admin.Tests.Components;

/// <summary>The carbon tint code (BRAND.md section 12): white customer, canary public reply, pink dashed notched internal note. Color is never the only cue.</summary>
public sealed class TintedEntryTests : BunitContext
{
    private IRenderedComponent<TintedEntry> Render(EntryKind kind) =>
        Render<TintedEntry>(p => p
            .Add(e => e.Kind, kind)
            .Add(e => e.Author, "Dana Whitfield")
            .Add(e => e.Time, "08:41")
            .AddChildContent("<p>The backup exits with code 17.</p>"));

    [Fact]
    public void A_customer_message_is_white_and_labelled_customer()
    {
        var cut = Render(EntryKind.Customer);

        var entry = cut.Find("article.ts-entry");
        entry.ClassList.ShouldContain("ts-entry--customer");
        cut.Find(".ts-entry-head").TextContent.ShouldContain("customer");
        cut.Find(".ts-entry-head strong").TextContent.ShouldBe("Dana Whitfield");
        cut.Find(".ts-entry-time").TextContent.ShouldBe("08:41");
        cut.Find(".ts-entry-body p").TextContent.ShouldBe("The backup exits with code 17.");
        cut.FindAll(".ts-entry-label").ShouldBeEmpty();
    }

    [Fact]
    public void A_public_reply_is_canary_and_labelled_agent_reply()
    {
        var cut = Render(EntryKind.PublicReply);

        cut.Find("article.ts-entry").ClassList.ShouldContain("ts-entry--public");
        cut.Find(".ts-entry-head").TextContent.ShouldContain("agent reply");
        cut.FindAll(".ts-entry-label").ShouldBeEmpty();
    }

    [Fact]
    public void An_internal_note_carries_the_INTERNAL_NOTE_label_so_pink_is_never_the_only_cue()
    {
        var cut = Render(EntryKind.InternalNote);

        cut.Find("article.ts-entry").ClassList.ShouldContain("ts-entry--note");
        cut.Find(".ts-entry-label").TextContent.ShouldBe("INTERNAL NOTE");
        cut.Find(".ts-entry-head").TextContent.ShouldNotContain("customer");
    }

    [Fact]
    public void Each_kind_gets_exactly_one_tint_class()
    {
        foreach (var kind in Enum.GetValues<EntryKind>())
        {
            var classes = Render(kind).Find("article.ts-entry").ClassList.Where(c => c.StartsWith("ts-entry--", StringComparison.Ordinal)).ToList();

            classes.Count.ShouldBe(1, kind.ToString());
        }
    }
}
```

```csharp
using Bunit;
using TechStrap.Admin.Components.Ui;

namespace TechStrap.Admin.Tests.Components;

public sealed class KbdAndLegendTests : BunitContext
{
    [Fact]
    public void Kbd_renders_a_keycap()
    {
        var cut = Render<Kbd>(p => p.AddChildContent("j"));

        cut.Find("kbd.ts-kbd").TextContent.ShouldBe("j");
    }

    [Fact]
    public void The_legend_names_all_three_tints_with_their_meaning()
    {
        var cut = Render<TintLegend>();

        var items = cut.FindAll(".ts-legend span").Select(s => s.TextContent.Trim()).ToList();
        items.ShouldBe(["Customer (white)", "Public reply (canary)", "Internal note (pink)"]);
        cut.Find(".ts-legend-note i").ClassList.ShouldContain("ts-legend-swatch--note");
    }
}
```

`TintStyleTests` covers the tint rules, the dark-mode readability of the pink note (Review Focus 2) and the two isolation guards:

```csharp
using TechStrap.Contracts.Branding;
using TechStrap.Tests.Shared;

namespace TechStrap.Admin.Tests;

/// <summary>
/// The carbon tint code is a hard rule (BRAND.md section 12): the tints keep their meaning, carry non-color cues, stay readable in
/// both themes, and are never reused for decoration.
/// </summary>
public sealed class TintStyleTests
{
    private const string LightScope = ":root,[data-bs-theme=light]";
    private const string DarkScope = "[data-bs-theme=dark]";
    private const double Aa = 4.5;

    private static readonly CompiledCss Css = CompiledCss.Load("TechStrap.Admin");

    // Styles that may paint with the carbon tints. PHASE-07 adds the reply composer, the avatar fill and the status-bar message
    // here when those components land; the list is deliberately short so a new use is a conscious decision.
    private static readonly string[] TintFiles = ["_tinted-entry.scss"];
    private static readonly string[] BrandMomentFiles = ["_brand-window.scss"];

    [Fact]
    public void Customer_is_white_with_a_grey_bar_public_is_canary_with_a_canary_bar()
    {
        var customer = Css.Declarations(".ts-entry--customer");
        var reply = Css.Declarations(".ts-entry--public");

        customer["background"].ShouldBe("var(--sheet)");
        customer["border-left"].ShouldBe("6px solid var(--ink-3)");
        reply["background"].ShouldBe("var(--canary)");
        reply["border-left"].ShouldBe("6px solid var(--canary-edge)");
    }

    [Fact]
    public void Internal_note_is_pink_dashed_notched_and_flat()
    {
        var note = Css.Declarations(".ts-entry--note");

        note["background"].ShouldBe("var(--pink)");
        note["border"].ShouldBe("2px dashed var(--pink-edge)");
        note["clip-path"].ShouldBe("polygon(0 0, calc(100% - 18px) 0, 100% 18px, 100% 100%, 0 100%)");
        note["box-shadow"].ShouldBe("none");
        Css.Declarations(".ts-entry--note .ts-entry-head")["color"].ShouldBe("var(--note-ink)");
    }

    [Theory]
    [InlineData("light")]
    [InlineData("dark")]
    public void Internal_note_text_is_readable_in_both_themes(string theme)
    {
        var tokens = Css.Declarations(theme == "light" ? LightScope : DarkScope);
        var background = Resolve(tokens, Css.Declarations(".ts-entry--note")["background"]);

        // Body text inherits ink from the entry; the head and label use note-ink.
        var body = Resolve(tokens, Css.Declarations(".ts-entry")["color"]);
        var head = Resolve(tokens, Css.Declarations(".ts-entry--note .ts-entry-head")["color"]);

        ProductAccent.ContrastRatio(body, background).ShouldBeGreaterThanOrEqualTo(Aa, $"note body in {theme}");
        ProductAccent.ContrastRatio(head, background).ShouldBeGreaterThanOrEqualTo(Aa, $"note head in {theme}");
    }

    [Theory]
    [InlineData("light")]
    [InlineData("dark")]
    public void Public_reply_text_is_readable_in_both_themes(string theme)
    {
        var tokens = Css.Declarations(theme == "light" ? LightScope : DarkScope);

        ProductAccent.ContrastRatio(
                Resolve(tokens, Css.Declarations(".ts-entry")["color"]),
                Resolve(tokens, Css.Declarations(".ts-entry--public")["background"]))
            .ShouldBeGreaterThanOrEqualTo(Aa);
    }

    [Fact]
    public void Keycaps_are_square_with_a_thicker_bottom_edge_and_sit_two_pixels_apart_in_a_chord()
    {
        var kbd = Css.Declarations(".ts-kbd");

        kbd["border-radius"].ShouldBe("0");
        kbd["border"].ShouldBe("1px solid var(--rule-strong)");
        kbd["border-bottom-width"].ShouldBe("2px");
        Css.Declarations(".ts-kbd+.ts-kbd")["margin-left"].ShouldBe("2px");
    }

    [Fact]
    public void Tint_tokens_are_used_only_by_the_files_allowed_to_paint_them()
    {
        foreach (var (file, text) in StyleSources())
        {
            var usesTint = new[] { "var(--canary", "var(--pink", "var(--note-ink" }.Any(text.Contains);

            (!usesTint || TintFiles.Contains(file)).ShouldBeTrue($"{file} paints with a carbon tint token; only {string.Join(", ", TintFiles)} may (BRAND.md: the tint code is never reused)");
        }
    }

    [Fact]
    public void Brand_moment_tokens_are_used_only_by_the_retro_window()
    {
        foreach (var (file, text) in StyleSources())
        {
            (!text.Contains("var(--bm-") || BrandMomentFiles.Contains(file)).ShouldBeTrue($"{file} uses a --bm-* token; only {string.Join(", ", BrandMomentFiles)} may");
        }
    }

    private static IEnumerable<(string File, string Text)> StyleSources() =>
        Directory.GetFiles(RepositoryRoot.Combine("src", "TechStrap.Admin", "Styles"), "*.scss")
            .Select(path => (Path.GetFileName(path), File.ReadAllText(path)));

    /// <summary>Turns <c>var(--ink)</c> into the theme's value for that token.</summary>
    private static string Resolve(IReadOnlyDictionary<string, string> tokens, string value) =>
        value.StartsWith("var(--", StringComparison.Ordinal) ? tokens[value[4..^1]] : value;
}
```

Extend the guide content tests:

```diff
@@ -33,4 +33,6 @@ public sealed class StyleGuideContentTests : IAsyncLifetime
     [InlineData("sg-states")]
     [InlineData("sg-stamps")]
+    [InlineData("sg-tints")]
+    [InlineData("sg-keys")]
     public void Section_is_present(string id)
     {
@@ -63,3 +65,26 @@ public sealed class StyleGuideContentTests : IAsyncLifetime
         }
     }
+
+    [Fact]
+    public void The_three_tinted_entries_and_the_legend_show()
+    {
+        var section = Section("sg-tints");
+
+        section.QuerySelectorAll(".ts-entry--customer").Length.ShouldBe(1);
+        section.QuerySelectorAll(".ts-entry--public").Length.ShouldBe(1);
+        section.QuerySelectorAll(".ts-entry--note").Length.ShouldBe(1);
+        section.QuerySelectorAll(".ts-legend").Length.ShouldBe(1);
+        section.QuerySelector(".ts-entry-label")!.TextContent.ShouldBe("INTERNAL NOTE");
+    }
+
+    [Fact]
+    public void Every_keyboard_convention_of_BRAND_md_has_a_keycap_row()
+    {
+        var keys = Section("sg-keys").QuerySelectorAll("kbd").Select(k => k.TextContent).ToHashSet();
+
+        foreach (var key in new[] { "j", "k", "Enter", "/", "r", "n", "e", "Esc", "Ctrl", "Cmd", "K" })
+        {
+            keys.ShouldContain(key);
+        }
+    }
 }
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test --project tests/TechStrap.Admin.Tests`
Expected: FAIL to compile (`TintedEntry`, `EntryKind`, `Kbd`, `TintLegend` do not exist).

- [ ] **Step 3: Write the components**

```csharp
namespace TechStrap.Admin.Components.Ui;

/// <summary>Who a timeline entry is from. The tint code is fixed to these three kinds and is never reused for anything else (docs/BRAND.md section 12).</summary>
public enum EntryKind
{
    Customer,
    PublicReply,
    InternalNote,
}
```

```razor
<article class="@CssClass">
    <div class="ts-entry-head">
        @if (Kind == EntryKind.InternalNote)
        {
            <span class="ts-entry-label">@InternalNoteLabel</span>
        }
        <strong>@Author</strong>
        @if (Kind != EntryKind.InternalNote)
        {
            <span class="ts-entry-role">@RoleLabel</span>
        }
        <span class="ts-entry-time">@Time</span>
    </div>
    <div class="ts-entry-body">@ChildContent</div>
</article>
```

```csharp
using Microsoft.AspNetCore.Components;

namespace TechStrap.Admin.Components.Ui;

/// <summary>
/// One message in a ticket timeline, drawn with the carbon tint code: white customer message, canary public reply, pink dashed
/// notched internal note. Every kind also carries a word (customer, agent reply, INTERNAL NOTE), so color is never the only cue.
/// </summary>
public partial class TintedEntry
{
    private const string InternalNoteLabel = "INTERNAL NOTE";

    [Parameter, EditorRequired]
    public EntryKind Kind { get; set; }

    [Parameter, EditorRequired]
    public string Author { get; set; } = string.Empty;

    /// <summary>The time as the caller wants it shown (already formatted).</summary>
    [Parameter]
    public string Time { get; set; } = string.Empty;

    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    private string CssClass => Kind switch
    {
        EntryKind.Customer => "ts-entry ts-entry--customer",
        EntryKind.PublicReply => "ts-entry ts-entry--public",
        EntryKind.InternalNote => "ts-entry ts-entry--note",
        _ => throw new ArgumentOutOfRangeException(nameof(Kind), Kind, "Unknown entry kind."),
    };

    private string RoleLabel => Kind == EntryKind.Customer ? "customer" : "agent reply";
}
```

```razor
<div class="ts-legend">
    <span class="ts-legend-customer"><i class="ts-legend-swatch ts-legend-swatch--customer"></i>Customer (white)</span>
    <span class="ts-legend-public"><i class="ts-legend-swatch ts-legend-swatch--public"></i>Public reply (canary)</span>
    <span class="ts-legend-note"><i class="ts-legend-swatch ts-legend-swatch--note"></i>Internal note (pink)</span>
</div>
```

```razor
<kbd class="ts-kbd">@ChildContent</kbd>

@code {
    [Parameter]
    public RenderFragment? ChildContent { get; set; }
}
```

- [ ] **Step 4: Write the Sass**

```scss
// The carbon tint code (BRAND.md section 12). A hard rule: white = customer, canary = public reply, pink + dashed edge + notched
// corner = internal note. These tokens are used here and nowhere else (TintStyleTests scans the styles for that).

.ts-entry {
  position: relative;
  padding: 8px 12px 10px;
  color: var(--ink);
  border: 1px solid var(--rule-strong);
  box-shadow: 2px 2px 0 var(--shadow);
}

.ts-entry-head {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  align-items: baseline;
  margin-bottom: 4px;
  font: 500 .6875rem var(--ts-font-mono);
  letter-spacing: .05em;
  text-transform: uppercase;
  color: var(--ink-2);

  strong {
    font-weight: 600;
    color: var(--ink);
  }
}

.ts-entry-time {
  margin-left: auto;
  letter-spacing: 0;
  text-transform: none;
}

// Message bodies are the long-form reading text: Serif 15/1.55, at most 68 characters wide.
.ts-entry-body {
  font: 400 .9375rem/1.55 var(--ts-font-serif);

  p {
    max-width: 68ch;
    margin: 4px 0;
  }
}

.ts-entry--customer {
  background: var(--sheet);
  border-left: 6px solid var(--ink-3);
}

.ts-entry--public {
  margin-left: 28px;
  background: var(--canary);
  border-left: 6px solid var(--canary-edge);
}

// Pink, dashed edge, notched top-right corner, no shadow: it reads as a cut-out that the customer never sees.
.ts-entry--note {
  margin-left: 28px;
  background: var(--pink);
  border: 2px dashed var(--pink-edge);
  clip-path: polygon(0 0, calc(100% - 18px) 0, 100% 18px, 100% 100%, 0 100%);
  box-shadow: none;

  .ts-entry-head {
    color: var(--note-ink);
  }
}

.ts-entry-label {
  display: inline-block;
  padding: 0 5px;
  font: 600 .6875rem var(--ts-font-mono);
  letter-spacing: .06em;
  color: var(--note-ink);
  border: 2px solid var(--note-ink);
  border-radius: 2px 5px 2px 4px / 4px 2px 5px 2px;
}

// Legend shown under the timeline.
.ts-legend {
  display: flex;
  flex-wrap: wrap;
  gap: 12px;
  margin: 12px 0 0;
  font: 400 .6875rem var(--ts-font-mono);
  color: var(--ink-2);
}

.ts-legend-swatch {
  display: inline-block;
  width: 12px;
  height: 12px;
  margin-right: 4px;
  vertical-align: -2px;
  border: 1px solid var(--rule-strong);
}

.ts-legend-swatch--customer {
  background: var(--sheet);
}

.ts-legend-swatch--public {
  background: var(--canary);
}

.ts-legend-swatch--note {
  background: var(--pink);
  border-style: dashed;
}

@media (max-width: 820px) {
  .ts-entry--public,
  .ts-entry--note {
    margin-left: 8px;
  }
}
```

```scss
// Ledger-ruled keycaps (BRAND.md section 18): Mono 11px 500, square, 1px rule-strong border with a 2px bottom edge, sheet fill.
.ts-kbd {
  display: inline-block;
  padding: 0 4px;
  font: 500 .6875rem/1.5 var(--ts-font-mono);
  white-space: nowrap;
  color: var(--ink);
  background: var(--sheet);
  border: 1px solid var(--rule-strong);
  border-bottom-width: 2px;
  border-radius: 0;
}

// Keys in a chord sit 2px apart.
.ts-kbd + .ts-kbd {
  margin-left: 2px;
}

// Inside a button the keycap is transparent and borrows the button's color.
.btn .ts-kbd {
  margin-left: 4px;
  color: inherit;
  background: transparent;
  border-color: currentcolor;
}
```

Add to `app.scss` after `@import "priority";`:

```scss
@import "tinted-entry";
@import "kbd";
```

Append to `_styleguide.scss`:

```scss

.ts-sg-timeline {
  display: flex;
  flex-direction: column;
  gap: 10px;
}
```

- [ ] **Step 5: Add the guide sections**

Insert both before the `sg-buttons` section of `StyleGuide.razor`:

```razor
        <section class="ts-sg-section" aria-labelledby="sg-tints">
            <h2 id="sg-tints" class="ts-label">Carbon tint code</h2>
            <p class="ts-sg-note">White is the customer, canary is a public reply (the customer sees it), pink with a dashed edge and a notched corner is an internal note (the customer never sees it). The code is never reused for anything else.</p>
            <div class="ts-sg-timeline">
                <TintedEntry Kind="EntryKind.Customer" Author="Dana Whitfield" Time="08:41">
                    <p>Since upgrading to 2.4 on Tuesday our nightly backup job exits with code 17 and no files are written. We have 40 workstations affected.</p>
                </TintedEntry>
                <TintedEntry Kind="EntryKind.InternalNote" Author="Priya Rao" Time="09:02">
                    <p>Exit 17 is the new VSS snapshot failure. The 2.4 installer drops the old service account. Checking with dev before I promise a fix.</p>
                </TintedEntry>
                <TintedEntry Kind="EntryKind.PublicReply" Author="Sam Marsh" Time="09:08">
                    <p>Hi Dana, thanks for the log. We can reproduce this on 2.4 and are checking the fix. As a workaround, re-run the service account repair from the Settings page.</p>
                </TintedEntry>
            </div>
            <TintLegend />
        </section>
```

```razor
        <section class="ts-sg-section" aria-labelledby="sg-keys">
            <h2 id="sg-keys" class="ts-label">Keyboard</h2>
            <p class="ts-sg-note">Single-key shortcuts never fire while typing. Every shortcut also has a command palette entry (PHASE-07).</p>
            <table class="table ts-sg-table">
                <thead><tr><th scope="col">Key</th><th scope="col">Context</th><th scope="col">Action</th></tr></thead>
                <tbody>
                    <tr><td><Kbd>j</Kbd> / <Kbd>ArrowDown</Kbd></td><td>Queue</td><td>Select next row</td></tr>
                    <tr><td><Kbd>k</Kbd> / <Kbd>ArrowUp</Kbd></td><td>Queue</td><td>Select previous row</td></tr>
                    <tr><td><Kbd>Enter</Kbd></td><td>Queue</td><td>Open the selected ticket</td></tr>
                    <tr><td><Kbd>/</Kbd></td><td>Queue, ticket</td><td>Focus the queue search</td></tr>
                    <tr><td><Kbd>r</Kbd></td><td>Ticket</td><td>Reply to the customer</td></tr>
                    <tr><td><Kbd>n</Kbd></td><td>Ticket</td><td>Add an internal note</td></tr>
                    <tr><td><Kbd>e</Kbd></td><td>Ticket</td><td>Focus the assignee control</td></tr>
                    <tr><td><Kbd>Esc</Kbd></td><td>Ticket, in a field</td><td>Back to the queue, or leave the field</td></tr>
                    <tr><td><Kbd>Ctrl</Kbd><Kbd>Enter</Kbd></td><td>In the composer</td><td>Send reply or add note</td></tr>
                    <tr><td><Kbd>Ctrl</Kbd><Kbd>K</Kbd> / <Kbd>Cmd</Kbd><Kbd>K</Kbd></td><td>Anywhere</td><td>Open or close the command palette</td></tr>
                </tbody>
            </table>
            <div class="ts-sg-row">
                <button type="button" class="btn btn-primary">Send reply <Kbd>Ctrl</Kbd><Kbd>Enter</Kbd></button>
                <button type="button" class="btn btn-secondary">Cancel <Kbd>Esc</Kbd></button>
            </div>
        </section>
```

- [ ] **Step 6: Run to verify they pass**

Run: `dotnet test --project tests/TechStrap.Admin.Tests`
Expected: PASS (`total: 206`). If `Internal_note_is_pink_dashed_notched_and_flat` fails on the `clip-path` string, the compiled CSS keeps spaces after commas inside `polygon(...)`; the test expects `polygon(0 0, calc(100% - 18px) 0, 100% 18px, 100% 100%, 0 100%)`.

- [ ] **Step 7: Commit**

```bash
git add src/TechStrap.Admin tests/TechStrap.Admin.Tests
git commit -m "feat(admin): add TintedEntry, TintLegend and Kbd with tint-code guards" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 9: Retro window and the branded 404

**Files:**
- Create: `src/TechStrap.Admin/Components/Ui/BrandWindow.razor`, `src/TechStrap.Admin/Styles/_brand-window.scss`
- Modify: `src/TechStrap.Admin/Components/Pages/NotFound.razor`, `src/TechStrap.Admin/Components/Pages/StyleGuide.razor`, `src/TechStrap.Admin/Program.cs`, `src/TechStrap.Admin/Styles/app.scss`, `src/TechStrap.Admin/Styles/_styleguide.scss`, `tests/TechStrap.Admin.Tests/StyleGuideContentTests.cs`
- Test: `tests/TechStrap.Admin.Tests/Components/BrandWindowTests.cs`, `BrandWindowStyleTests.cs`, `NotFoundHostTests.cs`

**Interfaces:**
- Consumes: `brand/mark.svg` (Task 5), `MainLayout` (Task 6), the `--bm-*` tokens.
- Produces: `<BrandWindow Title="string" Heading="string">copy and one action</BrandWindow>` (inline; `article.ts-window[aria-label=Heading]`, `.ts-window-titlebar` with `i.ts-window-led[aria-hidden]` and `.ts-window-title`, `.ts-window-body` with the 96px head mark, `h2`, content); helper classes `.ts-window-action`, `.ts-window-button`, `.ts-window-link`, `.ts-window-fine`, `.ts-window-page`. The Admin 404 now uses it, and unknown addresses are re-executed to `/not-found` with the 404 status kept.

**404 finding (verified):** an address that matches no endpoint never reaches the Blazor router, so `NotFoundPage` alone returned an empty 404 body. `app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true)` (the pattern in the `SyntaxCircus.Blazor.Components` README, section 3) fixes it and keeps status 404. Admin has no JSON endpoints, so an HTML 404 body is fine there.

- [ ] **Step 1: Write the failing tests**

```csharp
using Bunit;
using TechStrap.Admin.Components.Ui;

namespace TechStrap.Admin.Tests.Components;

/// <summary>The retro window frame of docs/BRAND.md section 18: beige plate, title bar with a LED and a short title, the head mark, one wink, one way back. No fake OS chrome.</summary>
public sealed class BrandWindowTests : BunitContext
{
    private IRenderedComponent<BrandWindow> RenderWindow() =>
        Render<BrandWindow>(p => p
            .Add(w => w.Title, "queue.exe — 0 items")
            .Add(w => w.Heading, "All caught up")
            .AddChildContent("<p>Zero tickets, fully supported.</p><a class=\"ts-window-link\" href=\"/\">View open tickets</a>"));

    [Fact]
    public void Shows_title_bar_heading_head_mark_and_the_content()
    {
        var cut = RenderWindow();

        cut.Find("article.ts-window").GetAttribute("aria-label").ShouldBe("All caught up");
        cut.Find(".ts-window-title").TextContent.ShouldBe("queue.exe — 0 items");
        cut.Find(".ts-window-led").GetAttribute("aria-hidden").ShouldBe("true");
        cut.Find(".ts-window-body h2").TextContent.ShouldBe("All caught up");
        cut.Find(".ts-window-body p").TextContent.ShouldBe("Zero tickets, fully supported.");
        cut.Find(".ts-window-link").TextContent.ShouldBe("View open tickets");
    }

    [Fact]
    public void The_mascot_is_the_96px_SVG_head_mark_and_decorative()
    {
        var mark = RenderWindow().Find(".ts-window-body img");

        mark.GetAttribute("src").ShouldBe("brand/mark.svg");
        mark.GetAttribute("alt").ShouldBe(string.Empty);
        mark.GetAttribute("width").ShouldBe("96");
        mark.GetAttribute("height").ShouldBe("96");
    }

    [Fact]
    public void The_title_bar_has_no_close_minimise_or_maximise_buttons()
    {
        var cut = RenderWindow();

        cut.FindAll(".ts-window-titlebar button, .ts-window-titlebar a, .ts-window-titlebar [role=button]").ShouldBeEmpty();
    }
}
```

```csharp
using TechStrap.Tests.Shared;

namespace TechStrap.Admin.Tests;

public sealed class BrandWindowStyleTests
{
    private static readonly CompiledCss Css = CompiledCss.Load("TechStrap.Admin");

    [Fact]
    public void The_window_is_a_beige_plate_with_a_2px_edge_a_hard_shadow_and_square_corners()
    {
        var window = Css.Declarations(".ts-window");

        window["background"].ShouldBe("var(--bm-plate)");
        window["border"].ShouldBe("2px solid var(--bm-edge)");
        window["box-shadow"].ShouldBe("3px 3px 0 var(--bm-shadow)");
        window.ContainsKey("border-radius").ShouldBeFalse();
        window["max-width"].ShouldBe("400px");
    }

    [Fact]
    public void The_title_bar_is_navy_mono_with_a_green_LED()
    {
        Css.Declarations(".ts-window-titlebar")["background"].ShouldBe("var(--bm-bar)");
        Css.Declarations(".ts-window-titlebar")["color"].ShouldBe("var(--bm-on-bar)");
        Css.Declarations(".ts-window-led")["background"].ShouldBe("var(--bm-led)");
        Css.Declarations(".ts-window-led")["border-radius"].ShouldBe("50%");
    }

    [Fact]
    public void The_button_is_CRT_blue_and_presses_in_by_one_pixel()
    {
        Css.Declarations(".ts-window-button")["background"].ShouldBe("var(--bm-crt)");
        Css.Declarations(".ts-window-button:active")["transform"].ShouldBe("translate(1px, 1px)");
    }

    [Fact]
    public void The_window_shrinks_to_the_viewport_and_never_overflows_at_320px()
    {
        Css.Declarations(".ts-window")["width"].ShouldBe("100%");
        Css.Declarations(".ts-window-body")["min-width"].ShouldBe("0");
        Css.Declarations(".ts-window-body p")["overflow-wrap"].ShouldBe("anywhere");
    }
}
```

```csharp
using System.Net;

namespace TechStrap.Admin.Tests;

public sealed class NotFoundHostTests
{
    [Fact]
    public async Task An_unknown_address_returns_404_with_the_retro_window_and_a_way_back()
    {
        await using var factory = new AdminFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/no-such-page", TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        html.ShouldContain("ts-window");
        html.ShouldContain("ERROR 404");
        html.ShouldContain("This page fell out of its strap.");
        html.ShouldContain("Back to the queue");
        html.ShouldContain("ts-shell");
    }
}
```

Extend the guide content tests:

```diff
@@ -35,4 +35,5 @@ public sealed class StyleGuideContentTests : IAsyncLifetime
     [InlineData("sg-tints")]
     [InlineData("sg-keys")]
+    [InlineData("sg-windows")]
     public void Section_is_present(string id)
     {
@@ -88,3 +89,24 @@ public sealed class StyleGuideContentTests : IAsyncLifetime
         }
     }
+
+    [Fact]
+    public void The_three_brand_moment_windows_show_with_their_titles_and_one_action_each()
+    {
+        var windows = Section("sg-windows").QuerySelectorAll(".ts-window");
+
+        windows.Select(w => w.QuerySelector(".ts-window-title")!.TextContent)
+            .ShouldBe(["queue.exe — 0 items", "techstrap — sign in", "ERROR 404 — not found"]);
+        foreach (var window in windows)
+        {
+            window.QuerySelectorAll(".ts-window-button, .ts-window-link").Length.ShouldBe(1, window.GetAttribute("aria-label"));
+        }
+    }
+
+    [Fact]
+    public void The_logo_wordmark_and_head_mark_are_shown_from_the_SVG_files()
+    {
+        var sources = Section("sg-windows").QuerySelectorAll(".ts-sg-logos img").Select(i => i.GetAttribute("src")).ToList();
+
+        sources.ShouldBe(["brand/logo.svg", "brand/mark.svg", "brand/wordmark.svg"]);
+    }
 }
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test --project tests/TechStrap.Admin.Tests`
Expected: FAIL to compile (`BrandWindow` does not exist).

- [ ] **Step 3: Write the component, Sass and 404**

```razor
<article class="ts-window" aria-label="@Heading">
    <div class="ts-window-titlebar">
        <i class="ts-window-led" aria-hidden="true"></i>
        <span class="ts-window-title">@Title</span>
    </div>
    <div class="ts-window-body">
        <img src="brand/mark.svg" alt="" width="96" height="96" />
        <h2>@Heading</h2>
        @ChildContent
    </div>
</article>

@code {
    /// <summary>The short title-bar text, for example "queue.exe — 0 items".</summary>
    [Parameter, EditorRequired]
    public string Title { get; set; } = string.Empty;

    [Parameter, EditorRequired]
    public string Heading { get; set; } = string.Empty;

    /// <summary>The copy and the single action: one sentence per line, then one button or one link.</summary>
    [Parameter]
    public RenderFragment? ChildContent { get; set; }
}
```

```scss
// The retro window (BRAND.md section 18). Allowed only on Admin all-caught-up, sign-in, 404 and the style guide. The --bm-* tokens are
// valid inside this window and nowhere else (TintStyleTests scans the styles for that). No close, minimize or maximize buttons,
// no bevels, no other fake OS chrome.

.ts-window {
  display: flex;
  flex-direction: column;
  width: 100%;
  min-width: 0;
  max-width: 400px;
  color: var(--bm-text);
  background: var(--bm-plate);
  border: 2px solid var(--bm-edge);
  box-shadow: 3px 3px 0 var(--bm-shadow);
}

.ts-window-titlebar {
  display: flex;
  gap: 10px;
  align-items: center;
  min-width: 0;
  padding: 4px 10px;
  font: 600 .75rem var(--ts-font-mono);
  color: var(--bm-on-bar);
  background: var(--bm-bar);
}

.ts-window-title {
  flex: 1;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.ts-window-led {
  display: inline-block;
  flex: none;
  width: 9px;
  height: 9px;
  background: var(--bm-led);
  border: 1.5px solid var(--bm-on-bar);
  border-radius: 50%;
}

.ts-window-body {
  display: flex;
  flex: 1;
  flex-direction: column;
  gap: 8px;
  align-items: center;
  min-width: 0;
  padding: 22px 20px 24px;
  text-align: center;

  img {
    width: 96px;
    height: 96px;
  }

  h2 {
    margin: 4px 0 0;
    font: 600 1.1875rem/1.2 var(--ts-font-mono);
  }

  p {
    max-width: 34ch;
    margin: 0;
    font: 400 .9375rem/1.5 var(--ts-font-serif);
    overflow-wrap: anywhere;
  }

  .ts-window-fine {
    font: 400 .75rem/1.4 var(--ts-font-mono);
    color: var(--bm-text2);
  }
}

.ts-window-action {
  margin-top: auto;
  padding-top: 10px;
}

.ts-window-button {
  max-width: 100%;
  min-height: 40px;
  padding: 8px 14px;
  font: 600 .75rem var(--ts-font-mono);
  letter-spacing: .05em;
  text-transform: uppercase;
  color: var(--bm-on-crt);
  background: var(--bm-crt);
  border: 2px solid var(--bm-edge);
  box-shadow: 2px 2px 0 var(--bm-shadow);
  cursor: pointer;

  &:active {
    transform: translate(1px, 1px);
    box-shadow: 1px 1px 0 var(--bm-shadow);
  }
}

.ts-window-link {
  display: inline-block;
  padding: 8px 2px;
  font: 600 .8125rem var(--ts-font-mono);
  color: var(--bm-text);
  text-decoration: underline;
  text-underline-offset: 3px;
}

// The page that centers one window on the plain background (404, all caught up).
.ts-window-page {
  display: grid;
  place-items: center;
  padding: 40px 16px 60px;
}
```

```razor
@page "/not-found"
@layout MainLayout

<PageTitle>Not found</PageTitle>
<div class="ts-window-page">
    <BrandWindow Title="ERROR 404 — not found" Heading="This page fell out of its strap.">
        <p>The address you followed doesn't match any page here. It may have moved, or the link may have a typo.</p>
        <div class="ts-window-action"><a class="ts-window-link" href="/">Back to the queue</a></div>
    </BrandWindow>
</div>
```

Add to `app.scss` after `@import "kbd";`: `@import "brand-window";`. Re-execute unmatched addresses in `Program.cs`:

```diff
@@ -49,4 +49,6 @@ telemetry.LogStartupWarning(app.Logger);
 app.UseForwardedHeaders();
 app.UseCorrelationId();
+// An address that matches no page gets the branded 404 (re-executed, so the 404 status code is kept).
+app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
 app.UseAntiforgery();
 app.MapStandardHealthChecks();
```

- [ ] **Step 4: Add the windows and logos to the style guide**

Insert before the `sg-buttons` section of `StyleGuide.razor`:

```razor
        <section class="ts-sg-section" aria-labelledby="sg-windows">
            <h2 id="sg-windows" class="ts-label">Brand moments</h2>
            <p class="ts-sg-note">The retro window and the mascot appear only on all caught up, sign-in and 404, and here. One wink per window; the explanation is plain; there is always a way back.</p>
            <div class="ts-sg-windows">
                <BrandWindow Title="queue.exe — 0 items" Heading="All caught up">
                    <p>Zero tickets, fully supported.</p>
                    <div class="ts-window-action"><button type="button" class="ts-window-button">View open tickets</button></div>
                </BrandWindow>
                <BrandWindow Title="techstrap — sign in" Heading="Agent sign-in">
                    <p class="ts-window-fine">Single sign-on through your company's Authentik. No passwords are entered here.</p>
                    <div class="ts-window-action"><button type="button" class="ts-window-button">Sign in with Authentik</button></div>
                    <p class="ts-window-fine">Agents only. Customers: use your product's support page.</p>
                </BrandWindow>
                <BrandWindow Title="ERROR 404 — not found" Heading="This page fell out of its strap.">
                    <p>The address you followed doesn't match any page here. It may have moved, or the link may have a typo.</p>
                    <div class="ts-window-action"><a class="ts-window-link" href="/">Back to the queue</a></div>
                </BrandWindow>
            </div>
            <h3 class="ts-sg-sub">Logo and wordmark (provisional SVGs)</h3>
            <div class="ts-sg-row ts-sg-logos">
                <img src="brand/logo.svg" alt="TechStrap mascot, full figure" height="160" />
                <img src="brand/mark.svg" alt="TechStrap mascot head mark" height="72" />
                <img class="ts-sg-wordmark" src="brand/wordmark.svg" alt="TechStrap wordmark" height="28" />
            </div>
        </section>
```

Append to `_styleguide.scss` (the mascot's navy outline vanishes on the dark paper, so logos sit on a neutral chip; the outlined wordmark is black and is inverted in dark):

```diff
@@ -121,2 +121,39 @@
   gap: 10px;
 }
+
+.ts-sg-windows {
+  display: grid;
+  grid-template-columns: repeat(3, minmax(0, 1fr));
+  gap: 20px;
+  align-items: stretch;
+  margin-top: 12px;
+
+  .ts-window {
+    max-width: none;
+  }
+}
+
+// The mascot's navy outline disappears on the dark paper, so logos sit on a neutral white chip (the portal background,
+// which does not change with the theme). The outlined wordmark is black by default and is inverted in dark.
+.ts-sg-logos {
+  gap: 20px;
+
+  img {
+    padding: 8px;
+    background: var(--p-bg);
+    border: 1px solid var(--rule-strong);
+  }
+}
+
+@include color-mode(dark) {
+  .ts-sg-wordmark {
+    filter: invert(1);
+    background: transparent;
+  }
+}
+
+@media (max-width: 760px) {
+  .ts-sg-windows {
+    grid-template-columns: minmax(0, 1fr);
+  }
+}
```

- [ ] **Step 5: Run to verify they pass**

Run: `dotnet test --project tests/TechStrap.Admin.Tests`
Expected: PASS (`total: 217`).

- [ ] **Step 6: Commit**

```bash
git add src/TechStrap.Admin tests/TechStrap.Admin.Tests
git commit -m "feat(admin): add the BrandWindow retro frame and the branded 404" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 10: Reconnect and error UI

**Files:**
- Create: `src/TechStrap.Admin/Components/Ui/UiCopy.cs`, `AppReconnectModal.razor`
- Modify: `src/TechStrap.Admin/Components/App.razor`, `Components/Layout/MainLayout.razor`, `Components/Pages/StyleGuide.razor`, `Styles/_feedback.scss`
- Test: `tests/TechStrap.Admin.Tests/ReconnectAndErrorTests.cs`

**Interfaces:**
- Consumes: `SyntaxCircus.Blazor.Components.Feedback.ReconnectModal` and `GlobalErrorView` (verified against package 0.1.3, same text as the source README):
  - `ReconnectModal` renders `<dialog id="components-reconnect-modal" class="@CssClass">` (only one may exist per page), loads its own module script through `@Assets[...]` (so it must be rendered from a component that has `Assets`, such as `App.razor`), and its JavaScript toggles `hidden` on `[data-reconnect-state]` blocks (`first retrying`, `first`, `retrying`, `failed`, `paused`, `resume-failed`) and needs buttons carrying `data-reconnect-action="retry"` or `"resume"`. Parameters used: `CssClass`, `FirstAttemptContent`, `RetryingContent` (must keep `id="components-seconds-to-next-attempt"`), `FailedContent`, `PausedContent`, `ResumeFailedContent`, `RetryActionContent`, `ResumeActionContent`.
  - `GlobalErrorView` parameters used: `CssClass`, `Title`, `Description`, `HomeLabel`.
- Produces: `UiCopy` string constants (`ReconnectFirst`, `ReconnectRetryingBefore/After`, `ReconnectFailed`, `ReconnectPaused`, `ReconnectResumeFailed`, `RetryLabel`, `ResumeLabel`, `ErrorTitle`, `ErrorDescription`, `ErrorHomeLabel`); `<AppReconnectModal />` mounted once in `App.razor`; classes `.ts-reconnect`, `.ts-reconnect--preview`, `.ts-error`.

**Why a static preview:** the real dialog is closed until a circuit drops and its id must be unique, so the style guide renders the five states as static markup with the same copy and the same `.ts-reconnect` rules, and a test asserts that only the real dialog carries the id.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Net;
using AngleSharp.Html.Parser;
using TechStrap.Tests.Shared;

namespace TechStrap.Admin.Tests;

/// <summary>
/// Admin is Blazor Server, so a lost circuit is a normal event. The SyntaxCircus.Blazor.Components reconnect dialog is mounted once in
/// App.razor, styled with brand tokens, with plain copy (no humor on a blocking error: BRAND.md section 3).
/// </summary>
public sealed class ReconnectAndErrorTests
{
    private static readonly CompiledCss Css = CompiledCss.Load("TechStrap.Admin");

    [Fact]
    public async Task Every_page_mounts_exactly_one_styled_reconnect_dialog_with_plain_copy()
    {
        await using var factory = new AdminFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/", TestContext.Current.CancellationToken);
        var page = await new HtmlParser().ParseDocumentAsync(html, TestContext.Current.CancellationToken);

        var dialogs = page.QuerySelectorAll("dialog#components-reconnect-modal");
        dialogs.Length.ShouldBe(1);
        dialogs[0].ClassList.ShouldContain("ts-reconnect");
        dialogs[0].TextContent.ShouldContain("Connection lost. Reconnecting");
        dialogs[0].TextContent.ShouldContain("Your changes are still here");
        dialogs[0].QuerySelectorAll("[data-reconnect-action=retry]").Length.ShouldBe(1);
    }

    [Fact]
    public async Task The_reconnect_script_of_the_package_is_served()
    {
        await using var factory = new AdminFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/_content/SyntaxCircus.Blazor.Components/Components/Feedback/ReconnectModal.razor.js", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public void The_dialog_and_the_error_view_use_brand_tokens_and_a_scrim_backdrop()
    {
        Css.Declarations(".ts-reconnect")["background"].ShouldBe("var(--sheet)");
        Css.Declarations(".ts-reconnect")["border"].ShouldBe("2px solid var(--ink)");
        Css.Declarations(".ts-reconnect::backdrop")["background"].ShouldBe("var(--scrim)");
        Css.Declarations(".ts-error")["background"].ShouldBe("var(--sheet)");
        Css.Declarations(".ts-error")["border"].ShouldBe("2px solid var(--st-spam)");
    }

    [Fact]
    public async Task The_style_guide_previews_every_reconnect_state_and_the_error_view_without_a_second_dialog()
    {
        await using var factory = new AdminFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/_styleguide", TestContext.Current.CancellationToken);
        var page = await new HtmlParser().ParseDocumentAsync(html, TestContext.Current.CancellationToken);

        var section = page.QuerySelector("section[aria-labelledby='sg-reconnect']");
        section.ShouldNotBeNull();
        var states = section.QuerySelectorAll(".ts-reconnect--preview [data-reconnect-state]").Select(s => s.GetAttribute("data-reconnect-state")).ToList();
        states.ShouldBe(["first", "retrying", "failed", "paused", "resume-failed"]);
        section.QuerySelectorAll("section.ts-error[role=alert]").Length.ShouldBe(1);
        page.QuerySelectorAll("#components-reconnect-modal").Length.ShouldBe(1, "only the real dialog may carry the id");
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test --project tests/TechStrap.Admin.Tests --filter-class "*ReconnectAndErrorTests"`
Expected: FAIL (no `dialog#components-reconnect-modal` on the page, no `sg-reconnect` section, no `.ts-reconnect` rules).

- [ ] **Step 3: Write the copy, the wrapper and the wiring**

```csharp
namespace TechStrap.Admin.Components.Ui;

/// <summary>
/// Copy for the blocking-failure states, defined once so the running app and the style guide cannot drift apart.
/// Voice rules (docs/BRAND.md section 3): plain cause plus next step, sentence case, no humor, no exclamation marks.
/// </summary>
public static class UiCopy
{
    public const string ReconnectFirst = "Connection lost. Reconnecting...";
    public const string ReconnectRetryingBefore = "Still offline. Trying again in ";
    public const string ReconnectRetryingAfter = " seconds.";
    public const string ReconnectFailed = "Could not reconnect. Your changes are still here; retry, or reload the page.";
    public const string ReconnectPaused = "This session was paused by the server.";
    public const string ReconnectResumeFailed = "Could not resume the session. Retry, or reload the page.";
    public const string RetryLabel = "Retry";
    public const string ResumeLabel = "Resume";

    public const string ErrorTitle = "Couldn't load this screen.";
    public const string ErrorDescription = "Something went wrong while drawing it. Try again, and tell an admin if it keeps happening.";
    public const string ErrorHomeLabel = "Back to the start";
}
```

```razor
<ReconnectModal CssClass="ts-reconnect">
    <FirstAttemptContent>
        <p>@UiCopy.ReconnectFirst</p>
    </FirstAttemptContent>
    <RetryingContent>
        <p>@UiCopy.ReconnectRetryingBefore<span id="components-seconds-to-next-attempt"></span>@UiCopy.ReconnectRetryingAfter</p>
    </RetryingContent>
    <FailedContent>
        <p>@UiCopy.ReconnectFailed</p>
    </FailedContent>
    <PausedContent>
        <p>@UiCopy.ReconnectPaused</p>
    </PausedContent>
    <ResumeFailedContent>
        <p>@UiCopy.ReconnectResumeFailed</p>
    </ResumeFailedContent>
    <RetryActionContent>
        <button type="button" data-reconnect-action="retry">@UiCopy.RetryLabel</button>
    </RetryActionContent>
    <ResumeActionContent>
        <button type="button" data-reconnect-action="resume">@UiCopy.ResumeLabel</button>
    </ResumeActionContent>
</ReconnectModal>
```

```razor
@inherits LayoutComponentBase

<div class="ts-shell">
    <nav class="ts-rail" aria-label="Admin navigation">
        <a class="ts-brand" href="/">
            <img src="brand/mark.svg" alt="" width="36" height="36" />
            <span>TechStrap<small>Call log / admin</small></span>
        </a>
        <a class="ts-rail-link" href="/">Home</a>
    </nav>
    <main class="ts-main">
        <GlobalErrorBoundary BoundaryName="admin UI"
                             CssClass="ts-error"
                             Title="@UiCopy.ErrorTitle"
                             Description="@UiCopy.ErrorDescription"
                             HomeLabel="@UiCopy.ErrorHomeLabel">
            @Body
        </GlobalErrorBoundary>
    </main>
</div>
```

Mount the dialog once, before the Blazor script, in `Components/App.razor`:

```razor
    <Routes />
    <AppReconnectModal />
    <script src="@Assets["_framework/blazor.web.js"]"></script>
```

- [ ] **Step 4: Style the dialog and add the guide section**

Append to `_feedback.scss` (the package toggles `hidden` on the state blocks, so these rules never set `display` on them):

```diff
@@ -37,2 +37,50 @@
   }
 }
+
+// The reconnect dialog from SyntaxCircus.Blazor.Components (CssClass="ts-reconnect"). The package toggles the hidden attribute on
+// [data-reconnect-state] blocks, so these rules must not set display on them. The static preview in the style guide reuses the class.
+.ts-reconnect {
+  max-width: 420px;
+  padding: 16px;
+  color: var(--ink);
+  background: var(--sheet);
+  border: 2px solid var(--ink);
+  box-shadow: 4px 4px 0 var(--shadow);
+
+  p {
+    margin: 0 0 12px;
+  }
+
+  button {
+    min-height: 34px;
+    padding: 6px 14px;
+    font: 600 .75rem var(--ts-font-mono);
+    letter-spacing: .05em;
+    text-transform: uppercase;
+    color: var(--paper);
+    background: var(--ink);
+    border: 2px solid var(--ink);
+    border-radius: 0;
+  }
+
+  &::backdrop {
+    background: var(--scrim);
+  }
+}
+
+// Style-guide preview: every state shown at once inside one sheet.
+.ts-reconnect--preview {
+  display: flex;
+  flex-direction: column;
+  gap: 12px;
+
+  [data-reconnect-state] {
+    padding-bottom: 12px;
+    border-bottom: 1px dashed var(--rule-strong);
+  }
+
+  [data-reconnect-state]:last-child {
+    padding-bottom: 0;
+    border-bottom: 0;
+  }
+}
```

Insert before the `sg-buttons` section of `StyleGuide.razor`:

```razor
        <section class="ts-sg-section" aria-labelledby="sg-reconnect">
            <h2 id="sg-reconnect" class="ts-label">Reconnect and error UI</h2>
            <p class="ts-sg-note">The real reconnect dialog (SyntaxCircus.Blazor.Components) is mounted once in App.razor and shows when the circuit drops; here each state is previewed as static markup with the same copy. Blocking failures are plain: a cause and a next step.</p>
            <div class="ts-sg-states">
                <div class="ts-reconnect ts-reconnect--preview">
                    <div data-reconnect-state="first"><p>@UiCopy.ReconnectFirst</p></div>
                    <div data-reconnect-state="retrying"><p>@UiCopy.ReconnectRetryingBefore<span>5</span>@UiCopy.ReconnectRetryingAfter</p></div>
                    <div data-reconnect-state="failed"><p>@UiCopy.ReconnectFailed</p><button type="button" data-reconnect-action="retry">@UiCopy.RetryLabel</button></div>
                    <div data-reconnect-state="paused"><p>@UiCopy.ReconnectPaused</p><button type="button" data-reconnect-action="resume">@UiCopy.ResumeLabel</button></div>
                    <div data-reconnect-state="resume-failed"><p>@UiCopy.ReconnectResumeFailed</p><button type="button" data-reconnect-action="resume">@UiCopy.ResumeLabel</button></div>
                </div>
                <GlobalErrorView CssClass="ts-error"
                                 Title="@UiCopy.ErrorTitle"
                                 Description="@UiCopy.ErrorDescription"
                                 HomeLabel="@UiCopy.ErrorHomeLabel" />
            </div>
        </section>
```

- [ ] **Step 5: Run to verify they pass**

Run: `dotnet test --project tests/TechStrap.Admin.Tests`
Expected: PASS (`total: 221`).

- [ ] **Step 6: Run the Admin and look at it**

Run (PowerShell, leave it running): `$env:ASPNETCORE_ENVIRONMENT='Development'; dotnet run --project src/TechStrap.Admin --urls http://127.0.0.1:5090`
Then capture the guide (headless Edge picks Auto dark from the OS; add `<html data-bs-theme="light">` through the browser console to compare):
`& "C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe" --headless --disable-gpu --hide-scrollbars --virtual-time-budget=4000 --screenshot="$env:TEMP\admin-sg.png" --window-size=1280,5200 http://127.0.0.1:5090/_styleguide`
Expected: a 1280 wide capture showing every section; stop the app with Ctrl+C afterwards. (The scratch run rendered all sections in the dark Auto theme: stamps, the three tinted entries, the three windows, the reconnect states and the error view.)

- [ ] **Step 7: Commit**

```bash
git add src/TechStrap.Admin tests/TechStrap.Admin.Tests
git commit -m "feat(admin): mount and style the reconnect dialog and error view, preview them in the style guide" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 11: Portal layout, product accent scope, Powered-by footer and style guide

**Files:**
- Create: `src/TechStrap.Portal/Components/Ui/AccentScope.razor`, `AccentScope.razor.cs`, `PoweredByOptions.cs`, `PoweredByFooter.razor`, `PoweredByFooter.razor.cs`, `DevelopmentOnly.razor`, `DevelopmentOnly.razor.cs`
- Create: `src/TechStrap.Portal/Components/Layout/PortalLayout.razor`, `Components/Pages/StyleGuide.razor`, `Styles/_components.scss`
- Modify: `src/TechStrap.Portal/Components/_Imports.razor`, `Routes.razor`, `Pages/Home.razor`, `Pages/NotFound.razor`, `Program.cs`, `Styles/app.scss`
- Test: `tests/TechStrap.Portal.Tests/Components/AccentScopeTests.cs`, `Components/PoweredByFooterTests.cs`, `PortalHostTests.cs`

**Interfaces:**
- Consumes: `ProductAccent.TryDerive` (Task 2), `PortalFactory` (Task 4), `brand/mark.svg` in the Portal wwwroot (Task 5), the Portal Sass of Task 3 (`.ts-accent-scope`, `--ts-*` hooks).
- Produces (PHASE-09 reuses all of it):
  - `<AccentScope Accent="#RRGGBB">content</AccentScope>`: `div.ts-accent-scope` whose `style` is exactly `--ts-accent:#..;--ts-on-accent:#..;--ts-accent-ink:#..` when the accent is valid, and has no `style` at all otherwise (fallbacks apply). Only values returned by `ProductAccent.TryDerive` can reach the attribute, so a hostile string cannot inject CSS.
  - `PoweredByOptions { bool Show = true }` with `const string ConfigurationKey = "TECHSTRAP_PORTAL_SHOW_POWERED_BY"`, bound in `Program.cs`; `<PoweredByFooter />` renders `footer.ts-powered` with the 16px head mark and a plain link "TechStrap" to `https://github.com/Syntax-Circus/techstrap`, or nothing when `Show` is false.
  - `PortalLayout` (`main.ts-portal-main` plus the footer), route `/_styleguide` (Development only; sections `sg-type`, `sg-palette`, `sg-accents` with the five BRAND.md sample accents, `sg-forms`, `sg-states`, `sg-footer`).
- No stamps, tints, windows, mascot, keycaps, hard shadows or `--bm-*` tokens in the Portal; `PortalHostTests` asserts the guide contains no `ts-window`, and Task 3's `StyleBuildTests` asserts no Admin token is emitted.

- [ ] **Step 1: Write the failing tests**

```csharp
using Bunit;
using TechStrap.Portal.Components.Ui;

namespace TechStrap.Portal.Tests.Components;

public sealed class AccentScopeTests : BunitContext
{
    [Theory]
    [InlineData("#F59E0B", "--ts-accent:#F59E0B;--ts-on-accent:#000000;--ts-accent-ink:#9D6507")]
    [InlineData("#7c3aed", "--ts-accent:#7C3AED;--ts-on-accent:#FFFFFF;--ts-accent-ink:#7C3AED")]
    public void Sets_exactly_the_three_derived_properties(string accent, string expectedStyle)
    {
        var cut = Render<AccentScope>(p => p.Add(s => s.Accent, accent).AddChildContent("<p>x</p>"));

        cut.Find("div.ts-accent-scope").GetAttribute("style").ShouldBe(expectedStyle);
        cut.Find("div.ts-accent-scope p").TextContent.ShouldBe("x");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("red")]
    [InlineData("#123456;background:url(x)")]
    public void A_malformed_accent_sets_no_style_so_the_fallbacks_apply_and_nothing_is_injected(string? accent)
    {
        var cut = Render<AccentScope>(p => p.Add(s => s.Accent, accent).AddChildContent("<p>x</p>"));

        cut.Find("div.ts-accent-scope").HasAttribute("style").ShouldBeFalse();
    }
}
```

```csharp
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TechStrap.Portal.Components.Ui;

namespace TechStrap.Portal.Tests.Components;

public sealed class PoweredByFooterTests : BunitContext
{
    [Fact]
    public void Shows_a_plain_link_to_the_GitHub_repository_with_the_16px_head_mark_by_default()
    {
        Services.AddSingleton(Options.Create(new PoweredByOptions()));

        var cut = Render<PoweredByFooter>();

        var link = cut.Find("footer.ts-powered a");
        link.GetAttribute("href").ShouldBe("https://github.com/Syntax-Circus/techstrap");
        link.TextContent.ShouldBe("TechStrap");
        cut.Find("footer.ts-powered").TextContent.Trim().ShouldBe("Powered by TechStrap");
        var mark = cut.Find("footer.ts-powered img");
        mark.GetAttribute("src").ShouldBe("brand/mark.svg");
        mark.GetAttribute("width").ShouldBe("16");
        mark.GetAttribute("alt").ShouldBe(string.Empty);
    }

    [Fact]
    public void Renders_nothing_when_the_installation_setting_hides_it()
    {
        Services.AddSingleton(Options.Create(new PoweredByOptions { Show = false }));

        Render<PoweredByFooter>().Markup.Trim().ShouldBeEmpty();
    }
}
```

```csharp
using System.Net;
using Microsoft.AspNetCore.Hosting;

namespace TechStrap.Portal.Tests;

public sealed class PortalHostTests
{
    [Fact]
    public async Task Style_guide_returns_200_in_Development_with_the_footer_and_every_sample_accent()
    {
        await using var factory = new PortalFactory("Development");
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/_styleguide", TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain("TechStrap portal style guide");
        html.ShouldContain("ts-powered");
        html.ShouldContain("--ts-accent:#F59E0B;--ts-on-accent:#000000;--ts-accent-ink:#9D6507");
        html.ShouldContain("--ts-accent:#4B7D87;--ts-on-accent:#FFFFFF;--ts-accent-ink:#4B7D87");
        html.ShouldNotContain("ts-window");
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task Style_guide_returns_404_and_leaks_nothing_outside_Development(string environment)
    {
        await using var factory = new PortalFactory(environment);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/_styleguide", TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        html.ShouldNotContain("TechStrap portal style guide");
    }

    [Fact]
    public async Task The_setting_TECHSTRAP_PORTAL_SHOW_POWERED_BY_false_hides_the_footer_on_every_page()
    {
        await using var factory = new PortalFactory().WithWebHostBuilder(b => b.UseSetting("TECHSTRAP_PORTAL_SHOW_POWERED_BY", "false"));
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/", TestContext.Current.CancellationToken);

        html.ShouldNotContain("ts-powered");
    }

    [Fact]
    public async Task The_footer_shows_by_default_on_the_home_page()
    {
        await using var factory = new PortalFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/", TestContext.Current.CancellationToken);

        html.ShouldContain("ts-powered");
        html.ShouldContain("TechStrap Portal");
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test --project tests/TechStrap.Portal.Tests`
Expected: FAIL to compile (`TechStrap.Portal.Components.Ui` has no `AccentScope`).

- [ ] **Step 3: Write the components and options**

```razor
<div class="ts-accent-scope" style="@_style">@ChildContent</div>
```

```csharp
using Microsoft.AspNetCore.Components;
using TechStrap.Contracts.Branding;

namespace TechStrap.Portal.Components.Ui;

/// <summary>
/// Applies a product accent to everything inside it, at runtime, as the three custom properties of docs/BRAND.md section 22.
/// The values come only from <see cref="ProductAccent.TryDerive"/>, which accepts nothing but #RRGGBB, so no other text can reach the
/// style attribute. A missing or malformed accent sets nothing and the stylesheet fallbacks apply. PHASE-09 reuses this per product.
/// </summary>
public partial class AccentScope
{
    private string? _style;

    [Parameter]
    public string? Accent { get; set; }

    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    protected override void OnParametersSet()
    {
        _style = ProductAccent.TryDerive(Accent, out var colors)
            ? $"--ts-accent:{colors.Accent};--ts-on-accent:{colors.OnAccent};--ts-accent-ink:{colors.AccentInk}"
            : null;
    }
}
```

```csharp
namespace TechStrap.Portal.Components.Ui;

/// <summary>
/// Installation-wide setting for the "Powered by TechStrap" mark (D-024). Shown by default; the environment variable
/// <c>TECHSTRAP_PORTAL_SHOW_POWERED_BY=false</c> hides it on every portal page. It is not per product.
/// </summary>
public sealed class PoweredByOptions
{
    public const string ConfigurationKey = "TECHSTRAP_PORTAL_SHOW_POWERED_BY";

    public bool Show { get; set; } = true;
}
```

```razor
@if (_show)
{
    <footer class="ts-powered">
        <img src="brand/mark.svg" alt="" width="16" height="16" />
        <span>Powered by <a href="@RepositoryUrl" rel="noopener">TechStrap</a></span>
    </footer>
}
```

```csharp
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Options;

namespace TechStrap.Portal.Components.Ui;

/// <summary>The only TechStrap element a customer sees: a small "Powered by TechStrap" link (docs/BRAND.md section 8), hideable per installation.</summary>
public partial class PoweredByFooter
{
    private const string RepositoryUrl = "https://github.com/Syntax-Circus/techstrap";

    private bool _show;

    [Inject]
    private IOptions<PoweredByOptions> Options { get; set; } = default!;

    protected override void OnInitialized() => _show = Options.Value.Show;
}
```

```razor
@if (_isDevelopment)
{
    @ChildContent
}
```

```csharp
using Microsoft.AspNetCore.Components;

namespace TechStrap.Portal.Components.Ui;

/// <summary>Renders its content in Development only; anywhere else it renders nothing and reports the page as not found.</summary>
public partial class DevelopmentOnly
{
    private bool _isDevelopment;

    [Inject]
    private IHostEnvironment HostEnvironment { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    protected override void OnInitialized()
    {
        _isDevelopment = HostEnvironment.IsDevelopment();
        if (!_isDevelopment)
        {
            Navigation.NotFound();
        }
    }
}
```

- [ ] **Step 4: Write the layout, routes, pages and wiring**

```razor
@inherits LayoutComponentBase

<div class="ts-portal">
    <main class="ts-portal-main">@Body</main>
    <PoweredByFooter />
</div>
```

```razor
@using Microsoft.AspNetCore.Components.Routing
@using Microsoft.AspNetCore.Components.Web
@using TechStrap.Portal
@using TechStrap.Portal.Components
@using TechStrap.Portal.Components.Layout
@using TechStrap.Portal.Components.Ui
```

```razor
<Router AppAssembly="typeof(Program).Assembly" NotFoundPage="typeof(Pages.NotFound)">
    <Found Context="routeData">
        <RouteView RouteData="routeData" DefaultLayout="typeof(Layout.PortalLayout)" />
    </Found>
</Router>
```

```razor
@page "/"

<PageTitle>TechStrap Portal</PageTitle>
<section class="shell-placeholder">
    <h1>TechStrap Portal</h1>
    <p>This host is a placeholder shell. The real interface arrives in a later phase.</p>
</section>
```

```razor
@page "/not-found"
@layout PortalLayout

<PageTitle>Not found</PageTitle>
<section class="shell-placeholder">
    <h1>Page not found</h1>
    <p><a href="/">Back to the start</a></p>
</section>
```

Bind the option and keep the 404 status on unmatched addresses in `Program.cs`:

```diff
@@ -6,4 +6,5 @@ using SyntaxCircus.DotEnv;
 using SyntaxCircus.Observability;
 using TechStrap.Portal.Components;
+using TechStrap.Portal.Components.Ui;
 
 const string ServiceName = "techstrap-portal";
@@ -42,4 +43,8 @@ if (!string.IsNullOrWhiteSpace(keyRingPath))
 
 builder.Services.AddRazorComponents();
+// Installation-wide switch for the "Powered by TechStrap" footer (D-024); shown unless set to false.
+builder.Services.AddOptions<PoweredByOptions>()
+    .Configure<IConfiguration>((options, configuration) =>
+        options.Show = configuration.GetValue(PoweredByOptions.ConfigurationKey, true));
 
 var app = builder.Build();
@@ -48,4 +53,6 @@ telemetry.LogStartupWarning(app.Logger);
 app.UseForwardedHeaders();
 app.UseCorrelationId();
+// An address that matches no page gets the not-found page (re-executed, so the 404 status code is kept).
+app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
 app.UseAntiforgery();
 app.MapStandardHealthChecks();
```

- [ ] **Step 5: Write the style guide and Sass**

The guide applies each sample accent through `AccentScope`, so it is the visible proof of the derivation (this is the only place a sample accent is applied; real per-product theming is PHASE-09).

```razor
@page "/_styleguide"

<PageTitle>TechStrap portal style guide</PageTitle>
<DevelopmentOnly>
    <div class="ts-sg">
        <h1>TechStrap portal style guide</h1>
        <p class="ts-sg-note">Development only. The portal is plain, light and product-led: Plex Sans, small radii, the product's accent and nothing else of ours except the footer line. No stamps, tints, windows or mascot.</p>

        <section class="ts-sg-section" aria-labelledby="sg-type">
            <h2 id="sg-type">Type scale</h2>
            <p class="ts-sg-h1">Heading 1 (Sans 26 / 600)</p>
            <p>Body (Sans 15 / 400). We reply by email and give you a private link to follow your ticket.</p>
            <p>Ticket id (Mono 15 / 600): <span class="ts-sg-id">ORB-38</span></p>
        </section>

        <section class="ts-sg-section" aria-labelledby="sg-palette">
            <h2 id="sg-palette">Palette</h2>
            <ul class="ts-sg-swatches">
                <li><span class="ts-sg-swatch" style="background: var(--p-bg)"></span><code>--p-bg</code></li>
                <li><span class="ts-sg-swatch" style="background: var(--p-soft)"></span><code>--p-soft</code></li>
                <li><span class="ts-sg-swatch" style="background: var(--p-ink)"></span><code>--p-ink</code></li>
                <li><span class="ts-sg-swatch" style="background: var(--p-ink2)"></span><code>--p-ink2</code></li>
                <li><span class="ts-sg-swatch" style="background: var(--p-line)"></span><code>--p-line</code></li>
            </ul>
        </section>

        <section class="ts-sg-section" aria-labelledby="sg-accents">
            <h2 id="sg-accents">Product accents</h2>
            <p class="ts-sg-note">Each sample is wrapped in AccentScope, which derives on-accent and accent-ink from the accent with the single shared rule (TechStrap.Contracts). #4B7D87 is the worst case for white-or-black text (4.58:1).</p>
            <AccentScope Accent="#7C3AED"><div class="ts-sg-accent"><span class="ts-sg-bar"></span><button type="button" class="btn btn-primary">Send ticket</button> <button type="button" class="btn btn-outline-primary">Cancel</button> <a href="/_styleguide">Help articles</a> <code>#7C3AED</code></div></AccentScope>
            <AccentScope Accent="#F59E0B"><div class="ts-sg-accent"><span class="ts-sg-bar"></span><button type="button" class="btn btn-primary">Send ticket</button> <button type="button" class="btn btn-outline-primary">Cancel</button> <a href="/_styleguide">Help articles</a> <code>#F59E0B</code></div></AccentScope>
            <AccentScope Accent="#0F3D2E"><div class="ts-sg-accent"><span class="ts-sg-bar"></span><button type="button" class="btn btn-primary">Send ticket</button> <button type="button" class="btn btn-outline-primary">Cancel</button> <a href="/_styleguide">Help articles</a> <code>#0F3D2E</code></div></AccentScope>
            <AccentScope Accent="#2E9AFF"><div class="ts-sg-accent"><span class="ts-sg-bar"></span><button type="button" class="btn btn-primary">Send ticket</button> <button type="button" class="btn btn-outline-primary">Cancel</button> <a href="/_styleguide">Help articles</a> <code>#2E9AFF</code></div></AccentScope>
            <AccentScope Accent="#4B7D87"><div class="ts-sg-accent"><span class="ts-sg-bar"></span><button type="button" class="btn btn-primary">Send ticket</button> <button type="button" class="btn btn-outline-primary">Cancel</button> <a href="/_styleguide">Help articles</a> <code>#4B7D87</code></div></AccentScope>
        </section>

        <section class="ts-sg-section" aria-labelledby="sg-forms">
            <h2 id="sg-forms">Forms</h2>
            <form class="ts-sg-form" onsubmit="return false">
                <div class="mb-3"><label class="form-label" for="sg-name">Your name</label><input id="sg-name" class="form-control" autocomplete="name" /></div>
                <div class="mb-3"><label class="form-label" for="sg-email">Email</label><input id="sg-email" class="form-control is-invalid" type="email" value="not-an-email" aria-describedby="sg-email-msg" /><div id="sg-email-msg" class="invalid-feedback">Enter an email address like name@example.com.</div></div>
                <div class="mb-3"><label class="form-label" for="sg-msg">Message</label><textarea id="sg-msg" class="form-control" rows="4"></textarea></div>
                <button type="submit" class="btn btn-primary">Send ticket</button>
            </form>
        </section>

        <section class="ts-sg-section" aria-labelledby="sg-states">
            <h2 id="sg-states">Alerts and states</h2>
            <div class="alert alert-success" role="status">We have it. Your ticket is ORB-38. We have emailed you a link to follow it.</div>
            <div class="alert alert-danger" role="alert">This link has expired. Enter your email and we will send a new one.</div>
            <p class="placeholder-glow" aria-busy="true"><span class="placeholder col-6"></span></p>
            <p class="ts-sg-note">No tickets yet for this email address.</p>
        </section>

        <section class="ts-sg-section" aria-labelledby="sg-footer">
            <h2 id="sg-footer">Powered-by footer</h2>
            <p class="ts-sg-note">Shown by default on every page below. Set TECHSTRAP_PORTAL_SHOW_POWERED_BY=false to hide it on this installation.</p>
        </section>
    </div>
</DevelopmentOnly>
```

```scss
// Portal layout, footer and style guide. Plain and product-led: no Admin tokens, no hard shadows, no Mono labels.

.ts-portal {
  display: flex;
  flex-direction: column;
  min-height: 100vh;
  background: var(--p-bg);
  color: var(--p-ink);
}

.ts-portal-main {
  flex: 1;
  width: 100%;
  max-width: 640px;
  margin: 0 auto;
  padding: 24px 16px 32px;
}

.ts-powered {
  display: flex;
  gap: 6px;
  align-items: center;
  justify-content: center;
  padding: 14px 16px;
  font-size: .75rem;
  color: var(--p-ink2);
  border-top: 1px solid var(--p-line);

  img {
    width: 16px;
    height: 16px;
  }
}

.ts-sg-section {
  margin-top: 24px;
  padding-top: 8px;
  border-top: 1px solid var(--p-line);
}

.ts-sg-note {
  color: var(--p-ink2);
}

.ts-sg-h1 {
  font-size: 1.625rem;
  font-weight: 600;
}

.ts-sg-id {
  font: 600 .9375rem var(--ts-font-mono);
}

.ts-sg-swatches {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(130px, 1fr));
  gap: 6px 12px;
  padding: 0;
  list-style: none;

  li {
    display: flex;
    gap: 8px;
    align-items: center;
  }
}

.ts-sg-swatch {
  width: 28px;
  height: 20px;
  border: 1px solid var(--p-line);
}

.ts-sg-accent {
  display: flex;
  flex-wrap: wrap;
  gap: 10px;
  align-items: center;
  margin-bottom: 12px;
  padding: 12px;
  border: 1px solid var(--p-line);
  border-top: 6px solid var(--ts-accent);
}

.ts-sg-bar {
  display: none;
}
```

Add `@import "components";` after `@import "fonts";` in `src/TechStrap.Portal/Styles/app.scss`.

- [ ] **Step 6: Run to verify they pass**

Run: `dotnet test --project tests/TechStrap.Portal.Tests`, then the whole solution: `dotnet test --solution TechStrap.slnx`
Expected: PASS (Portal `total: 57`; solution `total: 378`, `failed: 0`, which includes the Testcontainers tests, so Docker must be running).

- [ ] **Step 7: Commit**

```bash
git add src/TechStrap.Portal tests/TechStrap.Portal.Tests
git commit -m "feat(portal): add layout, AccentScope, PoweredByFooter and the Development-only style guide" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 12: Tracked-files guard, visual critique loop and closing the phase

**Files:**
- Create: `scripts/tests/TrackedFiles.Tests.ps1`
- Modify: `docs/BRAND.md` (section 25), `docs/architecture/PHASE-02-brand-and-ux.md`, `docs/architecture/00-DISCOVERY-INDEX.md`, `docs/architecture/99-IMPLEMENTATION-ROADMAP.md`
- Write (not committed): `$env:TEMP\phase-02-pr-body.md`

**Interfaces:**
- Consumes: everything above.
- Produces: the phase status `Complete`, the recorded logo-removal result, the PR body draft with the DESIGN.md section 14 checklist.

- [ ] **Step 1: Write the failing guard**

`TrackedFiles.Tests.ps1` automates the "no vendor, CSS or font binary is tracked, and nothing loads from a CDN at runtime" check of the phase Success Criteria:

```powershell
BeforeAll {
    $script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
    $script:Tracked = @(& git -C $script:RepoRoot ls-files)
}

Describe 'files that are restored or generated at build are never committed' {
    It 'tracks no Bootstrap vendor files' {
        $script:Tracked | Where-Object { $_ -match '/Styles/Vendor/' } | Should -BeNullOrEmpty
    }

    It 'tracks no compiled CSS' {
        $script:Tracked | Where-Object { $_ -match '/wwwroot/css/' } | Should -BeNullOrEmpty
    }

    It 'tracks no font binaries or restored font licences' {
        $script:Tracked | Where-Object { $_ -match '/wwwroot/fonts/' -or $_ -match '\.(woff2?|ttf|otf)$' } | Should -BeNullOrEmpty
    }

    It 'loads no font, script or style from a CDN at runtime' {
        $pattern = 'fonts\.googleapis|fonts\.gstatic|cdn\.jsdelivr|unpkg\.com|cdnjs\.cloudflare|code\.jquery'
        $sources = $script:Tracked | Where-Object { $_ -match '^src/.*\.(razor|cshtml|html|scss|css|js|cs)$' }
        foreach ($file in $sources) {
            (Get-Content -LiteralPath (Join-Path $script:RepoRoot $file) -Raw) | Should -Not -Match $pattern -Because "$file must not call a third-party host"
        }
    }
}
```

Run it first on a tree where you deliberately `git add -f src/TechStrap.Admin/wwwroot/fonts` to see it fail, then `git reset` the path.
Run: `pwsh -NoProfile -File scripts/Invoke-ScriptTests.ps1 -Path scripts/tests/TrackedFiles.Tests.ps1 -Output Minimal`
Expected: FAIL with `Expected $null or empty, but got ...wwwroot/fonts...` while the file is staged; `Tests Passed: 4` once it is not.

- [ ] **Step 2: Run the visual critique loop (DESIGN.md sections 10 and 14)**

Start both apps in Development (two terminals): `$env:ASPNETCORE_ENVIRONMENT='Development'; dotnet run --project src/TechStrap.Admin --urls http://127.0.0.1:5090` and `... src/TechStrap.Portal --urls http://127.0.0.1:5091`. Capture eight screenshots (desktop 1280 x 5200 and phone 390 x 5200, each of the Admin guide in light and dark, the Admin guide at 320 wide, and the Portal guide at 1280 and 390):

```powershell
$edge = 'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe'
foreach ($shot in @(
  @{ n='admin-1280'; w=1280; u='http://127.0.0.1:5090/_styleguide' },
  @{ n='admin-390';  w=390;  u='http://127.0.0.1:5090/_styleguide' },
  @{ n='admin-320';  w=320;  u='http://127.0.0.1:5090/_styleguide' },
  @{ n='portal-1280'; w=1280; u='http://127.0.0.1:5091/_styleguide' },
  @{ n='portal-390';  w=390;  u='http://127.0.0.1:5091/_styleguide' })) {
  & $edge --headless --disable-gpu --hide-scrollbars --virtual-time-budget=4000 "--screenshot=$env:TEMP\$($shot.n).png" "--window-size=$($shot.w),5200" $shot.u
}
```

For light versus dark, open the guide in a normal browser and flip `document.documentElement.dataset.bsTheme` between `light`, `dark` and removed (Auto). Review each capture against BRAND.md section 26 (the art-direction checklist) and the points below, and fix what fails in a small follow-up commit before closing:

  - no horizontal scroll at 320 px; the three windows stack under 760 px; the rail becomes a strip under 820 px
  - the tint code, stamps (straight in the table, tilted in the ticket column), the dashed notched pink note and the keycaps match the v2 mockup in both themes
  - no mascot, window or `--bm-*` color in the Portal capture; the Powered-by line is the only TechStrap element
  - Keyboard: Tab through the guide and confirm the 3px focus ring is visible on every control in both apps
  - Emulate reduced motion in DevTools (Rendering, `prefers-reduced-motion: reduce`) and confirm nothing animates

Then run Lighthouse on the Admin guide: `npx lighthouse http://127.0.0.1:5090/_styleguide --only-categories=accessibility --chrome-flags="--headless" --quiet --output=json --output-path=$env:TEMP\lh-admin.json` and read `categories.accessibility.score`. The phase asks for at least 0.95. Known candidates if it falls short: the dark `--bs-*-bg-subtle` alert colors (derived by Sass `shade-color`, not pinned by `TokenContrastTests`), and the `Spam?` stamp text size. Fix the token or markup, never the threshold.

- [ ] **Step 3: Record the logo-removal result**

BRAND.md section 25 already holds the design-time result (pass). Append the implementation check after viewing the Admin guide capture with the head mark, wordmark and logo hidden (`img { display: none }` in DevTools):

```markdown

**Implementation check (P02-T08, Admin style guide and shell, mascot and wordmark hidden):** pass. The ruled ledger table with the red margin line, the straight and tilted rubber-stamp badges, the white / canary / pink timeline with the dashed notched pink note and the ledger-ruled keycaps still identify the product. Portal: fails by design (the product's identity, not ours). The SVG logo and wordmark are provisional (auto-traced); this result does not depend on them.
```

Edit the sentence to match what you actually see; if any element fails the test, say which and fix the CSS.

- [ ] **Step 4: Tick the phase documents**

In `docs/architecture/PHASE-02-brand-and-ux.md` change `- [ ]` to `- [x]` on: the seven Deliverables (the logo line reads "provisional SVG sources"), the tasks P02-T05 to P02-T10, the six Success Criteria and the three open risks that this work closed (token sharing: link by relative import chosen; font hosting: self-hosted through libman; jsdelivr at build: the Docker font and CSS assertions fail loudly). In the Deliverables line `.gitignore entries for Styles/Vendor/ and wwwroot/css/app.css` also mention `wwwroot/fonts/`. In `docs/architecture/00-DISCOVERY-INDEX.md` change the PHASE-02 status cell from `Not started` to `Complete`, and the same cell in `docs/architecture/99-IMPLEMENTATION-ROADMAP.md`.

- [ ] **Step 5: Clean-clone verification**

```powershell
$clone = Join-Path $env:TEMP 'techstrap-p02-verify'
git clone D:\dev\SyntaxCircus\techstrap $clone
Set-Location $clone
git checkout feat/phase-02-brand-and-ux
dotnet build TechStrap.slnx
dotnet test --solution TechStrap.slnx
pwsh -NoProfile -File scripts/Invoke-ScriptTests.ps1 -Output Minimal
docker compose up -d --build --wait
```

Expected: build `0 Warning(s) 0 Error(s)`; tests `failed: 0` (about 378 after this plan, from 102 before); Pester `Tests Passed: 99` and `Failed: 0` (66 before this phase); `docker compose up --wait` reports api, admin, portal, worker and postgres `Healthy`. Both image builds already ran the CSS and font assertions; confirm them in the build log (`RUN test -f /app/publish/wwwroot/css/app.css` and the two font `RUN test` lines). Then:

```powershell
curl.exe -s -o NUL -w "%{http_code}`n" http://127.0.0.1:8081/_styleguide   # Admin guide, compose runs Development: 200
curl.exe -s -o NUL -w "%{http_code}`n" http://127.0.0.1:8082/_styleguide   # Portal guide: 200
curl.exe -s -o NUL -w "%{http_code}`n" http://127.0.0.1:8081/fonts/ibm-plex-sans/files/ibm-plex-sans-latin-400-normal.woff2   # 200
git ls-files | Select-String -Pattern 'Styles/Vendor|wwwroot/css|wwwroot/fonts|\.woff2?$'   # prints nothing
git status --short   # prints nothing (the build left the tree clean)
docker compose down
```

Finally prove the Production gate on a real image: `docker run --rm -d -p 8091:80 -e ASPNETCORE_ENVIRONMENT=Production -e TRUSTEDPROXY__TRUSTEDNETWORKS__0=192.0.2.0/24 --name ts-admin-prod techstrap-admin:local` then `curl.exe -s -o NUL -w "%{http_code}`n" http://127.0.0.1:8091/_styleguide` (expected `404`) and `docker rm -f ts-admin-prod`.

- [ ] **Step 6: Draft the PR body**

Write `$env:TEMP\phase-02-pr-body.md` (do not commit it) with: the summary of Tasks 1 to 12, the screenshots, the Lighthouse score, the three open owner items (D-025 approval; the `--ink-3` on `--sel` dark rule; the provisional SVGs) and the DESIGN.md section 14 checklist, each line ticked or justified:

```markdown
## DESIGN.md section 14 checklist
- [x] BRAND.md exists and reflects the selected direction (Carbon Copy v2; contrast exception added)
- [x] the implementation follows the project's visual grammar (ledger, stamps, tint code, hard shadows, square corners)
- [x] typography is intentional (Plex Sans chrome, Plex Mono labels, Source Serif 4 message bodies; self-hosted)
- [x] composition is intentional (208px rail, dense ledger tables, one centered window per brand moment)
- [x] generic patterns have not been introduced without reason (Bootstrap is a base; radii, shadows, buttons and focus are overridden)
- [x] desktop layout has been visually reviewed (admin-1280, portal-1280)
- [x] mobile layout has been visually reviewed (admin-390, admin-320, portal-390)
- [x] accessibility has been considered (AA token pairs pinned by tests, 3px focus ring, labels with every color cue, Lighthouse score)
- [x] reduced-motion behavior has been considered (global rule, static end state of the stamp, tested)
- [x] rendered screenshots have been critically reviewed (list what was fixed)
- [x] obvious AI/template design clichés have been addressed (no gradient, glass, pill or hero patterns; mascot confined to brand moments)
- [x] the logo-removal test has been considered (BRAND.md section 25, implementation check)
```

- [ ] **Step 7: Commit**

```bash
git add scripts/tests/TrackedFiles.Tests.ps1 docs
git commit -m "docs: close PHASE-02 with the logo-removal check, status ticks and tracked-file guard" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

## Verification Notes

Every API this plan relies on, what was proved, and what was not. The code in this plan is the code of a scratch clone of this branch (`git clone` into the session scratchpad, 12 commits, deleted afterwards): `dotnet build TechStrap.slnx` 0 warnings 0 errors, `dotnet test --solution TechStrap.slnx` 378 passed 0 failed, Pester 95 passed 0 failed, `git ls-files` shows no vendor, CSS or font file, and `docker build -f Dockerfile.admin` passed with the font assertions.

| Item | Verified | Not verified |
| --- | --- | --- |
| **`SyntaxCircus.Blazor.Components` 0.1.3** | Package README is identical to the source README; markup read from `D:\dev\SyntaxCircus\SyntaxCircus.Blazor.Components`. `GlobalErrorBoundary`/`GlobalErrorView` parameters (`CssClass`, `Title`, `Description`, `HomeLabel`) work: bUnit renders the error view for a throwing page. `ReconnectModal` renders one `dialog#components-reconnect-modal` through HTTP, takes all the `*Content` fragments and its script is served at `/_content/SyntaxCircus.Blazor.Components/Components/Feedback/ReconnectModal.razor.js`. `NotFoundView` was deliberately not used (the 404 is the BrandWindow). | The reconnect JavaScript behavior in a real browser (killing the circuit and watching the dialog show, retry and resume) was not exercised; the style guide previews the states statically. |
| **`AspNetCore.SassCompiler` 1.105.1 / Dart Sass** | `@import` of a file outside the project by relative path works with the existing `sasscompiler.json` (arguments silence the `import`, `global-builtin`, `color-functions` and `if-function` deprecations). Redefining Bootstrap's `color-mode` mixin after its `mixins` import works, and gives dark rules for both `[data-bs-theme=dark]` and Auto. Compressed output keeps spaces after `:` and `,` in custom properties and `polygon(...)`, rewrites `rgba(20,33,61,.06)` as `rgba(20, 33, 61, 0.06)`, and prefixes the file with a BOM (the test helper normalizes all three). Sass-computed colors may print as `rgb(91.76%, ...)`. | Behavior under a future Dart Sass that removes `@import` (the repo already silences the deprecation; migrating to `@use` is a later task). |
| **libman fonts** | `@fontsource/ibm-plex-sans@5.3.0`, `@fontsource/ibm-plex-mono@5.3.0`, `@fontsource-variable/source-serif-4@5.3.0` restore through the jsdelivr provider with the `files` filter, including `LICENSE`; a clean `dotnet publish` contains all of them; the host serves them as `font/woff2`. `.dockerignore` re-include `!assets/brand/scss/` works in BuildKit. | Offline behavior was reasoned, not run: libman restore needs jsdelivr on every clean build, so a build without network access fails (Bootstrap already had this property); the Docker assertion turns that into a loud failure. Whether libman skips the download when the files already exist (incremental builds) was not measured. The `LICENSE` file has no extension and is not served over HTTP (the test checks the disk and the Dockerfile checks the publish folder). |
| **vtracer 0.6.15** | `pip install vtracer` installs a Windows wheel and traces the head mark (31 KB) and mascot (79 KB); both rendered faithfully in headless Edge next to the PNG. The traced SVG has only `width` and `height`, so the generator rewrites the root with a `viewBox`. fonttools reads the WOFF2 (needs `brotli`) and outlines the wordmark (3 KB), which renders correctly. | Traced colors are quantized (about 5 levels per channel), so they are close to, not identical with, the BRAND.md palette; treated as provisional. Running the generator on Linux or macOS was not tried. |
| **bUnit 2.11.3 / AngleSharp** | `BunitContext`, `Render<T>(p => p.Add(...).AddChildContent(...))`, `NavigationManager.OnNotFound` for the `NotFound()` call, and `IHostEnvironment` substitution all work with xUnit v3. AngleSharp is available transitively and is used in the host tests. | Rendering `ReconnectModal` itself in bUnit (it needs `Assets`) was not attempted; it is covered over HTTP instead. |
| **`NavigationManager.NotFound()` gating** | In static SSR it returns a real 404 with the not-found page (Production and Staging tests pass for Admin and Portal). | A circuit that navigates client-side to `/_styleguide` in Production was reasoned (the same component runs and calls `NotFound()`), not driven in a browser. |
| **Docker** | `docker build -f Dockerfile.admin` passed in the scratch clone with the CSS and font assertions and the shared-SCSS context. | `Dockerfile.portal` image build, `docker compose up -d --build --wait` and the Production-image 404 check were not run in the scratch clone (Step 5 of Task 12 does it on a clean clone). |
| **Not run at all** | Lighthouse, DevTools reduced-motion emulation, keyboard walk-through, 320 px visual review, Auto-versus-explicit theme comparison in a real browser (only one headless Edge capture of the Admin guide in the OS dark theme was inspected). Contrast of Bootstrap's Sass-derived alert colors in dark. | Task 12 Step 2 covers these by hand. |

**Findings the owner should know about** (also in the tasks): BRAND.md claims every text pair is AA, but `--ink-3` on `--sel` in dark is 4.32:1 (rule added); the mockup's `$secondary-color` mapping names are really `$body-secondary-color` and `$body-tertiary-color` in Bootstrap 5.3.8; D-025 (accent helper in Contracts) needs owner confirmation; the SVG logos are provisional.
