// SPDX-License-Identifier: GPL-3.0-only
// Test harness: serves a Jellyfin-like page built from captured Jellyfin 12.4 markup and
// styles, optionally with Jellyfin Enhanced's real header-actions.js, plus Project A's
// client script; and launches headless Chrome.
//
// Chrome: set CHROME_PATH (CI uses the runner's Google Chrome); otherwise the
// @sparticuz/chromium build from npm is used.

import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import puppeteer from 'puppeteer-core';

const here = path.dirname(fileURLToPath(import.meta.url));
const repo = path.resolve(here, '..', '..');
const read = (p) => fs.readFileSync(p, 'utf8');
const stripComment = (html) => html.replace(/^<!--[\s\S]*?-->\n/, '');

export const files = {
    header: stripComment(read(path.join(here, 'fixtures/header-jf12.4.html'))),
    jeNative: stripComment(read(path.join(here, 'fixtures/je-native-buttons.html'))),
    jeHeaderActions: read(path.join(here, 'vendor/je-header-actions.d822e14.js')),
    clientPath: path.join(repo, 'src/Jellyfin.Plugin.ProjectA/Web/client/projectA.js'),
};

const fontCss = `
@font-face { font-family: "Material Icons"; font-display: block; src: url("/nm/material-design-icons-iconfont/dist/fonts/MaterialIcons-Regular.woff2") format("woff2"); }
@font-face { font-family: "Noto Sans"; font-weight: 400; src: url("/nm/@fontsource/noto-sans/files/noto-sans-latin-400-normal.woff2") format("woff2"); }
@font-face { font-family: "Noto Sans"; font-weight: 500; src: url("/nm/@fontsource/noto-sans/files/noto-sans-latin-500-normal.woff2") format("woff2"); }
@font-face { font-family: "Noto Sans"; font-weight: 600; src: url("/nm/@fontsource/noto-sans/files/noto-sans-latin-600-normal.woff2") format("woff2"); }
@font-face { font-family: "Noto Sans"; font-weight: 700; src: url("/nm/@fontsource/noto-sans/files/noto-sans-latin-700-normal.woff2") format("woff2"); }`;

// Stand-ins for the four Jellyfin Enhanced services header-actions.js calls. Controllable
// from tests through window.__je.
const jeStubs = `
window.__je = { userId: 'user-1', userChange: [] };
window.JellyfinEnhanced = {
  t: (key) => key,
  session: {
    getUserId: () => window.__je.userId,
    onUserChange: (id, cb) => window.__je.userChange.push(cb),
  },
  helpers: {
    addCSS(id, css) { if (document.getElementById(id)) return; const s = document.createElement('style'); s.id = id; s.textContent = css; document.head.appendChild(s); },
    onBodyMutation(id, cb) { new MutationObserver(cb).observe(document.body, { childList: true, subtree: true }); },
    // The element the real helpers.getHeaderRightContainer() returns for Jellyfin 12's MUI toolbar.
    getHeaderRightContainer() {
      const user = document.querySelector('[aria-controls="app-user-menu"]');
      const toolbar = user && user.closest('.MuiToolbar-root');
      if (!toolbar) return null;
      let box = user; while (box.parentElement !== toolbar) box = box.parentElement;
      return box.previousElementSibling;
    },
  },
};`;

// What JE's own features do after getTray(): add their buttons to it.
const jeFeatures = (nativeHtml) => `
(function addJeButtons() {
  const tray = window.JellyfinEnhanced.headerActions.getTray();
  if (!tray) { setTimeout(addJeButtons, 20); return; }
  const t = document.createElement('template'); t.innerHTML = ${JSON.stringify(nativeHtml)};
  tray.insertBefore(t.content, tray.querySelector('#je-header-launcher'));
})();`;

/**
 * Builds a test page.
 * @param {object} o
 * @param {boolean} [o.je=true] load Jellyfin Enhanced's header code
 * @param {string|null} [o.userId='user-1'] signed-in user (null = signed out)
 * @param {boolean} [o.jeLate=false] load JE 300 ms after Project A
 * @param {number} [o.injectTwice=0] extra copies of Project A's script tag
 * @param {string} [o.extra=''] extra script run before Project A loads
 */
