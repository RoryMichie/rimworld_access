#!/usr/bin/env python3
"""Shadow-copy ratchet for RimWorld Access.

The no-shadow-copies rule at grep level: a State or Scope must not hold a
private editable copy of a vanilla value across keypresses and write it back at
the end. The distinguishing shape is a value that lives in OUR object across
keypresses while vanilla's equivalent lives one IMGUI frame on the stack.

A file is a candidate if it declares a type whose name ends in `State` or
`Scope`. A field declared there is flagged when all three of these hold:

- Seed: the field is an assignment target in a method whose name starts with
  `Open`, `Begin` or `Start`, from a right-hand side that is not a literal,
  `null`, `default` or a bare `new` — i.e. the copy is being taken from
  somewhere.
- Mutate: the field is an assignment target (compound assignment and `++`/`--`
  included) in a method whose name starts with one of the key-handler verbs
  `Adjust`, `Select`, `Step`, `Jump`, `Handle`, `Increase`, `Decrease`, `Toggle`
  or `Cycle` — the value is being edited across keypresses.
- Commit: the field is read, not assigned, in a method whose name starts with
  `Confirm`, `Accept`, `Apply` or `Commit` — that read is the write-back.

SHADOW_BASELINE grandfathers known sites with a one-line reason each and is
shrink-only: an entry that no longer matches is itself a failure, so the set
stays exact. It is empty today.

This is an approximation, deliberately. It slices method bodies with a
brace-counting scanner and matches fields by name; it does not parse C#. The
semantic version of the same rule is the RWA0001 Roslyn analyzer, which resolves
symbols and can see through locals and helper calls. False negatives here are
acceptable; false positives are not, hence the exemptions below.

Usage: check_shadow_copies.py
"""
import glob
import os
import re
import sys

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

SEED_VERBS = ("Open", "Begin", "Start")
MUTATE_VERBS = ("Adjust", "Select", "Step", "Jump", "Handle", "Increase",
                "Decrease", "Toggle", "Cycle")
COMMIT_VERBS = ("Confirm", "Accept", "Apply", "Commit")

# Struct-typed fields only, for the member-write case (`range.min = x`). Writing
# a member of a VALUE-typed field rewrites the field itself, which is the shadow
# copy. Writing a member of a REFERENCE-typed field writes vanilla's own live
# object through a reference we hold, the live posture the rule asks for, and the
# posture the doctrine actually asks for (BillConfigState.bill,
# StorageSettingsMenuState.currentSettings). Unknown structs fall out as false
# negatives, which is the direction this ratchet errs in.
VALUE_TYPES = frozenset({
    "int", "uint", "long", "ulong", "short", "ushort", "byte", "sbyte",
    "float", "double", "decimal", "bool", "char",
    "FloatRange", "IntRange", "QualityRange", "Color", "Color32",
    "Vector2", "Vector3", "Rect", "IntVec2", "IntVec3", "Rot4", "CellRect",
})

TYPE_DECL_RE = re.compile(r'\b(?:class|struct)\s+(\w+)')
FIELD_DECL_RE = re.compile(
    r'^[ \t]*(?:(?:private|internal|public|protected|static|readonly|volatile'
    r'|new)\s+)+'
    r'([A-Za-z_][\w.]*(?:\s*<[^;=()]*>)?\??(?:\s*\[\s*\])?)\s+'
    r'([A-Za-z_]\w*)\s*(?:=[^;]*)?;', re.M)
CALL_SITE_RE = re.compile(r'\b(\w+)\s*\(')
# Right-hand sides that are not a copy of anything.
INERT_RHS_RE = re.compile(
    r'^(?:null|true|false|string\.Empty|default(?:\([^()]*\))?|-?\d[\w.]*'
    r'|"[^"]*"|\'[^\']*\''
    r'|new\s+[\w.<>\[\], ]+\(\s*\)(?:\s*\{\s*\})?)$')
# Statement keywords that also read as `name (` before a block.
BLOCK_KEYWORDS = frozenset({
    "if", "while", "for", "foreach", "switch", "catch", "using", "lock",
    "fixed", "return", "new", "do", "else", "case", "typeof", "nameof",
    "sizeof", "default", "when", "yield",
})

# Empty, and meant to stay that way. Census 12's four provisional sites
# (RangeEditMenuState's three range fields, QuantityMenuState.selectedQuantity)
# were replumbed to live write-through before this ratchet
# landed, so there is nothing left to grandfather. A new entry here needs a
# one-line reason keyed `relative/path.cs::fieldName`, and a stale one fails.
SHADOW_BASELINE = {}


def blank_comments_and_literals(text):
    """Replaces comment and string/char literal contents with spaces, keeping
    every offset and newline intact so brace counting and line-anchored field
    matching both stay honest."""
    out = []
    i, n = 0, len(text)
    while i < n:
        ch = text[i]
        if ch == "/" and text.startswith("//", i):
            while i < n and text[i] != "\n":
                out.append(" ")
                i += 1
        elif ch == "/" and text.startswith("/*", i):
            while i < n and not text.startswith("*/", i):
                out.append("\n" if text[i] == "\n" else " ")
                i += 1
            out.append("  ")
            i += 2
        elif ch == '"':
            verbatim = i > 0 and text[i - 1] == "@"
            out.append('"')
            i += 1
            while i < n:
                if not verbatim and text[i] == "\\":
                    out.append("  ")
                    i += 2
                    continue
                if text[i] == '"':
                    if verbatim and text.startswith('""', i):
                        out.append("  ")
                        i += 2
                        continue
                    break
                out.append("\n" if text[i] == "\n" else " ")
                i += 1
            out.append('"')
            i += 1
        elif ch == "'":
            out.append("'")
            i += 1
            while i < n and text[i] != "'":
                if text[i] == "\\":
                    out.append("  ")
                    i += 2
                    continue
                out.append(" ")
                i += 1
            out.append("'")
            i += 1
        else:
            out.append(ch)
            i += 1
    return "".join(out)


