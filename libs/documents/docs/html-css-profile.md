# HTML and CSS profile (v0)

The tables are generated from the code by `UnitTests.Extensions.Documents.Html/ProfileDocumentTests.cs`; do not edit.
The prose around them and the table of presentational attributes are written by hand in that test and in
`Html/PresentationalHints.cs` (`Documented`); the corpus checks the attributes against Chromium. What the HTML
front-end supports, what it ignores by rule (no effect in a static paged document) and what it refuses. Valid input
that is not listed as supported or ignored is refused: a diagnostic of class Refusal with its line and column
(`HtmlStyleDiagnostics.ClassOf`), which fails the document unless the caller asked for warnings only. The
reference is Chromium 159; see `docs/architecture.md`, "HTML/CSS front-end".

## Properties

Longhands with every value the engine supports. A value CSS defines that is not listed is refused. So is a value that
does not match the engine's grammar of a known property: a typing error such as `color: 12px`, or valid CSS beyond this
profile such as `vertical-align: -webkit-baseline-middle`. The engine cannot tell the two apart, and a browser drops the first and uses the
second, so both get the one answer "not valid or not supported in this profile". The same holds for a selector or media
query that does not parse and an unknown at-rule. Dropped with a warning, as in a browser, is only what CSS Syntax 3
recovers from (an unclosed block, a declaration without a colon), a top-level `!` other than `!important`, a rule in a
place CSS does not allow, another browser engine's property (`-moz-`, `-ms-`, `-o-`) and a `var()` that is cyclic or
names nothing.

| Property | Inherited | Initial | Supported values | Defined in |
| --- | --- | --- | --- | --- |
| `background-color` | no | `rgba(0, 0, 0, 0)` | `<color>` | CSS Backgrounds 3 §2.1 |
| `border-bottom-color` | no | `currentcolor` | `<color>` | CSS Backgrounds 3 §3.1 |
| `border-bottom-style` | no | `none` | `none \| hidden \| solid \| inset \| outset` | CSS Backgrounds 3 §3.2 |
| `border-bottom-width` | no | `2.25pt` | `<length [0,∞]> \| thin \| medium \| thick` | CSS Backgrounds 3 §3.3 |
| `border-collapse` | yes | `separate` | `separate \| collapse` | CSS 2.1 §17.6 |
| `border-left-color` | no | `currentcolor` | `<color>` | CSS Backgrounds 3 §3.1 |
| `border-left-style` | no | `none` | `none \| hidden \| solid \| inset \| outset` | CSS Backgrounds 3 §3.2 |
| `border-left-width` | no | `2.25pt` | `<length [0,∞]> \| thin \| medium \| thick` | CSS Backgrounds 3 §3.3 |
| `border-right-color` | no | `currentcolor` | `<color>` | CSS Backgrounds 3 §3.1 |
| `border-right-style` | no | `none` | `none \| hidden \| solid \| inset \| outset` | CSS Backgrounds 3 §3.2 |
| `border-right-width` | no | `2.25pt` | `<length [0,∞]> \| thin \| medium \| thick` | CSS Backgrounds 3 §3.3 |
| `border-spacing` | yes | `0pt 0pt` | `<length [0,∞]>{1,2}` | CSS 2.1 §17.6.1 |
| `border-top-color` | no | `currentcolor` | `<color>` | CSS Backgrounds 3 §3.1 |
| `border-top-style` | no | `none` | `none \| hidden \| solid \| inset \| outset` | CSS Backgrounds 3 §3.2 |
| `border-top-width` | no | `2.25pt` | `<length [0,∞]> \| thin \| medium \| thick` | CSS Backgrounds 3 §3.3 |
| `box-decoration-break` | no | `slice` | `slice \| clone` | CSS Fragmentation 3 §5.4 |
| `box-sizing` | no | `content-box` | `content-box \| border-box` | CSS Box Model 3 §4.3 |
| `break-after` | no | `auto` | `auto \| avoid \| avoid-page \| page` | CSS Fragmentation 3 §3.1 |
| `break-before` | no | `auto` | `auto \| avoid \| avoid-page \| page` | CSS Fragmentation 3 §3.1 |
| `break-inside` | no | `auto` | `auto \| avoid \| avoid-page` | CSS Fragmentation 3 §3.2 |
| `caption-side` | yes | `top` | `top \| bottom` | CSS 2.1 §17.4.1 |
| `color` | yes | `rgb(0, 0, 0)` | `<color>` | CSS Color 4 §3.1; the initial value is black (HTML Standard §15.3.4) |
| `color-scheme` | yes | `normal` | `normal \| light` | CSS Color Adjust 1 §2.1 |
| `counter-increment` | no | `none` | `none \| [<custom-ident> <integer>?]+ (a name other than list-item, within 32 bits, at most 16 names)` | CSS Lists 3 §4.3 |
| `counter-reset` | no | `none` | `none \| [<custom-ident> <integer>?]+ (a name other than list-item, within 32 bits, at most 16 names)` | CSS Lists 3 §4.2 |
| `counter-set` | no | `none` | `none \| [<custom-ident> <integer>?]+ (a name other than list-item, within 32 bits, at most 16 names)` | CSS Lists 3 §4.4 |
| `display` | no | `inline` | `block \| inline \| inline-block \| list-item \| none \| table \| table-row-group \| table-header-group \| table-footer-group \| table-row \| table-cell \| table-column-group \| table-column \| table-caption` | CSS Display 3 §2 |
| `empty-cells` | yes | `show` | `show \| hide` | CSS 2.1 §17.6.1.1 |
| `font-family` | yes | `"Times New Roman"` | `[ <family-name> \| <generic-family> ]#` | CSS Fonts 4 §2.1; the initial value is the user agent's (Chromium: Times New Roman) |
| `font-feature-settings` | yes | `normal` | `normal \| <feature-tag-value># (the tags "lnum", "onum", "pnum", "tnum" and "ordn", each with 0, 1, on or off)` | CSS Fonts 4 §7.1 |
| `font-kerning` | yes | `auto` | `auto \| normal` | CSS Fonts 4 §6.3; normal prints as auto (Chromium 159 kerns with both, measured) |
| `font-size` | yes | `12pt` | `<absolute-size> \| <relative-size> \| <length-percentage [0,∞]>` | CSS Fonts 4 §2.5 |
| `font-style` | yes | `normal` | `normal \| italic` | CSS Fonts 4 §2.4 |
| `font-synthesis-small-caps` | yes | `auto` | `auto \| none` | CSS Fonts 4 §7.3 (the engine never synthesises: a missing face is an error) |
| `font-synthesis-style` | yes | `auto` | `auto \| none` | CSS Fonts 4 §7.3 (the engine never synthesises: under none the nearest face is taken plainly, under auto a missing face is an error) |
| `font-synthesis-weight` | yes | `auto` | `auto \| none` | CSS Fonts 4 §7.3 (the engine never synthesises: under none the nearest face is taken plainly, under auto a missing face is an error) |
| `font-variant-numeric` | yes | `normal` | `normal \| [ lining-nums \| oldstyle-nums ] \|\| [ proportional-nums \| tabular-nums ] \|\| ordinal` | CSS Fonts 4 §6.7 |
| `font-weight` | yes | `400` | `normal \| bold \| bolder \| lighter \| <number [1,1000]>` | CSS Fonts 4 §2.2 |
| `height` | no | `auto` | `auto \| <length-percentage [0,∞]>` | CSS Sizing 3 §3.1 |
| `letter-spacing` | yes | `normal` | `normal \| <length>` | CSS Text 3 §7.2 |
| `line-height` | yes | `normal` | `normal \| <number [0,∞]> \| <length-percentage [0,∞]>` | CSS Inline 3 §5.1; CSS 2.1 §10.8.1 |
| `list-style-position` | yes | `outside` | `outside` | CSS Lists 3 §3.5 |
| `list-style-type` | yes | `disc` | `disc \| circle \| square \| decimal \| decimal-leading-zero \| lower-roman \| upper-roman \| lower-alpha \| upper-alpha \| lower-latin \| upper-latin \| none` | CSS Lists 3 §3.3; CSS Counter Styles 3 §6 |
| `margin-bottom` | no | `0pt` | `<length-percentage [0,∞]> \| auto` | CSS Box Model 3 §5.1 |
| `margin-left` | no | `0pt` | `<length-percentage [0,∞]> \| auto` | CSS Box Model 3 §5.1 |
| `margin-right` | no | `0pt` | `<length-percentage [0,∞]> \| auto` | CSS Box Model 3 §5.1 |
| `margin-top` | no | `0pt` | `<length-percentage [0,∞]> \| auto` | CSS Box Model 3 §5.1 |
| `max-height` | no | `none` | `none \| <length-percentage [0,∞]>` | CSS Sizing 3 §3.2 |
| `max-width` | no | `none` | `none \| <length-percentage [0,∞]>` | CSS Sizing 3 §3.2 |
| `min-height` | no | `auto` | `auto \| <length-percentage [0,∞]>` | CSS Sizing 3 §3.2 |
| `min-width` | no | `auto` | `auto \| <length-percentage [0,∞]>` | CSS Sizing 3 §3.2 |
| `orphans` | yes | `2` | `<integer [1,32767]>` | CSS Fragmentation 3 §3.3 |
| `outline-color` | no | `currentcolor` | `<color> (nothing is drawn: outline-style is only none; Chromium 159 does not take auto)` | CSS UI 4 §3.4 |
| `outline-offset` | no | `0pt` | `<length> (nothing is drawn: outline-style is only none)` | CSS UI 4 §3.5 |
| `outline-width` | no | `2.25pt` | `<line-width> (nothing is drawn: outline-style is only none)` | CSS UI 4 §3.2 |
| `overflow-x` | no | `visible` | `visible \| hidden \| clip` | CSS Overflow 3 §3.1 |
| `overflow-y` | no | `visible` | `visible \| hidden \| clip` | CSS Overflow 3 §3.1 |
| `padding-bottom` | no | `0pt` | `<length-percentage [0,∞]>` | CSS Box Model 3 §5.2 |
| `padding-left` | no | `0pt` | `<length-percentage [0,∞]>` | CSS Box Model 3 §5.2 |
| `padding-right` | no | `0pt` | `<length-percentage [0,∞]>` | CSS Box Model 3 §5.2 |
| `padding-top` | no | `0pt` | `<length-percentage [0,∞]>` | CSS Box Model 3 §5.2 |
| `print-color-adjust` | yes | `economy` | `economy \| exact` | CSS Color Adjust 1 §3.1 |
| `table-layout` | no | `auto` | `auto \| fixed` | CSS 2.1 §17.5.2 |
| `text-align` | yes | `start` | `start \| end \| left \| right \| center \| justify \| -webkit-left \| -webkit-right \| -webkit-center` | CSS Text 3 §6.1 |
| `text-decoration-color` | no | `currentcolor` | `<color>` | CSS Text Decoration 3 §2.3 |
| `text-decoration-line` | no | `none` | `none \| [ underline \|\| overline \|\| line-through ]` | CSS Text Decoration 3 §2.1 |
| `text-decoration-skip-ink` | yes | `auto` | `auto \| none` | CSS Text Decoration 4 §2.10.4 |
| `text-decoration-style` | no | `solid` | `solid` | CSS Text Decoration 3 §2.2 |
| `text-transform` | yes | `none` | `none \| capitalize \| uppercase \| lowercase` | CSS Text 3 §2.1 |
| `text-wrap-mode` | yes | `wrap` | `wrap \| nowrap` | CSS Text 4 §6.1 |
| `unicode-bidi` | no | `normal` | `normal \| isolate` | CSS Writing Modes 4 §2.2; isolate has no effect in left-to-right text |
| `vertical-align` | no | `baseline` | `baseline \| sub \| super \| top \| middle \| bottom \| <length-percentage>` | CSS 2.1 §10.8.1; CSS Inline 3 §4 |
| `white-space-collapse` | yes | `collapse` | `collapse \| preserve \| preserve-breaks \| break-spaces` | CSS Text 4 §3.1 |
| `widows` | yes | `2` | `<integer [1,32767]>` | CSS Fragmentation 3 §3.3 |
| `width` | no | `auto` | `auto \| <length-percentage [0,∞]>` | CSS Sizing 3 §3.1 |
| `word-spacing` | yes | `0pt` | `normal \| <length>` | CSS Text 4 §7.1 |

