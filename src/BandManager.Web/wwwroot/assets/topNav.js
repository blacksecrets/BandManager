// Builds the shared app shell (sidebar nav + slim topbar) on every
// authenticated page - one script tag, no per-page markup changes needed,
// same self-injecting pattern as branding.js/testModeBanner.js. Replaces
// the old horizontal dropdown topbar (#main-nav) with a persistent left
// sidebar, and moves the page's existing .topbar (with its own h1,
// brand-logo-slot, #role-badge, and /logout form) into the new shell
// rather than requiring any HTML changes - #role-badge and the logout
// form are relocated (not recreated) into the sidebar footer so their
// existing behavior keeps working untouched.
//
// Also owns band switching (folded in from the old standalone
// bandSwitcher.js) - that script raced this one to build sidebar DOM that
// didn't exist yet depending on which of two independent fetches resolved
// first, so it's simpler and safer as one script with one fetch sequence.
(function () {
    const NAV_SECTIONS = [
        {
            label: 'Band flow', bandScoped: true, items: [
                { href: '/dashboard', label: 'Web Presence' },
                { href: '/repertoire', label: 'Repertoire' },
                { href: '/gig-sets', label: 'Gig Management' },
                { href: '/calendar', label: 'Calendar' },
                { href: '/venue-campaigns', label: 'Venue Campaigns' },
            ]
        },
        {
            label: 'Band admin', bandScoped: true, adminOnly: true, items: [
                { href: '/band-admin', label: 'General' },
                { href: '/settings', label: 'Configure Web Presence' },
                { href: '/cadence', label: 'Cadence' },
                { href: '/repertoire', label: 'Repertoire' },
                { href: '/catalog', label: 'Images and Flyers' },
            ]
        },
        {
            label: 'Settings', items: [
                { href: '/profile', label: 'Profile' },
                { href: '/superadmin', label: 'SuperAdmin', superAdminOnly: true },
            ]
        },
    ];

    function escapeHtml(str) {
        const div = document.createElement('div');
        div.textContent = str;
        return div.innerHTML;
    }

    async function init() {
        const topbar = document.querySelector('.topbar');
        const badgeEl = document.getElementById('role-badge');
        if (!topbar) return;

        const res = await fetch('/api/profile/me');
        if (!res.ok) return; // not logged in - login.html/setup.html don't have this script anyway
        const me = await res.json();

        if (badgeEl) {
            badgeEl.textContent = me.isSuperAdmin ? 'SuperAdmin' : me.activeBandRole === 'BandAdmin' ? 'Band Admin' : '';
            badgeEl.hidden = !badgeEl.textContent;
        }

        // Build the empty shell and attach it to the document FIRST, then
        // move the page's existing content (the .topbar plus whatever
        // follows it) into #app-main - in that order, every moved node
        // stays continuously attached to the live document (appendChild
        // onto an already-attached parent is atomic, no detached gap).
        // Building #app-main detached and attaching it only at the end
        // (the first version of this) left a window where any other
        // script's own document.getElementById calls could silently miss
        // elements mid-move - hit that for real in testing (profile.js's
        // loadGear racing this function's own awaits against its fetch).
        const shell = document.createElement('div');
        shell.id = 'app-shell';
        const sidebar = document.createElement('aside');
        sidebar.id = 'app-sidebar';
        const appMain = document.createElement('div');
        appMain.id = 'app-main';
        shell.appendChild(sidebar);
        shell.appendChild(appMain);
        document.body.appendChild(shell);
        while (document.body.firstChild !== shell) appMain.appendChild(document.body.firstChild);

        const wordmark = document.createElement('div');
        wordmark.className = 'sidebar-wordmark';
        wordmark.innerHTML = 'BandManager<span class="plus">+</span>';
        sidebar.appendChild(wordmark);

        sidebar.appendChild(await buildBandSwitcher(me));
        sidebar.appendChild(buildNav(me));
        sidebar.appendChild(buildFooter(me, topbar));

        const bell = document.createElement('button');
        bell.type = 'button';
        bell.className = 'notif-bell';
        bell.title = 'Notifications';
        bell.setAttribute('aria-label', 'Notifications');
        bell.innerHTML = '&#128276;<span class="notif-badge" id="notif-badge" hidden></span>';
        bell.addEventListener('click', () => { location.href = '/notifications'; });
        topbar.appendChild(bell);

        pollNotifications(me.isSuperAdmin);
        setInterval(() => pollNotifications(me.isSuperAdmin), 60000);
        window.addEventListener('notif-changed', () => pollNotifications(me.isSuperAdmin));

        document.addEventListener('click', () => {
            const menu = sidebar.querySelector('.sidebar-band-menu');
            if (menu) menu.hidden = true;
        });
    }

    function buildFooter(me, topbar) {
        const footer = document.createElement('div');
        footer.className = 'sidebar-footer';

        const row = document.createElement('div');
        row.className = 'sidebar-footer-row';

        const avatar = document.createElement('div');
        avatar.className = 'sidebar-user-avatar';
        const initials = ((me.firstName || me.username || '?')[0] + (me.lastName ? me.lastName[0] : '')).toUpperCase();
        avatar.textContent = initials;
        row.appendChild(avatar);

        const meta = document.createElement('div');
        meta.className = 'sidebar-user-meta';
        const name = document.createElement('div');
        name.className = 'sidebar-user-name';
        name.textContent = me.firstName ? `${me.firstName} ${me.lastName || ''}`.trim() : me.username;
        meta.appendChild(name);

        // #role-badge already exists in the page's own .topbar markup -
        // relocated here (not recreated) so its fill logic above keeps
        // working untouched. Queried via topbar.querySelector (not
        // document.getElementById) because by this point topbar has
        // already been moved into the still-detached #app-main fragment -
        // document.getElementById only finds elements attached to the
        // live document, so it would silently return null here.
        const badgeEl = topbar.querySelector('#role-badge');
        if (badgeEl) meta.appendChild(badgeEl);
        row.appendChild(meta);
        footer.appendChild(row);

        const logoutForm = topbar.querySelector('form[action="/logout"]');
        if (logoutForm) footer.appendChild(logoutForm);

        return footer;
    }

    function buildNav(me) {
        const nav = document.createElement('nav');
        nav.className = 'sidebar-nav';
        const hasActiveBand = !!me.activeBandRole;
        const currentLocation = (location.pathname === '' ? '/' : location.pathname);

        for (const section of NAV_SECTIONS) {
            if (section.adminOnly && !me.isAdmin) continue;
            const visibleItems = section.items.filter((item) => !(item.superAdminOnly && !me.isSuperAdmin));
            if (visibleItems.length === 0) continue;

            const label = document.createElement('div');
            label.className = 'sidebar-section-label';
            label.textContent = section.label;
            nav.appendChild(label);

            for (const item of visibleItems) {
                const disabled = section.bandScoped && !hasActiveBand;
                const el = document.createElement(disabled ? 'span' : 'a');
                if (!disabled) el.href = item.href;
                el.textContent = item.label;
                el.className = 'sidebar-link' + (disabled ? ' nav-link-inactive' : '');
                if (!disabled && item.href === currentLocation) el.classList.add('active');
                nav.appendChild(el);
            }
        }
        return nav;
    }

    async function buildBandSwitcher(me) {
        const wrap = document.createElement('div');
        wrap.className = 'sidebar-band-switcher';

        const btn = document.createElement('button');
        btn.type = 'button';
        btn.className = 'sidebar-band-switcher-btn';
        wrap.appendChild(btn);

        const menu = document.createElement('div');
        menu.className = 'sidebar-band-menu';
        menu.hidden = true;
        wrap.appendChild(menu);

        const bandsRes = await fetch('/api/bands/mine');
        const bands = bandsRes.ok ? await bandsRes.json() : [];
        const active = bands.find((b) => b.bandId === me.activeBandId);
        const activeLabel = active ? active.bandName : (me.isSuperAdmin ? 'No band selected' : 'Pick a band');
        const initials = activeLabel.split(/\s+/).filter(Boolean).slice(0, 2).map((w) => w[0]).join('').toUpperCase() || '?';
        // Role line must never go blank just because no band is active -
        // that's exactly the state where knowing "you're SuperAdmin" (not
        // a plain member with nothing picked yet) matters most. Falls back
        // to isAdmin's own "Band Admin"/nothing the same way the sidebar
        // footer's #role-badge does, so the two never disagree.
        const roleLabel = active ? active.role : (me.isSuperAdmin ? 'SuperAdmin' : me.isAdmin ? 'Band Admin' : '');

        btn.innerHTML = `
            <span class="sidebar-band-avatar">${escapeHtml(initials)}</span>
            <span class="sidebar-band-meta">
                <span class="sidebar-band-name">${escapeHtml(activeLabel)}</span>
                <span class="sidebar-band-role">${escapeHtml(roleLabel)}</span>
            </span>
            <span class="chevron">&#9662;</span>
        `;

        if (bands.length === 0) {
            const empty = document.createElement('p');
            empty.className = 'sidebar-band-empty';
            empty.textContent = me.isSuperAdmin ? 'No bands yet.' : 'You are not a member of any band yet.';
            menu.appendChild(empty);
        }
        for (const band of bands) {
            const item = document.createElement('button');
            item.type = 'button';
            item.className = 'sidebar-band-item' + (band.bandId === me.activeBandId ? ' active' : '');
            item.innerHTML = `${escapeHtml(band.bandName)}<span class="role-tag">${escapeHtml(band.role)}</span>`;
            item.addEventListener('click', async () => {
                await fetch('/api/bands/active', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ bandId: band.bandId })
                });
                location.reload();
            });
            menu.appendChild(item);
        }

        btn.addEventListener('click', (e) => {
            e.stopPropagation();
            menu.hidden = !menu.hidden;
        });

        return wrap;
    }

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
