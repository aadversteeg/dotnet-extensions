# Chromium HTML-tree reference

The HTML parser (`Ave.Extensions.Documents.Html.Parsing`) follows Chromium 159 where Chromium differs from the HTML
Standard, or keeps the standard as a listed limit (`docs/architecture.md`, known limits of stage 3). This folder
measures the trees Chromium builds; the tests pin them. Nothing here is part of the engine or its build, and the tests
do not run Chromium.

## Run it

With Docker only:

```
pwsh -NoProfile -File measure.ps1
```

`measure.ps1` runs `measure.mjs` in the same pinned Playwright container as `../computed-style/measure.ps1`
(Playwright 1.65.1, Chromium 159.0.8154.6, by digest), from the folder above so that it uses that folder's
`package.json`. The output goes to `out/` (ignored by git); `measured/` holds the last run. After a deliberate
re-measurement, read the diff and copy `out/*` over `measured/`.

## What is measured

- `tree-construction.json`: every tree-construction test of `assets/test-suites/wpt/html/syntax/parsing/resources`
  without `#script-on` (1 959), a document through `DOMParser` (scripting off, as in the parser) and a fragment through
  `innerHTML` on its context element, written in the suite's text form (`HtmlTreeText`). Only the cases whose tree
  differs from the suite's `#document` are kept (17). `TreeConstructionSuiteTests` expects Chromium's tree there, except
  in the five fragment cases it lists as a limit.
- `cases.json`: Chromium's tree of every case of `cases.tsv`, the parser's differences from the standard that the
  suite does not cover (review 39, HD-1 .. HD-12). `ChromiumTreeTests` expects Chromium's tree, except for the cases
  named `limit-…`, which it pins as still different.
