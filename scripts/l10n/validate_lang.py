#!/usr/bin/env python3
"""Validate a translated RimWorld Access Keyed XML against its English source.

Uses a real XML parser (NOT regex). A naive `<Tag>(.*?)</Tag>` regex matches the
whole `<LanguageData>...</LanguageData>` wrapper first and swallows every key,
which silently makes placeholder checks pass vacuously -- this validator avoids
that trap by walking the parsed tree.

Checks:
  1. The translation is well-formed XML.
  2. No EXTRA keys (in the translation but not English) and no DUPLICATE keys.
     Extra keys usually mean English renamed/removed a key and the translation
     is now stale.
  3. For every key present in BOTH files, the SET of distinct string.Format
     placeholders ({0}, {1}, {NamedArg}, ...) in the value matches English.
     Distinct-set (not multiset): the translation may reference an index more
     times than English -- e.g. "{0} {0_numCase ? day : days : days}" uses {0}
     twice (once to print the number, once to inflect the noun) -- which is
     valid and required for Slavic plurals. A FOREIGN index (not in English) or
     a DROPPED index (in English, absent from the translation) is still a
     failure. The value's full text is used (via itertext), so a placeholder
     anywhere in the value -- including inside a nested element -- is counted.

Missing keys are NOT a failure: RimWorld falls back to English for them, so a
partially translated file is valid. This validates the keys that ARE present.
To find which keys still need translation, use l10n_status.py.

Usage: validate_lang.py <english.xml> <translation.xml>
Prints PASS or FAIL (with details) and exits 0 / 1.
"""
import re
import sys
import xml.etree.ElementTree as ET
from collections import Counter

PLACEHOLDER = re.compile(r"\{[^{}]*\}")
# A RimWorld grammar tag like "{0_numCase ? one : few : many}" wraps an existing
# placeholder (here {0}) to inflect a counted noun. For placeholder-parity it is
# equivalent to the bare "{0}" in the English source -- normalize it back so a
# legitimate Russian/Slavic plural is not flagged as a mismatch.
NUMCASE = re.compile(r"\{(\w+)_numCase[\s?:].*\}$", re.DOTALL)


def parse(path):
    # Read as bytes so the <?xml ... encoding="utf-8"?> declaration is honored.
    with open(path, "rb") as f:
        return ET.fromstring(f.read())


def normalize(token):
    """Reduce a {N_numCase ? ...} grammar tag to its bare {N} placeholder."""
    m = NUMCASE.match(token)
    return "{" + m.group(1) + "}" if m else token


def placeholders(elem):
    """Multiset of {..} placeholders across the element's entire text.

    Grammar tags ({N_numCase ? ...}) are normalized to their wrapped {N} so a
    valid plural in the translation matches the bare {N} in English.
    """
    return Counter(normalize(t) for t in PLACEHOLDER.findall("".join(elem.itertext())))


def check(en_path, tr_path):
    """Problems found in a translation, as printable strings; empty means PASS.

    Also returns the counts main() reports, so the CLI output is unchanged:
    (problems, translated_count, en_key_count, missing_count).
    """
    try:
        en_root = parse(en_path)
    except Exception as e:
        return [f"English source is malformed: {e}"], 0, 0, 0
    try:
        tr_root = parse(tr_path)
    except Exception as e:
        return [f"MALFORMED XML in translation: {e}"], 0, 0, 0

    problems = []
    en_keys = Counter(c.tag for c in en_root)
    tr_keys = Counter(c.tag for c in tr_root)

    extra = sorted(set(tr_keys) - set(en_keys))
    dup = sorted(k for k, n in tr_keys.items() if n > 1)
    missing = sorted(set(en_keys) - set(tr_keys))  # informational, not a failure
    if extra:
        problems.append(f"EXTRA keys not in English ({len(extra)}): {extra[:20]}")
    if dup:
        problems.append(f"DUPLICATE keys ({len(dup)}): {dup[:20]}")

    en_ph = {c.tag: placeholders(c) for c in en_root}
    tr_ph = {c.tag: placeholders(c) for c in tr_root}
    mism = []
    for k in en_ph:
        if k in tr_ph and set(en_ph[k]) != set(tr_ph[k]):
            mism.append(f"{k}: EN{sorted(set(en_ph[k]))} TR{sorted(set(tr_ph[k]))}")
    if mism:
        problems.append(f"PLACEHOLDER mismatches ({len(mism)}):")
        problems.extend("    " + m for m in mism[:30])

    translated = len(set(en_keys) & set(tr_keys))
    return problems, translated, len(en_keys), len(missing)


def main():
    if len(sys.argv) != 3:
        print("usage: validate_lang.py <english.xml> <translation.xml>", file=sys.stderr)
        return 2
    problems, translated, total, missing = check(sys.argv[1], sys.argv[2])
    if problems:
        print("FAIL")
        for p in problems:
            print("  -", p)
        return 1
    tail = f", {missing} still untranslated (English fallback)" if missing else ""
    print(f"PASS  translated={translated}/{total} keys, placeholders-ok{tail}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
