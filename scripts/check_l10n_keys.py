#!/usr/bin/env python3
"""Build-time localization key checker for RimWorld Access.

Catches the two failure classes the Localized-type compile guard cannot see:

1. FAKE keys (build ERROR): a string literal passed to .Translate() or .Loc()
   that exists neither in our Languages/English/Keyed XML nor in the game's
   own (Core + DLC) English Keyed XML. Such a call compiles and even renders
   plausible English (the key name), but resolves to the raw key in every
   language. Example bug: "Unrestricted".Translate() — the real vanilla key
   is NoAreaAllowed.

2. ORPHAN keys (build WARNING): a key defined in our English Keyed XML that
   no source file references. This is the signature of code that was
   reverted/rewritten (e.g. by a merge) without its localized announcement —
   the announcement silently became hardcoded English. Keys reached through
   string-prefix concatenation ("RimWorldAccess.X." + suffix) are honored.

3. MALFORMED XML (build ERROR): any Languages/**/*.xml that a real XML
   parser rejects. RimWorld's loader discards such a file WHOLE and silently
   — every key in it then speaks as its raw key name — while this script's
   own regex scanner would happily keep reading keys out of it (the July 2026
   incident: a "--" inside an XML comment, illegal in XML, invalidated the
   entire Compat file in-game yet passed both the regex key scan and the
   ratchets).

4. BARE CONCATENATION FRAGMENTS (build ERROR): an English Keyed value with
   no {0}-style placeholder and stray leading/trailing whitespace — the
   FloorSuffix/MalePrefix/PlacedPrefix pattern of gluing a fragment onto
   another string in C#. This freezes an English word order that other
   languages need to reorder; the fix is a whole-phrase template key.

Usage: check_l10n_keys.py [--game-dir <RimWorld install dir>]
The game dir is optional; without it (e.g. CI) the fake-key check only
validates against our own keys it can prove fake (RimWorldAccess.* prefixed),
since vanilla keys cannot be verified.
"""
import argparse
import glob
import os
import re
import sys
import xml.etree.ElementTree as ET

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

KEY_RE = re.compile(r"<([A-Za-z0-9_.\-]+)>")
LIT_CALL_RE = re.compile(r'"([A-Za-z0-9_.]+)"\s*\.\s*(?:Translate|Loc)\s*[(<]')

# Keys that are intentionally referenced although no Keyed XML defines them.
# LearnToPlay: vanilla MainMenuDrawer's own dead backward-compat fallback
# ("Tutorial".CanTranslate() ? "Tutorial" : "LearnToPlay"), mirrored exactly.
ALLOWED_FAKES = {"LearnToPlay"}
DYN_PREFIX_RE = re.compile(r'"(RimWorldAccess[A-Za-z0-9_.]*\.)"\s*\+')

# Reviewed list-joiner/hint fragments: legitimately bare (no placeholder,
# leading/trailing whitespace) because they are concatenated between two
# other fragments in C#, not spoken as a standalone phrase.
ALLOWED_BARE_FRAGMENTS = {
    "RimWorldAccess.Work.Task.SkillListAndSeparator",
    "RimWorldAccess.Inspection.Gizmo.Status.GrowthRewardsAnd",
    "RimWorldAccess.Work.NameList.ThreePlusLastJoiner",
}


def keys_from(pattern):
    keys = set()
    for f in glob.glob(pattern):
        text = open(f, encoding="utf-8").read()
        text = re.sub(r"<!--.*?-->", "", text, flags=re.S)
        for m in KEY_RE.finditer(text):
            if m.group(1) != "LanguageData":
                keys.add(m.group(1))
    return keys


def is_annotation_shaped(value):
    stripped = value.strip()
    return stripped.startswith(("(", "-", "•")) or stripped.endswith((":", ",", "."))


def bare_fragment_offenders(pattern):
    """English Keyed values with no {0}-style placeholder AND stray leading/
    trailing whitespace: the FloorSuffix/MalePrefix/PlacedPrefix pattern of
    gluing a fragment onto another string in C#, which freezes an English
    word order that French/Spanish/Turkish/Russian need to reorder."""
    offenders = []
    for f in glob.glob(pattern):
        try:
            root = ET.parse(f).getroot()
        except ET.ParseError:
            continue  # malformed XML already reported above
        for child in root:
            key = child.tag
            if key in ALLOWED_BARE_FRAGMENTS:
                continue
            value = child.text or ""
            # Whitespace-only values are layout padding, never word order.
            if not value.strip():
                continue
            if "{" in value or value == value.strip() or is_annotation_shaped(value):
                continue
            offenders.append((key, os.path.relpath(f, REPO)))
    return offenders


