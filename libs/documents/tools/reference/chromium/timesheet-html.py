#!/usr/bin/env python3
"""Writes the timesheet of the samples as finished HTML for print, from the samples' synthetic data.

The document is the timesheet that samples/Ave.Extensions.Documents.Samples builds with the code front-end
(Timesheet/Timesheet.cs), written the way an author writes HTML and CSS for print: semantic elements, one style sheet,
the entries as a table with a repeated header row, the logo as an img, fonts by @font-face from the family's own
font files, and the footer with "page x of y" in @page margin boxes. Filling a template with data is the caller's job;
this script is that caller, so the engine and Chromium both get the same finished HTML.

    python3 timesheet-html.py               writes documents/timesheet-one-page.html and documents/timesheet-pages.html

The texts are Dutch (nl-NL), as in the samples' golden files. As the sample does (FontCoverage.Displayable), every
character Lato Regular has no glyph for is dropped or replaced by a plain equivalent before it goes into the HTML.
"""
import html
import json
import struct
from datetime import datetime
from pathlib import Path

here = Path(__file__).resolve().parent
family = here.parents[2]
data = family / 'samples' / 'Ave.Extensions.Documents.Samples' / 'data'
fonts = family / 'assets' / 'fonts' / 'lato'

# The documents written, and the data set each one shows: the edge-case set fits one page, the long month runs over
# several.
DOCUMENTS = {'timesheet-one-page': 'sample-edge.json', 'timesheet-pages': 'sample-long.json'}

# The sample's labels for a Dutch culture (Timesheet/TimesheetLabels.cs) and its brand (Timesheet/Brand.cs).
LABELS = dict(title='Urenstaat', period='Periode', executed='Uitgevoerd op', task='Taak', item='Titel',
              hours='Duur (uur)', total='Totaal', page='Pagina', of='van', iban='Rekening', vat='BTW', coc='KvK')
COMPANY = dict(iban='NL00 BANK 0123 4567 89', vat='NL000000000B00', coc='00000000')

# Replacements for characters Lato lacks, as in the sample's FontCoverage.
FALLBACKS = {0x21D2: '=>', 0x2011: '-'}


def cmap(path):
    """The code points a TrueType font maps to a glyph (cmap formats 4 and 12)."""
    font = path.read_bytes()
    tables = {font[12 + 16 * i:16 + 16 * i]: struct.unpack('>II', font[20 + 16 * i:28 + 16 * i])
              for i in range(struct.unpack('>H', font[4:6])[0])}
    base = tables[b'cmap'][0]
    points = set()
    for i in range(struct.unpack('>H', font[base + 2:base + 4])[0]):
        offset = base + struct.unpack('>I', font[base + 8 + 8 * i:base + 12 + 8 * i])[0]
        form = struct.unpack('>H', font[offset:offset + 2])[0]
        if form == 4:
            count = struct.unpack('>H', font[offset + 6:offset + 8])[0] // 2
            ends = struct.unpack(f'>{count}H', font[offset + 14:offset + 14 + 2 * count])
            at = offset + 16 + 2 * count
            starts = struct.unpack(f'>{count}H', font[at:at + 2 * count])
            deltas = struct.unpack(f'>{count}h', font[at + 2 * count:at + 4 * count])
            ranges = at + 4 * count
            for k in range(count):
                range_offset = struct.unpack('>H', font[ranges + 2 * k:ranges + 2 * k + 2])[0]
                for point in range(starts[k], ends[k] + 1):
                    if point == 0xFFFF:
                        continue
                    if range_offset == 0:
                        glyph = (point + deltas[k]) & 0xFFFF
                    else:
                        where = ranges + 2 * k + range_offset + 2 * (point - starts[k])
                        glyph = struct.unpack('>H', font[where:where + 2])[0]
                        glyph = (glyph + deltas[k]) & 0xFFFF if glyph else 0
                    if glyph:
                        points.add(point)
        elif form == 12:
            for k in range(struct.unpack('>I', font[offset + 12:offset + 16])[0]):
                start, end, glyph = struct.unpack('>III', font[offset + 16 + 12 * k:offset + 28 + 12 * k])
                points.update(range(start, end + 1) if glyph or start else range(start + 1, end + 1))
    return points


