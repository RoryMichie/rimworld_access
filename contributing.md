# Contributing to RimWorld Access

I am strict about what comes into this project, and the bar for contributing code is high on purpose. That is not meant to push you away. I want your ideas. I just want to be careful about what gets merged.

## The most useful thing you can do

Submit a well-specified idea.

If you have a feature in mind, open an [issue](https://github.com/aaronr7734/rimworld_access/issues) or a [discussion](https://github.com/aaronr7734/rimworld_access/discussions) and document it thoroughly. Tell me exactly what you want it to do, how it should behave, what the screen reader should say, where it fits among the existing controls, and the edge cases you have already thought about. The more precise and complete the write-up, the better.

I welcome every suggestion. I may not build it, and I will not promise a timeline, but a clear, detailed proposal is worth more to me than almost anything else you could send. It saves me far more time than a pull request does, and frankly I would rather have it. A good spec means I can pick the work up and do it right. A pull request means I have to review someone else's work, which is the part of this I enjoy least.

So: ideas, all day. Please send them.

## About pull requests

I do not enjoy reviewing pull requests, especially from people I do not know, written with coding agents I cannot evaluate. Large language models have made it very hard to tell, from a diff alone, who I am dealing with and what they actually understand. So if you are going to send code, here is what I require.

### Pull requests, excluding localization-related ones, must include the agent transcript

If you used a coding agent to write your changes, your pull request must include the full transcript of the session that produced them. No transcript, no merge. This is not negotiable. The exception being localization pull requests. If you're adding support for a language, don't worry about including a transcript!

I am not trying to shame anyone for using these tools. I use them too. This entire codebase is largely AI generated. But I need to see the reasoning, the prompts, and the path the work took, not just the polished diff at the end.

If you use coding agents and don't know how to capture a transcript of a session, that is a sign you are not ready to be opening pull requests here yet. Producing one isn't that hard, and there are many tools out there that can do it. As one example, [simonw/claude-code-transcripts](https://github.com/simonw/claude-code-transcripts) exports Claude Code sessions. Other tools exist, and they won't be hard to find. If you use Codex, I believe it has this functionality built in. Claude itself should be able to build you a script to do this too, given session transcripts are just files on your machine. I'm genuinely not doing this to be a jerk, and will gladly help you generate a transcript on the Discord server if needed!

If you genuinely wrote your contribution by hand, with no agent involved, say so in the pull request. I find that hard to believe given the state of this codebase, but it is possible, and I will take you at your word while I review.

## A note on the codebase

You should know what you are walking into.

The keyboard input handling used to be the worst of the technical debt here. It predated my ownership of the project, it was woven into nearly every system in the mod, and for a long time I chose to live with it because I did not have the tooling to change it safely. That refactor is done. Input now routes through a single dispatcher, screens sit on a shared chassis, and a set of static checks locks the result in so it cannot quietly come apart again.

That does not make this a simple codebase. It is large, it reaches into a lot of RimWorld's internals through Harmony, and the parts that look independent usually are not. A change to the shell affects every screen at once. If your contribution touches input routing or the shell, expect extra scrutiny, not because the code is a minefield any more, but because the contracts there are real and easy to break from the outside.

This is still not an easy codebase to make a clean, isolated change to, for a human or for an agent.

## If you still want to send code

### Setup

Prerequisites:

- .NET Framework 4.7.2 SDK
- A local RimWorld 1.6 install
- An IDE or text editor

Build:

```bash
dotnet build
```

This compiles the mod and copies it into your RimWorld Mods folder via a post-build step.

If RimWorld is not at the default Steam path, copy `GamePaths.props.template` to `GamePaths.props` and set `RimWorldDir`. That file is gitignored, so your local setup never touches the repository.

The mod depends on [Prism](https://github.com/ethindp/prism) native libraries for screen reader output. These are not checked into Git. They download automatically on your first build. To pull the latest Prism release later, run `dotnet msbuild -t:UpdatePrism`.

Release package:

```bash
dotnet build -c Release
```

### Workflow

- Open an issue first and wait for a response before writing a feature. I would much rather talk about the idea than receive a surprise pull request.
- Fork the repository and branch from `master`.
- Use [Conventional Commits](https://www.conventionalcommits.org/) for messages (`feat:`, `fix:`, `docs:`, `refactor:`, `chore:`).
- Test in-game with a real screen reader before you submit. Test with only Harmony and RimWorld Access enabled, no other mods.
- Link the pull request to its issue (`Closes #123` or `Fixes #123`).
- Include the agent transcript. See above.

### Architecture, briefly

Every keypress arrives at one place. `ShellDispatcherPatch`, in `src/Shell/Focus/ShellDispatcher.Game.cs`, runs before RimWorld's own UI and walks a stack of focus scopes from the top down, handing the key to the first scope that claims it. A scope is one layer of keyboard focus: a screen, a dialog, an overlay. Each holds its own cursor and search state in instance fields, so popping a scope is the reset, and a modal scope masks everything beneath it. The header on `FocusScope` covers the shapes a scope can take and how each is pushed and popped.

Most screens are a `ScreenScope`, which is the shared chassis for anything that presents as one or more navigable lists or tables. It supplies the regions, the cursor, typeahead, a Buttons region built from the window's own buttons, and one announcement grammar, so a new screen declares its content instead of reimplementing navigation. There are about 130 of them, and the header on `ScreenScope` is the contract. The reason for the chassis is the whole point of the mod: a player learns one set of keys and one way things are spoken, and then every screen in the game answers to it. A screen that invents its own navigation is a screen the player has to learn twice.

Support for third-party mods is a drop-in folder under `src/Compat/`, one per mod, each with a single `CompatModule` that activates only when its mod is loaded. There are 26 of them. `src/Compat/README.md` is the guide for adding another, and it is the most detailed document in this repository.

Two rules run through all of it. Mutations ride a vanilla vehicle, meaning we invoke RimWorld's own widget, delegate, or gated method rather than writing game state ourselves, so we inherit its validation instead of guessing at it. And we present everything without editorializing, tooltips included. If a sighted player can see it, a screen reader user hears it.

Those two rules and the rest of the project's doctrine live in [CLAUDE.md](CLAUDE.md), along with the traps that have already cost someone a day. Comments and ratchet scripts throughout the source cite it by name, so read it before you change anything in the shell.

### Before you open the pull request

From the repository root, all of these must pass:

```bash
dotnet build                                                                    # 0 warnings, 0 errors
dotnet test tests/RimWorldAccess.Tests/RimWorldAccess.Tests.csproj
dotnet test analyzers/RimWorldAccess.Analyzers.Tests/RimWorldAccess.Analyzers.Tests.csproj
for s in scripts/check_*.py; do python3 "$s" || break; done
```

Those twelve `check_*.py` scripts are static ratchets. Each locks in one doctrine decision and prints exactly what it wants when it fails. Most are shrink-only: they will let you reduce a count and refuse to let you grow it. If one fails on your change, read its docstring before you go anywhere near its baseline.

Add a `changelog.d/` entry as well. One new file per change, named `<category>-<slug>.md`, containing the player-facing sentence in plain words. `changelog.d/README.md` explains the format and why it works that way.

## Questions

Open a [discussion](https://github.com/aaronr7734/rimworld_access/discussions), ask inside your issue, or come to the [Discord](https://discord.rimworldaccess.com).
