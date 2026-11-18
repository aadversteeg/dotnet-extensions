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
release mode the tests run against the published minimum versions. Some test projects cannot work that way:
`UnitTests.Extensions.Functional` uses `Ave.Extensions.Functional.FluentAssertions` and
`UnitTests.Extensions.FileSystem` uses `Ave.Extensions.FileSystem.InMemory`, and both helpers depend on the
package under test. In release mode the helper would bring the published version of the package under test next to
the one being built (error CS1704). These projects set `<TestInHeadMode>true</TestInHeadMode>`, and a release runs
them in head mode. In the documents family the same holds for `UnitTests.Extensions.Documents.Fonts`, `.Images`,
`.PageModel` and `.Render.Raster` (their test helper `Ave.Extensions.Documents.TestSupport` depends on Fonts, Images
and PageModel),
`UnitTests.Extensions.Documents.Fluent` (it renders through `Ave.Extensions.Documents`, which depends on Fluent) and
`UnitTests.Extensions.Documents.Architecture` (the family's rules, checked on the head build of every library
together).

Other packages of this repository that a test project uses besides the one it tests are package references, like a
library's (for example `Ave.Extensions.Documents.Typesetting` in `UnitTests.Extensions.Documents.Render.Pdf`), so in
release mode they are the published minimum versions too. A test project that tests no package of its own name sets
`<TestedPackageId>`: `UnitTests.Extensions.Documents.Samples` and `.Architecture` test `Ave.Extensions.Documents`.

## What the release workflow does

The workflow starts when a tag `*/*` is pushed. It has three jobs.

1. **Build, test and pack** (`build`):
   - checks that the tagged commit is on `main`;
   - resolves the tag to its package (`eng/release-tags.proj`, target `ResolveRelease`): project, version,
     target frameworks, the minimum versions of our own packages it depends on, and its test projects;
   - builds the package in release mode, runs its test projects in release mode (head mode for the two projects
     above), leaving out the tests with the trait `Category=Network`;
   - for `Ave.Extensions.FileSystem.AzureFileStorage` only: runs its integration tests against a real Azure
     storage account on the release-mode build (see "The Azure integration tests in a release" below);
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

### The Azure integration tests in a release

A release of `Ave.Extensions.FileSystem.AzureFileStorage` (tag `file-system-azure-file-storage/<version>`) also runs
`IntegrationTests.AzureFileStorage` against a real Azure storage account, as the CI job "Azure integration tests"
does on `main`. The difference is what is tested: CI tests the head build in three modes, the release tests the
release-mode build, which is what goes into the package.

- The build job fails when a repository secret (`AZURE_STORAGE_ACCOUNT_NAME`, `AZURE_STORAGE_ACCOUNT_KEY`) is
  missing, when a test fails or when a test is skipped. Then nothing is published.
- Only the steps that run these tests get the secrets, and only for this package. The publish job never gets them.
- The tests empty the file share they use. A release uses the share `integration-tests-release`, CI uses
  `integration-tests`, so a release and a CI run on `main` can run at the same time. The tests create the share
  when it does not exist.
- A release of any other package does not run these tests and gets no secret.

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
`git tag -d <tag>`) and tag the fixed commit again. Do that only for a version that was not published. Only a
repository admin can delete a release tag (see "Rulesets for `main` and the release tags").

## A change across two packages

A change that spans two packages is one pull request. Then:

1. Release the lower package.
2. In a new pull request, raise the minimum the upper package needs: the version in `Directory.Packages.props` when
   every package that uses it may require the new version, otherwise a `VersionOverride` on the reference in the
   upper project.
3. Release the upper package.

Raise the minimum only after the lower package is on nuget.org: until then a release-mode build cannot restore it,
and the CI job "Release mode (main only)" shows that as an error. nuget.org lists a new version some time after the
publish job is done; in the first releases from this repository that took between 6 and 26 minutes.

So the CI job "Release mode (main only)" is red on `main` from the merge of a change that adds packages or gives a
package new API another package uses, until the release wave of that change is done. This is expected. In release
mode every package and test project gets the packages it uses from nuget.org, at the minimum version in
`Directory.Packages.props`; until the lower package is released, that version does not have the new API (or a new
package is not on nuget.org at all), so the upper package does not compile there. Each step of the wave turns some of
it green. The job is one job for every family of the repository: while it is red for this reason, read its log to see
that only the expected packages fail, because a new break elsewhere has the same colour.

