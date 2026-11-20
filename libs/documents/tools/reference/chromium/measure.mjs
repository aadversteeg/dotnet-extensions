// Prints every HTML case in ./cases with Chromium (Playwright, version pinned in package.json), lists the position of
// every word on every page with poppler's pdftotext -bbox and every filled rectangle with PyMuPDF, both run in the
// reference tools image (../tools.Dockerfile, every input pinned; built here with the tag the other checks use).
//
//   node measure.mjs [case-name ...]                 every step, on a machine with Chromium and Docker
//   node measure.mjs --step print [case-name ...]    only Chromium's print: <case>.pdf
//   node measure.mjs --step boxes [case-name ...]    only Chromium's box sizes, at the page area's width that
//                                                    extract.sh read from the print: <case>.boxes.txt
//   node measure.mjs --step words [case-name ...]    only reading back what extract.sh wrote: <case>.words.txt
//   node measure.mjs --cases documents [name ...]    the HTML files of another folder than ./cases (a path relative to
//                                                    this folder), such as the whole documents in ./documents
//
// measure.ps1 runs the print, boxes and words steps in the official Playwright container of the pinned version and
// extract.sh in the tools image between the print and the others, so the host needs only Docker.
//
// Output, per case, in ./out: <case>.pdf (Chromium's print), <case>.words.txt (one line per word:
// "page xMin yMin xMax yMax word", points, origin top left, as pdftotext reports them), <case>.boxes.txt (width and
// height in points of every table cell, every element with an id and every grid item, laid out with print media in a
// window as wide as the print's page area, without page breaks: README.md, "Box sizes"), <case>.area (extract.sh:
// the width of each page's page area, from the print), <case>.lines.txt (one line per filled rectangle, such as a piece of a
// grid line: "page x0 y0 x1 y1 height", points, origin top left), <case>.print.txt (the print's pages, words with their
// baselines, filled rectangles and images, read from the PDF by positions.py: README.md, "Print positions"), for a print
// with a decoration line cut around ink <case>.ink.txt (ink.py: the line, its gaps and its visible pieces) and versions.txt.
// The engine's tests pin numbers measured this way; see README.md for which test uses which case.
import { execFileSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { mkdirSync, readdirSync, readFileSync, writeFileSync } from 'node:fs';
import { basename, join, resolve } from 'node:path';
import { pathToFileURL } from 'node:url';

// The reference is Chromium with font hinting off: the engine lays text out with the font's unhinted advances, which is
// what a PDF holds; hinted advances are a screen-rendering property of one platform (see README.md, "The reference").
const chromiumArgs = ['--font-render-hinting=none'];

const here = resolve(import.meta.dirname);
const out = join(here, 'out');

let args = process.argv.slice(2);
const casesAt = args.indexOf('--cases');
const cases = join(here, casesAt >= 0 ? args[casesAt + 1] : 'cases');
if (casesAt >= 0) {
  args = args.filter((_, index) => index !== casesAt && index !== casesAt + 1);
}
const stepAt = args.indexOf('--step');
const step = stepAt >= 0 ? args[stepAt + 1] : 'all';
if (!['all', 'print', 'boxes', 'words'].includes(step)) {
  throw new Error(`unknown step '${step}': use print, boxes or words, or no --step for every step`);
}
const wanted = stepAt >= 0 ? args.filter((_, index) => index !== stepAt && index !== stepAt + 1) : args;
const names = readdirSync(cases)
  .filter((file) => file.endsWith('.html'))
  .map((file) => basename(file, '.html'))
  .filter((name) => wanted.length === 0 || wanted.includes(name))
  .sort();
if (names.length === 0) {
  throw new Error(`no case found in ${cases} for: ${wanted.join(' ')}`);
}

mkdirSync(out, { recursive: true });
if (step === 'all' || step === 'print') {
  await print();
}
if (step === 'all') {
  extract();
}
if (step === 'all' || step === 'boxes') {
  await boxes();
}
if (step === 'all' || step === 'words') {
  words();
}

/**
 * Chromium's print of every case; to out/chromium-version.txt the Chromium version, its flags and the Playwright image
 * it ran in (MEASURE_IMAGE, set by measure.ps1; "host" when Chromium runs on this machine).
 */
async function print() {
  const { chromium } = await import('playwright');
  const browser = await chromium.launch({ args: chromiumArgs });
  const page = await browser.newPage();
  for (const name of names) {
    await load(page, name);
    await page.pdf({ path: join(out, `${name}.pdf`), preferCSSPageSize: true, printBackground: true });
  }

  writeFileSync(join(out, 'chromium-version.txt'),
    `chromium ${browser.version()}\nchromium flags ${chromiumArgs.join(' ')}\nplaywright image ${process.env.MEASURE_IMAGE ?? 'host'}\n`);
  await browser.close();
}

/**
 * The box sizes of every case (1 CSS px = 0.75pt): every table cell, every element with an id, and every item of a grid
 * (the fr columns), with print media, in a window as wide as the print's page area. A window lays the document out
 * without page breaks; at the page area's width a box is as wide as in the print, and so is a height that depends on
 * the width (text that wraps). The width is the print's: the page area's, as extract.sh read it from the clip around
 * each page's body (area.py), rounded up to a whole CSS pixel as Chromium lays a page out (README.md, "Box sizes").
 */
async function boxes() {
  const { chromium } = await import('playwright');
  const browser = await chromium.launch({ args: chromiumArgs });
  const page = await browser.newPage();
  for (const name of names) {
    const width = windowWidth(name);
    await page.setViewportSize({ width: width ?? 1280, height: 720 });
    await load(page, name);
    await page.emulateMedia({ media: 'print' });
    const boxes = await page.evaluate(() => [...document.querySelectorAll('td, th, [id], *')]
      .filter((element) => element.matches('td, th, [id]') || getComputedStyle(element.parentElement ?? element).display === 'grid')
      .map((element) => {
      const box = element.getBoundingClientRect();
      // A line feed in the text (preserved by white-space: pre) would end the file's line: it becomes a space.
      const label = element.id || (element.textContent ?? '').trim().slice(0, 24).replaceAll('\n', ' ');
      return `${element.tagName.toLowerCase()} ${(box.width * 0.75).toFixed(3)} ${(box.height * 0.75).toFixed(3)} ${label}`;
      }));
    if (width === null && boxes.length > 0) {
      throw new Error(`${name}: the print draws nothing on any page, so it has no page area to measure its ${boxes.length} boxes at`);
    }
    writeFileSync(join(out, `${name}.boxes.txt`), boxes.join('\n') + '\n');
    await page.emulateMedia({ media: null });
  }

  await browser.close();
}

/**
 * The window width for the boxes of a case, in CSS px: the width of its pages' page area (out/<case>.area, one line
 * "page width" in points per page, "page none" for a page that draws nothing in its body and so opens no clip) rounded
 * up to a whole pixel. The clip's box is up to 0.012 pt wider than the page area (a 300 pt page area, case pm-len-body,
 * is clipped 300.012 pt wide), so 0.05 px is taken off before rounding up. A print whose pages have page areas of
 * different widths has no one width: an error. A print that draws nothing in any page's body has none: null, and then
 * it must have no box to measure.
 */
function windowWidth(name) {
  const widths = readFileSync(join(out, `${name}.area`), 'utf8').split('\n').filter((line) => line.length > 0)
    .map((line) => line.split(' ')[1]).filter((width) => width !== 'none').map(Number);
  if (widths.some((width) => Math.abs(width - widths[0]) > 0.001)) {
    throw new Error(`${name}: the page areas of the print are not one width (${widths.join(', ')}); the boxes have no window to be measured in`);
  }
  return widths.length === 0 ? null : Math.ceil(widths[0] / 0.75 - 0.05);
}

/** Opens a case and waits for its fonts, so layout uses them. */
async function load(page, name) {
  await page.goto(pathToFileURL(join(cases, `${name}.html`)).href);
  // Chromium prints with a fallback font, without a word, when a font file of the case does not load (a copy of this
  // folder without the family's assets/fonts beside it did that): document.fonts.ready settles for a failed face too.
  // Layout first, so every face the text needs has been asked for, then stop on any face that failed.
  const failed = await page.evaluate(async () => {
    void document.body.offsetHeight;
    await document.fonts.ready;
    return [...document.fonts].filter((face) => face.status !== 'loaded' && face.status !== 'unloaded')
      .map((face) => `${face.family} ${face.weight} ${face.style}: ${face.status}`);
  });
  if (failed.length > 0) {
    throw new Error(`${name}: a font of the case did not load (${failed.join('; ')}); is assets/fonts beside this folder?`);
  }
}

/** extract.sh in the reference tools image, with ./out at /out and this folder at /src. */
function extract() {
  const dockerfile = readFileSync(join(here, '..', 'tools.Dockerfile'));
  const tools = `ave-extensions-documents-tools:${createHash('sha256').update(dockerfile).digest('hex').slice(0, 12)}`;
  execFileSync('docker', ['build', '-q', '-t', tools, '-'], { input: dockerfile, stdio: ['pipe', 'inherit', 'inherit'] });
  // As the current user on Linux and macOS, so the files in ./out are not root-owned (Docker Desktop on Windows maps
  // mounted folders itself).
  const user = process.platform === 'win32' ? [] : ['--user', `${process.getuid()}:${process.getgid()}`];
  execFileSync('docker', ['run', '--rm', ...user, '-v', `${out}:/out`, '-v', `${here}:/src:ro`, tools, 'bash', '/src/extract.sh', ...names],
    { stdio: 'inherit' });
}

/** Every word from pdftotext's XHTML (page number, box and text), and versions.txt. */
function words() {
  for (const name of names) {
    const html = readFileSync(join(out, `${name}.bbox.html`), 'utf8');
    const lines = [];
    html.split('<page ').slice(1).forEach((pageText, index) => {
      for (const match of pageText.matchAll(/<word xMin="([\d.]+)" yMin="([\d.]+)" xMax="([\d.]+)" yMax="([\d.]+)">([^<]*)<\/word>/g)) {
        lines.push(`${index + 1} ${match[1]} ${match[2]} ${match[3]} ${match[4]} ${match[5]}`);
      }
    });
    writeFileSync(join(out, `${name}.words.txt`), lines.join('\n') + '\n');
    console.log(`${name}: ${lines.length} words on ${html.split('<page ').length - 1} pages`);
  }

  const versions = [readFileSync(join(out, 'chromium-version.txt'), 'utf8').trim(), readFileSync(join(out, 'tools-versions.txt'), 'utf8').trim()];
  writeFileSync(join(out, 'versions.txt'), versions.join('\n') + '\n');
  console.log(versions.join('\n'));
}
