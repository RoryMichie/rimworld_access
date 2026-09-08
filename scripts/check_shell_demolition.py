#!/usr/bin/env python3
"""Input-plumbing demolition ratchet for RimWorld Access.

Locks in the outcome of the input-plumbing demolition so it can't silently
regress:

1. Dead-symbol guard: the retired ladder/isolation/registry symbols
   (UnifiedKeyboardPatch, KeyboardIsolationPatch, MenuOverlayGuard,
   IsAnyAccessibilityMenuActive, TypeaheadDispatcher, TypeaheadConsumerRegistry,
   PollActivations) must have zero live (non-comment) references in src/, and
   likewise the dead features deleted with that plumbing:
   SettlementBrowserState, QuestLocationsBrowserState, UninstallControlState,
   and the frozen SettlementBrowserLegacyPatch island. Doc comments and
   historical prose may still name them; this only checks code.
2. `src/Input/UnifiedKeyboardPatch.cs` itself must not exist.
3. Raw-text-field guard: no live code may call a native text-editing widget
   (Widgets.TextField/TextArea/TextFieldNumeric/DelayedTextField, GUI.TextField/
   TextArea) — every editable field routes through the shared browse/edit
   TextFieldEditSession / TextInputController instead. The sole exception is
   ImeInputHost's offscreen GUI.TextField (CJK/IME composition capture).
4. Cancel/accept blocker ratchet: counts Harmony patch classes on
   `Window.OnCancelKeyPressed` / `Window.OnAcceptKeyPressed` (or the `Page`
   equivalents) that do NOT delegate to the shared router pair
   (`WindowCancelKeyRouterPatch` / `WindowAcceptKeyRouterPatch` /
   `PageCancelKeyRouterPatch` / `PageAcceptKeyRouterPatch`). New non-delegating
   blockers should route through the router pair instead (see
   CLAUDE.md's keyboard-shell section); this only fails if the count grows.
5. OwnsAccept opt-in ratchet: `FocusScope.OwnsAccept` defaults to false (unlike
   `OwnsCancel`, which defaults true), so a bare `FocusScope` subclass that
   claims menus.activate over a kept-open vanilla window (closeOnAccept still
   true) is exposed to the Work-tab Enter bug:
   vanilla's own Accept pass can close the window before the dispatcher sees
   the key. Every file declaring a class extending `FocusScope` directly and
   claiming SharedMenuGrammar.Activate / "menus.activate" must either contain
   `override bool OwnsAccept` or be listed in OWNS_ACCEPT_BASELINE with a
   verdict comment. New offenders must add an explicit override (true, or a
   documented false); stale baseline entries (files that gained an override,
   or no longer match) must be removed to keep the set exact.

Scope note: this lint locks the demolition metrics that are cheap to verify.
The larger `static bool IsActive -> 0` / `== KeyCode. -> 0` state-conversion
sweep is deliberately out of scope.

Usage: check_shell_demolition.py
"""
import glob
import os
import re
import sys

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

DEAD_SYMBOLS = [
    "UnifiedKeyboardPatch",
    "KeyboardIsolationPatch",
    "MenuOverlayGuard",
    "IsAnyAccessibilityMenuActive",
    "TypeaheadDispatcher",
    "TypeaheadConsumerRegistry",
    "PollActivations",
    # Dead features deleted with the retired plumbing:
    "SettlementBrowserState",
    "QuestLocationsBrowserState",
    "UninstallControlState",
    "SettlementBrowserLegacyPatch",
    # The legacy 2D table engine, retired by the table-model T2 migrations
    # (2026-07-12): all six pawn tables now ride ScreenScope table regions.
    "TabularMenuHelper",
]

ROUTER_TWIN_NAMES = (
    "WindowCancelKeyRouterPatch",
    "WindowAcceptKeyRouterPatch",
    "PageCancelKeyRouterPatch",
    "PageAcceptKeyRouterPatch",
)

