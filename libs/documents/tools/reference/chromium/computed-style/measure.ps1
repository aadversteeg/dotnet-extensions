<#
.SYNOPSIS
    Measures the computed-style reference with nothing on the host but Docker.

.DESCRIPTION
    Runs computed-style/measure.mjs in the official Playwright container of the version pinned in ../package.json (by
    digest, the same image as ../measure.ps1), from the folder above so that it uses that folder's npm packages. The
    output goes to out/ (ignored by git); compare it with measured/ and copy it over after a deliberate re-measurement
    (README.md). On Linux and macOS the container runs as the current user.

.PARAMETER Step
    corpus, wpt, colors, or all (default).

.PARAMETER Cases
    The corpus cases to measure, by name (default: every case).

.EXAMPLE
    pwsh -NoProfile -File tools/reference/chromium/computed-style/measure.ps1
.EXAMPLE
    pwsh -NoProfile -File tools/reference/chromium/computed-style/measure.ps1 -Step wpt
#>
[CmdletBinding()]
param(
    [ValidateSet('all', 'corpus', 'wpt', 'colors')]
    [string]$Step = 'all',
    [Parameter(ValueFromRemainingArguments)]
    [string[]]$Cases = @(),
    [string]$PlaywrightImage = 'mcr.microsoft.com/playwright:v1.65.1-noble@sha256:1507cbfc550a63a6b7396791ca4f00df17beb56342d2447b3f106c306f525579'
)

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$family = (Resolve-Path (Join-Path $here '../../../..')).Path
$user = if ($IsWindows) { @() } else { @('--user', "$(id -u):$(id -g)") }
$playwright = @('run', '--rm') + $user + @('-e', 'HOME=/tmp', '-e', "MEASURE_IMAGE=$PlaywrightImage", '-v', "${family}:/family", '-w', '/family/tools/reference/chromium', $PlaywrightImage)

docker @playwright bash -c "npm ci --no-audit --no-fund --loglevel=error && node computed-style/measure.mjs --step $Step $($Cases -join ' ')"
if ($LASTEXITCODE -ne 0) { throw 'measuring failed' }
Write-Host "Done. Compare $(Join-Path $here 'out') with $(Join-Path $here 'measured')"
