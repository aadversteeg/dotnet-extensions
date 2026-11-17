# Unicode Character Database files

`18.0.0/` holds the files of the Unicode Character Database, version 18.0.0, that the engine needs, unchanged
from https://www.unicode.org/Public/18.0.0/ (downloaded 2026-11-15): `ucd/LineBreak.txt`,
`ucd/EastAsianWidth.txt`, `ucd/extracted/DerivedGeneralCategory.txt`, `ucd/emoji/emoji-data.txt`, `ucd/Scripts.txt` and the
conformance test `ucd/auxiliary/LineBreakTest.txt`. Licence: Unicode License v3 (`18.0.0/LICENSE.txt`).

`src/Ave.Documents.Typesetting/Unicode/UnicodePropertyRanges.g.cs` is generated from them by
`src/UnitTests.Ave.Documents.Typesetting/UnicodeDataGenerator.cs`; a test fails when the two differ. After
replacing these files with a newer version, change `UnicodeDataGenerator.Version`, move the folder, and run the
tests with `UPDATE_GENERATED=1`.
