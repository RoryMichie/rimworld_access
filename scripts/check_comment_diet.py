#!/usr/bin/env python3
"""Comment-diet ratchet for RimWorld Access.

A comment states a non-obvious constraint in as few words as it takes, and
nothing else — it is never a place to record who asked for a change, when,
or which QA session found it. This check enforces both halves of that rule
over every `src/**/*.cs` and `tests/**/*.cs` file:

Check 1 — attribution ban (zero tolerance, no baseline). Comment text (string
and char literals are stripped first by a small state machine, so a HarmonyId
like "aaronr7734.rimworldaccess" or a sample name inside a literal never
matches) may not name a person or cite user/QA provenance as the reason for a
change. ATTRIBUTION_ALLOWLIST names the handful of product-feature phrases
that happen to contain the word "QA" and are exempt.

Check 2 — per-file comment-line ceiling (shrink-only baseline). Comment lines
are counted the same way project convention measures them: a line whose
content, after stripping leading whitespace, starts with `//`, `/*`, or `*`.
A file already in the baseline (scripts/comment_diet_baseline.txt) fails if
its current count exceeds its baseline count — comments may only shrink or
hold, never silently regrow; a deliberate increase means editing the baseline
in the same commit, which the diff makes visible. A file not yet in the
baseline (new file) is capped at max(40, 30% of its total lines).

Run with --rebaseline to regenerate scripts/comment_diet_baseline.txt from the
current tree (sorted, deterministic); anyone may commit a lowered count for an
existing entry, matching CHASSIS_BASELINE / SHADOW_BASELINE shrink mechanics.

Usage: check_comment_diet.py [--rebaseline]
"""
import glob
import os
import re
import sys

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
BASELINE_PATH = os.path.join(REPO, "scripts", "comment_diet_baseline.txt")

# Phrases that legitimately contain "QA" or "user" as a product-feature name,
# not as provenance. Each is a standing claim: re-justify or delete it.
ATTRIBUTION_ALLOWLIST = [
    "QA R6",              # CLAUDE.md-cited defect class, not a QA citation
    "QA flight recorder",  # product feature name
    "QA trace",            # product feature name
    "qatrace",              # product feature name (log file prefix)
    "QAMark",               # product feature name (the marker method/action)
    "QA marker",            # product feature name
]

ATTRIBUTION_PATTERNS = [
    re.compile(r"(?i)aaron"),
    re.compile(r"(?i)the user (said|asked|wants|reported|confirmed|ruled)"),
    re.compile(r"(?i)user's (qa|ruling|request)"),
    re.compile(r"(?i)\bqa (ruling|report|session|feedback)\b"),
    re.compile(r"(?i)\bear-qa\b|\bbatch qa\b"),
    re.compile(r"\bQA 20\d\d-"),
    re.compile(r"(?i)per (the )?qa\b"),
]

COMMENT_LINE_RE = re.compile(r"^(//|/\*|\*)")

NEW_FILE_FLOOR = 40
NEW_FILE_RATIO = 0.30


def extract_comment_text(text):
    """Returns text of the same length and line breaks as the input, with
    every character NOT inside a `//` or `/* */` comment replaced by a space.
    String and char literals (regular, verbatim `@"..."`, interpolated
    `$"..."`/`$@"..."`) are tracked so a `//` or a name inside one is never
    mistaken for comment content."""
    out = []
    i, n = 0, len(text)
    while i < n:
        ch = text[i]
        # Verbatim / interpolated string prefixes: @"...", $"...", $@"...".
        if ch in "@$" and i + 1 < n and (
            text[i + 1] == '"' or (text[i + 1] in "@$" and i + 2 < n and text[i + 2] == '"')
        ):
            j = i
            while j < n and text[j] in "@$":
                j += 1
            # j now sits on the opening quote; blank the prefix chars.
            for _ in range(j - i):
                out.append(" ")
            i = j
            out.append(" ")  # opening quote
            i += 1
            while i < n:
                if text[i] == '"':
                    if i + 1 < n and text[i + 1] == '"':
                        out.append(" ")
                        out.append(" ")
                        i += 2
                        continue
                    out.append(" ")
                    i += 1
                    break
                out.append(" " if text[i] != "\n" else "\n")
                i += 1
            continue
        if ch == '"':
            out.append(" ")
            i += 1
            while i < n:
                if text[i] == "\\" and i + 1 < n:
                    out.append(" ")
                    out.append(" ")
                    i += 2
                    continue
                if text[i] == '"':
                    out.append(" ")
                    i += 1
                    break
                out.append(" " if text[i] != "\n" else "\n")
                i += 1
            continue
        if ch == "'":
            out.append(" ")
            i += 1
            while i < n:
                if text[i] == "\\" and i + 1 < n:
                    out.append(" ")
                    out.append(" ")
                    i += 2
                    continue
                if text[i] == "'":
                    out.append(" ")
                    i += 1
                    break
                out.append(" " if text[i] != "\n" else "\n")
                i += 1
            continue
        if text.startswith("//", i):
            while i < n and text[i] != "\n":
                out.append(text[i])
                i += 1
            continue
        if text.startswith("/*", i):
            out.append(" ")
            out.append(" ")
            i += 2
            while i < n and not text.startswith("*/", i):
                out.append(text[i])
                i += 1
            if i < n:
                out.append(text[i])
                out.append(text[i + 1])
                i += 2
            continue
        out.append(" " if ch != "\n" else "\n")
        i += 1
    return "".join(out)


