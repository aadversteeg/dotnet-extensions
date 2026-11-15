// Prints every HTML case in ./cases with Chromium (Playwright, version pinned in package.json) and lists the position
// of every word on every page with poppler's pdftotext -bbox, run in a Docker image pinned by digest.
//
//   npm ci && npx playwright install chromium && node measure.mjs [case-name ...]
//
// Output, per case, in ./out: <case>.pdf (Chromium's print), <case>.words.txt (one line per word:
// "page xMin yMin xMax yMax word", points, origin top left, as pdftotext reports them), <case>.boxes.txt (width and
// height in points of every table cell and every element with an id, laid out with print media; widths of a box do
// not depend on pagination) and versions.txt.
// The engine's tests pin numbers measured this way; see README.md for which test uses which case.
import { chromium } from 'playwright';
import { execFileSync } from 'node:child_process';
import { mkdirSync, readdirSync, readFileSync, writeFileSync } from 'node:fs';
import { basename, join, resolve } from 'node:path';
import { pathToFileURL } from 'node:url';

const here = resolve(import.meta.dirname);
const cases = join(here, 'cases');
const out = join(here, 'out');
const poppler = 'alpine@sha256:49340b346798875b5592e5d420241f2f9cfba5f47e62ebb1446a1b34734e898f';

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
  // Box sizes as laid out for print (1 CSS px = 0.75pt): every table cell and every element with an id.
  await page.emulateMedia({ media: 'print' });
  const boxes = await page.evaluate(() => [...document.querySelectorAll('td, th, [id]')].map((element) => {
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
const script = 'apk add -q --no-cache poppler-utils >/dev/null && pdftotext -v 2>&1 | head -1 && '
  + names.map((name) => `pdftotext -bbox /out/${name}.pdf /out/${name}.bbox.html`).join(' && ');
const report = execFileSync('docker', ['run', '--rm', '-v', `${out}:/out`, poppler, 'sh', '-c', script], { encoding: 'utf8' });
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
