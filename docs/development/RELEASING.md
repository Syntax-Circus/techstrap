# Releasing

A release is a `v*` tag on `main`. The tag is the version of everything it publishes: the container images, the three NuGet packages and the GitHub Release.

## What a v* tag does

Pushing a tag such as `v0.1.0` or `v0.1.0-rc.1` starts two independent workflows:

| Workflow | Publishes |
| --- | --- |
| `release.yml` | The four `linux/amd64` images (Api, Admin, Portal, Worker) to GHCR. |
| `publish-nuget.yml` | `TechStrap.Contracts`, `TechStrap.Client` and `TechStrap.Client.Maui` to nuget.org, then the GitHub Release `v<version>` with the packages attached and generated notes. |

`publish-nuget.yml` has two jobs. `pack` builds, runs the .NET tests (including the Docker-backed ones), packs the three packages with the tag's version and runs `scripts/Test-PackageContents.ps1`.
`publish` waits for a reviewer to approve the GitHub environment `release`, checks that the packed file names carry the tag's version, logs in to nuget.org with Trusted Publishing (OIDC, no API key
is stored anywhere), pushes the packages with `--skip-duplicate` (the `.snupkg` symbol packages go with them) and creates the GitHub Release. A tag that is not `v<semver>` fails the `pack` job
before anything is packed.

## Versioning

- The tag is the version: `v1.2.3` publishes `1.2.3`, and `v1.2.3-rc.1` publishes `1.2.3-rc.1`, which nuget.org shows as a prerelease and the GitHub Release marks as a prerelease.
- A version with a hyphen is a prerelease. Use `-rc.N` for release candidates.
- The tag also stamps the assembly versions: the `Build` step passes `-p:Version=<tag>` (AssemblyVersion, FileVersion and InformationalVersion), and `dotnet pack --no-build -p:Version=<tag>` stamps the package (nuspec) version with the same value. The hosts use GitVersion; the packages do not.
- The hosts (Api, Admin, Portal, Worker) use GitVersion for their informational version only. It does not decide the version of the packages.
- A published package version can never change. A mistake ships as the next version.

## One-time nuget.org setup

The owner did this once; it needs repeating only for a new package id or a renamed repository or workflow file.

- The `TechStrap.*` package ids are reserved on nuget.org.
- A Trusted Publishing policy belongs to the nuget.org owner (user or organization) and is matched by repository, workflow file and environment. Whether one policy covers new package ids depends on how the owner scoped it, so check it on nuget.org when a package id is added. The policy has exactly these values:
  - repository owner and name: `Syntax-Circus/techstrap`
  - workflow file: `publish-nuget.yml`
  - environment: `release`
- `NUGET_USER` is an organization secret available to the repository (not a repository secret). It holds the nuget.org account name (the profile name, not an email address). It is the only nuget.org value stored in GitHub, and it is not a credential.
- The GitHub environment `release` has required reviewers. Its approval is the last gate before anything reaches nuget.org. It also needs a deployment rule "Selected branches and tags" with the tag pattern `v*`: "Protected branches only" blocks tag refs, so the `publish` job could never start.
- The controller created the environment on 2026-10-08 (a required reviewer and the `v*` tag rule) and verified the environment and the organization secret with `gh api repos/Syntax-Circus/techstrap/environments/release`.

No long-lived API key exists. `NuGet/login` exchanges the workflow's OIDC token for a short-lived key at run time.

## Dry run

