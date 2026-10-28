// SPDX-License-Identifier: GPL-3.0-only
// Browser tests for the Spike's client script (header button + #/social page).
// Run: npm test (in tests/browser). See harness.mjs for what is real and what is stubbed.

import { test, before, after } from 'node:test';
import assert from 'node:assert/strict';
import { buildPage, startServer, launchBrowser, open, settle, buttonPlace } from './harness.mjs';

const pages = new Map([
    ['/je.html', buildPage()],
    ['/no-je.html', buildPage({ je: false })],
    ['/signed-out.html', buildPage({ userId: null })],
    ['/je-late.html', buildPage({ jeLate: true })],
    ['/router.html', buildPage({ router: true })],
    // Counts watchers registered by projectA.js itself (stack-attributed), to prove one set.
    ['/twice.html', buildPage({ injectTwice: 2, extra: `
        window.__ours = { listeners: 0, observers: 0 };
        const fromUs = () => (new Error().stack || '').includes('/client/projectA.js');
        const realAdd = window.addEventListener.bind(window);
        window.addEventListener = (type, ...rest) => { if (fromUs()) window.__ours.listeners++; return realAdd(type, ...rest); };
        const RealMO = window.MutationObserver;
        window.MutationObserver = class extends RealMO { constructor(cb) { super(cb); if (fromUs()) window.__ours.observers++; } };` })],
    // Hostile: a future JE whose getTray throws.
    ['/je-broken.html', buildPage({ extra: '', je: true }).replace(
        '<script src="/client/projectA.js"',
        '<script>window.JellyfinEnhanced.headerActions.getTray = () => { throw new Error("tray changed"); };</script><script src="/client/projectA.js"')],
    // Hostile: another plugin's link sits where the fallback box would be.
    ['/foreign-link.html', buildPage({ je: false }).replace(
        '<div class="MuiBox-root css-1riowxi">',
        '<a class="MuiBox-root css-1riowxi" href="#/elsewhere">')
        .replace(/(<a class="MuiBox-root css-1riowxi"[\s\S]*?)<\/div>(<div class="MuiBox-root css-2uchni">)/, '$1</a>$2')],
]);

let server;
let browser;

before(async () => {
    server = await startServer(pages);
    browser = await launchBrowser();
});

after(async () => {
    await browser?.close();
    await server?.close();
});

test('with Jellyfin Enhanced, the button joins its group and shows at full width', async () => {
    const { page, errors } = await open(browser, `${server.url}/je.html`, { width: 1600 });
    assert.equal(await buttonPlace(page), 'tray');
    const attrs = await page.$eval('#projectA-feed-button', (b) => ({
        title: b.title, aria: b.getAttribute('aria-label'), label: b.dataset.headerLabel,
        icon: b.querySelector('.material-icons').textContent, type: b.type,
        visible: b.getBoundingClientRect().width > 0,
    }));
    assert.deepEqual(attrs, { title: 'Activity Feed', aria: 'Activity Feed', label: 'Activity Feed', icon: 'dynamic_feed', type: 'button', visible: true });
    assert.deepEqual(errors, []);
    await page.close();
});

test('narrow window: the button folds into More and is listed there as "Activity Feed"', async () => {
    const { page, errors } = await open(browser, `${server.url}/je.html`, { width: 820 });
    assert.equal(await buttonPlace(page), 'tray-overflowed');
    await page.click('#je-header-launcher');
    await settle(page);
    const entries = await page.$$eval('#je-header-launcher-panel .je-launcher-action:not([hidden]) .je-launcher-label', (els) => els.map((e) => e.textContent));
    assert.ok(entries.includes('Activity Feed'), `More menu entries: ${entries.join(', ')}`);

    // Choosing it in More opens the page.
    await page.evaluate(() => {
        const entry = [...document.querySelectorAll('#je-header-launcher-panel .je-launcher-action')]
            .find((b) => b.textContent.includes('Activity Feed'));
        entry.click();
    });
    await settle(page, 300);
    assert.equal(await page.evaluate(() => location.hash), '#/social');
    assert.equal(await page.$eval('#projectA-social-page', (p) => !p.classList.contains('hide')), true);
    assert.deepEqual(errors, []);
    await page.close();
});

