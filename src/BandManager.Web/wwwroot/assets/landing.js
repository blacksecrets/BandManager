// Role-based Dashboard - the login landing page (was a bare "TBD"
// placeholder). BandMember/BandAdmin get a 2x2 grid of widgets, each its
// own small DataGrid summarizing one part of the active Band; SuperAdmin
// gets a stacked Bands + Connectivity Status view instead, since "the
// active Band's gigs/venues/calendar" isn't a meaningful SuperAdmin
// concept. Clicking a widget's header opens a screen-sized modal with an
// extended version of the same grid (more columns) and its own [Go To]
// button - the small widget grid stays a genuine, working DataGrid
// (sortable/searchable/paged), not just a static preview.
function escapeHtml(str) {
    const div = document.createElement('div');
    div.textContent = str ?? '';
    return div.innerHTML;
}

let activeDetailWidget = null;

function openDetailModal(title, buildGrid, gotoHref, gotoLabel) {
    document.getElementById('landing-detail-modal-title').textContent = title;
    const container = document.getElementById('landing-detail-modal-grid');
    container.innerHTML = '';
    buildGrid(container);
    const gotoBtn = document.getElementById('landing-detail-modal-goto-btn');
    gotoBtn.textContent = gotoLabel;
    gotoBtn.onclick = () => { location.href = gotoHref; };
    document.getElementById('landing-detail-modal-backdrop').hidden = false;
}
document.getElementById('landing-detail-modal-close').addEventListener('click', () => {
    document.getElementById('landing-detail-modal-backdrop').hidden = true;
});
document.getElementById('landing-detail-modal-backdrop').addEventListener('click', (e) => {
    if (e.target.id === 'landing-detail-modal-backdrop') document.getElementById('landing-detail-modal-backdrop').hidden = true;
});

document.querySelectorAll('.landing-goto-btn').forEach((btn) => {
    btn.addEventListener('click', (e) => { e.stopPropagation(); location.href = btn.dataset.href; });
});

async function init() {
    const res = await fetch('/api/profile/me');
    const me = await res.json();

    if (me.isSuperAdmin) {
        document.getElementById('landing-superadmin-stack').hidden = false;
        await loadSuperAdminDashboard();
        return;
    }

    if (!me.activeBandRole) {
        document.getElementById('landing-no-band').hidden = false;
        return;
    }

    document.getElementById('landing-member-grid').hidden = false;
    loadGigsWidget();
    loadVenuesWidget();
    loadWebPresenceWidget();
    loadCalendarWidget();
}

// --- Our Gigs ---
async function loadGigsWidget() {
    const res = await fetch('/api/gig-sets/gigs');
    const gigs = res.ok ? await res.json() : [];

    const widgetColumns = [
        { key: 'title', label: 'Title', render: (g) => escapeHtml(g.title) },
        { key: 'date', label: 'Date', sortValue: (g) => g.sortDate, render: (g) => escapeHtml(g.date) },
        { key: 'venue', label: 'Venue', render: (g) => escapeHtml(g.venue || '—') }
    ];
    window.DataGrid.render(document.getElementById('landing-gigs-grid'), {
        columns: widgetColumns, rows: gigs, getRowId: (g) => g.gigRef,
        defaultSortKey: 'date', defaultSortDir: 'desc', emptyMessage: 'No gigs yet.'
    });

    document.querySelector('[data-widget="gigs"] h2').addEventListener('click', () => {
        openDetailModal('Our Gigs', (container) => {
            const detailColumns = [
                ...widgetColumns,
                { key: 'time', label: 'Time', render: (g) => escapeHtml(g.time || '—') },
                { key: 'songCount', label: 'Songs', render: (g) => g.songCount },
                { key: 'duration', label: 'Duration', sortValue: (g) => g.durationSeconds, render: (g) => escapeHtml(g.duration || '—') }
            ];
            window.DataGrid.render(container, {
                columns: detailColumns, rows: gigs, getRowId: (g) => g.gigRef,
                defaultSortKey: 'date', defaultSortDir: 'desc', emptyMessage: 'No gigs yet.'
            });
        }, '/gig-sets', 'Go To Gig Management');
    });
}