Run the workflow by hand: Actions > Publish NuGet packages > Run workflow, on any branch or tag. It packs at version `0.0.0-dryrun.<run number>` (on a tag ref it packs at that tag's version), runs the tests and the package-content check and
uploads the packages as the `nuget-packages` artifact. The `publish` job is skipped, so nothing reaches nuget.org and no Release is created. Only a tag push publishes: the `publish` job requires a tag push, so a run started by hand on a tag ref packs at that tag's version but still publishes nothing.

GitHub's rule is that a workflow absent from the default branch cannot be dispatched, so it can only be dispatched once it is on `main`. The first dry run of `publish-nuget.yml` therefore happens right after its merge and before the first tag.

The same pack and check run locally with the commands under "Local pack" in [CLIENT-SDK.md](CLIENT-SDK.md).

## Cutting a release

1. Make sure `main` is green and holds what you want to ship.
2. Create an annotated tag on `main` and push it:

   ```
   git switch main
   git pull --ff-only
   git tag -a v0.1.0-rc.1 -m "TechStrap 0.1.0-rc.1"
   git push origin v0.1.0-rc.1
   ```

3. Watch both workflows. `release.yml` pushes the images. `publish-nuget.yml` runs `pack`, then waits on the `release` environment: a reviewer approves it.
   After `gh release create --generate-notes`, paste the Contracts README `## Version notes` entry for the version into the GitHub Release body.
4. Verify the result: the three packages at that version on nuget.org (indexing can take a few minutes), and the Release page for the tag with the packages attached.

If you re-run a failed `publish` job, the push skips packages that are already on nuget.org as duplicates. If the GitHub Release already exists, `gh release create` fails; attach the files to the existing Release instead with `gh release upload <tag> <files> --clobber`. The Release step uses `--verify-tag`, so it fails rather than create a tag when the tag is missing.

`ci.yml` sets up the SDK with `dotnet-version: 10.0.x`, while `publish-nuget.yml` uses `global.json`. That is intended: the publish workflow pins the SDK that builds the released packages.

## Image platforms

The published images are `linux/amd64` only (owner decision 2026-10-09, recorded under D-051). Until `v0.2.1` the release built arm64 as well, under QEMU; that needed the QEMU and BuildKit images from Docker Hub, pulled anonymously, and GitHub's shared runners exhaust Docker Hub's anonymous pull limit (`v0.2.1` failed three times with `toomanyrequests` before building anything). Both workflows now use the Docker daemon's own BuildKit (`driver: docker`) and pull nothing from Docker Hub; the base images come from `mcr.microsoft.com`. `Build-TechStrapDocker.ps1` can still build arm64 locally (`-Platforms linux/amd64,linux/arm64`). If arm64 images are wanted again, build them on GitHub's native arm64 runners (free for public repositories) and merge the manifests; do not reintroduce QEMU.

If a tag's `release.yml` run fails for a reason outside the repository, the images can be built and pushed from a developer machine with Docker logged in to GHCR (`docker login ghcr.io`, a token with `write:packages`):

```powershell
git checkout v<version>
./Build-TechStrapDocker.ps1 -Push -Registry ghcr.io/syntax-circus -ImageTag <version> -SemVerTag <version> -Platforms linux/amd64
```

`-PushLatest` defaults to true; pass `-PushLatest:$false` for a prerelease.

## Post-publish check

Prove that a stranger can use the packages from nuget.org alone:

1. Create a fresh console project outside the repository, with no `NuGet.config` that points at a local feed.
2. Add `PackageReference`s to `TechStrap.Contracts`, `TechStrap.Client` and `TechStrap.Client.Maui` at the released version.
3. Run `dotnet restore` and confirm that every package comes from nuget.org.
4. Use `AddTechStrapClient` and submit a ticket against the local compose stack, the way the console sample does. The dev API key exists only when the stack runs with `TECHSTRAP_SEED_DEV_DATA=true`; see "Running the sample" in [CLIENT-SDK.md](CLIENT-SDK.md) for the exact commands.

## Scans

CI fails the build on a known-vulnerable NuGet package, direct or transitive (`Vulnerable packages (direct and transitive)` in `build-test`, and `NuGetAudit` in `Directory.Build.props`), and on a High or Critical image finding that has a fix (the Trivy steps in `docker-build`). A finding that cannot be fixed yet is waived, and the waiver is recorded as an accepted finding in `docs/security/SECURITY-REVIEW.md`. An image finding goes in `.trivyignore`: one line per CVE with a reason and a review date at most 90 days out. A NuGet advisory goes in `Directory.Build.props` as `<NuGetAuditSuppress Include="https://github.com/advisories/GHSA-..." />`, with a comment giving the reason and the review date.

No SBOM is published; generate one on demand with `trivy image --format cyclonedx --output sbom-api.cdx.json ghcr.io/syntax-circus/techstrap-api:<version>` (owner decision 2026-10-09, D-051 amendment).

## Rollback

A published package cannot be replaced or deleted. If a version is bad:

1. Unlist it on nuget.org (the package page, Manage, untick "List in search results"). Existing consumers keep restoring it; new ones stop finding it.
2. Fix the problem on `main` and ship it as a new version (`v0.1.1`, or the next `-rc.N`).
3. Edit the GitHub Release of the bad version to say so, or mark it as a prerelease.

Images are rolled back separately by deploying the previous image tag (see the deployment runbook).
