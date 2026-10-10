# PHASE-11f Landing Page and Product Logos Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A configurable Portal landing page that lists the products as cards (logo, name, tagline), a per-product flag that keeps a product off that list, and an uploaded product logo that supersedes the linked logo URL.

**Architecture:** `TECHSTRAP_PORTAL_LANDING` (`Neutral` | `Products`) is Portal configuration; `Product.ListedOnLanding`, `ProductBranding.Tagline` and `ProductBranding.UploadedLogo` are data on the product, edited in the Admin. The uploaded logo is stored by file name under `product-logos/` on the Api's storage volume through a store that mirrors the KB image store (D-044), served anonymously at `/product-logos/{name}`, and turned into an absolute URL at read time by `IProductLogoUrls` (Api and Worker implementations). The existing anonymous product list gains the fields the landing needs. Contracts 0.3.0 adds trailing optional parameters only.

**Tech Stack:** .NET 10, ASP.NET Core controllers and minimal endpoints, Blazor SSR (Portal) and Blazor Server (Admin, bUnit tests), EF Core 10 + Npgsql (tool-generated migrations), SyntaxCircus.Storage, xUnit v3 + Shouldly + NSubstitute, Pester 5.

**Spec:** `docs/architecture/PHASE-11f-landing-and-logos.md` (decision D-052 in `docs/architecture/04-DECISION-LOG.md`, amends D-045).

## Global Constraints

- Branch `feat/phase-11f-landing-and-logos` from `main` (already created; Task 1 is committed as `c625fc7`). One pull request at the end.
- `dotnet build TechStrap.slnx -c Release` must report 0 warnings after every task. Tests: `dotnet test --solution TechStrap.CI.slnf -c Release --no-build` needs Docker (Testcontainers Postgres); per-project runs with `dotnet test tests/<Project> -c Release --no-build --filter "FullyQualifiedName~<Class>"` are fine while iterating.
- Architecture rules (tests in `tests/TechStrap.Architecture.Tests`): route literals only in `src/TechStrap.Portal/Routing/PortalRoutes.cs`; Portal components build links only through `PortalLinks` (never `PortalRoutes.<builder>(` in `.razor`/`.razor.cs`; the `*Template`/`*Segment`/`*Prefix` constants are allowed); exactly one `[FromServices] I...Handler` per controller action; handler constructors take only Application interfaces, `TimeProvider`, `ILogger<T>` and `IOptions<T>`; the Worker never references the Api project; every documented HTTP entry point in `docs/architecture/02-ARCHITECTURE.md` sections 7.1-7.5 exists with the named handler and vice versa.
- Contracts carries no enums (naming rule); DTO changes are trailing optional parameters only.
- C# style: file-scoped namespaces, `_camelCase` private fields, `sealed` by default, non-ASCII characters only as `\u` escapes; docs ASCII only; new files LF (`.gitattributes` normalises `*.cs`? No: only the listed types, so write new files with LF explicitly); edit existing files with the Edit tool, never `sed -i`.
- Tests: every awaiting test has `[Fact(Timeout = ...)]` or passes `TestContext.Current.CancellationToken` as the project does; record the RED run output in the report before implementing; one mutation per task (named in the task) must make a test fail.
- Secrets: none in code, tests or logs. Test fixtures use the existing `TestJwt` tokens.
- Commits: Conventional Commits, stage by explicit path (never `git add -A`, never `-f`), never commit `.superpowers/`, trailers:
  ```
  Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi
  ```
  (The session id must be exactly `session_01ReiWu2p7mSuArnHAMeBiMi`, copied character for character.)
- Never run `docker compose down -v`. The dev stack (`docker compose up -d --wait`) may be used for manual checks.
- Owner answers (binding): ship before 12c in `v0.3.0`; cards show name, logo and tagline; hosted products are listed and link to `https://{host}/`; logos are PNG, JPEG or WebP only, 1 MiB cap.

## Review Focus

1. **A `PUT api/products/{id}` from the Admin or a 0.2.0 client must never drop an uploaded logo.** `ProductBranding.CreateForUpdate` carries `UploadedLogo` over (Task 3 test `CreateForUpdate_keeps_the_uploaded_logo`; Task 5 handler test `An_update_keeps_the_uploaded_logo`).
2. **A 0.2.0 client that omits `ListedOnLanding` must not unlist a product.** `UpdateProductRequest.ListedOnLanding` null means unchanged (Task 5 test `A_null_listed_flag_leaves_the_stored_value`; Task 6 raw-JSON Api test with the property omitted).
3. **A hostile or malformed upload must never be stored or served.** GIF, SVG, HTML, zero bytes, 1 MiB + 1, a lying declared length (Task 6 store tests and corpus column); a name the store could not have written is a 404 `no-store` (Task 6 serving tests).
4. **The landing must never render on a product host and never build a URL from the Host header.** Task 8 host test `A_product_host_root_is_its_product_home_never_the_landing` and the hosted-card test asserting `https://support.paperplane.test/`.
5. **A Products-mode root with a default product configured must fail at startup, not silently redirect or list.** Task 8 `Products_with_a_default_product_stops_the_host_at_start`.

---

### Task 1: Spec, D-052, roadmap and discovery rows (DONE)

Committed as `c625fc7 docs(11f): PHASE-11f spec, D-052 (amends D-045), roadmap and discovery rows`. Nothing to do; the implementer of Task 2 starts from that commit.

---

### Task 2: Contracts 0.3.0

**Files:**
- Modify: `src/TechStrap.Contracts/Products/ProductDtos.cs` (all four records)
- Modify: `src/TechStrap.Contracts/Products/PublicProductDto.cs`
- Modify: `src/TechStrap.Contracts/Products/PublicProductSummaryDto.cs`
- Modify: `src/TechStrap.Contracts/Branding/BrandingRules.cs`
- Create: `src/TechStrap.Contracts/Products/ProductLogoNames.cs`
- Modify: `src/TechStrap.Contracts/README.md` (Stability line, `### 0.3.0` note)
- Test: `tests/TechStrap.Contracts.Tests/Products/ProductLogoNameTests.cs` (create; if the project does not exist, put the tests in `tests/TechStrap.Application.Tests/Products/ProductLogoNameTests.cs`)
- Test: `tests/TechStrap.Application.Tests/Products/ProductDtoCompatibilityTests.cs` (create)
- Modify (compile fixes only, positional constructions): `tests/TechStrap.Application.Tests/Products/GetPublicProductRequestHandlerTests.cs`, `tests/TechStrap.Application.Tests/Products/ListPublicProductsRequestHandlerTests.cs`, `tests/TechStrap.Portal.Tests/Clients/PublicProductClientTests.cs`, `tests/TechStrap.Portal.Tests/Products/ProductThemeViewModelTests.cs`, `tests/TechStrap.Portal.Tests/Seo/PortalSitemapBuilderTests.cs`, `tests/TechStrap.Portal.Tests/Forms/FormTestKit.cs` (only if they stop compiling; trailing optionals should keep them compiling)

**Interfaces:**
- Produces (exact signatures every later task uses):
  ```csharp
  public sealed record ProductBrandingDto(string DisplayName, string? LogoPath, string AccentColour, string OnAccentColour, string AccentInkColour, string? FromAddress, string? ReplyTo, string? Tagline = null, string? UploadedLogoUrl = null);
  public sealed record ProductBrandingRequest(string? DisplayName, string? LogoPath, string? AccentColour, string? FromAddress, string? ReplyTo, string? Tagline = null);
  public sealed record ProductDto(Guid Id, string Key, string Name, string NumberPrefix, bool IsActive, ProductBrandingDto Branding, uint Version, string? PortalHost = null, bool ListedOnLanding = true);
  public sealed record CreateProductRequest(string? Key, string? Name, string? NumberPrefix, ProductBrandingRequest? Branding, string? PortalHost = null, bool? ListedOnLanding = null);
  public sealed record UpdateProductRequest(string? Name, ProductBrandingRequest Branding, bool IsActive, uint Version, string? PortalHost = null, bool? ListedOnLanding = null);
  public sealed record PublicProductDto(string Key, string DisplayName, string? LogoPath, string AccentColour, string OnAccentColour, string AccentInkColour, string? PortalHost = null, string? Tagline = null);
  public sealed record PublicProductSummaryDto(string Key, string DisplayName, string? PortalHost = null, string? Tagline = null, string? LogoUrl = null, string? AccentColour = null, bool ListedOnLanding = true);
  public static class BrandingRules { public const int TaglineMaxLength = 160; public static bool IsAcceptableTagline(string? value); }
  public static class ProductLogoLimits { public const long MaxBytes = 1L * 1024 * 1024; public const string FieldName = "file"; public const string PathPrefix = "product-logos/"; public static IReadOnlyList<string> AllowedExtensions { get; } = [".png", ".jpg", ".jpeg", ".webp"]; }
  public static partial class ProductLogoName { public const string Png = "png"; public const string Jpeg = "jpg"; public const string Webp = "webp"; public const int MaxLength = 37; public static bool IsValid(string? name); public static string StorageKey(string fileName); public static string ContentTypeOf(string fileName); }
  ```

- [ ] **Step 1: Write the failing tests**

`tests/TechStrap.Application.Tests/Products/ProductLogoNameTests.cs` (Application.Tests already references Contracts):

```csharp
using TechStrap.Contracts.Branding;
using TechStrap.Contracts.Products;

namespace TechStrap.Application.Tests.Products;

public sealed class ProductLogoNameTests
{
    [Theory]
    [InlineData("0123456789abcdef0123456789abcdef.png", true)]
    [InlineData("0123456789abcdef0123456789abcdef.jpg", true)]
    [InlineData("0123456789abcdef0123456789abcdef.webp", true)]
    [InlineData("0123456789abcdef0123456789abcdef.gif", false)]
    [InlineData("0123456789abcdef0123456789abcdef.svg", false)]
    [InlineData("0123456789ABCDEF0123456789abcdef.png", false)]
    [InlineData("../0123456789abcdef0123456789abcdef.png", false)]
    [InlineData("0123456789abcdef0123456789abcdef", false)]
    [InlineData("0123456789abcdef0123456789abcdef.png\n", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_a_name_the_store_writes_is_valid(string? name, bool expected) => ProductLogoName.IsValid(name).ShouldBe(expected);

    [Fact]
    public void The_storage_key_and_content_type_follow_the_name()
    {
        ProductLogoName.StorageKey("0123456789abcdef0123456789abcdef.webp").ShouldBe("product-logos/0123456789abcdef0123456789abcdef.webp");
        ProductLogoName.ContentTypeOf("0123456789abcdef0123456789abcdef.png").ShouldBe("image/png");
        ProductLogoName.ContentTypeOf("0123456789abcdef0123456789abcdef.jpg").ShouldBe("image/jpeg");
        ProductLogoName.ContentTypeOf("0123456789abcdef0123456789abcdef.webp").ShouldBe("image/webp");
        ProductLogoName.MaxLength.ShouldBe(37);
        ProductLogoLimits.MaxBytes.ShouldBe(1_048_576);
        ProductLogoLimits.AllowedExtensions.ShouldBe([".png", ".jpg", ".jpeg", ".webp"]);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("   ", true)]
    [InlineData("Tickets for the Orbitly desktop app.", true)]
    [InlineData("Line one\nline two", false)]
    [InlineData("Tab\there", false)]
    public void A_tagline_is_one_line_of_plain_text(string? value, bool expected) => BrandingRules.IsAcceptableTagline(value).ShouldBe(expected);

    [Fact]
    public void A_tagline_may_be_160_characters_but_not_161()
    {
        BrandingRules.IsAcceptableTagline(new string('a', 160)).ShouldBeTrue();
        BrandingRules.IsAcceptableTagline(new string('a', 161)).ShouldBeFalse();
        BrandingRules.TaglineMaxLength.ShouldBe(160);
    }
}
```

`tests/TechStrap.Application.Tests/Products/ProductDtoCompatibilityTests.cs`:

```csharp
using TechStrap.Contracts.Products;

namespace TechStrap.Application.Tests.Products;

/// <summary>Contracts 0.3.0 adds trailing optional parameters only: the 0.2.0 positional argument lists still compile and the new values default as documented.</summary>
public sealed class ProductDtoCompatibilityTests
{
    [Fact]
    public void The_0_2_0_argument_lists_still_construct_every_record_with_the_documented_defaults()
    {
        var branding = new ProductBrandingDto("Orbitly", null, "#1F6FEB", "#FFFFFF", "#1F6FEB", null, null);
        var product = new ProductDto(Guid.Empty, "orbitly", "Orbitly", "ORB", true, branding, 1, "support.orbitly.test");
        var create = new CreateProductRequest("orbitly", "Orbitly", "ORB", null, null);
        var update = new UpdateProductRequest("Orbitly", new ProductBrandingRequest("Orbitly", null, null, null, null), true, 1, null);
        var publicDto = new PublicProductDto("orbitly", "Orbitly", null, "#1F6FEB", "#FFFFFF", "#1F6FEB", null);
        var summary = new PublicProductSummaryDto("orbitly", "Orbitly", null);

        branding.Tagline.ShouldBeNull();
        branding.UploadedLogoUrl.ShouldBeNull();
        product.ListedOnLanding.ShouldBeTrue();
        create.ListedOnLanding.ShouldBeNull();
        update.ListedOnLanding.ShouldBeNull();
        update.Branding.Tagline.ShouldBeNull();
        publicDto.Tagline.ShouldBeNull();
        summary.ShouldSatisfyAllConditions(
            s => s.Tagline.ShouldBeNull(),
            s => s.LogoUrl.ShouldBeNull(),
            s => s.AccentColour.ShouldBeNull(),
            s => s.ListedOnLanding.ShouldBeTrue());
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet build tests/TechStrap.Application.Tests -c Release 2>&1 | grep -E "error|Build succeeded" | head`
Expected: compile errors (`ProductLogoName`, `ProductLogoLimits`, `IsAcceptableTagline`, the new parameters do not exist).

- [ ] **Step 3: Implement the Contracts changes**

`src/TechStrap.Contracts/Products/ProductDtos.cs`: add the parameters with `<param>` docs:

```csharp
/// <summary>Branding as stored, plus the colours derived from the accent (D-025, D-031) for previews. <paramref name="UploadedLogoUrl"/> is the absolute address of an uploaded logo (D-052), read-only: it is set through the logo upload route, not through this DTO's request.</summary>
/// <param name="Tagline">One line of plain text shown on the product's landing card, or null.</param>
/// <param name="UploadedLogoUrl">The absolute https address of the uploaded logo, or null when none is uploaded (or the host cannot build it). It supersedes <paramref name="LogoPath"/> wherever a logo is shown.</param>
public sealed record ProductBrandingDto(
    string DisplayName,
    string? LogoPath,
    string AccentColour,
    string OnAccentColour,
    string AccentInkColour,
    string? FromAddress,
    string? ReplyTo,
    string? Tagline = null,
    string? UploadedLogoUrl = null);

/// <param name="Tagline">One line of plain text, at most 160 characters, shown on the landing card; null or blank clears it.</param>
public sealed record ProductBrandingRequest(
    string? DisplayName,
    string? LogoPath,
    string? AccentColour,
    string? FromAddress,
    string? ReplyTo,
    string? Tagline = null);
```

Keep the existing `<summary>` and `<param>` lines of each record and add:
- `ProductDto`: `/// <param name="ListedOnLanding">Whether the product appears on the Portal's landing page when it lists products (D-052). It changes nothing else.</param>` and the parameter `bool ListedOnLanding = true` after `PortalHost`.
- `CreateProductRequest`: `/// <param name="ListedOnLanding">Whether the product appears on the landing page; null means true.</param>`, parameter `bool? ListedOnLanding = null`.
- `UpdateProductRequest`: `/// <param name="ListedOnLanding">Whether the product appears on the landing page. Null leaves the stored value unchanged (a 0.2.0 client never unlists a product); true or false sets it.</param>`, parameter `bool? ListedOnLanding = null`.

`PublicProductDto.cs`: add `/// <param name="Tagline">One line of plain text about the product, or null.</param>` and `string? Tagline = null` after `PortalHost`. Replace the summary's "It never carries emails, keys or internal ids." sentence as is (keep).

`PublicProductSummaryDto.cs`: replace the whole summary and record with:

```csharp
/// <summary>
/// One row of the public product list (PHASE-09c, D-045; extended by D-052): the key, the display name, the product's own hostname, and what a landing card shows (tagline, effective logo, accent) plus whether it is
/// listed. No id, no email. The list holds active products only; it feeds the Portal's sitemap, its product-host map and, when <c>TECHSTRAP_PORTAL_LANDING=Products</c>, its landing page, which shows the rows whose
/// <see cref="ListedOnLanding"/> is true.
/// </summary>
/// <param name="Key">The product key.</param>
/// <param name="DisplayName">The name customers see.</param>
/// <param name="PortalHost">The product's own public hostname (lower-case, e.g. support.example.com), or null when it is served only on the default portal host.</param>
/// <param name="Tagline">One line of plain text about the product, or null.</param>
/// <param name="LogoUrl">The logo address to show: the uploaded logo when there is one, else the linked logo address, else null.</param>
/// <param name="AccentColour">The accent colour as #RRGGBB, or null for the default.</param>
/// <param name="ListedOnLanding">Whether the product appears on the landing page.</param>
public sealed record PublicProductSummaryDto(string Key, string DisplayName, string? PortalHost = null, string? Tagline = null, string? LogoUrl = null, string? AccentColour = null, bool ListedOnLanding = true);
```

`BrandingRules.cs`: add inside the class:

```csharp
    /// <summary>The longest tagline, the same limit as the Domain (<c>DomainLimits.TaglineMaxLength</c>).</summary>
    public const int TaglineMaxLength = 160;

    /// <summary>True for a blank value (no tagline) and for one line of plain text of at most <see cref="TaglineMaxLength"/> characters after trimming: no line breaks, tabs or other control characters.</summary>
    public static bool IsAcceptableTagline(string? value)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return true;
        }

        return text.Length <= TaglineMaxLength && !text.Any(char.IsControl);
    }
```

Also extend the class summary's parenthetical to `(<c>Guard.Colour</c>, <c>Guard.OptionalImageUrl</c>, <c>Guard.OptionalTagline</c>)`.

Create `src/TechStrap.Contracts/Products/ProductLogoNames.cs`:

```csharp
using System.Text.RegularExpressions;

namespace TechStrap.Contracts.Products;

/// <summary>Limits and fixed names of the uploaded product logo (D-052), read by the Api, the Admin pre-check and the Portal.</summary>
public static class ProductLogoLimits
{
    /// <summary>The largest logo an administrator may upload: 1 MiB.</summary>
    public const long MaxBytes = 1L * 1024 * 1024;

    /// <summary>The multipart field that carries the image on <c>POST api/products/{id}/logo</c>.</summary>
    public const string FieldName = "file";

    /// <summary>The public path prefix of an uploaded logo: <c>product-logos/{guid}.{ext}</c>.</summary>
    public const string PathPrefix = "product-logos/";

    /// <summary>The file extensions the Admin's picker offers; the Api decides by the bytes, never by the name.</summary>
    public static IReadOnlyList<string> AllowedExtensions { get; } = [".png", ".jpg", ".jpeg", ".webp"];
}

/// <summary>The only logo names the store writes and serves: 32 lower-case hex digits, a dot and one of three extensions (no GIF, no SVG). Anything else, including any path segment, is refused before storage is touched.</summary>
public static partial class ProductLogoName
{
    public const string Png = "png";
    public const string Jpeg = "jpg";
    public const string Webp = "webp";

    /// <summary>32 hex digits, a dot and the longest extension (<c>webp</c>).</summary>
    public const int MaxLength = 37;

    // \z, not $: $ would also accept a trailing newline.
    [GeneratedRegex(@"^[0-9a-f]{32}\.(png|jpg|webp)\z", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    public static bool IsValid(string? name) => name is not null && Pattern().IsMatch(name);

    public static string StorageKey(string fileName) => ProductLogoLimits.PathPrefix + fileName;

    /// <summary>The content type for a valid name's extension. Only call it for names <see cref="IsValid"/> accepts.</summary>
    public static string ContentTypeOf(string fileName) => fileName[(fileName.LastIndexOf('.') + 1)..] switch
    {
        Png => "image/png",
        Jpeg => "image/jpeg",
        Webp => "image/webp",
        _ => throw new ArgumentException("Not a product logo name.", nameof(fileName)),
    };
}
```

