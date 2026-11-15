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

The libraries target `netstandard2.0` and `net10.0`. Exceptions: `Ave.Extensions.ErrorPaths.AspNetCore` targets
`net10.0` only, and the file-system family targets `netstandard2.1` and `net10.0` for now.

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