test('clicking the button shows the placeholder page and hides the current page', async () => {
    const { page } = await open(browser, `${server.url}/je.html`);
    await page.click('#projectA-feed-button');
    await settle(page, 300);
    const s = await page.evaluate(() => ({
        hash: location.hash,
        ours: !document.getElementById('projectA-social-page').classList.contains('hide'),
        home: document.getElementById('indexPage').classList.contains('hide'),
        heading: document.querySelector('#projectA-social-page .sectionTitle').textContent,
        text: document.querySelector('#projectA-social-page .projectA-placeholder').textContent,
        insideMain: !!document.querySelector('.mainAnimatedPages > #projectA-social-page'),
    }));
    assert.deepEqual(s, {
        hash: '#/social', ours: true, home: true, heading: 'Activity Feed',
        text: 'Project A is installed and working. The feed arrives in the next release.', insideMain: true,
    });
    await page.close();
});

test('leaving the page by Jellyfin navigation (pushState, no event) restores the previous page', async () => {
    const { page } = await open(browser, `${server.url}/je.html#/social`);
    await settle(page, 200);
    assert.equal(await page.$eval('#projectA-social-page', (p) => p.classList.contains('hide')), false);
    await page.evaluate(() => history.pushState({}, '', '#/home'));
    await new Promise((r) => setTimeout(r, 600)); // watcher polls every 250 ms
    const s = await page.evaluate(() => ({
        ours: document.getElementById('projectA-social-page').classList.contains('hide'),
        home: document.getElementById('indexPage').classList.contains('hide'),
    }));
    assert.deepEqual(s, { ours: true, home: false });
    await page.close();
});

test('the location watcher only runs while the page is showing', async () => {
    const { page } = await open(browser, `${server.url}/je.html`);
    const count = () => page.evaluate(() => window.__intervals);
    await page.evaluate(() => {
        window.__intervals = 0;
        const real = window.setInterval;
        window.setInterval = (...a) => { window.__intervals++; return real(...a); };
    });
    await page.evaluate(() => { location.hash = '#/social'; });
    await settle(page, 200);
    await page.evaluate(() => { location.hash = '#/home'; });
    await settle(page, 400);
    await page.evaluate(() => { location.hash = '#/social'; });
    await settle(page, 200);
    assert.equal(await count(), 2, 'one interval per visit, none left running in between');
    await page.close();
});

test('only #/social and #/social?… are our route', async () => {
    const { page } = await open(browser, `${server.url}/je.html`);
    for (const [hash, ours] of [['#/socialx', false], ['#/social/../home', false], ['#/social?x=1', true], ['#/Social', false]]) {
        await page.evaluate((h) => { location.hash = h; }, hash);
        await settle(page, 300);
        const shown = await page.evaluate(() => {
            const p = document.getElementById('projectA-social-page');
            return !!p && !p.classList.contains('hide');
        });
        assert.equal(shown, ours, hash);
        await page.evaluate(() => { location.hash = '#/home'; });
        await settle(page, 300);
    }
    await page.close();
});

test('without Jellyfin Enhanced, the button goes into the header\'s own button box', async () => {
    const { page, errors } = await open(browser, `${server.url}/no-je.html`);
    assert.equal(await buttonPlace(page), 'fallback');
    assert.deepEqual(errors, []);
    await page.close();
});

test('signed out: no button, and #/social does not show the page', async () => {
    const { page } = await open(browser, `${server.url}/signed-out.html#/social`);
    await settle(page, 300);
    assert.equal(await buttonPlace(page), 'none');
    assert.equal(await page.$('#projectA-social-page'), null);
    await page.close();
});

test('signing out while on the page removes the page and the button', async () => {
    const { page } = await open(browser, `${server.url}/je.html#/social`);
    await settle(page, 200);
    await page.evaluate(() => {
        window.__user = null; window.__je.userId = null;
        for (const cb of window.__je.userChange) cb(); // what JE does on a user change
        document.body.appendChild(document.createElement('span')); // any DOM change triggers a pass
    });
    await settle(page, 300);
    assert.equal(await buttonPlace(page), 'none');
    assert.equal(await page.$('#projectA-social-page'), null);
    assert.equal(await page.$eval('#indexPage', (p) => p.classList.contains('hide')), false);
    await page.close();
});

test('switching user: the button comes back for the new user', async () => {
    const { page } = await open(browser, `${server.url}/je.html`);
    await page.evaluate(() => {
        for (const cb of window.__je.userChange) cb(); // JE empties its tray on a user change
        window.__user = 'user-2'; window.__je.userId = 'user-2';
        document.body.appendChild(document.createElement('span'));
    });
    await settle(page, 300);
    assert.equal(await buttonPlace(page), 'tray');
    await page.close();
});