export function buildPage({ je = true, userId = 'user-1', jeLate = false, injectTwice = 0, extra = '' } = {}) {
    const jeBlock = `<script>${jeStubs}</script><script>${files.jeHeaderActions}</script><script>${jeFeatures(files.jeNative)}</script>`;
    const ours = '<script src="/client/projectA.js" version="0.1.0.0-test" defer></script>';
    return `<!doctype html>
<html class="layout-desktop" data-theme="dark" lang="en"><head><meta charset="utf-8">
<link rel="stylesheet" href="/fixtures/styles-jf12.4-je12.14.css"><style>${fontCss}</style></head>
<body class="jellyfin-v12 libraryDocument withSectionTabs">
${files.header}
<div class="mainAnimatedPages skinBody">
  <div id="indexPage" class="page homePage libraryPage mainAnimatedPage"><h2 class="sectionTitle">Home</h2></div>
</div>
<script>
window.__user = ${JSON.stringify(userId)};
window.ApiClient = { getCurrentUserId: () => window.__user };
${extra}
</script>
${je && !jeLate ? jeBlock : ''}
${ours}${ours.repeat(injectTwice)}
${je && jeLate ? `<script>setTimeout(() => { for (const s of ${JSON.stringify([jeStubs, files.jeHeaderActions, jeFeatures(files.jeNative)])}) { const el = document.createElement('script'); el.textContent = s; document.body.appendChild(el); } }, 300);</script>` : ''}
</body></html>`;
}

/** Starts a local server. pages: Map of path -> html. Returns { url, close }. */
export async function startServer(pages) {
    const types = { '.css': 'text/css', '.js': 'application/javascript', '.woff2': 'font/woff2', '.html': 'text/html' };
    const server = http.createServer((req, res) => {
        const url = new URL(req.url, 'http://x');
        let file = null;
        if (pages.has(url.pathname)) {
            res.writeHead(200, { 'content-type': 'text/html' });
            res.end(pages.get(url.pathname));
            return;
        }
        if (url.pathname === '/client/projectA.js') file = files.clientPath;
        else if (url.pathname.startsWith('/fixtures/')) file = path.join(here, url.pathname);
        else if (url.pathname.startsWith('/nm/')) file = path.join(here, 'node_modules', url.pathname.slice(4));
        if (!file || !path.resolve(file).startsWith(repo) || !fs.existsSync(file)) {
            res.writeHead(404); res.end(); return;
        }
        res.writeHead(200, { 'content-type': types[path.extname(file)] || 'application/octet-stream' });
        fs.createReadStream(file).pipe(res);
    });
    await new Promise((r) => server.listen(0, '127.0.0.1', r));
    const { port } = server.address();
    return { url: `http://127.0.0.1:${port}`, close: () => new Promise((r) => server.close(r)) };
}

export async function launchBrowser() {
    let executablePath = process.env.CHROME_PATH;
    let args = ['--no-sandbox'];
    if (!executablePath) {
        const chromium = (await import('@sparticuz/chromium')).default;
        executablePath = await chromium.executablePath();
        args = chromium.args;
    }
    return puppeteer.launch({ executablePath, args, headless: true });
}

/** Opens a page, collects errors, waits for fonts and two frames. */
export async function open(browser, url, { width = 1600, height = 700 } = {}) {
    const page = await browser.newPage();
    const errors = [];
    page.on('pageerror', (e) => errors.push(e.message));
    await page.setViewport({ width, height });
    await page.goto(url, { waitUntil: 'networkidle0' });
    await settle(page);
    return { page, errors };
}

export async function settle(page, ms = 150) {
    await page.evaluate(() => document.fonts.ready);
    await page.evaluate(() => new Promise((r) => requestAnimationFrame(() => requestAnimationFrame(r))));
    await new Promise((r) => setTimeout(r, ms));
}

/** Where the Activity Feed button is: 'tray' | 'tray-overflowed' | 'fallback' | 'none' | 'elsewhere'. */
export function buttonPlace(page) {
    return page.evaluate(() => {
        const all = document.querySelectorAll('#projectA-feed-button');
        if (all.length === 0) return 'none';
        if (all.length > 1) return 'duplicated';
        const b = all[0];
        if (b.parentElement && b.parentElement.id === 'je-header-buttons-group') {
            return b.classList.contains('je-header-overflowed') ? 'tray-overflowed' : 'tray';
        }
        if (b.parentElement && b.parentElement.matches('.MuiToolbar-root > .MuiBox-root')) return 'fallback';
        return 'elsewhere';
    });
}
