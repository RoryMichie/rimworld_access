#!/usr/bin/env python3
"""One-command per-language localization gate for RimWorld Access.

Replaces the hand-rolled end-of-wave ritual (a loop of validate_lang over 67
files, then ad-hoc forbidden-byte scans, then ad-hoc DefInjected diffing) with
one command that runs nine checks per language and returns a single
PASS/FAIL plus a compact report.

Every punctuation and plural policy is per-language, read from
scripts/l10n/languages.json, because the shipped languages disagree: Russian
legitimately uses _numCase 231 times and guillemets/em dashes natively, while
Ukrainian is Slavic but its LanguageWorker does not support _numCase at all
(it resolves to an empty string in speech). A single global forbidden-byte
list would fail two shipped languages outright. Only the invisible-space
family (NBSP, NNBSP, thin space, ZWSP, BOM) is a universal ban.

Usage:
    python3 scripts/l10n/gate.py --lang French
    python3 scripts/l10n/gate.py --all
    python3 scripts/l10n/gate.py --lang Russian --explain
    python3 scripts/l10n/gate.py --all --strict

Exit codes: 0 all gated languages passed, 1 at least one failed,
2 usage error or missing English source.
"""
import argparse
import glob
import json
import os
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import l10n_status  # noqa: E402
import validate_lang  # noqa: E402

POLICY_PATH = os.path.join(HERE, "languages.json")

SOURCE_LANG = "English"
DEFAULT_REF = "SpanishLatin"

# Both live inline in Defs/ConceptDefs/ for English; every other language
# translates them under DefInjected/. Order and \n\n paragraph-break counts
# must match the reference language exactly (see check_definjected_parity).
DEFINJECTED_FILES = [
    os.path.join("DefInjected", "ConceptDef", "Concepts_RimWorldAccess.xml"),
    os.path.join("DefInjected", "RimWorldAccess.ConceptHelpOverrideDef",
                 "Overrides_Vanilla.xml"),
]

PUNCTUATION_CHARS = "«»“”‘’—–…"
CHAR_NAMES = {
    "«": "left guillemet", "»": "right guillemet",
    "“": "left curly quote", "”": "right curly quote",
    "‘": "left curly single quote", "’": "right curly single quote",
    "—": "em dash", "–": "en dash", "…": "ellipsis",
}

# Heuristic-only (--english-heuristic): meaningful only where the language's
# own script is not Latin, so an all-ASCII-letters value is actually a signal.
NON_LATIN_SCRIPT_LANGS = {"Russian", "Ukrainian", "ChineseSimplified"}
HOTKEY_WORDS = ["Page Up", "Page Down", "Ctrl", "Shift", "Alt", "Tab", "Enter",
                "Space"] + [f"F{n}" for n in range(1, 13)]
BRAND_WORDS = ["RimWorld Access", "RimWorld", "Harmony", "Steam", "NVDA",
               "JAWS", "Tolk"]


def load_policy():
    with open(POLICY_PATH, encoding="utf-8") as f:
        return json.load(f)


def display_root(languages_dir):
    """Anchor for relpath display so reported paths read as Languages/... even
    when --languages-dir points at a scratch copy outside the repo."""
    return os.path.dirname(os.path.abspath(languages_dir))


def context_slice(value, idx, width=60):
    """A width-character window around value[idx], with that character
    bracketed so the offending char is visible in a one-line report."""
    half = width // 2
    start = max(0, idx - half)
    end = min(len(value), idx + half)
    marked = value[start:idx] + "[" + value[idx] + "]" + value[idx + 1:end]
    return marked.replace("\n", "\\n")


def cap_list(items, cap=20):
    if len(items) <= cap:
        return ", ".join(items)
    return ", ".join(items[:cap]) + f", ... and {len(items) - cap} more"


def collect_trees(lang_dir, disp_root):
    """Parse every XML file under a language directory once, so every later
    check reuses the same trees instead of reparsing.

    Returns (trees, malformed): trees maps a display-relative path to its
    parsed root for well-formed files; malformed lists (relpath, error) for
    files a real parser rejects -- RimWorld's loader discards such a file
    WHOLE and silently, so a parse failure makes the rest of that file's
    findings meaningless and later checks skip it.
    """
    trees = {}
    malformed = []
    for path in sorted(glob.glob(os.path.join(lang_dir, "**", "*.xml"), recursive=True)):
        rel = os.path.relpath(path, disp_root)
        try:
            trees[rel] = ET.parse(path).getroot()
        except ET.ParseError as e:
            malformed.append((rel, str(e)))
    return trees, malformed


