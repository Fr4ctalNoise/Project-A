# Architecture: how Project A is built to change

Read with `PRINCIPLES.md`. This file says how the pieces fit, so features, layout, looks and
wording can change later without rewriting what already works. It also fixes the contracts
between Project A and its two sister projects, **Project Themes** (the seasonal theme as a
plugin) and **Project Modular Bot** (the Discord bot, formerly je-review-notifier), which come
later but must slot in without changes here.

Status: agreed 2026-10-28. The server side already follows it (step 2 part 1). The page side is
built to it in step 2 part 3a, before the feed itself.

---

## 1. The three projects and how they touch

```
                 ┌──────────────────────── Jellyfin 12 server ────────────────────────┐
                 │                                                                     │
 Browser ◄──page─┤  Project A plugin                      Project Themes plugin        │
 (Jellyfin web)  │   • injects its script                  • injects its stylesheet    │
                 │   • /Social/* endpoints                 • /Themes/* endpoints       │
                 │   • reads --t-* theme settings ◄─CSS──  • defines --t-* settings    │
                 │   • exposes --pa-* settings  ──CSS──►   • may set --pa-* per season │
                 │   • GET /Social/Health ◄──┐             • GET /Themes/Health ◄──┐   │
                 │   • GET /Social/Events ◄──┤ (later)                             │   │
                 └───────────────────────────┼─────────────────────────────────────┼───┘
                                             │  API key, read-only                 │
                                     Project Modular Bot (Docker on Unraid) ───────┘
                                       • health watch → admin webhook
                                       • post mirroring → Discord (later)
```

- **The page never depends on the theme being installed.** Every Project A style has a plain
  fallback. With Project Themes installed, the feed picks up the season automatically.
- **The theme never depends on Project A.** It styles Project A only through the documented
  `--pa-*` settings and hooks (section 5), which do nothing if Project A isn't there.
- **The bot only reads, with an API key**, through endpoints made for it (section 6). It never
  holds user tokens, never writes to Jellyfin, and the plugins never call out to Discord.

## 2. Server side (C#): one folder per feature

```
src/Jellyfin.Plugin.ProjectA/
  Core/        sign-in gate, database + upgrades, user visibility rule, text checks,
               error filter, feature switches, script/style injection     (shared, rarely changes)
  Features/
    Posts/     PostStore, PostsController, PostInput                      (step 2)
    Comments/  ...                                                         (step 3)
    Watch/     ...                                                         (step 4)
    ...
```

Rules:

1. **A feature owns its folder**: its store, its endpoints, its tests
   (`tests/.../Features/<Name>/`). It may use `Core`; it never reaches into another feature's
   folder. Shared needs move to `Core` first.
2. **A feature registers itself** in `PluginServiceRegistrator` with one line, and declares its
   database upgrade in `SocialStore.Migrations` (never edit a released one).
3. **Every endpoint passes the gate** (`[Authorize]` + `[SocialUser]`); the endpoint build test
   enforces it and feeds the live sign-in tests automatically.
4. **Feature switches** (admin page): `PluginConfiguration.DisabledFeatures` (a list of feature
   ids). A disabled feature's endpoints answer 404 and the page leaves it out. Core can't be
   switched off. *(Built in part 3a.)*
5. **One config answer for the page**: `GET /Social/Config` (signed-in) returns the plugin
   version, enabled features, limits (for example the 4,000-character post limit) and the
   viewer's role, so the page never hard-codes a server rule. *(Built in part 3a.)*
6. **Contracts are versioned** (section 7): JSON fields are only ever added; renaming or removing
   one is a breaking change that bumps the contract version.

*Today the step 2 files still sit in `Api/`, `Data/` and `Users/`; part 3a moves them into
`Core/` and `Features/Posts/` with no behaviour change.*

## 3. Page side (JavaScript): a small core plus modules

Separate source files, joined in a fixed order into the one `projectA.js` the server serves
(one small download, cached by version). A build test checks the served file matches the
sources and the order list.

```
Web/client/
  order.txt            the join order (core first, then modules)
  core/
    boot.js            starts once Jellyfin's ApiClient is ready; tracks the signed-in user;
                       tears everything down on sign-out or user switch
    api.js             the only code that calls the server: token in the header (never in URLs),
                       fixed error codes → wording, retry rules
    dom.js             el(tag, attrs, children) builder: text via textContent, never innerHTML
    markdown.js        markdown-it (html off) + DOMPurify → DOM nodes; https links only
    router.js          #/social and later routes: pushState, show/hide, Jellyfin page hand-back
    header.js          header buttons inside Jellyfin Enhanced's tray (folds into More)
    strings.js         every word the page shows, in one table
    registry.js        the module registry (below)
  modules/
    feed.js            the feed page: loading, automatic paging, empty and error states
    composer.js        writing a post
    post-status.js     how a text post looks
    menu-delete.js     the ⋯ menu's Delete action (+ confirmation)
    (later) post-watched.js, post-review.js, post-image.js, comments.js, likes.js,
            author-tags.js, author-roles.js, achievements.js, header-links.js
  styles/
    core.css, feed.css, ...   joined into one stylesheet the same way
```

**The registry** is the only way modules plug in, so any module can be removed by deleting its
file from `order.txt` (or switched off by the admin) without touching the others:

| Slot | What a module adds | Example |
| --- | --- | --- |
| `routes` | a page at a hash route | feed at `#/social`; later Recommended tab |
| `headerButtons` | a button in Jellyfin Enhanced's tray | Activity Feed; later Discord link |
| `postKinds` | how one kind of post renders | `status`, later `watched`, `review`, `image` |
| `authorParts` | items in the author column or name row, with an order | avatar, name, later tags, roles, badges |
| `postActions` | entries in a post's ⋯ menu | Delete; later Edit, Report |
| `feedTop` | blocks above the posts | composer; later sharing switch, one-time notice |

