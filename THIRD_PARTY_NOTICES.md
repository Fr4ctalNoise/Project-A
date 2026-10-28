# Third-party notices

Project A is licensed under the **GNU General Public License v3.0 only**
(`GPL-3.0-only`); the full text is in [`LICENSE`](LICENSE). It includes or
adapts code from the projects below, each under its own licence. Their
licence texts are kept verbatim in [`licenses/`](licenses/) and must ship in
every source and release zip.

| Project | Licence | Used for | Licence text | Source checked |
|---|---|---|---|---|
| [Jellyfin Enhanced](https://github.com/n00bcodr/Jellyfin-Enhanced) by n00bcodr and contributors | GPL-3.0 | Script injection, header button fallback, feed page shell, watch events, library-access filter, hidden/disabled-user filter, review reading | [`LICENSE`](LICENSE) (same licence) | `d822e14`, 2026-10-27 |
| [StarTrack](https://github.com/ZL154/jellyfin-plugin-startrack), © 2026 ZL154 and StarTrack contributors | MIT | Per-user privacy setting storage | [`licenses/StarTrack-MIT.txt`](licenses/StarTrack-MIT.txt) | `730b665`, 2026-10-15 |
| [Achievement Badges for Jellyfin](https://github.com/ZL154/AchievementBadges_for_Jellyfin), © 2026 ZL154 and Achievement Badges for Jellyfin contributors | MIT | Image upload checks, quotas, image serving, polling | [`licenses/AchievementBadges-MIT.txt`](licenses/AchievementBadges-MIT.txt) | `ada2c80`, 2026-10-09 |
| [markdown-it](https://github.com/markdown-it/markdown-it), © 2014 Vitaly Puzrin, Alex Kocharin | MIT | Markdown rendering (bundled) | [`licenses/markdown-it-MIT.txt`](licenses/markdown-it-MIT.txt) | `a8c00d4`, 2026-10-16 |
| [DOMPurify](https://github.com/cure53/DOMPurify), © Cure53 and other contributors | Apache-2.0 (chosen from its MPL-2.0 / Apache-2.0 dual licence) | HTML sanitising (bundled) | [`licenses/DOMPurify-Apache-2.0.txt`](licenses/DOMPurify-Apache-2.0.txt) | `b7e0edc`, 2026-10-24 |

The plugin is built against the `Jellyfin.Controller` and `Jellyfin.Model`
NuGet packages (declared `GPL-3.0-only`). They are referenced at build time
and are not included in the release zip; the Jellyfin server provides them.

## Not used

- **[jellyfin-updoot](https://github.com/BobHasNoSoul/jellyfin-updoot)** has no
  licence, so no code, markup or styles are copied from it. Only feature ideas
  (Recommend button, "Recommended by", title-page comments, sorting, admin
  bulk delete) were taken, each implemented from scratch.

## How adapted files are marked

Every file containing third-party code starts with a header naming the
original project, file, commit and licence, for example:

```
// SPDX-License-Identifier: GPL-3.0-only
// Adapted from Jellyfin Enhanced (GPL-3.0), commit d822e14:
//   Jellyfin.Plugin.JellyfinEnhanced/Services/ScriptInjectionStartupFilter.cs
// Copyright (c) n00bcodr and Jellyfin Enhanced contributors.
// Changes: injects /Social/script instead of /JellyfinEnhanced/script.
```

MIT-derived files keep the original copyright line and point to their licence
text in `licenses/`. A build test fails if any file under `src/` lacks an
`SPDX-License-Identifier` line, or if a release zip is missing `LICENSE`,
`THIRD_PARTY_NOTICES.md` or any file in `licenses/`.

## Not affiliated

Project A is an independent, personal plugin. It is not affiliated with or
endorsed by the Jellyfin project, Jellyfin Enhanced, StarTrack, Achievement
Badges, AniList, or any other project named here.