def iter_keys(trees):
    """Yield (tag, value) for every top-level entry across a language's parsed
    trees, in a stable (file, document) order. Values use itertext() so a
    placeholder inside a nested element is included; comments are already
    excluded because ElementTree drops them during iteration."""
    for rel in sorted(trees):
        for child in trees[rel]:
            yield child.tag, "".join(child.itertext())


def parity(ref_path, tgt_path):
    """DefInjected structural parity: child tag order (not just membership --
    a reordered file still loads but stops being diffable) and per-value
    literal "\\n" paragraph-break counts (these files use the two-character
    literal, not real newlines; RimWorld converts them at load)."""
    ref = [(c.tag, "".join(c.itertext()).count("\\n"))
           for c in ET.parse(ref_path).getroot()]
    tgt = [(c.tag, "".join(c.itertext()).count("\\n"))
           for c in ET.parse(tgt_path).getroot()]
    problems = []
    for i, (r, t) in enumerate(zip(ref, tgt)):
        if r[0] != t[0]:
            problems.append(f"entry {i}: order diverges, ref has {r[0]}, target has {t[0]}")
            break  # everything after a shift is noise
        if r[1] != t[1]:
            problems.append(f"{r[0]}: {r[1]} paragraph break(s) in ref, {t[1]} in target")
    if len(ref) != len(tgt):
        problems.append(f"entry count differs: ref {len(ref)}, target {len(tgt)}")
    return problems


# --- The nine checks, each returning (severity, check_name, location, message) tuples ---

def check_keyed_validation(languages_dir, lang, english_keyed_files, malformed_set, disp_root):
    findings = []
    for fname in english_keyed_files:
        en_path = os.path.join(languages_dir, SOURCE_LANG, "Keyed", fname)
        tr_path = os.path.join(languages_dir, lang, "Keyed", fname)
        if not os.path.isfile(tr_path):
            continue  # English fallback; check_completeness reports this
        if os.path.relpath(tr_path, disp_root) in malformed_set:
            continue  # already reported by check_well_formed
        problems, *_ = validate_lang.check(en_path, tr_path)
        for p in problems:
            findings.append(("FAIL", "keyed-validation", fname, p))
    return findings


def check_completeness(lang_dir, lang, source, strict):
    findings = []
    try:
        # l10n_status reparses every Keyed file itself and does not catch a
        # parse error -- a malformed file is already reported by
        # check_well_formed, so degrade gracefully instead of crashing.
        status = l10n_status.status_for(Path(lang_dir), source)
    except ET.ParseError:
        return [("FAIL", "completeness", lang,
                 "cannot compute: a Keyed file failed to parse (see well-formedness)")]
    if status["total_extra"] > 0:
        extra = sorted({k for info in status["files"].values() for k in info["extra"]})
        findings.append(("FAIL", "completeness", lang,
                         f"{status['total_extra']} extra key(s) not in English "
                         f"(English renamed/removed them, translation is stale): "
                         f"{cap_list(extra)}"))
    if status["total_missing"] > 0:
        missing = [m["key"] for info in status["files"].values() for m in info["missing"]]
        sev = "FAIL" if strict else "WARN"
        findings.append((sev, "completeness", lang,
                         f"{status['total_missing']} key(s) missing (safe English "
                         f"fallback, not yet complete): {cap_list(missing)}"))
    return findings


def check_definjected_parity(languages_dir, lang, ref, malformed_set, disp_root):
    findings = []
    for relfile in DEFINJECTED_FILES:
        ref_path = os.path.join(languages_dir, ref, relfile)
        tgt_path = os.path.join(languages_dir, lang, relfile)
        if os.path.relpath(tgt_path, disp_root) in malformed_set:
            continue  # already reported by check_well_formed
        if not os.path.isfile(ref_path):
            findings.append(("FAIL", "definjected-parity", relfile,
                             f"reference language has no {relfile}; pick another --ref"))
            continue
        if not os.path.isfile(tgt_path):
            findings.append(("FAIL", "definjected-parity", relfile, "file not found"))
            continue
        for p in parity(ref_path, tgt_path):
            findings.append(("FAIL", "definjected-parity", relfile, p))
    return findings


def check_forbidden_chars(trees, universal):
    findings = []
    forbidden = {chr(int(cp[2:], 16)): name
                 for cp, name in universal["forbidden_chars"].items()}
    for tag, val in iter_keys(trees):
        for ch, name in forbidden.items():
            idx = val.find(ch)
            if idx != -1:
                cp = "U+%04X" % ord(ch)
                findings.append(("FAIL", "forbidden-chars", tag,
                                 f"contains {cp} ({name}): {context_slice(val, idx)}"))
    return findings