# Text-field model ratchet:
# every editable text field routes through the shared browse/edit
# TextFieldEditSession / TextInputController, never a raw native editing widget.
# A natively focused Widgets.TextField/GUI.TextField swallows the arrow keys the
# shell needs and gives no cursor review, which is exactly the model this campaign
# removed. These patterns must have zero call sites in live code.
RAW_TEXTFIELD_RES = [
    re.compile(r'\bWidgets\.TextField\s*\('),
    re.compile(r'\bWidgets\.TextArea\s*\('),
    re.compile(r'\bWidgets\.TextAreaScrollable\s*\('),
    re.compile(r'\bWidgets\.TextFieldNumeric\b'),
    re.compile(r'\bWidgets\.DelayedTextField\s*\('),
    re.compile(r'\bGUI\.TextField\s*\('),
    re.compile(r'\bGUI\.TextArea\s*\('),
]
# The sole sanctioned native field: the offscreen GUI.TextField that
# ImeInputHost draws to capture CJK/IME composition (there is no accessible
# substitute — IME needs a real focused Unity field). Nothing else may.
RAW_TEXTFIELD_EXEMPT = frozenset({
    "src/Input/TextInput/ImeInputHost.cs",
})

# Current count of non-delegating Cancel/Accept blocker sites (the ratchet
# ceiling). Update this only after a deliberate review of the new offender
# list this script prints — growth here means a new screen wrote its own
# OnCancelKeyPressed/OnAcceptKeyPressed blocker instead of routing through
# WindowKeyRouter/PageKeyRouter (CLAUDE.md's keyboard-shell section explains why).
# Offenders at the time this ratchet was set,
# 28 sites across 18 files (a "site" is one attributed class or method; a
# few files carry both a Cancel and an Accept blocker):
#   src/Biotech/XenogermPatch.cs, GrowthMomentPatch.cs (x2)
#   src/UI/MenuSearchState.cs (x2)
#   src/Anomaly/EntityCodexPatch.cs (x2)
#   src/Combat/TargetConfirmDialogGuard.cs (x2: Dialog_MessageBox + Window)
#   src/Input/TextInput/TextInputModalProtectPatch.cs (x3)
#   src/Trade/SellableItemsNavigationPatch.cs, TradeNavigationPatch.cs (x2)
#   src/Shell/Screens/NamePawnScope.Game.cs
#   src/TransportPods/TransportPodPatch.cs
#   src/World/CaravanFormationPatch.cs (x2)
#   src/Portals/PortalPatch.cs
#   src/Animals/AutoSlaughterPatch.cs (x2)
#   src/History/HistoryPatch.cs (x2)
#   src/MainMenu/StartingSitePatch.cs
#   src/Rituals/DryadCastePatch.cs, RitualPatch.cs (x2)
NON_DELEGATING_BLOCKER_CEILING = 28

# OwnsAccept opt-in ratchet baseline (check 5): bare FocusScope subclasses that
# claim menus.activate over a coexisting vanilla window but are verified safe
# without an explicit OwnsAccept override. Each verdict was confirmed by
# reading the opener/coexistence path.
OWNS_ACCEPT_BASELINE = {
    "src/Shell/Screens/CaravanOverlayScopes.Game.cs",       # host dialogs covered by legacy Trade/Caravan accept blockers
    "src/Shell/Screens/GizmoScope.Game.cs",                 # windowless, inspect pane is not a WindowStack window
    # RoutePlannerScope/ViewingModeScope do not literally claim
    # SharedMenuGrammar.Activate (only doc-comment mentions), so they fall
    # out of the claim condition entirely, same as MapScope/WorldScope.
}

FOCUS_SCOPE_SUBCLASS_RE = re.compile(
    r'class\s+\w+\s*:\s*(?:sealed\s+|partial\s+)?FocusScope\b')
MENUS_ACTIVATE_RE = re.compile(
    r'SharedMenuGrammar\.Activate|"menus\.activate"')
OWNS_ACCEPT_OVERRIDE_RE = re.compile(r'override\s+bool\s+OwnsAccept\b')

HARMONY_TARGET_RE = re.compile(
    r'\[HarmonyPatch\(\s*typeof\(\s*\w+\s*\)\s*,\s*'
    r'"(OnCancelKeyPressed|OnAcceptKeyPressed)"\s*\)\]')