LATO = cmap(fonts / 'Lato-Regular.ttf')


def clean(text):
    """Keeps what Lato can show and all white space, replaces a few characters, drops the rest."""
    return ''.join(c if c.isspace() or ord(c) in LATO else FALLBACKS.get(ord(c), '') for c in text or '')


def day(value):
    return datetime.fromisoformat(value).strftime('%Y-%m-%d') if value else 'N/A'


def hours(value):
    """Two decimals in the Dutch number format: 1.234,50."""
    return f'{value:,.2f}'.replace(',', '_').replace('.', ',').replace('_', '.')


STYLE = """\
@font-face { font-family: Lato; src: url(../../../../assets/fonts/lato/Lato-Regular.ttf); font-weight: 400; font-style: normal; }
@font-face { font-family: Lato; src: url(../../../../assets/fonts/lato/Lato-Bold.ttf); font-weight: 700; font-style: normal; }
@font-face { font-family: Lato; src: url(../../../../assets/fonts/lato/Lato-Italic.ttf); font-weight: 400; font-style: italic; }

/* A4 with 1 cm side margins. Page 1 starts with the masthead in the flow; the later pages carry a running header
   in the top margin. The footer is in the bottom margin of every page. The margin boxes inherit from the page, not
   from the body, so the page names the font too. */
@page {
  size: A4;
  margin: 4cm 1cm 2cm;
  font-family: Lato, sans-serif;

  @top-left {
    content: "__TITLE__";
    font-size: 24pt;
    font-weight: 700;
    margin: 1cm 0 0.4cm;
    border-bottom: 3pt solid #ffa500;
    vertical-align: middle;
  }
  @top-right {
    content: "__RIGHT__";
    white-space: pre;
    font-size: 9pt;
    line-height: 1.5;
    text-align: right;
    margin: 1cm 0 0.4cm;
    border-bottom: 3pt solid #ffa500;
    vertical-align: middle;
  }
  @bottom-left {
    content: "__IBAN__";
    font-size: 8pt;
    margin-top: 0.4cm;
    padding-top: 6pt;
    border-top: 3pt solid #ffa500;
    vertical-align: top;
  }
  @bottom-center {
    content: "__VAT__\\2003\\2003__COC__";
    font-size: 8pt;
    margin-top: 0.4cm;
    padding-top: 6pt;
    border-top: 3pt solid #ffa500;
    vertical-align: top;
  }
  @bottom-right {
    content: "__PAGE__ " counter(page) " __OF__ " counter(pages);
    font-size: 8pt;
    margin-top: 0.4cm;
    padding-top: 6pt;
    border-top: 3pt solid #ffa500;
    vertical-align: top;
  }
}

@page :first {
  margin-top: 1cm;

  @top-left { content: none; }
  @top-right { content: none; }
}

body {
  margin: 0;
  font-family: Lato, sans-serif;
  font-size: 10pt;
  color: #000;
}

.masthead {
  margin: 0 0 0.4cm;
  border-bottom: 3pt solid #ffa500;
}

.masthead h1 {
  margin: 0;
  font-size: 24pt;
  font-weight: 700;
}

.masthead img {
  width: 2.5cm;
  height: 2.5cm;
  margin-right: 1cm;
  vertical-align: middle;
}

.parties {
  width: 100%;
  border-collapse: collapse;
}

.parties td {
  padding: 0;
  vertical-align: top;
}

.parties td + td {
  text-align: right;
}

.parties p {
  margin: 4pt 0 0;
  font-size: 11pt;
}

.parties p.name {
  margin: 0;
  font-size: 13pt;
}

.entries {
  width: 100%;
  margin-top: 10pt;
  table-layout: fixed;
  border-collapse: collapse;
}

.entries col.executed { width: 14.29%; }
.entries col.task { width: 17.14%; }
.entries col.item { width: 57.14%; }
.entries col.hours { width: 11.43%; }

.entries th,
.entries td {
  padding: 1.5pt 2pt;
  border: 1pt solid #d3d3d3;
  vertical-align: top;
  text-align: left;
}

.entries tbody {
  break-inside: avoid;
}

.entries .hours {
  text-align: right;
}

.entries .description {
  font-size: 8pt;
  font-style: italic;
  color: #232323;
}

.entries .total th,
.entries .total td {
  padding-top: 5pt;
  padding-bottom: 5pt;
  border: 1.5pt solid #000;
  font-weight: 700;
}
"""


