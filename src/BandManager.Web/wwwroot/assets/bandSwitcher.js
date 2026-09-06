// New in BandManager (the old single-tenant app never needed this) - a
// self-contained control injected into every page's .topbar, showing the
// active Band and letting anyone with more than one membership (or
// SuperAdmin, who has access to all of them) switch. Creating a new band
// lives under Settings > SuperAdmin instead (profile.html) - this is pure
// switching only. Self-injecting like password-toggle.js/infoIcon.js - one
// <script> tag, no markup changes needed on any page.
(function () {
    const style = document.createElement('style');
    style.textContent = `
        .band-switcher { position: relative; margin-left: 10px; font-size: 0.85rem; }
        .band-switcher-btn {
            background: #262626;
            color: #eee;
            border: 1px solid #444;
            padding: 6px 12px;
            border-radius: 6px;
            cursor: pointer;
            display: inline-flex;
            align-items: center;
            gap: 6px;
        }
        .band-switcher-btn:hover { border-color: #666; }
        .band-switcher-btn .chevron { font-size: 0.7em; opacity: 0.7; }
        .band-switcher-menu {
            position: absolute;
            top: calc(100% + 4px);
            left: 0;
            min-width: 200px;
            background: #1a1a1a;
            border: 1px solid #444;
            border-radius: 8px;
            box-shadow: 0 8px 24px rgba(0,0,0,0.5);
            z-index: 100;
            padding: 4px;
        }
        .band-switcher-menu[hidden] { display: none; }
        .band-switcher-item {
            display: block;
            width: 100%;
            text-align: left;
            background: none;
            border: none;
            color: #ddd;
            padding: 8px 10px;
            border-radius: 5px;
            cursor: pointer;
            font-size: 0.85rem;
        }
        .band-switcher-item:hover { background: #2c2c2c; }
        .band-switcher-item.active { color: #fff; font-weight: bold; }
        .band-switcher-item .role-tag { color: #999; font-weight: normal; font-size: 0.75em; margin-left: 6px; }
        .band-switcher-empty { color: #888; padding: 8px 10px; font-size: 0.8rem; }
    `;
    document.head.appendChild(style);

    async function init() {
        const topbar = document.querySelector('.topbar');
        if (!topbar) return;

        const meRes = await fetch('/api/auth/me');
        if (!meRes.ok) return; // not logged in - nothing to show
        const me = await meRes.json();

        const wrap = document.createElement('div');
        wrap.className = 'band-switcher';

        const btn = document.createElement('button');
        btn.type = 'button';
        btn.className = 'band-switcher-btn';
        wrap.appendChild(btn);

        const menu = document.createElement('div');
        menu.className = 'band-switcher-menu';
        menu.hidden = true;
        wrap.appendChild(menu);

        const logoutForm = topbar.querySelector('form[action="/logout"]');
        if (logoutForm) topbar.insertBefore(wrap, logoutForm);
        else topbar.appendChild(wrap);

        async function refresh() {
            const bandsRes = await fetch('/api/bands/mine');
            const bands = bandsRes.ok ? await bandsRes.json() : [];
            const active = bands.find((b) => b.bandId === me.activeBandId);

            btn.innerHTML = `${active ? escapeHtml(active.bandName) : (me.isSuperAdmin ? 'No band selected' : 'Pick a band')} <span class="chevron">▾</span>`;

            menu.innerHTML = '';
            if (bands.length === 0) {
                const empty = document.createElement('p');
                empty.className = 'band-switcher-empty';
                empty.textContent = me.isSuperAdmin ? 'No bands yet.' : 'You are not a member of any band yet.';
                menu.appendChild(empty);
            }
            for (const band of bands) {
                const item = document.createElement('button');
                item.type = 'button';
                item.className = 'band-switcher-item' + (band.bandId === me.activeBandId ? ' active' : '');
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
        }

        btn.addEventListener('click', async (e) => {
            e.stopPropagation();
            const opening = menu.hidden;
            menu.hidden = !opening;
            if (opening) await refresh();
        });
        document.addEventListener('click', () => { menu.hidden = true; });

        await refresh();
    }

    function escapeHtml(str) {
        const div = document.createElement('div');
        div.textContent = str;
        return div.innerHTML;
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