`src/TechStrap.Contracts/README.md`: in the Stability paragraph replace the last sentence with "Changes since 0.1.0 are additive in source; the 0.2.0 and 0.3.0 notes below name the binary breaks (positional parameters) that a recompile resolves." Under `## Version notes`, above `### 0.2.0`, add:

```markdown
### 0.3.0

Trailing optional parameters were added to the positional records `ProductBrandingDto` (`Tagline`, `UploadedLogoUrl`), `ProductBrandingRequest` (`Tagline`), `ProductDto` (`ListedOnLanding`, default true), `CreateProductRequest` (`ListedOnLanding`, null means true), `UpdateProductRequest` (`ListedOnLanding`, null means unchanged), `PublicProductDto` (`Tagline`) and `PublicProductSummaryDto` (`Tagline`, `LogoUrl`, `AccentColour`, `ListedOnLanding`). This is source-compatible but changes their constructor and `Deconstruct` signatures: recompile consumers built against 0.2.0. `LogoPath` on `PublicProductDto` and `LogoUrl` on `PublicProductSummaryDto` now carry the effective logo: the uploaded logo when a product has one, else the linked address. `ProductLogoLimits` and `ProductLogoName` are new. `TechStrap.Client` and `TechStrap.Client.Maui` move with it.
```

- [ ] **Step 4: Build and run the tests**

Run: `dotnet build TechStrap.slnx -c Release 2>&1 | grep -E " warning | error |Build succeeded"` then `dotnet test tests/TechStrap.Application.Tests -c Release --no-build --filter "FullyQualifiedName~ProductLogoNameTests|FullyQualifiedName~ProductDtoCompatibilityTests"`
Expected: 0 warnings; all new tests pass. If any existing test stopped compiling because it constructs a public DTO positionally with a `PortalHost` argument in a position that is now ambiguous, fix the call site with named arguments. Then run `dotnet test tests/TechStrap.Application.Tests tests/TechStrap.Portal.Tests -c Release --no-build` (two commands, one project each) and confirm green.

- [ ] **Step 5: Mutation**

Change the regex to `(png|jpg|gif|webp)`; `Only_a_name_the_store_writes_is_valid` must fail on the gif row. Restore.

- [ ] **Step 6: Commit**

```bash
git add src/TechStrap.Contracts/Products/ProductDtos.cs src/TechStrap.Contracts/Products/PublicProductDto.cs src/TechStrap.Contracts/Products/PublicProductSummaryDto.cs src/TechStrap.Contracts/Branding/BrandingRules.cs src/TechStrap.Contracts/Products/ProductLogoNames.cs src/TechStrap.Contracts/README.md tests/TechStrap.Application.Tests/Products/ProductLogoNameTests.cs tests/TechStrap.Application.Tests/Products/ProductDtoCompatibilityTests.cs
git commit -m "feat(contracts): 0.3.0 trailing optionals for landing flag, tagline and uploaded logo (D-052)"
```
(plus any test files touched for compile fixes).

---

### Task 3: Domain

**Files:**
- Modify: `src/TechStrap.Domain/Rules/DomainLimits.cs`
- Modify: `src/TechStrap.Domain/Rules/Guard.cs`
- Modify: `src/TechStrap.Domain/Products/Product.cs`
- Test: `tests/TechStrap.Domain.Tests/Products/ProductLandingAndLogoTests.cs` (create)
- Test: `tests/TechStrap.Application.Tests/Products/TaglineRulesParityTests.cs` (create; mirror `ProductHostRulesParityTests.cs`)

**Interfaces:**
- Consumes: `BrandingRules.TaglineMaxLength`, `BrandingRules.IsAcceptableTagline` (Task 2).
- Produces:
  ```csharp
  public static class DomainLimits { public const int TaglineMaxLength = 160; public const int UploadedLogoNameMaxLength = 64; }
  public static DomainResult<string?> Guard.OptionalTagline(string? value, int maxLength, string target);
  // ProductBranding
  public string? Tagline { get; }
  public string? UploadedLogo { get; }   // the stored file name {32hex}.{ext}, never a URL
  public static ProductBranding Restore(string displayName, string? logoPath, string accentColour, string? fromAddress, string? replyTo, string? tagline = null, string? uploadedLogo = null);
  public static DomainResult<ProductBranding> Create(string? displayName, string? logoPath, string? accentColour, string? fromAddress, string? replyTo, string? tagline = null);
  public static DomainResult<ProductBranding> CreateForUpdate(ProductBranding current, string? displayName, string? logoPath, string? accentColour, string? fromAddress, string? replyTo, string? tagline = null); // carries current.UploadedLogo
  public ProductBranding WithUploadedLogo(string? fileName);
  // Product
  public bool ListedOnLanding { get; private set; }
  public static Product Restore(Guid id, string key, string name, string numberPrefix, ProductBranding branding, bool isActive, uint version, string? portalHost = null, bool listedOnLanding = true);
  public void SetListedOnLanding(bool listed);
  public void SetUploadedLogo(string? fileName);
  ```

- [ ] **Step 1: Write the failing tests**

`tests/TechStrap.Domain.Tests/Products/ProductLandingAndLogoTests.cs`:

```csharp
using TechStrap.Domain.Products;
using TechStrap.Domain.Rules;

namespace TechStrap.Domain.Tests.Products;

/// <summary>D-052: the tagline is one line of plain text, the uploaded logo survives a branding update, and a product is listed on the landing page unless told otherwise.</summary>
public sealed class ProductLandingAndLogoTests
{
    private static readonly TimeProvider Clock = TimeProvider.System;

    [Theory]
    [InlineData("  Tickets for the desktop app.  ", "Tickets for the desktop app.")]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void A_tagline_is_trimmed_and_blank_means_none(string? input, string? expected) =>
        ProductBranding.Create("Orbitly", null, null, null, null, input).Value.Tagline.ShouldBe(expected);

    [Theory]
    [InlineData("Line one\nline two", "tagline-invalid")]
    [InlineData("Tab\there", "tagline-invalid")]
    public void A_tagline_with_a_line_break_or_control_character_is_refused(string input, string code)
    {
        var result = ProductBranding.Create("Orbitly", null, null, null, null, input);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe(code);
        result.Error.Target.ShouldBe("tagline");
    }

    [Fact]
    public void A_tagline_may_be_160_characters_but_161_is_too_long()
    {
        ProductBranding.Create("Orbitly", null, null, null, null, new string('a', 160)).IsSuccess.ShouldBeTrue();
        var tooLong = ProductBranding.Create("Orbitly", null, null, null, null, new string('a', 161));
        tooLong.Error!.Code.ShouldBe("tagline-too-long");
        DomainLimits.TaglineMaxLength.ShouldBe(160);
    }

    [Fact]
    public void Two_brandings_that_differ_only_in_the_tagline_are_not_equal()
    {
        var a = ProductBranding.Restore("Orbitly", null, "#1F6FEB", null, null, "One");
        var b = ProductBranding.Restore("Orbitly", null, "#1F6FEB", null, null, "Two");

        a.ShouldNotBe(b);
    }

    [Fact]
    public void CreateForUpdate_keeps_the_uploaded_logo_which_the_request_cannot_carry()
    {
        var current = ProductBranding.Restore("Orbitly", "https://cdn.orbitly.test/l.png", "#1F6FEB", null, null, "Old", "0123456789abcdef0123456789abcdef.png");

        var updated = ProductBranding.CreateForUpdate(current, "Orbitly Cloud", "https://cdn.orbitly.test/l.png", "#7C3AED", null, null, "New").Value;

        updated.UploadedLogo.ShouldBe("0123456789abcdef0123456789abcdef.png");
        updated.Tagline.ShouldBe("New");
        updated.DisplayName.ShouldBe("Orbitly Cloud");
    }

    [Fact]
    public void WithUploadedLogo_replaces_only_the_uploaded_logo()
    {
        var current = ProductBranding.Restore("Orbitly", "https://cdn.orbitly.test/l.png", "#1F6FEB", "a@b.test", null, "Tag");

        var with = current.WithUploadedLogo("0123456789abcdef0123456789abcdef.webp");
        var without = with.WithUploadedLogo(null);

        with.UploadedLogo.ShouldBe("0123456789abcdef0123456789abcdef.webp");
        with.LogoPath.ShouldBe("https://cdn.orbitly.test/l.png");
        with.Tagline.ShouldBe("Tag");
        without.UploadedLogo.ShouldBeNull();
        without.ShouldBe(current);
    }

    [Fact]
    public void A_restored_product_without_the_new_arguments_is_listed_with_no_tagline_or_uploaded_logo()
    {
        var product = Product.Restore(Guid.CreateVersion7(), "orbitly", "Orbitly", "ORB", ProductBranding.Restore("Orbitly", null, "#1F6FEB", null, null), true, 1);

        product.ListedOnLanding.ShouldBeTrue();
        product.Branding.Tagline.ShouldBeNull();
        product.Branding.UploadedLogo.ShouldBeNull();
    }

    [Fact]
    public void A_created_product_is_listed_and_can_be_unlisted_and_given_an_uploaded_logo()
    {
        var product = Product.Create("orbitly", "Orbitly", "ORB", null, Clock).Value;
        product.ListedOnLanding.ShouldBeTrue();

        product.SetListedOnLanding(false);
        product.SetUploadedLogo("0123456789abcdef0123456789abcdef.png");

        product.ListedOnLanding.ShouldBeFalse();
        product.Branding.UploadedLogo.ShouldBe("0123456789abcdef0123456789abcdef.png");
        product.Branding.DisplayName.ShouldBe("Orbitly");
    }
}
```

`tests/TechStrap.Application.Tests/Products/TaglineRulesParityTests.cs` (open `ProductHostRulesParityTests.cs` first and copy its shape):

```csharp
using TechStrap.Contracts.Branding;
using TechStrap.Domain.Products;
using TechStrap.Domain.Rules;

namespace TechStrap.Application.Tests.Products;

/// <summary>Domain cannot reference Contracts, so the Admin's tagline pre-check and the Domain rule are kept in step here.</summary>
public sealed class TaglineRulesParityTests
{
    [Fact]
    public void The_limits_match() => BrandingRules.TaglineMaxLength.ShouldBe(DomainLimits.TaglineMaxLength);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("One line.")]
    [InlineData("Two\nlines")]
    [InlineData("Tab\tbed")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void Contracts_and_Domain_agree_on_every_sample(string? value) =>
        BrandingRules.IsAcceptableTagline(value).ShouldBe(ProductBranding.Create("Orbitly", null, null, null, null, value).IsSuccess);
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet build tests/TechStrap.Domain.Tests -c Release 2>&1 | grep -E "error|Build succeeded" | head -5`
Expected: compile errors (no `Tagline`, `UploadedLogo`, `WithUploadedLogo`, `ListedOnLanding`, extra arguments).

- [ ] **Step 3: Implement**

`DomainLimits.cs`: after `UrlMaxLength` add

```csharp
    /// <summary>The longest product tagline (one line on the landing card, D-052).</summary>
    public const int TaglineMaxLength = 160;

    /// <summary>The longest stored name of an uploaded product logo (<c>{32 hex}.{ext}</c> is 37; the column leaves room).</summary>
    public const int UploadedLogoNameMaxLength = 64;
```

`Guard.cs`: after `OptionalText` add

```csharp
    /// <summary>One line of plain text, trimmed; blank is null. A line break, tab or other control character is <c>{target}-invalid</c>; over <paramref name="maxLength"/> is <c>{target}-too-long</c>.</summary>
    public static DomainResult<string?> OptionalTagline(string? value, int maxLength, string target)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return DomainResult<string?>.Ok(null);
        }

        if (text.Any(char.IsControl))
        {
            return DomainErrors.Validation($"{target}-invalid", $"{target} must be one line of plain text.", target);
        }

        return text.Length > maxLength
            ? DomainErrors.Validation($"{target}-too-long", $"{target} must be at most {maxLength} characters.", target)
            : DomainResult<string?>.Ok(text);
    }
```

`Product.cs`, `ProductBranding`:
- constructor gains `string? tagline, string? uploadedLogo` (last) and sets `Tagline`, `UploadedLogo`;
- properties `public string? Tagline { get; }` and `/// <summary>The stored file name of the uploaded logo (<c>{32 hex}.{ext}</c> under <c>product-logos/</c>), never a URL; it supersedes <see cref="LogoPath"/> (D-052).</summary> public string? UploadedLogo { get; }`;
- `Restore(..., string? tagline = null, string? uploadedLogo = null) => new(displayName, logoPath, accentColour, fromAddress, replyTo, tagline, uploadedLogo);`
- `Create(..., string? tagline = null) => Build(displayName, Guard.OptionalImageUrl(...), accentColour, fromAddress, replyTo, tagline, uploadedLogo: null);`
- `CreateForUpdate(current, ..., string? tagline = null)`: same logo logic, then `return Build(displayName, logo, accentColour, fromAddress, replyTo, tagline, current.UploadedLogo);` and extend its summary: "The uploaded logo is carried over from <paramref name="current"/>: the request cannot carry it (D-052), only the logo routes change it."
- `public ProductBranding WithUploadedLogo(string? fileName) => new(DisplayName, LogoPath, AccentColour, FromAddress, ReplyTo, Tagline, fileName);` with summary "The same branding with the uploaded logo replaced (null removes it). The name is the store's own and is not validated here."
- `Build(string? displayName, DomainResult<string?> logo, string? accentColour, string? fromAddress, string? replyTo, string? tagline, string? uploadedLogo)`: add `var line = Guard.OptionalTagline(tagline, DomainLimits.TaglineMaxLength, "tagline");`, include `line` in `FirstError(name, logo, accent, from, reply, line)` and in the constructor call.

`Product`:
- constructor gains `bool listedOnLanding` (last) and sets it; property `/// <summary>Whether the product appears on the Portal's landing page when it lists products (D-052). It changes nothing else: the product stays reachable by key and host.</summary> public bool ListedOnLanding { get; private set; }`;
- `Create` passes `listedOnLanding: true`;
- `Restore(..., string? portalHost = null, bool listedOnLanding = true)`;
- `public void SetListedOnLanding(bool listed) => ListedOnLanding = listed;`
- `public void SetUploadedLogo(string? fileName) => Branding = Branding.WithUploadedLogo(fileName);` with summary "Sets or clears (null) the uploaded logo's stored file name; the logo routes are the only callers."

- [ ] **Step 4: Build and run the tests**

Run: `dotnet build TechStrap.slnx -c Release 2>&1 | grep -E " warning | error |Build succeeded"`; `dotnet test tests/TechStrap.Domain.Tests -c Release --no-build`; `dotnet test tests/TechStrap.Application.Tests -c Release --no-build --filter "FullyQualifiedName~TaglineRulesParityTests"`
Expected: 0 warnings, green. (Record equality of `ProductBranding` includes every property automatically because it is a record with get-only properties set in the constructor.)

- [ ] **Step 5: Mutation**

In `CreateForUpdate` pass `uploadedLogo: null` instead of `current.UploadedLogo`; `CreateForUpdate_keeps_the_uploaded_logo_which_the_request_cannot_carry` must fail. Restore.

- [ ] **Step 6: Commit**

```bash
git add src/TechStrap.Domain/Rules/DomainLimits.cs src/TechStrap.Domain/Rules/Guard.cs src/TechStrap.Domain/Products/Product.cs tests/TechStrap.Domain.Tests/Products/ProductLandingAndLogoTests.cs tests/TechStrap.Application.Tests/Products/TaglineRulesParityTests.cs
git commit -m "feat(domain): ListedOnLanding, Tagline and UploadedLogo on the product (D-052)"
```

---

### Task 4: Persistence (record, mapping, migration, schema docs)

**Files:**
- Modify: `src/TechStrap.Infrastructure/Persistence/Records/ProductRecord.cs`
- Modify: `src/TechStrap.Infrastructure/Persistence/Configurations/ProductRecordConfiguration.cs`
- Modify: `src/TechStrap.Infrastructure/Persistence/Mapping/ProductMappings.cs`
- Create (tool-generated): `src/TechStrap.Infrastructure/Migrations/<timestamp>_AddProductLandingAndLogo.cs` + `.Designer.cs`; the tool updates `TechStrapDbContextModelSnapshot.cs`
- Modify: `docs/architecture/05-SCHEMA.md` (products entity lines ~37-41, migrations table ~l.163-170)
- Modify: `docs/architecture/02-ARCHITECTURE.md:151` (products column list)
- Test: `tests/TechStrap.Infrastructure.IntegrationTests/Products/ProductLandingAndLogoPersistenceTests.cs` (create; mirror `ProductPortalHostPersistenceTests.cs`)

**Interfaces:**
- Consumes: `Product.Restore(..., portalHost, listedOnLanding)`, `ProductBranding.Restore(..., tagline, uploadedLogo)`, `DomainLimits.TaglineMaxLength`, `DomainLimits.UploadedLogoNameMaxLength` (Task 3).
- Produces: columns `products.tagline varchar(160) null`, `products.uploaded_logo varchar(64) null`, `products.listed_on_landing boolean not null default true`; `ProductRecord.Tagline`, `ProductRecord.UploadedLogo`, `ProductRecord.ListedOnLanding`.

- [ ] **Step 1: Write the failing test**

Open `PostgresIntegrationTestBase`, `PersistenceTestHost`, `TicketScenario` and `RecordSeed` in the test project first and use their real method names (the shape below follows `ProductPortalHostPersistenceTests.The_host_is_stored_and_restored_through_the_repository_and_is_found_as_taken`).

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TechStrap.Application.Persistence;
using TechStrap.Domain.Products;
using TechStrap.Infrastructure.Persistence.Records;

namespace TechStrap.Infrastructure.IntegrationTests.Products;

