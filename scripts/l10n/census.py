#!/usr/bin/env python3
"""Term census and glossary-claim verifier for RimWorld Access localization.

One scan answers two questions before a delta wave fans out to agents:

1. Which files actually need editing? Emits a work list of only the files
   with hits, so agents are spawned exactly where there is work (the
   Castilian wave spawned 19 agents; several found zero edits to make).
2. Are the glossary's claimed occurrence counts true? Compares each term's
   claimed count against the measured count and reports discrepancies
   before anyone sizes work off a wrong number.

Scans element VALUES only, via ElementTree ("".join(child.itertext())).
Comments are dropped by the parser -- this matters, because vanilla and our
own XML both carry English "<!-- ... -->" context lines, and a text-level
grep would count English comment words as translated ones.

Usage: census.py --terms <terms.json> [--lang <Folder>] [--work-list <out.md>]
                  [--json <out.json>] [--verify] [--strict]
                  [--languages-dir Languages]
       census.py --from-glossary <glossary.md> [--out terms.json]

Exit codes: 0 report produced, 1 an ERROR-class finding under --strict or a
term table that does not parse, 2 usage error or unreadable language dir.
"""
import argparse
import glob
import json
import os
import re
import sys
from collections import Counter, OrderedDict, defaultdict
import xml.etree.ElementTree as ET

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

# Left edge boundary-anchored, right edge open so inflections/plurals count --
# a plain \b is wrong here because 'cabello' must also count 'cabellos', and
# a bare substring match is wrong because 'coger' must not match 'Escoger'.
WORD_CHAR = r"[^\W\d_]"


def term_regex(term, ignore_case):
    pattern = rf"(?<!{WORD_CHAR})({re.escape(term)}{WORD_CHAR}*)"
    return re.compile(pattern, re.IGNORECASE if ignore_case else 0)


def iter_kv(xml_path):
    """Yield (key, value) pairs from a LanguageData XML file. Comments are
    dropped by ElementTree itself, closing the English-comment-counted-as-
    translated-text trap a text-level grep falls into."""
    try:
        root = ET.parse(xml_path).getroot()
    except ET.ParseError:
        return
    for child in root:
        if not isinstance(child.tag, str):
            continue  # comment/PI nodes ElementTree still yields in some paths
        yield child.tag, "".join(child.itertext())


def lang_files(lang_dir):
    files = sorted(glob.glob(os.path.join(lang_dir, "Keyed", "*.xml")))
    files += sorted(glob.glob(os.path.join(lang_dir, "DefInjected", "**", "*.xml"),
                               recursive=True))
    return files


def load_terms(path):
    with open(path, encoding="utf-8") as f:
        data = json.load(f)
    for t in data.get("terms", []):
        if "term" not in t:
            raise ValueError("a term entry is missing the required 'term' field")
        t.setdefault("replacement", None)
        t.setdefault("claimed_count", None)
        t.setdefault("files_expected", None)
        t.setdefault("case_insensitive", True)
        t.setdefault("exclude", [])
        t.setdefault("senses", None)
        t.setdefault("notes", None)
        t.setdefault("judgement", False)
    return data


def scan_language(lang_dir, terms):
    """One pass over every file/value for every term. Returns per-term
    aggregates (raw and excluded counts, variant breakdowns, per-file hits)
    plus the file list scanned."""
    files = lang_files(lang_dir)
    per_term = {}
    for t in terms:
        per_term[t["term"]] = {
            "raw_cs": 0, "raw_ci": 0,
            "excluded_cs": 0, "excluded_ci": 0,
            "variant_hits": Counter(), "variant_excluded": Counter(),
            "exclude_pattern_hits": Counter(),
            "files_raw": set(),
            "file_hits": OrderedDict(),  # rel path -> {"count": int, "hits": [...]}
        }

    compiled = {}
    for t in terms:
        term = t["term"]
        compiled[term] = {
            "ci": term_regex(term, True),
            "cs": term_regex(term, False),
            "excludes": [(re.compile(p, re.IGNORECASE), p) for p in t["exclude"]],
        }

    for path in files:
        rel = os.path.relpath(path, lang_dir)
        for key, value in iter_kv(path):
            if not value:
                continue
            for t in terms:
                term = t["term"]
                pats = compiled[term]
                ci_matches = list(pats["ci"].finditer(value))
                if not ci_matches:
                    continue
                cs_positions = {m.start() for m in pats["cs"].finditer(value)}
                agg = per_term[term]
                agg["files_raw"].add(rel)
                for m in ci_matches:
                    form = m.group(1)
                    is_cs = m.start() in cs_positions
                    window = value[max(0, m.start() - 40):min(len(value), m.end() + 40)]
                    excluded_by = None
                    for pat, pat_str in pats["excludes"]:
                        if pat.search(window):
                            excluded_by = pat_str
                            break
                    agg["raw_ci"] += 1
                    if is_cs:
                        agg["raw_cs"] += 1
                    if excluded_by:
                        agg["excluded_ci"] += 1
                        if is_cs:
                            agg["excluded_cs"] += 1
                        agg["variant_excluded"][form] += 1
                        agg["exclude_pattern_hits"][excluded_by] += 1
                    else:
                        agg["variant_hits"][form] += 1
                        fh = agg["file_hits"].setdefault(rel, {"count": 0, "hits": []})
                        fh["count"] += 1
                        fh["hits"].append({"key": key, "value": value, "form": form})
    return per_term, files