test('Jellyfin Enhanced loading after Project A: the button moves into its group', async () => {
    const { page } = await open(browser, `${server.url}/je-late.html`);
    await new Promise((r) => setTimeout(r, 700));
    await settle(page);
    assert.equal(await buttonPlace(page), 'tray');
    await page.close();
});

test('script tag injected three times: runs once (one observer, two listeners), one button', async () => {
    const { page, errors } = await open(browser, `${server.url}/twice.html`);
    assert.equal(await buttonPlace(page), 'tray');
    assert.deepEqual(await page.evaluate(() => window.__ours), { listeners: 2, observers: 1 });
    assert.deepEqual(errors, []);
    await page.close();
});

test('a Jellyfin Enhanced change that breaks getTray: fallback placement, one warning, no errors', async () => {
    const { page, errors } = await open(browser, `${server.url}/je-broken.html`);
    assert.equal(await buttonPlace(page), 'fallback');
    assert.deepEqual(await page.evaluate(() => window.ProjectA.problems()), ['je-tray']);
    assert.deepEqual(errors, []);
    await page.close();
});

test('never puts the button inside another plugin\'s link or button', async () => {
    const { page, errors } = await open(browser, `${server.url}/foreign-link.html`);
    assert.equal(await buttonPlace(page), 'none');
    assert.deepEqual(errors, []);
    await page.close();
});

test('a burst of 2,000 DOM changes costs at most a pass per frame, with no tray calls once placed', async () => {
    const { page } = await open(browser, `${server.url}/je.html`);
    const calls = await page.evaluate(async () => {
        let trayCalls = 0;
        const real = window.JellyfinEnhanced.headerActions.getTray;
        window.JellyfinEnhanced.headerActions.getTray = (...a) => { trayCalls++; return real(...a); };
        const box = document.createElement('div');
        document.body.appendChild(box);
        for (let i = 0; i < 2000; i++) box.appendChild(document.createElement('i'));
        await new Promise((r) => requestAnimationFrame(() => requestAnimationFrame(r)));
        return trayCalls;
    });
    assert.equal(calls, 0);
    await page.close();
});

test('the script exposes nothing but a frozen version/problems object', async () => {
    const { page } = await open(browser, `${server.url}/je.html`);
    const r = await page.evaluate(() => ({
        keys: Object.keys(window.ProjectA).sort(),
        frozen: Object.isFrozen(window.ProjectA),
        version: window.ProjectA.version,
    }));
    assert.deepEqual(r, { keys: ['problems', 'version'], frozen: true, version: '0.1.0.0-test' });
    await page.close();
});

// Regression: on a live Jellyfin 12.4 server, Jellyfin's "Page not found" showed on top of
// ours, because the button changed location.hash (which the router reacts to) and because the
// router renders its not-found page for #/social on a direct visit. The router stub in
// harness.mjs behaves the same way.
const visiblePages = (page) => page.evaluate(() =>
    [...document.querySelectorAll('.mainAnimatedPage')].filter((p) => !p.classList.contains('hide')).map((p) => p.id));

test('Jellyfin router: the button opens our page without the router reacting', async () => {
    const { page } = await open(browser, `${server.url}/router.html`);
    await page.click('#projectA-feed-button');
    await settle(page, 300);
    assert.equal(await page.evaluate(() => location.hash), '#/social');
    assert.equal(await page.evaluate(() => window.__routerRenders), 0, 'navigated without a hash change the router sees');
    assert.deepEqual(await visiblePages(page), ['projectA-social-page']);
    await page.close();
});

test('Jellyfin router: a direct visit to #/social shows only our page, not "Page not found"', async () => {
    const { page } = await open(browser, `${server.url}/router.html#/social`);
    await settle(page, 400);
    assert.deepEqual(await visiblePages(page), ['projectA-social-page']);
    await page.close();
});

test('Jellyfin router: typing #/social after load shows only our page', async () => {
    const { page } = await open(browser, `${server.url}/router.html`);
    await page.evaluate(() => { location.hash = '#/social'; });
    await settle(page, 400);
    assert.deepEqual(await visiblePages(page), ['projectA-social-page']);
    await page.close();
});

test('Jellyfin router: leaving restores Jellyfin\'s page and leaves nothing we hid hidden', async () => {
    const { page } = await open(browser, `${server.url}/router.html#/social`);
    await settle(page, 400);
    await page.evaluate(() => { location.hash = '#/home'; });
    await settle(page, 600);
    assert.deepEqual(await visiblePages(page), ['indexPage']);
    await page.close();
});
