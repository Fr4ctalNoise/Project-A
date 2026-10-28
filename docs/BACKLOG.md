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
