# Security Policy

RimWorld Access is a game mod, so the security surface is small. But if you find something that could harm players, here is how to report it.

## Reporting

Please do not open a public issue for a security problem. Instead, send a private message to the maintainers on the [Discord](https://discord.rimworldaccess.com), or use GitHub's private vulnerability reporting on this repository. I will respond as quickly as I can.

## Scope notes

- The released mod ships only the compiled mod assembly and the Prism screen reader libraries. It does not open network ports or run remote code.
- There is a development-only HTTP bridge in the source (`src/DevBridge/`) used to inspect the running game while developing. It binds to localhost only, and it is compiled out of every Release build, so it never reaches players. If you believe that isolation is broken, that is worth reporting.

## Supported versions

Only the latest released version is supported. If you are on an older build, update before reporting.