def effective(agg):
    return agg["raw_cs"] - agg["excluded_cs"], agg["raw_ci"] - agg["excluded_ci"]


def verdict_for(term_cfg, agg):
    """OK / REVIEW / ERROR / None (no claim). See slice2-census.md for the
    reasoning: over-claim (more than the unscoped maximum) is unconditionally
    wrong except for judgement terms, which are never reported as ERROR;
    under-claim is REVIEW only when the term declares a reason (exclude or
    judgement) for its numbers to disagree."""
    claimed = term_cfg["claimed_count"]
    if claimed is None:
        return None
    eff_cs, eff_ci = effective(agg)
    if claimed == eff_cs or claimed == eff_ci:
        return "OK"
    if term_cfg["judgement"]:
        return "REVIEW"
    if claimed > agg["raw_ci"]:
        return "ERROR"
    if term_cfg["exclude"]:
        return "REVIEW"
    return "ERROR"


def build_verify_report(lang, terms, per_term):
    lines = [f"=== Glossary claim verification ({lang}) ==="]
    counts = Counter()
    file_lines = []
    for t in terms:
        term = t["term"]
        agg = per_term[term]
        claimed = t["claimed_count"]
        eff_cs, eff_ci = effective(agg)
        raw_cs, raw_ci = agg["raw_cs"], agg["raw_ci"]
        verdict = verdict_for(t, agg)
        if verdict is None:
            counts["NONE"] += 1
            lines.append(f"  {'NONE':<8} {term:<15} no claim, measured "
                          f"{raw_cs} case-sensitive / {raw_ci} insensitive")
        else:
            counts[verdict] += 1
            bits = f"claimed {claimed}, measured {raw_cs} case-sensitive / {raw_ci} insensitive"
            if agg["excluded_ci"] or agg["excluded_cs"]:
                bits += f" after excludes {eff_cs} / {eff_ci}"
            lines.append(f"  {verdict:<8} {term:<15} {bits}")
            if verdict in ("REVIEW", "ERROR") and (agg["excluded_ci"] or agg["excluded_cs"]):
                excl_bits = ", ".join(
                    f"{pat!r} x{n}" for pat, n in agg["exclude_pattern_hits"].most_common())
                lines.append(f"           excludes removed {agg['excluded_ci']} ({excl_bits})")
            elif verdict == "ERROR" and not t["exclude"] and not t["judgement"]:
                lines.append("           no excludes configured. Either add an `exclude` "
                              "for the out-of-scope sense or fix the claim.")

        expected = t["files_expected"]
        if expected is not None:
            # files_expected names bare filenames (as the glossary does); measured
            # paths are relative to the language dir (e.g. "Keyed/x.xml") -- compare
            # by basename so a DefInjected/Keyed split filename still matches.
            measured_basenames = {os.path.basename(f) for f in agg["files_raw"]}
            expected_set = set(expected)
            if measured_basenames == expected_set:
                file_lines.append(f"  FILES    {term:<15} glossary lists "
                                   f"{len(expected)} files, measured {len(measured_basenames)}, identical")
            else:
                missing = sorted(expected_set - measured_basenames)
                extra = sorted(measured_basenames - expected_set)
                detail = []
                if missing:
                    detail.append(f"missing {missing}")
                if extra:
                    detail.append(f"unexpected {extra}")
                file_lines.append(f"  FILES    {term:<15} glossary lists "
                                   f"{len(expected)} files; measured {len(measured_basenames)}: "
                                   + "; ".join(detail))

    lines.extend(file_lines)
    summary = ", ".join(f"{n} {label}" for label, n in
                         (("OK", counts["OK"]), ("REVIEW", counts["REVIEW"]),
                          ("ERROR", counts["ERROR"]), ("census-only (no claim)", counts["NONE"])))
    lines.append("")
    lines.append(f"{len(terms)} term(s): {summary}.")
    has_error = counts["ERROR"] > 0
    return "\n".join(lines), has_error


