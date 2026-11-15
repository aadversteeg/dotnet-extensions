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

The libraries target `netstandard2.0` and `net10.0`, with two differences: `Ave.Extensions.FileSystem.InMemory` and
`Ave.Extensions.FileSystem.AzureFileStorage` also target `netstandard2.1` (see the file-system README), and
`Ave.Extensions.ErrorPaths.AspNetCore` targets `net10.0` only.

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
the test assembly loads is the `netstandard2.0` build. `UnitTests.Extensions.ErrorPaths.AspNetCore` is left as
it is, because its library has no `netstandard2.0` build. The switch lives in `Directory.Build.targets`.

Delete the `bin` and `obj` folders before you switch between the two modes in the same working tree. The two
builds of a library can have the same file size and time stamp, and then the build does not copy the new one: the
test project keeps the library file of the other mode, and `NS20-001` fails.

After a Release build, this check reads the assembly references of every `netstandard2.0` build and fails when
one references an assembly, or a version of it, that the package's declared dependencies do not give a consumer:

```shell
dotnet run --project eng/Ns20ReferenceCheck -c Release -- --configuration Release
```

Such a reference compiles fine, but fails at run time where that assembly version is not present, for example
on .NET Framework.

`.github/workflows/ci.yml` runs this check and the tests (Linux and Windows, both builds) for every pull request
and every push to `main`.

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
