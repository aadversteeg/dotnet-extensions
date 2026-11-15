<#
.SYNOPSIS
    Builds and checks the samples with independent tools.

.DESCRIPTION
    1. Publishes the samples app (Release, framework-dependent, no native app host) to out/publish.
    2. Scans the publish folder: every .dll must be an IL-only managed assembly; no other binaries may exist.
    3. Runs the samples app, which writes the sample PDFs (letter, long text, and the timesheet for three data sets in
       Dutch and English) and their text serialisations to out/samples.
    4. In a throw-away Alpine container: qpdf --check on every PDF, pdftotext for the text, pdffonts for the
       embedded subset fonts, pdfimages -list for the embedded images (and their soft masks), every embedded font
       program extracted (mutool extract) and opened with fontTools, and PNG renderings with two independent renderers
       (poppler's pdftoppm and MuPDF's mutool) to out/samples/png.
    5. When the reporting PoC's PDFs are found (-PocPdfs, default ../reporting-poc/out next to this repository):
       side-by-side PNGs of every timesheet page, ours on the left and the PoC's Typst page on the right, both rendered
       by poppler at 100 dpi, to out/samples/sbs.
    Tools run in Docker only; nothing here is part of the engine. Needs Docker and network access for apk.

.PARAMETER PocPdfs
    The folder with the PoC's sample-{normal,long,edge}.{nl,en}.typst.pdf files.

.EXAMPLE
    pwsh -NoProfile -File tools/verify-samples.ps1
#>
[CmdletBinding()]
param(
    [string]$PocPdfs = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'reporting-poc/out')
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$publish = Join-Path $root 'out/publish'
$samples = Join-Path $root 'out/samples'

Write-Host '== 1. publish'
dotnet publish (Join-Path $root 'samples/Ave.Documents.Samples') -c Release -o $publish -nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'publish failed' }

Write-Host '== 2. scan the publish folder for native code'
$allowedData = '.json', '.pdb', '.xml', '.ttf', '.txt', '.png'
$problems = @()
foreach ($file in Get-ChildItem $publish -Recurse -File) {
    if ($file.Extension -eq '.dll') {
        $stream = [IO.File]::OpenRead($file.FullName)
        try {
            $pe = [Reflection.PortableExecutable.PEReader]::new($stream)
            $cor = $pe.PEHeaders.CorHeader
            if ($null -eq $cor) { $problems += "native (no CLR header): $($file.Name)" }
            elseif (-not $cor.Flags.HasFlag([Reflection.PortableExecutable.CorFlags]::ILOnly)) { $problems += "mixed-mode: $($file.Name)" }
            else { Write-Host ("   managed IL-only  {0}" -f $file.Name) }
        }
        finally { $stream.Dispose() }
    }
    elseif ($allowedData -notcontains $file.Extension) {
        $problems += "unexpected file: $($file.Name)"
    }
}
if ($problems.Count -gt 0) { $problems | ForEach-Object { Write-Host "   $_" }; throw 'publish folder contains native code or unexpected files' }
Write-Host '   no native code in the publish folder'

Write-Host '== 3. run the samples'
if (Test-Path $samples) { Remove-Item -Recurse -Force $samples }
dotnet (Join-Path $publish 'Ave.Documents.Samples.dll') $samples
if ($LASTEXITCODE -ne 0) { throw 'samples failed' }

Write-Host '== 4. independent checks in Docker'
$script = @'
set -e
apk add -q --no-cache qpdf poppler-utils mupdf-tools py3-fonttools >/dev/null
echo "qpdf $(qpdf --version | head -1 | cut -d' ' -f3), $(pdftoppm -v 2>&1 | head -1), mutool $(mutool -v 2>&1 | head -1 | cut -d' ' -f3), fontTools $(python3 -c 'import fontTools; print(fontTools.version)')"
mkdir -p /work/png /work/fonts
for f in /work/*.pdf; do
  name=$(basename "$f" .pdf)
  qpdf --check "$f" | sed "s|^|   qpdf $name: |"
  pdftotext -layout "$f" "/work/$name.txt"
  pdffonts "$f" | sed "s|^|   fonts $name: |"
  pdfimages -list "$f" | sed "s|^|   images $name: |"
  mkdir -p "/work/fonts/$name" && (cd "/work/fonts/$name" && mutool extract "$f" >/dev/null)
  for font in /work/fonts/$name/*.ttf; do
    python3 -c "import sys; from fontTools.ttLib import TTFont; f = TTFont(sys.argv[1]); g = f['glyf']; n = sum(1 for name in f.getGlyphOrder() if g[name].numberOfContours != 0); print('   fontTools $name', sys.argv[1].rsplit('/', 1)[1], 'tables', ' '.join(sorted(f.keys())), 'glyphs with outlines', n)" "$font"
  done
  pdftoppm -r 100 -png "$f" "/work/png/$name-poppler"
  mutool draw -q -r 100 -o "/work/png/$name-mupdf-%d.png" "$f"
done
ls /work/png | sed 's|^|   png: |'
'@
docker run --rm -v "${samples}:/work" alpine:latest sh -c $script
if ($LASTEXITCODE -ne 0) { throw 'independent checks failed' }

if (Test-Path $PocPdfs) {
    Write-Host "== 5. side by side with the PoC's Typst output ($PocPdfs)"
    $compose = @'
set -e
apk add -q --no-cache poppler-utils py3-pillow >/dev/null
mkdir -p /work/sbs /tmp/poc
for f in /poc/sample-*.typst.pdf; do pdftoppm -r 100 -png "$f" "/tmp/poc/$(basename "$f" .pdf)"; done
python3 - <<'PY'
import glob, os, re
from PIL import Image
for set_ in ("normal", "long", "edge"):
    for lang in ("nl", "en"):
        ours = sorted(glob.glob(f"/work/png/timesheet-{set_}.{lang}-poppler-*.png"), key=lambda p: int(re.findall(r"-(\d+)\.png$", p)[0]))
        typst = sorted(glob.glob(f"/tmp/poc/sample-{set_}.{lang}.typst-*.png"), key=lambda p: int(re.findall(r"-(\d+)\.png$", p)[0]))
        for page in range(max(len(ours), len(typst))):
            left = Image.open(ours[page]) if page < len(ours) else None
            right = Image.open(typst[page]) if page < len(typst) else None
            w, h = (left or right).size
            sheet = Image.new("RGB", (2 * w + 20, h), (128, 128, 128))
            if left: sheet.paste(left, (0, 0))
            if right: sheet.paste(right, (w + 20, 0))
            path = f"/work/sbs/timesheet-{set_}.{lang}-p{page + 1}.png"
            sheet.save(path)
            print("   side by side:", os.path.basename(path), "ours" if left else "-", "| typst" if right else "| -")
PY
'@
    docker run --rm -v "${samples}:/work" -v "${PocPdfs}:/poc:ro" alpine:latest sh -c $compose
    if ($LASTEXITCODE -ne 0) { throw 'side-by-side rendering failed' }
}
else {
    Write-Host "== 5. skipped: no PoC PDFs at $PocPdfs"
}

Write-Host "Done. PDFs, text and PNGs are in $samples"