ATTR_ONLY_LINE_RE = re.compile(r'^\s*\[[^\]]*\]\s*$')
CLASS_DECL_RE = re.compile(r'\bclass\s+(\w+)')
METHOD_DECL_RE = re.compile(r'\b(\w+)\s*\(')


def strip_comments(text):
    """Line-based comment stripper: drops `//...` tails and `///` doc lines.
    Not a full tokenizer (a `//` inside a string literal would be misread),
    but that pattern does not occur in this codebase's Harmony attribute
    lines or symbol usage sites."""
    out_lines = []
    for line in text.splitlines():
        idx = line.find("//")
        out_lines.append(line[:idx] if idx != -1 else line)
    return "\n".join(out_lines)


def all_src_files():
    return glob.glob(os.path.join(REPO, "src/**/*.cs"), recursive=True)


def check_dead_symbols():
    failures = []
    word_res = {sym: re.compile(r'\b' + re.escape(sym) + r'\b')
                for sym in DEAD_SYMBOLS}
    for f in all_src_files():
        stripped = strip_comments(open(f, encoding="utf-8").read())
        for sym, pattern in word_res.items():
            if pattern.search(stripped):
                rel = os.path.relpath(f, REPO)
                failures.append(f"error RWA-SHELL: dead symbol '{sym}' still "
                                 f"referenced in live code: {rel}")
    return failures


def check_raw_text_fields():
    failures = []
    for f in all_src_files():
        rel = os.path.relpath(f, REPO).replace(os.sep, "/")
        if rel in RAW_TEXTFIELD_EXEMPT:
            continue
        stripped = strip_comments(open(f, encoding="utf-8").read())
        for pattern in RAW_TEXTFIELD_RES:
            if pattern.search(stripped):
                failures.append(
                    f"error RWA-SHELL: raw native text field ({pattern.pattern}) "
                    f"in live code: {rel} — route editable text through the shared "
                    f"browse/edit TextFieldEditSession / TextInputController "
                    f"instead (CLAUDE.md's keyboard-shell section).")
    return failures


def check_ukp_file_gone():
    path = os.path.join(REPO, "src/Input/UnifiedKeyboardPatch.cs")
    if os.path.exists(path):
        return ["error RWA-SHELL: src/Input/UnifiedKeyboardPatch.cs still "
                "exists; the retired ladder is deleted whole, not tombstoned"]
    return []


def find_enclosing_class(lines, attr_line_index):
    """Scans backward from a Harmony attribute line for the nearest
    surrounding `class Name` declaration (used to qualify bare-method-style
    patches, where the attribute sits directly on a static method rather
    than on its own nested patch class)."""
    for i in range(attr_line_index - 1, -1, -1):
        m = CLASS_DECL_RE.search(lines[i])
        if m:
            return m.group(1)
    return None


def find_blocker_classes(text):
    """Yields one label per live OnCancelKeyPressed / OnAcceptKeyPressed
    Harmony patch site in the (already comment-stripped) text. Both idioms
    used in this codebase are handled: a dedicated nested patch class
    (`[HarmonyPatch(...)] public static class Foo { ... }`, yields "Foo"),
    and the attribute applied directly to a static method
    (`[HarmonyPatch(...)] [HarmonyPrefix] public static bool Bar(...)`,
    yields "EnclosingClass.Bar" so distinct methods in the same file still
    count separately)."""
    lines = text.splitlines()
    for i, line in enumerate(lines):
        if not HARMONY_TARGET_RE.search(line):
            continue
        j = i + 1
        while j < len(lines) and (ATTR_ONLY_LINE_RE.match(lines[j])
                                   or lines[j].strip() == ""):
            j += 1
        if j >= len(lines):
            continue
        decl_line = lines[j]
        cls = CLASS_DECL_RE.search(decl_line)
        if cls:
            yield cls.group(1)
            continue
        method = METHOD_DECL_RE.search(decl_line)
        if method:
            enclosing = find_enclosing_class(lines, i)
            yield f"{enclosing}.{method.group(1)}" if enclosing else method.group(1)


