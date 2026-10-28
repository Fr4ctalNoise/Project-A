# How Project A is built

Read this before changing anything. Every change, by anyone, follows it.
How the pieces fit, and the contracts with Project Themes and Project Modular Bot, are in
`ARCHITECTURE.md`; read that next.

## Key principles

**Fast, flexible, modular, secure and well documented.**

- **Fast:** speed is part of a smooth experience. No work on pages that don't need it, no
  document-wide scans when a narrow check will do, nothing repeated that can be done once.
- **Flexible:** code as efficient as possible: the least work, memory and network for the result.
- **Modular:** each feature lives in its own unit with a clear boundary, so features can be added
  or changed later without touching unrelated code.
- **Secure:** security comes first. Check everything once, then check it again. Every endpoint
  requires sign-in (a build test enforces it), user content is never parsed as HTML, tokens never
  go in URLs or logs, all SQL is parameterised, and every input is treated as hostile.
- **Built to change:** a feature is a module (its own server folder and page module, plugged in
  through one registry), layout and looks are CSS and settings rather than code, wording is in
  one table, and the contracts with the sister projects are versioned (`ARCHITECTURE.md`).
- **Well documented:** someone new must be able to read the code and know exactly what each part
  does, why it is that way, and how to change it following these same principles. Comments say
  *why*; this folder says how the pieces fit.

## Working rules

- **Mockups before anything visual**, using real images where possible and no copyrighted artwork.
- **Tests with every change**, including hostile input and failure cases, plus a security review.
  A bug fix starts with a test that reproduces the bug.
- **Test against real upstream markup and styles** (Jellyfin, Jellyfin Enhanced), not hand-built
  approximations. Scaffolding is labelled as such.
- **Each release:** version bump, CHANGELOG entry, a zip with a top-level `src/` and no stray
  files, and exact deploy commands for the Unraid server.
- **Say plainly what couldn't be verified**, and give step-by-step instructions to verify it on
  the server instead of skipping it (see `VERIFY.md`).
- **Think across the three projects:** before adding a style hook, an endpoint field or a page
  event, check `ARCHITECTURE.md` sections 5 to 7; anything Project Themes or Project Modular Bot
  could rely on is part of the contract.
- **Requests to run commands or send files** say first whether they only read or change
  something, and what sensitive information the output could hold.

## Session rules (so a cut-off never loses work)

1. **Check before building.** Start by reading the repo's state (GitHub, local copy and
   `docs/HANDOFF.md`). If there is work you don't recognise, stop and report it.
2. **Keep `docs/HANDOFF.md` current**, committed with every change.
3. **Small checkpoints:** commit and push after each working piece (never more than about
   15 minutes of unsaved work). Unfinished work goes on a `wip/` branch, never `main`.
4. **Size work up front** and split big chunks into parts.
