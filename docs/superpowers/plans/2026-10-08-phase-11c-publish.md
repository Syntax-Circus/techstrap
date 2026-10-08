# PHASE-11c Publish: Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the three SDK packages publishable and published (P11-T11 to T16): XML documentation and AOT-safe JSON in the packages, per-package READMEs whose snippets compile, a console sample, the tag-triggered `publish-nuget.yml` with a GitHub Release, the release documentation, and the first `v1.0.0-rc.1` publish with its post-publish check, in one pull request plus the owner's tag push.

**Architecture:** The pack metadata stays in `eng/Packaging.props`, which gains `GenerateDocumentationFile`; every public member of Contracts, Client and Client.Maui gets a doc comment, so IntelliSense ships and the public API is read once before 1.0. A source-generated `TechStrapJsonContext` replaces reflection serialization in `TechStrapClient` and the Maui merger's size check (AOT and full trimming). README snippets live in compiled code (`#region readme:<name>` blocks in the console sample and in a `Snippets.cs` of the Maui tests) and a Pester test proves each README contains them verbatim. `publish-nuget.yml` runs on a `v*` tag (and `workflow_dispatch` for a dry run): the version is the tag, packages are packed with it, validated, pushed through NuGet Trusted Publishing (OIDC) in the `release` environment, and a GitHub Release is created; `release.yml` keeps publishing the GHCR images from the same tag.

**Tech Stack:** .NET 10 (SDK 10.0.401), System.Text.Json source generation, GitHub Actions (`NuGet/login@v1`, `gh release create`), Pester, PowerShell 7.

**Spec:** `docs/architecture/PHASE-11-client-sdk.md` (tasks P11-T11 to T16; "Packaging reference" and "Sample and docs" decisions; Corrections D-047/D-048); `docs/architecture/04-DECISION-LOG.md` D-047 (GitVersion/doc file/SourceLink deferred to 11c), D-048; `PHASE-12-release-hardening.md` (the `v*` tag publishes four images, three packages and a GitHub Release); owner decisions of 2026-10-08 below, recorded as **D-049** in Task 5.

