# RimWorld Access analyzers

Compile-time guardrails for the mod's doctrine. `rimworld_access.csproj` references
`RimWorldAccess.Analyzers` as an analyzer (`ReferenceOutputAssembly="false"`), so the rules run on
every build of `src/` and nothing here ships to players.

## RWA0001: State holds an editable copy of vanilla state

A State or Scope must not keep a private editable copy of a vanilla value across keypresses and
write it back at the end. The distinguishing shape is a value that lives in OUR object across
keypresses while vanilla's equivalent lives one IMGUI frame on the stack. The fix is always the
same: write through to the vanilla object on every keypress, riding whatever gated vanilla
vehicle that value has, instead of buffering and committing.

The analyzer flags a field of a `*State` or `*Scope` type when all three of these hold:

- **Seed**: it is assigned in an `Open`/`Begin`/`Start` method from a vanilla member, a
  reflection `GetValue`, or a parameter.
- **Edit**: it is assigned, compound-assigned, or incremented in a key-handler method
  (`Adjust`, `Select`, `Step`, `Jump`, `Handle`, `Increase`, `Decrease`, `Toggle`, `Cycle`).
- **Commit**: it is read in a `Confirm`/`Accept`/`Apply`/`Commit` method.

Deliberately silent shapes, all of them legitimate and common here: typeahead and numeric-entry
buffers, one-shot `pending`/`armed` channels, cursor/index/scroll display state, `string` fields,
and a reference to a vanilla object that we edit in place, which is the live posture the rule
asks for rather than a copy.

`scripts/check_shadow_copies.py` is the textual approximation of the same rule; this analyzer is
the semantic one. It resolves types and namespaces through the compiler rather than by matching
text, so it tells a real vanilla type apart from a same-named type of ours and separates
value-type fields from reference-type fields correctly. Known limitation: it inspects only the
descendants of the assignment's value operation, so a vanilla read laundered through a local
variable before the assignment is not detected.

## Suppressions

Suppress with `#pragma warning disable RWA0001` or `[SuppressMessage]`. A justification is
required either way: the attribute's `Justification` argument, or a comment on the pragma line
saying why the shape is not a shadow copy. An unjustified suppression is a review failure. The
rule exists because the buffered value is invisible to a sighted player watching the same screen,
so silencing it without an argument silently reintroduces the parity gap.

## Running the tests

```
dotnet test analyzers/RimWorldAccess.Analyzers.Tests/RimWorldAccess.Analyzers.Tests.csproj
```

`SilentDisablementCanary_AnalyzerStillProducesRwa0001` is not a duplicate of the rule test: Roslyn
disables a throwing analyzer without a word, and every callback here swallows exceptions so a
broken analyzer cannot break a teammate's build. That canary is what turns a silent death red.
