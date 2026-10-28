# Handoff

The current state of the work, kept up to date with every commit. Read this first
(see `docs/PRINCIPLES.md`, session rules).

**Updated:** 2026-10-28 01:05 MDT

## Where things stand

- **Step:** 1 of 7, the Spike (design doc build order), version 0.1.0.0. Not released yet.
- **`main`** (`1c0ed8a`): the Spike is built. CI against a real Jellyfin 12.4 server with
  Jellyfin Enhanced 12.14.0.0 passes build, unit tests, browser tests, packaging and the live
  checks (plugin Active, script injected once, the full sign-in matrix, database created, emergency
  switch). **Failing:** `ui`, the real-browser UI test (see the `ci-results` branch,
  `latest/step-outcomes.txt`).
- **`wip/ui-test-fix`** (`1a1caaa`): an unfinished fix for that UI test, saved after a usage-limit
  cut-off at about 23:41 on 2026-10-27. Not reviewed or tested.

## Next step

1. Review `wip/ui-test-fix`, finish the UI-test fix, get CI fully green.
2. Add the extra browser test cases from the second version (kept outside the repo): hostile URLs
   in the hash, switching user tears down the page, the script loaded twice gives one button.
3. Release 0.1.0.0 and hand over `VERIFY.md` for the checks only the server can do.

## Open questions

- None.
