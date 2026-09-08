# RimWorld Access: project doctrine

The rules this codebase is held to, and the traps that have already cost someone a day.
Comments and ratchet scripts throughout the repo cite this file by name, so treat it as
the reference those citations point at.

If you are here to add support for a third-party mod, read `src/Compat/README.md` too.
It is more specific and it is the more detailed document.

## Verify, never assume

Three sources answer questions about how the game behaves, in this order:

1. **Decompiled game code.** How RimWorld actually works. The mod is built against a
   local decompile of the game assemblies; nothing in this repo is a substitute for
   reading them.
2. **Game XML defs**, under the game's `data` directory. Labels, descriptions, and
   translation strings.
3. **The live game**, through the DEBUG-only dev bridge: `curl -s --data '<C# expr>'
   http://127.0.0.1:8787/eval`. Resolved labels, open windows, real values.
   `ShellDev.InjectAndCapture("F2, DownArrow")` drives keyboard smoke tests. The class
   header on `ShellDev` lists what the harness cannot see.

RimWorld lazy-populates a lot of UI state during render and hover, so a property read
cold is often empty: `Command_Ability.Desc` is blank without a hover, and the right
answer is `ability.def.description`. Translation keys do not always resolve to short
labels, and def labels are sometimes empty. Check resolved values live rather than
reasoning about what they ought to be.

**The QA flight recorder** is the first place to look when diagnosing a report. It is
compiled into every build and toggled manually in game (Alt or Option plus the vanilla
screenshot key), writing one log per recording under
`<SaveDataFolder>/RimWorldAccess/FlightRecorder/`. It records every key resolution
(`[key] ... top=<scope> -> <action>`), every utterance (`[speech]`), every scope
push, pop and attach, every window add and remove with the caller chain that opened or
closed it, and `[mark]` section breaks. Read it before touching code.

Two signatures worth knowing. `attach generic-window for <Type>` at a dialog open means
that window has no bespoke scope and is being read generically. A consequence spoken
with no `[key]` line before it means something outside the dispatcher consumed the key,
usually a vanilla window's own GUI pass; Return arrives as two Unity events, so its
eaten KeyDown leaves an orphan `key=None ch=10 -> modal-swallow` twin. The trace sees
only the dispatcher chain, so vanilla-pass key handling shows up as these absences
rather than as lines of its own.

## Doctrine

**Mutations ride a vanilla vehicle.** Four categories, in order of preference:

- **A**: invoke the vanilla widget or delegate itself. A gizmo's `ProcessInput`, a
  `FloatMenuOption.action`, a dialog's own `CanAccept` plus `Accept`.
- **B**: call a gated vanilla method (`Try*`, or one returning an `AcceptanceReport`)
  and honor its answer. A mutator called without its `Can*` twin is a violation.
- **C**: a hand-copied gate, only where no A or B vehicle exists, marked
  `// MUTATION-C: mirrors <vanilla path>; <why A/B is impossible>`. The mutation ratchet
  parses those markers, so never delete or reword one.
- **D**: a raw ungated mutation. Forbidden.

If the action opens a vanilla flow, a ritual or a confirmation, open that flow. Never
skip to the result. Text-field limits are validation too: harvest them from the game's
own call sites, its dialogs, and its constants rather than picking a number.
`scripts/check_mutation_doctrine.py` enforces this, and `ActionParityOracle` checks it
at runtime in DEBUG builds.

**Read the game's live decision objects.** Derive structure, labels, columns and sorting
from vanilla's own defs, comparers and widget state, including private IMGUI flags where
that is what holds the answer. Never string-match a translated label, hand-transcribe a
UI, or hardcode a value the game already exposes. Prefer interfaces over concrete
classes, and verify every type hierarchy in the decompiled code rather than assuming it.

**Present everything; editorialize nothing.** If a sighted player can see it, tooltips
included, a screen reader user hears it. Read-only rows stay navigable and say that they
are read-only. Announcements run label, then hotkey, then stats, then description.
Supplementary information is re-announced only when its context changes. One
announcement per action. Separate parts with periods, never with newlines. Every
player-facing string is localized with a whole-phrase key, never assembled by
concatenating translated fragments.

**Deviate from vanilla only where vanilla is keyboard-hostile.** Keeping a targeting
session open after a range failure is the shape of a justified deviation.

## The keyboard shell

