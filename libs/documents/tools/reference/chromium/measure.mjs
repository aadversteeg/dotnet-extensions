// Prints every HTML case in ./cases with Chromium (Playwright, version pinned in package.json), lists the position of
// every word on every page with poppler's pdftotext -bbox and every filled rectangle with PyMuPDF, both run in the
// reference tools image (../tools.Dockerfile, every input pinned; built here with the tag the other checks use).
//
//   npm ci && npx playwright install chromium && node measure.mjs [case-name ...]
//
// Output, per case, in ./out: <case>.pdf (Chromium's print), <case>.words.txt (one line per word:
// "page xMin yMin xMax yMax word", points, origin top left, as pdftotext reports them), <case>.boxes.txt (width and
// height in points of every table cell, every element with an id and every grid item, laid out with print media;
// widths of a box do not depend on pagination), <case>.lines.txt (one line per filled rectangle, such as a piece of a
// grid line: "page x0 y0 x1 y1 height", points, origin top left) and versions.txt.
// The engine's tests pin numbers measured this way; see README.md for which test uses which case.
import { chromium } from 'playwright';
import { execFileSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { mkdirSync, readdirSync, readFileSync, writeFileSync } from 'node:fs';
import { basename, join, resolve } from 'node:path';
import { pathToFileURL } from 'node:url';

const here = resolve(import.meta.dirname);
const cases = join(here, 'cases');
const out = join(here, 'out');
const dockerfile = readFileSync(join(here, '..', 'tools.Dockerfile'));
const tools = `ave-extensions-documents-tools:${createHash('sha256').update(dockerfile).digest('hex').slice(0, 12)}`;
execFileSync('docker', ['build', '-q', '-t', tools, '-'], { input: dockerfile, stdio: ['pipe', 'inherit', 'inherit'] });

const wanted = process.argv.slice(2);
const names = readdirSync(cases)
  .filter((file) => file.endsWith('.html'))
  .map((file) => basename(file, '.html'))
  .filter((name) => wanted.length === 0 || wanted.includes(name))
  .sort();
if (names.length === 0) {
  throw new Error(`no case found in ${cases} for: ${wanted.join(' ')}`);
}

mkdirSync(out, { recursive: true });
const browser = await chromium.launch();
const page = await browser.newPage();
for (const name of names) {
  await page.goto(pathToFileURL(join(cases, `${name}.html`)).href);
  await page.evaluate(() => document.fonts.ready);
  await page.pdf({ path: join(out, `${name}.pdf`), preferCSSPageSize: true, printBackground: true });
  // Box sizes as laid out for print (1 CSS px = 0.75pt): every table cell, every element with an id, and every item
  // of a grid (the fr columns).
  await page.emulateMedia({ media: 'print' });
  const boxes = await page.evaluate(() => [...document.querySelectorAll('td, th, [id], *')]
    .filter((element) => element.matches('td, th, [id]') || getComputedStyle(element.parentElement ?? element).display === 'grid')
    .map((element) => {
    const box = element.getBoundingClientRect();
    const label = element.id || (element.textContent ?? '').trim().slice(0, 24);
    return `${element.tagName.toLowerCase()} ${(box.width * 0.75).toFixed(3)} ${(box.height * 0.75).toFixed(3)} ${label}`;
    }));
  writeFileSync(join(out, `${name}.boxes.txt`), boxes.join('\n') + '\n');
  await page.emulateMedia({ media: null });
}

const versions = [`chromium ${browser.version()}`];
await browser.close();

// pdftotext -bbox writes an XHTML file with <page> and <word> elements; keep page number, box and text.
// rectangles.py lists every filled rectangle with PyMuPDF. Both run in the tools image, with this folder at /src.
const script = ['set -eu -o pipefail', 'pdftotext -v 2>&1 | head -1', 'python3 -c "import pymupdf; print(\'PyMuPDF\', pymupdf.VersionBind)"']
  .concat(names.flatMap((name) => [
    `pdftotext -bbox /out/${name}.pdf /out/${name}.bbox.html`,
    `python3 /src/rectangles.py /out/${name}.pdf > /out/${name}.lines.txt`,
  ]))
  .join(' && ');
const report = execFileSync('docker', ['run', '--rm', '-v', `${out}:/out`, '-v', `${here}:/src:ro`, tools, 'bash', '-c', script], { encoding: 'utf8' });
versions.push(report.trim());
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

writeFileSync(join(out, 'versions.txt'), versions.join('\n') + '\n');
console.log(versions.join('\n'));