def check_punctuation(trees, lang, allowed, strict):
    findings = []
    allowed_set = set(allowed)
    for tag, val in iter_keys(trees):
        present = sorted({ch for ch in val if ch in PUNCTUATION_CHARS} - allowed_set)
        if present:
            names = ", ".join(f"'{ch}' ({CHAR_NAMES[ch]})" for ch in present)
            sev = "FAIL" if strict else "WARN"
            findings.append((sev, "punctuation", tag, f"{names} not allowed for {lang}"))
    return findings


def check_numcase(trees, lang, numcase_allowed):
    if numcase_allowed:
        return []
    offenders = [tag for tag, val in iter_keys(trees) if "_numCase" in val]
    if not offenders:
        return []
    return [("FAIL", "numcase-policy", lang,
             f"_numCase is not supported by this language's LanguageWorker and "
             f"resolves to an EMPTY string in speech -- use a flat plural form. "
             f"{len(offenders)} key(s): {cap_list(offenders, 10)}")]


def check_artifact_tags(lang_dir, tag_re, disp_root):
    """Raw file text, not the parsed tree -- the point is to catch a leaked
    tag even when it also broke well-formedness, not only when it didn't."""
    findings = []
    for path in sorted(glob.glob(os.path.join(lang_dir, "**", "*.xml"), recursive=True)):
        text = open(path, encoding="utf-8", errors="replace").read()
        rel = os.path.relpath(path, disp_root)
        for m in tag_re.finditer(text):
            findings.append(("FAIL", "artifact-tags", rel, f"leaked tool tag: {m.group(0)}"))
    return findings


def check_quote_balance(trees, quote_pairs, strict):
    findings = []
    for open_ch, close_ch in quote_pairs:
        if open_ch == close_ch:
            continue  # symmetric delimiter, cannot balance-count
        open_total = close_total = 0
        for _tag, val in iter_keys(trees):
            open_total += val.count(open_ch)
            close_total += val.count(close_ch)
        if open_total != close_total:
            sev = "FAIL" if strict else "WARN"
            findings.append((sev, "quote-balance", "(whole language)",
                             f"'{open_ch}' count={open_total} vs '{close_ch}' "
                             f"count={close_total} -- unbalanced"))
    return findings


def check_english_heuristic(trees, lang):
    if lang not in NON_LATIN_SCRIPT_LANGS:
        return []
    findings = []
    for tag, val in iter_keys(trees):
        val = val.strip()
        if not val or not any(c.isalpha() for c in val):
            continue
        if not all(not c.isalpha() or c.isascii() for c in val):
            continue  # has non-ASCII letters, so it is translated
        remainder = val
        for word in HOTKEY_WORDS + BRAND_WORDS:
            remainder = remainder.replace(word, "")
        if not any(c.isalpha() for c in remainder):
            continue  # only hotkeys/brand names remain
        findings.append(("WARN", "english-heuristic", tag,
                         f"value looks entirely untranslated (heuristic, may be a "
                         f"false positive): {val[:80]}"))
    return findings


def gate_one(lang, languages_dir, disp_root, lang_policies, universal, tag_re,
             ref, strict, english_heuristic, english_keyed_files, source):
    """Run all nine checks for one language. Returns a report dict."""
    if lang not in lang_policies:
        return {"language": lang, "findings": [
            ("FAIL", "policy", lang,
             "no policy in scripts/l10n/languages.json (add one before gating)")],
            "skipped": [], "notes": None}

    lang_dir = os.path.join(languages_dir, lang)
    if not os.path.isdir(lang_dir):
        return {"language": lang, "findings": [
            ("FAIL", "policy", lang, f"language folder not found under {languages_dir}/")],
            "skipped": [], "notes": None}

    pol = lang_policies[lang]
    is_english = lang == SOURCE_LANG
    is_ref = lang == ref
    findings = []
    skipped = []

    # 1. XML well-formedness -- first, since every later check needs the tree.
    trees, malformed = collect_trees(lang_dir, disp_root)
    malformed_set = {rel for rel, _ in malformed}
    for rel, err in malformed:
        findings.append(("FAIL", "well-formedness", rel, err))

    # 2. Keyed validation
    if is_english:
        skipped.append("keyed-validation")
    else:
        findings.extend(check_keyed_validation(
            languages_dir, lang, english_keyed_files, malformed_set, disp_root))

    # 3. Completeness
    findings.extend(check_completeness(lang_dir, lang, source, strict))

    # 4. DefInjected structural parity
    if is_english:
        skipped.append("definjected-parity")
    elif is_ref:
        skipped.append("definjected-parity (reference language)")
    else:
        findings.extend(check_definjected_parity(
            languages_dir, lang, ref, malformed_set, disp_root))

    # 5. Universal forbidden characters
    findings.extend(check_forbidden_chars(trees, universal))

    # 6. Per-language punctuation policy
    findings.extend(check_punctuation(trees, lang, pol["punctuation_allowed"], strict))

    # 7. _numCase policy
    findings.extend(check_numcase(trees, lang, pol["numcase_allowed"]))

    # 8. Tool-artifact tags
    findings.extend(check_artifact_tags(lang_dir, tag_re, disp_root))

    # 9. Quote balance
    findings.extend(check_quote_balance(trees, pol["quote_pairs"], strict))

    # Optional: leftover English heuristic
    if english_heuristic:
        findings.extend(check_english_heuristic(trees, lang))

    return {"language": lang, "findings": findings, "skipped": skipped,
            "notes": pol.get("notes")}