All input routes through `ShellDispatcherPatch`, in
`src/Shell/Focus/ShellDispatcher.Game.cs`. Per pass it reconciles the scope mirrors, lets
the IME funnel and any active text session consume first, then dispatches chord claims
top-down through the focus stack. A live MODAL scope masks everything beneath it.
Unclaimed keys are swallowed while `ShellGuards.MenuOwnsInput()` holds, and
under-claiming scopes depend on that swallow, so never weaken the predicate.

A new screen subclasses `ScreenScope`, whose header is the content, table and Buttons
region contract. Pick a push and pop shape from `FocusScope`'s header. Register action
ids in `ShellActionInventory` first.

Escape and Enter on real windows go through the shared router pair in
`src/Shell/Focus/WindowKeyRouter.Game.cs` and `PageKeyRouter.Game.cs`
(`WindowCancelKeyRouterPatch`, `WindowAcceptKeyRouterPatch`, and the `Page` equivalents),
which consult each scope's `OwnsCancel` and `OwnsAccept`. Never add a new Harmony blocker
on `OnCancelKeyPressed` or `OnAcceptKeyPressed`; route through the pair instead.
`Event.current.Use()` does **not** stop `Window.OnCancelKeyPressed`, which is how one
Escape ends up closing two dialogs.

**The window-pass ordering trap**, referred to in comments as QA R6: the focused window's
GUI pass can run BEFORE the dispatcher and shares Event state with it. A guard in a
window pass must therefore be state-based, or mask `keyCode` and restore it around the
vanilla body. It must never depend on a same-frame frame stamp, and it must never call
`Use()` on a key that a scope claims.

`scripts/check_shell_demolition.py` locks the outcome of the input-plumbing rebuild in
place.

## Gotchas that bite

- Harmony misses inherited and overridden methods. Patch the DECLARING type and guard
  with `__instance is`. An override that skips `base` escapes a base-type patch entirely.
- IMGUI focus dies for windowless hosts after a child window closes. Call
  `Notify_ManuallySetFocus` in `PostOpen`, and reclaim focus only inside the host's own
  GUI pass, never every frame under a modal.
- `DialogInterceptionPatch` swallows game-spawned `FloatMenu`s before they reach the
  WindowStack, reopening their options as a windowless menu. A `FloatMenu` may therefore
  be live while absent from the stack, so ask `WindowlessFloatMenuState`, not the stack.
- Inspect-tab visibility reads `Find.Selector.SingleSelectedThing`, so select the thing
  (with `playSound: false`) before reading its tabs.
- Never gate text input on a `KeyCode.A..Z` range; that breaks every non-US layout. Use
  the layout-aware `Event.character` through the shell's char funnel or
  `TextInputManager`.
- On map sweeps, pair cell lists with HashSets, and never call a hover or per-cell cache
  API in a loop. Each such query caches permanently.
- A state reached mid-flow may still be `IsActive` later. Close it explicitly. Loading a
  save fires no reset of its own: `GameStartPatch` (at FinalizeInit) is the reset point,
  so register with `StateResetRegistry`.
- Structurally identical dialogs share one State behind an adapter interface, the way
  `ITransferLoadDialog` and `ILordJobDialogAdapter` do.
- A source file that touches game types needs the `.Game.cs` suffix, or the test project
  will not build.
- Never hard-typeref a lazily loaded assembly, and never run a type sweep over one.
  Reflection only; a direct reference throws `TypeLoadException` at load.
- Launch the game only through `steam://rungameid/294100`. Launching the app directly
  corrupts `ModsConfig.xml`.

## Verifying a change

From the repository root:

```bash
dotnet build                                                                    # 0 warnings, 0 errors
dotnet test tests/RimWorldAccess.Tests/RimWorldAccess.Tests.csproj
dotnet test analyzers/RimWorldAccess.Analyzers.Tests/RimWorldAccess.Analyzers.Tests.csproj
for s in scripts/check_*.py; do python3 "$s" || break; done
```

The `check_*.py` scripts are static ratchets. Each locks in one doctrine decision and
prints what it wants when it fails. Most are shrink-only: they let you reduce a count and
refuse to let you grow it. Read a failing script's docstring before going near its
baseline.

Every player-facing change also gets a `changelog.d/` entry, one file per change.
`changelog.d/README.md` explains the format.
