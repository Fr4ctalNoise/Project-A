# Handoff

The current state of the work, kept up to date with every commit. Read this first
(see `docs/PRINCIPLES.md`, session rules).

**Updated:** 2026-10-28 02:25 MDT

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
- **Part 1, server side: DONE.** CI fully green on `c9e6df7` (155 unit tests, 100 live checks
  on real Jellyfin 12.4, verify-auth.sh 17/17). Pieces:
  - Schema v2 (`social_posts`, upgrade from v1 in place) and `Data/PostStore.cs` (create with a
    10-a-minute limit in one write transaction, feed filtered by allowed authors in SQL, soft delete).
  - `Api/PostsController.cs`: `POST /Social/Posts` (201; 400 `body_required|body_too_long|body_invalid`;
    429 + Retry-After 60; 413 over 64 KB; 415 non-JSON), `DELETE /Social/Posts/{id}` (204; 403 not
    author/admin; 404), `GET /Social/Feed?before=&limit=` (PascalCase JSON: `Posts[]` with `Id,
    AuthorId, AuthorName, AuthorHasImage, Kind, Body, CreatedAt, CanDelete`, and `NextBefore`).
    A real sample: ci-results `latest/live/feed-sample.json`.
  - `Users/UserDirectory.cs`: `AuthorVisibility.CanSee` is the one rule: admins see all, authors
    see their own, disabled authors hidden, login-screen-hidden authors hidden only if the new
    setting `HidePostsFromHiddenUsers` is on (default off; decided by Michael 2026-10-28 after the
    live test showed Jellyfin hides every new user by default).
  - `Api/DatabaseUnavailableFilter.cs`: database faults → 503 `database_unavailable` + code;
    oversized body → 413 (not Jellyfin's 500).
  - Sign-in tests use sample ids (`endpoints.tsv` 6th column). **At release:** VERIFY.md step 8
    expects `Result: 17 passed, 0 failed.`, and VERIFY.md gains the new setting.
  - Accepted: deleting someone else's post says 403, a missing one 404 (reveals only that an id
    exists; ids are sequential anyway).
- **Part 2, mockup** (in pieces; Michael is low on usage: stop after each):
  - **2a (done):** material listed in `docs/mockups/README.md`; the live UI test now also saves
    the real page DOM (`latest/live-ui/capture-social-page.html`).
  - **2b (done, reviewed):** 760 px and Show more approved; paging becomes automatic on scroll (see `docs/mockups/README.md`). Feed mockup (composer, text posts, delete, load more, empty state) at 1600 px
    and 390 px, from the captured markup/styles + Michael's theme CSS (attached in chat, not in repo).
  - **Open:** layout choice; current favourite is variant 2 (`docs/mockups/step2/feed-portrait2.*`:
    150x200 portrait, no box, name + tags on top). Also open: who sets user tags. Delete is behind a ⋯ menu (decided).
  - **2c:** error/limit messages, delete confirmation, admin vs user view.
  - **2d:** Michael's review and changes.
- **Part 3:** the page, browser tests, release 0.2.0.0.

Not in step 2 (per design doc): editing (step 10), mute (step 3), images (step 7).

## Next step

1. Part 2c (after Michael's 2b feedback): error/limit states, delete confirmation, admin vs user. Earlier note on 2b: mockup of the feed and composer, rendered from real captured Jellyfin 12.4 markup and
   Michael's theme CSS, for his review before any page code. Page notes: render `Body` with
   markdown-it (html off) + DOMPurify into DOM nodes (the repo test bans innerHTML), author names
   via textContent, avatars from `/Users/{id}/Images/Primary` only when `AuthorHasImage`.
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
