# Vendored test suites

Public test suites the HTML and CSS readers are checked against, each in its own folder with its upstream licence,
unchanged from the commit named here. Only the data files the tests read are copied. `.gitattributes` keeps them byte
for byte (some hold carriage returns and NULs that are test data). The tests that read them, and the cases they skip by
a stated rule, are named per suite; the counts are asserted by the tests themselves.

| Folder | Source | Commit | Licence | Read by |
| --- | --- | --- | --- | --- |
| `html5lib-tests/tokenizer/` | https://github.com/html5lib/html5lib-tests, `tokenizer/` | `c5495c539ca8a79c3c5107f5f14010bcff9a35d5` (2026-11-05) | MIT (`html5lib-tests/LICENSE`) | `UnitTests.Extensions.Documents.Html.Parsing/TokenizerSuiteTests.cs` |
| `css-parsing-tests/` | https://github.com/SimonSapin/css-parsing-tests, every JSON file | `203ce36bffd617db7f118c551e32794561fb273d` (2025-09-24) | CC0 1.0 (`css-parsing-tests/LICENSE`) | `UnitTests.Extensions.Documents.Css/CssParsingSuiteTests.cs`, `AnPlusBSuiteTests.cs`, `ColorSuiteTests.cs` |
| `wpt/css/cases.json` | https://github.com/web-platform-tests/wpt, the literal calls of test_valid_value, test_invalid_value, test_valid_selector, test_invalid_selector and test_computed_value in `css/css-syntax`, `css/selectors`, `css/css-cascade`, `css/css-variables`, `css/css-values`, extracted by `tools/extract-wpt-css-cases.py` (with the markup of the files test_computed_value is called in) | `93494ff9dfc26c59245941548bb7da799e8baaef` (2026-11-18) | BSD-3-Clause (`wpt/LICENSE.md`) | `UnitTests.Extensions.Documents.Css/WptCssSuiteTests.cs`; Chromium's answers to the same cases are measured in `tools/reference/chromium/computed-style` |
| `wpt/html/syntax/parsing/resources/` | https://github.com/web-platform-tests/wpt, the tree-construction `.dat` files (moved there from html5lib-tests in June 2026) | `93494ff9dfc26c59245941548bb7da799e8baaef` (2026-11-18) | BSD-3-Clause (`wpt/LICENSE.md`) | `UnitTests.Extensions.Documents.Html.Parsing/TreeConstructionSuiteTests.cs` |

To update a suite: copy the same files from a newer commit (for `wpt/css/cases.json`, run the extraction script on a
newer checkout and measure Chromium's answers again), change the commit here and in `NOTICE.md`, and adjust the counts
the tests assert after reading the diff of the data.