// --- Our Venues ---
async function loadVenuesWidget() {
    const res = await fetch('/api/venues');
    const venues = res.ok ? await res.json() : [];

    const widgetColumns = [
        { key: 'name', label: 'Name', render: (v) => escapeHtml(v.name) },
        { key: 'city', label: 'City', render: (v) => escapeHtml(v.city || '—') },
        { key: 'state', label: 'State', render: (v) => escapeHtml(v.state || '—') }
    ];
    window.DataGrid.render(document.getElementById('landing-venues-grid'), {
        columns: widgetColumns, rows: venues, getRowId: (v) => v.id,
        defaultSortKey: 'name', defaultSortDir: 'asc', emptyMessage: 'No venues yet.'
    });

    document.querySelector('[data-widget="venues"] h2').addEventListener('click', () => {
        openDetailModal('Our Venues', (container) => {
            const detailColumns = [
                ...widgetColumns,
                { key: 'addressLine1', label: 'Address', render: (v) => escapeHtml(v.addressLine1 || '—') },
                {
                    key: 'contact', label: 'Contact', sortable: false,
                    render: (v) => {
                        const primary = v.contacts?.find((c) => c.isPrimary) || v.contacts?.[0];
                        return escapeHtml(primary?.name || '—');
                    }
                }
            ];
            window.DataGrid.render(container, {
                columns: detailColumns, rows: venues, getRowId: (v) => v.id,
                defaultSortKey: 'name', defaultSortDir: 'asc', emptyMessage: 'No venues yet.'
            });
        }, '/venue-campaigns', 'Go To Venue Campaigns');
    });
}

// --- Our Web Presence ---
async function loadWebPresenceWidget() {
    const [platformsRes, itemsRes] = await Promise.all([fetch('/api/platforms'), fetch('/api/items')]);
    const platforms = platformsRes.ok ? await platformsRes.json() : [];
    const items = itemsRes.ok ? await itemsRes.json() : [];

    const countByPlatform = {};
    for (const item of items) countByPlatform[item.platform] = (countByPlatform[item.platform] || 0) + 1;

    const rows = platforms.map((p) => ({
        id: p.id,
        displayName: p.display_name,
        tileCount: countByPlatform[p.id] || 0,
        connected: !!p.account_id
    }));

    const widgetColumns = [
        { key: 'displayName', label: 'Platform', render: (p) => escapeHtml(p.displayName) },
        { key: 'tileCount', label: 'Tiles', render: (p) => p.tileCount }
    ];
    window.DataGrid.render(document.getElementById('landing-web-presence-grid'), {
        columns: widgetColumns, rows, getRowId: (p) => p.id,
        defaultSortKey: 'displayName', defaultSortDir: 'asc', emptyMessage: 'No platforms configured.'
    });

    document.querySelector('[data-widget="web-presence"] h2').addEventListener('click', () => {
        openDetailModal('Our Web Presence', (container) => {
            const detailColumns = [
                ...widgetColumns,
                { key: 'connected', label: 'Connected', sortValue: (p) => (p.connected ? 1 : 0), render: (p) => p.connected ? 'Yes' : 'No' }
            ];
            window.DataGrid.render(container, {
                columns: detailColumns, rows, getRowId: (p) => p.id,
                defaultSortKey: 'displayName', defaultSortDir: 'asc', emptyMessage: 'No platforms configured.'
            });
        }, '/dashboard', 'Go To Web Presence');
    });
}