`overflow-x` and `overflow-y`: `hidden` on one axis while the other stays `visible` is refused, since Chromium
computes the visible axis to auto then (Known limits); set both, as `overflow: hidden` does. `clip` does not scroll,
so `clip` beside `visible` is accepted and kept, as in Chromium.

`border-style: inset` and `outset` on a block box: the top and left sides are drawn darker and the bottom and right
sides lighter for `inset`, the other way round for `outset`, in two colours taken from the border colour as Chromium
takes them (by the colour's luminance: a dark colour gets two lighter shades, a light one its darker shade and
itself, any other a darker and a lighter shade; `currentcolor` gives the shades of #eeeeee whatever the text colour);
where two sides of different colours meet, the corner is split on the line from its outer to its inner point (cases
`bs-*`, `hr-*`). Refused: on a table or a part of one, whose lines are drawn solid, and inset beside outset sides on
one box. Solid sides of more than one colour on a block box are split at the corners in the same way (cases
`bs-solid-*`); a solid border of one colour is drawn as four rectangles. `hr` is drawn with its default style and
its `color`, `noshade`, `size`, `width` and `align` attributes:
`overflow: hidden` is accepted on a block box with no children and a border, padding or height that is not 0 at its
top or bottom, where it clips nothing.

`width` on a table part: a table is drawn at `auto`, a length or a percentage of its container (its border box; never
narrower than its content needs, and refused where wider than its container); a cell (`td`, `th`) takes a length, of
its content box, which its column takes: the widest in the automatic layout, the first row's in the fixed layout,
where a `col` length wins. A `col`, a cell and a `colgroup` without `col` elements (for each column of its span) take a
length or a percentage. In the automatic layout a percentage is of the width the columns share, and a table of width
`auto` grows until its columns have their percentages and the others their widest content; in the fixed layout a
percentage on a `col` when every other column is a percentage or has no width and, beside a column without a width,
the percentages add up to less than 100. Refused: in the automatic layout percentages of 100 or more beside other
columns, a percentage beside another width in one column, a percentage on a cell over several columns and a cell over
several columns beside a percent column; in the fixed layout a percentage on a cell, a width on a cell over several
columns and a cell width beside percent columns; a width on a `colgroup` with `col` elements. In the fixed layout a
column without a width is never narrower than its content, where Chromium lets the content overflow (Known limits). A
`tfoot` is drawn after the body rows, once; Chromium repeats a small one on every page of a table that breaks (Known
limits).

`border-collapse`: both models are drawn. A table with separated borders (`separate`, the initial value) keeps its
border, padding and background, each cell paints its own border, and `border-spacing` lies between the cells and
around them, in whole CSS pixels (another spacing is refused). Such a table breaks over pages as Chromium prints it,
sliced: between rows and inside rows, the cells of a cut row at the top of each part whatever their alignment, with a
repeated header. It is refused at the table's line where it breaks with a cell over several rows across the break,
with content sliced in a cell, or with `box-decoration-break: clone`. A border on a row or a row group of such a table
is refused; put borders on the cells. In both models the background of a row, a row group, a column or a column group
is drawn on the cells of it that have none of their own (the row's first, then the group's, the column's, the column
group's), as Chromium prints it; a table's background is drawn in both models, under its border box.

`vertical-align: baseline` on a table cell, the initial value, is drawn as `top`, as Chromium prints it: each cell's
first line at its own content top, whatever the font sizes and paddings in the row (cases `tv-baseline-sizes`,
`tv-baseline-height`). A browser window aligns the cells' first baselines instead.

`width`, `min-width`, `max-width` and `box-sizing` on a block box: a length or a percentage of the containing block's
width, for the content box or the border box; `max-width` limits the width and `min-width` wins over both. The
horizontal margins that are `auto` share the width the box leaves: both centre it, a left one alone puts it at the
right end; a box as wide as its container or wider starts at its left margin and passes the container's right edge,
as in Chromium. Laid out as Chromium lays them out: a length cut toward zero to 1/64 px, a percentage and the left
of two auto margins floored to it (cases `sz-*`). Refused: a calculated or intrinsic size (`calc()` with a
percentage, `fit-content`), a minimum or maximum width on a table part or an image, and a width, minimum or maximum
on a box inside a table cell or a page-margin box (`Documents.Input.Unsupported.Size`: their columns are sized by
their content's widths, and a sized box's share in them is not built).

`height`, `min-height` and `max-height` on a block box: a length, cut toward zero to 1/64 px, for the content box or
the border box; `max-height` limits the height and `min-height` wins over both. A percentage height is of the
parent's height where that is a length in a content box, floored to 1/64 px, and `auto` where the parent's height
depends on its content. Content taller than the box overflows it and is drawn, and what follows the box starts at its
end. Across a page break the box takes the rest of the page and the next page shows what the pages before did not
take, also a minimum height, as in Chromium (cases `sz-height*`, `sz-min-*`, `sz-max-height`). Refused: a percentage
height elsewhere (on the root, of a parent's percentage, border-box, minimum or maximum height), a percentage minimum
or maximum, a height on a table part, and content that goes on past a page break after the box's height ended on
the page (`Documents.Layout.OverflowAcrossPages`: Chromium prints the rest of it on the next page while what follows
the box stays on the first, cases `sz-height-overflow-page`, `sz-height-forced`).

### Supported at the initial value only

What the layout always does: a declaration that sets one of these to its initial value is accepted, so resets and
the user-agent style sheet are not refused. So is a value in the Accepted column: aspect-ratio auto with a ratio,
which the width and height attributes of img make; an image with a natural ratio uses its own, as in Chromium, and
on any other element stage 4 applies the ratio or refuses it. A zero length where the initial value is a keyword or
a percentage is the same value and accepted too: `background-position-x: 0` and `background-position-y: 0`
(computed 0px, as Chromium keeps it). Any other value is refused.

| Property | Initial | Accepted | Defined in |
| --- | --- | --- | --- |
| `aspect-ratio` | `auto` | `auto \| auto && <ratio>` | CSS Sizing 4 §2.1 |
| `backdrop-filter` | `none` | `none` | Filter Effects 2 §3 |
| `background-attachment` | `scroll` | `scroll` | CSS Backgrounds 3 §2.4 |
| `background-clip` | `border-box` | `border-box` | CSS Backgrounds 3 §2.8 |
| `background-image` | `none` | `none` | CSS Backgrounds 3 §2.2 |
| `background-origin` | `padding-box` | `padding-box` | CSS Backgrounds 3 §2.7 |
| `background-position-x` | `0%` | `0%` | CSS Backgrounds 4 §2.6 |
| `background-position-y` | `0%` | `0%` | CSS Backgrounds 4 §2.6 |
| `background-repeat` | `repeat` | `repeat` | CSS Backgrounds 3 §2.3 |
| `background-size` | `auto` | `auto` | CSS Backgrounds 3 §2.9 |
| `border-bottom-left-radius` | `0pt` | `0` | CSS Backgrounds 3 §5.1 |
| `border-bottom-right-radius` | `0pt` | `0` | CSS Backgrounds 3 §5.1 |
| `border-top-left-radius` | `0pt` | `0` | CSS Backgrounds 3 §5.1 |
| `border-top-right-radius` | `0pt` | `0` | CSS Backgrounds 3 §5.1 |
| `bottom` | `auto` | `auto` | CSS Positioned Layout 3 §3.1 |
| `box-shadow` | `none` | `none` | CSS Backgrounds 3 §6.1 |
| `clear` | `none` | `none` | CSS 2.1 §9.5.2 |
| `clip-path` | `none` | `none` | CSS Masking 1 §5.1 |
| `column-count` | `auto` | `auto` | CSS Multi-column 1 §3.2 |
| `column-gap` | `normal` | `normal` | CSS Box Alignment 3 §8.1 |
| `column-width` | `auto` | `auto` | CSS Multi-column 1 §3.1 |
| `contain` | `none` | `none` | CSS Containment 2 §2 |
| `content` | `normal` | `normal` | CSS Generated Content 3 §1 |
| `content-visibility` | `visible` | `visible` | CSS Containment 2 §4 |
| `direction` | `ltr` | `ltr` | CSS Writing Modes 4 §2.1 |
| `filter` | `none` | `none` | Filter Effects 1 §5 |
| `float` | `none` | `none` | CSS 2.1 §9.5.1 |
| `font-language-override` | `normal` | `normal` | CSS Fonts 4 §7.4 |
| `font-optical-sizing` | `auto` | `auto` | CSS Fonts 4 §2.6 |
| `font-size-adjust` | `none` | `none` | CSS Fonts 5 §2.1 |
| `font-stretch` | `100%` | `normal \| 100%` | CSS Fonts 4 §2.3 |
| `font-variant-alternates` | `normal` | `normal` | CSS Fonts 4 §6.8 |
| `font-variant-caps` | `normal` | `normal` | CSS Fonts 4 §6.6 |
| `font-variant-east-asian` | `normal` | `normal` | CSS Fonts 4 §6.10 |
| `font-variant-emoji` | `normal` | `normal` | CSS Fonts 4 §6.11 |
| `font-variant-ligatures` | `normal` | `normal` | CSS Fonts 4 §6.4 |
| `font-variant-position` | `normal` | `normal` | CSS Fonts 4 §6.5 |
| `font-variation-settings` | `normal` | `normal` | CSS Fonts 4 §7.2 |
| `hyphens` | `manual` | `manual` | CSS Text 3 §5.4 |
| `image-rendering` | `auto` | `auto` | CSS Images 3 §5.3 |
| `isolation` | `auto` | `auto` | Compositing 1 §3.3 |
| `left` | `auto` | `auto` | CSS Positioned Layout 3 §3.1 |
| `line-break` | `auto` | `auto` | CSS Text 3 §5.3 |
| `list-style-image` | `none` | `none` | CSS Lists 3 §3.4 |
| `mix-blend-mode` | `normal` | `normal` | Compositing 1 §3.2 |
| `object-fit` | `fill` | `fill` | CSS Images 3 §5.5 |
| `opacity` | `1` | `1` | CSS Color 4 §11.2 |
| `outline-style` | `none` | `none` | CSS UI 4 §3.3 |
| `overflow-wrap` | `normal` | `normal` | CSS Text 3 §5.5 |
| `page` | `auto` | `auto` | CSS Paged Media 3 §6 |
| `position` | `static` | `static` | CSS Positioned Layout 3 §2.1 |
| `quotes` | `auto` | `auto` | CSS Generated Content 3 §3 |
| `right` | `auto` | `auto` | CSS Positioned Layout 3 §3.1 |
| `row-gap` | `normal` | `normal` | CSS Box Alignment 3 §8.1 |
| `tab-size` | `8` | `8` | CSS Text 3 §4.2 |
| `text-align-last` | `auto` | `auto` | CSS Text 3 §6.2 |
| `text-decoration-thickness` | `auto` | `auto` | CSS Text Decoration 4 §2.4 |
| `text-indent` | `0pt` | `0` | CSS Text 3 §8.1 |
| `text-justify` | `auto` | `auto` | CSS Text 3 §6.4 |
| `text-overflow` | `clip` | `clip` | CSS Overflow 3 §5.1 |
| `text-rendering` | `auto` | `auto` | SVG 2 §11.9 |
| `text-shadow` | `none` | `none` | CSS Text Decoration 3 §4 |
| `text-underline-offset` | `auto` | `auto` | CSS Text Decoration 4 §2.8 |
| `text-underline-position` | `auto` | `auto` | CSS Text Decoration 3 §2.5 |
| `text-wrap-style` | `auto` | `auto` | CSS Text 4 §6.2 |
| `top` | `auto` | `auto` | CSS Positioned Layout 3 §3.1 |
| `transform` | `none` | `none` | CSS Transforms 1 §5 |
| `visibility` | `visible` | `visible` | CSS Display 3 §4 |
| `word-break` | `normal` | `normal` | CSS Text 3 §5.2 |
| `writing-mode` | `horizontal-tb` | `horizontal-tb` | CSS Writing Modes 4 §3.1 |
| `z-index` | `auto` | `auto` | CSS 2.1 §9.9.1 |

### Shorthands

| Shorthand | Sets | Supported | Defined in |
| --- | --- | --- | --- |
| `background` | background-color, background-image, background-repeat, background-attachment, background-position-x, background-position-y, background-size, background-origin, background-clip | `<color> \| none (one layer, no image)` | CSS Backgrounds 3 §2.10 |
| `border` | border-top-width, border-top-style, border-top-color, border-right-width, border-right-style, border-right-color, border-bottom-width, border-bottom-style, border-bottom-color, border-left-width, border-left-style, border-left-color | `<line-width> \|\| <line-style> \|\| <color>` | CSS Backgrounds 3 §3.4 |
| `border-bottom` | border-bottom-width, border-bottom-style, border-bottom-color | `<line-width> \|\| <line-style> \|\| <color>` | CSS Backgrounds 3 §3.4 |
| `border-color` | border-top-color, border-right-color, border-bottom-color, border-left-color | `<'border-top-color'>{1,4}` | CSS Backgrounds 3 §3.1 |
| `border-left` | border-left-width, border-left-style, border-left-color | `<line-width> \|\| <line-style> \|\| <color>` | CSS Backgrounds 3 §3.4 |
| `border-radius` | border-top-left-radius, border-top-right-radius, border-bottom-right-radius, border-bottom-left-radius | `<'border-top-left-radius'>{1,4}` | CSS Backgrounds 3 §5.1 |
| `border-right` | border-right-width, border-right-style, border-right-color | `<line-width> \|\| <line-style> \|\| <color>` | CSS Backgrounds 3 §3.4 |
| `border-style` | border-top-style, border-right-style, border-bottom-style, border-left-style | `<'border-top-style'>{1,4}` | CSS Backgrounds 3 §3.2 |
| `border-top` | border-top-width, border-top-style, border-top-color | `<line-width> \|\| <line-style> \|\| <color>` | CSS Backgrounds 3 §3.4 |
| `border-width` | border-top-width, border-right-width, border-bottom-width, border-left-width | `<'border-top-width'>{1,4}` | CSS Backgrounds 3 §3.3 |
| `columns` | column-width, column-count | `auto` | CSS Multi-column 1 §3.3 |
| `font` | font-style, font-variant-caps, font-weight, font-stretch, font-size, line-height, font-family, font-variant-ligatures, font-variant-numeric, font-variant-east-asian, font-variant-position, font-variant-alternates, font-variant-emoji, font-size-adjust, font-kerning, font-optical-sizing, font-feature-settings, font-variation-settings, font-language-override | `[ normal \| italic \|\| <font-weight> ]? <font-size> [ / <line-height> ]? <font-family>#` | CSS Fonts 4 §2.8 |
| `font-synthesis` | font-synthesis-weight, font-synthesis-style, font-synthesis-small-caps | `none \| [ weight \|\| style \|\| small-caps ]` | CSS Fonts 4 §7.3 |
| `font-variant` | font-variant-ligatures, font-variant-caps, font-variant-alternates, font-variant-numeric, font-variant-east-asian, font-variant-position, font-variant-emoji | `normal` | CSS Fonts 4 §6.12 |
| `gap` | row-gap, column-gap | `normal` | CSS Box Alignment 3 §8.3 |
| `inset` | top, right, bottom, left | `<'top'>{1,4}` | CSS Positioned Layout 3 §3.1 |
| `list-style` | list-style-position, list-style-image, list-style-type | `<list-style-position> \|\| none \|\| <list-style-type>` | CSS Lists 3 §3.6 |
| `margin` | margin-top, margin-right, margin-bottom, margin-left | `<'margin-top'>{1,4}` | CSS Box Model 3 §5.1 |
| `margin-block` | margin-top, margin-bottom | `<'margin-top'>{1,2}` | CSS Logical 1 §4 |
| `margin-block-end` | margin-bottom | `<length-percentage [0,∞]> \| auto` | CSS Logical 1 §4.2 |
| `margin-block-start` | margin-top | `<length-percentage [0,∞]> \| auto` | CSS Logical 1 §4.2 |
| `margin-inline` | margin-left, margin-right | `<'margin-left'>{1,2}` | CSS Logical 1 §4 |
| `margin-inline-end` | margin-right | `<length-percentage [0,∞]> \| auto` | CSS Logical 1 §4.2 |
| `margin-inline-start` | margin-left | `<length-percentage [0,∞]> \| auto` | CSS Logical 1 §4.2 |
| `outline` | outline-width, outline-style, outline-color | `<line-width> \|\| none \|\| <color>` | CSS UI 4 §3.1 |
| `overflow` | overflow-x, overflow-y | `[ visible \| hidden \| clip ]{1,2}` | CSS Overflow 3 §3.1 |
| `padding` | padding-top, padding-right, padding-bottom, padding-left | `<'padding-top'>{1,4}` | CSS Box Model 3 §5.2 |
| `padding-block` | padding-top, padding-bottom | `<'padding-top'>{1,2}` | CSS Logical 1 §4 |
| `padding-block-end` | padding-bottom | `<length-percentage [0,∞]>` | CSS Logical 1 §4.3 |
| `padding-block-start` | padding-top | `<length-percentage [0,∞]>` | CSS Logical 1 §4.3 |
| `padding-inline` | padding-left, padding-right | `<'padding-left'>{1,2}` | CSS Logical 1 §4 |
| `padding-inline-end` | padding-right | `<length-percentage [0,∞]>` | CSS Logical 1 §4.3 |
| `padding-inline-start` | padding-left | `<length-percentage [0,∞]>` | CSS Logical 1 §4.3 |
| `page-break-after` | break-after | `auto \| always \| avoid` | CSS Fragmentation 3 §3.4 (legacy) |
| `page-break-before` | break-before | `auto \| always \| avoid` | CSS Fragmentation 3 §3.4 (legacy) |
| `page-break-inside` | break-inside | `auto \| avoid` | CSS Fragmentation 3 §3.4 (legacy) |
| `text-decoration` | text-decoration-line, text-decoration-style, text-decoration-color, text-decoration-thickness | `<text-decoration-line> \|\| <text-decoration-style> \|\| <color>` | CSS Text Decoration 3 §2.4 |
| `text-wrap` | text-wrap-mode, text-wrap-style | `<text-wrap-mode> \|\| <text-wrap-style>` | CSS Text 4 §6.3 |
| `white-space` | white-space-collapse, text-wrap-mode | `normal \| pre \| nowrap \| pre-wrap \| <white-space-collapse> \|\| <text-wrap-mode>` | CSS Text 4 §3 |
| `word-wrap` | overflow-wrap | `normal` | CSS Text 3 §5.5 (legacy name) |

`all` takes only a CSS-wide keyword (initial, inherit, unset, revert, revert-layer).

### Ignored by rule

Each was checked against Chromium 159's print rendering; the values that change it are refused instead.

| Property | Why | Refused instead |
| --- | --- | --- |
| `-moz-osx-font-smoothing` | screen rendering hints; they do not change what is drawn on a page |  |
| `-moz-text-size-adjust` | screen rendering hints; they do not change what is drawn on a page |  |
| `-ms-text-size-adjust` | screen rendering hints; they do not change what is drawn on a page |  |
| `-webkit-appearance` | interaction with a pointer or keyboard; a static document has none |  |
| `-webkit-font-smoothing` | screen rendering hints; they do not change what is drawn on a page |  |
| `-webkit-tap-highlight-color` | interaction with a pointer or keyboard; a static document has none |  |
| `-webkit-text-size-adjust` | screen rendering hints; they do not change what is drawn on a page |  |
| `-webkit-touch-callout` | interaction with a pointer or keyboard; a static document has none |  |
| `-webkit-user-drag` | interaction with a pointer or keyboard; a static document has none |  |
| `-webkit-user-modify` | interaction with a pointer or keyboard; a static document has none | any value but read-only: Chromium 159 wraps and keeps the text as it does editable text (measured: read-write-plaintext-only makes white-space-collapse preserve) |
| `-webkit-user-select` | interaction with a pointer or keyboard; a static document has none |  |
| `accent-color` | interaction with a pointer or keyboard; a static document has none |  |
| `animation` | transitions and the parts of an animation: without a change of state or an animation name they change nothing on the printed page | a value that names an animation: Chromium 159 prints the animated state |
| `animation-composition` | transitions and the parts of an animation: without a change of state or an animation name they change nothing on the printed page |  |
| `animation-delay` | transitions and the parts of an animation: without a change of state or an animation name they change nothing on the printed page |  |
| `animation-direction` | transitions and the parts of an animation: without a change of state or an animation name they change nothing on the printed page |  |
| `animation-duration` | transitions and the parts of an animation: without a change of state or an animation name they change nothing on the printed page |  |
| `animation-fill-mode` | transitions and the parts of an animation: without a change of state or an animation name they change nothing on the printed page |  |
| `animation-iteration-count` | transitions and the parts of an animation: without a change of state or an animation name they change nothing on the printed page |  |
| `animation-name` | transitions and the parts of an animation: without a change of state or an animation name they change nothing on the printed page | any value but none: Chromium 159 prints the animated state |
| `animation-play-state` | transitions and the parts of an animation: without a change of state or an animation name they change nothing on the printed page |  |
| `animation-range` | transitions and the parts of an animation: without a change of state or an animation name they change nothing on the printed page |  |
| `animation-range-end` | transitions and the parts of an animation: without a change of state or an animation name they change nothing on the printed page |  |
| `animation-range-start` | transitions and the parts of an animation: without a change of state or an animation name they change nothing on the printed page |  |
| `animation-timeline` | transitions and the parts of an animation: without a change of state or an animation name they change nothing on the printed page |  |
| `animation-timing-function` | transitions and the parts of an animation: without a change of state or an animation name they change nothing on the printed page |  |
| `appearance` | interaction with a pointer or keyboard; a static document has none |  |
| `caret` | interaction with a pointer or keyboard; a static document has none |  |
| `caret-animation` | interaction with a pointer or keyboard; a static document has none |  |
| `caret-color` | interaction with a pointer or keyboard; a static document has none |  |
| `caret-shape` | interaction with a pointer or keyboard; a static document has none |  |
| `cursor` | interaction with a pointer or keyboard; a static document has none |  |
| `field-sizing` | interaction with a pointer or keyboard; a static document has none |  |
| `font-smooth` | screen rendering hints; they do not change what is drawn on a page |  |
| `forced-color-adjust` | screen rendering hints; they do not change what is drawn on a page |  |
| `interactivity` | interaction with a pointer or keyboard; a static document has none |  |
| `overflow-anchor` | scrolling; a paged document does not scroll |  |
| `overscroll-behavior` | scrolling; a paged document does not scroll |  |
| `overscroll-behavior-block` | scrolling; a paged document does not scroll |  |
| `overscroll-behavior-inline` | scrolling; a paged document does not scroll |  |
| `overscroll-behavior-x` | scrolling; a paged document does not scroll |  |
| `overscroll-behavior-y` | scrolling; a paged document does not scroll |  |
| `pointer-events` | interaction with a pointer or keyboard; a static document has none |  |
| `resize` | interaction with a pointer or keyboard; a static document has none | any value but none: Chromium 159 prints the resize handle |
| `scroll-behavior` | scrolling; a paged document does not scroll |  |
| `scroll-margin` | scrolling; a paged document does not scroll |  |
| `scroll-margin-block` | scrolling; a paged document does not scroll |  |
| `scroll-margin-bottom` | scrolling; a paged document does not scroll |  |
| `scroll-margin-inline` | scrolling; a paged document does not scroll |  |
| `scroll-margin-left` | scrolling; a paged document does not scroll |  |
| `scroll-margin-right` | scrolling; a paged document does not scroll |  |
| `scroll-margin-top` | scrolling; a paged document does not scroll |  |
| `scroll-padding` | scrolling; a paged document does not scroll |  |
| `scroll-padding-block` | scrolling; a paged document does not scroll |  |
| `scroll-padding-bottom` | scrolling; a paged document does not scroll |  |
| `scroll-padding-inline` | scrolling; a paged document does not scroll |  |
| `scroll-padding-left` | scrolling; a paged document does not scroll |  |
| `scroll-padding-right` | scrolling; a paged document does not scroll |  |
| `scroll-padding-top` | scrolling; a paged document does not scroll |  |
| `scroll-snap-align` | scrolling; a paged document does not scroll |  |
| `scroll-snap-stop` | scrolling; a paged document does not scroll |  |
| `scroll-snap-type` | scrolling; a paged document does not scroll |  |
| `scroll-timeline` | scrolling; a paged document does not scroll |  |
| `scroll-timeline-axis` | scrolling; a paged document does not scroll |  |
| `scroll-timeline-name` | scrolling; a paged document does not scroll |  |
| `scrollbar-color` | scrolling; a paged document does not scroll |  |
| `scrollbar-gutter` | scrolling; a paged document does not scroll | any value but auto: Chromium 159 keeps the gutter's width out of the box also when printing |
| `scrollbar-width` | scrolling; a paged document does not scroll |  |
| `speak` | screen rendering hints; they do not change what is drawn on a page |  |
| `speak-as` | screen rendering hints; they do not change what is drawn on a page |  |
| `text-size-adjust` | screen rendering hints; they do not change what is drawn on a page |  |
| `touch-action` | interaction with a pointer or keyboard; a static document has none |  |
| `transition` | transitions and the parts of an animation: without a change of state or an animation name they change nothing on the printed page |  |
| `transition-behavior` | transitions and the parts of an animation: without a change of state or an animation name they change nothing on the printed page |  |
| `transition-delay` | transitions and the parts of an animation: without a change of state or an animation name they change nothing on the printed page |  |
| `transition-duration` | transitions and the parts of an animation: without a change of state or an animation name they change nothing on the printed page |  |
| `transition-property` | transitions and the parts of an animation: without a change of state or an animation name they change nothing on the printed page |  |
| `transition-timing-function` | transitions and the parts of an animation: without a change of state or an animation name they change nothing on the printed page |  |
| `user-select` | interaction with a pointer or keyboard; a static document has none |  |
| `view-timeline` | scrolling; a paged document does not scroll |  |
| `view-timeline-axis` | scrolling; a paged document does not scroll |  |
| `view-timeline-name` | scrolling; a paged document does not scroll |  |
| `view-transition-class` | transitions and the parts of an animation: without a change of state or an animation name they change nothing on the printed page |  |
| `view-transition-name` | transitions and the parts of an animation: without a change of state or an animation name they change nothing on the printed page |  |
| `will-change` | a hint for the browser's compositor | any value but auto, scroll-position and contents: Chromium 159 paints the element in a layer of its own, which changes the printed rendering |
| `zoom` | screen rendering hints; they do not change what is drawn on a page | any value but normal, 1 and 100%: Chromium 159 scales the box when printing |

## Values

- Absolute lengths: cm, in, mm, pc, pt, px, q. Relative lengths the engine resolves: ch, em, ex, rem, vh, vw (ex and ch need the caller's font metrics, else they are refused). Refused: cap, cqb, cqh, cqi, cqmax, cqmin, cqw, dvb, dvh, dvi, dvmax, dvmin, dvw, ic, lh, lvb, lvh, lvi, lvmax, lvmin, lvw, rcap, rch, rex, ric, rlh, svb, svh, svi, svmax, svmin, svw, vb, vi, vmax, vmin.
- Math functions: calc(), min(), max(), clamp(); a result that still mixes a length and a percentage after computing is refused. Refused: abs(), acos(), asin(), atan(), atan2(), calc-size(), cos(), exp(), hypot(), log(), mod(), pow(), progress(), random(), rem(), round(), sibling-count(), sibling-index(), sign(), sin(), sqrt(), tan(), and typed arithmetic (multiplying or dividing dimensions).
- Colours: the 148 named colours, transparent, currentcolor, #rgb, #rgba, #rrggbb, #rrggbbaa, rgb(), rgba(), hsl(), hsla(); system colours activetext, canvas, canvastext, linktext, visitedtext. Refused: -webkit-image-set(), color(), color-mix(), contrast-color(), device-cmyk(), hwb(), lab(), lch(), light-dark(), oklab(), oklch(), relative colour syntax.
- Custom properties and var() (CSS Variables 1), with fallbacks; missing values make the property unset, as in a browser; revert and revert-layer made by substitution roll the cascade back. Refused: a cycle of var() references (Chromium does not make every member invalid), a '!' inside a var(), env(), attr(), if().
- CSS-wide keywords initial, inherit, unset, revert and revert-layer (CSS Cascade 5 §7.3). An important revert-layer rolls back the normal declarations of its own layer, of the later layers and the unlayered ones too, so the value comes from the layers below it (§7.3.5); in the style attribute it also rolls back the attribute's normal declarations, as in Chromium 159.

## Selectors

Type, universal, ID, class, the attribute selectors with the i modifier, the four combinators, the nesting
selector & (CSS Nesting 1). Pseudo-classes:

| Pseudo-class | Support | Note |
| --- | --- | --- |
| `:-webkit-any()` | refused | Chromium's legacy :is() for compound selectors |
| `:-webkit-any-link` | supported | Chromium's legacy name of :any-link |
| `:-webkit-autofill` | never matches (ignored by rule) | Chromium's legacy name of :autofill |
| `:-webkit-drag` | never matches (ignored by rule) | user action |
| `:-webkit-full-page-media` | never matches (ignored by rule) | a media file shown as the page |
| `:-webkit-full-screen` | never matches (ignored by rule) | Chromium's legacy :fullscreen |
| `:-webkit-full-screen-ancestor` | never matches (ignored by rule) | Chromium's legacy full-screen state |
| `:active` | never matches (ignored by rule) | user action (§10.2) |
| `:active-view-transition` | never matches (ignored by rule) | a view transition runs by script |
| `:active-view-transition-type()` | never matches (ignored by rule) | a view transition runs by script |
| `:any-link` | supported | Selectors 4 §8.1: the same as :link, since nothing is visited |
| `:autofill` | never matches (ignored by rule) | the browser fills forms in; nothing is filled |
| `:buffering` | never matches (ignored by rule) | resource state (§11.4) |
| `:checked` | refused | form controls are not supported |
| `:corner-present` | never matches (ignored by rule) | Chromium's scroll bar states |
| `:current` | never matches (ignored by rule) | time-dimensional (§11.1): no media timeline |
| `:decrement` | never matches (ignored by rule) | Chromium's scroll bar states |
| `:default` | refused | form controls are not supported |
| `:defined` | refused | custom elements |
| `:dir()` | refused | Selectors 4 §7.1 |
| `:disabled` | refused | form controls are not supported |
| `:double-button` | never matches (ignored by rule) | Chromium's scroll bar states |
| `:empty` | supported | Selectors 4 §13.2 |
| `:enabled` | refused | form controls are not supported |
| `:end` | never matches (ignored by rule) | Chromium's scroll bar states |
| `:first-child` | supported | Selectors 4 §13.4.3 |
| `:first-of-type` | supported | Selectors 4 §13.5.3 |
| `:focus` | never matches (ignored by rule) | user action (§10.3) |
| `:focus-visible` | never matches (ignored by rule) | user action (§10.4) |
| `:focus-within` | never matches (ignored by rule) | user action (§10.5) |
| `:fullscreen` | never matches (ignored by rule) | display state: nothing is shown full screen |
| `:future` | never matches (ignored by rule) | time-dimensional (§11.3) |
| `:has()` | refused | Selectors 4 §4.5 |
| `:horizontal` | never matches (ignored by rule) | Chromium's scroll bar states: a page has no scroll bars |
| `:host` | refused | shadow trees |
| `:host()` | refused | shadow trees |
| `:host-context()` | refused | shadow trees |
| `:hover` | never matches (ignored by rule) | user action (Selectors 4 §10.1) |
| `:in-range` | refused | form controls are not supported |
| `:increment` | never matches (ignored by rule) | Chromium's scroll bar states |
| `:indeterminate` | refused | form controls are not supported |
| `:interest-source` | never matches (ignored by rule) | user interest (hover or focus) |
| `:interest-target` | never matches (ignored by rule) | user interest (hover or focus) |
| `:invalid` | refused | form controls are not supported |
| `:is()` | supported | Selectors 4 §4.2, forgiving |
| `:lang()` | refused | Selectors 4 §7.2 |
| `:last-child` | supported | Selectors 4 §13.4.4 |
| `:last-of-type` | supported | Selectors 4 §13.5.4 |
| `:link` | supported | Selectors 4 §8.2: an a or area with href (no link is visited in a new document) |
| `:modal` | never matches (ignored by rule) | a modal dialog opens by script |
| `:muted` | never matches (ignored by rule) | resource state (§11.5) |
| `:no-button` | never matches (ignored by rule) | Chromium's scroll bar states |
| `:not()` | supported | Selectors 4 §4.3 |
| `:nth-child()` | supported | Selectors 4 §13.4.1, with `of S` |
| `:nth-last-child()` | supported | Selectors 4 §13.4.2, with `of S` |
| `:nth-last-of-type()` | supported | Selectors 4 §13.5.2 |
| `:nth-of-type()` | supported | Selectors 4 §13.5.1 |
| `:only-child` | supported | Selectors 4 §13.4.5 |
| `:only-of-type` | supported | Selectors 4 §13.5.5 |
| `:open` | refused | open details, dialog and select (a static state of their attributes) |
| `:optional` | refused | form controls are not supported |
| `:out-of-range` | refused | form controls are not supported |
| `:past` | never matches (ignored by rule) | time-dimensional (§11.2) |
| `:paused` | never matches (ignored by rule) | resource state (§11.4) |
| `:picture-in-picture` | never matches (ignored by rule) | display state |
| `:placeholder-shown` | refused | form controls are not supported |
| `:playing` | never matches (ignored by rule) | resource state (§11.4): nothing plays |
| `:popover-open` | never matches (ignored by rule) | a popover opens by script or user action |
| `:read-only` | refused | form controls are not supported |
| `:read-write` | refused | form controls are not supported |
| `:required` | refused | form controls are not supported |
| `:root` | supported | Selectors 4 §13.1 |
| `:scope` | refused | Selectors 4 §13.6 |
| `:seeking` | never matches (ignored by rule) | resource state (§11.4) |
| `:single-button` | never matches (ignored by rule) | Chromium's scroll bar states |
| `:stalled` | never matches (ignored by rule) | resource state (§11.4) |
| `:start` | never matches (ignored by rule) | Chromium's scroll bar states |
| `:state()` | refused | custom element states |
| `:target` | never matches (ignored by rule) | the document has no URL fragment (§8.4) |
| `:user-invalid` | never matches (ignored by rule) | after user interaction only (§12.3.3) |
| `:user-valid` | never matches (ignored by rule) | after user interaction only (§12.3.3) |
| `:valid` | refused | form controls are not supported |
| `:vertical` | never matches (ignored by rule) | Chromium's scroll bar states |
| `:visited` | never matches (ignored by rule) | history (§8.2): nothing is visited |
| `:volume-locked` | never matches (ignored by rule) | resource state (§11.5) |
| `:where()` | supported | Selectors 4 §4.4, forgiving, specificity zero |
| `:window-inactive` | never matches (ignored by rule) | window focus |
| `:xr-overlay` | never matches (ignored by rule) | an immersive session |

Pseudo-elements supported: `::before` (CSS Pseudo 4 §4.1: inline generated text (strings and attr()) before the element's content), `::after` (CSS Pseudo 4 §4.1: inline generated text (strings and attr()) after the element's content), `::marker` (CSS Lists 3 §3.1: the color, font-weight, font-style and a content of strings of a list item's marker).

Pseudo-elements never draw in an accepted document and are ignored by rule: `::placeholder`, `::selection`, `::-webkit-scrollbar`, `::-webkit-scrollbar-button`, `::-webkit-scrollbar-thumb`, `::-webkit-scrollbar-track`, `::-webkit-scrollbar-track-piece`, `::-webkit-scrollbar-corner`, `::-webkit-resizer`. The others are refused: `::first-line`, `::first-letter`, `::target-text`, `::spelling-error`, `::grammar-error`, `::search-text`, `::highlight()`, `::backdrop`, `::file-selector-button`, `::cue`, `::cue()`, `::part()`, `::slotted()`, `::details-content`, `::picker()`, `::picker-icon`, `::checkmark`, `::column`, `::scroll-marker`, `::scroll-marker-group`, `::scroll-button()`, `::view-transition`, `::view-transition-group()`, `::view-transition-image-pair()`, `::view-transition-old()`, `::view-transition-new()`, `::view-transition-group-children()`, and Chromium's other `::-webkit-*`.

## At-rules

- Supported: @media (media types; the width and height features with Chromium's 1/64px tolerance, other features
  refused), @supports (answered as Chromium 159 would where the engine can tell, refused where it cannot), @import (through the caller's resolver, with layer() and supports()), @layer, @font-face and
  @page (read as values for the layout), CSS Nesting 1 inside style rules.
- Ignored by rule: @-webkit-keyframes, @keyframes.
- Refused: @color-profile, @container, @counter-style, @document, @font-feature-values, @font-palette-values, @function, @namespace, @position-try, @property, @scope, @starting-style, @view-transition, and every
  other at-rule the engine does not know, a vendor-prefixed one too (`@-moz-document`); Chromium drops those.
- Read without effect: @charset (the encoding is decided before the sheet is parsed).

Page-margin boxes (CSS Paged Media 3 §5) are drawn for `@page` and `@page :first`: the top and bottom rows
(`@top-left`, `@top-center`, `@top-right`, `@bottom-left`, `@bottom-center`, `@bottom-right`), their widths as
§5.3.2 shares them, each box with its margin, padding, solid borders, background colour, text alignment and
vertical alignment (top, middle, bottom), and its content: strings, `counter(page)`, `counter(pages)` and images
(`url()`, or `image-set()` of one URL and its resolution) on the baseline as an `img` in a line, at their natural
size divided by the resolution, the URL resolved against the style sheet that holds the `content` declaration;
an image that does not come or does not decode is refused there. The
page context inherits from the root element, `font-family` too; a face that only a margin box uses is drawn,
where Chromium prints its fallback serif face (it has not loaded the face then; case `pm-family-unused`). Refused at their
place: the corner and side boxes, other page selectors, `width` and `height` on a margin box, the size and
margins of `@page :first`, and content wider than its box (`Documents.Layout.MarginBoxOverflow`), which
Chromium draws over its neighbours. Content taller than its box is drawn as Chromium draws it: from the top of
the box whatever its vertical alignment, unclipped, under the body; the box keeps its height.

## Fonts

There are no system fonts. A family name the document writes is taken from its `@font-face` rules (the first TrueType
source, through the caller's resolver), then from the caller's fonts (`HtmlRenderOptions.Fonts`). The first family of a
`font-family` list that has any face is the family of the text, and it must have the computed weight and style
exactly: no synthesis, no fallback per character. Where a browser would synthesise bold or italic, the face is
refused ("… Give the face, or write font-synthesis: none to print in the nearest face."). Under
`font-synthesis-weight: none` a face of another weight is taken, under `font-synthesis-style: none` one of another
style, plainly, the nearest by CSS Fonts 4 §5.2 (the style first, then the weight: from 400 to 500 the heavier ones up
to 500, then the lighter, then those above 500; below 400 the lighter first; above 500 the heavier first), as Chromium
159 prints it (cases `fs-none-regular-only`, `fs-none-regular-bold`: 600 and 800 take a 700 face, 500 and 300 a 400
face, italic the upright face).

A browser takes a font of the system for a generic family. Here the caller maps them in
`HtmlRenderOptions.GenericFamilies`, and a mapped name is looked up as a written name is (`@font-face` rules first):

- `Standard`: the initial value of `font-family`, and `cursive`, `fantasy`, `math`, `emoji`, `fangsong` and the `ui-*`
  keywords, which Chromium 159 prints in its standard font (measured);
- `Serif`: `serif`; when it is not set, `serif` takes `Standard`;
- `SansSerif`: `sans-serif` and `system-ui`;
- `Monospace`: `monospace` (the keyword still gives text in `monospace` alone its own sizes, 13px at `medium`).

The initial value is the name "Times New Roman", as Chromium computes it: an `@font-face` rule or a caller's font of
that family is taken first, then `Standard`. `initial`, `inherit`, `unset` and `revert` keep the initial value; a
"Times New Roman" written in the document is a family name like any other. A generic family never takes an
`@font-face` rule of its own name, as in Chromium.

Refused in both modes, also with warnings only (`Documents.Html.Unsupported.Font`, at the element's start tag; for a
page-margin box at its `content` declaration):

- a generic family the options do not map, where the list reaches it, also when a later family is supplied:
  "'font-family: sans-serif' needs a family for the generic family 'sans-serif': set GenericFamilies.SansSerif in the
  options (a browser would take a font of the system; there are no system fonts)."
- a mapped name no face answers: "The family 'Inter' that GenericFamilies.SansSerif maps 'sans-serif' to has no face:
  give it in a @font-face rule or in the fonts of the options."
- the initial value with neither a "Times New Roman" face nor `Standard`: "'font-family' is not set here, and its
  initial value needs a family: set GenericFamilies.Standard in the options, or give 'Times New Roman' in a @font-face
  rule or in the fonts of the options (…)."
- a list that runs out: "No family of 'font-family: Inter, Roboto' is supplied: give one in a @font-face rule or in
  the fonts of the options, or end the list with a generic family the options map (there are no system fonts)."

The mapped names are checked once, as input (`Documents.Html.InvalidOptions`): not empty, no control character, at
most 32,767 characters (the longest family name of a TrueType font), and not a generic family keyword.

What is shown, and how. Chromium prints a generic family in a font of the system it runs on, not in the family the
caller maps, so a document with a generic family is not a measured case. Equal to Chromium is shown for the
written-out family: the cases `gf-*` measure the initial value through an `@font-face` rule of "Times New Roman" (in
the body, in a page-margin box, through `inherit`, `unset` and `revert`) and lists that end in a generic family after
a supplied one. That a mapping prints exactly as the written-out family is shown by `GenericFamilyTests`: each generic
document, printed with its mapping, gives the same page model item for item as the equal case it was made from.

### A character the face has no glyph for

A browser takes such a character from a later family of the list, or from any font of the system. The engine takes
no glyph from another font and draws no box for a missing one, so the character is refused in both modes
(`Documents.Font.GlyphMissing`), at its own line and column in the document (a character reference: its ampersand;
the text of a page-margin box: its `content` declaration). Every missing character of the document is reported, each
pair of face and character once, in source order, up to 64 (`HtmlBoxGenerator.MaxMissingGlyphs`). The message names
the character, its code point, the face, the list and what to do, and a later family of the list that has it:

"The character '※' (U+203B) has no glyph in the face 'Lato-Regular' that 'font-family: Lato, Theano Didot' gives
here, and the engine takes no glyph from another font: put the character in an element whose font has it, or use an
image. The family 'Theano Didot' of the list has it: set 'Theano Didot' on an element around the character."

The text is checked as it is drawn, after its `text-transform`: a character only the transform makes (a capital, a
small letter, the final sigma) is refused at the place of the character it is made of, and the message says so: "The
character 'Ɑ' (U+2C6D), which 'text-transform: uppercase' makes of 'ɑ' (U+0251) written here, has no glyph in the face
'Lato-Regular' …"

The check is the shaper's own rule, which `HtmlRendering` passes in (`HtmlBoxOptions.MissingCharacters`); a caller of
`HtmlBoxGenerator` alone who leaves it out gets layout's refusal of the first paragraph with a missing character, at
the line of its text. Never missing: the characters that need no glyph (Default_Ignorable_Code_Point, among them the
soft hyphen where no line breaks at it, U+200B, U+200C, U+200D, U+2060, U+FEFF and the variation selectors; the
mandatory breaks), white space that collapses (a tab, a line feed), and a tab where white space is preserved (refused
on its own). One character is drawn with another's glyph, as Chromium prints it: U+2011 NON-BREAKING HYPHEN, in a face
without it, with the face's U+2010 (case `gm-nbhyphen-lato`, Lato at 12 and 20 pt: the same advance, no break at it,
read back as U+2011). Refused, because Chromium takes them from a font of the system: U+2010 and U+2011 in a face that
has only U+002D (case `gm-nbhyphen-theano`), and the spaces U+00A0, U+202F, U+2009, U+2002, U+2003, U+2007, U+2008 and
U+200A in a face without them (case `gm-spaces-roboto`).

## HTML

- The user-agent style sheet: `Ave.Extensions.Documents.Html/UserAgent/html.css` (HTML Standard §15, as Chromium 159
  applies it).
- A document in quirks mode is refused (add `<!DOCTYPE html>`).
- Refused when drawn (display not none): input, button, select, textarea, meter, progress.
- Refused wherever they are: audio, canvas, details, embed, fencedframe, geolocation, iframe, marquee, noscript, object, optgroup, option, summary, svg and math (their own
  matching and style rules), and an element whose user-agent style is outside the profile (sub, sup, del, s, ruby …).
- Scripts are not run, so a script element a browser runs (no type, a JavaScript type, module, importmap) and every
  event handler attribute (`on…`) are refused; a data block (any other type) is not.
- Style sheets: style elements, link rel=stylesheet (not alternate; the preferred title set), with media and type;
  linked sheets and imports through the caller's `ResourceResolver`; a sheet it does not give is refused.
- Links: an `a` with `href` inside a line of text is a link to a URI (a link annotation; in a tagged PDF a Link element),
  around all of its content: text, images and the inline elements inside it. Its URI is the `href` resolved as HTML
  resolves it (`LinkUrls`): white space at the ends and tabs and line breaks inside removed, the scheme in lower case, a
  relative URL resolved against the document base only when that base is `http` or `https`, and the C0 controls, space,
  `"`, `<`, `>`, U+007F and every character beyond ASCII percent-encoded as UTF-8. Accepted are the schemes `http`, `https`,
  `mailto` and `tel` (`LinkUris`, the model's one rule): a link opens a web page, a mail program or a phone call. Its
  description (`/Contents`, which PDF/UA-1 needs) is the `title`, else the link's text, else the `alt` of an image in it,
  else the URI. Refused at the start tag (`Documents.Html.Unsupported.Link`, with the `href`): any other scheme
  (`javascript`, `vbscript`, `file`, `data`, `ftp`, …), also in mixed case or with white space, tabs or line breaks in it;
  a relative URL without an `http` or `https` base (a document read from a file has a `file:` base, and Chromium would
  write a path of the printing machine); a host with characters beyond ASCII; an `a` with `href` that is not inside a
  line (`display: block`, a table display). An `a` without `href`, and an `a` with `href` that draws nothing, is no link,
  as in Chromium. A link in a table header row that repeats on every page leads from its first copy only (the copies
  are artifacts); Chromium writes it on every page (a listed difference, case `lk-header-row`).
- Links inside the document: an `href` that starts with `#` (also under a `base`) leads to the element the HTML
  Standard's "indicated part of the document" names: `#` alone, and `#top` in any case when no element has that id, the
  top of the first page; else the first element with that `id`, else the first `a` with that `name`, then both again
  with the fragment percent-decoded. The PDF's destination is the page where the target starts and the top of its box
  (or of its first line part, for an inline element), the left edge and the zoom unchanged; no names are written.
  Refused at the start tag: a name no element has (Chromium writes no link), and an element that makes no box or text
  a link can lead to (not rendered, an inline element that draws nothing, an image).
- Languages: an element's language (`xml:lang` in the XML namespace, else `lang`, else its parent's) that differs from
  its parent's is written as `/Lang` on its structure element in a tagged PDF: a block's on its box's element, an inline
  element's on a `Span` element made for it (an `a href`: on its `Link` element). The tag is written as given when it is
  well-formed by BCP 47 (at most 64 characters); any other tag, and `lang=""` (an unknown language, which a PDF/UA-1
  `/Lang` cannot say), is refused at the start tag (`Documents.Html.Unsupported.Language`) and the element keeps its
  parent's language. The root's language is the document's, in the catalog.
- Table header cells: a `th` is a `TH` element whose `/Scope` says what it heads (`Html/Boxes/HtmlBoxGenerator.HeaderScopes.cs`).
  `scope=row` and `scope=rowgroup` head the row, `scope=col` and `scope=colgroup` the column, in any ASCII case; PDF has
  no scope for a group, so a group scope heads the cell's own row or column only. Without a valid `scope`, a `th` in a
  `thead`, or in a row whose cells are all `th`, heads its column; any other `th` heads its row. No cell heads both.
  Not read, and drawn as without them: the `headers` attribute of a cell (no `/Headers` and no element ids are
  written, so a cell under headers that nest has only the scopes to name its headers), `scope` on a `td`, and `abbr`.
- Document metadata: the first `title` is the title. The first `meta` in tree order whose `name` is `author`, in any
  ASCII case, with a `content` that is not empty after stripping white space at both ends is the author; the same for
  `description`, written as the PDF's subject and XMP description (`HtmlMetadata`). Such a content longer than 4 096
  characters, or a title or content with a control character (other than tab, line feed and carriage return) or a
  character XML does not allow (`MetadataTexts`), is refused at the `meta` or the `title`
  (`Documents.Html.Unsupported.Metadata`) and left out, not cleaned. Every other `meta` name (`keywords`, `generator`,
  `date`, `application-name`, …) is ignored. Chromium writes neither the author nor the description into its PDF.

Presentational attributes (HTML Standard §15.2–§15.4):

| Element | Mapped | Refused |
| --- | --- | --- |
| html, head |  | align |
| body | bgcolor, text, link (the colour of a elements that are links), marginheight/topmargin and marginwidth/leftmargin (the last of each pair in the tag) | align, background |
| table | width, height, bgcolor, bordercolor, border (outset, or solid with a bordercolor that is not empty), cellspacing, align=center | align=left/right/inline-start/inline-end (float), background, frame, rules |
| caption | align=top, align=bottom (caption-side: bottom) |  |
| col, colgroup | width, align (-webkit-left/right/center, or one text-align value) |  |
| thead, tbody, tfoot, tr | height, bgcolor, align (-webkit-left/right/center, absmiddle as center, or one text-align value), valign | background |
| td, th | width, height, bgcolor, align, valign, nowrap; the table's cellpadding (1px without one; the integer it starts with, at least 0, at most 65,535; 0 when that integer is beyond 2,147,483,647) and border (1px inset, or solid with bordercolor) | background; a td or th with no table around it (only a tree made in code has one) |
| div, p | align (-webkit-left/right/center, or one text-align value) |  |
| hr | align, color (a solid border, in that colour when it is one), noshade (gray), size (the integer it starts with, 0 when none), width |  |
| img | width, height (and aspect-ratio auto w / h), border (solid; a width that is not a non-negative integer is 0), hspace, vspace, align=top/baseline/absmiddle/abscenter/bottom/absbottom | align=left/right (float), texttop, middle/center; alt text with a size when the image is not there (no src, or the resource resolver gives no PNG or JPEG) |
| ol, ul, li | type; ol start, ol reversed and li value number the items (box generation) | a start or value beyond a 32-bit integer; li value in a reversed ol, or outside an ol where its list shows numbers |
| pre | wrap |  |
| br |  | clear |
| font | color, face (one font-family value), size |  |
| legend |  | align |
| iframe, embed, object, video, canvas, input, marquee, applet, frame |  | width, height, align, hspace, vspace, border |
| meta |  | http-equiv=Content-Security-Policy in head; name=color-scheme with a content other than normal or light |
| link |  | charset other than utf-8 (on any link) |
| every other element (h1–h6, lists, sections, phrasing, custom and unknown elements) but button, select and textarea | align (middle as center, or one text-align value) |  |
| every element but embed | hidden (display: none) | hidden=until-found (its hint content-visibility: hidden is outside this profile) |
| every element |  | contenteditable (empty, true or plaintext-only), autofocus |
