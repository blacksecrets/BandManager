// Shared top navigation, one script tag on every authenticated page -
// self-injecting like bandSwitcher.js/branding.js, so no per-page markup
// duplication (each page used to hand-author its own topbar links plus a
// separate settings-tabs strip, which drifted and never surfaced
// SuperAdmin's own tools at all). Fills #role-badge and #main-nav, both
// expected to already exist (empty) inside .topbar - bandSwitcher.js finds
// .topbar and the logout form itself and keeps working unmodified.
//
// Three top-level items: a plain link ("Web Presence", the dashboard) and
// two dropdown groups (Band Flow, Settings) - matches the same
// button+menu dropdown pattern bandSwitcher.js already established,
// rather than inventing a second one.
(function () {
    const NAV = [
        {
            // bandScoped here disables the whole group (button won't even
            // open) rather than just greying it - every item under it needs
            // an active Band, so there's nothing useful to show without one.
            // Repertoire is deliberately listed here AND under Band Admin
            // below - every Band member browses/edits the shared catalog
            // (see SongsController), not just admins, so it needs a plain
            // one-click nav entry too; Band Admin's copy is for admins who
            // think of it alongside Cadence/Configure Web Presence. Catalog
            // is NOT here - it moved entirely under Band Admin below.
            type: 'dropdown', label: 'Band Flow', bandScoped: true, items: [
                { href: '/dashboard', label: 'Web Presence', disabledIfNoBand: true },
                { href: '/repertoire', label: 'Repertoire', disabledIfNoBand: true },
                { href: '/gig-sets', label: 'Gig Management', disabledIfNoBand: true },
                { href: '/calendar', label: 'Calendar', disabledIfNoBand: true },
                { href: '/venue-campaigns', label: 'Venue Campaigns', disabledIfNoBand: true },
            ]
        },
        {
            // Not bandScoped - Profile and SuperAdmin work with no Band
            // selected, so the group itself always opens. Only Band Admin
            // (an active-Band-scoped item) is individually disabled below.
            type: 'dropdown', label: 'Settings', items: [
                { href: '/profile', label: 'Profile' },
                {
                    // No href of its own - Band Admin is a pure category
                    // now, clicking the label just opens the submenu (same
                    // as the chevron) rather than navigating anywhere.
                    // "General" is what /band-admin itself used to mean.
                    label: 'Band Admin', adminOnly: true, disabledIfNoBand: true, children: [
                        { href: '/band-admin', label: 'General' },
                        { href: '/settings', label: 'Configure Web Presence' },
                        { href: '/cadence', label: 'Cadence' },
                        { href: '/repertoire', label: 'Repertoire' },
                        { href: '/catalog', label: 'Images and Flyers' },
                    ]
                },
                { href: '/superadmin', label: 'SuperAdmin', superAdminOnly: true },
            ]
        },
    ];

    const style = document.createElement('style');
    style.textContent = `
        .nav-dropdown { position: relative; }
        .nav-dropdown-btn {
            background: none;
            border: none;
            color: #aaa;
            font-size: 0.85rem;
            cursor: pointer;
            padding: 0;
            display: inline-flex;
            align-items: center;
            gap: 4px;
        }
        .nav-dropdown-btn:hover { color: #eee; }
        .nav-dropdown-btn.active { color: #fff; font-weight: bold; }
        .nav-dropdown-btn.nav-link-inactive { color: #666; }
        .nav-dropdown-btn.nav-link-inactive:hover { color: #888; }
        .nav-dropdown-btn .chevron { font-size: 0.7em; opacity: 0.7; }
        .nav-dropdown-menu {
            position: absolute;
            top: calc(100% + 8px);
            left: 0;
            min-width: 180px;
            background: #1a1a1a;
            border: 1px solid #444;
            border-radius: 8px;
            box-shadow: 0 8px 24px rgba(0,0,0,0.5);
            z-index: 100;
            padding: 4px;
        }
        .nav-dropdown-menu[hidden] { display: none; }
        .nav-dropdown-item {
            display: block;
            padding: 8px 10px;
            border-radius: 5px;
            color: #ddd;
            text-decoration: none;
            font-size: 0.85rem;
        }
        .nav-dropdown-item:hover { background: #2c2c2c; }
        .nav-dropdown-item.active { color: #fff; font-weight: bold; }
        .nav-dropdown-item.nav-link-inactive { color: #666; }
        .nav-subitem-wrap {
            position: relative;
            display: flex;
            align-items: center;
            justify-content: space-between;
            gap: 6px;
            padding: 0;
        }
        .nav-subitem-wrap.active > .nav-subitem-link { color: #fff; font-weight: bold; }
        .nav-subitem-link {
            flex: 1;
            padding: 8px 4px 8px 10px;
            border-radius: 5px;
            color: #ddd;
            text-decoration: none;
            font-size: 0.85rem;
            background: none;
            border: none;
            font-family: inherit;
            text-align: left;
            cursor: pointer;
        }
        .nav-subitem-link:hover, .nav-subitem-wrap:hover .nav-subitem-link { background: #2c2c2c; }
        .nav-subitem-link.active { color: #fff; font-weight: bold; }
        .nav-subitem-toggle {
            background: none;
            border: none;
            color: #999;
            cursor: pointer;
            padding: 8px 10px 8px 4px;
            font-size: 0.7em;
            border-radius: 5px;
        }
        .nav-subitem-toggle:hover { background: #2c2c2c; color: #eee; }
        .nav-submenu {
            /* Opens to the LEFT of its parent item, not the right - Band
               Admin (this flyout's only current user) sits near the right
               edge of the topbar already, so a rightward flyout renders
               past the window edge and is never actually visible. */
            top: 0;
            left: auto;
            right: calc(100% + 4px);
        }
        .breadcrumb-parent { color: #999; text-decoration: none; }
        .breadcrumb-parent:hover { color: #eee; text-decoration: underline; }
        .breadcrumb-sep { color: #666; }
        .breadcrumb-current { color: inherit; }
        .dashboard-back-link {
            color: #aaa;
            text-decoration: none;
            font-size: 0.85rem;
            padding: 4px 8px;
            border: 1px solid #444;
            border-radius: 6px;
            white-space: nowrap;
        }
        .dashboard-back-link:hover { color: #eee; border-color: #666; }
        .notif-bell {
            position: relative;
            background: none;
            border: none;
            color: #aaa;
            font-size: 1.1rem;
            cursor: pointer;
            padding: 4px 6px;
            line-height: 1;
        }
        .notif-bell:hover { color: #eee; }
        .notif-badge {
            position: absolute;
            top: -2px;
            right: -2px;
            background: #c0392b;
            color: #fff;
            border-radius: 999px;
            font-size: 0.6rem;
            font-weight: bold;
            line-height: 1;
            padding: 2px 5px;
            min-width: 14px;
            text-align: center;
        }
        .notif-badge[hidden] { display: none; }
    `;
    document.head.appendChild(style);

    async function init() {
        const topbar = document.querySelector('.topbar');
        const badgeEl = document.getElementById('role-badge');
        const navEl = document.getElementById('main-nav');
        if (!topbar || !navEl) return;

        const res = await fetch('/api/profile/me');
        if (!res.ok) return; // not logged in - login.html/setup.html don't have this script anyway
        const me = await res.json();

        if (badgeEl) {
            badgeEl.textContent = me.isSuperAdmin ? 'SuperAdmin' : me.activeBandRole === 'BandAdmin' ? 'Band Admin' : '';
            badgeEl.hidden = !badgeEl.textContent;
        }

        // Injected rather than a static per-page placeholder (unlike
        // #role-badge/#main-nav) - keeps every page's topbar markup
        // untouched, same reasoning as this script's own injected <style>.
        // Present on every page, including the Dashboard itself - clicking
        // it there is just a harmless reload, not worth special-casing.
        const dashboardLink = document.createElement('a');
        dashboardLink.href = '/';
        dashboardLink.className = 'dashboard-back-link';
        dashboardLink.textContent = '‹ Dashboard';
        topbar.insertBefore(dashboardLink, navEl);

        const bell = document.createElement('button');
        bell.type = 'button';
        bell.className = 'notif-bell';
        bell.title = 'Notifications';
        bell.innerHTML = '&#128276;<span class="notif-badge" id="notif-badge" hidden></span>';
        bell.addEventListener('click', () => { location.href = '/notifications'; });
        topbar.insertBefore(bell, navEl);

        pollNotifications(me.isSuperAdmin);
        setInterval(() => pollNotifications(me.isSuperAdmin), 60000);
        window.addEventListener('notif-changed', () => pollNotifications(me.isSuperAdmin));

        const hasActiveBand = !!me.activeBandRole;
        // Compares the full path+hash, not just the path - Profile and
        // SuperAdmin both point at /profile (one plain, one with a hash),
        // so matching on path alone would mark both active at once
        // whenever either is current.
        const currentLocation = (location.pathname === '' ? '/' : location.pathname) + location.hash;

        for (const entry of NAV) {
            if (entry.type === 'link') {
                const a = document.createElement('a');
                a.href = entry.href;
                a.textContent = entry.label;
                a.className = 'main-nav-link';
                if (entry.href === currentLocation) a.classList.add('active');
                if (entry.bandScoped && !hasActiveBand) a.classList.add('nav-link-inactive');
                navEl.appendChild(a);
                continue;
            }

            // Dropdown group - skip entirely if every item inside it would
            // be hidden (e.g. "Settings" would still show Profile even for
            // a non-SuperAdmin, so it's never actually empty in practice,
            // but this keeps the check honest rather than assuming).
            const visibleItems = entry.items.filter((item) => !(item.superAdminOnly && !me.isSuperAdmin) && !(item.adminOnly && !me.isAdmin));
            if (visibleItems.length === 0) continue;

            // Whole-group disable (Band Flow): every item needs a Band, so
            // there's no point letting the menu even open without one.
            const groupDisabled = entry.bandScoped && !hasActiveBand;

            const wrap = document.createElement('div');
            wrap.className = 'nav-dropdown';

            const btn = document.createElement('button');
            btn.type = 'button';
            btn.className = 'nav-dropdown-btn';
            btn.innerHTML = `${entry.label} <span class="chevron">&#9662;</span>`;
            const groupIsCurrent = visibleItems.some((item) =>
                item.href === currentLocation || (item.children && item.children.some((c) => c.href === currentLocation)));
            if (groupIsCurrent) btn.classList.add('active');
            if (groupDisabled) btn.classList.add('nav-link-inactive');
            wrap.appendChild(btn);

            const menu = document.createElement('div');
            menu.className = 'nav-dropdown-menu';
            menu.hidden = true;
            for (const item of visibleItems) {
                // Per-item disable (e.g. Band Admin within Settings): the
                // group itself still opens, but a Band-scoped item inside it
                // renders as a plain, non-navigable <span> instead of a link.
                const itemDisabled = groupDisabled || (item.disabledIfNoBand && !hasActiveBand);

                if (item.children && !itemDisabled) {
                    // A submenu (e.g. Band Admin > General/Configure Web
                    // Presence/Cadence). If the item has its own href, the
                    // label navigates there and the chevron opens a flyout
                    // with the children. If it doesn't (a pure category,
                    // like Band Admin), the label behaves exactly like the
                    // chevron - opens the flyout instead of navigating -
                    // since there's no page of its own to go to.
                    const wrap = document.createElement('div');
                    wrap.className = 'nav-dropdown-item nav-subitem-wrap';

                    const link = document.createElement(item.href ? 'a' : 'button');
                    if (item.href) link.href = item.href;
                    else link.type = 'button';
                    link.textContent = item.label;
                    link.className = 'nav-subitem-link';
                    if (item.href && item.href === currentLocation) link.classList.add('active');
                    wrap.appendChild(link);

                    const childIsCurrent = item.children.some((c) => c.href === currentLocation);
                    if (childIsCurrent || (item.href && item.href === currentLocation)) wrap.classList.add('active');

                    const subBtn = document.createElement('button');
                    subBtn.type = 'button';
                    subBtn.className = 'nav-subitem-toggle';
                    subBtn.innerHTML = '&#9656;';
                    wrap.appendChild(subBtn);

                    const subMenu = document.createElement('div');
                    subMenu.className = 'nav-dropdown-menu nav-submenu';
                    subMenu.hidden = true;
                    for (const child of item.children) {
                        const childEl = document.createElement('a');
                        childEl.href = child.href;
                        childEl.textContent = child.label;
                        childEl.className = 'nav-dropdown-item';
                        if (child.href === currentLocation) childEl.classList.add('active');
                        subMenu.appendChild(childEl);
                    }
                    wrap.appendChild(subMenu);

                    const toggleSubMenu = (e) => {
                        e.stopPropagation();
                        const opening = subMenu.hidden;
                        for (const otherSub of menu.querySelectorAll('.nav-submenu')) otherSub.hidden = true;
                        subMenu.hidden = !opening;
                    };
                    subBtn.addEventListener('click', toggleSubMenu);
                    if (!item.href) link.addEventListener('click', toggleSubMenu);

                    menu.appendChild(wrap);
                    continue;
                }

                const el = document.createElement(itemDisabled ? 'span' : 'a');
                if (!itemDisabled) el.href = item.href;
                el.textContent = item.label;
                el.className = 'nav-dropdown-item' + (itemDisabled ? ' nav-link-inactive' : '');
                if (!itemDisabled && item.href === currentLocation) el.classList.add('active');
                menu.appendChild(el);
            }
            wrap.appendChild(menu);

            btn.addEventListener('click', (e) => {
                e.stopPropagation();
                if (groupDisabled) return;
                const opening = menu.hidden;
                for (const otherMenu of navEl.querySelectorAll('.nav-dropdown-menu')) otherMenu.hidden = true;
                menu.hidden = !opening;
            });

            navEl.appendChild(wrap);
        }

        // Breadcrumb the topbar's page-name heading, only for a page
        // reached through a submenu (e.g. Band Admin > General) - a page
        // linked directly from a dropdown (Profile, Repertoire, ...) keeps
        // its plain static heading, nothing to breadcrumb. The parent
        // segment (Band Admin, itself not a real page) links to its first
        // child instead - the closest thing it has to a "page of its own".
        breadcrumbSearch:
        for (const entry of NAV) {
            if (entry.type !== 'dropdown') continue;
            for (const item of entry.items) {
                if (!item.children) continue;
                const child = item.children.find((c) => c.href === currentLocation);
                if (!child) continue;

                const h1 = topbar.querySelector('h1');
                if (!h1) break breadcrumbSearch;
                const parentHref = item.href || item.children[0].href;
                h1.innerHTML = '';
                const parentLink = document.createElement('a');
                parentLink.href = parentHref;
                parentLink.className = 'breadcrumb-parent';
                parentLink.textContent = item.label;
                h1.appendChild(parentLink);
                const sep = document.createElement('span');
                sep.className = 'breadcrumb-sep';
                sep.textContent = ' › ';
                h1.appendChild(sep);
                const current = document.createElement('span');
                current.className = 'breadcrumb-current';
                current.textContent = child.label;
                h1.appendChild(current);
                break breadcrumbSearch;
            }
        }

        document.addEventListener('click', () => {
            for (const menu of navEl.querySelectorAll('.nav-dropdown-menu')) menu.hidden = true;
        });
    }

    // Personal unread count (everyone) + SuperAdmin's pending-review queue
    // count (a live query, not a second inbox - see NotificationsController's
    // doc comment) - summed into one badge. Flat 60s poll, matching
    // dashboard.js's only existing polling precedent; notif-changed fires
    // immediately after an approve/reject/read action so the badge doesn't
    // wait for the next tick.
    async function pollNotifications(isSuperAdmin) {
        const badge = document.getElementById('notif-badge');
        if (!badge) return;
        const personal = await fetch('/api/notifications/unread-count').then((r) => (r.ok ? r.json() : { count: 0 })).catch(() => ({ count: 0 }));
        let queue = { count: 0 };
        if (isSuperAdmin) {
            queue = await fetch('/api/song-edit-requests/pending-count').then((r) => (r.ok ? r.json() : { count: 0 })).catch(() => ({ count: 0 }));
        }
        const total = (personal.count || 0) + (queue.count || 0);
        badge.textContent = total > 99 ? '99+' : String(total);
        badge.hidden = total === 0;
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
