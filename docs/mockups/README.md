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
design doc's full-feed mockup. Delete sits behind a "⋯" (More options) button, not a bin icon,
so it can't be hit by accident; the open menu and the confirmation are drawn in 2c.
No all-post-types preview (Michael: explored when each step arrives).

**Variant under consideration:** a forum-style side column (big profile picture with the name
under it, post on the right): `step2/feed-side.html`, `step2/feed-side.css`,
`step2/step2-feed-side-desktop.png`. Desktop only; phones keep the compact head row.
Portrait version at Michael's reference size (150 x 200, feed widened to 880 px):
`step2/feed-portrait.*`, `step2/step2-feed-portrait-desktop.png`; its two avatars are original
drawings made for the mockup (`avatar-cat.svg`, `avatar-ghost.svg`).
Jellyfin 12 stores profile pictures as uploaded (no resize, no crop, no size limit in
`ImageController.PostUserImage` beyond the server's request cap), and can serve a resized copy,
so the page asks for about 300 x 400 (sharp on high-DPI) and crops to fill with `object-fit`.
Variant 2 (Michael, 2026-10-28): no box around the picture, fitted whole (`object-fit: contain`)
so cut-out figures with transparent backgrounds aren't trimmed; name with tags on the top row,
time under it: `step2/feed-portrait2.*`, `step2/step2-feed-portrait2-desktop.png`
(`avatar-cutout.svg` is an original transparent-background drawing). Transparency survives only
if the page asks Jellyfin for PNG or WebP, never JPEG. Tags: only admins set them (decided); tags, roles and achievements are later steps (BACKLOG.md).
No picture: a faint silhouette (`avatar-none.svg`, drawn as a CSS mask so its colour comes from the theme).

**Theming contract (author column):** sizes and colours are `--pa-*` variables (`--pa-feed-width`,
`--pa-avatar-width/height/radius/fit`, `--pa-silhouette-colour/opacity`, `--pa-tag-*`, `--pa-role-*`)
defaulting to the theme's `--t-*` settings. Stable hooks: `.projectA-post`, `.projectA-side`,
`.projectA-avatar(--none)`, `.projectA-namerow`, `.projectA-author`, `.projectA-tag(--role)`,
`data-pa-tag`, `data-pa-role`. Project Themes overrides variables, not selectors. See the end of
`step2/feed-portrait2.css`.
Jellyfin caps every `form` at 54em, so the composer sets `max-width: none` (needed in part 3).


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