def css_string(text):
    """Text as the inside of a CSS string: backslash and quote escaped, a line break as \\A."""
    return text.replace('\\', '\\\\').replace('"', '\\"').replace('\n', '\\A ')


def document(source):
    raw = json.loads((data / source).read_text(encoding='utf-8'))
    customer, project = clean(raw['customerName']), clean(raw['projectName'])
    period = f"{day(raw['periodStart'])} - {day(raw['periodEnd'])}"
    entries = raw['timeEntries']
    total = sum(entry['duration'] for entry in entries)
    e = html.escape

    style = (STYLE
             .replace('__TITLE__', css_string(LABELS['title']))
             .replace('__RIGHT__', css_string(f'{customer}\n{project}\n{period}'))
             .replace('__IBAN__', css_string(f"{LABELS['iban']}: {COMPANY['iban']}"))
             .replace('__VAT__', css_string(f"{LABELS['vat']}: {COMPANY['vat']}"))
             .replace('__COC__', css_string(f"{LABELS['coc']}: {COMPANY['coc']}"))
             .replace('__PAGE__', css_string(LABELS['page']))
             .replace('__OF__', css_string(LABELS['of'])))

    rows = []
    for entry in entries:
        description = clean(entry['description']).strip()
        span = ' rowspan="2"' if description else ''
        rows.append('<tbody>')
        rows.append(f'<tr><td{span}>{day(entry["executedAt"])}</td><td>{e(clean(entry["taskName"]))}</td>'
                    f'<td>{e(clean(entry["title"]))}</td><td class="hours"{span}>{hours(entry["duration"])}</td></tr>')
        if description:
            rows.append(f'<tr><td class="description" colspan="2">{e(description)}</td></tr>')
        rows.append('</tbody>')

    return f"""<!DOCTYPE html>
<html lang="nl-NL">
<head>
<meta charset="utf-8">
<title>{e(LABELS['title'])} {e(customer)} {e(period)}</title>
<style>
{style}</style>
</head>
<body>
<header class="masthead">
<h1><img src="../../../../samples/Ave.Extensions.Documents.Samples/assets/logo.png" alt="Logo">{e(LABELS['title'])}</h1>
</header>
<main>
<table class="parties" role="presentation">
<tr>
<td><p class="name">{e(customer)}</p><p>{day(raw['reportDate'])}</p></td>
<td><p class="name">{e(project)}</p><p>{e(LABELS['period'])}: {e(period)}</p></td>
</tr>
</table>
<table class="entries">
<colgroup><col class="executed"><col class="task"><col class="item"><col class="hours"></colgroup>
<thead>
<tr><th scope="col">{e(LABELS['executed'])}</th><th scope="col">{e(LABELS['task'])}</th><th scope="col">{e(LABELS['item'])}</th><th scope="col" class="hours">{e(LABELS['hours'])}</th></tr>
</thead>
{chr(10).join(rows)}
<tbody class="total">
<tr><th scope="row" colspan="3">{e(LABELS['total'])}:</th><td class="hours">{hours(total)}</td></tr>
</tbody>
</table>
</main>
</body>
</html>
"""


if __name__ == '__main__':
    out = here / 'documents'
    out.mkdir(exist_ok=True)
    for name, source in DOCUMENTS.items():
        (out / f'{name}.html').write_text(document(source), encoding='utf-8', newline='\n')
        print(f'{name}.html from {source}')
