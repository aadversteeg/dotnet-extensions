#!/usr/bin/env python3
"""Extracts the CSS parsing and computed-value cases of web-platform-tests into one JSON file.

The WPT tests in css/css-syntax, css/selectors, css/css-cascade, css/css-variables and css/css-values call five
helpers of css/support (parsing-testcommon.js, computed-testcommon.js) with literal arguments:

    test_valid_value(property, value, serialized?)     test_invalid_value(property, value)
    test_valid_selector(selector, serialized?)         test_invalid_selector(selector)
    test_computed_value(property, specified, computed?)

This script reads every test file of those directories, finds each call, and keeps the call when every argument is a
JavaScript literal (a string, an array of strings, a number; an options object is dropped). A call with any other
argument (a variable, an expression, a template with ${...}) is listed under "notExtracted" with its place, so the
test project can report it as skipped by rule. For test_computed_value the file's markup is kept as well (the test
computes the value on its #target element, whose context the markup sets).

Usage: extract-wpt-css-cases.py <wpt checkout> <output.json>
The output is committed under assets/test-suites/wpt/css/; the checkout's commit is recorded in it.
"""

import json
import os
import re
import subprocess
import sys

DIRECTORIES = ["css/css-syntax", "css/selectors", "css/css-cascade", "css/css-variables", "css/css-values"]
HELPERS = ["test_valid_value", "test_invalid_value", "test_valid_selector", "test_invalid_selector", "test_computed_value"]
CALL = re.compile(r"\b(" + "|".join(HELPERS) + r")\s*\(")
EXTENSIONS = (".html", ".htm", ".xht", ".xhtml", ".js")


class NotLiteral(Exception):
    pass


def read_string(text, i):
    quote = text[i]
    i += 1
    out = []
    while i < len(text):
        c = text[i]
        if c == quote:
            return "".join(out), i + 1
        if quote == "`" and c == "$" and text.startswith("${", i):
            raise NotLiteral("template with a substitution")
        if c == "\\":
            i += 1
            e = text[i]
            simple = {"n": "\n", "t": "\t", "r": "\r", "b": "\b", "f": "\f", "v": "\v", "0": "\0"}
            if e in simple and not (e == "0" and i + 1 < len(text) and text[i + 1].isdigit()):
                out.append(simple[e])
                i += 1
            elif e == "x":
                out.append(chr(int(text[i + 1:i + 3], 16)))
                i += 3
            elif e == "u" and text[i + 1] == "{":
                end = text.index("}", i)
                out.append(chr(int(text[i + 2:end], 16)))
                i = end + 1
            elif e == "u":
                code = int(text[i + 1:i + 5], 16)
                i += 5
                if 0xD800 <= code <= 0xDBFF and text.startswith("\\u", i):
                    low = int(text[i + 2:i + 6], 16)
                    if 0xDC00 <= low <= 0xDFFF:
                        code = 0x10000 + ((code - 0xD800) << 10) + (low - 0xDC00)
                        i += 6
                out.append(chr(code) if not 0xD800 <= code <= 0xDFFF else "�")
            elif e == "\r" and text.startswith("\r\n", i):
                i += 2
            elif e in "\n  \r":
                i += 1
            else:
                out.append(e)
                i += 1
            continue
        if c == "\n" and quote != "`":
            raise NotLiteral("unterminated string")
        out.append(c)
        i += 1
    raise NotLiteral("unterminated string")


def skip_space(text, i):
    while i < len(text):
        if text[i].isspace():
            i += 1
        elif text.startswith("//", i):
            i = text.index("\n", i) if "\n" in text[i:] else len(text)
        elif text.startswith("/*", i):
            i = text.index("*/", i) + 2
        else:
            break
    return i


def read_value(text, i):
    i = skip_space(text, i)
    c = text[i]
    if c in "'\"`":
        value, i = read_string(text, i)
        # A string followed by + is a concatenation: not a literal.
        if skip_space(text, i) < len(text) and text[skip_space(text, i)] == "+":
            raise NotLiteral("a concatenation")
        return value, i
    if c == "[":
        items = []
        i += 1
        while True:
            i = skip_space(text, i)
            if text[i] == "]":
                return items, i + 1
            item, i = read_value(text, i)
            items.append(item)
            i = skip_space(text, i)
            if text[i] == ",":
                i += 1
    if c == "{":
        depth = 0
        while i < len(text):
            if text[i] in "'\"`":
                _, i = read_string(text, i)
                continue
            if text[i] == "{":
                depth += 1
            elif text[i] == "}":
                depth -= 1
                if depth == 0:
                    return {"options": True}, i + 1
            i += 1
        raise NotLiteral("unterminated object")
    number = re.match(r"-?\d+(\.\d+)?", text[i:])
    if number:
        return float(number.group(0)), i + len(number.group(0))
    raise NotLiteral("not a literal: " + text[i:i + 30].split("\n")[0])


def read_arguments(text, i):
    args = []
    while True:
        i = skip_space(text, i)
        if text[i] == ")":
            return args, i + 1
        value, i = read_value(text, i)
        args.append(value)
        i = skip_space(text, i)
        if text[i] == ",":
            i += 1
        elif text[i] != ")":
            raise NotLiteral("not a literal argument list")


def is_definition(text, start):
    return re.search(r"function\s+$", text[max(0, start - 20):start]) is not None


def main():
    root, output = sys.argv[1], sys.argv[2]
    commit = subprocess.run(["git", "-C", root, "rev-parse", "HEAD"], capture_output=True, text=True, check=True).stdout.strip()
    cases, not_extracted, markup = [], [], {}
    for directory in DIRECTORIES:
        for folder, _, files in sorted(os.walk(os.path.join(root, directory))):
            for name in sorted(files):
                if not name.endswith(EXTENSIONS):
                    continue
                path = os.path.join(folder, name)
                relative = os.path.relpath(path, root).replace(os.sep, "/")
                with open(path, encoding="utf-8", errors="replace") as handle:
                    text = handle.read()
                for match in CALL.finditer(text):
                    if is_definition(text, match.start()):
                        continue
                    line = text.count("\n", 0, match.start()) + 1
                    helper = match.group(1)
                    try:
                        args, _ = read_arguments(text, match.end())
                        args = [arg for arg in args if not isinstance(arg, dict)]
                        if not args or not all(isinstance(arg, (str, list)) for arg in args):
                            raise NotLiteral("an argument is not a string")
                        cases.append({"file": relative, "line": line, "helper": helper, "args": args})
                        if helper == "test_computed_value":
                            markup[relative] = text
                    except (NotLiteral, IndexError, ValueError) as problem:
                        not_extracted.append({"file": relative, "line": line, "helper": helper, "reason": str(problem)})
    result = {
        "source": "https://github.com/web-platform-tests/wpt",
        "commit": commit,
        "directories": DIRECTORIES,
        "cases": cases,
        "notExtracted": not_extracted,
        "markup": markup,
    }
    with open(output, "w", encoding="utf-8", newline="\n") as handle:
        json.dump(result, handle, ensure_ascii=False, indent=1)
        handle.write("\n")
    counts = {}
    for case in cases:
        counts[case["helper"]] = counts.get(case["helper"], 0) + 1
    print(f"{len(cases)} cases {counts}; {len(not_extracted)} not extracted")


if __name__ == "__main__":
    main()
