// Measures what the HTML/CSS front-end is compared with, in Chromium (Playwright, version pinned in ../package.json):
//
//   node computed-style/measure.mjs --step corpus [case ...]   getComputedStyle of every registry property on every
//                                                              element of every case in ./cases → out/<case>.txt
//   node computed-style/measure.mjs --step wpt                 Chromium's verdict on every extracted web-platform-tests
//                                                              case (assets/test-suites/wpt/css/cases.json) →
//                                                              out/wpt-css-verdicts.json
//
// Run from ../ (where node_modules is); measure.ps1 runs it in the pinned Playwright container. See README.md.
import { mkdirSync, readdirSync, readFileSync, writeFileSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { chromium } from 'playwright';

const here = resolve(import.meta.dirname);
const family = resolve(here, '../../../..');
const out = join(here, 'out');
mkdirSync(out, { recursive: true });

const args = process.argv.slice(2);
const stepAt = args.indexOf('--step');
const step = stepAt >= 0 ? args[stepAt + 1] : 'all';
if (!['all', 'corpus', 'wpt'].includes(step)) {
  throw new Error(`unknown step '${step}': use corpus or wpt, or no --step for both`);
}
const wanted = stepAt >= 0 ? args.filter((_, index) => index !== stepAt && index !== stepAt + 1) : args;

const browser = await chromium.launch({ args: ['--font-render-hinting=none'] });
const version = browser.version();
const image = process.env.MEASURE_IMAGE ?? '(not in the container)';

// The page media: print on A4 (210 mm × 297 mm = 793.7 × 1122.5 CSS px; the viewport is whole pixels).
async function printPage() {
  const page = await browser.newPage({ viewport: { width: 794, height: 1123 } });
  await page.emulateMedia({ media: 'print' });
  return page;
}

if (step === 'all' || step === 'corpus') {
  const properties = readFileSync(join(here, 'properties.txt'), 'utf8').split('\n').map((line) => line.trim()).filter((line) => line && !line.startsWith('#'));
  const names = readdirSync(join(here, 'cases')).filter((file) => file.endsWith('.html')).map((file) => file.slice(0, -5))
    .filter((name) => wanted.length === 0 || wanted.includes(name)).sort();
  for (const name of names) {
    // A new page per case: setContent keeps the Document, and with it state such as a body's link colour.
    const page = await printPage();
    const html = readFileSync(join(here, 'cases', `${name}.html`), 'utf8');
    // Unrendered: with the root not rendered, getComputedStyle returns computed values, not used ones (CSSOM §9,
    // "resolved value"); the root's own display is then the none set here, so it is not measured (README.md).
    await page.setContent(html, { waitUntil: 'load' });
    await page.addStyleTag({ content: 'html { display: none !important }' });
    const lines = await page.evaluate((properties) => {
      const result = [];
      const path = (element) => {
        const parts = [];
        for (let node = element; node && node.nodeType === 1; node = node.parentElement) {
          const index = node.parentElement ? Array.from(node.parentElement.children).indexOf(node) + 1 : 1;
          parts.unshift(`${node.localName}[${index}]`);
        }
        return '/' + parts.join('/');
      };
      for (const element of document.querySelectorAll('*')) {
        if (element.localName === 'style' && element.textContent.includes('display: none !important')) continue;
        const style = getComputedStyle(element);
        for (const property of properties) {
          if (element === document.documentElement && property === 'display') continue;
          result.push(`${path(element)} ${property} ${style.getPropertyValue(property)}`);
        }
      }
      return result;
    }, properties);
    writeFileSync(join(out, `${name}.txt`), lines.join('\n') + '\n');
    console.log(`${name}: ${lines.length} values`);
    await page.close();
  }
}

if (step === 'all' || step === 'wpt') {
  const suite = JSON.parse(readFileSync(join(family, 'assets/test-suites/wpt/css/cases.json'), 'utf8'));
  const page = await printPage();
  await page.setContent('<!DOCTYPE html><body></body>');
  const values = suite.cases.filter((item) => item.helper !== 'test_computed_value');
  // What the helpers of css/support/parsing-testcommon.js check first: whether e.style[property] = value sets the
  // property, and whether querySelector accepts the selector.
  const verdicts = await page.evaluate((values) => values.map((item) => {
    if (item.helper.endsWith('_selector')) {
      try { document.querySelector(item.args[0]); return true; } catch { return false; }
    }
    // A new element for every case, so no earlier case's declarations can remain.
    const div = document.createElement('div');
    document.body.append(div);
    div.style[item.args[0]] = item.args[1];
    const set = div.style.getPropertyValue(item.args[0]) !== '';
    div.remove();
    return set;
  }), values);
  // test_computed_value: the test file's markup without its scripts, #target given the specified value, read back.
  const computed = [];
  for (const item of suite.cases.filter((entry) => entry.helper === 'test_computed_value')) {
    const markup = suite.markup[item.file].replace(/<script[\s\S]*?<\/script>/gi, '');
    const screen = await browser.newPage();
    await screen.setContent(markup, { waitUntil: 'load' });
    computed.push(await screen.evaluate(([property, value]) => {
      const target = document.getElementById('target');
      if (!target) return null;
      target.style[property] = '';
      target.style[property] = value;
      return getComputedStyle(target).getPropertyValue(property);
    }, item.args));
    await screen.close();
  }
  const result = {
    chromium: version, image, wpt: suite.commit,
    values: values.map((item, index) => ({ at: `${item.file}:${item.line}`, helper: item.helper, args: item.args, chromium: verdicts[index] })),
    computed: suite.cases.filter((entry) => entry.helper === 'test_computed_value').map((item, index) => ({ at: `${item.file}:${item.line}`, args: item.args, chromium: computed[index] })),
  };
  writeFileSync(join(out, 'wpt-css-verdicts.json'), JSON.stringify(result, null, 1) + '\n');
  console.log(`wpt: ${values.length} validity verdicts, ${computed.length} computed values`);
  await page.close();
}

writeFileSync(join(out, 'versions.txt'), `chromium ${version}\nflags --font-render-hinting=none\nimage ${image}\n`);
await browser.close();
