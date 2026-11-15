#!/usr/bin/env python3
"""Lists every citation and quotation in the repository's code, tests, docs and tools.

A citation is a reference to an outside text: a specification section (§), a numbered standard (ISO, RFC, ITU-T),
a named specification (CSS, HTML, Unicode, OpenType, PNG, XML, XMP, PDF), a veraPDF rule, or another program's source
(HarfBuzz, Chromium). The script reads comment blocks in source files and paragraphs in Markdown, keeps each block that
contains a citation, and writes one record per block: where it is, which references it names, and which quotations it
holds (text in straight or curly double quotes). The audit (docs/citations-audit.md) is built from this list; run the
script again after a change to see what is new.

Usage: python tools/extract-citations.py [--json out.jsonl] [--markdown out.md]
"""

import argparse
import json
import re
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent

# Text that is not ours to audit: third-party data and licences, generated reference measurements, golden files.
SKIPPED = re.compile(r"^(assets/|tools/reference/chromium/measured/|.*/Golden/|.*\.(pdf|ttf|otf|png|jpe?g|icc|wasm|bin)$)")
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
QUOTE = re.compile(r"(?<![=\\])\"([^\"\n<>]{12,}?)\"|“([^”<>]{8,}?)”")
# In code, a quotation inside a string literal is written with escaped quotes.
ESCAPED_QUOTE = re.compile(r"\\\"([^\"\n<>]{8,}?)\\\"|“([^”<>]{8,}?)”")

# A reference that points at a place: a section, a table, a numbered rule or standard. Without one and without a
# quotation, a name such as "PNG" or "HarfBuzz" is a mention and makes no claim about a place in the text.
LOCATOR = re.compile(r"§|^Table |^\d|veraPDF|^RFC|^ISO|^UAX|^UTS|^ITU|^T\.81|^XML 1|^ICC|hb-")

COMMENT = re.compile(r"^\s*(///|//|#|\*|/\*\*?|<!--)\s?")


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
    """Paragraphs and list items of a Markdown file."""
    block, start = [], 0
    for number, line in enumerate(lines, 1):
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
        for line, text, quote in sorted(found, key=lambda item: item[0]):
            references = sorted({match.group(0).strip() for match in REFERENCE.finditer(text)})
            if not references:
                continue
            quotes = [match.group(1) or match.group(2) for match in quote.finditer(text)]
            located = bool(quotes) or any(LOCATOR.search(reference) for reference in references)
            kind = "citation" if located else "mention"
            yield {"place": f"{path}:{line}", "kind": kind, "references": references, "quotes": quotes, "text": text}


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
                out.write(f"| `{record['place']}` | {record['kind']} | {', '.join(record['references'])} | {quotes} |\n")
    citations = sum(1 for record in found if record["kind"] == "citation")
    quoted = sum(1 for record in found if record["quotes"])
    print(f"{len(found)} blocks: {citations} citations ({quoted} with a quotation), {len(found) - citations} mentions", file=sys.stderr)


if __name__ == "__main__":
    main()
