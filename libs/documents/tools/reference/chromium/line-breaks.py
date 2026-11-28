#!/usr/bin/env python3
"""Measures where Chromium's print may end a line: the matrix that the engine's line-break rules for the HTML front-end
are checked against (Ave.Extensions.Documents.Typesetting, LineBreaking/ChromiumLineBreaking.cs).

  python3 line-breaks.py write <folder>                     writes the matrix documents, <folder>/lb-*.html
  python3 line-breaks.py read <out> <resource>              reads <out>/lb-*.print.txt back into <resource>

Each document holds one row per paragraph, in a box 1 pt wide, so that Chromium breaks a row at every place where it
may: the lines of the print ARE the break opportunities. A break is counted between two visible characters; spaces
and the invisible characters (INVISIBLE) are not counted, so a break before or after one of them is the same print.

To measure again, from this folder (Docker, as measure.ps1 needs it):

  python3 line-breaks.py write out/line-breaks
  pwsh -NoProfile -File measure.ps1 -CaseFolder out/line-breaks
  python3 line-breaks.py read out ../../../UnitTests.Extensions.Documents.Typesetting/Reference/Chromium/line-breaks.txt

The resource is written in the format its README gives (Reference/Chromium/README.md beside it). Every row's print
must be read back, and a row in two documents must break alike in both, or the read stops with an error.
"""
import os
import sys

# The documents, in the order of the resource.
DOCUMENTS = ['lb-ascii-letters', 'lb-ascii-digits', 'lb-pairs-letters', 'lb-pairs-digits', 'lb-strings',
             'lb-letters-beside-punctuation', 'lb-signs-in-numbers', 'lb-signs-in-paragraphs']

# The hyphens a line shows where it ends at a soft hyphen.
HYPHENS = {'-', '\u2010'}

# The characters that print nothing: a break beside one of them is the break beside the visible character next to it.
INVISIBLE = {0x20, 0xA0, 0x202F, 0x3000, 0xAD, 0x200B, 0x2060, 0x200E, 0x200F, 0xFEFF, 0x09, 0x0A}

# Characters met in documents: dashes, slashes, punctuation, quotes, brackets, signs, the no-break and zero-width
# characters, an emoji and the ellipsis.
PAIR_CHARACTERS = [
    0x61, 0x31, 0x20, 0x2D, 0x2010, 0x2011, 0x2012, 0x2013, 0x2014, 0x2F, 0x5C, 0x3A, 0x3B, 0x2C, 0x2E, 0x21, 0x3F,
    0x22, 0x27, 0x201C, 0x201D, 0x2018, 0x2019, 0x201E, 0xAB, 0xBB, 0x28, 0x29, 0x5B, 0x5D, 0x7B, 0x7D, 0x25, 0x24,
    0x20AC, 0xA3, 0x2B, 0x3D, 0x23, 0x40, 0x26, 0x2A, 0x5F, 0x7C, 0x7E, 0x3C, 0x3E, 0xA0, 0x202F, 0x200B, 0xAD, 0x2060,
    0x3000, 0x1F600, 0x2026,
]

# Strings of ordinary documents: addresses, paths, dates, numbers, amounts, codes, abbreviations, quotes.
STRINGS = [
    'https://example.com/a/b/c?d=e&f=g#h', 'http://example.org/path/to/page?q=1', 'www.example.nl/over-ons',
    'jan.jansen@example.com', 'info+test@sub.example.org', '/usr/local/share/doc', 'C:\\Users\\jan\\Documents\\file.txt',
    '..\\dir\\file.txt', './a/b.txt', '2026-11-28', '28-11-2026', '28/11/2026', '28.11.2026', '2026/11/28',
    '10:30', '10:30:15', '10.30 uur', '1,234,567.89', '1.234.567,89', '1 234 567,89', '1\u202F234\u202F567,89',
    '3.14', '3,14', '\u20AC 12,50', '\u20AC12,50', '12,50 \u20AC', '12,50\u20AC', '$1,000.00', '1.000,00 EUR',
    '\u00A3 5', '50%', '50 %', '12.5%', '-5', '+31 20 123 4567', '+31-20-1234567', '(020) 123-4567', '06-12345678',
    'NL91 ABNA 0417 1643 00', 'NL91ABNA0417164300', 'REF-2026-0042', 'AB-12-CD', 'INV/2026/0042', 'v1.2.3',
    '1.2.3-beta.4', 'version 2.0', '10\u201312', '10-12', '10 \u2013 12', 'pp.\u00A010\u201312', 'and/or', 'he/she',
    'input/output', 'word\u2014word', 'word \u2014 word', 'self-evident', 'e.g.', 'i.e. this', 'Dr. J. Smith',
    'J.R.R. Tolkien', 'U.S.A.', '"quoted"', '\u201Cquoted\u201D', '\u2018single\u2019', '\u201Ehallo\u201D',
    '\u00ABguillemets\u00BB', "it's", "rock 'n' roll", '(brackets)', '[note]', '{curly}', 'end...', 'end\u2026',
    'wait\u2026 what', 'a\u2026b', 'x=y+z', 'a<b>c', '#hashtag', '@handle', 'snake_case_name', 'CamelCaseName', 'a|b|c',
    'abcd!efgh?ijk', 'Hello!World', 'C++', 'C#', 'A&B', 'R&D', '24/7', '1/2', '\u00BD', 'No.\u00A012', 'art. 7:658 BW',
    '3:5', '1:1000', '5\u00D73', 'p.m.', '10 a.m.', '#1', '100\u00B0C', '-12,5\u00B0', '\u00A7 3.2',
    'ISBN 978-90-12345-67-8', '978-90-12345-67-8',
]