## The first file-system releases from this repository

The file-system packages on nuget.org stopped at `0.6.0-preview.1`, which targets `netstandard2.1` and `net10.0`
and has no `netstandard2.0` build. On 17 November 2026 all six file-system packages were released from this
repository as `0.7.0-preview.10`, in dependency order, raising the minimum in `Directory.Packages.props` after
each step:

1. `file-system-abstractions`;
2. `file-system`;
3. `file-system-local`, `file-system-in-memory` and `file-system-mime-detective`;
4. `file-system-azure-file-storage`.

Since then the file-system minimums in `Directory.Packages.props` are versions that are on nuget.org, and the CI
job "Release mode (main only)" is green for the file-system packages. A file-system package can be released on
its own; only a change across two packages needs the order described above.

## First release of the documents family

The documents family (`libs/documents`) is new on nuget.org. Its first release is `0.1.0-preview.2` of all 11
packages. The minimum versions of its own packages in `Directory.Packages.props` are already `0.1.0-preview.2`, so
nothing has to be raised between the steps; but in release mode a package restores the packages below it from
nuget.org, so each step starts only when the packages of the step before are on nuget.org (indexing can take a few
minutes). Until the wave is done, the CI job "Release mode (main only)" is red for the documents packages that are
not published yet.

The tag names are the package ids without `Ave.Extensions.`, as for every package:

| Tag name | Package |
|---|---|
| `documents-core` | `Ave.Extensions.Documents.Core` |
| `documents-fonts` | `Ave.Extensions.Documents.Fonts` |
| `documents-images` | `Ave.Extensions.Documents.Images` |
| `documents-page-model` | `Ave.Extensions.Documents.PageModel` |
| `documents-pdf` | `Ave.Extensions.Documents.Pdf` |
| `documents-typesetting` | `Ave.Extensions.Documents.Typesetting` |
| `documents-document-model` | `Ave.Extensions.Documents.DocumentModel` |
| `documents-layout` | `Ave.Extensions.Documents.Layout` |
| `documents-render-pdf` | `Ave.Extensions.Documents.Render.Pdf` |
| `documents-fluent` | `Ave.Extensions.Documents.Fluent` |
| `documents` | `Ave.Extensions.Documents` |

Release order, lowest layer first (the packages of one step can go in any order):

1. `documents-core` (it needs `Ave.Extensions.Functional` 0.1.0-preview.3 and `Ave.Extensions.ErrorPaths`
   0.0.3-preview.1, both on nuget.org);
2. `documents-fonts`, `documents-images`;
3. `documents-page-model`;
4. `documents-pdf`, `documents-typesetting`, `documents-document-model`: their tests use
   `Ave.Extensions.Documents.TestSupport`, which needs Fonts, Images and PageModel;
5. `documents-layout`, `documents-render-pdf` (its tests also use Typesetting), `documents-fluent`;
6. `documents`.

For example, step 1:

```shell
git tag documents-core/0.1.0-preview.2
git push origin documents-core/0.1.0-preview.2
```

## The documents raster back-end (milestone M2, stage 2)

Stage 2 adds two packages and changes five:

| Tag name | Package | What changed |
|---|---|---|
| `documents-graphics` | `Ave.Extensions.Documents.Graphics` | new |
| `documents-render-raster` | `Ave.Extensions.Documents.Render.Raster` | new |
| `documents-fonts` | `Ave.Extensions.Documents.Fonts` | glyph outlines (`TrueTypeFont.Shape`) |
| `documents-images` | `Ave.Extensions.Documents.Images` | PNG encoder, JPEG decoder |
| `documents-page-model` | `Ave.Extensions.Documents.PageModel` | the page-model contract (`PageModelRules`), moved here from Render.Pdf |
| `documents-render-pdf` | `Ave.Extensions.Documents.Render.Pdf` | uses `PageModelRules` from PageModel |
| `documents` | `Ave.Extensions.Documents` | `ToPages`, `ToPng`, `ToPngPages`, `ToImages` |

