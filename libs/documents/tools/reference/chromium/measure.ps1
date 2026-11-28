<#
.SYNOPSIS
    Measures the Chromium reference cases with nothing on the host but Docker.

.DESCRIPTION
    Runs measure.mjs in the official Playwright container of the version pinned in package.json (the image is pinned
    by digest, so the same Chromium measures on every machine), and extract.sh in the reference tools image between
    its two steps:

    1. Playwright container: npm ci, then node measure.mjs --step print (Chromium's print of every case), with font
       hinting off (--font-render-hinting=none, set in measure.mjs: see README.md, "The reference").
    2. Reference tools image (../tools.Dockerfile): extract.sh (pdftotext -bbox, rectangles.py, positions.py, ink.py,
       links.py, area.py: the width of each page's page area, tool versions).
    3. Playwright container: node measure.mjs --step boxes (box sizes per case, in a window as wide as the page area
       step 2 read), then --step words (words.txt per case, versions.txt).

    The output goes to out/ (ignored by git); compare it with measured/ (see README.md). Works on Linux, macOS and
    Windows (Docker with Linux containers). On Linux and macOS the containers run as the current user, so out/ and
    node_modules/ are not root-owned. Needs network access for the first image pulls and for npm ci.

.PARAMETER Cases
    The cases to measure, by name (default: every case).

.PARAMETER CaseFolder
    The folder of the HTML files, relative to this folder (default: cases). The whole documents are in documents; a
    folder elsewhere in the family works too, as long as the files it links to are inside the family folder.

.PARAMETER PrintOnly
    Reads the prints back for their words and positions only (print.txt, words.txt and the files that need no word of
    a case): the line-break documents of line-breaks.py need no more, and the glyph, paint order, clip, image and
    destination files are read for the cases.

.PARAMETER PlaywrightImage
    The Playwright image: v1.65.1-noble by digest, the version package.json pins (Chromium 159).

.EXAMPLE
    pwsh -NoProfile -File tools/reference/chromium/measure.ps1
.EXAMPLE
    pwsh -NoProfile -File tools/reference/chromium/measure.ps1 mf4-line-0 widths
.EXAMPLE
    pwsh -NoProfile -File tools/reference/chromium/measure.ps1 -CaseFolder documents
#>
[CmdletBinding(PositionalBinding = $false)]
param(
    [Parameter(ValueFromRemainingArguments)]
    [string[]]$Cases = @(),
    [string]$CaseFolder = 'cases',
    [switch]$PrintOnly,
    [string]$PlaywrightImage = 'mcr.microsoft.com/playwright:v1.65.1-noble@sha256:1507cbfc550a63a6b7396791ca4f00df17beb56342d2447b3f106c306f525579'
)

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
# The family folder (libs/documents): the cases load the fonts from its assets/ folder.
$family = (Resolve-Path (Join-Path $here '../../..')).Path
$out = Join-Path $here 'out'
New-Item -ItemType Directory -Force $out | Out-Null

$user = if ($IsWindows) { @() } else { @('--user', "$(id -u):$(id -g)") }
$names = @(if ($Cases.Count -gt 0) { $Cases } else { Get-ChildItem (Join-Path $here $CaseFolder) -Filter *.html | Sort-Object Name | ForEach-Object BaseName })

# MEASURE_IMAGE: measure.mjs records the image in versions.txt, next to the Chromium version and flags.
$playwright = @('run', '--rm') + $user + @('-e', 'HOME=/tmp', '-e', "MEASURE_IMAGE=$PlaywrightImage", '-v', "${family}:/family", '-w', '/family/tools/reference/chromium', $PlaywrightImage)

Write-Host "== 1. print with Chromium ($PlaywrightImage)"
docker @playwright bash -c "npm ci --no-audit --no-fund --loglevel=error && node measure.mjs --cases '$CaseFolder' --step print $($names -join ' ')"
if ($LASTEXITCODE -ne 0) { throw 'the print step failed' }

Write-Host '== 2. read the prints back in the reference tools image'
$dockerfile = Join-Path $here '../tools.Dockerfile'
$tools = 'ave-extensions-documents-tools:' + (Get-FileHash $dockerfile -Algorithm SHA256).Hash.ToLowerInvariant().Substring(0, 12)
Get-Content -Raw $dockerfile | docker build -q -t $tools -
if ($LASTEXITCODE -ne 0) { throw "building $tools failed" }
$only = if ($PrintOnly) { @('-e', 'EXTRACT_PRINT_ONLY=1') } else { @() }
docker run --rm @user @only -v "${out}:/out" -v "${here}:/src:ro" $tools bash /src/extract.sh @names
if ($LASTEXITCODE -ne 0) { throw 'the extract step failed' }

Write-Host '== 3. box sizes at the page area''s width, words and versions'
docker @playwright node measure.mjs --cases $CaseFolder --step boxes @names
if ($LASTEXITCODE -ne 0) { throw 'the boxes step failed' }
docker @playwright node measure.mjs --cases $CaseFolder --step words @names
if ($LASTEXITCODE -ne 0) { throw 'the words step failed' }

Write-Host "Done. Compare $out with $(Join-Path $here 'measured')"
