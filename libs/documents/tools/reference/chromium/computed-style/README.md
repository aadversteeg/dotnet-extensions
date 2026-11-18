# Chromium computed-style reference

The HTML/CSS front-end (`Ave.Extensions.Documents.Css`, `Ave.Extensions.Documents.Html`) is compared with Chromium's own
reading of the same CSS: which values and selectors it takes, and the computed value of every registered property on
every element of a corpus of documents. This folder measures those answers; the tests pin them. Nothing here is part
of the engine or its build, and the tests do not run Chromium.

## Run it

With Docker only:

```
pwsh -NoProfile -File measure.ps1             # both steps
pwsh -NoProfile -File measure.ps1 -Step wpt    # only the web-platform-tests verdicts
pwsh -NoProfile -File measure.ps1 -Step corpus # only the corpus
```

`measure.ps1` runs `measure.mjs` in the same pinned Playwright container as `../measure.ps1` (Playwright 1.65.1,
Chromium 159.0.8154.6, `mcr.microsoft.com/playwright:v1.65.1-noble` by digest), from the folder above so that it uses
that folder's `package.json`. The output goes to `out/` (ignored by git); `measured/` holds the last run. After a
deliberate re-measurement, read the diff and copy `out/*` over `measured/`.

## What is measured

- `wpt` → `wpt-css-verdicts.json`: for every case of `assets/test-suites/wpt/css/cases.json` (extracted from
  web-platform-tests by `tools/extract-wpt-css-cases.py`), whether Chromium takes it: `e.style[property] = value` on a
  new element sets the property, `document.querySelector(selector)` does not throw. These are the first checks of the
  WPT helpers (`css/support/parsing-testcommon.js`). For the test_computed_value cases, the value
  `getComputedStyle(#target)` gives after `#target.style[property] = specified`, in the test file's markup without its
  scripts. The tests compare the engine with Chromium's answer, not with the test's expectation: WPT holds tests of
  drafts Chromium 159 does not implement (`random-item()`, `{}`-wrapped `var()` arguments, `:heading`), and the engine
  follows Chromium. Where the two disagree, the tests count it.
- `corpus` → `<case>.txt`: for every element of every case in `cases/`, one line per property of `properties.txt`
  (every longhand of the property registry): `/html[1]/body[1]/p[2] margin-top 16px`. Print media, a viewport of
  794 × 1123 CSS px (A4).

## Why the corpus is measured unrendered

`getComputedStyle` returns the *resolved value* (CSSOM §9): for most properties the computed value, but for some
(`width`, `height`, `margin-*`, `padding-*`, `line-height`, and others) the *used* value when the element is rendered,
which is layout's result, not the cascade's. The engine's stage 3 ends at computed values, so the corpus is measured
with the document not rendered: `measure.mjs` adds `html { display: none !important }`, and Chromium then returns
computed values (`width: 50%` stays `50%`). The root's own `display` is therefore not measured (it is the `none` the
harness set); every other property of the root is.

One property still comes back as a used value unrendered: `line-height` with a number (`line-height: 1.5` gives
`24px` at a 16px font, where the computed value is the number). The comparison takes this into account per property;
see the test that reads the corpus.