The new code of each upper package needs the new version of the lower ones, so this is a change across packages
(see above): release a step, raise the minimums the next step needs in `Directory.Packages.props` once the step is
on nuget.org, then release the next step. The two new packages are in `Directory.Packages.props` with a placeholder
minimum (`0.1.0-preview.2`, a version they never had); set it to their first version when they are released.

1. `documents-fonts`, `documents-images`, `documents-graphics`;
2. `documents-page-model` (its tests run in head mode);
3. `documents-render-pdf` (needs the new PageModel), `documents-render-raster` (needs the new Fonts, Images, PageModel
   and Graphics);
4. `documents`.

From the merge of this stage until step 4 is done, the documents rows of "Release mode (main only)" that are red are
these, and only these (measured with the job's steps on the branch): the libraries `Ave.Extensions.Documents.Render.Pdf`
(it needs `PageModelRules` from the new PageModel), `Ave.Extensions.Documents.Render.Raster` (Graphics is not on
nuget.org) and `Ave.Extensions.Documents` (Render.Raster is not), and the test projects
`UnitTests.Extensions.Documents.Render.Pdf` and `UnitTests.Extensions.Documents.Samples`, for the same reasons. The
release-mode tests of Core, DocumentModel, Graphics, Layout, Pdf and Typesetting stay green: the shared test helper
`Ave.Extensions.Documents.TestSupport` uses only API that is already on nuget.org, so the packages this stage does not
change can still be released in between.

## The HTML and CSS front-end of the documents family (M2)

Milestone M2 adds three packages to the documents family:

| Tag name | Package | Needs |
|---|---|---|
| `documents-html-parsing` | `Ave.Extensions.Documents.Html.Parsing` | Core |
| `documents-css` | `Ave.Extensions.Documents.Css` | Core |
| `documents-html` | `Ave.Extensions.Documents.Html` | Core, Html.Parsing, Css |

They need a Core that has `TextPosition`, which M2 adds: `documents-core/0.1.0-preview.2` on nuget.org does not have
it. So their first release is one wave of three steps:

1. a new release of `documents-core`;
2. once it is on nuget.org, a pull request that raises the minimum version of `Ave.Extensions.Documents.Core` in
   `Directory.Packages.props` to it, then `documents-html-parsing` and `documents-css` (in any order);
3. `documents-html`.

The minimum is not raised before the new Core is on nuget.org: release mode would then restore a Core that does not
exist. Until the wave is done, the CI job "Release mode (main only)" is red for the three new packages on `main`: in
release mode they restore Core `0.1.0-preview.2` from nuget.org and do not compile.

## Both parts of M2 in one release wave

The two sections above each give the order for their own packages. When both are released in one wave, this order
is true for both (the packages of one step can go in any order):

1. `documents-core`;
2. once it is on nuget.org, a pull request that raises the minimum version of `Ave.Extensions.Documents.Core` in
   `Directory.Packages.props` to it, then `documents-fonts`, `documents-images`, `documents-graphics`,
   `documents-html-parsing` and `documents-css`;
3. `documents-page-model` (its tests run in head mode), `documents-html`;
4. `documents-render-pdf` (needs the new PageModel), `documents-render-raster` (needs the new Fonts, Images, PageModel
   and Graphics);
5. `documents`.

Between the steps, raise the minimums the next step needs in `Directory.Packages.props` once the step is on
nuget.org, as both sections say.

## One-time setup

### GitHub environment `nuget`

In the repository on GitHub: Settings, Environments, New environment, name `nuget`.

- Required reviewers: the maintainers who approve a publish (at least one).
- Deployment branches and tags: "Selected branches and tags", add a tag rule `*/*`. Then only a release tag can use
  the environment.
- No secrets and no variables: trusted publishing needs neither.

### Rulesets for `main` and the release tags

In the repository on GitHub: Settings, Rules, Rulesets. Two rulesets, both with enforcement status "Active".

A branch ruleset for `main` (target: the default branch). Nobody pushes to `main` directly; every change is a pull
request.

| rule | setting |
|---|---|
| Restrict deletions | on |
| Block force pushes | on |
| Require a pull request before merging | required approvals: 0 |
| Require status checks to pass | one check: `CI result` |

- Required approvals is 0 because there is one maintainer, and GitHub does not accept an approval from the author
  of the pull request.
- `CI result` is the last job of `.github/workflows/ci.yml`. It fails when any other pull request job failed or
  was cancelled. The other jobs cannot be required themselves: most are skipped when their family is not affected.
- The jobs that run on `main` only ("Release mode" and the Azure integration tests) run after the merge. Look at
  the CI run on `main` after merging a pull request.
- Bypass list: the role "Repository admin", with "For pull requests only". An admin can then merge a pull request
  while its check is red, for example when CI itself is broken, but cannot push to `main` directly either.

A tag ruleset for the release tags (target: the pattern `*/*`). A release tag never moves.

| rule | setting |
|---|---|
| Restrict updates | on |
| Restrict deletions | on |

- Bypass list: the role "Repository admin", with "Always": for deleting the tag of a failed build (see "Release
  one package").
- Creating a release tag is not restricted and needs no pull request. The release workflow checks that the tagged
  commit is on `main`, and the publish job waits for approval.

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

## The documents layout backlog (milestone M2, stage 1)

Nothing is released per stage: stage 1's packages go out with the M2 release of the documents family. Stage 1 changes
six packages and adds none:

| Tag name | Package | What changed |
|---|---|---|
| `documents-core` | `Ave.Extensions.Documents.Core` | the input codes `Invalid.Orphans`, `Invalid.Widows`, `Invalid.LineHeight` |
| `documents-fonts` | `Ave.Extensions.Documents.Fonts` | line metrics as Chromium reads them (`FontMetrics.Line`, USE_TYPO_METRICS) |
| `documents-typesetting` | `Ave.Extensions.Documents.Typesetting` | shaping again at a line break, normalization before shaping |
| `documents-document-model` | `Ave.Extensions.Documents.DocumentModel` | line heights, the strut, orphans and widows; `ModelLimits`, `ModelCheck` |
| `documents-layout` | `Ave.Extensions.Documents.Layout` | page starts and cuts, early breaks, orphans and widows, line heights, the error `EngineDefect` |
| `documents-fluent` | `Ave.Extensions.Documents.Fluent` | `.Orphans()`, `.Widows()`, `.LineHeight…()` |

Release order within this stage, lowest layer first: `documents-core`; `documents-fonts`; `documents-typesetting`,
`documents-document-model`; `documents-layout`; `documents-fluent`; then `documents` (unchanged code, released with
the new minimums). Each step raises the minimums the next step needs in `Directory.Packages.props` once it is on
nuget.org (a change across packages, see above). From the merge of this stage until the M2 release, the CI job
"Release mode (main only)" is red: Layout and Fluent do not compile against the released `0.1.0-preview.2` packages
(`DocumentModel.LineHeight` is not in them), and the sample goldens that show the strut or the orphans rule differ.

Binary breaks against `0.1.0-preview.2` (allowed on the `0.1.0-preview` line; the release note names them):

- `Layout.BreakAppeal` has two more values, `LastResort` before `ViolatesAvoid` and `ViolatesOrphansWidows` between
  `ViolatesAvoid` and `Perfect`, so the numbers of the old values moved (`Perfect` was 1, is 3); code compiled against
  the old values must be compiled again.
- `Layout.LayoutResult.Place(BoxFragment, BreakToken?, BreakAppeal)` is replaced by the overload with a fourth, optional
  parameter (the early break); a caller compiled against the old one must be compiled again.
- The default of `LayoutResult.Placed`'s `Appeal` parameter is `Perfect` as before, but the number behind it changed
  with the enumeration (see the first point), so a caller compiled against the old default passes `ViolatesAvoid`.

Pages change without a code change: paragraphs get orphans and widows of 2 (CSS's initial values; `Document.Orphans(1)`
and `.Widows(1)` give the old pages back), every line starts with its paragraph's strut (a line of small text is as
high as the paragraph's own line height), decomposed text is shown with composed glyphs, and bottom padding that does
not fit below a box's content moves to the next page.