Each module declares `{ id, feature, slots }`. `feature` ties it to a server feature switch, so
the page drops it when `GET /Social/Config` says the feature is off. A module that throws is
caught, logged once and skipped; the rest of the page keeps working.

**Hooks for others** (Project Themes, custom scripts in JavaScript Injector): the page fires DOM
events on `document`: `projecta:page-shown`, `projecta:post-rendered` (detail: post id and kind
only, never content or tokens), `projecta:page-hidden`. Reacting to them is optional.

## 4. Layout and UX: decided by attributes and CSS, not code

- The feed root carries `data-pa-layout="side"` (portrait author column, the current design)
  or `"compact"` (small avatar in the post's top row). Phones get compact automatically. Both
  layouts use the **same markup**; only CSS differs, so a new layout is a stylesheet, not code.
- Behaviour choices that are taste rather than rules (automatic paging vs a button, how long a
  post is before "Show more") are named settings in one place in `core/strings.js` /
  `GET /Social/Config`, not scattered numbers.
- Wording lives in `core/strings.js` only, so rewording the page is one file (and translation
  becomes possible later).

## 5. Looks: the CSS contract with Project Themes

**Project A consumes** the theme's settings: `--t-*` (Part A of the theme: colours, fonts, radii,
panel look) and Jellyfin's `--jf-palette-*` (theme Part B). Every use has a plain fallback.

**Project A exposes** its own settings, each defaulting to a `--t-*` setting:

| Setting | Default |
| --- | --- |
| `--pa-feed-width` | 880px |
| `--pa-avatar-width` / `--pa-avatar-height` | 150px / 200px |
| `--pa-avatar-radius` / `--pa-avatar-fit` | 0 / contain |
| `--pa-silhouette-colour` / `--pa-silhouette-opacity` | `--t-text-soft` / 0.35 |
| `--pa-panel-bg` / `--pa-panel-border` / `--pa-panel-radius` | `--t-panel-bg` / `--t-panel-border` / `--t-radius` |
| `--pa-tag-bg` / `--pa-tag-border` / `--pa-tag-text` | `--t-chip-*` |
| `--pa-role-bg` / `--pa-role-border` | accent tint / `--t-accent` |

**Stable hooks** (never renamed without a contract version bump): classes `.projectA-feed`,
`.projectA-post`, `.projectA-side`, `.projectA-avatar`, `.projectA-avatar--none`,
`.projectA-namerow`, `.projectA-author`, `.projectA-tag`, `.projectA-tag--role`,
`.projectA-body`, `.projectA-composer`; attributes `data-pa-layout`, `data-pa-kind` (post
kind), `data-pa-tag` (tag slug), `data-pa-role` (role slug).

**Zero specificity:** Project A's own rules are written inside `:where(...)`, so any theme or
custom CSS rule beats them without `!important`, whatever order the stylesheets load in.

**Loading:** Project A's stylesheet is served by Project A (`GET /Social/styles`, anonymous like
the script: code, no data) and added by its script. Project Themes injects its own stylesheet
itself. Neither depends on load order because of `var()` fallbacks and zero specificity.

**What Project Themes may do:** set `--t-*` (its job) and, per season, any `--pa-*`, or style
the hooks above (for example a gold `[data-pa-tag="movie-night-host"]` at Christmas). It must
not rely on any other Project A class.

## 6. Contract with Project Modular Bot

- **Health** (exists): `GET /Social/Health`, admin or API key. The bot's health watch alerts
  once on break and once on recovery. Base fields shared by every Fr4ctalNoise plugin, so the bot
  checks Project Themes the same way (`GET /Themes/Health`):

  | Field | Meaning |
  | --- | --- |
  | `Version` | installed plugin version |
  | `ContractVersion` | the contract version in section 7 *(added in part 3a)* |
  | `ScriptInjectedSinceStart` / `ScriptInjectionDisabled` | page injection state (Themes: stylesheet) |
  | plugin-specific | Project A: `DatabaseOk`, `DatabaseProblem`, `SchemaVersion` |

  Also checked by the bot without any endpoint: plugin status in Jellyfin's plugin list, the
  Jellyfin version, and each plugin's tag in `/web/index.html`.
- **Events** (later, step 10 "mirror posts to Discord"): `GET /Social/Events?after=<cursor>`,
  API key only, read-only, returns new public posts as ids, kind, author name, time and text
  (never tokens, never anything a hidden or disabled author's rule would hide). The bot polls
  it; Project A never calls out, so no Discord webhook or secret is ever stored in Jellyfin.
  Admin switch to turn mirroring off.
- **Never:** the bot gets no user tokens, no write endpoints, and no access to anything the
  feed wouldn't show an ordinary signed-in user.

## 7. Contract versions

`ContractVersion` (an integer, starting at 1 in part 3a) covers section 5's settings and hooks
and section 6's JSON fields. Adding a field or a setting doesn't change it. Renaming or removing
one bumps it, is listed in the CHANGELOG under **Breaking for Project Themes / Modular Bot**, and
the sister project is updated in the same release window.

## 8. Shared conventions across the three repos

Each repo has the same `docs/PRINCIPLES.md` rules, its own `docs/HANDOFF.md`, CI writing to a
`ci-results` branch, releases started by hand after every check passes, and a version in one
place. Plugins: one GUID each, never changed; each has its own catalog `manifest.json`;
`autoUpdate: false`. Sister-project plans: the Project Modular Bot and Project Themes docs
(links in `docs/HANDOFF.md`).