def build_work_list(lang, target_lang, terms, per_term, files, lang_dir):
    all_rel = sorted(os.path.relpath(f, lang_dir) for f in files)
    file_term = defaultdict(dict)
    for t in terms:
        term = t["term"]
        for rel, fh in per_term[term]["file_hits"].items():
            file_term[rel][term] = fh

    work_files = sorted(file_term.keys())
    no_work_files = sorted(set(all_rel) - set(work_files))

    lines = [f"# Term census -- {lang} -> {target_lang}"]
    lines.append(f"Scanned {len(all_rel)} files in Languages/{lang}. "
                  f"{len(terms)} terms. {len(work_files)} files have work.")
    lines.append("")

    for rel in work_files:
        fmap = file_term[rel]
        file_hit_total = sum(fh["count"] for fh in fmap.values())
        term_word = "term" if len(fmap) == 1 else "terms"
        lines.append(f"## {rel}  ({len(fmap)} {term_word}, {file_hit_total} hits)")
        lines.append("")
        for t in terms:
            term = t["term"]
            if term not in fmap:
                continue
            fh = fmap[term]
            variants = Counter(h["form"] for h in fh["hits"])
            variant_str = ", ".join(f"{form} {n}" for form, n in variants.most_common())
            label = f"{term} -> {t['replacement']}" if t["replacement"] else term
            marker = "   JUDGEMENT REQUIRED" if t["judgement"] else ""
            lines.append(f"### {label}   ({fh['count']} hits: {variant_str}){marker}")
            note = t["notes"] or t["senses"]
            if note:
                lines.append(note)
            shown = fh["hits"][:8]
            for h in shown:
                lines.append(f'- {h["key"]}: "{h["value"]}"')
            remaining = len(fh["hits"]) - len(shown)
            if remaining > 0:
                lines.append(f"- ... {remaining} more")
            lines.append("")

    lines.append(f"## Files with NO work (not to be assigned): {len(no_work_files)}")
    lines.append(", ".join(no_work_files))
    return "\n".join(lines), file_term, no_work_files


def build_json_report(terms, file_term, no_work_files):
    files_out = {}
    for rel, fmap in file_term.items():
        term_out = {}
        for t in terms:
            term = t["term"]
            if term not in fmap:
                continue
            fh = fmap[term]
            term_out[term] = {"replacement": t["replacement"], "hits": fh["hits"]}
        files_out[rel] = term_out
    return {"files": files_out, "no_work": no_work_files}


# ---------------------------------------------------------------------------
# --from-glossary seeding

BOLD_RE = re.compile(r"\*\*(.+?)\*\*")
JUDGEMENT_TRIGGERS = ("judgement", "only where", "only change", "check each site")
XML_FILE_RE = re.compile(r"`([A-Za-z0-9_.]+\.xml)`")

# `term`[ / `Alt`] -> `repl`[ / `Alt`] -- **N occurrences**  (checklist shape)
CHECKLIST_CLAUSE_RE = re.compile(
    r"`([^`]+)`(?:\s*/\s*`[^`]+`)?\s*→\s*`([^`]+)`(?:\s*/\s*`[^`]+`)?"
    r"\s*—\s*\**\s*(\d+)\s*occurrences?\b", re.UNICODE)
# `term`[ / `Alt`] -> `repl`[ / `Alt`]  (no count -- for judgement lines whose
# per-term count is folded into a shared "N and M occurrences" annotation)
CHECKLIST_PAIR_RE = re.compile(
    r"`([^`]+)`(?:\s*/\s*`[^`]+`)?\s*→\s*`([^`]+)`(?:\s*/\s*`[^`]+`)?", re.UNICODE)


def strip_bold(cell):
    return BOLD_RE.sub(r"\1", cell).strip().strip("`").strip()


