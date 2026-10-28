// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 Fr4ctalNoise and Project A contributors.
// Header fallback container adapted from Jellyfin Enhanced (GPL-3.0), commit d822e14,
// js/enhanced/helpers.js getHeaderRightContainer(); page show/hide follows
// js/enhanced/bookmarks/bookmarks-library-page.js. Copyright (c) n00bcodr and
// Jellyfin Enhanced contributors.
//
// Project A browser script (Spike). Served anonymously by GET /Social/script and
// injected into jellyfin-web's index.html. It holds no data and does nothing until
// a user is signed in. Adds:
//   - an "Activity Feed" header button. With Jellyfin Enhanced loaded it joins JE's
//     button group, so it folds into JE's More menu on narrow windows; without JE it
//     goes into the header's own button box.
//   - a placeholder page at #/social.
// Safety rules for this file: no innerHTML with dynamic values (text via textContent);
// no tokens in URLs; one set of watchers for the life of the page, never re-registered.

(function () {
    'use strict';

    if (window.ProjectA) {
        return; // Already running (the tag was injected twice by some other tool).
    }

    const BUTTON_ID = 'projectA-feed-button';
    const PAGE_ID = 'projectA-social-page';
    const ROUTE = '#/social';
    const LABEL = 'Activity Feed';
    const ICON = 'dynamic_feed';
    const LOCATION_POLL_MS = 250;

    const state = {
        pageVisible: false,
        previousPage: null,
        locationTimer: null,
        locationSignature: '',
        frame: 0,
    };

    // ---------------------------------------------------------------- session

    /** The signed-in user's id, or '' when nobody is signed in. Never throws. */
    function currentUserId() {
        try {
            const client = window.ApiClient;
            const id = client && typeof client.getCurrentUserId === 'function' ? client.getCurrentUserId() : '';
            return typeof id === 'string' ? id : '';
        } catch (e) {
            return '';
        }
    }

    // ----------------------------------------------------------------- header

    /**
     * The container the button belongs in right now, or null when the header
     * isn't ready (or nobody is signed in).
     */
    function targetContainer() {
        const je = window.JellyfinEnhanced;
        if (je && je.headerActions && typeof je.headerActions.getTray === 'function') {
            try {
                const tray = je.headerActions.getTray();
                if (tray) {
                    return tray;
                }
            } catch (e) {
                // A Jellyfin Enhanced change broke its tray: use our own placement.
                reportProblem('je-tray', e);
            }
        }

        return fallbackContainer();
    }

    /**
     * Without Jellyfin Enhanced: the legacy .headerRight when visible, otherwise the
     * Jellyfin 12 toolbar's native button box (the box before the user menu).
     */
    function fallbackContainer() {
        const legacy = document.querySelector('.headerRight');
        if (legacy && legacy.offsetParent !== null) {
            return legacy;
        }

        const userMenu = document.querySelector('[aria-controls="app-user-menu"]');
        const toolbar = userMenu ? userMenu.closest('.MuiToolbar-root') : null;
        if (!toolbar) {
            return null;
        }

        let userBox = userMenu;
        while (userBox && userBox.parentElement !== toolbar) {
            userBox = userBox.parentElement;
        }

        const box = userBox ? userBox.previousElementSibling : null;
        // Never put our button inside another plugin's button or link.
        if (!box || box.matches('button, a, [role="button"]')) {
            return null;
        }

        return box;
    }

    function createButton() {
        const button = document.createElement('button');
        button.type = 'button';
        button.id = BUTTON_ID;
        button.setAttribute('is', 'paper-icon-button-light');
        button.className = 'headerButton headerButtonRight paper-icon-button-light';
        button.title = LABEL;
        button.setAttribute('aria-label', LABEL);
        // Jellyfin Enhanced's More menu labels its entries from data-header-label.
        button.dataset.headerLabel = LABEL;

        const icon = document.createElement('span');
        icon.className = 'material-icons';
        icon.setAttribute('aria-hidden', 'true');
        icon.textContent = ICON;
        button.appendChild(icon);

        button.addEventListener('click', function () {
            if (window.location.hash !== ROUTE) {
                window.location.hash = ROUTE;
            } else {
                showPage();
            }
        });
        return button;
    }

    /** Puts the button where it belongs, or removes it when nobody is signed in. */
    function syncButton() {
        let button = document.getElementById(BUTTON_ID);
        if (!currentUserId()) {
            if (button) {
                button.remove();
            }
            return;
        }

        // Fast path for the common case (called on DOM changes): already in place.
        // In JE's tray, or in our fallback while JE isn't loaded, nothing to do.
        if (button && button.isConnected) {
            const inTray = button.parentElement && button.parentElement.id === 'je-header-buttons-group';
            const jeReady = !!(window.JellyfinEnhanced && window.JellyfinEnhanced.headerActions);
            if (inTray || !jeReady) {
                return;
            }
        }

        const container = targetContainer();
        if (!container) {
            return;
        }

        if (!button) {
            button = createButton();
        }

        if (button.parentElement !== container) {
            container.appendChild(button);
        }
    }

    // ------------------------------------------------------------------- page

    function isOurRoute() {
        const hash = window.location.hash;
        return hash === ROUTE || hash.startsWith(ROUTE + '?');
    }

    function createPage() {
        let page = document.getElementById(PAGE_ID);
        if (page) {
            return page;
        }

        page = document.createElement('div');
        page.id = PAGE_ID;
        page.className = 'page type-interior mainAnimatedPage hide';
        page.setAttribute('data-title', LABEL);
        page.setAttribute('data-url', ROUTE);
        page.setAttribute('data-type', 'custom');

        const content = document.createElement('div');
        content.setAttribute('data-role', 'content');
        const primary = document.createElement('div');
        primary.className = 'content-primary padded-left padded-right';
        const section = document.createElement('div');
        section.className = 'verticalSection';

        const heading = document.createElement('h2');
        heading.className = 'sectionTitle sectionTitle-cards';
        heading.textContent = LABEL;
        const note = document.createElement('p');
        note.className = 'projectA-placeholder';
        note.textContent = 'Project A is installed and working. The feed arrives in the next release.';

        section.append(heading, note);
        primary.appendChild(section);
        content.appendChild(primary);
        page.appendChild(content);

        const host = document.querySelector('.mainAnimatedPages');
        (host || document.body).appendChild(page);
        return page;
    }

    function showPage() {
        if (state.pageVisible || !currentUserId()) {
            return;
        }

        state.pageVisible = true;
        startLocationWatcher();
        const page = createPage();

        const active = document.querySelector('.mainAnimatedPage:not(.hide):not(#' + PAGE_ID + ')');
        if (active) {
            state.previousPage = active;
            active.classList.add('hide');
            active.dispatchEvent(new CustomEvent('viewhide', { bubbles: true, detail: { type: 'interior' } }));
        }

        page.classList.remove('hide');
        page.dispatchEvent(new CustomEvent('viewshow', { bubbles: true, detail: { type: 'custom', isRestored: false, options: {} } }));
        document.title = LABEL;
    }

    function hidePage() {
        if (!state.pageVisible) {
            return;
        }

        const page = document.getElementById(PAGE_ID);
        if (page) {
            page.classList.add('hide');
            page.dispatchEvent(new CustomEvent('viewhide', { bubbles: true, detail: { type: 'custom' } }));
        }

        const anotherShowing = document.querySelector('.mainAnimatedPage:not(.hide):not(#' + PAGE_ID + ')');
        if (state.previousPage && state.previousPage.isConnected && !anotherShowing) {
            state.previousPage.classList.remove('hide');
            state.previousPage.dispatchEvent(new CustomEvent('viewshow', { bubbles: true, detail: { type: 'interior', isRestored: true } }));
        }

        state.pageVisible = false;
        state.previousPage = null;
        stopLocationWatcher();
    }

    function handleNavigation() {
        if (isOurRoute() && currentUserId()) {
            showPage();
        } else if (state.pageVisible) {
            hidePage();
        }
    }

    /**
     * Jellyfin's router navigates with pushState, which fires no event, so while our
     * page is showing the location is checked every 250 ms. Stops when the page hides.
     */
    function startLocationWatcher() {
        if (state.locationTimer) {
            return;
        }
        state.locationSignature = window.location.pathname + window.location.hash;
        state.locationTimer = setInterval(function () {
            const signature = window.location.pathname + window.location.hash;
            if (signature !== state.locationSignature) {
                state.locationSignature = signature;
                handleNavigation();
            }
        }, LOCATION_POLL_MS);
    }

    function stopLocationWatcher() {
        if (state.locationTimer) {
            clearInterval(state.locationTimer);
            state.locationTimer = null;
        }
    }

    // ------------------------------------------------------------- lifecycle

    const problems = [];
    function reportProblem(code, error) {
        if (problems.indexOf(code) === -1) {
            problems.push(code);
            console.warn('[Project A] ' + code + ':', error && error.message ? error.message : error);
        }
    }

    /** One pass, at most once per animation frame however many DOM changes arrive. */
    function tick() {
        state.frame = 0;
        try {
            syncButton();
            if (!currentUserId() && state.pageVisible) {
                hidePage();
                const page = document.getElementById(PAGE_ID);
                if (page) {
                    page.remove();
                }
            } else if (isOurRoute() && !state.pageVisible) {
                showPage();
            }
        } catch (e) {
            reportProblem('tick', e);
        }
    }

    function schedule() {
        if (!state.frame) {
            state.frame = requestAnimationFrame(tick);
        }
    }

    // The one set of watchers, registered once.
    new MutationObserver(schedule).observe(document.body, { childList: true, subtree: true });
    window.addEventListener('hashchange', handleNavigation);
    window.addEventListener('popstate', handleNavigation);
    schedule();

    window.ProjectA = Object.freeze({
        version: (document.currentScript && document.currentScript.getAttribute('version')) || 'dev',
        problems: function () { return problems.slice(); },
    });
})();
