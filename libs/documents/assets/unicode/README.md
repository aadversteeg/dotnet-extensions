# Unicode Character Database files

`18.0.0/` holds the files of the Unicode Character Database, version 18.0.0, that the engine needs, unchanged
from https://www.unicode.org/Public/18.0.0/ (downloaded 2026-11-15): `ucd/LineBreak.txt`,
`ucd/EastAsianWidth.txt`, `ucd/extracted/DerivedGeneralCategory.txt`, `ucd/emoji/emoji-data.txt`, `ucd/Scripts.txt` and the
conformance test `ucd/auxiliary/LineBreakTest.txt`; and, for normalization before shaping (downloaded 2026-11-18),
`ucd/UnicodeData.txt` (SHA-256 `0736451de439ae7baf1425136617da495e09ee5afbe6e394374db7009ea08950`) and
`ucd/DerivedNormalizationProps.txt` (SHA-256 `98ac7f67d985fe781e317f6182e885e94cabb0c314769e6dd73e48b226931ccd`).
Licence: Unicode License v3 (`18.0.0/LICENSE.txt`).

`Ave.Extensions.Documents.Typesetting/Unicode/UnicodePropertyRanges.g.cs` and `UnicodeNormalizationData.g.cs` are
generated from them by `UnitTests.Extensions.Documents.Typesetting/UnicodeDataGenerator.cs`; a test fails when a table
and the files differ. After
replacing these files with a newer version, change `UnicodeDataGenerator.Version`, move the folder, and run the
tests with `UPDATE_GENERATED=1`.
