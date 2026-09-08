# RimWorld Access

Screen reader and keyboard accessibility for RimWorld. This repository holds the mod's source code. It is the developer-facing side of the project.

**If you are a player, you are probably in the wrong place.** You don't need this repository to install or use the mod, and it is not where the instructions live. Everything a player needs, including installation, setup, a full keyboard reference, and how each screen works, is at **[rimworldaccess.com](https://rimworldaccess.com)**. Start there. The fastest way to install is through the [Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3750094441), and the [Discord](https://discord.rimworldaccess.com) is the best place to get help.

This README used to double as the mod's manual. It no longer does. The documentation has a proper home now, and keeping a second copy here only guaranteed the two would drift apart.

> **Developer who stumbled in and wondering what this is?** Nearly every line here was written by an AI coding agent, and some of it looks the part. [WHY.md](WHY.md) explains what this project actually is, why it is built the way it is, and who else is building things like it. Read that first.

## History and credit

RimWorld Access was created by **[Shane Earley](https://github.com/shane12300)**, who did the early work that made everything after it possible. Shane transferred the project to me, Aaron Ramirez, in early 2026, and I have been the main person working on it since. If the mod is useful to you, a large share of the credit is his.

## What this is

A C# mod for RimWorld 1.6 that injects keyboard navigation and screen reader output into the game's existing UI. The short version of the stack:

- .NET Framework 4.7.2
- [HarmonyLib](https://github.com/pardeike/Harmony) for runtime patching of RimWorld
- [Prism](https://github.com/ethindp/prism) for cross-platform screen reader output (NVDA, JAWS, VoiceOver, Orca, SAPI, and others)

Keyboard input all routes through a single dispatcher, and screens are built on a shared chassis that gives every one of them the same navigation and the same announcement grammar. Support for third-party mods is a drop-in folder per mod. [contributing.md](contributing.md) has a short architecture section, [CLAUDE.md](CLAUDE.md) holds the project's doctrine and its sharp edges, and [src/Compat/README.md](src/Compat/README.md) covers adding mod support.

## Building

```bash
dotnet build              # debug build, auto-deploys to your RimWorld Mods folder
dotnet build -c Release   # release package
```

If RimWorld is not at the default Steam path, copy `GamePaths.props.template` to `GamePaths.props` and set `RimWorldDir`. See [contributing.md](contributing.md) for the full setup, including the Prism native libraries.

## Contributing

Read **[contributing.md](contributing.md) before opening anything.** The short version: ideas and detailed feature proposals are welcome and wanted. Code, less so, largely because I hate code review and when 99% of contributors are using AI, things get chaotic quickly. Plus, code isn't the bottleneck for me. Coming up with ideas and designing how screens will work is, and that's where I welcome the most help!

## License

MIT. See [LICENSE](LICENSE).
