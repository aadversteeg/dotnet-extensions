# Releasing

Every package has its own version and is released on its own. A release is one tag; the workflow
`.github/workflows/release.yml` does the rest. Nothing is ever published from a local machine, and there is no
NuGet API key anywhere: nuget.org trusted publishing gives the workflow a short-lived key for each release.

## Release tags

A release tag is `<tag name>/<version>`, for example `functional-core/0.1.0-preview.4`.

The tag name is the package id without `Ave.Extensions.`, in lower case, with the words split by hyphens:
`Ave.Extensions.ErrorPaths.AspNetCore` becomes `error-paths-asp-net-core`. A project can override the derived
name with a `ReleaseTagName` property. This command prints every name and fails when two packages share one:

```shell
dotnet msbuild eng/release-tags.proj -nologo
```

The version is a SemVer 2.0 version without build metadata (`1.2.0`, `0.7.0-preview.10`). A version with a
pre-release part (`-preview.4`) becomes a pre-release on GitHub. No version is stored in the project files.
Local builds are `0.0.0-dev`; a release passes the version from the tag with `-p:Version=<version>`.

To see what a tag would release, without releasing anything:

```shell
dotnet msbuild eng/release-tags.proj -nologo -t:ResolveRelease -p:Tag=functional-core/0.1.0-preview.4
```

## Dependencies between our own packages

A library references another package of this repository with a `PackageReference`. Its version in
`Directory.Packages.props` is the lowest version that any package in this repository needs. A project that needs
a newer version sets `VersionOverride` on its reference.

- `UseRepoProjectReferences=true` (the default, for local work and the CI head build): the reference becomes a
  project reference to `libs/*/<PackageId>/<PackageId>.csproj`.
- `UseRepoProjectReferences=false` (release mode): the reference stays a package reference. The packed nuspec
  carries the minimum version, and the lower package is not built.

A test project references only the package it tests. The packages below it come through that package, so in
release mode the tests run against the published minimum versions. Two test projects cannot work that way:
`UnitTests.Extensions.Functional` uses `Ave.Extensions.Functional.FluentAssertions` and
`UnitTests.Extensions.FileSystem` uses `Ave.Extensions.FileSystem.InMemory`, and both helpers depend on the
package under test. In release mode the helper would bring the published version of the package under test next to
the one being built (error CS1704). These projects set `<TestInHeadMode>true</TestInHeadMode>`, and a release runs
them in head mode.

## What the release workflow does

The workflow starts when a tag `*/*` is pushed. It has three jobs.

1. **Build, test and pack** (`build`):
   - checks that the tagged commit is on `main`;
   - resolves the tag to its package (`eng/release-tags.proj`, target `ResolveRelease`): project, version,
     target frameworks, the minimum versions of our own packages it depends on, and its test projects;
   - builds the package in release mode, runs its test projects in release mode (head mode for the two projects
     above), leaving out the tests with the trait `Category=Network`;
   - packs it from a clean tree in release mode with the version from the tag;
   - verifies the package (`eng/PackageCheck`): file name, id, version, one dependency group per target
     framework, the dependencies on our own packages at exactly their minimum versions, no development version,
     the assemblies and XML documentation per target framework, README and LICENSE, the repository URL and
     commit, and the symbols package.
2. **Publish to nuget.org** (`publish`): runs in the GitHub environment `nuget`, so it waits for approval. It logs
   in with `NuGet/login` (trusted publishing: the GitHub OIDC token is exchanged for an API key that is valid for a
   short time) and pushes the package; the symbols package goes with it.
3. **GitHub release** (`github-release`): runs only after the publish succeeded, and creates the GitHub release for
   the tag with the `.nupkg` and `.snupkg` attached.

The GitHub release comes last on purpose. Until the approval is given and the package is on nuget.org, only the tag
exists: there is no GitHub release for a version that is not on nuget.org.

What to do when a job fails:

- `build` fails: nothing was released. Fix the problem (see below for the tag).
- The approval is refused, or `publish` fails (for example at the nuget.org login): nothing was released. Fix the
  cause, then run the failed jobs again ("Re-run failed jobs"); the package from the `build` job is reused. The push
  uses `--skip-duplicate`, so a version that already reached nuget.org does not fail the rerun.
- Only `github-release` fails: the package is on nuget.org. Run that job again; do not delete or move the tag, the
  version cannot be used again.

## Release one package

