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
            // Cadence isn't here - it's reachable from the Band Admin page
            // instead, alongside Configure Web Presence.
            type: 'dropdown', label: 'Band Flow', bandScoped: true, items: [
                { href: '/dashboard', label: 'Web Presence', disabledIfNoBand: true },
                { href: '/catalog', label: 'Catalog', disabledIfNoBand: true },
                { href: '/repertoire', label: 'Repertoire', disabledIfNoBand: true },
                { href: '/gig-sets', label: 'Gig Sets', disabledIfNoBand: true },
            ]
        },
        {
            // Not bandScoped - Profile and SuperAdmin work with no Band
            // selected, so the group itself always opens. Only Band Admin
            // (an active-Band-scoped item) is individually disabled below.
            type: 'dropdown', label: 'Settings', items: [
                { href: '/profile', label: 'Profile' },
                {
                    href: '/band-admin', label: 'Band Admin', adminOnly: true, disabledIfNoBand: true, children: [
                        { href: '/settings', label: 'Configure Web Presence' },
                        { href: '/cadence', label: 'Cadence' },
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
            top: 0;
            left: calc(100% + 4px);
        }
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
                    // A submenu (e.g. Band Admin > Configure Web Presence/
                    // Cadence) - the label itself still navigates straight
                    // to the parent page; the chevron opens a flyout with
                    // the children, closed by the same outside-click and
                    // sibling-menu-close handling as every other dropdown.
                    const wrap = document.createElement('div');
                    wrap.className = 'nav-dropdown-item nav-subitem-wrap';

                    const link = document.createElement('a');
                    link.href = item.href;
                    link.textContent = item.label;
                    link.className = 'nav-subitem-link';
                    if (item.href === currentLocation) link.classList.add('active');
                    wrap.appendChild(link);

                    const childIsCurrent = item.children.some((c) => c.href === currentLocation);
                    if (childIsCurrent || item.href === currentLocation) wrap.classList.add('active');

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

                    subBtn.addEventListener('click', (e) => {
                        e.stopPropagation();
                        const opening = subMenu.hidden;
                        for (const otherSub of menu.querySelectorAll('.nav-submenu')) otherSub.hidden = true;
                        subMenu.hidden = !opening;
                    });

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

        document.addEventListener('click', () => {
            for (const menu of navEl.querySelectorAll('.nav-dropdown-menu')) menu.hidden = true;
        });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
