# Mockups

Mockups come before any visual code (`docs/PRINCIPLES.md`). They are rendered from real
Jellyfin markup and styles, never hand-built look-alikes, and use no copyrighted artwork.

## Step 2 (text posts): what the feed mockup is built from

Gathered 2026-10-28 (part 2a).

| Material | Where | Real? |
| --- | --- | --- |
| Jellyfin 12.4 header with Jellyfin Enhanced 12.14's buttons | `tests/browser/fixtures/header-jf12.4.html` | Captured from the official image |
| Jellyfin 12.4 + Jellyfin Enhanced 12.14 stylesheets, combined | `tests/browser/fixtures/styles-jf12.4-je12.14.css` | Captured |
| The whole page while `#/social` shows: header, our page inside `.mainAnimatedPages`, the hidden home page beside it | ci-results `latest/live-ui/capture-social-page.html` (from CI, added in 2a) | Captured by the live UI test |
| Screenshots of the header, the More menu and the placeholder page | ci-results `latest/live-ui/*.png` | Real browser on a real server |
| A feed API answer (field names, dates, ids) | ci-results `latest/live/feed-sample.json` | Real server |
| Michael's theme (Halloween edition) | His attached `Jellyfin Season Theme (Halloween Edition).css`; not committed here (it moves to Project Themes) | His file |

**Theme pieces the feed reuses, so it looks native without Project A styling much itself:**

- Headings: `.sectionTitle.sectionTitle-cards` inside `.verticalSection` (theme C4 adds its
  heading emoji and font).
- Post cards: rounded panels using the theme's `--t-radius`, `--t-chip-bg`, `--t-divider` and
  accent tokens (C5 card radius; C13's Jellyfin Enhanced review cards are the closest existing
  look for a text post).
- Composer: Jellyfin's `.emby-textarea` and `.button-submit` (theme C6 styles both).
- Colours only through the theme's `--t-*` tokens and Jellyfin's `--jf-palette-*` (Part B), with
  plain fallbacks, so the page still works with no theme or a future season's Part A.
- Not used in the feed: the SCROLL FADE (title page only, step 6) and the 1100px details-page
  layout (C9 to C13).

**Consistency:** the design doc's existing feed mockup (header button, sharing switch, notice,
watched, image and review posts) is the full picture; step 2 draws only what step 2 builds:
the composer, text posts, delete, "load more" and the empty state.

## Step 2 mockup (part 2b, 2026-10-28)

**Michael's review (2026-10-28):** 760 px column: approved (one CSS value, easy to change
later). "Show more" fold for long posts: approved. Paging: load automatically while scrolling
instead of a "Load more" button (part 3: an IntersectionObserver near the end of the list; the
button stays only as a fallback if a load fails, and the end of the feed says "You're all caught
up"). Watched, review and image posts get their own mockups with steps 4, 5 and 7, matching the
design doc's full-feed mockup.


- `step2/feed.html`, `step2/empty.html`: the proposed page markup (what part 3 will build with
  DOM methods). `step2/feed.css`: the proposed styles, theme tokens with plain fallbacks.
- Screenshots: `step2/step2-feed-desktop.png` (1600 px), `step2/step2-feed-phone.png` (390 px),
  `step2/step2-empty-desktop.png`.
- Re-render (needs the theme CSS and the capture from ci-results):

  ```bash
  cd <scratch dir> && npm i material-icons@1.13.12 @fontsource/creepster@5.1.0 @fontsource/nosifer@5.1.0 \
      @fontsource/oswald@5.1.0 @fontsource/noto-sans@5.1.0 @fontsource/noto-color-emoji@5.1.0
  git -C <repo> show origin/ci-results:latest/live-ui/capture-social-page.html > capture.html
  node <repo>/docs/mockups/render.mjs capture.html <theme.css> node_modules out \
      <repo>/docs/mockups/step2/feed.html <repo>/docs/mockups/step2/feed.css step2-feed
  ```

**Limits (not verified):** the phone shot uses the desktop capture with `layout-mobile` switched
on, so the header is the desktop header squeezed (the real phone app collapses it); only the feed
area is the proposal. Fonts come from npm copies of the same families the theme loads from Google.