// --- Our Calendar ---
async function loadCalendarWidget() {
    const from = new Date();
    const to = new Date();
    to.setDate(to.getDate() + 90);
    const fmt = (d) => d.toISOString().slice(0, 10);
    const res = await fetch(`/api/calendar?from=${fmt(from)}&to=${fmt(to)}`);
    const entries = res.ok ? await res.json() : [];

    const widgetColumns = [
        { key: 'date', label: 'Date', render: (e) => escapeHtml(e.displayDate || e.date) },
        { key: 'title', label: 'Title', render: (e) => escapeHtml(e.title) },
        { key: 'kind', label: 'Type', render: (e) => e.kind === 'gig' ? 'Gig' : 'Rehearsal' }
    ];
    window.DataGrid.render(document.getElementById('landing-calendar-grid'), {
        columns: widgetColumns, rows: entries, getRowId: (e) => `${e.kind}-${e.id}`,
        defaultSortKey: 'date', defaultSortDir: 'asc', emptyMessage: 'Nothing on the calendar in the next 90 days.'
    });

    document.querySelector('[data-widget="calendar"] h2').addEventListener('click', () => {
        openDetailModal('Our Calendar (next 90 days)', (container) => {
            const detailColumns = [
                ...widgetColumns,
                { key: 'venueOrLocation', label: 'Venue / Location', sortable: false, render: (e) => escapeHtml(e.venue || e.location || '—') }
            ];
            window.DataGrid.render(container, {
                columns: detailColumns, rows: entries, getRowId: (e) => `${e.kind}-${e.id}`,
                defaultSortKey: 'date', defaultSortDir: 'asc', emptyMessage: 'Nothing on the calendar in the next 90 days.'
            });
        }, '/calendar', 'Go To Calendar');
    });
}

// --- SuperAdmin: Bands + Connectivity Status ---
async function loadSuperAdminDashboard() {
    const [bandsRes, usersRes] = await Promise.all([fetch('/api/superadmin/bands'), fetch('/api/superadmin/users')]);
    const bands = bandsRes.ok ? await bandsRes.json() : [];
    const users = usersRes.ok ? await usersRes.json() : [];

    const adminNamesByBandId = {};
    for (const u of users) {
        for (const m of (u.memberships || [])) {
            if (m.role !== 'BandAdmin') continue;
            (adminNamesByBandId[m.bandId] ??= []).push(u.username);
        }
    }

    const bandRows = bands.filter((b) => !b.isArchived).map((b) => ({
        id: b.id, name: b.name,
        bandAdmins: (adminNamesByBandId[b.id] || []).join(', ') || '—',
        createdAt: b.createdAt
    }));
    window.DataGrid.render(document.getElementById('landing-bands-grid'), {
        columns: [
            { key: 'name', label: 'Band', render: (b) => escapeHtml(b.name) },
            { key: 'bandAdmins', label: 'Band Admin(s)', render: (b) => escapeHtml(b.bandAdmins) },
            { key: 'createdAt', label: 'Onboarded', sortValue: (b) => new Date(b.createdAt).getTime(), render: (b) => new Date(b.createdAt).toLocaleDateString() }
        ],
        rows: bandRows, getRowId: (b) => b.id,
        defaultSortKey: 'name', defaultSortDir: 'asc', emptyMessage: 'No bands yet.'
    });

    // Connectivity is per-Band (credentials are stored per-Band) - shows
    // for whichever Band is currently active via the switcher, same as
    // every other Band-scoped SuperAdmin view in this app.
    const note = document.getElementById('landing-connectivity-note');
    const me = await (await fetch('/api/profile/me')).json();
    if (!me.activeBandRole) {
        note.textContent = 'Select a band from the switcher above to see its connectivity status.';
        return;
    }
    note.textContent = `Showing ${me.activeBandName}'s connected platforms.`;
    const [platformsRes, credsRes] = await Promise.all([fetch('/api/platforms'), fetch('/api/settings/credentials')]);
    const platforms = platformsRes.ok ? await platformsRes.json() : [];
    const creds = credsRes.ok ? await credsRes.json() : {};
    const connectivityRows = platforms.map((p) => ({ id: p.id, displayName: p.display_name, connected: !!creds[p.id]?.configured }));
    window.DataGrid.render(document.getElementById('landing-connectivity-grid'), {
        columns: [
            { key: 'displayName', label: 'Platform', render: (p) => escapeHtml(p.displayName) },
            { key: 'connected', label: 'Status', sortValue: (p) => (p.connected ? 1 : 0), render: (p) => p.connected ? '<span class="status-connected">Connected</span>' : '<span class="status-not-connected">Not connected</span>' }
        ],
        rows: connectivityRows, getRowId: (p) => p.id,
        defaultSortKey: 'displayName', defaultSortDir: 'asc', searchable: false, emptyMessage: 'No platforms configured.'
    });
}

init();
