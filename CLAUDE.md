# Project A: instructions for Claude Code

A private Jellyfin 12 plugin for Michael's server: an AniList-style social feed (text and image
posts, watch activity, Jellyfin Enhanced reviews, likes, comments). Only Jellyfin 12 matters.

## Read first, every session

@docs/PRINCIPLES.md
@docs/HANDOFF.md

Before changing structure, adding an endpoint, a page module, a CSS hook or a page event, read
`docs/ARCHITECTURE.md` (the foundation and the contracts with Project Themes and Project Modular
Bot). Plans and backlog: `docs/BACKLOG.md`; mockups: `docs/mockups/README.md`; checks Michael
runs on his server: `VERIFY.md`.

## Start of every session (session rule 1)

1. `git fetch --all` and `git status`; compare with `docs/HANDOFF.md` and the newest CI results:
   `git show origin/ci-results:latest/step-outcomes.txt` (full results under `latest/`).
2. If there is work you don't recognise, stop and tell Michael before building.

## Building and testing locally

| What | Command (from the repo root) | Needs |
| --- | --- | --- |
| Build (warnings are errors) | `dotnet build src/Jellyfin.Plugin.ProjectA/Jellyfin.Plugin.ProjectA.csproj -c Release` | .NET 10 SDK |
| Unit tests | `dotnet test tests/Jellyfin.Plugin.ProjectA.Tests -c Release` | .NET 10 SDK |
| Browser tests (real captured Jellyfin markup) | `cd tests/browser && npm ci && npm test` | Node 22, Chrome (`CHROME_PATH`) |
| Package | `python3 build/package.py src/Jellyfin.Plugin.ProjectA/bin/Release/net10.0 dist` | Python 3 |
| Live server + UI tests | see `.github/workflows/ci.yml` (`tests/integration/`) | Docker |

CI runs all of these on every push to `main` and `wip/**` against the official Jellyfin 12.4
image with Jellyfin Enhanced 12.14 and writes everything to the `ci-results` branch. CI is the
final word; local runs are for speed.

## Git and releases

- Unfinished work goes on a `wip/` branch, never `main`. Commit and push after each working
  piece (about 15 minutes at most), with `docs/HANDOFF.md` updated in the same commit.
- Releases are started by hand by Michael: GitHub > Actions > CI > Run workflow > `main` >
  tick "release". GitHub creates the tag after every check passes; the bot adds the
  `manifest.json` entry.
- Never change the plugin GUID `0fe4dc69-b52d-40bc-ad8d-ff518bad55a3`. Version lives only in
  `Directory.Build.props`.

## Michael's server (for deploy and verify instructions)

Unraid server "FractalAnomaly", official image `jellyfin/jellyfin:latest`, container named
`Jellyfin` (capital J). `/mnt/user/appdata/Jellyfin` is `/config`; plugins in
`/config/plugins/`, plugin data in `/config/plugins/configurations/`. Docker network `proxynet`.
Jellyfin 12.4, Jellyfin Enhanced 12.14.0.0, plus many other web-page plugins (File
Transformation, JavaScript Injector, Custom Tabs, Plugin Pages, Jellyfin Tweaks, KefinTweaks).

## Files that stay off GitHub

- Michael's theme CSS (it moves to Project Themes later): keep a copy in `local/` (ignored).
- Anything with tokens, API keys, webhook URLs or server captures: `.env*`, `secrets/`,
  `captures/`, `*.local.*`, `CLAUDE.local.md` are all ignored.

## Design documents (outside the repo)

- Project A design doc: https://claude.ai/artifact/EzgazWzknCV24WasU1XSpQ
- Project Modular Bot: https://claude.ai/artifact/HXpzXuQ12K3kDsXRrv951v
- Project Themes: https://claude.ai/artifact/RzK4fCZtFomGqNK7u4ChWx

The repo is the source of truth for decisions; when a decision changes, update the repo docs
first and tell Michael which design doc section to update.