def check_blocker_ratchet():
    offenders = []
    for f in all_src_files():
        stripped = strip_comments(open(f, encoding="utf-8").read())
        classes = list(find_blocker_classes(stripped))
        if not classes:
            continue
        references_router = any(name in stripped for name in ROUTER_TWIN_NAMES)
        if references_router:
            continue
        rel = os.path.relpath(f, REPO)
        for cls in classes:
            offenders.append(f"{rel} ({cls})")

    messages = []
    count = len(offenders)
    if offenders:
        messages.append("info RWA-SHELL: non-delegating cancel/accept "
                         f"blocker classes ({count}):")
        for o in offenders:
            messages.append(f"  - {o}")
    if count > NON_DELEGATING_BLOCKER_CEILING:
        messages.append(
            f"error RWA-SHELL: non-delegating cancel/accept blocker count "
            f"({count}) exceeds the ratchet ceiling "
            f"({NON_DELEGATING_BLOCKER_CEILING}). Route new blockers through "
            f"WindowKeyRouter/PageKeyRouter (CLAUDE.md's keyboard-shell section) "
            f"instead of a standalone Harmony patch, or update the ceiling "
            f"after a deliberate review of the new offender above.")
        return messages, True
    return messages, False


def check_owns_accept_opt_in():
    failures = []
    seen_baseline = set()
    for f in all_src_files():
        rel = os.path.relpath(f, REPO).replace(os.sep, "/")
        stripped = strip_comments(open(f, encoding="utf-8").read())
        if not FOCUS_SCOPE_SUBCLASS_RE.search(stripped):
            continue
        if not MENUS_ACTIVATE_RE.search(stripped):
            continue
        has_override = bool(OWNS_ACCEPT_OVERRIDE_RE.search(stripped))
        in_baseline = rel in OWNS_ACCEPT_BASELINE
        if in_baseline:
            seen_baseline.add(rel)
        if has_override:
            if in_baseline:
                failures.append(
                    f"error RWA-SHELL: {rel} now has an explicit OwnsAccept "
                    f"override — remove it from OWNS_ACCEPT_BASELINE "
                    f"(check_shell_demolition.py), the set must stay exact.")
            continue
        if not in_baseline:
            failures.append(
                f"error RWA-SHELL: {rel} is a bare FocusScope subclass "
                f"claiming menus.activate with no `override bool OwnsAccept` "
                f"and no OWNS_ACCEPT_BASELINE entry. Add an explicit "
                f"OwnsAccept override (true, or documented false) — see "
                f"ScheduleScope/WorkMenuScope — or extend the baseline with "
                f"a one-line verdict comment after confirming the coexisting "
                f"window can't eat Enter (CLAUDE.md's keyboard-shell section).")

    stale = OWNS_ACCEPT_BASELINE - seen_baseline
    for rel in sorted(stale):
        failures.append(
            f"error RWA-SHELL: OWNS_ACCEPT_BASELINE entry '{rel}' no longer "
            f"matches (file gained an override, stopped claiming "
            f"menus.activate, or no longer extends FocusScope directly) — "
            f"remove it from check_shell_demolition.py.")
    return failures


def main():
    failed = False
    for msg in check_dead_symbols():
        print(msg)
        failed = True
    for msg in check_raw_text_fields():
        print(msg)
        failed = True
    for msg in check_ukp_file_gone():
        print(msg)
        failed = True
    for msg in check_owns_accept_opt_in():
        print(msg)
        failed = True
    ratchet_messages, ratchet_failed = check_blocker_ratchet()
    for msg in ratchet_messages:
        print(msg)
    failed = failed or ratchet_failed

    if failed:
        return 1
    print("check_shell_demolition: OK — dead symbols absent, "
          "UnifiedKeyboardPatch.cs deleted, no raw native text fields, "
          "cancel/accept blocker count within ratchet, "
          "OwnsAccept opt-in decision covers every bare FocusScope")
    return 0


if __name__ == "__main__":
    sys.exit(main())
