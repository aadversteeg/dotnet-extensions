// Measures how Chromium (Playwright, version pinned in ../package.json) builds the trees of the tree-construction tests
// the parser is compared with (assets/test-suites/wpt/html/syntax/parsing/resources/*.dat): every test without
// #script-on, a document through DOMParser (scripting off, as in the parser) and a fragment through innerHTML on its
// context element, written in the suite's own text form. The cases whose Chromium tree differs from the suite's
// #document go to out/tree-construction.json; the tests compare the parser with Chromium there. The own cases of
// cases.tsv (beyond the suite) go to out/cases.json, every one.
//
// Run from ../ (where node_modules is); measure.ps1 runs it in the pinned Playwright container. See README.md.
import { mkdirSync, readdirSync, readFileSync, writeFileSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { chromium } from 'playwright';

const here = resolve(import.meta.dirname);
const family = resolve(here, '../../../..');
const suite = join(family, 'assets', 'test-suites', 'wpt', 'html', 'syntax', 'parsing', 'resources');
const out = join(here, 'out');
mkdirSync(out, { recursive: true });

// The tests of one file, read as UnitTests.Extensions.Documents.Html.Parsing/TreeConstructionSuiteTests.cs reads them.
function read(file) {
  const lines = readFileSync(join(suite, file), 'utf8').split('\n');
  const starts = (index) => (lines[index] === '#data' || lines[index] === '#description') && (index === 0 || lines[index - 1].length === 0);
  const tests = [];
  let index = 0;
  while (index < lines.length) {
    if (!starts(index)) { index++; continue; }
    const start = index + 1;
    const sections = { [lines[index]]: [] };
    let current = lines[index];
    index++;
    while (index < lines.length && !starts(index)) {
      const line = lines[index];
      if (current !== '#document' && ['#data', '#errors', '#new-errors', '#document-fragment', '#script-on', '#script-off', '#document'].includes(line)) {
        current = line;
        sections[current] = [];
      } else {
        sections[current].push(line);
      }
      index++;
    }
    const document = sections['#document'] ?? [];
    while (document.length > 0 && document[document.length - 1].length === 0) document.pop();
    tests.push({
      id: `${file}:${start}`,
      data: sections['#data'].join('\n'),
      fragment: sections['#document-fragment']?.[0] ?? null,
      scriptOn: '#script-on' in sections,
      expected: document.length === 0 ? '' : document.join('\n') + '\n',
    });
  }
  return tests;
}

// The suite's text form of the children of a node (HtmlTreeText.cs), in the page.
function trees(tests) {
  const SVG = 'http://www.w3.org/2000/svg', MATH = 'http://www.w3.org/1998/Math/MathML', HTML = 'http://www.w3.org/1999/xhtml';
  const XLINK = 'http://www.w3.org/1999/xlink', XML = 'http://www.w3.org/XML/1998/namespace', XMLNS = 'http://www.w3.org/2000/xmlns/';
  const attributeName = (a) => (a.namespaceURI === XLINK ? 'xlink ' : a.namespaceURI === XML ? 'xml ' : a.namespaceURI === XMLNS ? 'xmlns ' : '') + a.localName;
  const write = (lines, root) => {
    const stack = Array.from(root.childNodes).reverse().map((node) => [node, 0]);
    while (stack.length > 0) {
      const [node, depth] = stack.pop();
      const indent = '| ' + ' '.repeat(depth * 2);
      if (typeof node === 'string') { lines.push(indent + node + '\n'); continue; }
      switch (node.nodeType) {
        case 1: {
          const prefix = node.namespaceURI === SVG ? 'svg ' : node.namespaceURI === MATH ? 'math ' : node.namespaceURI === HTML ? '' : '{' + node.namespaceURI + '} ';
          lines.push(indent + '<' + prefix + node.localName + '>\n');
          const attributes = Array.from(node.attributes).map((a) => [attributeName(a), a.value]).sort((x, y) => (x[0] < y[0] ? -1 : x[0] > y[0] ? 1 : 0));
          for (const [name, value] of attributes) lines.push(indent + '  ' + name + '="' + value + '"\n');
          const children = Array.from(node.childNodes);
          for (let i = children.length - 1; i >= 0; i--) stack.push([children[i], depth + 1]);
          if (node.namespaceURI === HTML && node.localName === 'template') {
            const content = Array.from(node.content.childNodes);
            for (let i = content.length - 1; i >= 0; i--) stack.push([content[i], depth + 2]);
            stack.push(['content', depth + 1]);
          }
          break;
        }
        case 3: lines.push(indent + '"' + node.data + '"\n'); break;
        case 7: lines.push(indent + '<?' + node.target + ' ' + node.data + '?>\n'); break;
        case 8: lines.push(indent + '<!-- ' + node.data + ' -->\n'); break;
        case 10:
          lines.push(indent + '<!DOCTYPE ' + node.name + (node.publicId.length > 0 || node.systemId.length > 0 ? ' "' + node.publicId + '" "' + node.systemId + '"' : '') + '>\n');
          break;
        default: lines.push(indent + '#node ' + node.nodeType + '\n');
      }
    }
  };
  const parser = new DOMParser();
  const standards = parser.parseFromString('<!DOCTYPE html>', 'text/html');
  return tests.map((test) => {
    const lines = [];
    if (test.fragment === null) {
      write(lines, parser.parseFromString(test.data, 'text/html'));
    } else {
      const parts = test.fragment.split(' ');
      const context = parts.length === 2
        ? standards.createElementNS(parts[0] === 'svg' ? SVG : MATH, parts[1])
        : standards.createElement(parts[0]);
      context.innerHTML = test.data;
      write(lines, parts.length === 1 && parts[0] === 'template' ? context.content : context);
    }
    return lines.join('');
  });
}

const browser = await chromium.launch();
const page = await browser.newPage();
const tests = readdirSync(suite).filter((file) => file.endsWith('.dat')).sort().flatMap(read).filter((test) => !test.scriptOn);
const chromiumTrees = await page.evaluate(trees, tests);
const differ = tests.flatMap((test, i) => (chromiumTrees[i] === test.expected ? [] : [{ case: test.id, fragment: test.fragment, tree: chromiumTrees[i] }]));

// The own cases: id, fragment context, input with \uXXXX and \\ escapes.
const unescape = (text) => text.replace(/\\(\\|u([0-9A-Fa-f]{4}))/g, (_, all, hex) => (all === '\\' ? '\\' : String.fromCharCode(parseInt(hex, 16))));
const own = readFileSync(join(here, 'cases.tsv'), 'utf8').split('\n').filter((line) => line.length > 0 && !line.startsWith('#')).map((line) => {
  const [id, fragment, input] = line.split('\t');
  return { id, fragment: fragment.length > 0 ? fragment : null, data: unescape(input) };
});
const ownTrees = await page.evaluate(trees, own);
writeFileSync(join(out, 'cases.json'), JSON.stringify({
  chromium: browser.version(),
  image: process.env.MEASURE_IMAGE ?? '(not in the container)',
  cases: own.map((item, i) => ({ id: item.id, tree: ownTrees[i] })),
}, null, 1) + '\n');
const result = {
  chromium: browser.version(),
  image: process.env.MEASURE_IMAGE ?? '(not in the container)',
  measured: tests.length,
  differ,
};
writeFileSync(join(out, 'tree-construction.json'), JSON.stringify(result, null, 1) + '\n');
console.log(`${tests.length} cases measured, ${differ.length} differ from the suite; ${own.length} own cases`);
await browser.close();
