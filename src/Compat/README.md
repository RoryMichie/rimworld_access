# Adding mod support to RimWorld Access

Support for a third-party mod is a drop-in package: one folder under `src/Compat/` named
for the mod, containing one `CompatModule` subclass and whatever adapters, scopes, and
handlers the mod's screens need. Nothing outside your folder should require editing. If
you find yourself changing shared files to make your mod work, stop and open an issue
first; that usually means the generic reader has a gap worth fixing for every mod at once.

## The module

Every folder has exactly one activation entry point:

```csharp
using HarmonyLib;

namespace RimWorldAccess
{
    internal sealed class MyModModule : CompatModule
    {
        public override string TargetPackageId => "author.mymod";

        public override void Activate(Harmony harmony)
        {
            MyModGizmoCompat.RegisterGizmoHandlers();
            MyModDialogCompat.RegisterDialogScopes(harmony);
        }
    }
}
```

`CompatBootstrap` discovers every module at startup and calls `Activate` only when the
mod is present. The default gate is `ModsConfig.IsActive(TargetPackageId)`; override
`ShouldActivate` with a type probe (`AccessTools.TypeByName(...) != null`) when forks or
renamed uploads matter, or when your family spans several package ids. When the mod is
absent your code must cost nothing: no Harmony patches, no reflection resolution, no
handler registrations. Never use a declarative `[HarmonyPatch]` attribute in this tree,
because `PatchAll` applies those before any module gate runs; patch from `Activate` (or a
method it calls) with `harmony.Patch(...)`, skipping targets that fail to resolve.

## Reflection

You never reference the target mod's assembly at compile time. Resolve its internals
through `ReflectionSurface`:

```csharp
var surface = new ReflectionSurface("MyModThingAdapter");
compType = surface.Type("MyMod.CompThing");
countField = surface.Field(compType, "count");
tryStartMethod = surface.Method(compType, "TryStart", new[] { typeof(Pawn) });
ready = surface.Ready;
```

`Ready` is silent when the mod is absent and logs every missing name once when the mod is
present but has changed shape, which is what makes a mod update diagnosable from a player
log. Route only REQUIRED members through the surface. A type you obtained elsewhere (a
constructor parameter, a registry) is declared with `surface.Supplied("MyMod.SomeGizmo",
type)`. A version-optional member uses the non-recording static
`ReflectionSurface.TryFieldOrProperty(type, name)` with a one-line comment saying it is
optional. A class whose capabilities degrade independently, each with its own warning,
should keep that shape instead of forcing an all-or-nothing surface onto it.

More shared pieces you should not reimplement: `ModLogger.LimitedError(context, ex)`
for anything that can fail per frame or per row,
`RimWorldAccess.Shell.SpeechFlatten.ToSentences(text)` for flattening multi-line mod text
into announceable prose, `RimWorldAccess.Shell.CompatText` for reading a host mod's own
translation keys (including `ResolveOrFallback`), `CompatRegistration` for the
probe-construct-gate-register-log ceremony around tab adapters and gizmo handlers, `LazyReflectionGate` for the resolve-once/failed-once gate compat files used to hand-roll
as a bool pair, and `Guarded` (Get/FieldOf/Call) for invoke-time read plumbing. The
surface guards resolve time, `Guarded` guards the call, and gated writes never route
through it.

## The rules the reviewers hold you to

Find the vanilla screen your mod's screen imitates and reuse this project's
implementation of it: same scope shape, same table or tree model, same navigation, same
announcement grammar. A novel design needs a reason no vanilla analogue exists.

Mutations ride a vanilla vehicle. Invoke the mod's own widget or delegate, or call its
gated method and honor the answer. A hand-copied gate is a last resort and must carry a
`// MUTATION-C: mirrors <path>; <why>` marker; the mutation ratchet parses those markers,
so never delete or reword one.

Present everything and editorialize nothing. If a sighted player sees it, a screen reader
user hears it. Read labels, columns, and sorting from the mod's own defs and widget state,
never from string-matching translated text. Announcements go label, hotkey, stats,
description, separated by periods, never newlines.

Every player-facing string is localized with a whole-phrase key under
`RimWorldAccess.Compat.<Mod>.*`. A string that is never spoken or displayed raw (a
dispatch token) carries an inline `// l10n-exempt: <reason>` marker, which the
localization ratchet reads.

Accessibility is additive. Never hide, blank, or degrade the mod's own rendering. A
sighted viewer watching a blind player's stream should not be able to tell, except for
the keyboard.

Code style: one top-level type per file, named for the type. Game-coupled files end in
`.Game.cs`. C# 7.3 only, pinned in `rimworld_access.csproj`. Comments state constraints the code cannot show, in one or two
tight sentences; no history, no narration, no references to plans or review rounds.

## Before you open the PR

From the repo root, all of these must pass:

```
dotnet build                                              # 0 warnings, 0 errors
dotnet test tests/RimWorldAccess.Tests/RimWorldAccess.Tests.csproj
dotnet test analyzers/RimWorldAccess.Analyzers.Tests/RimWorldAccess.Analyzers.Tests.csproj
for s in scripts/check_*.py; do python3 "$s" || break; done
```

That last line runs every static ratchet. The ones a compat change trips most often are
`check_mutation_doctrine.py` (a write with no vanilla vehicle and no MUTATION-C marker),
`check_localization.py` and `check_l10n_keys.py` (an unlocalized player-facing string, or a
key that no language file defines), `check_comment_diet.py` (comment bloat, or provenance
in a comment), and `check_tree_doctrine.py` (a hand-rolled tree that should be a
`TreeRegionScope`). Each script prints what it wants when it fails; read its docstring
before touching its baseline.

Add a short player-facing entry in `changelog.d/` describing what a blind player can now
do, in plain words. Then test with the mod loaded and, just as important, confirm the
game starts clean with your target mod removed.
