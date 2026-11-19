// Prints every HTML case in ./cases with Chromium (Playwright, version pinned in package.json), lists the position of
// every word on every page with poppler's pdftotext -bbox and every filled rectangle with PyMuPDF, both run in the
// reference tools image (../tools.Dockerfile, every input pinned; built here with the tag the other checks use).
//
//   node measure.mjs [case-name ...]                 every step, on a machine with Chromium and Docker
//   node measure.mjs --step print [case-name ...]    only Chromium: <case>.pdf, <case>.boxes.txt
//   node measure.mjs --step words [case-name ...]    only reading back what extract.sh wrote: <case>.words.txt
//
// measure.ps1 runs the print and words steps in the official Playwright container of the pinned version and
// extract.sh in the tools image between them, so the host needs only Docker.
//
// Output, per case, in ./out: <case>.pdf (Chromium's print), <case>.words.txt (one line per word:
// "page xMin yMin xMax yMax word", points, origin top left, as pdftotext reports them), <case>.boxes.txt (width and
// height in points of every table cell, every element with an id and every grid item, laid out with print media;
// widths of a box do not depend on pagination), <case>.lines.txt (one line per filled rectangle, such as a piece of a
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
const cases = join(here, 'cases');
const out = join(here, 'out');

const args = process.argv.slice(2);
const stepAt = args.indexOf('--step');
const step = stepAt >= 0 ? args[stepAt + 1] : 'all';
if (!['all', 'print', 'words'].includes(step)) {
  throw new Error(`unknown step '${step}': use print or words, or no --step for every step`);
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
if (step === 'all' || step === 'words') {
  words();
}

/**
 * Chromium's print of every case and the box sizes; to out/chromium-version.txt the Chromium version, its flags and the
 * Playwright image it ran in (MEASURE_IMAGE, set by measure.ps1; "host" when Chromium runs on this machine).
 */
async function print() {
  const { chromium } = await import('playwright');
  const browser = await chromium.launch({ args: chromiumArgs });
  const page = await browser.newPage();
  for (const name of names) {
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
    await page.pdf({ path: join(out, `${name}.pdf`), preferCSSPageSize: true, printBackground: true });
    // Box sizes as laid out for print (1 CSS px = 0.75pt): every table cell, every element with an id, and every item
    // of a grid (the fr columns).
    await page.emulateMedia({ media: 'print' });
    const boxes = await page.evaluate(() => [...document.querySelectorAll('td, th, [id], *')]
      .filter((element) => element.matches('td, th, [id]') || getComputedStyle(element.parentElement ?? element).display === 'grid')
      .map((element) => {
      const box = element.getBoundingClientRect();
      // A line feed in the text (preserved by white-space: pre) would end the file's line: it becomes a space.
      const label = element.id || (element.textContent ?? '').trim().slice(0, 24).replaceAll('\n', ' ');
      return `${element.tagName.toLowerCase()} ${(box.width * 0.75).toFixed(3)} ${(box.height * 0.75).toFixed(3)} ${label}`;
      }));
    writeFileSync(join(out, `${name}.boxes.txt`), boxes.join('\n') + '\n');
    await page.emulateMedia({ media: null });
  }

  writeFileSync(join(out, 'chromium-version.txt'),
    `chromium ${browser.version()}\nchromium flags ${chromiumArgs.join(' ')}\nplaywright image ${process.env.MEASURE_IMAGE ?? 'host'}\n`);
  await browser.close();
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
