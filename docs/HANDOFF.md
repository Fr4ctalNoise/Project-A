# Handoff

The current state of the work, kept up to date with every commit. Read this first
(see `docs/PRINCIPLES.md`, session rules).

**Updated:** 2026-10-28 01:45 MDT

## Where things stand

- **Step:** 1 of 7, the Spike (design doc build order), version 0.1.0.0. **Released** (see below).
- **`main`** (`1c0ed8a`): the Spike is built. CI against a real Jellyfin 12.4 server with
  Jellyfin Enhanced 12.14.0.0 passes build, unit tests, browser tests, packaging and the live
  checks (plugin Active, script injected once, the full sign-in matrix, database created, emergency
  switch). The real-browser UI test found one bug: opening the page left Jellyfin's
  "Page not found" page visible underneath. **Fixed** (button uses pushState; other pages stay
  hidden while ours shows), with three browser tests that reproduce it. **CI fully green** on
  `629d15a` (all six stages; real-browser UI test 15/15).
- Part B done: hostile-URL test, and a user-switch test that found and fixed a real issue (the
  previous user's page was reused). CI fully green on `f7975dd`; browser tests 22/22.

- **Released 0.1.0.0** (confirmed 2026-10-28): GitHub release `v0.1.0.0` on `ee8b423` (CI run
  40649995760, workflow_dispatch, all six stages green) with `project-a_0.1.0.0.zip` (MD5
  `7bd5dcd9927073766c6a028d2a9ad938`, matches `manifest.json`) and `project-a_0.1.0.0_source.zip`
  (top-level `src/`). The bot added the `manifest.json` entry in `3a0973b`.

## Next step

1. Michael installs 0.1.0.0 and runs `VERIFY.md` (walked through in chat 2026-10-28). Waiting on
   his results; fix anything that fails (bug fixes get a failing test first).
2. After Michael's VERIFY.md results: build step 2 (text posts) per the design doc.

## Pending small items

- Update the design doc to match what was verified: header tray API row
  (`JE.headerActions.getTray`), disabled-user wording (Jellyfin refuses them itself), health
  endpoint (admin and API key only), build step 1 marked done.
- Michael's call: pin the Jellyfin image to a version tag instead of `:latest`.

## Open questions

- None.
