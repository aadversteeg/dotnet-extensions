#!/usr/bin/env python3
"""Lists every citation and quotation in the repository's code, tests, docs and tools.

A citation is a reference to an outside text: a specification section (§), a numbered standard (ISO, RFC, ITU-T),
a named specification (CSS, HTML, Unicode, OpenType, PNG, XML, XMP, PDF), a veraPDF rule, or another program's source
(HarfBuzz, Chromium). The script reads comment blocks in source files and paragraphs in Markdown, keeps each block that
contains a citation, and writes one record per block: where it is, which references it names, and which quotations it
holds (text in straight or curly double quotes). A block that names no specification but holds a quotation of five
words or more is kept too, as kind "quotation": its specification is usually named in the member's or the class's
summary above it, so the record names the nearest reference above the block in the same file (or, failing that, the
first one in the file) as its context. The audit (docs/citations-audit.md) is built from this list; run the script
again after a change to see what is new.

Usage: python tools/extract-citations.py [--json out.jsonl] [--markdown out.md]
"""

import argparse
import json
import re
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent

# Text that is not ours to audit: third-party data and licences, generated reference measurements, golden files, and
# the two generated documents that list citations by design (this audit itself, and the conformance matrix, whose rule
# texts are veraPDF's, read from the pinned profiles).
SKIPPED = re.compile(r"^(assets/|tools/reference/chromium/measured/|.*/Golden/|docs/citations-audit\.md$|docs/conformance-matrix\.md$"
                     r"|.*\.(pdf|ttf|otf|png|jpe?g|icc|wasm|bin)$)")
SOURCE = re.compile(r"\.(cs|ps1|py|mjs|js|sh|props|csproj|xml|html)$")
MARKDOWN = re.compile(r"\.md$")

REFERENCE = re.compile(
    r"§\s?[0-9A-Z][0-9A-Za-z.]*"
    r"|\bCSS(?: [A-Z][A-Za-z-]*)*(?: \d(?:\.\d)?)\b"
    r"|\bISO(?:/IEC)? \d{4,5}(?:-\d+)?(?::\d{4})?"
    r"|\bRFC ?\d{3,4}"
    r"|\bITU-T [A-Z]\.\d+"
    r"|\bUAX ?#\d+|\bUTS ?#\d+|\bUnicode(?: Standard)?(?: \d+(?:\.\d+)*)?"
    r"|\bOpenType\b|\bTrueType\b|\bHarfBuzz\b|\bhb-[a-z-]+\.(?:cc|hh)\b"
    r"|\bHTML\b|\bXML 1\.[01]\b|\bXMP\b|\bPNG\b(?: spec)?|\bJFIF\b|\bT\.81\b"
    r"|\bveraPDF(?: rule)?\s*\d[\d.]*-\d+|\b\d\.\d+(?:\.\d+)*-\d+\b"
    r"|\bMatterhorn\b|\bPDF/(?:A|UA)-\d[a-z]?\b|\bICC\.1\b|\bTable \d{2,3}\b|\bChromium\b|\bBlink\b|\bLayoutNG\b"
)
# A quotation: text in double quotes that is prose, not an XML attribute value (name="…") or markup.
# Every pair of quotation marks is matched, short or long, left to right, so that the closing mark of a short quotation
# is never taken as the opening mark of a long one; only quotations of some length are kept (quotes_in). A quotation
# may hold < and >, as a comparison such as loca[n] <= loca[n+1] does; markup ends a quotation instead (MARKUP), so
# the value of a tag's attribute never pairs with a quotation mark of the prose.
QUOTE = re.compile(r"(?<![=\\])\"([^\"\n]*?)\"|“([^”]*?)”")
# In code, a quotation inside a string literal is written with escaped quotes.
ESCAPED_QUOTE = re.compile(r"\\\"([^\"\n]*?)\\\"|“([^”]*?)”")
# An XML or HTML tag or processing instruction in a comment (<see cref="X"/>, <c>, </para>, <?xpacket?>): after the <
# comes a letter, a slash or a question mark. Each is replaced by a line end, which no straight quotation crosses.
MARKUP = re.compile(r"</?[A-Za-z?][^<>\n]*>")

# The shortest text in straight quotes that counts as a quotation (shorter ones are names and values, "kern"), and in
# curly or escaped quotes.
MIN_STRAIGHT_QUOTE = 12
MIN_OTHER_QUOTE = 8


def quotes_in(text, pattern):
    """The quotations of a block: every quoted text of the minimum length, in order."""
    found = []
    for match in pattern.finditer(MARKUP.sub("\n", text)):
        straight = match.group(1)
        if straight is not None and len(straight) >= (MIN_STRAIGHT_QUOTE if pattern is QUOTE else MIN_OTHER_QUOTE):
            found.append(straight)
        elif match.group(2) is not None and len(match.group(2)) >= MIN_OTHER_QUOTE:
            found.append(match.group(2))
    return found

# A reference that points at a place: a section, a table, a numbered rule or standard. Without one and without a
# quotation, a name such as "PNG" or "HarfBuzz" is a mention and makes no claim about a place in the text.
LOCATOR = re.compile(r"§|^Table |^\d|veraPDF|^RFC|^ISO|^UAX|^UTS|^ITU|^T\.81|^XML 1|^ICC|hb-")

