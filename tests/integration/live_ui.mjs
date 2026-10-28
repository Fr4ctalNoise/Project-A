// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 Fr4ctalNoise and Project A contributors.
//
// Live UI test against the throwaway Jellyfin 12.4 + Jellyfin Enhanced 12.14 server that
// live_server.py leaves running (--keep). Signs in as an ordinary user through the real login
// page and checks the Activity Feed button, the More menu and the #/social page.
//
//   CHROME_PATH=/usr/bin/google-chrome node tests/integration/live_ui.mjs <credentials.json> <out-dir>
//
// Run from tests/browser (for puppeteer-core). Screenshots go to <out-dir>; they show the
// throwaway server only. Writes summary.json; exits 1 on any failure.

import fs from 'node:fs';
import path from 'node:path';
import { createRequire } from 'node:module';

const require = createRequire(path.join(process.cwd(), 'package.json'));
const puppeteer = require('puppeteer-core');

const [credsPath, outDir] = process.argv.slice(2);
const creds = JSON.parse(fs.readFileSync(credsPath, 'utf8'));
fs.mkdirSync(outDir, { recursive: true });

const results = [];
const check = (name, ok, detail = '') => {
    results.push({ check: name, ok: !!ok, detail });
    console.log(`${ok ? 'PASS' : 'FAIL'} ${name}${detail ? `  (${detail})` : ''}`);
    return ok;
};
const shot = (page, name, clip) => page.screenshot({ path: path.join(outDir, `${name}.png`), ...(clip ? { clip } : {}) });
const wait = (ms) => new Promise((r) => setTimeout(r, ms));

const browser = await puppeteer.launch({ executablePath: process.env.CHROME_PATH, args: ['--no-sandbox'], headless: true });
const page = await browser.newPage();
const consoleProblems = [];
page.on('pageerror', (e) => consoleProblems.push(`pageerror: ${e.message}`));
page.on('console', (m) => { if (/Project A/.test(m.text())) consoleProblems.push(`${m.type()}: ${m.text()}`); });

