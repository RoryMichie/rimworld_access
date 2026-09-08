# Changelog fragments

This folder holds **unreleased** changelog entries, one per file. At release time
`scripts/build_changelog.py` collects every fragment here, groups them by category,
compiles them into a dated entry at the top of `CHANGELOG.md` (and the website
changelog page), and deletes the fragments.

## Why one file per change

Appending to a shared `CHANGELOG.md` from several worktrees at once causes constant
merge conflicts. Here, each change is its **own new file**, so parallel work never
touches the same file and Git merges cleanly.

## Adding an entry

Create a new Markdown file named `<category>-<short-slug>.md`, where `<category>` is
one of:

- `added`: new features
- `changed`: changes to existing behavior
- `fixed`: bug fixes
- `removed`: removed features
- `deprecated`: soon-to-be-removed features
- `security`: security fixes

Pick a slug that describes the change so filenames stay unique across worktrees, e.g.
`fixed-inventory-location-l10n.md`. Anything after the category prefix is ignored by
the compiler; it exists only to keep filenames distinct.

The file's contents are the player-facing entry text (one change per file). Write it
as a plain sentence with no leading `- ` bullet; the compiler adds that. Example
(`changelog.d/fixed-quantity-typeahead.md`):

```
Fixed digits typed into a quantity chooser also passing through to the screen behind it.
```

Unknown or missing category prefixes are compiled under "Changed".

Within a category, entries compile in alphabetical filename order, so a numeric
slug prefix (`added-010-...`, `added-020-...`) controls the order readers see.

A fragment named `summary.md` is special: its contents (paragraphs preserved)
are rendered as prose between the version heading and the category sections,
for a release-level overview. Use it for large releases only.

This `README.md` is ignored by the compiler.
