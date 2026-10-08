# Releasing

A release is a `v*` tag on `main`. The tag is the version of everything it publishes: the container images, the three NuGet packages and the GitHub Release.

## What a v* tag does

Pushing a tag such as `v0.1.0` or `v0.1.0-rc.1` starts two independent workflows:

| Workflow | Publishes |
| --- | --- |
| `release.yml` | The four multi-arch images (Api, Admin, Portal, Worker) to GHCR. |
| `publish-nuget.yml` | `TechStrap.Contracts`, `TechStrap.Client` and `TechStrap.Client.Maui` to nuget.org, then the GitHub Release `v<version>` with the packages attached and generated notes. |

`publish-nuget.yml` has two jobs. `pack` builds, runs the .NET tests (including the Docker-backed ones), packs the three packages with the tag's version and runs `scripts/Test-PackageContents.ps1`.
`publish` waits for a reviewer to approve the GitHub environment `release`, checks that the packed file names carry the tag's version, logs in to nuget.org with Trusted Publishing (OIDC, no API key
is stored anywhere), pushes the packages with `--skip-duplicate` (the `.snupkg` symbol packages go with them) and creates the GitHub Release. A tag that is not `v<semver>` fails the `pack` job
before anything is packed.

## Versioning

- The tag is the version: `v1.2.3` publishes `1.2.3`, and `v1.2.3-rc.1` publishes `1.2.3-rc.1`, which nuget.org shows as a prerelease and the GitHub Release marks as a prerelease.
- A version with a hyphen is a prerelease. Use `-rc.N` for release candidates.
- `dotnet pack --no-build -p:Version=<tag>` stamps the package (nuspec) version, while the assembly versions keep the build's own value. The hosts use GitVersion; the packages do not.
- The hosts (Api, Admin, Portal, Worker) use GitVersion for their informational version only. It does not decide the version of the packages.
- A published package version can never change. A mistake ships as the next version.

## One-time nuget.org setup

The owner did this once; it needs repeating only for a new package id or a renamed repository or workflow file.

- The `TechStrap.*` package ids are reserved on nuget.org.
- Each package has a Trusted Publishing policy with exactly these values:
  - repository owner and name: `Syntax-Circus/techstrap`
  - workflow file: `publish-nuget.yml`
  - environment: `release`
- The repository secret `NUGET_USER` holds the nuget.org account name (the profile name, not an email address). It is the only nuget.org value stored in GitHub, and it is not a credential.
- The GitHub environment `release` has required reviewers. Its approval is the last gate before anything reaches nuget.org.

No long-lived API key exists. `NuGet/login` exchanges the workflow's OIDC token for a short-lived key at run time.

## Dry run

Run the workflow by hand: Actions > Publish NuGet packages > Run workflow, on any branch or tag. It packs at version `0.0.0-dryrun.<run number>`, runs the tests and the package-content check and
uploads the packages as the `nuget-packages` artifact. The `publish` job is skipped, so nothing reaches nuget.org and no Release is created. Only a tag push publishes: a run started by hand on a tag ref is still a dry run.

GitHub's rule is that a new or changed workflow can only be dispatched once it is on `main`. The first dry run of `publish-nuget.yml` therefore happens right after its merge and before the first tag.

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
4. Verify the result: the three packages at that version on nuget.org (indexing can take a few minutes), and the Release page for the tag with the packages attached.

## Post-publish check

Prove that a stranger can use the packages from nuget.org alone:

1. Create a fresh console project outside the repository, with no `NuGet.config` that points at a local feed.
2. Add `PackageReference`s to `TechStrap.Contracts`, `TechStrap.Client` and `TechStrap.Client.Maui` at the released version.
3. Run `dotnet restore` and confirm that every package comes from nuget.org.
4. Use `AddTechStrapClient` and submit a ticket against the local compose stack (`docker compose up -d --build`, see the README quick start), the way the console sample does.

## Rollback

A published package cannot be replaced or deleted. If a version is bad:

1. Unlist it on nuget.org (the package page, Manage, untick "List in search results"). Existing consumers keep restoring it; new ones stop finding it.
2. Fix the problem on `main` and ship it as a new version (`v0.1.1`, or the next `-rc.N`).
3. Edit the GitHub Release of the bad version to say so, or mark it as a prerelease.

Images are rolled back separately by deploying the previous image tag (see the deployment runbook).