# Letters that are not ASCII, beside every ASCII punctuation character.
LETTERS = [0xE9, 0xFC, 0xDF, 0x3A9, 0x430, 0xC9]
EXTRA_LETTER_ROWS = ['ab \u00B7 cd', 'ab \u00B7cd', 'ab\u00B7cd', '12 \u00B7 34', 'ab \u2022 cd', 'ab \u2014 cd']

# The signs of class PR and PO beside the ASCII punctuation of a number, inside U+0080 to U+00FF and beyond it.
SIGNS = [0x24, 0x25, 0x2B, 0x5C, 0xA2, 0xA3, 0xA4, 0xA5, 0xB0, 0xB1, 0x20A4, 0x20A9, 0x20AC, 0x20B9, 0x20BA, 0x20BD,
         0x20BF, 0x2116, 0x2212, 0x2030, 0x2103, 0x20A7, 0xFFE1, 0x23]
SIGN_NEIGHBOURS = '/:;,.)]}([{!?-'
PARAGRAPH_SIGNS = [0xA2, 0xA3, 0xA5, 0xB0, 0xB1, 0x20AC]
PARAGRAPH_ENDS = ['', ' x\u20AC', ' x\u03A9', ' x\u00E9', ' x\u2014', ' x\u00A0y', ' x\u0101']


def rows_of(document):
    """The rows of a document, each a string."""
    ascii_ = [chr(code) for code in range(0x20, 0x7F)]
    pairs = [chr(code) for code in PAIR_CHARACTERS]
    punctuation = [chr(code) for code in range(0x21, 0x7F) if not chr(code).isalnum()]
    if document == 'lb-ascii-letters':
        return ['ab' + x + y + 'cd' for x in ascii_ for y in ascii_]
    if document == 'lb-ascii-digits':
        return ['12' + x + y + '34' for x in ascii_ for y in ascii_]
    if document == 'lb-pairs-letters':
        return ['ab' + x + y + 'cd' for x in pairs for y in pairs]
    if document == 'lb-pairs-digits':
        return ['12' + x + y + '34' for x in pairs for y in pairs]
    if document == 'lb-strings':
        return STRINGS
    if document == 'lb-letters-beside-punctuation':
        rows = []
        for letter in (chr(code) for code in LETTERS):
            for mark in punctuation:
                rows += ['ab' + mark + letter + 'cd', 'ab' + letter + mark + 'cd', '12' + mark + letter + '34',
                         'a' + letter + mark + '1cd', 'ab ' + mark + letter + 'cd']
        return rows + EXTRA_LETTER_ROWS
    if document == 'lb-signs-in-numbers':
        rows = []
        for sign in (chr(code) for code in SIGNS):
            rows.append('12' + sign + '34')
            for mark in SIGN_NEIGHBOURS:
                rows += ['12' + mark + sign + '34', '12' + sign + mark + '34']
        return rows
    if document == 'lb-signs-in-paragraphs':
        rows = []
        for end in PARAGRAPH_ENDS:
            for sign in (chr(code) for code in PARAGRAPH_SIGNS):
                rows += ['12' + mark + sign + '34' + end for mark in '/:;,.)]}']
                rows += ['12' + sign + mark + '34' + end for mark in '([{']
            rows += ['ab/\u00A3cd' + end, 'ab\u00A3(cd' + end, '12 \u00A3/34' + end]
        return rows
    raise SystemExit('unknown document ' + document)