def check_attribution(path, rel):
    """Returns a list of 'path:line: message' failures for banned attribution
    patterns found in comment text."""
    with open(path, encoding="utf-8") as handle:
        raw = handle.read()
    comment_text = extract_comment_text(raw)

    # Blank out allowlisted phrases so their substrings (e.g. "QA" inside
    # "QA flight recorder") never trip a pattern below.
    masked = comment_text
    for phrase in ATTRIBUTION_ALLOWLIST:
        masked = re.sub(re.escape(phrase), lambda m: " " * len(m.group(0)), masked, flags=re.IGNORECASE)

    failures = []
    for pattern in ATTRIBUTION_PATTERNS:
        for match in pattern.finditer(masked):
            line = masked.count("\n", 0, match.start()) + 1
            line_start = masked.rfind("\n", 0, match.start()) + 1
            line_end = masked.find("\n", match.start())
            if line_end == -1:
                line_end = len(masked)
            snippet = comment_text[line_start:line_end].strip()
            failures.append(
                f"error RWA-COMMENTDIET: {rel}:{line}: attribution/provenance "
                f"phrase found in comment: {snippet!r}")
    return failures


def count_comment_lines(raw_text):
    """Counts lines whose leading-whitespace-stripped content starts with
    `//`, `/*`, or `*` — the project's standing census one-liner, restated in
    Python so this check needs no shell pipeline."""
    count = 0
    for line in raw_text.split("\n"):
        if COMMENT_LINE_RE.match(line.lstrip()):
            count += 1
    return count


def load_baseline():
    baseline = {}
    if not os.path.exists(BASELINE_PATH):
        return baseline
    with open(BASELINE_PATH, encoding="utf-8") as handle:
        for line in handle:
            line = line.rstrip("\n")
            if not line:
                continue
            count_str, rel = line.split("\t", 1)
            baseline[rel] = int(count_str)
    return baseline


def write_baseline(counts):
    with open(BASELINE_PATH, "w", encoding="utf-8") as handle:
        for rel in sorted(counts):
            handle.write(f"{counts[rel]}\t{rel}\n")


def all_source_files():
    paths = []
    for pattern in ("src/**/*.cs", "tests/**/*.cs"):
        paths.extend(glob.glob(os.path.join(REPO, pattern), recursive=True))
    rels = sorted(os.path.relpath(p, REPO).replace(os.sep, "/") for p in paths)
    return rels


def main():
    argv = sys.argv[1:]
    rels = all_source_files()

    counts = {}
    total_lines = {}
    for rel in rels:
        path = os.path.join(REPO, rel)
        with open(path, encoding="utf-8") as handle:
            raw = handle.read()
        counts[rel] = count_comment_lines(raw)
        total_lines[rel] = raw.count("\n") + 1

    if "--rebaseline" in argv:
        write_baseline(counts)
        print(f"check_comment_diet: wrote baseline for {len(counts)} files "
              f"to {os.path.relpath(BASELINE_PATH, REPO)}")
        return 0

    failures = []

    for rel in rels:
        path = os.path.join(REPO, rel)
        failures.extend(check_attribution(path, rel))

    baseline = load_baseline()
    tightened = []
    for rel in rels:
        current = counts[rel]
        if rel in baseline:
            ceiling = baseline[rel]
            if current > ceiling:
                failures.append(
                    f"error RWA-COMMENTDIET: {rel}: {current} comment lines "
                    f"exceeds its baseline ceiling of {ceiling}. Comments may "
                    f"only shrink or hold — if the increase is deliberate, "
                    f"rerun with --rebaseline and commit the new baseline "
                    f"alongside this change.")
            elif current < ceiling:
                tightened.append((rel, current, ceiling))
        else:
            ceiling = max(NEW_FILE_FLOOR, int(total_lines[rel] * NEW_FILE_RATIO))
            if current > ceiling:
                failures.append(
                    f"error RWA-COMMENTDIET: {rel}: new file has {current} "
                    f"comment lines, over its ceiling of {ceiling} "
                    f"(max({NEW_FILE_FLOOR}, 30% of {total_lines[rel]} total "
                    f"lines)). Trim the comments or, if this is a genuinely "
                    f"contract-bearing new scope, discuss raising the floor.")

    for message in failures:
        print(message)
    if failures:
        return 1

    if tightened:
        print(f"check_comment_diet: --tighten report — {len(tightened)} "
              f"file(s) now under their baseline ceiling (anyone may commit "
              f"the lower number via --rebaseline):")
        for rel, current, ceiling in sorted(tightened):
            print(f"  {rel}: {current} (baseline {ceiling})")

    print(f"check_comment_diet: OK — attribution-free, "
          f"{len(baseline)} file(s) within their comment-line baseline, "
          f"{len(rels) - len(baseline)} new file(s) within the default ceiling")
    return 0


if __name__ == "__main__":
    sys.exit(main())
