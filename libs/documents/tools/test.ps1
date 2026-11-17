<#
.SYNOPSIS
    Builds and tests the documents family at low priority, so the machine stays usable while the tests run.

.DESCRIPTION
    Runs dotnet build and dotnet test for the family solution (or the projects given) at below-normal process
    priority (nice 10 on Linux and macOS); MSBuild and the test hosts inherit it. MSBuild node reuse is off, so no build
    process stays behind after the run. Each argument is passed to dotnet as it is, without a shell, so a filter such
    as "Category!=Reference&FullyQualifiedName~Layout" needs no quoting tricks. Works on Linux, macOS and Windows.

.PARAMETER Project
    The solution or project to test (default: the family solution extensions.slnx).

.PARAMETER Filter
    A dotnet test filter, for example "Category!=Reference" to leave out the tests that need Docker.

.PARAMETER Configuration
    The build configuration (default: Release).

.PARAMETER SingleNode
    Build with one MSBuild node (-m:1): slower, lightest on the machine.

.EXAMPLE
    pwsh -NoProfile -File tools/test.ps1
.EXAMPLE
    pwsh -NoProfile -File tools/test.ps1 -Project UnitTests.Extensions.Documents.Layout -Filter "FullyQualifiedName~Chromium"
#>
[CmdletBinding()]
param(
    [string]$Project = 'extensions.slnx',
    [string]$Filter,
    [string]$Configuration = 'Release',
    [switch]$SingleNode
)

$ErrorActionPreference = 'Stop'
$family = Split-Path -Parent $PSScriptRoot
Set-Location $family

$env:MSBUILDDISABLENODEREUSE = '1'

# Children (dotnet, MSBuild, the test hosts) inherit the priority of the process that starts them. On Windows that is
# this process's priority class. On Linux and macOS .NET sets it for one thread only, so dotnet starts under nice.
if ($IsWindows) {
    [System.Diagnostics.Process]::GetCurrentProcess().PriorityClass = [System.Diagnostics.ProcessPriorityClass]::BelowNormal
}

function Invoke-Dotnet([string[]]$Arguments) {
    if ($IsWindows) { & dotnet @Arguments } else { & nice -n 10 dotnet @Arguments }
}

$build = @('build', $Project, '-c', $Configuration, '-nologo')
if ($SingleNode) { $build += '-m:1' }
Invoke-Dotnet $build
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$test = @('test', $Project, '-c', $Configuration, '--no-build', '-nologo')
if ($Filter) { $test += @('--filter', $Filter) }
Invoke-Dotnet $test
exit $LASTEXITCODE
