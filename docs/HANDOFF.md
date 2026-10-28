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

## In progress: step 2, text posts (branch `wip/step2-text-posts`)

Split into three parts (sized 2026-10-28):
- **Part 1, server side** (no visuals, CI tests all of it):
  - **1a (done, CI green on `6309e12`):** schema v2 (`social_posts`, upgrade from v1 in place) and
    `Data/PostStore.cs` (create with a 10-a-minute limit in one write transaction, feed newest
    first filtered by allowed authors in SQL, soft delete). Tests: `PostStoreTests.cs`.
  - **1b (pushed, waiting on CI):** `POST /Social/Posts`, `DELETE /Social/Posts/{id}` (author or admin), `GET /Social/Feed`
    (`before`, `limit`); user directory (hidden/disabled/deleted authors left out for non-admins);
    input validation (`Api/PostInput.cs`); database errors become a fixed 503
    (`Api/DatabaseUnavailableFilter.cs`). The endpoint list now uses sample ids (0) and a 6th
    column (`allowed`: `200` or `any`), so the sign-in tests create and delete nothing.
    **At release:** VERIFY.md step 8's expected line becomes `Result: 17 passed, 0 failed.`
  - **1c:** live checks on real Jellyfin 12.4 in CI.
- **Part 2:** mockup of feed + composer for Michael's review.
- **Part 3:** the page, browser tests, release 0.2.0.0.

Not in step 2 (per design doc): editing (step 10), mute (step 3), images (step 7).

## Next step

1. Finish part 1 on the wip branch (exact next piece: whichever of 1a/1b/1c above isn't marked done).
2. Michael still has to run `VERIFY.md` for 0.1.0.0 on his server when he can.

## Pending small items

- Design doc updated 2026-10-28 (header tray API, disabled-user wording, health endpoint, step 1
  done).
- Michael's call: pin the Jellyfin image to a version tag instead of `:latest`.

## Open questions

- None.

## Related projects (parked; Project A comes first)

- **Project Modular Bot** (working name; formerly je-review-notifier, still that name in code and
  in the design doc): own repo + Unraid template, then the 2.3.0 health watch that calls
  `GET /Social/Health`. Plan: https://claude.ai/artifact/HXpzXuQ12K3kDsXRrv951v
- **Project Themes**: the seasonal theme CSS as its own Jellyfin plugin and repo. Project A uses its
  variables (`--hw-score`, `data-label`, SCROLL FADE) with fallbacks so it works without it.
  Plan: https://claude.ai/artifact/RzK4fCZtFomGqNK7u4ChWx