COMMENT = re.compile(r"^\s*(///|//|#|\*|/\*\*?|<!--)\s?")

# A quotation long enough to be a claim about another text's wording: five words or more.
MIN_QUOTED_WORDS = 5


def tracked_files():
    output = subprocess.run(["git", "ls-files"], cwd=ROOT, capture_output=True, text=True, check=True).stdout
    return [line for line in output.splitlines() if line and not SKIPPED.match(line)]


def comment_blocks(lines):
    """Consecutive comment lines as one block: (first line number, text)."""
    block, start = [], 0
    for number, line in enumerate(lines, 1):
        match = COMMENT.match(line)
        if match:
            if not block:
                start = number
            block.append(line[match.end():].strip())
            continue
        trailing = line.find("//") if "//" in line and "://" not in line else -1
        if block:
            yield start, " ".join(block)
            block = []
        if trailing >= 0:
            yield number, line[trailing + 2:].strip()
    if block:
        yield start, " ".join(block)


def string_blocks(lines):
    """Error messages and other strings in code can cite too: each line with a citation outside a comment."""
    for number, line in enumerate(lines, 1):
        if COMMENT.match(line):
            continue
        code = line.split("//")[0] if "://" not in line else line
        if REFERENCE.search(code) and '"' in code:
            yield number, code.strip()


def markdown_blocks(lines):
    """Paragraphs and list items of a Markdown file. A fenced code block is code, not prose: its string literals are not
    quotations, so it is left out (a citation in a code comment there is still found by its paragraph's neighbours)."""
    block, start, in_code = [], 0, False
    for number, line in enumerate(lines, 1):
        if line.lstrip().startswith("```"):
            in_code = not in_code
            if block:
                yield start, " ".join(block)
            block = []
            continue
        if in_code:
            continue
        is_new_item = re.match(r"^\s*([-*]|\d+\.|\|)\s", line)
        if not line.strip() or (is_new_item and block):
            if block:
                yield start, " ".join(block)
            block = []
            if not line.strip():
                continue
        if not block:
            start = number
        block.append(line.strip())
    if block:
        yield start, " ".join(block)


def long_quotes(quotes):
    """The quotations of at least MIN_QUOTED_WORDS words that are prose, not a link."""
    return [quote for quote in quotes if len(quote.split()) >= MIN_QUOTED_WORDS and not quote.startswith("http")]


def records():
    for path in tracked_files():
        file = ROOT / path
        if MARKDOWN.search(path):
            blocks = markdown_blocks
        elif SOURCE.search(path):
            blocks = None
        else:
            continue
        lines = file.read_text(encoding="utf-8", errors="replace").splitlines()
        found = [(line, text, QUOTE) for line, text in (blocks(lines) if blocks else comment_blocks(lines))]
        if not blocks:
            found += [(line, text, ESCAPED_QUOTE) for line, text in string_blocks(lines)]
        found.sort(key=lambda item: item[0])
        named = [(line, sorted({match.group(0).strip() for match in REFERENCE.finditer(text)})) for line, text, _ in found]
        first_in_file = next((references for _, references in named if references), None)
        for index, (line, text, quote) in enumerate(found):
            references = named[index][1]
            quotes = quotes_in(text, quote)
            if references:
                located = bool(quotes) or any(LOCATOR.search(reference) for reference in references)
                kind = "citation" if located else "mention"
                yield {"place": f"{path}:{line}", "kind": kind, "references": references, "quotes": quotes, "text": text}
            elif long_quotes(quotes):
                # The nearest block above that names a specification: the member's or the class's summary.
                above = next((references for _, references in reversed(named[:index]) if references), None)
                context = above or first_in_file or []
                yield {"place": f"{path}:{line}", "kind": "quotation", "references": [], "context": context,
                       "quotes": long_quotes(quotes), "text": text}


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--json", help="write one JSON record per line to this file")
    parser.add_argument("--markdown", help="write a Markdown table to this file")
    arguments = parser.parse_args()
    found = list(records())
    if arguments.json:
        with open(arguments.json, "w", encoding="utf-8") as out:
            for record in found:
                out.write(json.dumps(record, ensure_ascii=False) + "\n")
    if arguments.markdown:
        with open(arguments.markdown, "w", encoding="utf-8") as out:
            out.write("| Place | Kind | References | Quotations |\n| --- | --- | --- | --- |\n")
            for record in found:
                quotes = " / ".join(f"“{quote}”" for quote in record["quotes"]).replace("|", "\\|")
                references = ", ".join(record["references"]) or "(context: " + (", ".join(record.get("context", [])) or "none") + ")"
                out.write(f"| `{record['place']}` | {record['kind']} | {references} | {quotes} |\n")
    citations = sum(1 for record in found if record["kind"] == "citation")
    quotations = sum(1 for record in found if record["kind"] == "quotation")
    mentions = sum(1 for record in found if record["kind"] == "mention")
    quoted = sum(1 for record in found if record["kind"] == "citation" and record["quotes"])
    print(f"{len(found)} blocks: {citations} citations ({quoted} with a quotation), {quotations} quotations whose block names"
          f" no specification, {mentions} mentions", file=sys.stderr)


if __name__ == "__main__":
    main()