def print_report(report, explain, english_keyed_count, total_en_keys):
    lang = report["language"]
    print(f"=== {lang} ===")
    if explain and report["notes"]:
        print(f"  notes: {report['notes']}")

    skipped = report["skipped"]
    if skipped:
        if lang == SOURCE_LANG:
            print(f"  skipped: {', '.join(skipped)} (source language)")
        else:
            print(f"  skipped: {', '.join(skipped)}")

    findings = report["findings"]
    fails = [f for f in findings if f[0] == "FAIL"]
    warns = [f for f in findings if f[0] == "WARN"]

    if not findings:
        if lang == SOURCE_LANG:
            print(f"  PASS  {english_keyed_count} keyed files, {total_en_keys} keys "
                  f"(source language)")
        else:
            print(f"  PASS  {english_keyed_count} keyed files, "
                  f"{total_en_keys}/{total_en_keys} keys, definjected parity ok")
        return len(fails), len(warns)

    for sev, check, location, message in findings:
        print(f"  {sev} {check}  {location}: {message}")
    if fails:
        print(f"  FAIL with {len(fails)} failure(s), {len(warns)} warning(s)")
    else:
        print(f"  PASS with {len(warns)} warning(s)")
    return len(fails), len(warns)


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--lang", action="append", dest="langs",
                    help="gate one language folder (repeatable)")
    ap.add_argument("--all", action="store_true", help="gate every folder under Languages/")
    ap.add_argument("--ref", default=DEFAULT_REF,
                    help="reference language for DefInjected structural parity "
                         f"(default: {DEFAULT_REF})")
    ap.add_argument("--strict", action="store_true",
                    help="promote every warning to a failure")
    ap.add_argument("--english-heuristic", action="store_true",
                    help="enable the noisy leftover-untranslated-English scan")
    ap.add_argument("--languages-dir", default="Languages",
                    help="path to the mod's Languages/ folder (default: ./Languages)")
    ap.add_argument("--explain", action="store_true",
                    help="print each gated language's policy notes")
    args = ap.parse_args()

    policy = load_policy()
    universal = policy["universal"]
    lang_policies = policy["languages"]
    tag_re = re.compile(r"</?(?:" + "|".join(re.escape(t) for t in universal["artifact_tags"])
                        + r")\b")

    languages_dir = args.languages_dir
    disp_root = display_root(languages_dir)
    english_keyed_dir = Path(languages_dir) / SOURCE_LANG / "Keyed"
    if not english_keyed_dir.is_dir():
        print(f"error: no English source at {english_keyed_dir}", file=sys.stderr)
        return 2

    english_keyed_files = sorted(p.name for p in english_keyed_dir.glob("*.xml"))
    source = {fname: {"order": l10n_status.key_order(english_keyed_dir / fname),
                      "text": l10n_status.key_text(english_keyed_dir / fname)}
              for fname in english_keyed_files}
    total_en_keys = sum(len(s["order"]) for s in source.values())

    if args.langs:
        targets = args.langs
    else:
        targets = sorted(d for d in os.listdir(languages_dir)
                         if os.path.isdir(os.path.join(languages_dir, d)))

    print(f"l10n gate: {len(targets)} language(s), ref={args.ref}\n")

    total_fail = total_warn = 0
    failed_langs = []
    for lang in targets:
        report = gate_one(lang, languages_dir, disp_root, lang_policies, universal, tag_re,
                          args.ref, args.strict, args.english_heuristic,
                          english_keyed_files, source)
        n_fail, n_warn = print_report(report, args.explain,
                                      len(english_keyed_files), total_en_keys)
        total_fail += n_fail
        total_warn += n_warn
        if n_fail:
            failed_langs.append(lang)
        print()

    if failed_langs:
        print(f"Result: FAIL -- {len(failed_langs)} of {len(targets)} language(s) failed "
              f"({', '.join(failed_langs)}). {total_warn} warning(s) total.")
        return 1
    print(f"Result: PASS -- {len(targets)} of {len(targets)} language(s) passed. "
          f"{total_warn} warning(s) total.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
