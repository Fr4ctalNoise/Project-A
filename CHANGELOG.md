# Changelog

Newest first. Versions follow `MAJOR.MINOR.PATCH.0` (Jellyfin's four-part plugin versions).
Each release notes anything you must do when upgrading under **Upgrade notes**.

## 0.1.0.0 — 2026-10-28

The Spike (build step 1): proves the plugin loads on Jellyfin 12, its script reaches the web
page, it can write its own database, and nothing in it is reachable without signing in.

- **Activity Feed button** in the header. With Jellyfin Enhanced it joins JE's button group and
  folds into JE's **More** menu on narrow windows; without JE it sits with Jellyfin's own header
  buttons.
- **Placeholder page** at `#/social`, styled by your theme.
- **Database** `social.db` in `plugins/configurations/ProjectA/` (kept across updates). Only a
  health-check row so far.
- **Health check** `GET /Social/Health` for administrators and API keys: version, database state,
  whether the script is being injected. No user data.
- **Access gate** on every endpoint except the script: signed-in, existing, enabled users only;
  API keys refused except on the health check. Jellyfin 12.4 already refuses disabled accounts
  at sign-in (401), and the gate checks again in case that ever changes; Jellyfin treats API keys
  as unrestricted administrators, and the gate is what refuses them.
- **Page behaviour** tested on a real Jellyfin 12.4 in a real browser: opening the feed hides
  Jellyfin's page (including its "Page not found" on a direct visit or refresh), leaving restores
  it, and the page is discarded on sign-out or when a different user signs in.
- **Emergency switch** in Dashboard → Plugins → Project A: turns off the script injection.
- Never updates itself (`autoUpdate: false`).

**Upgrade notes:** first install. Add the repository, install from the Catalog, restart
Jellyfin, then follow `VERIFY.md`.

## Earlier

- Repository set up: GPL-3.0-only licence, third-party notices and licence texts for
  Jellyfin Enhanced, StarTrack, Achievement Badges, markdown-it and DOMPurify.
