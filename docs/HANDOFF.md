# Handoff

The current state of the work, kept up to date with every commit. Read this first
(see `docs/PRINCIPLES.md`, session rules).

**Updated:** 2026-10-28 01:25 MDT

## Where things stand

- **Step:** 1 of 7, the Spike (design doc build order), version 0.1.0.0. Not released yet.
- **`main`** (`1c0ed8a`): the Spike is built. CI against a real Jellyfin 12.4 server with
  Jellyfin Enhanced 12.14.0.0 passes build, unit tests, browser tests, packaging and the live
  checks (plugin Active, script injected once, the full sign-in matrix, database created, emergency
  switch). The real-browser UI test found one bug: opening the page left Jellyfin's
  "Page not found" page visible underneath. **Fixed** (button uses pushState; other pages stay
  hidden while ours shows), with three browser tests that reproduce it. **CI fully green** on
  `629d15a` (all six stages; real-browser UI test 15/15).

## Next step

1. Confirm CI is green for the user-switch fix (Part B): new tests for hostile URLs and for the
   previous user's page being removed on a user switch (which found and fixed a real issue).
2. Release 0.1.0.0 and hand over `VERIFY.md` for the checks only the server can do.

## Open questions

- None.
