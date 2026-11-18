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
`24px` at a 16px font, where the computed value is the number).

Each case is measured in a new page: Playwright's `setContent` keeps the Document, and with it state such as the link
colour a body's `link` attribute sets, which then leaks into the next case.

The images of the cases are `data:` URLs, so they load: for an image that does not load, Chromium draws the
alternative text and drops the image's width, height and align attributes (they are presentational hints of a
replaced element only).

## The cases

| Case | Covers |
| --- | --- |
| `ua-bare` | the user-agent style sheet: every v0 element, no author style |
| `properties` | every supported value of the box, colour, text and white-space properties, shorthands, units, math functions |
| `fonts-tables-lists` | font sizes (keywords, relative sizes, monospace), families, weights, line heights, the font shorthand, tables, lists, breaks |
| `selectors` | every selector form of the v0 set, and the ones that never match |
| `cascade` | origins and importance, the style attribute, specificity and order, layers, revert and revert-layer, nesting, custom properties, inheritance and defaulting, calc(), @media, @supports, media attributes |
| `hints` | the presentational attributes of the v0 elements |
| `elements` | every element of the HTML Standard's element index, in its usual place: the ones outside v0 are refused |
| `hints-gaps` | presentational attributes with values the other cases do not try (invalid, negative, empty, relative), from review 39 |
| `ua-extra` | elements the user-agent sheet has rules for that `ua-bare` does not contain, from review 39 |

## How the tests compare

`UnitTests.Extensions.Documents.Html/ChromiumCorpusTests.cs` compares every measured line with the engine's computed
value, written as getComputedStyle writes it (`ComputedText.cs`, the one place for these rules): lengths in px with six
significant digits (in exponent form below 1e-4 and from 1e6 on, as C's %g); `currentcolor` as the element's colour; `min-width` and `min-height` `auto` as `0px`; a number
`line-height` as number × font size (the used value above); family names quoted as Chromium quotes them. Nothing else
is changed. Each value is equal, or refused (the engine refuses its element or one around it, at a start tag or an
attribute), never different; the number of values and of each outcome is pinned per case, and so is the number of
refusals of the default mode. Where Chromium computes a value of its own
(`text-align: -webkit-center` from `align=center`), the engine computes the same value.

`WptComputedValueTests.cs` compares the test_computed_value cases with the `computed` part of `wpt-css-verdicts.json`;
`UnitTests.Extensions.Documents.Css/WptCssSuiteTests.cs` the value and selector cases with its `values` part.
