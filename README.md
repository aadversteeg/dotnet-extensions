# dotnet-extensions

The `Ave.Extensions.*` libraries for .NET, in one repository. Every package has its own version and is
released on its own.

## Families

| Folder | Packages | Read more |
|--------|----------|-----------|
| `libs/functional` | `Ave.Extensions.Functional.Core`, `Ave.Extensions.Functional`, `Ave.Extensions.Functional.FluentAssertions` | [README](libs/functional/README.md) |
| `libs/error-paths` | `Ave.Extensions.ErrorPaths`, `.Functional`, `.Serialization`, `.FluentAssertions`, `.AspNetCore` | [README](libs/error-paths/README.md) |
| `libs/fluent-contracts` | `Ave.Extensions.FluentContracts`, `.ForTesting`, `.ForValidation` | [README](libs/fluent-contracts/README.md) |
| `libs/file-system` | `Ave.Extensions.FileSystem.Abstractions`, `Ave.Extensions.FileSystem`, `.Local`, `.InMemory`, `.AzureFileStorage`, `.MimeDetective` | [README](libs/file-system/README.md) |
| `libs/documents` | `Ave.Extensions.Documents` and `.Core`, `.Fonts`, `.Images`, `.Pdf`, `.Typesetting`, `.DocumentModel`, `.PageModel`, `.Layout`, `.Render.Pdf`, `.Fluent`: a document engine that writes PDF/A and PDF/UA | [README](libs/documents/README.md) |

The libraries target `netstandard2.0` and `net10.0`, with these differences: `Ave.Extensions.FileSystem.InMemory` and
`Ave.Extensions.FileSystem.AzureFileStorage` also target `netstandard2.1` (see the file-system README), and
`Ave.Extensions.ErrorPaths.AspNetCore` and the documents family target `net10.0` only.

## Building and testing

The .NET 10 SDK is required (see `global.json`).

```shell
dotnet build dotnet-extensions.slnx
dotnet test dotnet-extensions.slnx
```

One family only:

```shell
cd libs/functional
dotnet test extensions.slnx
```

The test projects target `net10.0`, so by default they test the `net10.0` build of the libraries. To run the
same tests against the `netstandard2.0` build:

```shell
dotnet test dotnet-extensions.slnx -p:UseNs20=true
```

In that mode every test project also runs test `NS20-001`, which fails unless every `Ave.Extensions` library
the test assembly loads is the `netstandard2.0` build.

To run them against the `netstandard2.1` build of the libraries that have one, and the `netstandard2.0` build of
the others (what an application on .NET Core 3.0 up to .NET 9 gets):

```shell
dotnet test dotnet-extensions.slnx -p:UseNs21=true
```

In that mode every test project runs test `NS21-001` instead, which fails unless every `Ave.Extensions` library
the test assembly loads is that build. Only `Ave.Extensions.FileSystem.InMemory` and
`Ave.Extensions.FileSystem.AzureFileStorage` have a `netstandard2.1` build, so for the other families this mode
tests the same as `-p:UseNs20=true`.

On Windows, to run the tests on .NET Framework 4.8 against the `netstandard2.0` build of the libraries (what an
application on .NET Framework gets):

```shell
dotnet test dotnet-extensions.slnx -p:UseNet48=true
```

In that mode the test projects target `net48` instead of `net10.0`. Every test project runs `NS20-001`, and also
`NET48-001`, which fails unless the tests run on .NET Framework. A few APIs that the test code uses do not exist on
.NET Framework; `eng/Net48Polyfills.cs` provides them in this mode only. On .NET Framework,
`Ave.Extensions.FileSystem.Local` cannot use long paths unless Windows allows it; see "Long paths on .NET
Framework" in the [file-system README](libs/file-system/README.md#long-paths-on-net-framework).

Use one switch at a time; together they stop the build with an error. `UnitTests.Extensions.ErrorPaths.AspNetCore`
and the documents tests are left as they are in all three modes, because their libraries have no `netstandard2.0`
build: with `-p:UseNet48=true` they stay on `net10.0`. The switches live in `Directory.Build.targets`.

Delete the `bin` and `obj` folders before you switch between the four modes (the default and the three switches)
in the same working tree. Two builds of a library can have the same file size and time stamp, and then the build
does not copy the new one: the test project keeps the library file of the other mode, and `NS20-001` or
`NS21-001` fails.

After a Release build, this check reads the assembly references of every `netstandard2.0` build and fails when
one references an assembly, or a version of it, that the package's declared dependencies do not give a consumer:

```shell
dotnet run --project eng/Ns20ReferenceCheck -c Release -- --configuration Release
```

Such a reference compiles fine, but fails at run time where that assembly version is not present, for example
on .NET Framework.

`.github/workflows/ci.yml` runs this check and the tests (Linux and Windows; the `net10.0` and the `netstandard2.0`
build), and on Windows also on .NET Framework 4.8, for every pull request and every push to `main`. When the change
affects the file-system family, it also runs the file-system tests with `-p:UseNs21=true`. The documents family
runs on Linux and Windows against its `net10.0` build only; its reference tests need Docker with Linux containers and are skipped where there is none
(see the documents README).

Nobody pushes to `main` directly: every change is a pull request, and it can be merged when the job `CI result`
is green. That job fails when any other pull request job failed. The rules are in
[docs/RELEASING.md](docs/RELEASING.md), "Rulesets for `main` and the release tags".

## References between the packages

A library references another package of this repository as a NuGet package:

```xml
<PackageReference Include="Ave.Extensions.ErrorPaths" />
```

`Directory.Packages.props` holds the minimum version it needs. By default (`UseRepoProjectReferences=true`)
`Directory.Build.targets` turns that reference into a project reference, so the whole repository builds and tests
against its own latest code. A release builds with `-p:UseRepoProjectReferences=false`, so the package depends
on the published minimum version. See [docs/RELEASING.md](docs/RELEASING.md).

## License

MIT, see [LICENSE](LICENSE).