/// <summary>D-052: the three new columns round-trip, and a row written without them (an upgrade) reads as listed with no tagline or uploaded logo.</summary>
public sealed class ProductLandingAndLogoPersistenceTests(PostgresFixture postgres) : PostgresIntegrationTestBase(postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_tagline_the_uploaded_logo_and_the_listed_flag_round_trip_through_the_repository()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        var product = (await host.ReadAsync(sp => sp.GetRequiredService<IProductRepository>().GetByIdAsync(scenario.Product.Id, Ct)))!;
        var branding = ProductBranding.CreateForUpdate(product.Branding, product.Branding.DisplayName, product.Branding.LogoPath, product.Branding.AccentColour, null, null, "One line.").Value;
        product.UpdateDetails(product.Name, branding);
        product.SetUploadedLogo("0123456789abcdef0123456789abcdef.png");
        product.SetListedOnLanding(false);
        await host.WriteAsync(sp => sp.GetRequiredService<IProductRepository>().Update(product));

        var reread = (await host.ReadAsync(sp => sp.GetRequiredService<IProductRepository>().GetByIdAsync(scenario.Product.Id, Ct)))!;

        reread.Branding.Tagline.ShouldBe("One line.");
        reread.Branding.UploadedLogo.ShouldBe("0123456789abcdef0123456789abcdef.png");
        reread.ListedOnLanding.ShouldBeFalse();
    }

    [Fact]
    public async Task A_row_inserted_without_the_new_columns_reads_as_listed_with_nothing_else()
    {
        await using var context = CreateDbContext();
        await RecordSeed.ProductAsync(context, "acme", "ACME");
        await context.Database.ExecuteSqlRawAsync("UPDATE products SET listed_on_landing = DEFAULT, tagline = NULL, uploaded_logo = NULL WHERE key = 'acme'", Ct);

        var record = await context.Set<ProductRecord>().AsNoTracking().SingleAsync(p => p.Key == "acme", Ct);

        record.ListedOnLanding.ShouldBeTrue();
        record.Tagline.ShouldBeNull();
        record.UploadedLogo.ShouldBeNull();
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet build tests/TechStrap.Infrastructure.IntegrationTests -c Release 2>&1 | grep -E "error|Build succeeded" | head -5`
Expected: compile errors (`ProductRecord` has no `Tagline`, `UploadedLogo`, `ListedOnLanding`).

- [ ] **Step 3: Implement**

`ProductRecord.cs`: after `PortalHost` add

```csharp
    public string? Tagline { get; set; }

    /// <summary>The stored file name of the uploaded logo (<c>{32 hex}.{ext}</c>), or null (D-052).</summary>
    public string? UploadedLogo { get; set; }

    public bool ListedOnLanding { get; set; } = true;
```

`ProductRecordConfiguration.cs`: after the `PortalHost` property line add

```csharp
        builder.Property(p => p.Tagline).HasMaxLength(DomainLimits.TaglineMaxLength);
        builder.Property(p => p.UploadedLogo).HasMaxLength(DomainLimits.UploadedLogoNameMaxLength);
        // Existing rows become listed when the column is added (D-052).
        builder.Property(p => p.ListedOnLanding).HasDefaultValue(true);
```

`ProductMappings.cs`: `ToDomain` passes `ProductBranding.Restore(record.DisplayName, record.Logo, record.AccentColour, record.FromAddress, record.ReplyTo, record.Tagline, record.UploadedLogo)` and `Product.Restore(..., record.PortalHost, record.ListedOnLanding)`; `CopyTo` adds `record.Tagline = product.Branding.Tagline; record.UploadedLogo = product.Branding.UploadedLogo; record.ListedOnLanding = product.ListedOnLanding;`.

Generate the migration (never hand-edit it; `SchemaDocs.Tests.ps1` requires a Designer file and refuses `migrationBuilder.Sql`). If the `ef-migrate` skill is listed, follow it; otherwise:

```bash
dotnet ef migrations add AddProductLandingAndLogo --project src/TechStrap.Infrastructure --startup-project src/TechStrap.Api
dotnet ef migrations has-pending-model-changes --project src/TechStrap.Infrastructure --startup-project src/TechStrap.Api
```

Expected `Up`: `AddColumn<bool>("listed_on_landing", "products", nullable: false, defaultValue: true)`, `AddColumn<string>("tagline", ..., maxLength: 160, nullable: true)`, `AddColumn<string>("uploaded_logo", ..., maxLength: 64, nullable: true)`.

`05-SCHEMA.md`: in the `products` entity block add, in the block's own syntax, `varchar tagline "nullable, max 160, one line (D-052)"`, `varchar uploaded_logo "nullable, max 64, the stored logo file name (D-052)"`, `boolean listed_on_landing "not null, default true (D-052)"`; in the migrations table add a row for `AddProductLandingAndLogo` naming the three columns and D-052.

`02-ARCHITECTURE.md:151`: extend the products row's column list with `portal_host` (if absent), `tagline`, `uploaded_logo`, `listed_on_landing`.

- [ ] **Step 4: Build, test, verify the schema docs**

Run: `dotnet build TechStrap.slnx -c Release 2>&1 | grep -E " warning | error |Build succeeded"`; `dotnet test tests/TechStrap.Infrastructure.IntegrationTests -c Release --no-build --filter "FullyQualifiedName~ProductLandingAndLogoPersistenceTests|FullyQualifiedName~ProductPortalHostPersistenceTests"`; `pwsh -NoProfile -Command "Invoke-Pester -Path scripts/tests/SchemaDocs.Tests.ps1 -Output Minimal"`
Expected: green; the has-pending-model-changes command printed "No changes have been made to the model since the last migration."

- [ ] **Step 5: Mutation**

Remove `record.ListedOnLanding = product.ListedOnLanding;` from `CopyTo`; the round-trip test must fail. Restore.

- [ ] **Step 6: Commit**

```bash
git add src/TechStrap.Infrastructure/Persistence/Records/ProductRecord.cs src/TechStrap.Infrastructure/Persistence/Configurations/ProductRecordConfiguration.cs src/TechStrap.Infrastructure/Persistence/Mapping/ProductMappings.cs src/TechStrap.Infrastructure/Migrations docs/architecture/05-SCHEMA.md docs/architecture/02-ARCHITECTURE.md tests/TechStrap.Infrastructure.IntegrationTests/Products/ProductLandingAndLogoPersistenceTests.cs
git commit -m "feat(persistence): tagline, uploaded_logo and listed_on_landing columns with migration AddProductLandingAndLogo (D-052)"
```

---

### Task 5: Application and Api read side

**Files:**
- Create: `src/TechStrap.Application/Products/IProductLogoUrls.cs`
- Create: `src/TechStrap.Api/Startup/ProductLogoUrls.cs`
- Modify: `src/TechStrap.Api/Program.cs` (register next to `IKbImageUrls`, ~l.121)
- Modify: `src/TechStrap.Application/Products/ProductMapping.cs` and every caller of `ProductMapping.ToDto` (run `grep -rn "ProductMapping.ToDto" src/TechStrap.Application`: the create, update, get and list product handlers at least) to inject and pass `IProductLogoUrls`
- Modify: `src/TechStrap.Application/Products/CreateProductRequestHandler.cs`, `UpdateProductRequestHandler.cs`, `GetPublicProductRequestHandler.cs`, `ListPublicProductsRequestHandler.cs`
- Test: `tests/TechStrap.Application.Tests/Products/UpdateProductRequestHandlerTests.cs`, `CreateProductRequestHandlerTests.cs`, `GetPublicProductRequestHandlerTests.cs`, `ListPublicProductsRequestHandlerTests.cs` (amend constructors; add the tests below; amend the property pin)
- Test: `tests/TechStrap.Api.Tests/Products/ProductLandingFieldsEndpointTests.cs` (create; real Postgres; mirrors `ProductPortalHostEndpointTests.cs`)

**Interfaces:**
- Produces:
  ```csharp
  namespace TechStrap.Application.Products;
  public interface IProductLogoUrls { string? UrlFor(string fileName); }   // null when this host cannot build the address
  public static class ProductLogos { public static string? EffectiveLogoUrl(ProductBranding branding, IProductLogoUrls urls); }
  public static class ProductMapping { public static ProductDto ToDto(Product product, IProductLogoUrls logoUrls); }
  ```
  Handlers that call `ToDto` gain an `IProductLogoUrls logoUrls` constructor parameter placed directly before `TimeProvider clock` (last when there is no clock). Audit `changed` entries: `"listedOnLanding"` is new; a tagline-only edit shows as `"branding"`.

- [ ] **Step 1: Write the failing tests**

`UpdateProductRequestHandlerTests.cs`: add `private readonly IProductLogoUrls _logoUrls = Substitute.For<IProductLogoUrls>();`, pass it in `Handler(...)` before `_clock`, and add:

```csharp
    [Fact]
    public async Task A_null_listed_flag_leaves_the_stored_value_and_is_not_audited()
    {
        _product.SetListedOnLanding(false);

        var result = await Handler().HandleAsync(_product.Id, new UpdateProductRequest("Orbitly Cloud", SameBranding, true, 7), TestContext.Current.CancellationToken);

        result.Value.ListedOnLanding.ShouldBeFalse();
        _events.Received(1).Add(Arg.Is<AdminEvent>(e => e.PayloadJson == "{\"changed\":[\"name\"]}"));
    }

    [Fact]
    public async Task Unlisting_a_product_is_audited_as_listedOnLanding()
    {
        var result = await Handler().HandleAsync(_product.Id, new UpdateProductRequest("Orbitly", SameBranding, true, 7, null, false), TestContext.Current.CancellationToken);

        result.Value.ListedOnLanding.ShouldBeFalse();
        _product.ListedOnLanding.ShouldBeFalse();
        _events.Received(1).Add(Arg.Is<AdminEvent>(e => e.PayloadJson == "{\"changed\":[\"listedOnLanding\"]}"));
    }

    [Fact]
    public async Task A_tagline_only_edit_is_audited_as_branding()
    {
        var request = new UpdateProductRequest("Orbitly", new ProductBrandingRequest("Orbitly", null, "#1F6FEB", null, null, "Tickets for the desktop app."), true, 7);

        var result = await Handler().HandleAsync(_product.Id, request, TestContext.Current.CancellationToken);

        result.Value.Branding.Tagline.ShouldBe("Tickets for the desktop app.");
        _events.Received(1).Add(Arg.Is<AdminEvent>(e => e.PayloadJson == "{\"changed\":[\"branding\"]}"));
    }

    [Fact]
    public async Task An_update_keeps_the_uploaded_logo_and_reports_its_url()
    {
        _product.SetUploadedLogo("0123456789abcdef0123456789abcdef.png");
        _logoUrls.UrlFor("0123456789abcdef0123456789abcdef.png").Returns("https://api.test/product-logos/0123456789abcdef0123456789abcdef.png");

        var result = await Handler().HandleAsync(_product.Id, new UpdateProductRequest("Orbitly Cloud", SameBranding, true, 7), TestContext.Current.CancellationToken);

        _product.Branding.UploadedLogo.ShouldBe("0123456789abcdef0123456789abcdef.png");
        result.Value.Branding.UploadedLogoUrl.ShouldBe("https://api.test/product-logos/0123456789abcdef0123456789abcdef.png");
    }
```

`CreateProductRequestHandlerTests.cs` (same substitute): `A_null_listed_flag_lists_the_product` (a request with `ListedOnLanding: null` gives `result.Value.ListedOnLanding == true`) and `An_explicit_false_unlists_it_at_creation` (`ListedOnLanding: false` gives false, and the `Product` passed to `_products.Add` has `ListedOnLanding == false`).

`GetPublicProductRequestHandlerTests.cs`: the handler is now `new GetPublicProductRequestHandler(_products, _logoUrls)`; add:

```csharp
    [Fact]
    public async Task The_public_product_carries_the_tagline_and_the_uploaded_logo_wins_over_the_linked_one()
    {
        var branding = ProductBranding.Restore("Orbitly", "https://cdn.orbitly.test/l.png", "#1F6FEB", null, null, "One line.", "0123456789abcdef0123456789abcdef.png");
        var product = Product.Restore(Guid.CreateVersion7(), "orbitly", "Orbitly", "ORB", branding, true, 1);
        _products.GetByKeyAsync("orbitly", Arg.Any<CancellationToken>()).Returns(product);
        _logoUrls.UrlFor("0123456789abcdef0123456789abcdef.png").Returns("https://api.test/product-logos/0123456789abcdef0123456789abcdef.png");

        var result = await new GetPublicProductRequestHandler(_products, _logoUrls).HandleAsync("orbitly", Ct);

        result.Value.Tagline.ShouldBe("One line.");
        result.Value.LogoPath.ShouldBe("https://api.test/product-logos/0123456789abcdef0123456789abcdef.png");
    }
```

`ListPublicProductsRequestHandlerTests.cs`: the handler is `new ListPublicProductsRequestHandler(_products, _logoUrls)`; replace the property pin with:

```csharp
    [Fact]
    public void The_summary_dto_has_the_landing_fields_and_no_id_or_email()
    {
        typeof(PublicProductSummaryDto).GetProperties().Select(property => property.Name).Order()
            .ShouldBe(["AccentColour", "DisplayName", "Key", "ListedOnLanding", "LogoUrl", "PortalHost", "Tagline"]);
        PublicProductLimits.MaxListed.ShouldBe(1_000);
    }
```

and add:

```csharp
    [Fact]
    public async Task The_list_keeps_unlisted_active_products_and_carries_the_landing_fields()
    {
        var unlisted = Product.Restore(Guid.CreateVersion7(), "acme", "Acme Corp", "ACM", ProductBranding.Restore("Acme Corp", "https://cdn.acme.test/l.png", "#7C3AED", null, null, "Acme tagline"), true, 1, null, listedOnLanding: false);
        var uploaded = Product.Restore(Guid.CreateVersion7(), "orbitly", "Orbitly", "ORB", ProductBranding.Restore("Orbitly", null, "#1F6FEB", null, null, null, "0123456789abcdef0123456789abcdef.webp"), true, 1, "support.orbitly.example");
        _products.ListAsync(true, Arg.Any<CancellationToken>()).Returns([uploaded, unlisted]);
        _logoUrls.UrlFor("0123456789abcdef0123456789abcdef.webp").Returns("https://api.test/product-logos/0123456789abcdef0123456789abcdef.webp");

        var result = await new ListPublicProductsRequestHandler(_products, _logoUrls).HandleAsync(Ct);

        result.Value.ShouldBe([
            new PublicProductSummaryDto("acme", "Acme Corp", null, "Acme tagline", "https://cdn.acme.test/l.png", "#7C3AED", ListedOnLanding: false),
            new PublicProductSummaryDto("orbitly", "Orbitly", "support.orbitly.example", null, "https://api.test/product-logos/0123456789abcdef0123456789abcdef.webp", "#1F6FEB", ListedOnLanding: true)]);
    }
```

`tests/TechStrap.Api.Tests/Products/ProductLandingFieldsEndpointTests.cs`: copy the fixture of `ProductPortalHostEndpointTests` (`StartAsync`, the admin client, `PostAsync`, `PutRawAsync` at l.91-96, how it reads `errorCodes`) and add:

```csharp
    [Fact(Timeout = 120_000)]
    public async Task Create_update_and_the_public_endpoints_carry_the_tagline_and_the_listed_flag()
    {
        var (factory, database, admin) = await StartAsync();
        await using var _ = factory;
        using var created = await PostAsync(admin, new CreateProductRequest("orbitly", "Orbitly", "ORB", new ProductBrandingRequest("Orbitly", null, null, null, null, "  Tickets for the app.  "), null, false));
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var dto = JsonDocument.Parse(await created.Content.ReadAsStringAsync(Ct));
        dto.RootElement.GetProperty("listedOnLanding").GetBoolean().ShouldBeFalse();
        dto.RootElement.GetProperty("branding").GetProperty("tagline").GetString().ShouldBe("Tickets for the app.");
        (await database.ScalarAsync<bool>("SELECT listed_on_landing FROM products WHERE key = 'orbitly'")).ShouldBeFalse();

        // A 0.2.0 client omits the property: the flag stays false.
        var id = dto.RootElement.GetProperty("id").GetGuid();
        var version = dto.RootElement.GetProperty("version").GetUInt32();
        using var put = await PutRawAsync(admin, id, "{\"name\":\"Orbitly\",\"branding\":{\"displayName\":\"Orbitly\"},\"isActive\":true,\"version\":" + version + "}");
        put.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await database.ScalarAsync<bool>("SELECT listed_on_landing FROM products WHERE key = 'orbitly'")).ShouldBeFalse();

        using var anonymous = factory.CreateClient();
        using var list = JsonDocument.Parse(await anonymous.GetStringAsync("/api/public/products", Ct));
        var row = list.RootElement.EnumerateArray().Single(p => p.GetProperty("key").GetString() == "orbitly");
        row.GetProperty("listedOnLanding").GetBoolean().ShouldBeFalse();
        row.GetProperty("tagline").GetString().ShouldBe("Tickets for the app.");
        row.GetProperty("accentColour").GetString().ShouldBe("#1F6FEB");
        using var one = JsonDocument.Parse(await anonymous.GetStringAsync("/api/public/products/orbitly", Ct));
        one.RootElement.GetProperty("tagline").GetString().ShouldBe("Tickets for the app.");
    }

    [Fact(Timeout = 120_000)]
    public async Task A_tagline_with_a_line_break_is_a_400_on_the_tagline_field()
    {
        var (factory, _, admin) = await StartAsync();
        await using var _ = factory;

        using var response = await PostAsync(admin, new CreateProductRequest("orbitly", "Orbitly", "ORB", new ProductBrandingRequest("Orbitly", null, null, null, null, "two\nlines")));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        body.RootElement.GetProperty("errorCodes").GetProperty("tagline").GetString().ShouldBe("tagline-invalid");
    }
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet build tests/TechStrap.Application.Tests -c Release 2>&1 | grep -E "error|Build succeeded" | head -5`
Expected: compile errors (`IProductLogoUrls` missing; handler constructors).

- [ ] **Step 3: Implement**

Create `src/TechStrap.Application/Products/IProductLogoUrls.cs`:

```csharp
using TechStrap.Domain.Products;

namespace TechStrap.Application.Products;

/// <summary>
/// The absolute public address of an uploaded product logo, <c>{api public url}/product-logos/{fileName}</c> (D-052). The Api builds it from <c>TECHSTRAP_API_PUBLIC_URL</c>; the Worker builds it from its own
/// optional copy of that setting and answers null when it is blank, so emails then fall back to the linked logo.
/// </summary>
public interface IProductLogoUrls
{
    string? UrlFor(string fileName);
}

/// <summary>The one rule for which logo a reader sees: the uploaded logo when there is one and this host can address it, else the linked logo address (D-052).</summary>
public static class ProductLogos
{
    public static string? EffectiveLogoUrl(ProductBranding branding, IProductLogoUrls urls)
    {
        ArgumentNullException.ThrowIfNull(branding);
        ArgumentNullException.ThrowIfNull(urls);
        return branding.UploadedLogo is { Length: > 0 } name ? urls.UrlFor(name) ?? branding.LogoPath : branding.LogoPath;
    }
}
```

`ProductMapping.ToDto(Product product, IProductLogoUrls logoUrls)`: the branding DTO becomes `new ProductBrandingDto(branding.DisplayName, branding.LogoPath, colours.Accent, colours.OnAccent, colours.AccentInk, branding.FromAddress, branding.ReplyTo, branding.Tagline, branding.UploadedLogo is { } name ? logoUrls.UrlFor(name) : null)` and the product DTO ends with `product.PortalHost, product.ListedOnLanding`. Every caller injects `IProductLogoUrls logoUrls` and passes it.

`CreateProductRequestHandler`: `ProductBranding.Create(input.DisplayName, input.LogoPath, input.AccentColour, input.FromAddress, input.ReplyTo, input.Tagline)`; after `product.SetPortalHost(host);` add `product.SetListedOnLanding(request.ListedOnLanding ?? true);`.

`UpdateProductRequestHandler`: `CreateForUpdate(product.Branding, input?.DisplayName, input?.LogoPath, input?.AccentColour, input?.FromAddress, input?.ReplyTo, input?.Tagline)`; after the `portalHost` change check add

```csharp
        // null = the caller did not send the field (a 0.2.0 client): the flag stays as stored.
        var listed = request.ListedOnLanding ?? product.ListedOnLanding;
        if (product.ListedOnLanding != listed)
        {
            changed.Add("listedOnLanding");
        }
