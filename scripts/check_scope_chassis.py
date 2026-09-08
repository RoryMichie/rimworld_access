#!/usr/bin/env python3
"""Scope-chassis ratchet for RimWorld Access.

A game screen must be a real screen on the shared chassis. Every
window, dialog, menu and windowless overlay screen derives from ScreenScope (or
TreeRegionScope), so navigation, typeahead and announcement grammar exist once,
correctly, for all of them — never as a hand-rolled copy per screen. A bespoke
FocusScope subclass satisfies check_screen_coverage.py (the window IS looked at)
while quietly reimplementing half the chassis, which is how 37 screens drifted
apart and how the AssignScope typeahead fiasco happened.

So: every class in src/ declared as `class X : FocusScope` — direct derivation,
extra interfaces allowed — must be one of

1. absent, the desired end state (a screen on the chassis derives from
   ScreenScope or TreeRegionScope, which this regex does not match);
2. in CHASSIS_EXEMPT, the genuine non-screens, each with its reason recorded;
3. in CHASSIS_BASELINE, the shrink-only grandfather set of screens still
   awaiting migration.

CHASSIS_BASELINE only ever shrinks: an entry whose class no longer derives
directly from FocusScope (migrated, renamed, or deleted) is itself a failure
telling the author to delete the entry, matching check_shadow_copies.py's
SHADOW_BASELINE mechanics. Nothing may be ADDED to it — a new screen starts on
the chassis.

This is a grep, deliberately: it reads declarations out of comment-stripped
source and does not resolve a type hierarchy, so an indirect derivation through
some other bespoke base is a false negative. Direct derivation is the shape the
this retires, and it is the shape a new screen would be written in.

Usage: check_scope_chassis.py
"""
import glob
import os
import re
import sys

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

SCOPE_DECL_RE = re.compile(r'\bclass\s+(\w+)\s*:\s*FocusScope\b')

# The genuine non-screens, with the reason each is not a screen. An exemption is
# a standing claim: re-justify it or delete it, never silently forget it.
CHASSIS_EXEMPT = {
    "ScreenScope": "the chassis itself — every screen's base",
    "MapScope": "ambient base, not a screen",
    "WorldScope": "ambient base, not a screen",
    "TextSessionScope": "dispatcher infrastructure shadow",
    "GoToScope": "map overlay cursor tool",
    "ScannerSearchScope": "map overlay cursor tool; deliberate named exception",
    "TargetingScope": "cursor/camera mode over the map surface, non-modal by design",
    "PlacementScope": "cursor/camera mode over the map surface, non-modal by design",
    "MapToolScope": "cursor/camera mode over the map surface, non-modal by design",
    "ViewingModeScope": "cursor/camera mode over the map surface, non-modal by design",
    "RoutePlannerScope": "world-map cursor tool",
    "VfRoutePlannerScope": "world-map cursor tool",
    "ShelfLinkingScope": "non-modal map-cursor toggle whose arrows must keep reaching the map",
    "TransportPodSelectionScope": "non-modal map-cursor toggle whose arrows must keep reaching the map",
    "GizmoScope": "non-modal HUD-bar overlay whose load-bearing pass-throughs "
                  "(Ctrl+Alt+Enter inspection, Alt+arrows, F1-F4) are incompatible "
                  "with a modal screen chassis",
    "QuantityMenuScope": "value picker whose Up/Down ARE the control — no rows to navigate",
    "RerollScope": "input blackout during pawn reroll, not a surface",
}

# The screens found off the chassis when this ratchet was set. Shrink-only: a
# that migrates a screen deletes its entry here in the same change.
CHASSIS_BASELINE = frozenset()


def blank_comments(text):
    """Replaces comment contents with spaces, keeping every offset and newline
    intact so line numbers stay honest (check_shadow_copies.py's helper, minus
    the literal blanking this ratchet has no use for)."""
    out = []
    i, n = 0, len(text)
    while i < n:
        if text.startswith("//", i):
            while i < n and text[i] != "\n":
                out.append(" ")
                i += 1
        elif text.startswith("/*", i):
            while i < n and not text.startswith("*/", i):
                out.append("\n" if text[i] == "\n" else " ")
                i += 1
            out.append("  ")
            i += 2
        else:
            out.append(text[i])
            i += 1
    return "".join(out)


def scan_file(path):
    """Yields (line number, class name) for every direct FocusScope derivation."""
    with open(path, encoding="utf-8") as handle:
        text = blank_comments(handle.read())
    for match in SCOPE_DECL_RE.finditer(text):
        yield text.count("\n", 0, match.start()) + 1, match.group(1)


def main():
    failures = []
    matched_baseline = set()
    for path in sorted(glob.glob(os.path.join(REPO, "src/**/*.cs"), recursive=True)):
        rel = os.path.relpath(path, REPO).replace(os.sep, "/")
        for line, name in scan_file(path):
            if name in CHASSIS_EXEMPT:
                continue
            if name in CHASSIS_BASELINE:
                matched_baseline.add(name)
                continue
            failures.append(
                f"error RWA-CHASSIS: {rel}:{line}: {name} derives directly from "
                f"FocusScope. A game screen must be a real screen on the shared "
                f"chassis — derive from ScreenScope (or TreeRegionScope) so it "
                f"gets the one correct copy of navigation, typeahead and "
                f"announcement grammar. If this is genuinely not a screen, add it "
                f"to CHASSIS_EXEMPT in check_scope_chassis.py with the reason.")

    for name in sorted(CHASSIS_BASELINE - matched_baseline):
        failures.append(
            f"error RWA-CHASSIS: CHASSIS_BASELINE entry '{name}' no longer "
            f"derives directly from FocusScope (migrated, renamed, or removed) — "
            f"delete it from check_scope_chassis.py, the set must stay exact.")

    for message in failures:
        print(message)
    if failures:
        return 1

    print(f"check_scope_chassis: OK — every screen scope is on the ScreenScope "
          f"chassis or accounted for ({len(CHASSIS_BASELINE)} awaiting "
          f"migration, {len(CHASSIS_EXEMPT)} exempt non-screens)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
