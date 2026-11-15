# Releasing

Every package has its own version and is released on its own. The release workflow is not in this
repository yet; this page describes the rules it will follow.

## Release tags

A release tag is `<tag name>/<version>`, for example `functional-core/0.1.0-preview.4`.

The tag name is the package id without `Ave.Extensions.`, in lower case, with the words split by hyphens:
`Ave.Extensions.ErrorPaths.AspNetCore` becomes `error-paths-asp-net-core`. A project can override the derived
name with a `ReleaseTagName` property. This command prints every name and fails when two packages share one:

```shell
dotnet msbuild eng/release-tags.proj -nologo
```

No version is stored in the project files. Local builds are `0.0.0-dev`; a release passes the version from
the tag with `-p:Version=<version>`.

## Dependencies between our own packages

A library references another package of this repository with a `PackageReference`. Its version in
`Directory.Packages.props` is the lowest version that any package in this repository needs. A project that needs
a newer version sets `VersionOverride` on its reference.

- `UseRepoProjectReferences=true` (the default, for local work and the CI head build): the reference becomes a
  project reference to `libs/*/<PackageId>/<PackageId>.csproj`.
- `UseRepoProjectReferences=false` (release): the reference stays a package reference. The packed nuspec carries
  the minimum version, and the lower package is not built.

A change that spans two packages is one pull request. Release the lower package first, raise its minimum in
`Directory.Packages.props` (or the `VersionOverride` of the project that needs it), then release the upper one.

Release mode for one package:

```shell
dotnet pack libs/functional/Ave.Extensions.Functional/Ave.Extensions.Functional.csproj \
  -c Release -p:UseRepoProjectReferences=false -p:Version=<version>
```

## First release wave

The file-system packages were developed past the versions on nuget.org: nuget.org has `0.6.0-preview.1`, the
source is past `0.7.0-preview.9`. So the file-system minimum versions in `Directory.Packages.props`
(`0.6.0-preview.1`) cannot be right before the first release from this repository. In release mode
`Ave.Extensions.FileSystem` does not compile against them. `.Local`, `.InMemory`, `.MimeDetective` and
`.AzureFileStorage` do compile, but against an older Abstractions API than their code is written for, so a package
built that way must not be published.

The first wave therefore goes in dependency order, raising the minimum after each step:

1. `file-system-abstractions`
2. raise `Ave.Extensions.FileSystem.Abstractions` in `Directory.Packages.props` to that version, then release
   `file-system`
3. raise `Ave.Extensions.FileSystem`, then release `file-system-local`, `file-system-in-memory` and
   `file-system-mime-detective`
4. raise `Ave.Extensions.FileSystem.Local`, then release `file-system-azure-file-storage`

The functional, error-paths and fluent-contracts minimums are the versions that are on nuget.org today, so those
packages can be released in any order.