```

and after `product.SetActive(request.IsActive);` add `product.SetListedOnLanding(listed);`.

`GetPublicProductRequestHandler(IProductRepository products, IProductLogoUrls logoUrls)`: `new PublicProductDto(product.Key, branding.DisplayName, ProductLogos.EffectiveLogoUrl(branding, logoUrls), colors.Accent, colors.OnAccent, colors.AccentInk, product.PortalHost, branding.Tagline)`.

`ListPublicProductsRequestHandler(IProductRepository products, IProductLogoUrls logoUrls)`: project `new PublicProductSummaryDto(product.Key, product.Branding.DisplayName, product.PortalHost, product.Branding.Tagline, ProductLogos.EffectiveLogoUrl(product.Branding, logoUrls), product.Branding.AccentColour, product.ListedOnLanding)`; rewrite the class summary: the list feeds the Portal's sitemap, its host map and (D-052) its landing page, still holds every active product, and an unlisted one carries `ListedOnLanding = false`.

Create `src/TechStrap.Api/Startup/ProductLogoUrls.cs` as a copy of `KbImageUrls` implementing `IProductLogoUrls` with `ProductLogoLimits.PathPrefix`, the same Development-only request fallback and the exception text "The logo URL needs TECHSTRAP_API_PUBLIC_URL or a current request."; register in `Program.cs` right after the `IKbImageUrls` registration: `builder.Services.AddScoped<TechStrap.Application.Products.IProductLogoUrls, ProductLogoUrls>();`.

- [ ] **Step 4: Build and test**

Run: `dotnet build TechStrap.slnx -c Release 2>&1 | grep -E " warning | error |Build succeeded"`; `dotnet test tests/TechStrap.Application.Tests -c Release --no-build`; `dotnet test tests/TechStrap.Api.Tests -c Release --no-build --filter "FullyQualifiedName~Products"`; `dotnet test tests/TechStrap.Architecture.Tests -c Release --no-build`
Expected: green.

- [ ] **Step 5: Mutation**

In the update handler replace `request.ListedOnLanding ?? product.ListedOnLanding` with `request.ListedOnLanding ?? true`; `A_null_listed_flag_leaves_the_stored_value_and_is_not_audited` must fail. Restore.

- [ ] **Step 6: Commit**

```bash
git add src/TechStrap.Application/Products src/TechStrap.Api/Startup/ProductLogoUrls.cs src/TechStrap.Api/Program.cs tests/TechStrap.Application.Tests/Products tests/TechStrap.Api.Tests/Products/ProductLandingFieldsEndpointTests.cs
git commit -m "feat(api): effective logo, tagline and listed flag on the product DTOs and public list (D-052)"
```

---

### Task 6: Write side (store, upload and remove routes, anonymous serving)

**Files:**
- Create: `src/TechStrap.Infrastructure/Attachments/CappedImageIntake.cs`
- Modify: `src/TechStrap.Infrastructure/Attachments/KbImageStore.cs` (use the shared intake; behaviour unchanged)
- Create: `src/TechStrap.Application/Products/IProductLogoStore.cs`
- Create: `src/TechStrap.Infrastructure/Attachments/ProductLogoStore.cs`
- Modify: `src/TechStrap.Infrastructure/Attachments/AttachmentServiceCollectionExtensions.cs` (`TryAddScoped<IProductLogoStore, ProductLogoStore>()`)
- Create: `src/TechStrap.Application/Products/UploadProductLogoRequestHandler.cs`, `src/TechStrap.Application/Products/RemoveProductLogoRequestHandler.cs`
- Modify: `src/TechStrap.Application/Products/ProductErrors.cs` (`LogoFileRequired()`)
- Register the two handlers where `IUploadKbImageRequestHandler` is registered (run `grep -rn "IUploadKbImageRequestHandler" src --include=*.cs` and mirror it)
- Create: `src/TechStrap.Api/Startup/ProductLogoRequestLimits.cs`, `src/TechStrap.Api/Startup/ProductLogoEndpoints.cs`
- Modify: `src/TechStrap.Api/Controllers/ProductsController.cs` (two actions + `ProductLogoForm`)
- Modify: `src/TechStrap.Api/Startup/AttachmentSandbox.cs` (add `"/product-logos"`)
- Modify: `src/TechStrap.Api/Program.cs` (`app.MapProductLogos();` next to `app.MapKbImages();`)
- Modify: `tests/TechStrap.Api.Tests/Auth/AgentAccessCoverageTests.cs` (`AdminOnlyRoutes` +2)
- Modify: `docs/architecture/02-ARCHITECTURE.md` (7.2 rows, 7.6 row, the `IKbImageStore` abstraction row ~l.120 gets an `IProductLogoStore` sibling)
- Modify: `tests/Shared/HostileUploadCorpus.cs`, `tests/Shared/Fixtures/hostile-uploads/manifest.json`
- Test: `tests/TechStrap.Infrastructure.IntegrationTests/Products/ProductLogoStoreTests.cs` (create; mirror `KbImageStoreTests.cs`)
- Test: `tests/TechStrap.Application.Tests/Products/UploadProductLogoRequestHandlerTests.cs`, `RemoveProductLogoRequestHandlerTests.cs` (create)
- Test: `tests/TechStrap.Api.Tests/Products/ProductLogoEndpointTests.cs`, `ProductLogoServingTests.cs`, `ProductLogoDiskFullTests.cs` (create)

**Interfaces:**
- Consumes: `ProductLogoLimits`, `ProductLogoName` (Task 2); `Product.SetUploadedLogo`, `ProductBranding.UploadedLogo` (Task 3); `IProductLogoUrls`, `ProductMapping.ToDto(product, logoUrls)` (Task 5); `KbImageSignatures.Identify` (existing, internal to Infrastructure).
- Produces:
  ```csharp
  // Infrastructure (internal)
  internal sealed record CappedImage(MemoryStream Content, string? Extension);   // Extension from KbImageSignatures.Identify, null when not an image
  internal static class CappedImageIntake { public static Task<CappedImage?> ReadAsync(Stream content, long declaredLength, long maxBytes, CancellationToken ct); } // null = over maxBytes (declared or real)
  // Application
  public sealed record IncomingProductLogo(long Length, Stream Content);
  public sealed record StoredProductLogo(string Key, string FileName, string ContentType, long Size);
  public sealed record ProductLogoContent(Stream Content, string ContentType, long Size);
  public interface IProductLogoStore
  {
      Task<Result<StoredProductLogo>> SaveAsync(IncomingProductLogo logo, CancellationToken cancellationToken);   // product-logo-too-large | product-logo-type-not-allowed, target "file"
      Task<ProductLogoContent?> OpenReadAsync(string fileName, CancellationToken cancellationToken);
      Task DeleteAsync(string fileName, CancellationToken cancellationToken);   // best effort: swallows storage errors; ignores an invalid name
  }
  public interface IUploadProductLogoRequestHandler { Task<Result<ProductDto>> HandleAsync(Guid productId, IncomingProductLogo? logo, CancellationToken cancellationToken); }
  public interface IRemoveProductLogoRequestHandler { Task<Result<ProductDto>> HandleAsync(Guid productId, CancellationToken cancellationToken); }
  // Api
  public static class ProductLogoRequestLimits { public const long FormBytes = ProductLogoLimits.MaxBytes + (1024 * 1024); }
  public static class ProductLogoEndpoints { public const string Route = "/product-logos/{name}"; public static IEndpointRouteBuilder MapProductLogos(this IEndpointRouteBuilder app); }
  // Routes: POST api/products/{id:guid}/logo (Admin, multipart "file", 200 ProductDto), DELETE api/products/{id:guid}/logo (Admin, 200 ProductDto)
  ```
  Error codes: `product-logo-too-large`, `product-logo-type-not-allowed` (both Validation, target `file`), `file-required` (Validation, target `file`). Audit: `ProductUpdated` with `{"changed":["uploadedLogo"]}`.

- [ ] **Step 1: Write the failing tests**

`tests/TechStrap.Infrastructure.IntegrationTests/Products/ProductLogoStoreTests.cs` (open `KbImageStoreTests.cs` and copy its fixture: how it builds the store over the local provider in a temp root, its PNG/JPEG/WebP/GIF byte samples):

```csharp
    [Fact]
    public async Task A_png_a_jpeg_and_a_webp_are_stored_under_product_logos_with_a_random_name()
    {
        foreach (var (bytes, extension, contentType) in new[] { (Png, "png", "image/png"), (Jpeg, "jpg", "image/jpeg"), (Webp, "webp", "image/webp") })
        {
            var stored = (await Store().SaveAsync(new IncomingProductLogo(bytes.Length, new MemoryStream(bytes)), Ct)).Value;

            stored.Key.ShouldStartWith("product-logos/");
            stored.FileName.ShouldEndWith("." + extension);
            ProductLogoName.IsValid(stored.FileName).ShouldBeTrue();
            stored.ContentType.ShouldBe(contentType);
            (await Store().OpenReadAsync(stored.FileName, Ct)).ShouldNotBeNull().ContentType.ShouldBe(contentType);
        }
    }

    [Fact]
    public async Task A_gif_an_svg_and_a_zero_byte_file_are_refused_as_type_not_allowed()
    {
        foreach (var bytes in new[] { Gif, "<svg xmlns='http://www.w3.org/2000/svg'/>"u8.ToArray(), Array.Empty<byte>() })
        {
            var result = await Store().SaveAsync(new IncomingProductLogo(bytes.Length, new MemoryStream(bytes)), Ct);

            result.IsFailure.ShouldBeTrue();
            result.Errors[0].Code.ShouldBe("product-logo-type-not-allowed");
            result.Errors[0].Target.ShouldBe("file");
        }
    }

    [Fact]
    public async Task One_mebibyte_is_accepted_and_one_byte_more_is_too_large_whether_declared_or_real()
    {
        var atLimit = PngOfLength((int)ProductLogoLimits.MaxBytes);
        (await Store().SaveAsync(new IncomingProductLogo(atLimit.Length, new MemoryStream(atLimit)), Ct)).IsSuccess.ShouldBeTrue();

        var declaredOver = await Store().SaveAsync(new IncomingProductLogo(ProductLogoLimits.MaxBytes + 1, new MemoryStream(Png)), Ct);
        declaredOver.Errors[0].Code.ShouldBe("product-logo-too-large");

        var over = PngOfLength((int)ProductLogoLimits.MaxBytes + 1);
        var lyingLength = await Store().SaveAsync(new IncomingProductLogo(Png.Length, new MemoryStream(over)), Ct);
        lyingLength.Errors[0].Code.ShouldBe("product-logo-too-large");
    }

    [Theory]
    [InlineData("0123456789abcdef0123456789abcdef.gif")]
    [InlineData("../kb-images/0123456789abcdef0123456789abcdef.png")]
    [InlineData("0123456789ABCDEF0123456789abcdef.png")]
    public async Task A_name_the_store_could_not_have_written_is_never_read(string name) =>
        (await Store().OpenReadAsync(name, Ct)).ShouldBeNull();

    [Fact]
    public async Task Delete_removes_the_object_and_is_quiet_for_a_missing_or_invalid_name()
    {
        var stored = (await Store().SaveAsync(new IncomingProductLogo(Png.Length, new MemoryStream(Png)), Ct)).Value;

        await Store().DeleteAsync(stored.FileName, Ct);
        await Store().DeleteAsync(stored.FileName, Ct);
        await Store().DeleteAsync("../etc/passwd", Ct);

        (await Store().OpenReadAsync(stored.FileName, Ct)).ShouldBeNull();
    }

    [Theory]
    [MemberData(nameof(HostileUploadCorpus.Rows), MemberType = typeof(HostileUploadCorpus))]
    public async Task Every_hostile_upload_gets_its_recorded_product_logo_outcome(string id)
    {
        var upload = HostileUploadCorpus.Get(id);

        var result = await Store().SaveAsync(new IncomingProductLogo(upload.Content.Length, new MemoryStream(upload.Content)), Ct);

        result.IsSuccess.ShouldBe(upload.ProductLogo.Stored, id);
        if (!upload.ProductLogo.Stored)
        {
            result.Errors[0].Code.ShouldBe(upload.ProductLogo.ErrorCode, id);
        }
    }
