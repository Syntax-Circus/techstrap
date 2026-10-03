# Task 12 report

Status: DONE_WITH_CONCERNS

Commit: 68dd29c docs: close PHASE-02 with the logo-removal check, status ticks and tracked-file guard

## Implemented
- scripts/tests/TrackedFiles.Tests.ps1 (verbatim from brief). Failing demo: force-adding Admin wwwroot/fonts gave "Tests Passed: 3, Failed: 1" with "Expected $null or empty, but got @('src/TechStrap.Admin/wwwroot/fonts/...". After git reset: "Tests Passed: 4, Failed: 0".
- BRAND.md section 25: implementation check appended as "pending controller visual review" (no text-only method in the brief).
- 00-DISCOVERY-INDEX.md and 99-IMPLEMENTATION-ROADMAP.md: PHASE-02 = "Complete (visual review pending)".
- PHASE-02 doc: ticked only what the commands proved: deliverables (libman/sasscompiler, style-guide page, .gitignore incl. wwwroot/fonts/), tasks T05, T06, T09, success criteria (clean-clone build/no tracked files; ProductAccentContrastTests and StyleBuildTests; CI/Docker builds), and the three closed risks. Left unticked: BRAND.md approval, UX briefs, design exploration, logo assets, T01-T04, T07, T08, T10, style-guide rendering criterion (all need owner or visual review).
- Browser steps (screenshots, Lighthouse, keyboard, reduced motion) not attempted per ruling.

## Clean-clone verification (scratch clone at 68dd29c, since deleted)
- dotnet tool restore OK; build Release: 0 Warning(s) 0 Error(s)
- dotnet test: total 378, failed 0, succeeded 378, skipped 0
- Pester: Tests Passed: 99, Failed: 0
- Check-PackageVersions: Package version check passed: 41 packages match the package map.
- docker build Dockerfile.admin and Dockerfile.portal: both exit 0; CSS test -f and font count assertions ran (admin 7 woff2 / 3 LICENSE; portal 6 / 2).
- docker compose up -d --build --wait: exit 0; admin, api, portal, postgres, worker all healthy. _styleguide 200 on 8081 and 8082. down -v done.
- git ls-files check: no output. git status --short in clone: clean.
- Production image: /_styleguide = 404.

## Concern (product bug, not fixed)
GET http://127.0.0.1:8081/fonts/ibm-plex-sans/files/ibm-plex-sans-latin-400-normal.woff2 returns 404 (brief expected 200). The file exists at /app/wwwroot/fonts/... in the image, and the build-time obj staticwebassets.build.json lists the fonts, but the published /app/TechStrap.Admin.staticwebassets.endpoints.json contains no font entries (css/app.css is there). Likely the libman restore during a clean publish happens after static-asset endpoint discovery, so MapStaticAssets does not serve the fonts. Result: brand fonts fall back to system fonts in containers. Needs a follow-up fix. Portal not curled separately but has the same setup.

## Cleanup
Removed clone, compose containers/volumes/network, ts-admin-prod container, and images techstrap-{api,worker,portal,admin}:local. Pre-existing scratch/latest/dev images left alone.

## Deliverables
pr-title.txt and pr-body.md in the sdd folder (not committed).


## Fix round 1

### Symptom
Published images answered 404 for `/fonts/**/*.woff2` (files present in `/app/wwwroot/fonts`, `app.css` served fine).

### Root-cause evidence
1. Clean repro without Docker (fonts, bin, obj deleted; `dotnet publish src/TechStrap.Admin -c Release -o <scratch>/pub`): `src/TechStrap.Admin/wwwroot/fonts` ended with 7 .woff2, `pub/wwwroot/fonts` had 7 .woff2, but `pub/TechStrap.Admin.staticwebassets.endpoints.json` had 0 `.woff2` matches. Also 0 in `obj/Release/net10.0/staticwebassets.build.json`.
2. Diagnostic log (`-v:diag`): `LibraryManagerRestore` ran (as `BuildDependsOn`) before `ResolveProjectStaticWebAssets`, so target order was NOT the problem. Libman wrote the fonts, but they were never `Content` items: the SDK's `wwwroot/**` Content glob is expanded at project evaluation, before libman restores on a clean tree. `DefineStaticWebAssets` takes `CandidateAssets="@(Content->Distinct())"`, so the late files were never candidates. The hypothesis in the task (restore after discovery step) was refined: the restore runs first, but the item glob was evaluated even earlier.
3. First fix attempt (add `Content Include="$(MSBuildProjectDirectory)/wwwroot/**"` after restore) still gave 0. Log showed `Rejected asset '...\wwwroot\fonts\...woff2' for pattern 'wwwroot/**'`: DefineStaticWebAssets matches the pattern against the item spec, so absolute paths are rejected. Project-relative `wwwroot/**` works.
4. Existing `FontHostingTests` pass because the dev tree already has fonts at evaluation time.

### Change
- `Directory.Build.targets`: new target `IncludeLibraryManagerWwwrootAssets`, `AfterTargets="LibraryManagerRestore"`, conditioned on `libman.json` existing in the project and not design-time. It adds `wwwroot/**` (relative, `Exclude="@(Content)"`, `None Remove`) to `Content`. Applies to build and publish for Admin and Portal. No font files committed, no `UseStaticFiles`.
- `Dockerfile.admin`, `Dockerfile.portal`: after the font count check, `RUN grep -q '\.woff2' /app/publish/TechStrap.<App>.staticwebassets.endpoints.json \` with a continuation line `|| { echo ...; exit 1; }`.
- `scripts/tests/Dockerfiles.Tests.ps1`: asserts the guard in both Dockerfiles; new Describe that does a clean publish (removes wwwroot/fonts, bin, obj, publishes to a temp dir) per app and asserts the endpoints manifest contains `.woff2`. Takes about 3-6 s per app (network to jsdelivr needed, same as the Docker build).
- Test red/green: with `Directory.Build.targets` stashed the clean-publish test failed for Admin (manifest lacks .woff2); with the fix all 34 Dockerfiles tests pass.

### Verification output
- `dotnet build TechStrap.slnx -c Release`: Build succeeded, 0 Warning(s), 0 Error(s)
- `dotnet test --solution TechStrap.slnx`: total: 378, failed: 0, succeeded: 378, skipped: 0
- `pwsh -File scripts/Invoke-ScriptTests.ps1`: Tests Passed: 103, Failed: 0
- `docker compose up -d --build --wait` built all images (both Dockerfile guards ran) and all containers reached Healthy.
- curl admin: `curl -sI http://127.0.0.1:8081/fonts/ibm-plex-sans/files/ibm-plex-sans-latin-400-normal.woff2`
```
HTTP/1.1 200 OK
Content-Type: font/woff2
```
- curl portal: `curl -sI http://127.0.0.1:8082/fonts/ibm-plex-sans/files/ibm-plex-sans-latin-400-normal.woff2`
```
HTTP/1.1 200 OK
Content-Type: font/woff2
```
- `docker compose down -v` removed containers, network and volumes; `docker rmi techstrap-{admin,portal,worker,api}:local` done.