### Owner decisions (2026-10-08), recorded as D-049
1. **nuget.org is ready** (owner action #9 done: `TechStrap.*` IDs reserved, a Trusted Publishing policy per package, repository secret `NUGET_USER`, GitHub environment `release` with required reviewers). 11c ends with the real `v1.0.0-rc.1`.
2. **XML documentation: enable and document everything** (`GenerateDocumentationFile` for the three packages; every public member documented; the `.xml` ships in each nupkg).
3. **UAT is not deployed**: the post-publish check (T16) submits against the local compose stack; the UAT submit moves to PHASE-12 (P12-T14).
4. **The approved design** (below): tag-triggered `publish-nuget.yml` with a GitHub Release, version from the tag, console sample with region-extracted README snippets, no MAUI sample project, source-generated JSON.

### Decisions made while drafting (D-049 records them)
- **Version from the tag, not `GitVersion.MsBuild` in the packages.** The workflow derives `1.0.0-rc.1` from `v1.0.0-rc.1` (reject anything not `^v\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$`) and packs with `-p:Version`. The packages therefore never run the GitVersion task (no coupling to the Dockerfiles' `DisableGitVersionTask`), and "tag == packed version" holds by construction and is still checked by file name. The hosts keep GitVersion for their informational versions. Spec deviation recorded.
- **No `Microsoft.SourceLink.GitHub` package** (SDK-bundled SourceLink; `PublishRepositoryUrl` + `EmbedUntrackedSources` already set) - carried from D-047.
- **`publish-nuget.yml` does not run on pull requests**: `ci.yml` already packs and validates on every PR (11a). Duplicate restore/build/test minutes buy nothing. `workflow_dispatch` provides the manual dry run (pack + validate, no publish).
- **No `NUGET_API_KEY` fallback**: Trusted Publishing is configured; a stored long-lived key is exactly what OIDC removes.
- **Tests run in the tag workflow** (`dotnet test --solution TechStrap.CI.slnf`, Docker is available on `ubuntu-latest`): a tag must never publish an untested tree.
- **GitHub Release is created by `publish-nuget.yml`** after the NuGet push, with `gh release create --generate-notes` (prerelease when the version contains `-`) and the `.nupkg`/`.snupkg` files attached. `release.yml` is untouched: the same tag publishes the GHCR images.
- **README snippets are compiled code.** `#region readme:<name>` ... `#endregion` blocks in `samples/TechStrap.Client.Samples.Console/Program.cs` (Client) and `tests/TechStrap.Client.Maui.Tests/Snippets/MauiReadmeSnippets.cs` (Maui; compiled, never executed) are the source; `scripts/tests/ReadmeSnippets.Tests.ps1` extracts each region (dedented) and asserts the matching README contains it verbatim inside a ```csharp fence. A README edit that drifts from code fails the script tests.
- **No MAUI sample project.** The spec allows a README snippet; a MAUI app project needs a workload the CI does not have and the package is interface-only.
- **The console sample** reads `TECHSTRAP__BASEADDRESS` and `TECHSTRAP__APIKEY` (environment or `--base-address`/`--api-key` arguments), uses `AddTechStrapClient(IConfiguration)` with the `TechStrap` section, submits one ticket with a generated idempotency key, prints the ticket number and view URL, exit code 0/1 (2 for a validation failure). It never embeds a key; `docs/development/CLIENT-SDK.md` documents the not-a-secret dev Trusted key (`DEV-DATA.md`) for local runs. It is built by CI (added to `TechStrap.CI.slnf`) but is not a test project.
- **Source-generated JSON:** `TechStrap.Client.Json.TechStrapJsonContext : JsonSerializerContext` with `[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, NumberHandling = JsonNumberHandling.AllowReadingFromString)]` (the `JsonSerializerDefaults.Web` equivalents) and `[JsonSerializable(typeof(SubmitTicketRequest))]`, `[JsonSerializable(typeof(SubmitTicketResponse))]`, `[JsonSerializable(typeof(Dictionary<string, string>))]`; public so Client.Maui (and consumers) can use it. `TechStrapClient` serializes/deserializes with `TechStrapJsonContext.Default.SubmitTicketRequest/Response`; `MauiMetadataMerger` measures with `TechStrapJsonContext.Default.DictionaryStringString`. A parity test serializes a sample request, response and dictionary (with `<`, `&`, a non-ASCII character and a quote) through both the context and `new JsonSerializerOptions(JsonSerializerDefaults.Web)` and asserts identical strings (the server measures with Web defaults). `IsAotCompatible=true` on Client and Client.Maui (the analyzers must stay clean under `TreatWarningsAsErrors`; `ProblemResponseMapper` uses `JsonDocument`, which is AOT-safe).
- **The `.xml` doc file is validated by `Test-PackageContents.ps1`** (`lib/net10.0/<PackageId>.xml` present in every expected package; a fixture test for a missing file).
- **T15 is the owner's work, already done**; 11c documents the one-time setup (no secrets) in `docs/development/RELEASING.md` and ticks T15 on the owner's confirmation. **T16 is post-merge**: the owner pushes the tag (publishing is theirs to trigger and approve in the `release` environment); the post-publish check and the T16 tick follow in a tiny docs PR or as part of PHASE-12's first commit.

## Global Constraints
- Build: SDK 10.0.401, `TreatWarningsAsErrors`, `EnforceCodeStyleInBuild`; `dotnet build TechStrap.slnx -c Release` ends with 0 warnings (incl. CS1591 once the doc file is on, and the AOT analyzers). `_camelCase` fields, PascalCase constants, file-scoped namespaces.
- Doc comments: `<summary>` on every public type and member of Contracts, Client and Client.Maui; one or two plain sentences; record parameters documented with `<param>` on the record (or `/// <param name="X">` lines above the record); no "Gets or sets the X" filler - say what the value means and any limit (`IntakeLimits`) that applies.
- Encoding: non-ASCII in C# only as `\u` escapes; new files LF; existing files via the Edit tool (CRLF preserved); READMEs and docs ASCII (`SourceEncodingTests`).
- Packages: no new `PackageReference` in Contracts; Client/Client.Maui package sets unchanged (`ClientRules`, `ClientMauiRules`); `Directory.Packages.props` and `03-PACKAGE-MAP.md` change together (`Check-PackageVersions.ps1`) - expected: none.
- Secrets: no API key, token or `NUGET_API_KEY` anywhere in the repo; the sample never embeds a key; the workflow uses only `secrets.NUGET_USER` and `GITHUB_TOKEN`.
- Tests: xUnit v3 + Shouldly (+ NSubstitute); async tests carry `Timeout` and pass `Xunit.TestContext.Current.CancellationToken`; Pester for scripts/workflows/docs; TDD with RED recorded; mutations run against committed code and restored with `git checkout -- <file>`.
- Commits: Conventional Commits, staged by explicit path (never `git add -A`/`-f`, never `.superpowers/`), `git diff --cached --stat` first, each ending with exactly these two lines:
  ```
  Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi
  ```
- Verification (whole PR): `dotnet build TechStrap.slnx -c Release`; `dotnet test --solution TechStrap.CI.slnf -c Release --no-build` (Docker); `pwsh -File scripts/Invoke-ScriptTests.ps1`; `pwsh -File scripts/Check-PackageVersions.ps1`; pack all three with `-p:Version=0.0.0-local` + `Test-PackageContents.ps1`; run the console sample against `docker compose up` and see the ticket in the Admin; `workflow_dispatch` dry run of `publish-nuget.yml` on the branch after push (green `pack` job, `publish` skipped); `dotnet ef migrations has-pending-model-changes ...` (no changes).

## Review Focus
1. A tag that is not semver (`v1.0`, `release-1`) must fail the workflow before anything is packed or pushed - Task 4 Pester pin on the version regex step and a `workflow_dispatch` dry run proving the `publish` job is skipped.
2. A consumer with `PublishAot`/`TrimMode=full` must serialize and deserialize without reflection - Task 2 parity test plus `IsAotCompatible` analyzers clean.
3. A README code block that no longer matches the compiled snippet must fail CI - Task 3 `ReadmeSnippets.Tests.ps1` fixture with a drifted block.
4. A package missing its `.xml` doc file must fail the pack validation - Task 1 contents-script fixture.
5. The sample run against a stack with a wrong key must exit non-zero with the SDK's `invalid-api-key` message, never a stack trace - Task 3 sample exit-code handling (manual check in the compose run, recorded in the report).

---

### Task 1: XML documentation for the three packages and the `.xml` pack check

**Files:**
- Modify: `eng/Packaging.props` (add `<GenerateDocumentationFile>true</GenerateDocumentationFile>`), every `.cs` under `src/TechStrap.Contracts`, `src/TechStrap.Client`, `src/TechStrap.Client.Maui` that has an undocumented public member (about 127 / 17 / 3), `scripts/Test-PackageContents.ps1` (xml check), `scripts/tests/Test-PackageContents.Tests.ps1` (fixtures), `docs/development/CLIENT-SDK.md` ("Local pack": the xml is part of the package).

**Interfaces:**
- Produces: `lib/net10.0/TechStrap.Contracts.xml`, `TechStrap.Client.xml`, `TechStrap.Client.Maui.xml` in the nupkgs; `Test-PackageContents.ps1` fails when `lib/net10.0/<id>.xml` is missing from an expected package.

- [x] **Step 1: Failing Pester fixture.** In `Test-PackageContents.Tests.ps1` add a fixture package that has everything but the `lib/net10.0/<id>.xml` entry and assert the script reports `<id>: the XML documentation file lib/net10.0/<id>.xml is missing.` and exits 1; extend the good fixture with the xml entry. RED: `pwsh -File scripts/Invoke-ScriptTests.ps1 -Path scripts/tests/Test-PackageContents.Tests.ps1`.
- [x] **Step 2: Script.** After the snupkg check (~line 94) add: `if (-not ($zip.Entries.FullName -contains "lib/net10.0/$id.xml")) { $problems.Add("the XML documentation file lib/net10.0/$id.xml is missing.") }` (match the script's existing variable names). GREEN.
- [x] **Step 3: Enable the doc file.** Add `GenerateDocumentationFile` to `eng/Packaging.props`; `dotnet build TechStrap.slnx -c Release` now fails with CS1591 errors - record the count as RED.
- [x] **Step 4: Document.** Work project by project (Contracts first). For each public type/member without `///`: a `<summary>`; records get `<param>` per positional parameter; constants state the wire value or limit they carry; interfaces describe the contract. Keep neighbours' voice (see `IntakeLimits.cs`, `ITechStrapClient.cs`). No member may be hidden with `NoWarn`.
- [x] **Step 5: GREEN.** Build 0 warnings; `dotnet pack src/TechStrap.Contracts -c Release --no-build -p:Version=0.0.0-local -o <scratch>` (same for Client, Client.Maui) and `scripts/Test-PackageContents.ps1` with the three-package map passes; unzip one nupkg and confirm the `.xml` is populated (not only the assembly name).
- [x] **Step 6: Mutations.** Remove the xml entry from the good fixture (script dies); delete one `///` block and build (CS1591 error returns) - restore.
- [x] **Step 7: Commit** `docs(sdk): XML documentation for Contracts, Client and Client.Maui; pack check requires the doc file (P11-T11)`.
  - **As built:** Contracts had 154 undocumented public members, not the ~127 estimated; Client and Client.Maui were already fully documented. The review found the `TicketViews.Open`/`Unassigned`/`Mine` docs wrong and corrected them to the repository's status sets (Open = New + Open; Unassigned and Mine = New, Open, Pending; no spam), and sharpened the metadata and header docs. The `.xml` check in `Test-PackageContents.ps1` is fixture-tested.

### Task 2: Source-generated JSON and AOT compatibility

**Files:**
- Create: `src/TechStrap.Client/Json/TechStrapJsonContext.cs`
- Modify: `src/TechStrap.Client/TechStrapClient.cs` (lines ~20, ~66, ~126: use `TechStrapJsonContext.Default.SubmitTicketRequest` / `.SubmitTicketResponse`), `src/TechStrap.Client.Maui/MauiMetadataMerger.cs` (~9, ~41: `JsonSerializer.Serialize(dictionary, TechStrapJsonContext.Default.DictionaryStringString)` - the merged value must be a `Dictionary<string,string>`), `src/TechStrap.Client/TechStrap.Client.csproj` and `src/TechStrap.Client.Maui/TechStrap.Client.Maui.csproj` (`<IsAotCompatible>true</IsAotCompatible>`), `docs/development/CLIENT-SDK.md` (Known limits: remove the AOT/source-gen follow-up; add "AOT-compatible").
- Test: `tests/TechStrap.Client.Tests/Json/TechStrapJsonContextTests.cs`

**Interfaces:**
- Produces: `public sealed partial class TechStrapJsonContext : JsonSerializerContext` (namespace `TechStrap.Client.Json`) with `Default`, type infos for `SubmitTicketRequest`, `SubmitTicketResponse`, `Dictionary<string, string>`.

- [x] **Step 1: Failing tests.** `Context_serializes_a_request_exactly_like_web_defaults` (request with every property set, metadata containing `<`, `&`, `"`, `00e9`, a null `ExternalUserRef`): `JsonSerializer.Serialize(req, TechStrapJsonContext.Default.SubmitTicketRequest)` equals `JsonSerializer.Serialize(req, new JsonSerializerOptions(JsonSerializerDefaults.Web))`; `Context_deserializes_a_response_case_insensitively` (`{"ticketNumber":"T-1","viewUrl":null,"warnings":["external-user-ref-ignored"]}` and the PascalCase variant both deserialize); `Context_measures_a_dictionary_exactly_like_web_defaults` (escaped characters). RED: compile failure.
- [x] **Step 2: Implement the context** with the attributes listed in "Decisions made while drafting"; wire `TechStrapClient` and `MauiMetadataMerger`; set `IsAotCompatible` in both csproj files. Build: any IL2026/IL3050 analyzer warning is an error - resolve by using the context, never by suppression.
- [x] **Step 3: GREEN.** `dotnet test --project tests/TechStrap.Client.Tests -c Release` (unit tests; Docker-tagged ones may run too - fine), `tests/TechStrap.Client.Maui.Tests`, build 0 warnings. The existing `SubmitRequestShapeTests` (case-sensitive camelCase) and the Maui size-boundary test (escaped characters) still pass - they are the regression net.
- [x] **Step 4: Mutations.** Set `PropertyNamingPolicy` to none on the context (shape test and parity test die); remove `PropertyNameCaseInsensitive` (PascalCase response test dies); use a different encoder in the merger (Maui boundary test dies). Restore each.
- [x] **Step 5: Commit** `feat(client): source-generated JSON context and AOT compatibility for Client and Client.Maui`.
  - **As built:** the context is public, `TechStrap.Client.Json.TechStrapJsonContext`, with CamelCase naming, case-insensitive reading and numbers readable from strings; parity tests prove byte-identical output against `JsonSerializerDefaults.Web`. `IsAotCompatible` is on for Client and Client.Maui with no analyzer warnings and no suppressions. Source generation has no encoder option; parity holds because both sides use the default encoder.

### Task 3: Console sample, README snippets and the three READMEs

**Files:**
- Create: `samples/TechStrap.Client.Samples.Console/TechStrap.Client.Samples.Console.csproj`, `Program.cs`, `appsettings.json` (section `TechStrap` with empty `BaseAddress`/`ApiKey` placeholders - no key), `tests/TechStrap.Client.Maui.Tests/Snippets/MauiReadmeSnippets.cs`, `scripts/tests/ReadmeSnippets.Tests.ps1`
- Modify: `TechStrap.slnx` (new `/samples/` folder), `TechStrap.CI.slnf` (add the sample), `src/TechStrap.Contracts/README.md`, `src/TechStrap.Client/README.md`, `src/TechStrap.Client.Maui/README.md` (full READMEs), `docs/development/CLIENT-SDK.md` ("Running the sample" with the compose stack and the dev key reference), `tests/TechStrap.Architecture.Tests/ReferenceRules.cs` only if its project enumeration must exclude `samples/` (check `ProjectGraph` root scanning first).

**Interfaces:**
- Produces: sample exit codes 0 (ticket created), 1 (SDK failure: prints `code: message`), 2 (configuration missing/invalid); `#region readme:client-register`, `readme:client-submit`, `readme:client-errors` in `Program.cs`; `#region readme:maui-register`, `readme:maui-submit` in `MauiReadmeSnippets.cs`; `ReadmeSnippets.Tests.ps1` with a `Get-ReadmeRegions -Path` helper (returns name -> dedented body) and one `It` per README/region pair.

- [x] **Step 1: Failing Pester.** `ReadmeSnippets.Tests.ps1`: helper tests over a `$TestDrive` C# fixture (two regions; nested indentation dedented; a region without `#endregion` is an error); README tests: for each (README, snippet file, region) pair the README contains the region body inside a ```` ```csharp ```` fence; a drifted fixture README fails. RED (the sample and snippet files do not exist).
- [x] **Step 2: Sample.** csproj: `OutputType Exe`, `net10.0` (inherited), `IsPackable=false` (inherited), ProjectReference `../../src/TechStrap.Client/TechStrap.Client.csproj`; PackageReferences `Microsoft.Extensions.Hosting` (check the pin; add centrally + package-map row if missing) or the lighter `Microsoft.Extensions.Configuration` + `Configuration.EnvironmentVariables` + `Configuration.Json` + `Configuration.CommandLine` + `DependencyInjection` - prefer the set already pinned; `Program.cs`: build `IConfiguration` from appsettings + env + args (`--base-address`, `--api-key` map to `TechStrap:BaseAddress`/`TechStrap:ApiKey`), `services.AddTechStrapClient(configuration.GetSection(TechStrapClientDefaults.ConfigurationSection))` inside `#region readme:client-register`, resolve `ITechStrapClient`, `SubmitTicketAsync(new SubmitTicketRequest(...), idempotencyKey: Guid.NewGuid().ToString("N"), ct)` inside `#region readme:client-submit`, result handling inside `#region readme:client-errors` (print `TicketNumber` and `ViewUrl`; on failure print each `Code: Message` and exit 1); `OptionsValidationException` -> exit 2 with the validation messages. Add to slnx and slnf.
- [x] **Step 3: Maui snippets.** `MauiReadmeSnippets.cs` (internal static class, compiled, never run): `readme:maui-register` shows `builder.Services.AddTechStrapMaui(client => { client.BaseAddress = ...; client.ApiKey = ...; }, context => { context.IncludeDisplay = true; })` using `IServiceCollection` (no `MauiApp` type: the test project has no MAUI); `readme:maui-submit` shows resolving `IMauiTicketSubmitter` and `SubmitAsync(new MauiTicketDraft("Subject", "Message", "user@example.com") { IdempotencyKey = ... })`.
- [x] **Step 4: READMEs** (ASCII; headings per package): Contracts - what it is, stability promise (semver; `v1.0.0` locks the API), what it contains (DTOs, `HeaderNames`, `IntakeRoutes`, `IntakeLimits`, `TicketMetadataKeys`), "you normally get it through TechStrap.Client". Client - Install, Register (snippet), Submit (snippet), Handling results (snippet + the error-code table from CLIENT-SDK), Keys (Trusted server-side vs Public in apps; never embed a Trusted key), Retries and idempotency (supply a stable key to retry; `api-unavailable` may have created the ticket), Configuration keys, Compatibility (net10.0; AOT-compatible; Contracts lockstep). Client.Maui - Install, Register (snippet), Submit (snippet), What is collected (the 19-key table, defaults vs opt-in, Android permissions, iOS UI-thread note), Privacy (never collected; untrusted on the server for Public keys; redaction), Limits (50 keys incl. collected; local `metadata-invalid`), Compatibility (MAUI >= 10.0.0; no screenshot until a later release). Links to `docs/development/CLIENT-SDK.md` on GitHub.
- [x] **Step 5: GREEN.** `pwsh -File scripts/Invoke-ScriptTests.ps1`; build 0 warnings; `dotnet run --project samples/TechStrap.Client.Samples.Console -- --base-address http://localhost:8080 --api-key <dev key from DEV-DATA.md>` against `docker compose up` prints a ticket number and view URL and the ticket shows in the Admin; a wrong key prints `invalid-api-key: ...` and exits 1; no key exits 2. Record the outputs.
- [x] **Step 6: Mutations.** Edit one README code line (snippet test dies); remove `#endregion` in the fixture (helper test dies). Restore.
- [x] **Step 7: Commit** `feat(samples): console sample, compiled README snippets and the package READMEs (P11-T11, P11-T12)`.
  - **As built:** `Microsoft.Extensions.Hosting` 10.0.12 is the sample's only new central pin (with a package-map row). The Task 3 review minors were deferred to the final wave. A live run printed ticket `ORB-7` and its view URL (exit 0); a wrong key gave `invalid-api-key` (exit 1) and no key gave exit 2.

### Task 4: `publish-nuget.yml`, its pins and `RELEASING.md`

**Files:**
- Create: `.github/workflows/publish-nuget.yml`, `scripts/tests/PublishWorkflow.Tests.ps1`, `docs/development/RELEASING.md`
- Modify: `.github/workflows/release.yml` (comment only: "NuGet packages and the GitHub Release come from publish-nuget.yml on the same tag"), `README.md` (one line under releases/packages if a section exists), `docs/development/CLIENT-SDK.md` ("Local pack" points to RELEASING.md).

**Interfaces:**
- Produces: workflow `Publish NuGet packages` with jobs `pack` (always) and `publish` (tag only, `environment: release`); artifact `nuget-packages`; GitHub Release `v<version>`.

- [x] **Step 1: Failing Pester pins** (`PublishWorkflow.Tests.ps1`, parsing the YAML as text like `Dockerfiles.Tests.ps1` does): file exists; triggers are exactly `push.tags: ['v*']` and `workflow_dispatch`; no `pull_request`; `pack` job on `ubuntu-latest` with `fetch-depth: 0`, a version step matching `^v\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$` and failing otherwise, `dotnet test --solution TechStrap.CI.slnf`, three `dotnet pack ... -p:Version=`, `scripts/Test-PackageContents.ps1`, `upload-artifact`; `publish` job with `needs: pack`, `if: startsWith(github.ref, 'refs/tags/v')`, `environment: release`, `permissions: id-token: write` and `contents: write`, `NuGet/login@v1` with `user: ${{ secrets.NUGET_USER }}`, `dotnet nuget push ... --skip-duplicate`, a file-name version check, `gh release create` with `--generate-notes`, `--prerelease` conditional on `-`; no `NUGET_API_KEY` anywhere in `.github/`. RED.
- [x] **Step 2: Workflow.**
  ```yaml
  name: Publish NuGet packages
  on:
    push:
      tags: ['v*']
    workflow_dispatch:   # dry run: pack + validate, no publish
  permissions:
    contents: read
  jobs:
    pack:
      name: Build, test and pack
      runs-on: ubuntu-latest
      outputs:
        version: ${{ steps.version.outputs.version }}
      steps:
        - uses: actions/checkout@v7
          with: { fetch-depth: 0 }
        - uses: actions/setup-dotnet@v6
          with: { global-json-file: global.json }
        - name: Version from the tag
          id: version
          shell: pwsh
          run: |
            if ('${{ github.ref_type }}' -eq 'tag') {
              if ('${{ github.ref_name }}' -notmatch '^v(\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?)$') { throw "Tag '${{ github.ref_name }}' is not v<semver>." }
              $version = $Matches[1]
            } else { $version = '0.0.0-dryrun.${{ github.run_number }}' }
            "version=$version" >> $env:GITHUB_OUTPUT
        - run: dotnet tool restore
        - run: dotnet restore TechStrap.CI.slnf
        - run: dotnet build TechStrap.CI.slnf -c Release --no-restore
        - run: dotnet test --solution TechStrap.CI.slnf -c Release --no-build
        - name: Pack
          shell: pwsh
          run: |
            foreach ($p in 'TechStrap.Contracts','TechStrap.Client','TechStrap.Client.Maui') { dotnet pack "src/$p" -c Release --no-build -p:Version=${{ steps.version.outputs.version }} -o "${{ runner.temp }}/pack"; if ($LASTEXITCODE) { exit $LASTEXITCODE } }
            ./scripts/Test-PackageContents.ps1 -PackageDirectory "${{ runner.temp }}/pack" -Expected @{
              'TechStrap.Contracts'   = @()
              'TechStrap.Client'      = @('TechStrap.Contracts', 'SyntaxCircus.Http.Resilience', 'SyntaxCircus.Common', 'Microsoft.Extensions.Http', 'Microsoft.Extensions.Options', 'Microsoft.Extensions.DependencyInjection.Abstractions')
              'TechStrap.Client.Maui' = @('TechStrap.Client', 'TechStrap.Contracts', 'Microsoft.Maui.Essentials', 'Microsoft.Extensions.DependencyInjection.Abstractions', 'Microsoft.Extensions.Options')
            }
        - uses: actions/upload-artifact@v4
          with: { name: nuget-packages, path: "${{ runner.temp }}/pack/*.*nupkg", retention-days: 14 }
    publish:
      name: Publish to nuget.org and create the GitHub Release
      needs: pack
      if: github.ref_type == 'tag'
      runs-on: ubuntu-latest
      environment: release
      permissions:
        contents: write
        id-token: write
      steps:
        - uses: actions/checkout@v7
        - uses: actions/setup-dotnet@v6
          with: { global-json-file: global.json }
        - uses: actions/download-artifact@v8
          with: { name: nuget-packages, path: artifacts }
        - name: The packed version is the tag
          shell: pwsh
          run: |
            $v = '${{ needs.pack.outputs.version }}'
            foreach ($p in 'TechStrap.Contracts','TechStrap.Client','TechStrap.Client.Maui') { if (-not (Test-Path "artifacts/$p.$v.nupkg")) { throw "artifacts/$p.$v.nupkg is missing: the packed version does not match tag ${{ github.ref_name }}." } }
        - name: NuGet login (OIDC)
          id: login
          uses: NuGet/login@v1
          with: { user: "${{ secrets.NUGET_USER }}" }
        - name: Push
          run: dotnet nuget push "artifacts/*.nupkg" --api-key ${{ steps.login.outputs.NUGET_API_KEY }} --source https://api.nuget.org/v3/index.json --skip-duplicate
        - name: GitHub Release
          env: { GH_TOKEN: "${{ github.token }}" }
          shell: pwsh
          run: |
            $v = '${{ needs.pack.outputs.version }}'
            $pre = if ($v -match '-') { '--prerelease' } else { '' }
            gh release create '${{ github.ref_name }}' artifacts/*.nupkg artifacts/*.snupkg --title "TechStrap $v" --generate-notes $pre
  ```
  (Exact action versions: match what `ci.yml`/`release.yml` use - checkout@v7, setup-dotnet@v6 - and the TokenStorage reference for `NuGet/login@v1`, `download-artifact@v8`; use `upload-artifact@v4` unless the repo pins another.) Add the comment line to `release.yml`.
- [x] **Step 3: RELEASING.md.** Sections: What a `v*` tag does (release.yml images; publish-nuget.yml packages + GitHub Release; the `release` environment approval); Versioning (tag is the version; prerelease `-rc.N`; hosts use GitVersion informationally); One-time nuget.org setup (what the owner did: reserved `TechStrap.*` IDs, Trusted Publishing policy per package bound to this repo and `publish-nuget.yml`, secret `NUGET_USER`, environment `release` with reviewers - no values); Dry run (`workflow_dispatch`; local pack + `Test-PackageContents.ps1`); Cutting a release (annotated tag on `main`, push, approve the environment, verify on nuget.org and the Release page); Post-publish check (a fresh console project with `PackageReference`s to the three packages at the version, `dotnet restore` from nuget.org, run against the compose stack); Rollback (unlist on nuget.org; a fix ships as a new version - packages are immutable).
- [x] **Step 4: GREEN** (`pwsh -File scripts/Invoke-ScriptTests.ps1`). Pester pin in `RepositoryDocs.Tests.ps1` or `PublishWorkflow.Tests.ps1`: `docs/development/RELEASING.md` exists with its headings; `release.yml` carries the pointer comment.
- [x] **Step 5: Mutation.** Add a `pull_request:` trigger (pin dies); drop `environment: release` (pin dies). Restore.
- [x] **Step 6: Commit** `ci: publish-nuget.yml (tag-triggered OIDC publish + GitHub Release) and RELEASING.md (P11-T13, P11-T14, P11-T15)`.
  - **As built:** deviations from the draft above: the tag workflow also runs `dotnet test --solution TechStrap.CI.slnf` before packing; a dispatch packs at `0.0.0-dryrun.<run number>`; ref values pass through `env:` instead of inline expressions; `release.yml` gained only a pointer comment. After the Task 4 review, the `publish` condition became `github.ref_type == 'tag' && github.event_name == 'push'`, so a dispatch on a tag ref stays a dry run (commit `ci: publish only on tag pushes (dispatch stays a dry run)`). `RELEASING.md` "Dry run" and "Versioning" gained the dispatch-on-`main` rule and the `-p:Version` note in Task 5.
- [ ] **Step 7 (after the branch is pushed for the PR): dry run.** `gh workflow run publish-nuget.yml --ref feat/phase-11c-publish`; expect `pack` green with three `.nupkg`+`.snupkg` artifacts at `0.0.0-dryrun.N` and `publish` skipped. Record the run URL in the report; fix and re-run if red.
  - **As built:** runs after the merge (GitHub cannot dispatch a workflow absent from main).

### Task 5: Close-out: D-049, spec ticks, roadmap, CLIENT-SDK, pins, plan ticks

**Files:**
- Modify: `docs/architecture/04-DECISION-LOG.md` (Approval-basis bullet, index row, D-049 section after D-048 in D-048's format), `docs/architecture/PHASE-11-client-sdk.md` (`### Corrections (D-049, 2026-10-08)`: version from the tag not GitVersion.MsBuild; no SourceLink package; publish-nuget on tag + dispatch only, no PR trigger; no NUGET_API_KEY fallback; GitHub Release from publish-nuget.yml; compiled README snippets; no MAUI sample; doc file enabled; AOT context; T16 against the local compose stack (UAT in PHASE-12); tick T11, T12, T13, T14, T15 with `**As built (11c):**`; T16 stays unticked with "pending the owner's `v1.0.0-rc.1` tag push"; Deliverables 5 and 6 ticked, 7 unticked until T16; Success Criteria: README/samples ticked, the publish criterion ticked once the dry run is green with a note "rc.1 publish pending"), `docs/architecture/99-IMPLEMENTATION-ROADMAP.md` (row 11: `11a merged (PR #20); 11b merged (PR #21); 11c complete (pending merge): T11 to T15, T16 pending the rc.1 tag; T05 (attachments) deferred to 11d, which first needs multipart intake`; D column adds D-049; owner action #9 "done 2026-10-08"), `docs/architecture/00-DISCOVERY-INDEX.md` (row 11), `docs/architecture/02-ARCHITECTURE.md` (samples folder mention), `docs/architecture/03-PACKAGE-MAP.md` (SourceLink row note: SDK-bundled, no package; GitVersion row: hosts only), `docs/development/CLIENT-SDK.md` (Known limits updates; "Running the sample"; link RELEASING.md), `README.md` (repo root: a "Packages" line with the three nuget.org IDs), `scripts/tests/RepositoryDocs.Tests.ps1` (row-11 pin; `Describe 'D-049 (publishing)'` pins: heading/status/date/index row/approval bullet; phrases `publish-nuget.yml`, `Trusted Publishing`, `GenerateDocumentationFile`, `TechStrapJsonContext`, `samples/TechStrap.Client.Samples.Console`, `RELEASING.md`; PHASE-11 Corrections (D-049) heading; T11-T15 ticked with `**As built (11c):**`; T16 unticked), `docs/superpowers/plans/2026-10-08-phase-11c-publish.md` (tick steps; `**As built:**` notes).

- [x] **Step 1: Pins first (RED).** Add the D-049 Describe and the new row-11 pin; run `pwsh -File scripts/Invoke-ScriptTests.ps1 -Path scripts/tests/RepositoryDocs.Tests.ps1`; record failures.
- [x] **Step 2: Docs** (Edit tool; CRLF; ASCII; column counts). D-049 sections: Status "Approved (owner 2026-10-08; technical rulings at PHASE-11c plan review)", Related D-003, D-005, D-047, D-048, PHASE-11, PHASE-12, this plan; Context (owner action #9 done; UAT not deployed; the deferred items from D-047; what release.yml does today); Decision = owner decisions 1-4 + the drafting rulings; Alternatives (GitVersion.MsBuild in packages; publish on PRs; API-key fallback; GitHub Release from release.yml; MAUI sample project; README snippets as plain text); Consequences + Known limits (rc.1 publishes GHCR images too; packages immutable, fix = new version; the `release` environment needs a reviewer each publish; UAT submit in PHASE-12; attachments 11d).
- [x] **Step 3: GREEN** + `pwsh -File scripts/Check-PackageVersions.ps1`; `dotnet build TechStrap.slnx -c Release`; `SourceEncodingTests`; `dotnet ef migrations has-pending-model-changes ...`.
- [x] **Step 4: Mutation.** Break the D-049 index row (pin dies); restore.
- [x] **Step 5: Commit** `docs: PHASE-11c close-out, D-049, RELEASING pointers and pins`.
  - **As built:** the folded-in items from the Task 4 review were done here: the publish condition guard with its pin, and the two `RELEASING.md` additions. The root README gained a "Packages" section; the `RepositoryDocs.Tests.ps1` D-047 and D-048 pins now leave only T16 unticked. T16 is post-merge (the owner's `v1.0.0-rc.1` tag push).

## Post-merge: P11-T16 (owner tag push + post-publish check)
1. Owner: `git tag -a v1.0.0-rc.1 -m "TechStrap 1.0.0-rc.1" <merge commit on main>` and `git push origin v1.0.0-rc.1`; approve the `release` environment when the `publish` job waits. Both workflows run: `release.yml` pushes GHCR `1.0.0-rc.1` images (not `latest`); `publish-nuget.yml` pushes the three packages and creates the prerelease.
2. Verify: nuget.org shows `TechStrap.Contracts`, `TechStrap.Client`, `TechStrap.Client.Maui` at `1.0.0-rc.1` (indexing can take minutes); the GitHub Release exists with notes and the six package files.
3. Post-publish check: in the scratchpad, `dotnet new console`, `dotnet add package TechStrap.Client --version 1.0.0-rc.1` and `TechStrap.Client.Maui --version 1.0.0-rc.1`, copy the sample's submit code, run against `docker compose up` with the dev key: a ticket number prints and the ticket shows in the Admin; a Public key run shows the metadata flagged untrusted. Record the outputs.
4. Tick T16 in `PHASE-11-client-sdk.md`, Deliverable 7 and the roadmap row (`11c merged (PR #N); v1.0.0-rc.1 published`), with pins, in a small docs PR (or PHASE-12's first commit).

## Verification (whole PR)
```
dotnet build TechStrap.slnx -c Release                      # 0 warnings (CS1591 + AOT analyzers on)
dotnet test --solution TechStrap.CI.slnf -c Release --no-build   # Docker running
pwsh -File scripts/Invoke-ScriptTests.ps1
pwsh -File scripts/Check-PackageVersions.ps1
dotnet pack src/{Contracts,Client,Client.Maui} -c Release --no-build -p:Version=0.0.0-local -o <scratch>; pwsh scripts/Test-PackageContents.ps1 ... (3 packages, .xml present)
dotnet run --project samples/TechStrap.Client.Samples.Console -- --base-address http://localhost:8080 --api-key <dev key>   # against docker compose up
gh workflow run publish-nuget.yml --ref feat/phase-11c-publish   # dry run: pack green, publish skipped
dotnet ef migrations has-pending-model-changes --project src/TechStrap.Infrastructure --startup-project src/TechStrap.Api --configuration Release --no-build
```
Then `superpowers:finishing-a-development-branch`: PR "PHASE-11c: publish workflow, READMEs, sample, XML docs and AOT (D-049)" against `main`.

## Risks / open items
- `IsAotCompatible` analyzers may flag `SyntaxCircus.Common`/`Http.Resilience` call paths outside our control; if so, record the specific warning and decide (suppress with justification or drop `IsAotCompatible` while keeping the context) - report, do not silently suppress.
- `NuGet/login@v1` needs the Trusted Publishing policy bound to this repo + workflow file name (`publish-nuget.yml`); a mismatch fails the first publish with an auth error - RELEASING.md names the exact values the policy must carry.
- The `rc.1` tag also publishes GHCR images at `1.0.0-rc.1` (release.yml) - intended (PHASE-12 deploys the RC to UAT).
- `--generate-notes` quality depends on PR titles; PHASE-12 may replace it with a Conventional Commits changelog.
- The `Microsoft.Extensions.Hosting`/`Configuration.*` pins for the sample may be new central pins (package-map rows) - Task 3 checks before adding.