def escaped(text):
    return ''.join(c if (c.isascii() and c.isalnum()) or c == ' ' else '&#x%X;' % ord(c) for c in text)


def write(folder):
    os.makedirs(folder, exist_ok=True)
    here = os.path.dirname(os.path.abspath(__file__))
    font = os.path.relpath(os.path.join(here, '..', '..', '..', 'assets', 'fonts', 'lato', 'Lato-Regular.ttf'),
                           os.path.abspath(folder)).replace(os.sep, '/')
    head = ('<!doctype html><html><head><meta charset="utf-8"><style>'
            '@font-face { font-family: Lato; src: url(%s); } '
            'html, body { margin: 0; } body { font-family: Lato; font-size: 10pt; } '
            '@page { size: 600pt 14000pt; margin: 18pt; } '
            'div.t { width: 1pt; line-height: 12pt; margin: 0 0 40pt 0; break-inside: avoid; } '
            '</style></head><body>\n') % font
    for document in DOCUMENTS:
        with open(os.path.join(folder, document + '.html'), 'w', encoding='utf-8', newline='\n') as html:
            html.write(head)
            for row in rows_of(document):
                html.write('<div class="t">%s</div>\n' % escaped(row))
            html.write('</body></html>\n')


def printed_paragraphs(path):
    """The print's lines, grouped into paragraphs: a gap of more than 30 pt between baselines starts the next."""
    words = []
    with open(path, encoding='utf-8') as lines:
        for line in lines:
            if line.startswith('word '):
                fields = line.rstrip('\n').split(' ')
                words.append((int(fields[1]), float(fields[4]), float(fields[2]), ' '.join(fields[6:-2])))
    paragraphs, last, line_of = [], None, {}
    for page, baseline, _, text in sorted(words):
        key = (page, round(baseline, 1))
        if key not in line_of:
            if last is None or page != last[0] or baseline - last[1] > 30:
                paragraphs.append([])
            paragraphs[-1].append('')
            line_of[key] = len(paragraphs[-1]) - 1
            last = (page, baseline)
        paragraphs[-1][line_of[key]] += text
    return paragraphs


def breaks_of(row, lines):
    """The breaks of a row as the number of visible characters before each, or None when the print is not the row."""
    visible = [c for c in row if ord(c) not in INVISIBLE]
    count, breaks = 0, []
    for number, line in enumerate(lines):
        for c in line:
            if ord(c) in INVISIBLE:
                continue
            if count < len(visible) and visible[count] == c:
                count += 1
            elif c in HYPHENS and number < len(lines) - 1 and line.endswith(c) and '\u00AD' in row:
                pass
            else:
                return None
        if number < len(lines) - 1 and count not in breaks:
            # A line of nothing but invisible characters and a soft hyphen's hyphen ends at the same place.
            breaks.append(count)
    return breaks if count == len(visible) else None


def read(out, resource):
    seen = {}
    sections = []
    for document in DOCUMENTS:
        rows = rows_of(document)
        paragraphs = printed_paragraphs(os.path.join(out, document + '.print.txt'))
        if len(paragraphs) != len(rows):
            raise SystemExit('%s: %d paragraphs printed for %d rows' % (document, len(paragraphs), len(rows)))
        kept = []
        for row, lines in zip(rows, paragraphs):
            breaks = breaks_of(row, lines)
            if breaks is None:
                raise SystemExit('%s: the print of %r is not the row: %r' % (document, row, lines))
            if row in seen:
                if seen[row] != breaks:
                    raise SystemExit('%s: %r breaks at %r, and at %r in an earlier document' % (document, row, breaks, seen[row]))
                continue
            seen[row] = breaks
            kept.append((row, breaks))
        sections.append((document, kept))
    with open(resource, 'w', encoding='ascii', newline='\n') as text:
        text.write('# Where Chromium 159.0.8154.6 may end a line: its print of the line-break matrix, measured by\n')
        text.write('# tools/reference/chromium/line-breaks.py (format: README.md in this folder).\n')
        for document, kept in sections:
            text.write('@%s\n' % document)
            for row, breaks in kept:
                text.write('%s;%s\n' % (' '.join('%X' % ord(c) for c in row), ' '.join(str(b) for b in breaks)))


if __name__ == '__main__':
    if len(sys.argv) == 3 and sys.argv[1] == 'write':
        write(sys.argv[2])
    elif len(sys.argv) == 4 and sys.argv[1] == 'read':
        read(sys.argv[2], sys.argv[3])
    else:
        raise SystemExit(__doc__)