def from_glossary(path, out_path):
    text = open(path, encoding="utf-8").read()
    terms = OrderedDict()  # lowercased term -> record

    # Shape 1: delta-table rows "| English | Our <Src> | <Target> | citation |".
    for line in text.splitlines():
        stripped = line.strip()
        if not stripped.startswith("|") or not stripped.endswith("|"):
            continue
        cols = [c.strip() for c in stripped.strip("|").split("|")]
        if len(cols) != 4:
            continue
        if cols[0] in ("English", "") or re.fullmatch(r"-+:?", cols[0].replace(" ", "")):
            continue
        term_raw = strip_bold(cols[1])
        repl_raw = strip_bold(cols[2])
        if not term_raw or not repl_raw or "`" not in cols[3]:
            continue  # citation column anchors this as the vocabulary table, not another table
        key = term_raw.lower()
        if key not in terms:
            terms[key] = {
                "term": term_raw, "replacement": repl_raw, "claimed_count": None,
                "files_expected": [], "case_insensitive": True, "exclude": [],
                "senses": None, "notes": None, "judgement": False,
            }

    # Shape 2: the checklist section's bullet lines.
    heading = re.search(r"^#+.*checklist.*$", text, re.IGNORECASE | re.MULTILINE)
    if heading:
        checklist_text = text[heading.end():]
        blocks = re.split(r"\n\s*-\s+(?=`)", checklist_text)[1:]
        for block in blocks:
            trigger_hit = any(p in block.lower() for p in JUDGEMENT_TRIGGERS)
            files_expected = XML_FILE_RE.findall(block)
            claimed_terms = set()
            for m in CHECKLIST_CLAUSE_RE.finditer(block):
                term_raw, repl_raw, count = m.group(1), m.group(2), int(m.group(3))
                key = term_raw.lower()
                claimed_terms.add(key)
                if key not in terms:
                    terms[key] = {
                        "term": term_raw, "replacement": repl_raw, "claimed_count": count,
                        "files_expected": files_expected, "case_insensitive": True,
                        "exclude": [], "senses": None, "notes": None, "judgement": False,
                    }
                else:
                    terms[key]["claimed_count"] = count
                    if not terms[key]["files_expected"]:
                        terms[key]["files_expected"] = files_expected
            if trigger_hit:
                for m in CHECKLIST_PAIR_RE.finditer(block):
                    term_raw, repl_raw = m.group(1), m.group(2)
                    key = term_raw.lower()
                    if key not in terms:
                        terms[key] = {
                            "term": term_raw, "replacement": repl_raw, "claimed_count": None,
                            "files_expected": files_expected, "case_insensitive": True,
                            "exclude": [], "senses": None, "notes": None, "judgement": False,
                        }
                    terms[key]["judgement"] = True
                    terms[key]["notes"] = "TODO: review before use -- glossary text near this " \
                        "term mentions judgement/scoping; confirm the count and add `exclude` " \
                        "patterns or hand-scope this term before assigning agents."

    result = {
        "lang": "TODO-fill-in-source-dialect-folder",
        "target_lang": "TODO-fill-in-target-dialect-folder",
        "source": f"{os.path.relpath(path, REPO)} (auto-extracted by --from-glossary, best-effort)",
        "terms": list(terms.values()),
    }

    print(f"census --from-glossary: best-effort extraction from {path}. "
          f"REVIEW BEFORE USE -- lang/target_lang are placeholders, claimed_count "
          f"may be missing or wrong, files_expected is a guess. "
          f"{len(terms)} term(s) extracted, "
          f"{sum(1 for t in terms.values() if t['judgement'])} marked judgement TODO.")

    if os.path.exists(out_path) and out_path != "-":
        print(f"error: {out_path} already exists; refusing to overwrite without review. "
              f"Pass a different --out path.", file=sys.stderr)
        return 1
    with open(out_path, "w", encoding="utf-8") as f:
        json.dump(result, f, indent=2, ensure_ascii=False)
        f.write("\n")
    print(f"wrote {out_path}")
    return 0


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("--terms")
    ap.add_argument("--lang")
    ap.add_argument("--work-list")
    ap.add_argument("--json")
    ap.add_argument("--verify", action="store_true")
    ap.add_argument("--strict", action="store_true")
    ap.add_argument("--languages-dir", default=os.path.join(REPO, "Languages"))
    ap.add_argument("--from-glossary")
    ap.add_argument("--out", default="terms.json")
    args = ap.parse_args()

    if args.from_glossary:
        return from_glossary(args.from_glossary, args.out)

    if not args.terms:
        print("error: --terms is required (or use --from-glossary)", file=sys.stderr)
        return 2

    try:
        data = load_terms(args.terms)
    except (json.JSONDecodeError, ValueError, OSError) as e:
        print(f"error: could not parse terms file {args.terms}: {e}", file=sys.stderr)
        return 1

    terms = data.get("terms", [])
    lang = args.lang or data.get("lang")
    target_lang = data.get("target_lang", "?")
    if not lang:
        print("error: no --lang given and terms file has no 'lang'", file=sys.stderr)
        return 2

    lang_dir = os.path.join(args.languages_dir, lang)
    if not os.path.isdir(lang_dir):
        print(f"error: language directory not found: {lang_dir}", file=sys.stderr)
        return 2

    per_term, files = scan_language(lang_dir, terms)

    work_list, file_term, no_work_files = build_work_list(
        lang, target_lang, terms, per_term, files, lang_dir)

    if args.work_list:
        with open(args.work_list, "w", encoding="utf-8") as f:
            f.write(work_list + "\n")
        print(f"wrote {args.work_list}")
    else:
        print(work_list)

    if args.json:
        report = build_json_report(terms, file_term, no_work_files)
        with open(args.json, "w", encoding="utf-8") as f:
            json.dump(report, f, indent=2, ensure_ascii=False)
        print(f"wrote {args.json}")

    has_error = False
    if args.verify:
        report_text, has_error = build_verify_report(lang, terms, per_term)
        print()
        print(report_text)

    if args.strict and has_error:
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
