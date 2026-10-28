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

## Next (part 2b)

`docs/mockups/step2-feed.html`: the captured page with our page area filled with the composer
and sample posts, styled by the captured stylesheets plus the theme, rendered with headless
Chrome at 1600 px and 390 px wide. Sample names are invented; avatars are initials.
