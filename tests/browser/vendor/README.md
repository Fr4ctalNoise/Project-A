# Vendored upstream code (tests only)

Kept byte-for-byte as published, so tests exercise the real thing. Never edited; to
update, replace the file and rename it to the new commit.

| File | Source | Licence |
|---|---|---|
| `je-header-actions.d822e14.js` | [Jellyfin Enhanced](https://github.com/n00bcodr/Jellyfin-Enhanced) `Jellyfin.Plugin.JellyfinEnhanced/js/enhanced/header-actions.js` at commit `d822e14` (2026-10-27), shipped in Jellyfin Enhanced 12.14.0.0 | GPL-3.0, © n00bcodr and Jellyfin Enhanced contributors |

The test harness supplies the four Jellyfin Enhanced services this file calls
(`JE.t`, `JE.session`, `JE.helpers.addCSS`/`onBodyMutation`/`getHeaderRightContainer`)
as minimal stand-ins; see `harness.mjs`.
