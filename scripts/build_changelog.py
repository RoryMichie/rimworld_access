#!/usr/bin/env python3
"""Compile changelog.d/ fragments into a dated release entry.

Collects every fragment in changelog.d/ (one change per file, see that folder's
README), groups them by category, and inserts a "## [version] - date" section at
the top of the release list in both CHANGELOG.md and the website changelog page.
On success it deletes the consumed fragments.

The version is stamped from src/Core/RimWorldAccessVersion.cs unless --version is
given, so the changelog heading can never drift from the mod's real version.

Usage:
  scripts/build_changelog.py [--version x.y.z] [--date YYYY-MM-DD] [--dry-run]

--dry-run prints the compiled entry and leaves all files (and fragments) untouched.
"""
import argparse
import datetime
import glob
import os
import re
import sys

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
FRAGMENT_DIR = os.path.join(REPO, "changelog.d")
VERSION_CS = os.path.join(REPO, "src", "Core", "RimWorldAccessVersion.cs")
TARGETS = [
    os.path.join(REPO, "CHANGELOG.md"),
    os.path.join(REPO, "docs-site", "docs", "reference", "changelog.md"),
]
MARKER = "<!-- BUILD_CHANGELOG_INSERT"

# Keep a Changelog categories, in display order. Filename prefix -> heading.
CATEGORY_ORDER = ["added", "changed", "deprecated", "removed", "fixed", "security"]
CATEGORY_HEADINGS = {
    "added": "Added",
    "changed": "Changed",
    "deprecated": "Deprecated",
    "removed": "Removed",
    "fixed": "Fixed",
    "security": "Security",
}


def read_version():
    with open(VERSION_CS, encoding="utf-8") as f:
        m = re.search(r'Current\s*=\s*"([^"]+)"', f.read())
    if not m:
        sys.exit("[build_changelog] ERROR: could not read version from RimWorldAccessVersion.cs")
    return m.group(1)


def collect_fragments():
    """Return ({category: [bullet_text, ...]}, consumed file paths, summary text or None).

    A fragment named summary.md is rendered as prose between the version heading
    and the category sections, paragraphs preserved, instead of as a bullet.
    """
    buckets = {}
    consumed = []
    summary = None
    paths = sorted(glob.glob(os.path.join(FRAGMENT_DIR, "*.md")))
    for path in paths:
        name = os.path.basename(path)
        if name.lower() == "readme.md":
            continue
        if name.lower() == "summary.md":
            with open(path, encoding="utf-8") as f:
                text = f.read().strip()
            if text:
                summary = text
                consumed.append(path)
            continue
        prefix = name.split("-", 1)[0].lower()
        category = prefix if prefix in CATEGORY_HEADINGS else "changed"
        with open(path, encoding="utf-8") as f:
            text = " ".join(line.strip() for line in f if line.strip())
        if not text:
            continue
        buckets.setdefault(category, []).append(text)
        consumed.append(path)
    return buckets, consumed, summary


def render_entry(version, date, buckets, summary):
    lines = [f"## [{version}] - {date}", ""]
    if summary:
        lines.append(summary)
        lines.append("")
    for category in CATEGORY_ORDER:
        items = buckets.get(category)
        if not items:
            continue
        lines.append(f"### {CATEGORY_HEADINGS[category]}")
        lines.append("")
        for item in items:
            lines.append(f"- {item}")
        lines.append("")
    return "\n".join(lines).rstrip() + "\n"


def insert_after_marker(path, entry):
    with open(path, encoding="utf-8") as f:
        content = f.read()
    lines = content.splitlines(keepends=True)
    for i, line in enumerate(lines):
        if MARKER in line:
            block = "\n" + entry + "\n"
            lines.insert(i + 1, block)
            with open(path, "w", encoding="utf-8") as f:
                f.write("".join(lines))
            return True
    return False


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--version", default=None)
    parser.add_argument("--date", default=None)
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args()

    version = args.version or read_version()
    date = args.date or datetime.date.today().isoformat()

    buckets, consumed, summary = collect_fragments()
    if not consumed:
        print("[build_changelog] No fragments in changelog.d/ — nothing to compile.")
        return

    entry = render_entry(version, date, buckets, summary)

    if args.dry_run:
        print(f"[build_changelog] Would compile {len(consumed)} fragment(s) into:\n")
        print(entry)
        return

    for target in TARGETS:
        if not os.path.exists(target):
            sys.exit(f"[build_changelog] ERROR: missing target {target}")
        if not insert_after_marker(target, entry):
            sys.exit(f"[build_changelog] ERROR: insertion marker not found in {target}")

    for path in consumed:
        os.remove(path)

    print(f"[build_changelog] Compiled {len(consumed)} fragment(s) into version {version} ({date}).")


if __name__ == "__main__":
    main()
