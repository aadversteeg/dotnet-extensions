# Reads back the glyphs inside each word of a print: which glyph the PDF draws and where its origin is. Run by
# extract.sh in the reference tools image (PyMuPDF):
#
#   python3 glyphs.py <file.pdf>
#
# One line per word, the words of print.txt in its order (positions.py: page by page, sorted by baseline and then x0),
# in points, origin at the top left of the page (y down), three decimals:
#
#   <page> <baseline> <text> <glyph> <x> <dy> <end> [<glyph> <x> <dy> <end> ...]
#
# with four values per glyph the PDF draws for the word, in drawing order: the glyph id, the x of its origin, the
# distance of its origin below the word's baseline (negative above it) and the x where its advance ends. The glyphs come from PyMuPDF's text trace,
# which lists the glyphs as the content stream shows them (its glyph id is the font program's, as Chromium embeds a
# font subset that keeps the glyph ids), and not from its text reading, which positions.py's words come from: the text
# reading spreads a ligature over the characters it stands for at positions of its own (case ls-letters: "fi" is one
# glyph at 76.312, read as "f" at 76.312 and "i" at 82.329), and joins a base and the combining marks drawn after it
# into one character (case sk-extreme). So each word's characters are matched to the trace: its first character to the
# glyph with the same origin, then glyph by glyph in drawing order, each glyph standing for the characters its own
# character decomposes into (a ligature, case ls-letters; a precomposed letter with a mark, case ta-marks: "y" with
# U+0323 is the one glyph of U+1EF5), a combining mark for itself where the reading lists it, else for none (case
# sk-part), up to white space. Chromium draws a format character (a soft hyphen, a zero-width space, a joiner; case
# ws-soft: U+00AD between "aa" and "bb") as the font's space glyph without an advance, which the trace lists as a space:
# it stands for that character and draws nothing, so it is no glyph of the word either.
# The trace lists the further characters of one glyph's text (Chromium's ToUnicode map gives Lato's "ft" ligature the
# text "ft") as entries of their own at the glyph's origin with the glyph id -1 (case a2-blank-part: "after" is the
# glyphs 45, 91, 59, 117, with a "t" of glyph -1 at the origin of 91): such an entry stands for its character and draws
# nothing, so it is no glyph of the word. A word whose characters do not match the trace so stops the read-back, so a
# print never loses what it draws without a word.
import sys
import unicodedata

import pymupdf

import positions

def characters(glyph, after_base, chars, counted):
    """How many characters of the text reading, from the one at counted on, a glyph of the trace stands for: as many as
    give its character's compatibility decomposition (a ligature, U+FB01 "fi", the two it expands to; a precomposed
    letter the reading writes as its base and its mark, U+1EF5 as "y" and U+0323, the two), a combining mark after a base
    one where the reading lists it as a character of its own and none where it reads it into its base; white space one
    where the reading has a format character there (Chromium draws a soft hyphen, a zero-width space or a joiner as the
    font's space glyph, case ws-soft), else None: white space is no part of a word."""
    code = glyph[0]
    if code > 0 and chr(code).isspace():
        return 1 if counted < len(chars) and unicodedata.category(chars[counted]["c"]) == "Cf" else None
    if code > 0 and unicodedata.combining(chr(code)) and after_base:
        return 1 if counted < len(chars) and chars[counted]["c"] == chr(code) else 0
    if code > 0:
        wanted = unicodedata.normalize("NFKD", chr(code))
        for count in range(1, len(chars) - counted + 1):
            if unicodedata.normalize("NFKD", "".join(char["c"] for char in chars[counted:counted + count])) == wanted:
                return count
    return 1


def glyphs(number, trace, chars):
    """The glyphs of the trace that draw a word's characters, from the one at its first character's origin on, each with
    its index in the trace."""
    origin = chars[0]["origin"]
    start = next((index for index, glyph in enumerate(trace) if abs(glyph[2][0] - origin[0]) < 0.0005 and abs(glyph[2][1] - origin[1]) < 0.0005), None)
    if start is None:
        raise ValueError(f"page {number}: no glyph is drawn at the start of '{''.join(char['c'] for char in chars)}' at {origin}")
    found, counted, index = [], 0, start
    while index < len(trace) and (counted < len(chars) or (found and characters(trace[index], True, chars, counted) == 0)):
        count = characters(trace[index], bool(found), chars, counted)
        if count is None:
            break
        counted += count
        found.append(trace[index])
        index += 1
    if counted != len(chars):
        raise ValueError(f"page {number}: the glyphs drawn for '{''.join(char['c'] for char in chars)}' stand for {counted} characters, not {len(chars)}")
    return [(glyph, start + offset) for offset, glyph in enumerate(found) if glyph[1] >= 0 and not chr(max(glyph[0], 0)).isspace()]


def words(page, number):
    """The words of a page in the order of print.txt, each as (baseline, text, its glyphs, the place of each glyph in the
    page's drawing sequence: PyMuPDF's seqno of the text that draws it)."""
    spans = page.get_texttrace()
    trace = [glyph for span in spans for glyph in span["chars"]]
    places = [span["seqno"] for span in spans for _ in span["chars"]]
    found = []
    for _, chars in positions.pieces(page):
        drawn = glyphs(number, trace, chars)
        found.append((chars[0]["origin"][1], chars[0]["origin"][0], "".join(char["c"] for char in chars), [glyph for glyph, _ in drawn], [places[index] for _, index in drawn]))
    return [(baseline, text, drawn, at) for baseline, _, text, drawn, at in sorted(found, key=lambda word: (round(word[0], 3), round(word[1], 3)))]


def main(path):
    for number, page in enumerate(pymupdf.open(path), start=1):
        for baseline, text, drawn, _ in words(page, number):
            fours = " ".join(f"{glyph[1]} {glyph[2][0]:.3f} {round(glyph[2][1] - baseline, 3) + 0.0:.3f} {glyph[3][2]:.3f}" for glyph in drawn)
            print(f"{number} {baseline:.3f} {text} {fours}")


if __name__ == "__main__":
    main(sys.argv[1])