def strip_comments(line):
    stripped = line.lstrip()
    if stripped.startswith("//"):  # includes /// doc comments
        return ""
    return line


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--game-dir", default=None)
    args = ap.parse_args()

    # Failure class 3: RimWorld's loader silently discards any XML file a real
    # parser rejects, so validate every language file with one before trusting
    # the regex key scan below.
    malformed = []
    for f in glob.glob(os.path.join(REPO, "Languages/**/*.xml"), recursive=True):
        try:
            ET.parse(f)
        except ET.ParseError as e:
            malformed.append((os.path.relpath(f, REPO), str(e)))
    if malformed:
        for path, err in malformed:
            print(f"error RWA-L10N: malformed XML (RimWorld will silently "
                  f"discard the WHOLE file in-game): {path}: {err}")
        return 1

    our_keys = keys_from(os.path.join(REPO, "Languages/English/Keyed/*.xml"))

    vanilla_keys = None
    if args.game_dir:
        for data_name in ("data", "Data"):
            data_dir = os.path.join(args.game_dir, data_name)
            if os.path.isdir(data_dir):
                vanilla_keys = set()
                for mod_keyed in glob.glob(
                        os.path.join(data_dir, "*/Languages/English/Keyed")):
                    vanilla_keys |= keys_from(os.path.join(mod_keyed, "*.xml"))
                break

    src_files = glob.glob(os.path.join(REPO, "src/**/*.cs"), recursive=True)
    src_text_all = ""
    fakes = {}
    for f in src_files:
        lines = open(f, encoding="utf-8").readlines()
        src_text_all += "".join(lines)
        for i, raw in enumerate(lines, 1):
            line = strip_comments(raw)
            for m in LIT_CALL_RE.finditer(line):
                k = m.group(1)
                if k in our_keys or k in ALLOWED_FAKES:
                    continue
                if vanilla_keys is not None:
                    if k in vanilla_keys:
                        continue
                elif not k.startswith("RimWorldAccess"):
                    continue  # cannot verify vanilla keys without game data
                fakes.setdefault(k, []).append(
                    f"{os.path.relpath(f, REPO)}:{i}")

    dyn_prefixes = set(DYN_PREFIX_RE.findall(src_text_all))
    orphans = [k for k in sorted(our_keys)
               if k not in src_text_all
               and not any(k.startswith(p) for p in dyn_prefixes)]

    for k in orphans:
        print(f"warning RWA-L10N: orphaned key (defined in English XML, "
              f"unreferenced in src — reverted code?): {k}")

    # Failure class 4: a fragment concatenated onto another string in C#
    # (leading/trailing whitespace, no placeholder) freezes an English word
    # order that other languages need to reorder — should be a template key.
    bare_fragments = bare_fragment_offenders(
        os.path.join(REPO, "Languages/English/Keyed/*.xml"))
    for k, path in bare_fragments:
        print(f"error RWA-L10N: bare concatenation fragment '{k}' in {path} "
              f"(no placeholder, leading/trailing whitespace glues English "
              f"word order onto another string — translations can't "
              f"reorder it; use a whole-phrase template key instead)")

    if fakes:
        for k in sorted(fakes):
            sites = "; ".join(fakes[k][:5])
            print(f"error RWA-L10N: fake translation key '{k}' "
                  f"(exists in no Keyed XML, will speak raw key name in "
                  f"every language) at {sites}")

    if fakes or bare_fragments:
        return 1

    print(f"check_l10n_keys: OK — {len(our_keys)} keys, "
          f"{len(orphans)} orphan warning(s), 0 fake keys, "
          f"0 bare concatenation fragments"
          + ("" if vanilla_keys is not None
             else " (vanilla keys not checked: no game dir)"))
    return 0


if __name__ == "__main__":
    sys.exit(main())
