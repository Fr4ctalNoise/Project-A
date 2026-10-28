# Handoff

The current state of the work, kept up to date with every commit. Read this first
(see `docs/PRINCIPLES.md`, session rules).

**Updated:** 2026-10-28 01:55 MDT

## Where things stand

- **Step:** 1 of 7, the Spike (design doc build order), version 0.1.0.0. Not released yet.
- **`main`** (`1c0ed8a`): the Spike is built. CI against a real Jellyfin 12.4 server with
  Jellyfin Enhanced 12.14.0.0 passes build, unit tests, browser tests, packaging and the live
  checks (plugin Active, script injected once, the full sign-in matrix, database created, emergency
  switch). The real-browser UI test found one bug: opening the page left Jellyfin's
  "Page not found" page visible underneath. **Fixed** (button uses pushState; other pages stay
  hidden while ours shows), with three browser tests that reproduce it. **CI fully green** on
  `629d15a` (all six stages; real-browser UI test 15/15).
- Part B done: hostile-URL test, and a user-switch test that found and fixed a real issue (the
  previous user's page was reused). CI fully green on `f7975dd`; browser tests 22/22.

## Next step

1. `v0.1.0.0` tagged: the tag's CI run repeats every check, then builds the zip, creates the
   GitHub release and adds it to `manifest.json`. Confirm the release exists, then Michael installs
   and runs `VERIFY.md`. (CI already runs `verify-auth.sh` as VERIFY.md step 8 does: 5/5.)
2. After Michael's VERIFY.md results: build step 2 (text posts) per the design doc.

## Open questions

- None.
