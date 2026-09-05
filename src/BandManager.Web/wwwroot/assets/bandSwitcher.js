// New in BandManager (the old single-tenant app never needed this) - a
// self-contained control injected into every page's .topbar, showing the
// active Band and letting anyone with more than one membership (or
// SuperAdmin, who has access to all of them) switch. SuperAdmin also gets
// a "+ New Band" option here, since there's nowhere else to create one yet.
// Self-injecting like password-toggle.js/infoIcon.js - one <script> tag,
// no markup changes needed on any page.
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
        .band-switcher-divider { border: none; border-top: 1px solid #333; margin: 4px 0; }
        .band-switcher-new { color: #8fb4d9; }
        .band-switcher-empty { color: #888; padding: 8px 10px; font-size: 0.8rem; }
        .band-switcher-new-form { padding: 6px 8px 8px; display: flex; flex-direction: column; gap: 6px; }
        .band-switcher-new-form input {
            background: #262626;
            border: 1px solid #444;
            color: #eee;
            padding: 6px 8px;
            border-radius: 5px;
            font-size: 0.85rem;
        }
        .band-switcher-new-form button {
            background: #c00;
            color: white;
            border: none;
            padding: 6px 8px;
            border-radius: 5px;
            font-weight: bold;
            cursor: pointer;
            font-size: 0.85rem;
        }
        .band-switcher-new-error { color: #ff8a8a; font-size: 0.78rem; margin: 0; }
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

            if (me.isSuperAdmin) {
                if (bands.length > 0) {
                    const hr = document.createElement('hr');
                    hr.className = 'band-switcher-divider';
                    menu.appendChild(hr);
                }
                const newBtn = document.createElement('button');
                newBtn.type = 'button';
                newBtn.className = 'band-switcher-item band-switcher-new';
                newBtn.textContent = '+ New band';

                const newForm = document.createElement('form');
                newForm.className = 'band-switcher-new-form';
                newForm.hidden = true;
                newForm.innerHTML = `
                    <input type="text" name="name" placeholder="Band name" required autocomplete="off">
                    <button type="submit">Create</button>
                    <p class="band-switcher-new-error" hidden></p>
                `;
                const errorEl = newForm.querySelector('.band-switcher-new-error');

                newBtn.addEventListener('click', (e) => {
                    e.stopPropagation();
                    newForm.hidden = !newForm.hidden;
                    if (!newForm.hidden) newForm.name.focus();
                });
                newForm.addEventListener('click', (e) => e.stopPropagation());
                newForm.addEventListener('submit', async (e) => {
                    e.preventDefault();
                    const name = newForm.name.value.trim();
                    if (!name) return;
                    const slug = name.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/(^-|-$)/g, '');
                    const res = await fetch('/api/superadmin/bands', {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ name, slug })
                    });
                    const body = await res.json();
                    if (!res.ok) {
                        errorEl.textContent = body.error || 'Could not create band.';
                        errorEl.hidden = false;
                        return;
                    }
                    await fetch('/api/bands/active', {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ bandId: body.id })
                    });
                    location.reload();
                });

                menu.appendChild(newBtn);
                menu.appendChild(newForm);
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
