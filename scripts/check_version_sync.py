#!/usr/bin/env python3
"""Build-time version-sync checker for RimWorld Access.

The mod's version number lives in two places that must match, because update
detection and the vanilla mod list both read from them:

  1. src/Core/RimWorldAccessVersion.cs  ->  public const string Current = "x.y.z";
     (the runtime source of truth for What's New update detection)
  2. About/About.xml                    ->  <modVersion>x.y.z</modVersion>
     (shown in the vanilla mod list)

If they disagree, update detection misfires or the mod list is mislabeled, so
this script fails the build (exit 1) when they differ. It is wired in via the
CheckVersionSync MSBuild target.

Note: CHANGELOG.md is intentionally NOT checked here. Release entries are
compiled from changelog.d/ fragments by scripts/build_changelog.py, which stamps
the heading from the version — so the changelog version cannot drift from the
source by construction.

Usage: check_version_sync.py
"""
import os
import re
import sys

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

VERSION_CS = os.path.join(REPO, "src", "Core", "RimWorldAccessVersion.cs")
ABOUT_XML = os.path.join(REPO, "About", "About.xml")


def read(path):
    with open(path, encoding="utf-8") as f:
        return f.read()


def find(pattern, text, source):
    m = re.search(pattern, text)
    if not m:
        print(f"[check_version_sync] ERROR: could not find a version in {source}.")
        sys.exit(1)
    return m.group(1).strip()


def main():
    for path in (VERSION_CS, ABOUT_XML):
        if not os.path.exists(path):
            print(f"[check_version_sync] ERROR: missing file {path}")
            sys.exit(1)

    const_version = find(r'Current\s*=\s*"([^"]+)"', read(VERSION_CS), "RimWorldAccessVersion.cs")
    about_version = find(r"<modVersion>\s*([^<]+?)\s*</modVersion>", read(ABOUT_XML), "About.xml")

    if const_version != about_version:
        print("[check_version_sync] ERROR: mod version differs across sources:")
        print(f"    {const_version:>12}  <-  RimWorldAccessVersion.cs const")
        print(f"    {about_version:>12}  <-  About.xml <modVersion>")
        print("[check_version_sync] Make both match before building.")
        sys.exit(1)

    print(f"[check_version_sync] OK: version {const_version} (const and About.xml agree).")


if __name__ == "__main__":
    main()