try {
    await page.setViewport({ width: 1600, height: 900 });
    await page.goto(`${creds.base}/web/`, { waitUntil: 'networkidle2', timeout: 90000 });

    // Signed out: the script is present but adds nothing.
    await wait(1500);
    check('signed out: no Activity Feed button on the login page', (await page.$('#projectA-feed-button')) === null);
    await page.goto(`${creds.base}/web/#/social`, { waitUntil: 'networkidle2' });
    await wait(1500);
    check('signed out: #/social shows no Project A page', (await page.$('#projectA-social-page')) === null);
    await shot(page, '0-signed-out-social-link');

    // Sign in through the real form.
    await page.goto(`${creds.base}/web/#/login`, { waitUntil: 'networkidle2' });
    // Jellyfin keeps earlier pages in the document (hidden), and may show either the user
    // list or the manual form first. Work only with what is actually visible.
    const ready = await page.waitForFunction(() => {
        const visible = (el) => !!el && el.offsetParent !== null && getComputedStyle(el).visibility !== 'hidden';
        const name = [...document.querySelectorAll('#txtManualName')].find(visible);
        if (name) return 'form';
        const manual = [...document.querySelectorAll('.btnManual')].find(visible);
        if (manual) { manual.click(); }
        return false;
    }, { timeout: 60000, polling: 500 }).then(() => true, () => false);
    if (!check('login form shown', ready)) throw new Error('no visible login form');
    await page.evaluate((user, password) => {
        const visible = (el) => !!el && el.offsetParent !== null;
        const set = (sel, value) => {
            const el = [...document.querySelectorAll(sel)].find(visible);
            el.focus(); el.value = value; el.dispatchEvent(new Event('input', { bubbles: true })); el.dispatchEvent(new Event('change', { bubbles: true }));
            return el;
        };
        const name = set('#txtManualName', user);
        set('#txtManualPassword', password);
        name.closest('form').requestSubmit();
    }, creds.user, creds.password);

    const appeared = await page.waitForSelector('#projectA-feed-button', { timeout: 60000 }).then(() => true, () => false);
    check('signed in: Activity Feed button appears', appeared);
    await wait(1500);
    const place = await page.evaluate(() => {
        const b = document.getElementById('projectA-feed-button');
        return b && { parent: b.parentElement.id, overflowed: b.classList.contains('je-header-overflowed'), label: b.dataset.headerLabel, icon: b.textContent.trim() };
    });
    check('button is inside Jellyfin Enhanced\'s group', place && place.parent === 'je-header-buttons-group', JSON.stringify(place));
    check('button label and icon', place && place.label === 'Activity Feed' && place.icon === 'dynamic_feed');
    check('full width: button visible (not folded)', place && !place.overflowed);
    await shot(page, '1-header-wide', { x: 0, y: 0, width: 1600, height: 80 });

    // Narrow the window step by step (desktop layout, above the ~900 px point where Jellyfin
    // moves library links into its menu) until Jellyfin Enhanced folds its buttons into More.
    let foldWidth = 0;
    for (const w of [1400, 1300, 1200, 1100, 1050, 1000, 960, 920]) {
        await page.setViewport({ width: w, height: 900 });
        await wait(1200);
        if (await page.$eval('#projectA-feed-button', (b) => b.classList.contains('je-header-overflowed'))) { foldWidth = w; break; }
    }
    check('narrowing the window: the button folds into More', foldWidth > 0, foldWidth ? `folded at ${foldWidth} px` : 'never folded');
    if (!foldWidth) throw new Error('header never crowded enough to fold');
    await shot(page, '2-header-narrow', { x: 0, y: 0, width: foldWidth, height: 80 });
    await page.click('#je-header-launcher');
    await wait(800);
    const labels = await page.$$eval('#je-header-launcher-panel .je-launcher-action:not([hidden]) .je-launcher-label', (els) => els.map((e) => e.textContent));
    check('More menu lists "Activity Feed"', labels.includes('Activity Feed'), labels.join(', '));
    await shot(page, '3-more-menu');
    await page.evaluate(() => [...document.querySelectorAll('#je-header-launcher-panel .je-launcher-action')].find((b) => b.textContent.includes('Activity Feed')).click());
    await wait(1500);

    const shown = await page.evaluate(() => {
        const p = document.getElementById('projectA-social-page');
        const others = [...document.querySelectorAll('.mainAnimatedPage:not(.hide)')].filter((x) => x !== p).length;
        return { hash: location.hash, visible: !!p && !p.classList.contains('hide'), othersVisible: others, inMain: !!p && !!p.closest('.mainAnimatedPages') };
    });
    check('choosing it opens #/social with the placeholder page', shown.hash === '#/social' && shown.visible, JSON.stringify(shown));
    check('the previous page is hidden while ours shows', shown.othersVisible === 0);
    check('the page sits in Jellyfin\'s page area', shown.inMain);
    await page.setViewport({ width: 1600, height: 900 });
    await wait(800);
    await shot(page, '4-social-page');

    await page.evaluate(() => { location.hash = '#/home'; });
    await wait(2000);
    const back = await page.evaluate(() => ({
        ours: document.getElementById('projectA-social-page')?.classList.contains('hide'),
        home: !!document.querySelector('.mainAnimatedPage:not(.hide):not(#projectA-social-page)'),
    }));
    check('leaving restores Jellyfin\'s page', back.ours === true && back.home, JSON.stringify(back));

    const problems = await page.evaluate(() => window.ProjectA && window.ProjectA.problems());
    check('no problems reported by the script', Array.isArray(problems) && problems.length === 0, JSON.stringify(problems));
    check('no page errors or Project A console warnings', consoleProblems.length === 0, consoleProblems.join(' | '));
} catch (e) {
    check('UI test ran to completion', false, e.message);
    await shot(page, 'error').catch(() => {});
} finally {
    await browser.close();
    const failed = results.filter((r) => !r.ok).length;
    fs.writeFileSync(path.join(outDir, 'summary.json'), JSON.stringify({ passed: results.length - failed, failed, results }, null, 2));
    console.log(`\n${results.length - failed} passed, ${failed} failed`);
    process.exitCode = failed ? 1 : 0;
}