def method_bodies(text):
    """Yields (name, body) for every `name(...) { ... }` in already-blanked
    text. Statement keywords are filtered out; an object initializer or a local
    function can still slip through, which only ever costs a false negative
    because the verb prefixes below are what select a body."""
    n = len(text)
    for match in CALL_SITE_RE.finditer(text):
        name = match.group(1)
        if name in BLOCK_KEYWORDS:
            continue
        i = match.end() - 1
        depth = 0
        while i < n:
            if text[i] == "(":
                depth += 1
            elif text[i] == ")":
                depth -= 1
                if depth == 0:
                    break
            i += 1
        if i >= n:
            continue
        j = i + 1
        while j < n and text[j].isspace():
            j += 1
        if text.startswith("where", j):
            j = text.find("{", j)
            if j == -1:
                continue
        if j >= n or text[j] != "{":
            continue
        depth = 0
        k = j
        while k < n:
            if text[k] == "{":
                depth += 1
            elif text[k] == "}":
                depth -= 1
                if depth == 0:
                    break
            k += 1
        yield name, text[j:k + 1]


def target_re(field, allow_member):
    member = r'(?:\s*\.\s*\w+)*' if allow_member else ""
    return (r'(?<![\w.])(?:this\s*\.\s*)?' + re.escape(field) + member)


def seed_sources(body, field):
    pattern = re.compile(target_re(field, False) + r'\s*=(?!=)')
    for match in pattern.finditer(body):
        end = body.find(";", match.end())
        yield body[match.end():end if end != -1 else len(body)].strip()


def is_mutated(body, field, allow_member):
    head = target_re(field, allow_member)
    patterns = (
        head + r'\s*(?:=(?!=)|[-+*/%|&^]=|<<=|>>=)',
        head + r'\s*(?:\+\+|--)',
        r'(?:\+\+|--)\s*' + target_re(field, False) + r'\b',
    )
    return any(re.search(p, body) for p in patterns)


def is_read(body, field):
    for match in re.finditer(target_re(field, False) + r'\b', body):
        if not re.match(r'\s*=(?!=)', body[match.end():match.end() + 3]):
            return True
    return False


def exemption(field_type, field_name):
    lowered = field_name.lower()
    if "typeahead" in lowered or "buffer" in lowered:
        return "typeahead buffer"          # in-flight search text, never written back
    if "pending" in lowered or "armed" in lowered:
        return "one-shot pending slot"     # ListingRowCapture's armed-channel shape
    if field_name.endswith(("Index", "Cursor", "Scroll")):
        return "cursor/selection/scroll"   # display model, rebuilt not committed
    if field_type == "int" and "index" in lowered:
        return "cursor index"
    if field_type == "string":
        return "in-flight text entry"      # commits on Enter, same as a vanilla text field
    return None


def scan_file(path):
    with open(path, encoding="utf-8") as handle:
        text = blank_comments_and_literals(handle.read())
    if not any(name.endswith(("State", "Scope"))
               for name in TYPE_DECL_RE.findall(text)):
        return []
    fields = {m.group(2): m.group(1).strip()
              for m in FIELD_DECL_RE.finditer(text)}
    if not fields:
        return []
    bodies = list(method_bodies(text))

    hits = []
    for field_name, field_type in fields.items():
        if exemption(field_type, field_name):
            continue
        seeded = next(
            (name for name, body in bodies if name.startswith(SEED_VERBS)
             and any(not INERT_RHS_RE.match(rhs)
                     for rhs in seed_sources(body, field_name))),
            None)
        if seeded is None:
            continue
        allow_member = field_type in VALUE_TYPES
        mutated = next(
            (name for name, body in bodies if name.startswith(MUTATE_VERBS)
             and is_mutated(body, field_name, allow_member)),
            None)
        if mutated is None:
            continue
        committed = next(
            (name for name, body in bodies if name.startswith(COMMIT_VERBS)
             and is_read(body, field_name)),
            None)
        if committed is None:
            continue
        hits.append((field_name, seeded, mutated, committed))
    return hits


def main():
    failures = []
    matched_baseline = set()
    for path in sorted(glob.glob(os.path.join(REPO, "src/**/*.cs"),
                                 recursive=True)):
        rel = os.path.relpath(path, REPO).replace(os.sep, "/")
        for field_name, seeded, mutated, committed in scan_file(path):
            key = f"{rel}::{field_name}"
            if key in SHADOW_BASELINE:
                matched_baseline.add(key)
                continue
            failures.append(
                f"error RWA-SHADOW: {key} looks like a shadow copy of vanilla "
                f"state — seeded in {seeded}, edited across keypresses in "
                f"{mutated}, written back from {committed}. Write through to "
                f"the vanilla object on every keypress instead. "
                f"If this is a legitimate shape the exemptions "
                f"miss, widen them in check_shadow_copies.py and record the "
                f"shape in a comment.")

    for key in sorted(set(SHADOW_BASELINE) - matched_baseline):
        failures.append(
            f"error RWA-SHADOW: SHADOW_BASELINE entry '{key}' no longer "
            f"matches (the field was replumbed, renamed, or removed) — delete "
            f"it from check_shadow_copies.py, the set must stay exact.")

    for message in failures:
        print(message)
    if failures:
        return 1

    print(f"check_shadow_copies: OK — no State/Scope field is seeded, edited "
          f"across keypresses and written back on commit "
          f"({len(SHADOW_BASELINE)} baselined)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
