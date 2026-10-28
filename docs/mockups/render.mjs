// SPDX-License-Identifier: GPL-3.0-only
// Renders a mockup into the REAL captured Jellyfin 12.4 page (see README.md) and screenshots it.
//
//   node render.mjs <capture.html> <theme.css|-> <fonts node_modules dir> <out dir> <fragment.html> <feed.css> <name>
//
// capture.html: ci-results latest/live-ui/capture-social-page.html (scripts already stripped).
// The page's own stylesheet links point at the server, so they are replaced by the captured
// Jellyfin 12.4 + Jellyfin Enhanced styles (tests/browser/fixtures), then Michael's theme, then
// the proposed feed.css. Fonts come from npm packages (@fontsource, material-icons) because the
// workspace can't reach Google Fonts; they are the same font families the theme names.
// Mockup tooling only: never shipped, so it may use innerHTML.
import fs from 'node:fs';
import path from 'node:path';
import { createRequire } from 'node:module';
import { pathToFileURL } from 'node:url';

// require(), not import: the workspace's pre-installed Playwright is found through NODE_PATH,
// which ES module imports ignore.
const { chromium } = createRequire(import.meta.url)('playwright');

const [capture, theme, fonts, outDir, fragmentPath, feedCss, name] = process.argv.slice(2);
const repo = path.resolve(path.dirname(new URL(import.meta.url).pathname), '..', '..');
const url = (p) => pathToFileURL(path.resolve(p)).href;
fs.mkdirSync(outDir, { recursive: true });

let html = fs.readFileSync(capture, 'utf8')
    .replace(/<link[^>]+rel="?stylesheet"?[^>]*>/gi, '')
    .replace(/<link[^>]+rel="?(manifest|apple-touch-icon|shortcut icon)"?[^>]*>/gi, '');

const fontCss = [
    'material-icons/iconfont/filled.css',
    '@fontsource/noto-sans/400.css', '@fontsource/noto-sans/600.css', '@fontsource/noto-sans/700.css',
    '@fontsource/creepster/400.css', '@fontsource/nosifer/400.css', '@fontsource/oswald/600.css',
    '@fontsource/noto-color-emoji/400.css',
].map((f) => `<link rel="stylesheet" href="${url(path.join(fonts, f))}">`);

const styles = [
    ...fontCss,
    `<link rel="stylesheet" href="${url(path.join(repo, 'tests/browser/fixtures/styles-jf12.4-je12.14.css'))}">`,
];
if (theme && theme !== '-') {
    // The theme's first rule imports Google Fonts, which the workspace can't reach; the same
    // families are loaded above instead.
    const themeCopy = path.join(outDir, 'theme-local.css');
    fs.writeFileSync(themeCopy, fs.readFileSync(theme, 'utf8').replace(/@import url\([^)]*fonts\.googleapis[^)]*\);/, ''));
    styles.push(`<link rel="stylesheet" href="${url(themeCopy)}">`);
}
styles.push(`<link rel="stylesheet" href="${url(feedCss)}">`);
html = html.replace('</head>', styles.join('\n') + '\n</head>');

const assembled = path.join(outDir, `${name}.assembled.html`);
fs.writeFileSync(assembled, html);
const fragment = fs.readFileSync(fragmentPath, 'utf8');

const browser = await chromium.launch();
for (const view of [
    { suffix: 'desktop', width: 1600, height: 1000, scale: 1, mobile: false },
    { suffix: 'phone', width: 390, height: 844, scale: 2, mobile: true },
]) {
    const page = await browser.newPage({ viewport: { width: view.width, height: view.height }, deviceScaleFactor: view.scale });
    await page.goto(url(assembled));
    await page.evaluate(({ fragment, mobile }) => {
        const host = document.querySelector('#projectA-social-page .content-primary');
        host.innerHTML = fragment;
        if (mobile) {
            document.documentElement.classList.remove('layout-desktop');
            document.documentElement.classList.add('layout-mobile');
        }
        window.scrollTo(0, 0);
    }, { fragment, mobile: view.mobile });
    await page.evaluate(() => document.fonts.ready);
    // Grow the window to the content, so fixed backgrounds and decorations cover the whole shot.
    const height = await page.evaluate(() => document.documentElement.scrollHeight);
    await page.setViewportSize({ width: view.width, height: Math.max(view.height, height) });
    await page.waitForTimeout(300);
    await page.screenshot({ path: path.join(outDir, `${name}-${view.suffix}.png`), fullPage: true });
    await page.close();
}
await browser.close();
console.log(`rendered ${name}`);