1. Make sure the change is on `main` and CI is green. The CI job "Release mode (main only)" builds and tests every
   package the way a release does; the package you release must be green there.
2. Choose the version. A version that reached nuget.org can never be used again.
3. Tag the commit on `main` and push the tag:

   ```shell
   git tag functional-core/0.1.0-preview.4
   git push origin functional-core/0.1.0-preview.4
   ```

4. Watch the workflow run in GitHub Actions. When the build job is done, approve the publish job (environment
   `nuget`). The GitHub release follows the publish.
5. Check the package on nuget.org and the GitHub release. Indexing on nuget.org can take a few minutes.

If the build job fails, fix the problem on `main`, delete the tag (`git push origin :refs/tags/<tag>` and
`git tag -d <tag>`) and tag the fixed commit again. Do that only for a version that was not published.

## A change across two packages

A change that spans two packages is one pull request. Then:

1. Release the lower package.
2. In a new pull request, raise the minimum the upper package needs: the version in `Directory.Packages.props` when
   every package that uses it may require the new version, otherwise a `VersionOverride` on the reference in the
   upper project.
3. Release the upper package.

Raise the minimum only after the lower package is on nuget.org: until then a release-mode build cannot restore it,
and the CI job "Release mode (main only)" shows that as an error.

## First release wave

The file-system packages were developed past the versions on nuget.org: nuget.org has `0.6.0-preview.1`, which
targets `netstandard2.1` and `net10.0`, and the source is past `0.7.0-preview.9` and targets `netstandard2.0` and
`net10.0` (`.InMemory` and `.AzureFileStorage` also `netstandard2.1`). So the file-system minimum versions in
`Directory.Packages.props` (`0.6.0-preview.1`) cannot be used for a release from this repository: in release mode
`.Local`, `.InMemory`, `.MimeDetective`, `.AzureFileStorage` and `Ave.Extensions.FileSystem` do not restore (the
published packages have no `netstandard2.0` build), and the CI job "Release mode (main only)" is red for file-system
until this wave is done.

The first wave therefore goes in dependency order, raising the minimum after each step:

1. release `file-system-abstractions`;
2. raise `Ave.Extensions.FileSystem.Abstractions` in `Directory.Packages.props` to that version, then release
   `file-system`;
3. raise `Ave.Extensions.FileSystem`, then release `file-system-local`, `file-system-in-memory` and
   `file-system-mime-detective`;
4. raise `Ave.Extensions.FileSystem.Local`, then release `file-system-azure-file-storage`.

All file-system minimums rise in this wave.

The functional, error-paths and fluent-contracts minimums are versions that are on nuget.org today, so those
packages can be released in any order.

## One-time setup

### GitHub environment `nuget`

In the repository on GitHub: Settings, Environments, New environment, name `nuget`.

- Required reviewers: the maintainers who approve a publish (at least one).
- Deployment branches and tags: "Selected branches and tags", add a tag rule `*/*`. Then only a release tag can use
  the environment.
- No secrets and no variables: trusted publishing needs neither.

### Trusted publishing policy on nuget.org

Sign in on nuget.org with the account that owns the `Ave.Extensions.*` packages and add a trusted publishing
policy for GitHub Actions (nuget.org documentation:
<https://learn.microsoft.com/nuget/nuget-org/trusted-publishing>):

| field | value |
|---|---|
| Repository owner | `aadversteeg` |
| Repository | `dotnet-extensions` |
| Workflow file | `release.yml` (the file name only, without `.github/workflows/`) |
| Environment | `nuget` |

nuget.org also asks for a policy name and the package owner (the account that owns the packages), and it requires
scopes when a policy is created: the scope says what the keys it hands out may do, and for which packages.

| scope field | value |
|---|---|
| Scope | push new packages and new versions (`package:push`) |
| Packages | glob `Ave.Extensions.*` |

`package:push` and not only "push new versions": the first release of a package that is not on nuget.org yet needs
it. Check the scopes again after editing a policy; editing it in the nuget.org web page can reset them.

`NuGet/login` asks for the API key in the name of a nuget.org user: the `NUGET_USER` value at the top of
`release.yml`, `ahmverst`. It must be the user name of the account that created the policy (not an organization).

While the repository is private, nuget.org cannot read the repository and owner ids from GitHub. The policy is then
temporary for 7 days, and becomes permanent at the first successful login from the workflow. For a public
repository it is permanent at once. Renaming or transferring the repository breaks the policy.
