# Backlog

Work agreed but not scheduled yet. The design doc's build order (steps 1–10) is
the plan; this file holds items added since, so nothing is forgotten. Move an
item into the CHANGELOG when it ships.

## Header links (design doc "Later", item 11)

**Added:** 2026-10-27 · **When:** after the Spike (step 1) passes its checks.

Replace the Discord invite button that currently comes from a JavaScript
Injector script plus custom CSS with a small, separate Project A module.

- **Configured on Project A's admin page:** URL, label and icon per link. The
  invite URL lives in the server's plugin configuration, never in this repo.
- **Validation:** `https://` only; opens in a new tab with
  `rel="noopener noreferrer"`; built with DOM methods, never `innerHTML`.
- **Placement:** inside Jellyfin Enhanced's header button group as a
  `.headerButton` with `data-header-label`, so it folds into the **More** menu
  on narrow windows like every other button. Uses the same fallback container
  as the Feed button when Jellyfin Enhanced isn't loaded.
- **Icon:** the Discord logo used only to link to the Discord server, as
  Discord's brand guidelines allow, or a generic icon. Decide when built.
- **Migration:** once it works, remove the old JavaScript Injector script and
  its CSS, so the button doesn't appear twice. Release notes say exactly what
  to remove.

**Needed before starting:** the current JavaScript Injector script and its CSS.
Check them for API keys, tokens or webhook URLs before sharing; the invite link
itself is public and fine to share.

## Tags and roles (design doc "Later", item 12)

**Added:** 2026-10-28 · **Priority:** low · **When:** after step 2 at the earliest.

- Short labels beside a user's name on their posts (for example "Movie night host").
- **Only admins set tags**, from Project A's admin page. Roles (Admin first; more later) show
  the same way, in the accent colour (`.projectA-tag--role`, `data-pa-role`).
- New table `social_user_tags` (schema upgrade); the feed sends each author's tags with their
  posts (`PostView.Author.Tags`). Tag text checked like post text, short length limit.
- Later: tags awarded automatically by achievements.

## Achievements (design doc "Later", item 13)

**Added:** 2026-10-28 · **Priority:** low · **When:** after tags.

- Milestones earned from activity (posts written, episodes watched, ...), shown as badges in the
  author column and able to grant tags.
- Rules live in one module, so an achievement is added or removed without touching the feed.
- `licenses/AchievementBadges-MIT.txt` is already in the repo if that project's ideas are reused.

## How these plug in (applies to both, and to anything else in the author column)

- **Modular:** each feature is its own module that adds items to the name row or the author
  column through one small registry in the page script; switching a module off removes its
  items and nothing else. The server side follows the same rule (each adds its own fields).
- **Themeable:** sizes and colours are `--pa-*` CSS variables defaulting to the theme's `--t-*`
  settings; stable classes plus `data-pa-tag` / `data-pa-role` hooks. Project Themes and custom
  CSS restyle by setting variables. Contract written down in `docs/mockups/README.md`.
