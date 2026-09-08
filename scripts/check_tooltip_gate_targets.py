#!/usr/bin/env python3
"""Drift ratchet for the generated tooltip-gate target registry.

Regenerates src/Shell/Sync/TooltipGateTargets.Generated.cs into a buffer and
compares it with the committed file, so a hand edit or a game update that adds
or removes a tooltip-registering type fails here instead of silently shrinking
the gate's coverage.

Exits 0 with a skip message when the decompiled tree is absent, so the ratchet
still runs on a machine without the decompile.

Usage: check_tooltip_gate_targets.py
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import gen_tooltip_gate_targets as gen


def main():
    if not os.path.isdir(gen.DECOMPILED):
        print("SKIP: decompiled tree not found at " + gen.DECOMPILED)
        return 0
    if not os.path.isfile(gen.OUTPUT):
        print("FAIL: " + gen.OUTPUT + " is missing; run scripts/gen_tooltip_gate_targets.py")
        return 1
    expected = gen.render(gen.collect())
    with open(gen.OUTPUT, "r", encoding="utf-8") as handle:
        actual = handle.read()
    if expected == actual:
        print("OK: tooltip gate registry matches the decompiled tree "
              "(" + str(expected.count('",')) + " types)")
        return 0
    expected_names = set(l.strip() for l in expected.splitlines() if l.strip().startswith('"'))
    actual_names = set(l.strip() for l in actual.splitlines() if l.strip().startswith('"'))
    print("FAIL: " + gen.OUTPUT + " is out of date; run scripts/gen_tooltip_gate_targets.py")
    for name in sorted(expected_names - actual_names):
        print("  missing: " + name.strip('",'))
    for name in sorted(actual_names - expected_names):
        print("  stale:   " + name.strip('",'))
    return 1


if __name__ == "__main__":
    sys.exit(main())
