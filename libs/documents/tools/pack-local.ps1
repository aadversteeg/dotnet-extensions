<#
.SYNOPSIS
    Packs the engine's product libraries and copies the packages into the private local feed.

.DESCRIPTION
    The engine is not on nuget.org; applications on this machine (SmallBusiness first) restore it from the folder that
    %JASMIN_LOCAL_FEED% names, next to nuget.org. This script puts one version of the engine there, reproducibly:

    1. Refuses a working tree with changes, so the commit written into every package (nuspec <repository commit>) is
       the code that is in it.
    2. Finds the packed projects by the repository's own rule (Directory.Build.props): every project under src/ that
       is neither a UnitTests.* project nor a sample or helper (IsSampleOrSupport). That is the 11 libraries; the
       architecture test PackageRulesTests checks the same list.
    3. Cleans: deletes out/packages and each packed library's Release output (bin/Release, obj/Release).
    4. Packs each library in Release, one project and one MSBuild node at a time, at below-normal priority (the
       machine may host other services), into out/packages: a .nupkg and a .snupkg per library.
    5. Checks every package before anything is copied: its version is the expected one, and its dependencies are only
       other Ave.Extensions.Documents.* packages of the same version or the four Ave.Extensions.* packages.
    6. Refuses to overwrite: a package version in the feed is never replaced, because NuGet caches packages by id and
       version (the global packages folder) and a replaced file would be ignored on machines that restored it before.
       To replace one, raise the version.
    7. Copies the packages into the feed and lists what was written (file, size, SHA-256).

    Nothing is published anywhere else.

.PARAMETER Feed
    The feed folder. Default: the JASMIN_LOCAL_FEED environment variable.

.EXAMPLE
    pwsh -NoProfile -File tools/pack-local.ps1
#>
[CmdletBinding()]
param(
    [string] $Feed = $env:JASMIN_LOCAL_FEED
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# Children (dotnet, MSBuild) inherit the priority of this process.
[System.Diagnostics.Process]::GetCurrentProcess().PriorityClass = [System.Diagnostics.ProcessPriorityClass]::BelowNormal
$env:MSBUILDDISABLENODEREUSE = '1'

$root = Split-Path -Parent $PSScriptRoot
$staging = Join-Path $root 'out/packages'

if ([string]::IsNullOrWhiteSpace($Feed)) { throw 'No feed: pass -Feed or set JASMIN_LOCAL_FEED.' }
if (-not (Test-Path -LiteralPath $Feed -PathType Container)) { throw "The feed folder '$Feed' does not exist." }

# 1. A clean working tree.
$changes = git -C $root status --porcelain
if ($LASTEXITCODE -ne 0) { throw 'git status failed.' }
if ($changes) { throw "The working tree has changes; commit them first:`n$($changes -join "`n")" }
$commit = (git -C $root rev-parse --short HEAD).Trim()

# 2. The packed projects.
$projects = Get-ChildItem -Path (Join-Path $root 'src') -Filter '*.csproj' -Recurse |
    Where-Object { $_.FullName -notmatch '[\\/]obj[\\/]' -and -not $_.BaseName.StartsWith('UnitTests.') } |
    Where-Object { -not ([xml](Get-Content -LiteralPath $_.FullName -Raw)).SelectSingleNode("//IsSampleOrSupport[text()='true']") } |
    Sort-Object BaseName
$ids = @($projects | ForEach-Object BaseName)
Write-Host "Packing $($ids.Count) libraries at commit ${commit}: $($ids -join ', ')"

# The version every package must have, as MSBuild evaluates it.
$version = (dotnet msbuild $projects[0].FullName -getProperty:PackageVersion -p:Configuration=Release).Trim()
if ($LASTEXITCODE -ne 0 -or -not $version) { throw 'Could not read the package version.' }

# 3. Clean.
if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
foreach ($project in $projects) {
    foreach ($output in 'bin/Release', 'obj/Release') {
        $path = Join-Path $project.DirectoryName $output
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force }
    }
}

# 4. Pack.
foreach ($project in $projects) {
    dotnet pack $project.FullName -c Release -m:1 -o $staging --nologo -v:minimal
    if ($LASTEXITCODE -ne 0) { throw "dotnet pack failed for $($project.BaseName)." }
}

# 5. Check every package: version and dependencies, read from the nuspec inside it.
Add-Type -AssemblyName System.IO.Compression.FileSystem
$allowed = @('Ave.Extensions.Functional', 'Ave.Extensions.Functional.Core', 'Ave.Extensions.ErrorPaths', 'Ave.Extensions.ErrorPaths.Functional')
$problems = [System.Collections.Generic.List[string]]::new()
foreach ($id in $ids) {
    $nupkg = Join-Path $staging "$id.$version.nupkg"
    if (-not (Test-Path -LiteralPath $nupkg)) { $problems.Add("$id`: no $id.$version.nupkg"); continue }
    if (-not (Test-Path -LiteralPath (Join-Path $staging "$id.$version.snupkg"))) { $problems.Add("$id`: no symbols package") }

    $zip = [System.IO.Compression.ZipFile]::OpenRead($nupkg)
    try {
        $entry = $zip.Entries | Where-Object { $_.FullName -eq "$id.nuspec" }
        $reader = [System.IO.StreamReader]::new($entry.Open())
        try { $nuspec = [xml]$reader.ReadToEnd() } finally { $reader.Dispose() }
    }
    finally { $zip.Dispose() }

    if ($nuspec.package.metadata.version -ne $version) { $problems.Add("$id`: version $($nuspec.package.metadata.version)") }
    foreach ($dependency in $nuspec.SelectNodes("//*[local-name()='dependency']")) {
        $name = $dependency.GetAttribute('id')
        $isEngine = $ids -contains $name -and $dependency.GetAttribute('version') -eq $version
        if (-not $isEngine -and $allowed -notcontains $name) {
            $problems.Add("$id`: depends on $name $($dependency.GetAttribute('version'))")
        }
    }
}
$unexpected = Get-ChildItem -LiteralPath $staging -File | Where-Object { $ids -notcontains ($_.Name -replace "\.$([regex]::Escape($version))\.s?nupkg$", '') }
foreach ($file in $unexpected) { $problems.Add("unexpected file $($file.Name)") }
if ($problems.Count -gt 0) { throw "The packages break the package rules:`n$($problems -join "`n")" }

# 6. Never overwrite a version in the feed.
$packages = Get-ChildItem -LiteralPath $staging -File | Sort-Object Name
$existing = @($packages | Where-Object { Test-Path -LiteralPath (Join-Path $Feed $_.Name) })
if ($existing.Count -gt 0) {
    throw "The feed already holds $($existing.Count) of these packages (raise the version to replace them):`n$(($existing | ForEach-Object Name) -join "`n")"
}

# 7. Copy and list.
foreach ($package in $packages) { Copy-Item -LiteralPath $package.FullName -Destination $Feed }
Write-Host "Written to ${Feed}:"
$packages | ForEach-Object {
    $written = Get-Item -LiteralPath (Join-Path $Feed $_.Name)
    [pscustomobject]@{
        File   = $written.Name
        Bytes  = $written.Length
        SHA256 = (Get-FileHash -LiteralPath $written.FullName -Algorithm SHA256).Hash
    }
} | Format-Table -AutoSize | Out-String -Width 200 | Write-Host
