#!/usr/bin/env python3
"""Report localization coverage of every shipped language against English.

English (`<languages-dir>/English/Keyed/`) is the source of truth. For each
other language this reports, per file, the keys present in English but missing
in that language (the keys still needing translation, with their English text)
and any extra keys the language has that English no longer does (usually a sign
English renamed or removed a key).

RimWorld falls back to English for any missing key, so a missing key is never a
crash -- it just means that string is still spoken in English for that language.
This tool is what turns "I added some English strings, now catch up the other
languages" into a concrete, per-language, per-file, per-key work list.

Run it from the mod repo root (so the default `Languages` path resolves), or
pass --languages-dir explicitly.

Usage:
    python3 l10n_status.py                       # summary: which languages/files/keys
    python3 l10n_status.py --text                # also print each missing key + English text
    python3 l10n_status.py --json                # machine-readable (for a translator agent)
    python3 l10n_status.py --lang "Ukrainian (Українська)"          # one language
    python3 l10n_status.py --languages-dir path/to/Languages       # custom location

Exit code is 0 always (this is a report, not a gate); use --fail-on-missing to
make it exit non-zero when any language has missing keys (handy in CI).
"""
import argparse
import json
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

SOURCE_LANG = "English"


def keyed_files(lang_dir: Path):
    """Map of filename -> absolute path for a language's Keyed XMLs."""
    keyed = lang_dir / "Keyed"
    if not keyed.is_dir():
        return {}
    return {p.name: p for p in sorted(keyed.glob("*.xml"))}


def _root(path: Path):
    """Parse a Keyed XML and return the <LanguageData> root element.

    ElementTree drops comments during iteration, so child elements are exactly
    the translation keys -- no regex pitfalls (a naive <Tag>(.*)</Tag> regex
    matches the whole <LanguageData>...</LanguageData> wrapper and swallows every
    key, and also miscounts tag-like tokens inside comments).
    """
    return ET.fromstring(path.read_bytes())


def key_order(path: Path):
    """Translation keys in document order (the root's element children)."""
    return [child.tag for child in _root(path)]


def key_text(path: Path):
    """Map of key -> inner English text, for showing translators what to translate."""
    return {child.tag: (child.text or "").strip() for child in _root(path)}


def language_dirs(lang_root: Path):
    """Every language folder under the languages dir except the English source."""
    return sorted(
        d for d in lang_root.iterdir()
        if d.is_dir() and d.name != SOURCE_LANG
    )


def status_for(lang_dir: Path, source: dict):
    """Per-file missing/extra keys for one language vs the English source.

    `source` maps filename -> {"order": [keys], "text": {key: english}}.
    """
    lang_files = keyed_files(lang_dir)
    files = {}
    total_missing = total_extra = 0
    for fname, src in source.items():
        src_order, src_text = src["order"], src["text"]
        src_set = set(src_order)
        tgt_set = set(key_order(lang_files[fname])) if fname in lang_files else set()
        # Preserve English order so the work list reads naturally top-to-bottom.
        missing = [{"key": k, "english": src_text.get(k, "")}
                   for k in src_order if k not in tgt_set]
        extra = sorted(tgt_set - src_set)
        if missing or extra:
            files[fname] = {
                "missing": missing,
                "extra": extra,
                "file_untranslated": fname not in lang_files,
            }
        total_missing += len(missing)
        total_extra += len(extra)
    return {
        "language": lang_dir.name,
        "total_missing": total_missing,
        "total_extra": total_extra,
        "files": files,
    }


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--languages-dir", default="Languages",
                    help="path to the mod's Languages/ folder (default: ./Languages)")
    ap.add_argument("--json", action="store_true", help="emit machine-readable JSON")
    ap.add_argument("--text", action="store_true",
                    help="print each missing key with its English source text")
    ap.add_argument("--lang", help="limit to a single language folder name")
    ap.add_argument("--fail-on-missing", action="store_true",
                    help="exit non-zero if any language has missing keys")
    args = ap.parse_args()

    lang_root = Path(args.languages_dir)
    src_dir = lang_root / SOURCE_LANG
    src_files = keyed_files(src_dir)
    if not src_files:
        print(f"No English source found at {src_dir / 'Keyed'}", file=sys.stderr)
        return 2
    source = {fname: {"order": key_order(p), "text": key_text(p)}
              for fname, p in src_files.items()}
    source_key_count = sum(len(s["order"]) for s in source.values())

    targets = language_dirs(lang_root)
    if args.lang:
        targets = [d for d in targets if d.name == args.lang]
        if not targets:
            print(f"No language folder named {args.lang!r}", file=sys.stderr)
            return 2

    reports = [status_for(d, source) for d in targets]

    if args.json:
        print(json.dumps({"source_keys": source_key_count, "languages": reports},
                         ensure_ascii=False, indent=2))
        return 1 if (args.fail_on_missing and any(r["total_missing"] for r in reports)) else 0

    print(f"English source: {len(src_files)} files, {source_key_count} keys")

    needing = [r for r in reports if r["total_missing"] or r["total_extra"]]
    if not needing:
        print("\nAll languages complete. Nothing to translate.")
        for r in reports:
            print(f"  ✓ {r['language']}")
        return 0

    print("\nLanguages needing work:")
    for r in needing:
        bits = []
        if r["total_missing"]:
            bits.append(f"{r['total_missing']} to translate")
        if r["total_extra"]:
            bits.append(f"{r['total_extra']} stale/extra")
        print(f"  ✗ {r['language']}: {', '.join(bits)}")
    for r in reports:
        if r not in needing:
            print(f"  ✓ {r['language']}: complete")

    for r in needing:
        print(f"\n=== {r['language']} ===")
        for fname, info in r["files"].items():
            tag = " (entire file untranslated)" if info["file_untranslated"] else ""
            counts = []
            if info["missing"]:
                counts.append(f"{len(info['missing'])} to translate")
            if info["extra"]:
                counts.append(f"{len(info['extra'])} stale/extra")
            print(f"  {fname}: {', '.join(counts)}{tag}")
            for m in info["missing"]:
                print(f"      {m['key']}")
                if args.text:
                    print(f"          EN: {m['english']}")
            for k in info["extra"]:
                print(f"      [stale/extra] {k}")

    return 1 if (args.fail_on_missing and any(r["total_missing"] for r in reports)) else 0


if __name__ == "__main__":
    sys.exit(main())