```

(`PngOfLength(n)`: the PNG signature + IHDR sample padded with zeros to `n` bytes; `Store()` builds a `ProductLogoStore` the way `KbImageStoreTests` builds its store.)

`tests/Shared/HostileUploadCorpus.cs`: `HostileUpload` gains `Expectation ProductLogo` (last), `Read` adds `Expect(entry.GetProperty("productLogo"))`. In `manifest.json` every entry gets a `"productLogo"` object following this rule: the same outcome as `kbImage`, except (a) a GIF body (`GIF87a`/`GIF89a` content) is `{ "stored": false, "errorCode": "product-logo-type-not-allowed" }`, (b) `oversize` is `{ "stored": false, "errorCode": "product-logo-too-large" }`, (c) every refused `kbImage` keeps its outcome with the code renamed `kb-image-...` to `product-logo-...`. Add one new entry at the end, `"id": "logo-just-over"`, `fileName` `logo.png`, `declaredContentType` `image/png`, `content` `{ "generate": { "prefixHex": "89504e470d0a1a0a0000000d49484452", "totalBytes": 1048577 } }`, `attachment` stored true (name `logo.png`, content type `image/png`; follow the existing png entry's attachment shape), `kbImage` stored true, `productLogo` `{ "stored": false, "errorCode": "product-logo-too-large" }`. Keep `HostileUploadCorpusTests` (pins the 15 original ids) passing: if it pins the exact count, add `logo-just-over` to its list.

`tests/TechStrap.Application.Tests/Products/UploadProductLogoRequestHandlerTests.cs` (copy the fixture style of `UpdateProductRequestHandlerTests`: admin claims, `_products`, `_events`, `UnitOfWorkSubstitute`; substitutes `IProductLogoStore _store` and `IProductLogoUrls _logoUrls`):

```csharp
    [Fact]
    public async Task An_upload_stores_the_file_sets_the_name_audits_and_deletes_the_previous_file_after_commit()
    {
        _product.SetUploadedLogo("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.png");
        _store.SaveAsync(Arg.Any<IncomingProductLogo>(), Arg.Any<CancellationToken>())
            .Returns(Result<StoredProductLogo>.Success(new StoredProductLogo("product-logos/bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb.webp", "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb.webp", "image/webp", 10)));
        _logoUrls.UrlFor("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb.webp").Returns("https://api.test/product-logos/bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb.webp");

        var result = await Handler().HandleAsync(_product.Id, new IncomingProductLogo(10, new MemoryStream(new byte[10])), TestContext.Current.CancellationToken);

        result.Value.Branding.UploadedLogoUrl.ShouldBe("https://api.test/product-logos/bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb.webp");
        _product.Branding.UploadedLogo.ShouldBe("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb.webp");
        _products.Received(1).Update(_product);
        _events.Received(1).Add(Arg.Is<AdminEvent>(e => e.Type == AdminEventType.ProductUpdated && e.PayloadJson == "{\"changed\":[\"uploadedLogo\"]}"));
        await _store.Received(1).DeleteAsync("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.png", Arg.Any<CancellationToken>());
        await _store.DidNotReceive().DeleteAsync("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb.webp", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_failed_commit_deletes_the_new_file_and_keeps_the_previous_name()
    {
        _product.SetUploadedLogo("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.png");
        _store.SaveAsync(Arg.Any<IncomingProductLogo>(), Arg.Any<CancellationToken>())
            .Returns(Result<StoredProductLogo>.Success(new StoredProductLogo("product-logos/bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb.webp", "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb.webp", "image/webp", 10)));

        var result = await Handler(Result.Failure(new ResultError("db-down", "down", ResultErrorKind.Unexpected))).HandleAsync(_product.Id, new IncomingProductLogo(10, new MemoryStream(new byte[10])), TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        await _store.Received(1).DeleteAsync("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb.webp", Arg.Any<CancellationToken>());
        await _store.DidNotReceive().DeleteAsync("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.png", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task No_file_is_file_required_and_a_store_refusal_is_passed_through_before_anything_changes()
    {
        (await Handler().HandleAsync(_product.Id, null, TestContext.Current.CancellationToken)).Errors[0].Code.ShouldBe("file-required");
        _store.SaveAsync(Arg.Any<IncomingProductLogo>(), Arg.Any<CancellationToken>())
            .Returns(Result<StoredProductLogo>.Failure(new ResultError("product-logo-type-not-allowed", "no", ResultErrorKind.Validation, "file")));

        var refused = await Handler().HandleAsync(_product.Id, new IncomingProductLogo(3, new MemoryStream(new byte[3])), TestContext.Current.CancellationToken);

        refused.Errors[0].Code.ShouldBe("product-logo-type-not-allowed");
        _products.DidNotReceive().Update(Arg.Any<Product>());
        _events.DidNotReceive().Add(Arg.Any<AdminEvent>());
    }

    [Fact]
    public async Task An_unknown_product_is_not_found_and_nothing_is_stored()
    {
        var result = await Handler().HandleAsync(Guid.NewGuid(), new IncomingProductLogo(3, new MemoryStream(new byte[3])), TestContext.Current.CancellationToken);

        result.Errors[0].Code.ShouldBe("product-not-found");
        await _store.DidNotReceive().SaveAsync(Arg.Any<IncomingProductLogo>(), Arg.Any<CancellationToken>());
    }
```

`RemoveProductLogoRequestHandlerTests.cs`: `Removing_clears_the_name_audits_and_deletes_the_file_after_commit` (stored `aaaa...png`; after the call `_product.Branding.UploadedLogo` is null, audit `{"changed":["uploadedLogo"]}`, `DeleteAsync("aaaa...png")` received once, `UploadedLogoUrl` null in the DTO); `Removing_when_there_is_no_uploaded_logo_is_a_no_op_200` (no `Update`, no audit, no delete, `IsSuccess`); `An_unknown_product_is_not_found`.

`tests/TechStrap.Api.Tests/Products/ProductLogoEndpointTests.cs` (real Postgres; fixture of `ProductPortalHostEndpointTests`; `Storage:Local:RootPath` set to a temp directory as `KbImageServingTests` does; an admin client and an agent client):

```csharp
    [Fact(Timeout = 120_000)]
    public async Task An_admin_uploads_replaces_and_removes_a_logo_and_the_files_follow()
    {
        var (factory, database, admin) = await StartAsync();
        await using var _ = factory;
        var id = await CreateProductAsync(admin, "orbitly");

        using var first = await admin.PostAsync($"/api/products/{id}/logo", Form(Png, "logo.png", "image/png"), Ct);
        first.StatusCode.ShouldBe(HttpStatusCode.OK, await first.Content.ReadAsStringAsync(Ct));
        var firstUrl = Json(await first.Content.ReadAsStringAsync(Ct)).GetProperty("branding").GetProperty("uploadedLogoUrl").GetString()!;
        firstUrl.ShouldStartWith("http://localhost/product-logos/");
        var firstName = firstUrl[(firstUrl.LastIndexOf('/') + 1)..];
        File.Exists(Path.Combine(_storage, "product-logos", firstName)).ShouldBeTrue();
        (await database.ScalarAsync<string>("SELECT uploaded_logo FROM products WHERE key = 'orbitly'")).ShouldBe(firstName);

        using var second = await admin.PostAsync($"/api/products/{id}/logo", Form(Webp, "logo.webp", "image/webp"), Ct);
        second.StatusCode.ShouldBe(HttpStatusCode.OK);
        var secondName = NameOf(Json(await second.Content.ReadAsStringAsync(Ct)));
        File.Exists(Path.Combine(_storage, "product-logos", firstName)).ShouldBeFalse("the replaced file is deleted after the commit");
        File.Exists(Path.Combine(_storage, "product-logos", secondName)).ShouldBeTrue();

        using var anonymous = factory.CreateClient();
        using var served = await anonymous.GetAsync($"/product-logos/{secondName}", Ct);
        served.StatusCode.ShouldBe(HttpStatusCode.OK);
        served.Content.Headers.ContentType!.MediaType.ShouldBe("image/webp");

        using var removed = await admin.DeleteAsync($"/api/products/{id}/logo", Ct);
        removed.StatusCode.ShouldBe(HttpStatusCode.OK);
        Json(await removed.Content.ReadAsStringAsync(Ct)).GetProperty("branding").GetProperty("uploadedLogoUrl").ValueKind.ShouldBe(JsonValueKind.Null);
        File.Exists(Path.Combine(_storage, "product-logos", secondName)).ShouldBeFalse();
        (await database.ScalarAsync<string?>("SELECT uploaded_logo FROM products WHERE key = 'orbitly'")).ShouldBeNull();
        (await database.ScalarAsync<long>("SELECT count(*) FROM admin_events WHERE payload::text LIKE '%uploadedLogo%'")).ShouldBe(3);
    }

    [Fact(Timeout = 120_000)]
    public async Task An_svg_a_gif_and_a_missing_file_are_400s_and_an_agent_is_403()
    {
        var (factory, _, admin) = await StartAsync();
        await using var _ = factory;
        var id = await CreateProductAsync(admin, "orbitly");

        using var svg = await admin.PostAsync($"/api/products/{id}/logo", Form("<svg xmlns='http://www.w3.org/2000/svg'/>"u8.ToArray(), "logo.svg", "image/svg+xml"), Ct);
        svg.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await svg.Content.ReadAsStringAsync(Ct)).ShouldContain("product-logo-type-not-allowed");
        using var gif = await admin.PostAsync($"/api/products/{id}/logo", Form(Gif, "logo.gif", "image/gif"), Ct);
        gif.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var empty = await admin.PostAsync($"/api/products/{id}/logo", new MultipartFormDataContent { { new StringContent("x"), "other" } }, Ct);
        empty.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await empty.Content.ReadAsStringAsync(Ct)).ShouldContain("file-required");

        using var agent = factory.CreateClient().Bearer(TestJwt.Token("agent", [TestJwt.AgentGroup], email: "agent@example.com"));
        using var forbidden = await agent.PostAsync($"/api/products/{id}/logo", Form(Png, "logo.png", "image/png"), Ct);
        forbidden.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using var unknown = await admin.DeleteAsync($"/api/products/{Guid.NewGuid()}/logo", Ct);
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact(Timeout = 120_000)]
    public async Task A_body_far_over_the_limit_is_a_413_before_the_handler_runs()
    {
        var (factory, _, admin) = await StartAsync();
        await using var _ = factory;
        var id = await CreateProductAsync(admin, "orbitly");

        using var response = await admin.PostAsync($"/api/products/{id}/logo", Form(new byte[(int)ProductLogoRequestLimits.FormBytes + 1024], "big.png", "image/png"), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
    }
```

(`Form(bytes, name, type)` builds a `MultipartFormDataContent` with the part named `file`; `Json(text)` parses; `NameOf(root)` extracts the file name from `uploadedLogoUrl`. In the Api test host `TECHSTRAP_API_PUBLIC_URL` may be blank in Development so the URL base is the request origin `http://localhost`; check `ApiFactory` and, if it sets the public URL, assert against that value instead.)

`ProductLogoServingTests.cs`: copy `KbImageServingTests.cs` wholesale for `/product-logos/{name}` through `IProductLogoStore`: safe headers + immutable cache + sandbox + CORP + no `Set-Cookie`; HEAD mirrors GET headers with no body; a name that is not a logo name (`..%2Fkb-images%2Fx.png`, `0123456789abcdef0123456789abcdef.gif`, an unknown valid name, a trailing slash) is 404 `no-store`; POST is 405.

`ProductLogoDiskFullTests.cs`: copy `KbImageDiskFullTests.cs` with an admin token, a product created first through `POST /api/products`, `failOnStoreCall: 1`, the POST to `/api/products/{id}/logo`, and `Directory.Exists(Path.Combine(_storage, "product-logos"))` true with no files under `_storage`.

- [ ] **Step 2: Run to verify failure**

Run: `dotnet build tests/TechStrap.Infrastructure.IntegrationTests -c Release 2>&1 | grep -E "error|Build succeeded" | head -5`
Expected: compile errors (`IProductLogoStore`, `IncomingProductLogo`, `HostileUpload.ProductLogo` missing).

- [ ] **Step 3: Implement**

`CappedImageIntake.cs`:

```csharp
using System.Buffers;

namespace TechStrap.Infrastructure.Attachments;

/// <summary>An uploaded image read into memory: the bytes and the extension <see cref="KbImageSignatures.Identify"/> gives them (null when they are not an accepted image).</summary>
internal sealed record CappedImage(MemoryStream Content, string? Extension);

/// <summary>
/// The capped read shared by the KB image store and the product logo store (D-044, D-052): refuses a declared length over the cap, then reads at most cap + 1 bytes so a declared length that lies
/// cannot exhaust memory, and returns null when the real length is over the cap too. The caller decides the error code and which extensions it accepts.
/// </summary>
internal static class CappedImageIntake
{
    public static async Task<CappedImage?> ReadAsync(Stream content, long declaredLength, long maxBytes, CancellationToken cancellationToken)
    {
        if (declaredLength > maxBytes)
        {
            return null;
        }

        var copy = new MemoryStream((int)Math.Clamp(declaredLength, 0, maxBytes));
        var buffer = ArrayPool<byte>.Shared.Rent(81_920);
        try
        {
            long total = 0;
            while (total <= maxBytes)
            {
                var want = (int)Math.Min(buffer.Length, maxBytes + 1 - total);
                var read = await content.ReadAsync(buffer.AsMemory(0, want), cancellationToken);
                if (read == 0)
                {
                    break;
                }

                copy.Write(buffer, 0, read);
                total += read;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        if (copy.Length > maxBytes)
        {
            await copy.DisposeAsync();
            return null;
        }

        copy.Position = 0;
        return new CappedImage(copy, KbImageSignatures.Identify(copy.GetBuffer().AsSpan(0, (int)copy.Length)));
    }
}
```

`KbImageStore.SaveAsync` becomes: `var image = await CappedImageIntake.ReadAsync(incoming.Content, incoming.Length, KbLimits.MaxImageBytes, ct); if (image is null) return TooLarge(); await using var content = image.Content; if (image.Extension is null) return Failure("kb-image-type-not-allowed", ...);` then the existing name/store/delete-on-failure code with `content`. Every `KbImageStoreTests` test must stay green unchanged.

`IProductLogoStore.cs` (Application): the three records and the interface from the Interfaces block, with summaries modelled on `IKbImageStore.cs` (public-read `product-logos/` prefix, no database row for the file itself, the product row holds the name).

`ProductLogoStore.cs` (Infrastructure, `internal sealed class ProductLogoStore(IStorageProvider storage) : IProductLogoStore`):
- `SaveAsync`: `CappedImageIntake.ReadAsync(logo.Content, logo.Length, ProductLogoLimits.MaxBytes, ct)`; null -> `product-logo-too-large` ("This image is too large. Logos can be up to 1 MB."); extension null or `KbImageName.Gif` -> `product-logo-type-not-allowed` ("Only PNG, JPEG and WebP images can be uploaded."); name `$"{Guid.CreateVersion7():N}.{extension}"`, key `ProductLogoName.StorageKey`, content type `ProductLogoName.ContentTypeOf`; `storage.StoreAsync(new StoreObjectRequest(key, content, contentType), ct)` with the same delete-quietly-and-rethrow guard as the KB store.
- `OpenReadAsync`: `ProductLogoName.IsValid(fileName)` else null; `storage.ReadAsync(ProductLogoName.StorageKey(fileName), ct)`; wrap with `OwnedReadStream` as the KB store does.
- `DeleteAsync`: return when `!ProductLogoName.IsValid(fileName)`; `try { await storage.DeleteAsync(ProductLogoName.StorageKey(fileName), ct); } catch (Exception) { /* best effort */ }`.
- Register: `services.TryAddScoped<IProductLogoStore, ProductLogoStore>();` in `AddTechStrapAttachments`.

`ProductErrors.LogoFileRequired()` => `new("file-required", "Choose an image to upload.", ResultErrorKind.Validation, "file")`.

`UploadProductLogoRequestHandler(ICurrentAgentClaims currentAgent, IAgentRepository agents, IProductRepository products, IProductLogoStore store, IProductLogoUrls logoUrls, IAdminEventRepository adminEvents, IUnitOfWork unitOfWork, TimeProvider clock)`:

```csharp
        await using var scope = await unitOfWork.BeginAsync(cancellationToken);
        var actor = await CurrentAgent.RequireActiveAsync(currentAgent, agents, cancellationToken);
        if (actor.IsFailure) return Result<ProductDto>.Failure(actor.Errors[0]);
        var product = await products.GetByIdAsync(productId, cancellationToken);
        if (product is null) return Result<ProductDto>.Failure(ProductErrors.NotFound());
        if (logo is null) return Result<ProductDto>.Failure(ProductErrors.LogoFileRequired());
        var stored = await store.SaveAsync(logo, cancellationToken);
        if (stored.IsFailure) return Result<ProductDto>.Failure(stored.Errors[0]);
        var previous = product.Branding.UploadedLogo;
        product.SetUploadedLogo(stored.Value.FileName);
        products.Update(product);
        AdminAudit.Record(adminEvents, AdminEventType.ProductUpdated, actor.Value, AdminSubjectType.Product, product.Id, new { changed = new[] { "uploadedLogo" } }, clock);
        var committed = await scope.CommitAsync(cancellationToken);
        if (committed.IsFailure)
        {
            // The row did not change, so the new file is the orphan: remove it. The request token may be gone; the delete is best effort on its own token.
            await store.DeleteAsync(stored.Value.FileName, CancellationToken.None);
            return Result<ProductDto>.Failure(committed.Errors[0]);
        }
        if (previous is not null) await store.DeleteAsync(previous, CancellationToken.None);
        var saved = await products.GetByIdAsync(product.Id, cancellationToken) ?? product;
        return Result<ProductDto>.Success(ProductMapping.ToDto(saved, logoUrls));
```

(Check how `AdminAudit.Record` serialises `new { changed }` in the update handler and produce exactly `{"changed":["uploadedLogo"]}`: pass `new { changed = new List<string> { "uploadedLogo" } }` if the existing payload uses a `List<string>`.)

`RemoveProductLogoRequestHandler`: same preamble; `if (product.Branding.UploadedLogo is not { } previous) return Success(ToDto(product))`; `product.SetUploadedLogo(null); products.Update(product); audit; commit (failure -> return); await store.DeleteAsync(previous, CancellationToken.None); re-read; ToDto`.

`ProductsController`: add

```csharp
/// <summary>Multipart form posted to upload one product logo: the file in the field named <c>file</c>.</summary>
public sealed class ProductLogoForm
{
    public IFormFile? File { get; set; }
}
```

and the actions

```csharp
    /// <summary>One png, jpeg or webp up to 1 MiB, by its leading bytes (D-052). It supersedes the linked logo address; a second upload replaces the first.</summary>
    [HttpPost("{id:guid}/logo")]
    [Authorize(Policy = AuthorizationPolicies.Admin)]
    [ReadFormBeforeBinding]
    [RequestSizeLimit(ProductLogoRequestLimits.FormBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = ProductLogoRequestLimits.FormBytes * 2)]
    public async Task<IActionResult> UploadLogo(Guid id, [FromForm] ProductLogoForm form, [FromServices] IUploadProductLogoRequestHandler uploadLogo, CancellationToken cancellationToken)
    {
        if (form.File is not { } file)
        {
            return (await uploadLogo.HandleAsync(id, null, cancellationToken)).ToActionResult(this, Ok);
        }

        await using var stream = file.OpenReadStream();
        return (await uploadLogo.HandleAsync(id, new IncomingProductLogo(file.Length, stream), cancellationToken)).ToActionResult(this, Ok);
    }

    /// <summary>Removes the uploaded logo; the linked logo address, if any, shows again. Idempotent.</summary>
    [HttpDelete("{id:guid}/logo")]
    [Authorize(Policy = AuthorizationPolicies.Admin)]
    public async Task<IActionResult> RemoveLogo(Guid id, [FromServices] IRemoveProductLogoRequestHandler removeLogo, CancellationToken cancellationToken) =>
        (await removeLogo.HandleAsync(id, cancellationToken)).ToActionResult(this, Ok);
```

`ProductLogoRequestLimits.cs` and `ProductLogoEndpoints.cs`: copies of `KbRequestLimits.ImageFormBytes` and `KbImageEndpoints` for the logo (route `/product-logos/{name}`, `IProductLogoStore`, same headers, 404 `no-store`, trailing slash 404, `.AllowAnonymous().ExcludeFromDescription()`). `AttachmentSandbox._prefixes` gains `"/product-logos"`. `Program.cs`: `app.MapProductLogos();` directly after `app.MapKbImages();`.

`AgentAccessCoverageTests.AdminOnlyRoutes`: add `"DELETE api/products/{id:guid}/logo"` and `"POST api/products/{id:guid}/logo"` in sorted position.

`02-ARCHITECTURE.md`: in 7.2 after the `DELETE /api/products/{id}/api-keys/{keyId}` row add
`| \`POST /api/products/{id}/logo\` (Admin; multipart) | \`UploadProductLogoRequestHandler\` | \`IProductRepository\`, \`IProductLogoStore\`, \`IProductLogoUrls\`, \`IAdminEventRepository\`, \`IAgentRepository\`, \`ICurrentAgentClaims\` | EF repos, UoW, logo store (public-read \`product-logos/\` prefix) | 200 \`ProductDto\`; 400 \`product-logo-too-large\`, \`product-logo-type-not-allowed\` (SVG and GIF refused), \`file-required\`; 404; 413 over the request limit | H, C, I | D-052 |`
and
`| \`DELETE /api/products/{id}/logo\` (Admin) | \`RemoveProductLogoRequestHandler\` | \`IProductRepository\`, \`IProductLogoStore\`, \`IProductLogoUrls\`, \`IAdminEventRepository\`, \`IAgentRepository\`, \`ICurrentAgentClaims\` | EF repos, UoW, logo store | 200 \`ProductDto\` (idempotent); 404 | H, C, I | D-052 |`.
In 7.6 after the KB images row add `| Product logos under the public-read \`product-logos/\` prefix (API, \`GET /product-logos/{name}\`) | Public static assets served from storage by the API (D-052); written only by \`UploadProductLogoRequestHandler\`, removed by \`RemoveProductLogoRequestHandler\` | No application workflow; the prefix never holds ticket attachments |`. Beside the `IKbImageStore` abstraction row (~l.120) add `| \`IProductLogoStore\` | Over \`SyntaxCircus.Storage\` | Uploaded product logos under the public-read \`product-logos/\` prefix, served by \`GET /product-logos/{name}\` (D-052) |`.

- [ ] **Step 4: Build and test**

Run: `dotnet build TechStrap.slnx -c Release 2>&1 | grep -E " warning | error |Build succeeded"`; `dotnet test tests/TechStrap.Infrastructure.IntegrationTests -c Release --no-build --filter "FullyQualifiedName~ProductLogoStoreTests|FullyQualifiedName~KbImageStoreTests|FullyQualifiedName~HostileUploadCorpusTests|FullyQualifiedName~AttachmentStoreTests"`; `dotnet test tests/TechStrap.Application.Tests -c Release --no-build --filter "FullyQualifiedName~ProductLogo"`; `dotnet test tests/TechStrap.Api.Tests -c Release --no-build --filter "FullyQualifiedName~ProductLogo|FullyQualifiedName~AgentAccessCoverageTests|FullyQualifiedName~Intake"`; `dotnet test tests/TechStrap.Architecture.Tests -c Release --no-build`
Expected: green (the corpus column also runs through `PublicIntakeEndpointTests`, which reads `attachment` only).

- [ ] **Step 5: Mutation**

In `ProductLogoStore.SaveAsync` stop refusing `KbImageName.Gif`; `A_gif_an_svg_and_a_zero_byte_file_are_refused_as_type_not_allowed` and the corpus theory must fail. Restore.

- [ ] **Step 6: Commit**

```bash
git add src/TechStrap.Infrastructure/Attachments src/TechStrap.Application/Products src/TechStrap.Api/Controllers/ProductsController.cs src/TechStrap.Api/Startup src/TechStrap.Api/Program.cs tests/TechStrap.Api.Tests/Auth/AgentAccessCoverageTests.cs docs/architecture/02-ARCHITECTURE.md tests/Shared/HostileUploadCorpus.cs tests/Shared/Fixtures/hostile-uploads/manifest.json tests/TechStrap.Infrastructure.IntegrationTests/Products/ProductLogoStoreTests.cs tests/TechStrap.Application.Tests/Products tests/TechStrap.Api.Tests/Products
git commit -m "feat(api): uploaded product logos: store, admin upload and remove routes, anonymous serving (D-052)"
```
(plus the handler-registration file and any `HostileUploadCorpusTests` change.)

---

### Task 7: Worker and emails

**Files:**
- Create: `src/TechStrap.Application/Products/ProductLogoUrlOptions.cs`
- Create: `src/TechStrap.Infrastructure/Attachments/ConfiguredProductLogoUrls.cs` (+ registration method in `AttachmentServiceCollectionExtensions.cs`: `AddTechStrapProductLogoUrls(IConfiguration)`)
- Modify: `src/TechStrap.Worker/Program.cs` (call the registration after `AddTechStrapEmail`)
- Modify: `src/TechStrap.Application/Email/DrainEmailOutboxHandler.cs` (inject `IProductLogoUrls`, effective logo)
- Modify: `src/TechStrap.Worker/appsettings.json`, `src/TechStrap.Worker/.env.example`, `deploy/.env.worker.example`
- Modify: `scripts/tests/ConfigContract.Tests.ps1:44` (Worker `BlankKeys` + `TECHSTRAP_API_PUBLIC_URL`)
- Test: `tests/TechStrap.Application.Tests/Email/DrainEmailOutboxHandlerTests.cs` (constructor + two tests)
- Test: `tests/TechStrap.Infrastructure.IntegrationTests/Products/ConfiguredProductLogoUrlsTests.cs` (create; or in Application.Tests if Infrastructure has a plain unit-test file pattern; check where `PortalLinkOptions` is tested and sit beside it)

**Interfaces:**
- Consumes: `IProductLogoUrls`, `ProductLogos.EffectiveLogoUrl` (Task 5); `ProductLogoLimits.PathPrefix` (Task 2).
- Produces:
  ```csharp
  namespace TechStrap.Application.Products;
  public sealed class ProductLogoUrlOptions { public const string PublicUrlKey = "TECHSTRAP_API_PUBLIC_URL"; public string PublicUrl { get; set; } = string.Empty; }
  // Infrastructure: internal sealed class ConfiguredProductLogoUrls(IOptions<ProductLogoUrlOptions>) : IProductLogoUrls  -> "{PublicUrl trimmed of '/'}/product-logos/{name}", null when PublicUrl is blank
  public static IServiceCollection AddTechStrapProductLogoUrls(this IServiceCollection services, IConfiguration configuration); // binds PublicUrl from the key, validates "blank or absolute http(s) URL without user info, query or fragment", TryAddSingleton<IProductLogoUrls, ConfiguredProductLogoUrls>
  ```
  The Api keeps its own `ProductLogoUrls` (Task 5) and does not call this registration.

- [ ] **Step 1: Write the failing tests**

`DrainEmailOutboxHandlerTests`: add `private readonly IProductLogoUrls _logoUrls = Substitute.For<IProductLogoUrls>();` and pass it to the handler constructor after `_sender` (see Step 3 for the position); add

```csharp
    [Fact]
    public async Task With_an_uploaded_logo_and_a_known_api_address_the_email_shows_the_uploaded_logo()
    {
        _product.SetUploadedLogo("0123456789abcdef0123456789abcdef.png");
        _logoUrls.UrlFor("0123456789abcdef0123456789abcdef.png").Returns("https://api.test/product-logos/0123456789abcdef0123456789abcdef.png");
        Claims(Item());

        await _handler.HandleAsync("w1", TestContext.Current.CancellationToken);

        _renderer.Received(1).RenderTicketConfirmation(Arg.Any<TicketConfirmationEmail>(), Arg.Is<EmailBranding>(b => b.LogoPath == "https://api.test/product-logos/0123456789abcdef0123456789abcdef.png"));
    }

    [Fact]
    public async Task Without_an_api_address_the_email_keeps_the_linked_logo()
    {
        _product.SetUploadedLogo("0123456789abcdef0123456789abcdef.png");
        _logoUrls.UrlFor(Arg.Any<string>()).Returns((string?)null);
        Claims(Item());

        await _handler.HandleAsync("w1", TestContext.Current.CancellationToken);

        _renderer.Received(1).RenderTicketConfirmation(Arg.Any<TicketConfirmationEmail>(), Arg.Is<EmailBranding>(b => b.LogoPath == "https://cdn.orbitly.test/l.png"));
    }
```

`ConfiguredProductLogoUrlsTests`:

```csharp
    [Theory]
    [InlineData("https://api.example.com", "https://api.example.com/product-logos/0123456789abcdef0123456789abcdef.png")]
    [InlineData("https://api.example.com/", "https://api.example.com/product-logos/0123456789abcdef0123456789abcdef.png")]
    [InlineData("https://example.com/api", "https://example.com/api/product-logos/0123456789abcdef0123456789abcdef.png")]
    public void A_configured_address_gives_the_absolute_logo_url(string configured, string expected) =>
        new ConfiguredProductLogoUrls(Options.Create(new ProductLogoUrlOptions { PublicUrl = configured })).UrlFor("0123456789abcdef0123456789abcdef.png").ShouldBe(expected);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_address_gives_null_so_emails_keep_the_linked_logo(string configured) =>
        new ConfiguredProductLogoUrls(Options.Create(new ProductLogoUrlOptions { PublicUrl = configured })).UrlFor("0123456789abcdef0123456789abcdef.png").ShouldBeNull();
```

(`ConfiguredProductLogoUrls` is internal: add `InternalsVisibleTo` for the test assembly only if the Infrastructure project does not already expose internals to it; `KbImageStoreTests` tests an internal class, so it does.)

- [ ] **Step 2: Run to verify failure**

Run: `dotnet build tests/TechStrap.Application.Tests -c Release 2>&1 | grep -E "error|Build succeeded" | head -5`
Expected: compile errors (`ProductLogoUrlOptions`, `ConfiguredProductLogoUrls`, handler constructor).

- [ ] **Step 3: Implement**

`ProductLogoUrlOptions.cs` (Application): the class above with the summary "The Api's public address as the Worker knows it (<c>TECHSTRAP_API_PUBLIC_URL</c>, optional): the base of an uploaded logo's address in emails (D-052). Blank means emails show the linked logo only."

`ConfiguredProductLogoUrls.cs` (Infrastructure):

```csharp
internal sealed class ConfiguredProductLogoUrls(IOptions<ProductLogoUrlOptions> options) : IProductLogoUrls
{
    public string? UrlFor(string fileName)
    {
        var configured = options.Value.PublicUrl.Trim();
        if (configured.Length == 0)
        {
            return null;
        }

        // Built from the parsed address, never the raw string; start-up validation has already refused anything that is not an absolute http(s) URL.
        var baseUrl = new Uri(configured, UriKind.Absolute).GetLeftPart(UriPartial.Path).TrimEnd('/');
        return $"{baseUrl}/{ProductLogoLimits.PathPrefix}{fileName}";
    }
}
```

Registration in `AttachmentServiceCollectionExtensions`:

```csharp
    /// <summary>The Worker's view of uploaded logo addresses (D-052): optional TECHSTRAP_API_PUBLIC_URL; blank means null URLs and emails fall back to the linked logo.</summary>
    public static IServiceCollection AddTechStrapProductLogoUrls(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ProductLogoUrlOptions>()
            .Configure(options => options.PublicUrl = configuration[ProductLogoUrlOptions.PublicUrlKey]?.Trim() ?? string.Empty)
            .Validate(
                options => string.IsNullOrWhiteSpace(options.PublicUrl)
                    || (Uri.TryCreate(options.PublicUrl, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0),
                $"{ProductLogoUrlOptions.PublicUrlKey} must be blank or an absolute http or https URL without user info, query or fragment.")
            .ValidateOnStart();
        services.TryAddSingleton<IProductLogoUrls, ConfiguredProductLogoUrls>();
        return services;
    }
```

`Worker/Program.cs`: after `builder.Services.AddTechStrapEmail(builder.Configuration);` add `builder.Services.AddTechStrapProductLogoUrls(builder.Configuration);` with the comment "Uploaded product logos in emails (D-052): optional; blank keeps the linked logo." and the `using TechStrap.Infrastructure.Attachments;`.

`DrainEmailOutboxHandler`: constructor gains `IProductLogoUrls logoUrls` after `IOutboundEmailSender sender`; `var emailBranding = new EmailBranding(branding.DisplayName, ProductLogos.EffectiveLogoUrl(branding, logoUrls), branding.AccentColour, branding.FromAddress, branding.ReplyTo);`.

`Worker/appsettings.json`: add `"TECHSTRAP_API_PUBLIC_URL": "",` after `"TECHSTRAP_AUTOCLOSE_DAYS"`. `src/TechStrap.Worker/.env.example`: a block

```
# --- Uploaded product logos in emails (D-052, optional) ---
# The Api's public address, the same value as the Api's TECHSTRAP_API_PUBLIC_URL. Blank: emails show a product's linked logo address only.
TECHSTRAP_API_PUBLIC_URL=
```

and in `deploy/.env.worker.example` the same key under a `# -- Uploaded product logos in emails [Worker] (optional, D-052) --` heading (commented out as `# TECHSTRAP_API_PUBLIC_URL=` if optional keys are commented in that template; follow the template's own convention, stated in its header). Reword line 5 of both files: "The Worker never migrates the database and reads no storage setting; its only portal-facing setting is the optional Api public URL for uploaded logos in emails." `ConfigContract.Tests.ps1:44`: append `'TECHSTRAP_API_PUBLIC_URL'` to the Worker `BlankKeys` list.

- [ ] **Step 4: Build and test**

Run: `dotnet build TechStrap.slnx -c Release 2>&1 | grep -E " warning | error |Build succeeded"`; `dotnet test tests/TechStrap.Application.Tests -c Release --no-build --filter "FullyQualifiedName~DrainEmailOutboxHandlerTests"`; `dotnet test tests/TechStrap.Infrastructure.IntegrationTests -c Release --no-build --filter "FullyQualifiedName~ConfiguredProductLogoUrlsTests"`; `dotnet test tests/TechStrap.Worker.Tests -c Release --no-build` (if the project exists: the Worker host must start with the new registration); `pwsh -NoProfile -Command "Invoke-Pester -Path scripts/tests/ConfigContract.Tests.ps1 -Output Minimal"`
Expected: green.

- [ ] **Step 5: Mutation**

In `DrainEmailOutboxHandler` pass `branding.LogoPath` instead of the effective URL; `With_an_uploaded_logo_and_a_known_api_address_the_email_shows_the_uploaded_logo` must fail. Restore.

- [ ] **Step 6: Commit**

```bash
git add src/TechStrap.Application/Products/ProductLogoUrlOptions.cs src/TechStrap.Infrastructure/Attachments src/TechStrap.Worker/Program.cs src/TechStrap.Application/Email/DrainEmailOutboxHandler.cs src/TechStrap.Worker/appsettings.json src/TechStrap.Worker/.env.example deploy/.env.worker.example scripts/tests/ConfigContract.Tests.ps1 tests/TechStrap.Application.Tests/Email/DrainEmailOutboxHandlerTests.cs tests/TechStrap.Infrastructure.IntegrationTests/Products/ConfiguredProductLogoUrlsTests.cs
git commit -m "feat(worker): uploaded product logo in emails through the optional TECHSTRAP_API_PUBLIC_URL (D-052)"
```

---

### Task 8: Portal landing page

**Files:**
- Modify: `src/TechStrap.Portal/Settings/PortalOptions.cs`, `PortalOptionsValidator.cs`, `PortalOptionsRegistration.cs`
- Create: `src/TechStrap.Portal/Settings/PortalLandingModes.cs`
- Modify: `src/TechStrap.Portal/appsettings.json` (`"TECHSTRAP_PORTAL_LANDING": "Neutral"`), `src/TechStrap.Portal/.env.example`, `deploy/.env.portal.example`
- Modify: `src/TechStrap.Portal/Components/Pages/Home.razor`, `Home.razor.cs`
- Create: `src/TechStrap.Portal/Components/Pages/LandingCardViewModel.cs`
- Modify: `src/TechStrap.Portal/Components/ShellCopy.cs`
- Modify: `src/TechStrap.Portal/Products/ProductThemeViewModel.cs` (`AcceptableLogo` becomes `internal static string? AcceptableLogoUrl(string? logoUrl, bool allowLoopbackImages)`)
- Modify: `src/TechStrap.Portal/Headers/PortalHeaderRules.cs` (`Rules` becomes `Rules(PortalOptions options)`), `src/TechStrap.Portal/Program.cs:78`
- Modify: `src/TechStrap.Portal/Caching/PortalCachePaths.cs` (`IsCacheable` + `IsRoot`)
- Modify: `src/TechStrap.Portal/Clients/IPublicProductClient.cs` (ListAsync doc), `src/TechStrap.Portal/Styles/_components.scss` (`.ts-landing*`)
- Test: `tests/TechStrap.Portal.Tests/Products/RootPageHostTests.cs` (keep every existing test; add the Products-mode class below), `tests/TechStrap.Portal.Tests/Products/LandingPageHostTests.cs` (create), `tests/TechStrap.Portal.Tests/Products/NeutralPagesGuardTests.cs` (unchanged; it runs in Neutral mode by default)

**Interfaces:**
- Consumes: `PublicProductSummaryDto` fields (Task 2), `PortalLinks.ForListedProduct`, `PortalLinks.Absolute`, `PortalLinks.ProductHome`, `ProductHostContext`, `ProductHostMap` (existing).
- Produces:
  ```csharp
  public static class PortalLandingModes { public const string Neutral = "Neutral"; public const string Products = "Products"; public static bool IsKnown(string? value); }
  // PortalOptions
  public const string LandingKey = "TECHSTRAP_PORTAL_LANDING";
  public string Landing { get; set; } = PortalLandingModes.Neutral;
  public bool ListsProducts => string.Equals(Landing?.Trim(), PortalLandingModes.Products, StringComparison.OrdinalIgnoreCase);
  internal sealed record LandingCardViewModel(string Key, string DisplayName, string? Tagline, string? LogoUrl, string Href);
  public static IReadOnlyList<PathHeaderRule> PortalHeaderRules.Rules(PortalOptions options);
  public static bool PortalCachePaths.IsRoot(PathString path);   // "/" or "" only
  // ShellCopy
  public const string LandingHeading = "Support";
  public const string LandingIntro = "Choose your product to find help articles or contact support.";
  ```

- [ ] **Step 1: Write the failing tests**

`tests/TechStrap.Portal.Tests/Products/LandingPageHostTests.cs`:

```csharp
using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using TechStrap.Contracts.Products;
using TechStrap.Portal.Settings;
using TechStrap.Tests.Shared;

namespace TechStrap.Portal.Tests.Products;

/// <summary>D-052: with TECHSTRAP_PORTAL_LANDING=Products the default host's root lists the listed products as cards; a product host, an unlisted product, an empty list and a failed list never show one.</summary>
public sealed class LandingPageHostTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static Dictionary<string, string?> Products(string? defaultProduct = null) => new()
    {
        [PortalOptions.LandingKey] = "Products",
        [PortalOptions.DefaultProductKey] = defaultProduct,
    };

    private static PublicProductSummaryDto[] List() =>
    [
        new("acme", "Acme Corp", null, "Tickets for Acme.", "https://cdn.acme.test/logo.png", "#7C3AED"),
        new("hidden", "Hidden One", null, "Never shown", null, "#000000", ListedOnLanding: false),
        new("paperplane", "Paperplane", "support.paperplane.test", null, "http://insecure.test/logo.png", "#F59E0B"),
    ];

    [Fact]
    public async Task The_root_lists_the_listed_products_as_cards_in_api_order_with_safe_logos_only()
    {
        await using var factory = new PortalFactory(settings: Products());
        factory.Api.OnJson(HttpMethod.Get, "/api/public/products", List());
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await client.GetAsync("/", Ct);
        var html = await response.Content.ReadAsStringAsync(Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain("<h1>Support</h1>");
        html.ShouldContain("Choose your product");
        html.ShouldContain("class=\"ts-landing-card\" href=\"/p/acme\"");
        html.ShouldContain("Acme Corp");
        html.ShouldContain("Tickets for Acme.");
        html.ShouldContain("src=\"https://cdn.acme.test/logo.png\"");
        html.ShouldContain("class=\"ts-landing-card\" href=\"https://support.paperplane.test/\"");
        html.ShouldNotContain("insecure.test");
        html.ShouldNotContain("Hidden One");
        html.ShouldNotContain("/p/hidden");
        html.IndexOf("Acme Corp", StringComparison.Ordinal).ShouldBeLessThan(html.IndexOf("Paperplane", StringComparison.Ordinal));
        html.ShouldContain("rel=\"canonical\" href=\"https://portal.test/\"");
        response.Headers.Contains("Set-Cookie").ShouldBeFalse();
        factory.Api.Count(HttpMethod.Get, "/api/public/products").ShouldBe(1);
    }

    [Fact]
    public async Task A_second_request_is_served_from_the_output_cache_with_a_public_minute_and_a_query_string_is_not_cached()
    {
        await using var factory = new PortalFactory(settings: Products());
        factory.Api.OnJson(HttpMethod.Get, "/api/public/products", List());
        using var client = factory.CreateClient();

        using var first = await client.GetAsync("/", Ct);
        using var second = await client.GetAsync("/", Ct);
        using var query = await client.GetAsync("/?utm=1", Ct);
        using var query2 = await client.GetAsync("/?utm=2", Ct);

        first.Headers.CacheControl!.ToString().ShouldBe("public, max-age=60");
        second.Headers.Age.ShouldNotBeNull("the second request is a cache hit");
        factory.Api.Count(HttpMethod.Get, "/api/public/products").ShouldBe(3, "one build for the two plain requests, one per query variant");
    }

    [Theory]
    [InlineData(200, "[]")]
    [InlineData(429, null)]
    [InlineData(503, null)]
    public async Task An_empty_list_or_a_failed_list_renders_the_neutral_copy_with_200(int status, string? body)
    {
        await using var factory = new PortalFactory(settings: Products());
        if (body is null) factory.Api.OnStatus(HttpMethod.Get, "/api/public/products", (HttpStatusCode)status); else factory.Api.OnJson(HttpMethod.Get, "/api/public/products", Array.Empty<PublicProductSummaryDto>());
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/", Ct);
        var html = await response.Content.ReadAsStringAsync(Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain("the link in your email");
        html.ShouldNotContain("ts-landing-card");
    }

    [Fact]
    public async Task A_product_host_root_is_its_product_home_never_the_landing()
    {
        await using var factory = new PortalFactory(settings: Products());
        factory.Api.OnJson(HttpMethod.Get, "/api/public/products", List());
        factory.Api.OnJson(HttpMethod.Get, "/api/public/products/paperplane", new PublicProductDto("paperplane", "Paperplane", null, "#F59E0B", "#000000", "#9D6507", "support.paperplane.test"));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Host = "support.paperplane.test";

        var html = await client.GetStringAsync("/", Ct);

        html.ShouldContain("How can we help?");
        html.ShouldNotContain("ts-landing-card");
        html.ShouldNotContain("Acme Corp");
    }

    [Fact]
    public async Task Neutral_mode_is_the_page_it_always_was_and_never_calls_the_api()
    {
        await using var factory = new PortalFactory(settings: new Dictionary<string, string?> { [PortalOptions.LandingKey] = "Neutral" });
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/", Ct);

        html.ShouldContain("the link in your email");
        html.ShouldNotContain("ts-landing");
        factory.Api.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Products_with_a_default_product_stops_the_host_at_start()
    {
        await using var factory = new PortalFactory(settings: Products("paperplane"));

        StartupFailure.Capture(factory, () => factory.LogSink.Events)
            .ShouldSatisfyAllConditions(
                text => text.ShouldContain(PortalOptions.LandingKey),
                text => text.ShouldContain(PortalOptions.DefaultProductKey));
    }

    [Fact]
    public async Task An_unknown_landing_value_stops_the_host_at_start()
    {
        await using var factory = new PortalFactory(settings: new Dictionary<string, string?> { [PortalOptions.LandingKey] = "List" });

        StartupFailure.Capture(factory, () => factory.LogSink.Events).ShouldContain(PortalOptions.LandingKey);
    }
}
```

(Look at `RootPageHostTests.A_default_product_that_is_not_a_slug_stops_the_host_at_start` for the exact `StartupFailure.Capture` return type and adapt the assertions; look at `Hosting/ProductHostHostTests.cs` for how a product host request is made and whether the host map needs a warm-up request first. If `factory.Api.Count` does not exist, count `factory.Api.Requests` entries with that path.)

- [ ] **Step 2: Run to verify failure**

Run: `dotnet build tests/TechStrap.Portal.Tests -c Release 2>&1 | grep -E "error|Build succeeded" | head -5`
Expected: compile error (`PortalOptions.LandingKey`).

- [ ] **Step 3: Implement**

`PortalLandingModes.cs`:

```csharp
namespace TechStrap.Portal.Settings;

/// <summary>The two values of <c>TECHSTRAP_PORTAL_LANDING</c> (D-052): the neutral root of D-045, or a list of the products whose <c>ListedOnLanding</c> is true.</summary>
public static class PortalLandingModes
{
    public const string Neutral = "Neutral";
    public const string Products = "Products";

    public static bool IsKnown(string? value) =>
        string.Equals(value?.Trim(), Neutral, StringComparison.OrdinalIgnoreCase) || string.Equals(value?.Trim(), Products, StringComparison.OrdinalIgnoreCase);
}
```

`PortalOptions`: `/// <summary>What the Portal's root shows (D-052): <c>Neutral</c> (the default) or <c>Products</c>, a list of the listed products. Mutually exclusive with <see cref="DefaultProductKey"/>.</summary> public const string LandingKey = "TECHSTRAP_PORTAL_LANDING";`, `public string Landing { get; set; } = PortalLandingModes.Neutral;`, `public bool ListsProducts => ...`. Reword the `DefaultProductKey` summary: "Optional: blank shows the neutral page, or the product list when `TECHSTRAP_PORTAL_LANDING=Products`."

`PortalOptionsRegistration`: `options.Landing = configuration[PortalOptions.LandingKey] is { } landing && !string.IsNullOrWhiteSpace(landing) ? landing.Trim() : PortalLandingModes.Neutral;` and update the summary ("its four keys").

`PortalOptionsValidator`: after the default-product check add

```csharp
        if (!PortalLandingModes.IsKnown(options.Landing))
        {
            failures.Add($"{PortalOptions.LandingKey} must be {PortalLandingModes.Neutral} or {PortalLandingModes.Products}.");
        }
        else if (options.ListsProducts && options.DefaultProductKeyOrNull is not null)
        {
            failures.Add($"{PortalOptions.LandingKey}={PortalLandingModes.Products} and {PortalOptions.DefaultProductKey} cannot both be set: the root either lists the products or redirects to one. Blank one of them.");
        }
```

`appsettings.json`: `"TECHSTRAP_PORTAL_LANDING": "Neutral",` after the default-product key. `src/TechStrap.Portal/.env.example` and `deploy/.env.portal.example`: next to the default-product block add

```
# -- Landing page [Portal] (D-052) --
# Neutral (default): the root is a neutral page. Products: the root lists the products whose "Listed on the landing page" flag is on, as cards. Cannot be Products while a default product is set.
TECHSTRAP_PORTAL_LANDING=Neutral
```

and change the default-product comment to "Optional: a product key. When set, the Portal's root page redirects to /p/<key>; blank shows the neutral page, or the product list when TECHSTRAP_PORTAL_LANDING=Products." (both files, and `PortalOptions.cs:15`, `Home.razor.cs` summary, `ShellCopy.cs:9` comment, `IPublicProductClient.ListAsync` doc: "Feeds the sitemap, the product-host map and, in Products mode, the landing page (D-052).", `PublicProductSummaryDto` was reworded in Task 2).

`ShellCopy.cs`: replace the root comment with `// The root page: the neutral copy (TECHSTRAP_PORTAL_LANDING=Neutral, or nothing to list), and the landing page (Products, D-052). Inactive products never appear.` and add after `RootIntro`:

```csharp
    public const string LandingHeading = "Support";
    public const string LandingIntro = "Choose your product to find help articles or contact support.";
```

`LandingCardViewModel.cs` (`src/TechStrap.Portal/Components/Pages/`):

```csharp
namespace TechStrap.Portal.Components.Pages;

/// <summary>One card of the landing page (D-052): text the page encodes, a logo address that already passed the https rule, and the href <c>PortalLinks</c> built. Presentation only.</summary>
internal sealed record LandingCardViewModel(string Key, string DisplayName, string? Tagline, string? LogoUrl, string Href);
```

`ProductThemeViewModel`: rename the private `AcceptableLogo` to `internal static string? AcceptableLogoUrl(string? logoUrl, bool allowLoopbackImages)` (same body) and call it from `From`.

`Home.razor.cs`:

```csharp
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Options;
using TechStrap.Portal.Clients;
using TechStrap.Portal.Hosting;
using TechStrap.Portal.Products;
using TechStrap.Portal.Routing;
using TechStrap.Portal.Settings;

namespace TechStrap.Portal.Components.Pages;

/// <summary>
/// The Portal's root. With <c>TECHSTRAP_PORTAL_DEFAULT_PRODUCT</c> set it sends the visitor to that product's home (a 302; the API is not asked). With <c>TECHSTRAP_PORTAL_LANDING=Products</c> (D-052) it lists the
/// active products whose <c>ListedOnLanding</c> is true as cards; an empty list, a 429 or any API failure renders the neutral copy with 200. Otherwise it is the neutral page of D-045. On a product host the
/// middleware has already rewritten <c>/</c> to that product's home, so the list is never rendered there; the guard below is belt and braces.
/// </summary>
public partial class Home
{
    private IReadOnlyList<LandingCardViewModel> _cards = [];

    [Inject]
    private IOptions<PortalOptions> Portal { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [Inject]
    private PortalLinks Links { get; set; } = default!;

    [Inject]
    private IPublicProductClient Products { get; set; } = default!;

    [Inject]
    private ProductHostContext Host { get; set; } = default!;

    [Inject]
    private ProductHostMap HostMap { get; set; } = default!;

    [Inject]
    private IHostEnvironment Environment { get; set; } = default!;

    [CascadingParameter]
    private HttpContext? HttpContext { get; set; }

    private bool ShowsCards => _cards.Count > 0;

    protected override async Task OnInitializedAsync()
    {
        if (Portal.Value.DefaultProductKeyOrNull is { } key)
        {
            Navigation.NavigateTo(Links.ProductHome(key));
            return;
        }

        if (!Portal.Value.ListsProducts || Host.IsProductHost)
        {
            return;
        }

        var listed = await Products.ListAsync(HttpContext?.RequestAborted ?? CancellationToken.None);
        if (listed.IsFailure)
        {
            return;
        }

        _cards = [.. listed.Value
            .Where(product => product.ListedOnLanding)
            .Select(product => new LandingCardViewModel(
                product.Key,
                string.IsNullOrWhiteSpace(product.DisplayName) ? product.Key : product.DisplayName.Trim(),
                string.IsNullOrWhiteSpace(product.Tagline) ? null : product.Tagline.Trim(),
                ProductThemeViewModel.AcceptableLogoUrl(product.LogoUrl, Environment.IsDevelopment()),
                HrefFor(product)))];
    }

    // A hosted product's card goes to its own host (an absolute https address built from the stored host, never from a header); the others stay on this host under /p/{key}.
    private string HrefFor(Contracts.Products.PublicProductSummaryDto product)
    {
        var host = string.IsNullOrWhiteSpace(product.PortalHost) ? null : product.PortalHost.Trim().ToLowerInvariant();
        if (host is null)
        {
            return Links.ProductHome(product.Key);
        }

        var links = PortalLinks.ForListedProduct(HostMap, Portal, product.Key, host);
        return links.Absolute(links.ProductHome(product.Key));
    }
}
```

(If `ProductPageBase` resolves the request token or the environment differently, follow it. If `[CascadingParameter] HttpContext` is not how other pages read the request token, use what they use.)

`Home.razor`:

```razor
@attribute [Route(PortalRoutes.HomeTemplate)]

@if (ShowsCards)
{
    <SeoHead Title="@ShellCopy.LandingHeading" Description="@ShellCopy.LandingIntro" RelativeUrl="@Links.Absolute(PortalRoutes.HomeTemplate)" />
    <PageTitle>@ShellCopy.LandingHeading</PageTitle>
    <h1>@ShellCopy.LandingHeading</h1>
    <p class="ts-landing-intro">@ShellCopy.LandingIntro</p>
    <section class="ts-landing" aria-label="@ShellCopy.LandingHeading">
        @foreach (var card in _cards)
        {
            <a class="ts-landing-card" href="@card.Href" data-product="@card.Key">
                @if (card.LogoUrl is { } logo)
                {
                    <img class="ts-landing-logo" src="@logo" alt="" height="40" loading="lazy" />
                }
                <span class="ts-landing-name">@card.DisplayName</span>
                @if (card.Tagline is { } tagline)
                {
                    <span class="ts-landing-tagline">@tagline</span>
                }
            </a>
        }
    </section>
}
else
{
    <PageTitle>@ShellCopy.RootTitle</PageTitle>
    <section class="shell-placeholder">
        <h1>@ShellCopy.RootHeading</h1>
        <p>@ShellCopy.RootIntro</p>
    </section>
}
```

(`SeoHead`'s parameters are those used in `KbHome.razor:11`; `PortalRoutes.HomeTemplate` is a `*Template` constant, allowed by `PortalRules`.)

`_components.scss`: a small block

```scss
.ts-landing { display: grid; gap: 1rem; grid-template-columns: repeat(auto-fill, minmax(16rem, 1fr)); margin-top: 1.5rem; }
.ts-landing-card { display: flex; flex-direction: column; gap: .5rem; padding: 1.25rem; border: 1px solid var(--ts-border, #d0d7de); border-radius: .5rem; color: inherit; text-decoration: none; }
.ts-landing-card:hover, .ts-landing-card:focus-visible { border-color: var(--ts-accent, #1f6feb); }
.ts-landing-logo { height: 2.5rem; width: auto; align-self: flex-start; }
.ts-landing-name { font-weight: 600; }
.ts-landing-tagline { color: var(--ts-muted, #57606a); }
```

(Use the token names `_tokens.scss` defines; the CSS is compiled by the SassCompiler on build.)

`PortalHeaderRules`: change the property to

```csharp
    /// <summary>The rules for <c>UseTechStrapWebHost</c>: the ticket headers, the attachment sandbox, the form pages' headers, the help centre's cache headers and, in Products mode (D-052), the landing page's.</summary>
    public static IReadOnlyList<PathHeaderRule> Rules(PortalOptions options) =>
    [
        ... the four existing rules ...,
        // The landing page is the same for every visitor and is kept for a minute like a help-centre page; the Neutral root never gets a public header.
        PathHeaderRule.SetOnSuccess(path => options.ListsProducts && PortalCachePaths.IsRoot(path), ("Cache-Control", PortalCachePaths.BrowserCacheControl)),
    ];
```

`Program.cs:78`: `app.UseTechStrapWebHost(PortalHeaderRules.Rules(app.Services.GetRequiredService<IOptions<PortalOptions>>().Value));`.

`PortalCachePaths`: add `public static bool IsRoot(PathString path) => !path.HasValue || path.Value == "/";` and at the top of `IsCacheable`:

```csharp
        // The landing page (D-052): only the bare root, with no query string at all (so ?utm=... cannot fill the store), and only in Products mode.
        if (IsRoot(request.Path))
        {
            return !request.QueryString.HasValue && request.HttpContext.RequestServices.GetRequiredService<IOptions<PortalOptions>>().Value.ListsProducts;
        }
```

Extend the class summary with the landing page.

- [ ] **Step 4: Build and test**

Run: `dotnet build TechStrap.slnx -c Release 2>&1 | grep -E " warning | error |Build succeeded"`; `dotnet test tests/TechStrap.Portal.Tests -c Release --no-build`; `dotnet test tests/TechStrap.Architecture.Tests -c Release --no-build`; `pwsh -NoProfile -Command "Invoke-Pester -Path scripts/tests/ConfigContract.Tests.ps1 -Output Minimal"`
Expected: green, including `RootPageHostTests`, `NeutralPagesGuardTests`, `RouteLiteralTests`, `PortalLinkRuleTests`, `SeoHostTests` and `SitemapHostTests` unchanged.

- [ ] **Step 5: Mutation**

Remove `|| Host.IsProductHost` from the guard and make `ProductHostMiddleware` irrelevant by requesting `/p/paperplane`? No: instead drop `.Where(product => product.ListedOnLanding)`; `The_root_lists_the_listed_products_as_cards_in_api_order_with_safe_logos_only` must fail on "Hidden One". Restore.

- [ ] **Step 6: Commit**

```bash
git add src/TechStrap.Portal tests/TechStrap.Portal.Tests/Products/LandingPageHostTests.cs tests/TechStrap.Portal.Tests/Products/RootPageHostTests.cs deploy/.env.portal.example
git commit -m "feat(portal): TECHSTRAP_PORTAL_LANDING=Products lists the products as cards on the root (D-052)"
```

---

### Task 9: Admin editor, logo upload button, products list

**Files:**
- Modify: `src/TechStrap.Admin/Clients/ReferenceDataClients.cs` (`IProductsClient` + `ProductsClient`: `UploadLogoAsync`, `RemoveLogoAsync`; record `ProductLogoFile`)
- Modify: `src/TechStrap.Admin/Clients/ApiFields.cs` (`Tagline = "tagline"`), `src/TechStrap.Admin/Clients/ApiErrorCodes.cs` (`ProductLogoTypeNotAllowed`, `ProductLogoTooLarge`, `FileRequired` if absent)
- Modify: `src/TechStrap.Admin/Features/Settings/Products/ProductEditorViewModel.cs`, `ProductFields.cs`, `ProductsCopy.cs`, `ProductEditorContent.razor`, `ProductEditorContent.razor.cs`, `ProductRowViewModel.cs`, `ProductsContent.razor`
- Create: `src/TechStrap.Admin/Features/Settings/Products/ProductLogoUploadButton.razor`, `ProductLogoUploadButton.razor.cs`
- Test: `tests/TechStrap.Admin.Tests/Components/ProductEditorTests.cs` (add tests), `tests/TechStrap.Admin.Tests/Components/ProductLogoUploadButtonTests.cs` (create; mirror the KB upload button tests found with `grep -rl KbImageUploadButton tests/TechStrap.Admin.Tests`), `tests/TechStrap.Admin.Tests/Clients/ProductsClientTests.cs` (two tests), `tests/TechStrap.Admin.Tests/Components/ProductsPageTests.cs` (Listed column)
- Modify: `tests/TechStrap.Admin.Tests/Support/TestData.Products.cs` (`ProductDetail` gains `tagline`, `uploadedLogoUrl`, `listed` optional parameters)

**Interfaces:**
- Consumes: Contracts 0.3.0 records (Task 2), `ProductLogoLimits`, `ProductLogoName` (Task 2), the routes `POST/DELETE api/products/{id}/logo` (Task 6).
- Produces:
  ```csharp
  public sealed record ProductLogoFile(string FileName, string ContentType, Func<Stream> OpenRead);
  // IProductsClient
  Task<Result<ProductDto>> UploadLogoAsync(Guid productId, ProductLogoFile file, CancellationToken cancellationToken);   // POST api/products/{id}/logo, multipart part "file"
  Task<Result<ProductDto>> RemoveLogoAsync(Guid productId, CancellationToken cancellationToken);                        // DELETE api/products/{id}/logo
  // ProductEditorViewModel
  public string Tagline { get; set; } = string.Empty;
  public bool ListedOnLanding { get; set; } = true;
  public string? UploadedLogoUrl { get; set; }
  // element ids: ts-product-tagline, ts-product-listed, ts-product-logo-upload (the InputFile), ts-product-logo-remove (the button)
  // ApiFields.Tagline = "tagline"; ApiErrorCodes.ProductLogoTypeNotAllowed = "product-logo-type-not-allowed"; ApiErrorCodes.ProductLogoTooLarge = "product-logo-too-large"; ApiErrorCodes.FileRequired = "file-required"
  ```

- [ ] **Step 1: Write the failing tests**

`TestData.Products.cs`: extend `ProductDetail(...)` with `string? tagline = null, string? uploadedLogoUrl = null, bool listed = true` and pass them into `ProductBrandingDto(..., tagline, uploadedLogoUrl)` and `ProductDto(..., listed)`.

`ProductEditorTests.cs` additions:

```csharp
    [Fact]
    public void A_new_product_sends_the_listed_flag_explicitly_and_the_tagline_trimmed()
    {
        var cut = RenderNew();
        Type(cut, "ts-product-key", "orbitly");
        Type(cut, "ts-product-name", "Orbitly");
        Type(cut, "ts-product-prefix", "ORB");
        Type(cut, "ts-product-display", "Orbitly");
        Type(cut, "ts-product-tagline", "  Tickets for the app.  ");
        cut.Find("#ts-product-listed").Change(false);
        Save(cut);

        var sent = Creates.ShouldHaveSingleItem();
        sent.ListedOnLanding.ShouldBe(false);
        sent.Branding!.Tagline.ShouldBe("Tickets for the app.");
    }

    [Fact]
    public void An_edit_sends_the_listed_flag_as_shown_and_a_blank_tagline_as_null()
    {
        _products.GetAsync(TestData.OrbitlyId, Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.ProductDetail(tagline: "Old", listed: false)));
        var cut = RenderEdit();
        cut.Find("#ts-product-listed").GetAttribute("checked").ShouldBeNull();
        Value(cut, "ts-product-tagline").ShouldBe("Old");

        Type(cut, "ts-product-tagline", "   ");
        Save(cut);

        var sent = Updates.ShouldHaveSingleItem();
        sent.ListedOnLanding.ShouldBe(false);
        sent.Branding.Tagline.ShouldBeNull();
    }

    [Fact]
    public void A_tagline_over_160_characters_or_with_a_line_break_blocks_the_save_at_the_field()
    {
        var cut = RenderEdit();

        Type(cut, "ts-product-tagline", new string('a', 161));
        Save(cut);

        FieldError(cut, "ts-product-tagline").ShouldBe(ProductsCopy.TaglineInvalid);
        Updates.ShouldBeEmpty();
    }

    [Fact]
    public void The_preview_prefers_the_uploaded_logo_and_the_new_form_has_no_upload_control()
    {
        _products.GetAsync(TestData.OrbitlyId, Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.ProductDetail(logo: "https://cdn.example.com/linked.png", uploadedLogoUrl: "https://api.test/product-logos/0123456789abcdef0123456789abcdef.png")));
        var edit = RenderEdit();
        edit.Find(".ts-accent-preview img").GetAttribute("src").ShouldBe("https://api.test/product-logos/0123456789abcdef0123456789abcdef.png");
        edit.FindAll("#ts-product-logo-upload").Count.ShouldBe(1);
        edit.FindAll("#ts-product-logo-remove").Count.ShouldBe(1);

        var create = RenderNew();
        create.FindAll("#ts-product-logo-upload").ShouldBeEmpty();
        create.Markup.ShouldContain(ProductsCopy.LogoUploadAfterSave);
    }

    [Fact]
    public async Task Removing_the_uploaded_logo_splices_the_new_version_and_keeps_a_pending_edit()
    {
        _products.GetAsync(TestData.OrbitlyId, Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.ProductDetail(version: 7, uploadedLogoUrl: "https://api.test/product-logos/0123456789abcdef0123456789abcdef.png")));
        _products.RemoveLogoAsync(TestData.OrbitlyId, Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.ProductDetail(version: 8)));
        var cut = RenderEdit();
        Type(cut, "ts-product-name", "Orbitly Cloud");

        await cut.Find("#ts-product-logo-remove").ClickAsync(new());
        cut.WaitForAssertion(() => cut.FindAll(".ts-accent-preview img").ShouldBeEmpty());
        Save(cut);

        Value(cut, "ts-product-name").ShouldBe("Orbitly Cloud");
        var sent = Updates.ShouldHaveSingleItem();
        sent.Name.ShouldBe("Orbitly Cloud");
        sent.Version.ShouldBe(8u);
    }
```

(`.ts-accent-preview img` is a guess at `AccentPreview`'s markup: open `src/TechStrap.Admin/Components/Ui/AccentPreview.razor` and use its real logo selector. Follow the file's existing helpers for clicking and awaiting; the KB upload button tests show the `InputFile` idiom with `InputFileChangeEventArgs`.)

`ProductLogoUploadButtonTests.cs`: copy the KB upload button test class and adapt: picking `logo.gif` shows `ProductsCopy.LogoTypeNotAllowed` and calls nothing; a file over 1 MiB shows `ProductsCopy.LogoTooLarge` and calls nothing; a valid PNG calls `UploadLogoAsync(productId, file with FileName "logo.png", ...)` once and raises `OnChanged` with the returned `ProductDto`; a `product-logo-type-not-allowed` result shows that copy; `OnUploadingChanged` fires true then false.

`ProductsClientTests.cs`: `UploadLogoAsync_posts_multipart_with_the_file_part_named_file` (the request to `api/products/{id}/logo` is multipart, the part's name is `file`, the file name is the cleaned name) and `RemoveLogoAsync_sends_delete_and_returns_the_product` (DELETE to `api/products/{id}/logo`, 200 body deserialised). Mirror the existing `KbClientTests` upload test.

`ProductsPageTests.cs`: `The_list_shows_whether_a_product_is_listed_on_the_landing_page` (a row with `listed: false` shows the `ProductsCopy.Hidden` pill in a `Landing` column, a listed one `ProductsCopy.Listed`).

- [ ] **Step 2: Run to verify failure**

Run: `dotnet build tests/TechStrap.Admin.Tests -c Release 2>&1 | grep -E "error|Build succeeded" | head -5`
Expected: compile errors (`UploadLogoAsync`, `ProductsCopy.TaglineInvalid`, `ProductDetail` parameters).

- [ ] **Step 3: Implement**

`ApiFields.cs`: `public const string Tagline = "tagline";` under the products block. `ApiErrorCodes.cs`: `ProductLogoTypeNotAllowed = "product-logo-type-not-allowed"`, `ProductLogoTooLarge = "product-logo-too-large"`, and `FileRequired = "file-required"` if it is not there.

`ReferenceDataClients.cs`: `public sealed record ProductLogoFile(string FileName, string ContentType, Func<Stream> OpenRead);` next to the interface; interface methods with the docs "`POST /api/products/{id}/logo` (Admin, multipart, part `file`): 400 product-logo-type-not-allowed, product-logo-too-large or file-required; 404 product-not-found; returns the re-read product with the new Version" and "`DELETE /api/products/{id}/logo` (Admin): idempotent; returns the re-read product"; `ProductsClient.UploadLogoAsync` is a copy of `KbClient.UploadImageAsync` with `ProductLogoLimits.FieldName` and `$"api/products/{productId}/logo"`; `RemoveLogoAsync` is `connection.SendAsync<ProductDto>(HttpMethod.Delete, $"api/products/{productId}/logo", null, cancellationToken)`.

`ProductsCopy.cs`: add

```csharp
    public const string TaglineLabel = "Tagline";
    public const string TaglineHelp = "One line about the product, shown on the landing page card. Optional.";
    public const string TaglineInvalid = "Use one line of plain text, 160 characters or fewer.";
    public const string ListedLabel = "Listed on the landing page";
    public const string ListedHelp = "When the portal lists products on its front page, this product has a card. Unlisting hides the card only: the product's own pages, host and help articles stay reachable.";
    public const string ColumnListed = "Landing";
    public const string Listed = "Listed";
    public const string Hidden = "Hidden";
    public const string LogoUploadLabel = "Upload a logo";
    public const string LogoUploadHelp = "PNG, JPEG or WebP, up to 1 MB. An uploaded logo replaces the logo address wherever the logo is shown.";
    public const string LogoUploading = "Uploading\u2026";
    public const string LogoRemove = "Remove uploaded logo";
    public const string LogoUploadAfterSave = "Save the product first, then upload a logo from its editor.";
    public const string LogoTypeNotAllowed = "Choose a PNG, JPEG or WebP image.";
    public const string LogoTooLarge = "That image is too large. Logos can be up to 1 MB.";
    public const string LogoReadFailed = "That image could not be read. Choose it again.";
    public const string LogoUncertain = "The upload may have gone through. Reload the product to see its logo.";
    public const string LogoFailed = "The logo could not be uploaded.";
```

`ProductFields.All`: insert `ApiFields.Tagline` after `ApiFields.DisplayName`.

`ProductEditorViewModel`: properties `Tagline`, `ListedOnLanding = true`, `UploadedLogoUrl`; `From` copies `Tagline = product.Branding.Tagline ?? string.Empty`, `UploadedLogoUrl = product.Branding.UploadedLogoUrl`, `ListedOnLanding = product.ListedOnLanding`; `ToCreateRequest() => new(Key.Trim(), Name.Trim(), NumberPrefix.Trim(), ToBranding(), NormalisedHost(), ListedOnLanding)`; `ToUpdateRequest() => new(Name.Trim(), ToBranding(), IsActive, Version, NormalisedHostForUpdate(), ListedOnLanding)` (the editor always sends the shown value; null-means-unchanged exists for clients that do not know the field); `ToBranding()` adds `Blank(Tagline)` as the last argument; `Check` gains `ApiFields.Tagline => BrandingRules.IsAcceptableTagline(Tagline) ? null : ProductsCopy.TaglineInvalid`. Update the class summary for the flag.

`ProductEditorContent.razor`: after the display-name field add `@Field("ts-product-tagline", ProductsCopy.TaglineLabel, ApiFields.Tagline, _model.Tagline, v => _model.Tagline = v, ProductsCopy.TaglineHelp)`; after the host field add (on create and edit)

```razor
                <div class="form-check ts-product-listed">
                    <input id="ts-product-listed" class="form-check-input" type="checkbox" checked="@_model.ListedOnLanding" @onchange="OnListedChanged" />
                    <label class="form-check-label" for="ts-product-listed">@ProductsCopy.ListedLabel</label>
                    <p class="ts-field-help">@ProductsCopy.ListedHelp</p>
                </div>
```

and, in the preview section, before `<AccentPreview ...>`:

```razor
                @if (_creating)
                {
                    <p class="ts-field-help">@ProductsCopy.LogoUploadAfterSave</p>
                }
                else
                {
                    <ProductLogoUploadButton ProductId="@_id" HasUploadedLogo="@(_model.UploadedLogoUrl is not null)" Disabled="@_busy" OnChanged="OnLogoChanged" OnUploadingChanged="OnLogoUploadingChanged" />
                }
```

`ProductEditorContent.razor.cs`: `PreviewLogo => _model.UploadedLogoUrl ?? (BrandingRules.IsAcceptableLogoUrl(_model.LogoPath) && !string.IsNullOrWhiteSpace(_model.LogoPath) ? _model.LogoPath.Trim() : null)`; `OnListedChanged(ChangeEventArgs e) { _model.ListedOnLanding = e.Value is true; _dirty = true; }`; `OnLogoChanged(ProductDto saved) { _model.UploadedLogoUrl = saved.Branding.UploadedLogoUrl; _model.Version = saved.Version; }` (nothing else from `saved`: pending edits stay); `OnLogoUploadingChanged(bool uploading) => _logoBusy = uploading;` and `CanSave` becomes `(_creating ? !_uncertain : _dirty) && !_logoBusy`.

`ProductLogoUploadButton.razor(.cs)`: copy `KbImageUploadButton` and adapt: parameters `Guid ProductId`, `bool HasUploadedLogo`, `bool Disabled`, `EventCallback<ProductDto> OnChanged`, `EventCallback<bool> OnUploadingChanged`; the `InputFile` has `id="ts-product-logo-upload"` and `accept="@string.Join(',', ProductLogoLimits.AllowedExtensions)"`; the pre-check uses `ProductLogoLimits.AllowedExtensions` and `ProductLogoLimits.MaxBytes`; the call is `Products.UploadLogoAsync(ProductId, new ProductLogoFile(file.Name, file.ContentType, () => new MemoryStream(bytes, writable: false)), CancellationToken.None)`; on success `await OnChanged.InvokeAsync(result.Value)`; a `<button type="button" id="ts-product-logo-remove">` shown when `HasUploadedLogo` calls `Products.RemoveLogoAsync(ProductId, CancellationToken.None)` and raises `OnChanged` with its result; `ErrorFor` maps `ProductLogoTypeNotAllowed`, `ProductLogoTooLarge`/`RequestTooLarge`, uncertain writes, else `LogoFailed` + message. Both files end in the `Products` feature folder so they can use `ProductsCopy`.

`ProductRowViewModel`: add `bool ListedOnLanding` (last) from `product.ListedOnLanding`. `ProductsContent.razor`: a `Landing` column header after Portal host and a cell `<span class="ts-pill @(row.ListedOnLanding ? "ts-pill--on" : "ts-pill--off")">@(row.ListedOnLanding ? ProductsCopy.Listed : ProductsCopy.Hidden)</span>`.

- [ ] **Step 4: Build and test**

Run: `dotnet build TechStrap.slnx -c Release 2>&1 | grep -E " warning | error |Build succeeded"`; `dotnet test tests/TechStrap.Admin.Tests -c Release --no-build`
Expected: green (including the existing `The_active_switch_sends_the_flipped_value...` and the real-Api editor tests).

- [ ] **Step 5: Mutation**

In `OnLogoChanged` also assign `_model = ProductEditorViewModel.From(saved)`; `Removing_the_uploaded_logo_splices_the_new_version_and_keeps_a_pending_edit` must fail on the name. Restore.

- [ ] **Step 6: Commit**

```bash
git add src/TechStrap.Admin/Clients src/TechStrap.Admin/Features/Settings/Products tests/TechStrap.Admin.Tests
git commit -m "feat(admin): tagline, landing flag, logo upload and removal in the product editor (D-052)"
```

---

### Task 10: Docs, pins and close-out

**Files:**
- Modify: `docs/self-hosting/SELF-HOSTING.md` (Portal table: `TECHSTRAP_PORTAL_LANDING`; Worker table: `TECHSTRAP_API_PUBLIC_URL` optional; the default-site Caddy block and the two prose mentions of `/kb-images/` at ~l.90 and ~l.301 gain `/product-logos/`; a "plain http Api shows no uploaded logo" sentence beside `TECHSTRAP_API_PUBLIC_URL`; Volumes table row: "Ticket attachments, knowledge-base images and uploaded product logos")
- Modify: `docs/self-hosting/DEPLOYMENT.md:84` (proxy note), `deploy/.env.api.example:56` and `src/TechStrap.Api/.env.example:47-48` (the `/kb-images/` proxy comment names `/product-logos/` too)
- Modify: `docs/runbooks/backup-restore.md` (l.10 prefixes; the ownership check at l.174 and l.206 names `product-logos/`)
- Modify: `docs/security/SECURITY-REVIEW.md` ("### 3. Uploads" rows cite `ProductLogoStoreTests`, `ProductLogoServingTests`, `ProductLogoDiskFullTests`, `ProductLogoEndpointTests`; SR-05 gains "Uploaded product logos (D-052) are public by design, like KB images; their names are version 7 GUIDs and nothing else is in the path.")
- Modify: `docs/development/ADMIN-APP.md` (products section: tagline, listed flag, logo upload), `docs/development/PORTAL-APP.md:36` and the "no product list" wording (landing mode), `docs/architecture/02-ARCHITECTURE.md` 8.2 row l.370 (rewrite: "Portal root (`/`) | Paired page | the neutral page (D-045); with `TECHSTRAP_PORTAL_LANDING=Products` the listed products as cards from `PublicProductSummaryDto` (D-052); a 302 to `TECHSTRAP_PORTAL_DEFAULT_PRODUCT` when set | `LandingCardViewModel` | Server-rendered; fail-soft to the neutral copy; never on a product host | `PublicProductSummaryDto`") and 11.2 (the `public` row's "Applies to" unchanged; add a sentence under the table: "`GET /product-logos/{name}` and `GET /kb-images/{name}` are static assets with no limiter, served with an immutable cache header.")
- Modify: `docs/architecture/PHASE-11f-landing-and-logos.md` (tick T01 to T10 with **As built** notes where the build differs; tick Deliverables, Success Criteria and Boundary Validation; tick Risks), `docs/architecture/99-IMPLEMENTATION-ROADMAP.md` and `docs/architecture/00-DISCOVERY-INDEX.md` row 11f -> "11f complete (pending merge): T01 to T10", `docs/architecture/03-PACKAGE-MAP.md` (no version change; the Contracts row notes 0.3.0 at the `v0.3.0` tag)
- Modify: `scripts/tests/RepositoryDocs.Tests.ps1` (11f block: spec ticked `- \[x\] \*\*P11f-T` for 01-10 and no `- [ ] **P11f-T`; roadmap/discovery "11f complete (pending merge)"; SELF-HOSTING mentions `TECHSTRAP_PORTAL_LANDING` and `/product-logos/`; runbook mentions `product-logos/`; security review mentions `ProductLogoStoreTests`), `scripts/tests/SelfHostDocs.Tests.ps1` (the Caddy pin at l.135-136 also requires `/product-logos/`; the env-table comparison will fail until the tables list the new keys)
- Modify: this plan file: an `## As built` section at the end (what differed per task, test counts, the manual compose check)

- [ ] **Step 1: Run the Pester suite to see what the docs owe**

Run: `pwsh -NoProfile -File scripts/Invoke-ScriptTests.ps1 2>&1 | sed 's/\x1b\[[0-9;]*m//g' | grep -E "^\s*\[-\]|Expected|Tests Passed"`
Expected: failures in `SelfHostDocs` (env tables vs templates for `TECHSTRAP_PORTAL_LANDING` and the Worker `TECHSTRAP_API_PUBLIC_URL`) and possibly `ConfigContract`; record them.

- [ ] **Step 2: Add the pins first (RED)**

In the `PHASE-11f` Describe of `RepositoryDocs.Tests.ps1` add:

```powershell
    It 'ticks every 11f task and marks 11f complete pending merge' {
        foreach ($n in 1..10) { $script:Spec | Should -Match ('- \[x\] \*\*P11f-T' + $n.ToString('00') + '\*\*') -Because "P11f-T$($n.ToString('00')) is ticked" }
        $script:Spec | Should -Not -Match '- \[ \] \*\*P11f-T'
        (Get-RepoText 'docs/architecture/99-IMPLEMENTATION-ROADMAP.md') | Should -Match '(?m)^\| 11f \|.*\| 11f complete \(pending merge\): T01 to T10'
        (Get-RepoText 'docs/architecture/00-DISCOVERY-INDEX.md') | Should -Match '(?m)^\| 11f \|.*\| 11f complete \(pending merge\): T01 to T10'
    }

    It 'documents the landing setting, the logo proxy path, the runbook prefix and the security tests' {
        $selfHost = Get-RepoText 'docs/self-hosting/SELF-HOSTING.md'
        $selfHost | Should -Match 'TECHSTRAP_PORTAL_LANDING'
        $selfHost | Should -Match '/product-logos/'
        $selfHost | Should -Match 'plain http'
        (Get-RepoText 'docs/runbooks/backup-restore.md') | Should -Match 'product-logos/'
        $review = Get-RepoText 'docs/security/SECURITY-REVIEW.md'
        foreach ($phrase in 'ProductLogoStoreTests', 'ProductLogoServingTests', 'ProductLogoDiskFullTests', 'D-052') { $review | Should -Match $phrase -Because $phrase }
        (Get-RepoText 'docs/architecture/02-ARCHITECTURE.md') | Should -Match 'TECHSTRAP_PORTAL_LANDING=Products'
    }
```

In `SelfHostDocs.Tests.ps1` extend the Caddy pin (l.135-136) so the block names `/product-logos/` beside `/kb-images/`. Run the two files: the new `It`s fail.

- [ ] **Step 3: Write the docs**

Make every edit listed under Files. For SELF-HOSTING's Portal env table add `| \`TECHSTRAP_PORTAL_LANDING\` | no | \`Neutral\` | \`Neutral\` shows the neutral root page; \`Products\` lists the products whose "Listed on the landing page" flag is on, as cards (name, logo, tagline). Not allowed together with \`TECHSTRAP_PORTAL_DEFAULT_PRODUCT\`. |`; for the Worker table `| \`TECHSTRAP_API_PUBLIC_URL\` | no | blank | The Api's public address, the same value as the Api's own. When set, emails show a product's uploaded logo; blank keeps the linked logo address. |`; beside the Api's `TECHSTRAP_API_PUBLIC_URL` row add the sentence "An Api behind plain http in Production shows no uploaded logo anywhere: the Portal and the email layout accept https logos only." Update the Caddy block: wherever the Api site mentions `/kb-images/` (the proxied public static paths), mention `/product-logos/` too. In the spec, tick each task with an **As built** note recording any difference the implementers reported (names, test files, counts); tick Deliverables, Success Criteria, Boundary Validation and Risks the same way; update the roadmap, discovery and package-map rows.

- [ ] **Step 4: Verify everything**

Run: `pwsh -NoProfile -File scripts/Invoke-ScriptTests.ps1 2>&1 | sed 's/\x1b\[[0-9;]*m//g' | tail -3`; `dotnet build TechStrap.slnx -c Release 2>&1 | grep -E " warning | error |Build succeeded"`; `dotnet test --solution TechStrap.CI.slnf -c Release --no-build 2>&1 | tail -15`; `dotnet ef migrations has-pending-model-changes --project src/TechStrap.Infrastructure --startup-project src/TechStrap.Api --configuration Release --no-build`; `git diff --check`; `grep -rn -P "[^\x00-\x7F]" docs/architecture/PHASE-11f-landing-and-logos.md docs/self-hosting/SELF-HOSTING.md docs/superpowers/plans/2026-10-09-phase-11f-landing-and-logos.md` (empty).
Then the manual compose check (record the output in the plan's As built): `docker compose up -d --wait`; in the Admin create or open a product, set a tagline, upload a PNG; `curl -sI http://127.0.0.1:8080/product-logos/<name> | grep -i -E "content-security-policy|cache-control|x-content-type"` shows `sandbox`, `immutable`, `nosniff`; upload an `.svg` -> 400 with `product-logo-type-not-allowed`; set `TECHSTRAP_PORTAL_LANDING=Products` on the portal container (compose override or `.env.local`), restart it, `curl -s http://127.0.0.1:8082/ | grep -c ts-landing-card` >= 1 and the hidden product absent; set `Neutral` back.

- [ ] **Step 5: Commit**

```bash
git add docs scripts/tests/RepositoryDocs.Tests.ps1 scripts/tests/SelfHostDocs.Tests.ps1 deploy/.env.api.example src/TechStrap.Api/.env.example
git commit -m "docs(11f): self-hosting, runbook, security review, app docs and pins for the landing page and product logos (D-052)"
```

---

## Self-review notes

- **Spec coverage:** A (landing mode, fail-soft, SEO, cache) -> Task 8; B (flag, list semantics) -> Tasks 3, 5, 8; C (tagline) -> Tasks 2, 3, 5, 9; D (uploaded logo, store, routes, serving, effective logo, emails, security) -> Tasks 5, 6, 7; E (Contracts 0.3.0) -> Task 2; F (Admin) -> Task 9; G (persistence, docs, pins) -> Tasks 4, 10. Spec Success Criteria map to Tasks 6, 8, 9, 10.
- **Type consistency:** `IProductLogoUrls.UrlFor(string) : string?` (Tasks 5, 6, 7); `ProductLogos.EffectiveLogoUrl(ProductBranding, IProductLogoUrls)` (5, 7); `ProductMapping.ToDto(Product, IProductLogoUrls)` (5, 6); `IncomingProductLogo(long, Stream)` (6); `ProductLogoFile(string, string, Func<Stream>)` (9); `PortalOptions.ListsProducts` (8); `ProductBranding.Restore(displayName, logoPath, accentColour, fromAddress, replyTo, tagline, uploadedLogo)` and `Product.Restore(..., portalHost, listedOnLanding)` (3, 4, 5, 6).
- **Review Focus pins:** 1 -> Task 3 and Task 5 tests named in the list; 2 -> Task 5 handler test and the raw-JSON PUT in `ProductLandingFieldsEndpointTests`; 3 -> Task 6 store, corpus and serving tests; 4 -> Task 8 `A_product_host_root_is_its_product_home_never_the_landing` and the `https://support.paperplane.test/` assertion; 5 -> Task 8 `Products_with_a_default_product_stops_the_host_at_start`.
- **Known deviations the implementer may meet:** the test fixture method names in Tasks 4, 5, 6, 8 and 9 are taken from neighbouring tests and must be checked against the real helpers before use; `AccentPreview`'s logo selector; whether `factory.Api.Count` exists; where handler interfaces are registered. None of these change the design.

## As built

Branch `feat/phase-11f-landing-and-logos`, Tasks 2 to 10 (commits 739fc49 to 71b6bb5). The per-task differences are recorded in the spec (`docs/architecture/PHASE-11f-landing-and-logos.md`, the **As built** line under each task). Summary of where the build departs from this plan:

- Task 2: XML-doc gate forced extra `<param>` and `<summary>` tags; the summary DTO property pin changed to seven properties.
- Task 5: a PUT that omits the tagline clears it (branding is replaced whole, as `LogoPath` is); the raw-JSON PUT test repeats the tagline. `errorCodes.tagline` is an array.
- Task 6: the two handler rows sit in 02-ARCHITECTURE section 7.1 beside the api-keys rows (the plan said 7.2); handlers are auto-registered; no hostile-corpus entry has a GIF body, so the GIF rule is pinned in `ProductLogoStoreTests`; the 413 test runs on real Kestrel.
- Task 7: `EmailDrainIntegrationTests`, `EmailServiceRegistrationTests` and `DeadLetterIntegrationTests` register `AddTechStrapProductLogoUrls`.
- Task 8: the request token comes from `IHttpContextAccessor`; a `.ts-landing-intro` rule exists; in Products mode a product host's bare `/` also receives the one-minute public `Cache-Control` header (accepted, browser-only; added to the spec's Risks).
- Task 9: the preview selector is `img.ts-accent-preview-logo`; `SaveAsync` also returns early while a logo upload or removal is running.
- Task 10: `ResultMappingTests` (`ControllerActions.cs`) needed the two new logo actions at 200, found only by the whole-suite run.

### Verification

- `pwsh -NoProfile -File scripts/Invoke-ScriptTests.ps1`: Tests Passed: 512, Failed: 0 (before the docs: 508 passed, 4 failed).
- `dotnet build TechStrap.slnx -c Release`: Build succeeded, 0 Warning(s).
- `dotnet test --solution TechStrap.CI.slnf -c Release --no-build`: total 8310, failed 0, succeeded 8310, skipped 0.
- `dotnet ef migrations has-pending-model-changes ...`: No changes have been made to the model since the last migration.
- `git diff --check` clean; the new text in the spec and SELF-HOSTING is ASCII.

### Manual compose check

Port 8025 was held by another project's container (`gat_dev-mailhog-1`), so Mailpit moved with `TECHSTRAP_MAILPIT_PORT=18025`. The Admin upload could not be driven: the Admin signs in through an OIDC provider that is not part of the dev stack, and the Api routes need an admin JWT, so the upload, the SVG refusal (`product-logo-type-not-allowed`) and the served-logo headers (`sandbox`, `immutable`, `nosniff`) are covered by `ProductLogoEndpointTests`, `ProductLogoServingTests` and `ProductLogoStoreTests`, not performed by hand. What was performed on the compose stack:

```
$ TECHSTRAP_MAILPIT_PORT=18025 docker compose up -d --build --wait
 Container techstrap-api-1 Healthy / techstrap-portal-1 Healthy / techstrap-admin-1 Healthy / techstrap-worker-1 Healthy / techstrap-mailpit-1 Healthy / techstrap-postgres-1 Healthy
$ curl -s -D - -o /dev/null http://127.0.0.1:8080/product-logos/0123456789abcdef0123456789abcdef.png
HTTP/1.1 404 Not Found
Cache-Control: no-store
X-Content-Type-Options: nosniff
Content-Security-Policy: default-src 'none'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'
$ curl -s -o /dev/null -w '%{http_code}
' http://127.0.0.1:8080/product-logos/x.svg
404
$ curl -s -D - -o /dev/null http://127.0.0.1:8082/        (Neutral)
HTTP/1.1 200 OK
$ curl -s http://127.0.0.1:8082/ | grep -o ts-landing-card | wc -l
0
$ curl -s http://127.0.0.1:8080/api/public/products
[{"key":"orbitly","displayName":"Orbitly","portalHost":null,"tagline":null,"logoUrl":null,"accentColour":"#7C3AED","listedOnLanding":true},{"key":"paperplane",...,"listedOnLanding":true}]
$ docker compose run -d --rm --name ts-landing-portal -e TECHSTRAP_PORTAL_LANDING=Products -p 127.0.0.1:18082:80 portal
$ curl -s -D - -o /dev/null http://127.0.0.1:18082/
HTTP/1.1 200 OK
Cache-Control: public, max-age=60
$ curl -s http://127.0.0.1:18082/ | grep -o ts-landing-card | wc -l
2          (Orbitly and Paperplane, one card each)
```

The extra Portal container was removed afterwards; the main stack was left running (no `down -v`). The migration `AddProductLandingAndLogo` applied on start (the public list already returns the new fields). The "hidden product absent" step was not performed: both seeded products are listed and unlisting needs the Admin; `The_root_lists_the_listed_products_as_cards_in_api_order_with_safe_logos_only` pins it.
