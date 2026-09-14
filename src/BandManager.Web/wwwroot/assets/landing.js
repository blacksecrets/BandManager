// Role-based, customizable Dashboard - the login landing page (was a bare
// "TBD" placeholder). Every role picks from the same widget grid (see
// WIDGETS below) and reorders it via Customize Widgets; a widget scoped to
// the active Band (bandSpecific) shows "Please select a Band to view"
// instead of its data when none is selected - this is what lets a
// SuperAdmin, who may never pick a Band at all, use the same dashboard as
// everyone else instead of a separate hardcoded page. Clicking most
// widgets' header opens a screen-sized modal with an extended version of
// the same grid (more columns) and its own [Go To] button - the small
// widget grid stays a genuine, working DataGrid (sortable/searchable/
// paged), not just a static preview.
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

// Every widget landing.js knows how to render, keyed the same way as
// ProfileController.KnownDashboardWidgets so the two never drift apart.
// label is only used by the customize picker. bandSpecific widgets need
// an active Band to fetch anything meaningful - contentElId names the
// container init() fills with a "Please select a Band to view" note
// instead of calling load() when none is selected, rather than letting
// the widget try and fail. Widgets with neither bandSpecific nor
// superAdminOnly (song-catalog) work the same for everyone regardless of
// which Band, if any, is active - the shared catalog isn't Band data.
const WIDGETS = [
    { key: 'gigs', label: 'Our Gigs', load: loadGigsWidget, bandSpecific: true, contentElId: 'landing-gigs-grid' },
    { key: 'venues', label: 'Our Venues', load: loadVenuesWidget, bandSpecific: true, contentElId: 'landing-venues-grid' },
    { key: 'web-presence', label: 'Our Web Presence', load: loadWebPresenceWidget, bandSpecific: true, contentElId: 'landing-web-presence-grid' },
    { key: 'calendar', label: 'Our Calendar', load: loadCalendarWidget, bandSpecific: true, contentElId: 'landing-calendar-grid' },
    { key: 'next-two-weeks', label: 'My Next Two Weeks', load: loadNextTwoWeeksWidget, bandSpecific: true, contentElId: 'landing-next-two-weeks-content' },
    { key: 'venue-campaign-status', label: 'Venue Campaign Status', load: loadVenueCampaignStatusWidget, bandSpecific: true, contentElId: 'landing-venue-campaign-status' },
    // adminOnly: hidden from the customize picker entirely for a plain
    // member, not just visibility-gated once added - the widget shows
    // this band's overall gross earnings, which a member has no access
    // to anywhere else (My Accounting only ever shows their own rows).
    // The underlying endpoint is BandAdmin-only regardless, so this is
    // belt-and-suspenders, not the only thing standing between a member
    // and the data.
    { key: 'accounting-graph', label: 'Accounting Graph', load: loadAccountingGraphWidget, adminOnly: true, bandSpecific: true, contentElId: 'landing-accounting-graph' },
    // Not bandSpecific - the shared Song Catalog (SongsController's own
    // doc comment) is the same catalog no matter which Band, if any, is
    // active, so this works with nothing selected.
    { key: 'song-catalog', label: 'Song Catalog', load: loadSongCatalogWidget },
    // superAdminOnly, same shape as adminOnly above but gated on true
    // SuperAdmin rather than "admin of the active Band" - these replace
    // what used to be a separate, non-customizable SuperAdmin-only page
    // state, so a SuperAdmin now picks their own layout exactly like
    // everyone else, mixing these with Band-specific widgets once they've
    // selected a Band via the switcher.
    { key: 'superadmin-platform', label: 'Platform', load: loadPlatformStatsWidget, superAdminOnly: true },
    { key: 'superadmin-bands', label: 'Bands', load: loadBandsWidget, superAdminOnly: true }
];

let dashboardWidgets = WIDGETS.map((w) => w.key);
let isAdminGlobal = false;
let isSuperAdminGlobal = false;

// Applies dashboardWidgets (order + which are shown) to the already-in-DOM
// widget sections via CSS order + hidden, rather than rebuilding markup -
// there are only ever these known widgets, so a fixed set of sections
// toggled/reordered is simpler than templating them from scratch.
function applyWidgetLayout() {
    WIDGETS.forEach((w) => {
        const section = document.querySelector(`[data-widget="${w.key}"]`);
        if (!section) return;
        // An adminOnly/superAdminOnly widget stays visually hidden for
        // someone who doesn't qualify even if it's still sitting in their
        // saved layout (e.g. they were demoted after picking it) - matches
        // the load loop in init() skipping it too, so there's never an
        // empty, unloaded section left showing.
        const eligible = (!w.adminOnly || isAdminGlobal) && (!w.superAdminOnly || isSuperAdminGlobal);
        const position = eligible ? dashboardWidgets.indexOf(w.key) : -1;
        section.hidden = position === -1;
        section.style.order = position === -1 ? WIDGETS.length : position;
    });
}

async function init() {
    const res = await fetch('/api/profile/me');
    const me = await res.json();

    isAdminGlobal = !!me.isAdmin;
    isSuperAdminGlobal = !!me.isSuperAdmin;
    const hasBand = !!me.activeBandRole;

    // A hint, not a gate - band-specific widgets show their own inline
    // "Please select a Band to view" below instead of the whole dashboard
    // disappearing behind this note. Most relevant to a SuperAdmin (who
    // may never pick a Band at all) but applies the same way to anyone
    // whose active Band went stale (e.g. it was archived - see
    // ProfileController.Me()'s comment on that).
    document.getElementById('landing-no-band').hidden = hasBand;

    dashboardWidgets = (me.dashboardWidgets || []).filter((k) => WIDGETS.some((w) => w.key === k));
    applyWidgetLayout();

    document.getElementById('landing-customize-btn').hidden = false;
    document.getElementById('landing-member-grid').hidden = false;
    for (const w of WIDGETS) {
        if (!dashboardWidgets.includes(w.key)) continue;
        if (w.adminOnly && !isAdminGlobal) continue;
        if (w.superAdminOnly && !isSuperAdminGlobal) continue;
        if (w.bandSpecific && !hasBand) {
            const el = document.getElementById(w.contentElId);
            if (el) el.innerHTML = '<p class="save-note">Please select a Band to view.</p>';
            continue;
        }
        w.load();
    }
    initCustomizeModal();
}

// --- Our Gigs ---
async function loadGigsWidget() {
    const res = await fetch('/api/gig-sets/gigs');
    const gigs = res.ok ? await res.json() : [];
    // The compact tile is "what's coming up," not a full history - already-
    // past gigs are still one click away via "Go To Gig Management" (which
    // has its own Upcoming/Past split), so this preview only ever shows
    // what's still ahead, soonest first, instead of defaulting to furthest-
    // future-first and burying the next gig below everything else.
    const upcomingGigs = gigs.filter((g) => !g.isPast);

    const widgetColumns = [
        { key: 'title', label: 'Title', render: (g) => escapeHtml(g.title) },
        { key: 'date', label: 'Date', sortValue: (g) => g.sortDate, render: (g) => escapeHtml(g.date) },
        { key: 'venue', label: 'Venue', render: (g) => escapeHtml(g.venue || '—') }
    ];
    window.DataGrid.render(document.getElementById('landing-gigs-grid'), {
        columns: widgetColumns, rows: upcomingGigs, getRowId: (g) => g.gigRef,
        defaultSortKey: 'date', defaultSortDir: 'asc', emptyMessage: 'No upcoming gigs.'
    });

    document.querySelector('[data-widget="gigs"] h2').addEventListener('click', () => {
        openDetailModal('Our Gigs', (container) => {
            const detailColumns = [
                ...widgetColumns,
                { key: 'time', label: 'Time', render: (g) => escapeHtml(g.time || '—') },
                { key: 'songCount', label: 'Songs', render: (g) => g.songCount },
                { key: 'duration', label: 'Duration', sortValue: (g) => g.durationSeconds, render: (g) => escapeHtml(g.duration || '—') }
            ];
            // The expanded "everything" view keeps past gigs too (someone
            // opening this probably does want the full list), just sorted
            // soonest-first same as the tile, instead of furthest-first.
            window.DataGrid.render(container, {
                columns: detailColumns, rows: gigs, getRowId: (g) => g.gigRef,
                defaultSortKey: 'date', defaultSortDir: 'asc', emptyMessage: 'No gigs yet.'
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

// --- My Next Two Weeks ---
// Keyed by GigPrepListType's numeric value (Pre=0, Packing=1, Post=2) -
// listType comes over the wire as that number, not a string name, same
// as gig-sets.js's own gigPrepItems handling already assumes.
const PREP_LIST_LABELS = { 0: 'Pre-gig', 1: 'Packing', 2: 'Post-gig' };

async function loadNextTwoWeeksWidget() {
    const res = await fetch('/api/dashboard/next-two-weeks');
    const data = res.ok ? await res.json() : { gigs: [], rehearsals: [], openPrepTasks: [] };
    const container = document.getElementById('landing-next-two-weeks-content');

    function section(title, items, renderItem) {
        if (items.length === 0) return `<div class="next-two-weeks-section"><h3>${escapeHtml(title)}</h3><p class="save-note">Nothing here.</p></div>`;
        return `<div class="next-two-weeks-section"><h3>${escapeHtml(title)} (${items.length})</h3><ul>${items.map(renderItem).join('')}</ul></div>`;
    }

    const rehearsalTimeFmt = new Intl.DateTimeFormat(undefined, { weekday: 'long', month: 'long', day: 'numeric', hour: 'numeric', minute: '2-digit' });

    container.innerHTML = [
        section('Gigs', data.gigs, (g) => `<li>${escapeHtml(g.title)} - ${escapeHtml(g.date)}${g.venue ? ` @ ${escapeHtml(g.venue)}` : ''}</li>`),
        section('Rehearsals', data.rehearsals, (r) => `<li>${escapeHtml(r.title)} - ${escapeHtml(rehearsalTimeFmt.format(new Date(r.startsAt)))}${r.location ? ` @ ${escapeHtml(r.location)}` : ''}</li>`),
        section('My Open Gig Prep Tasks', data.openPrepTasks, (t) => `<li>${escapeHtml(t.gigTitle)}: [${escapeHtml(PREP_LIST_LABELS[t.listType] || t.listType)}] ${escapeHtml(t.text)}</li>`)
    ].join('');
}

// --- Venue Campaign Status ---
const CAMPAIGN_STATUS_LABELS = { NotStarted: 'Not started', Active: 'Active', Paused: 'Paused', CompleteBooked: 'Booked', CompleteRejected: 'Rejected' };

async function loadVenueCampaignStatusWidget() {
    const res = await fetch('/api/venue-campaigns');
    const venues = res.ok ? await res.json() : [];

    const counts = {};
    for (const v of venues) counts[v.status] = (counts[v.status] || 0) + 1;

    const container = document.getElementById('landing-venue-campaign-status');
    const order = ['Active', 'CompleteBooked', 'Paused', 'CompleteRejected', 'NotStarted'];
    container.innerHTML = order.map((status) => `
        <div class="landing-stat-tile">
            <span class="landing-stat-value">${counts[status] || 0}</span>
            <span class="landing-stat-label">${escapeHtml(CAMPAIGN_STATUS_LABELS[status])}</span>
        </div>
    `).join('');
}

// --- Accounting Graph (admin-only - see WIDGETS' adminOnly note) ---
async function loadAccountingGraphWidget() {
    const container = document.getElementById('landing-accounting-graph');
    const year = new Date().getFullYear();
    const res = await fetch(`/api/accounting/summary?year=${year}`);
    if (!res.ok) {
        // Belt-and-suspenders case: someone lost admin mid-session with
        // this widget already in their layout. Explain plainly rather
        // than showing a blank box or a raw error.
        container.innerHTML = '<p class="save-note">You need Band Admin access to see this.</p>';
        return;
    }
    const data = await res.json();
    const usdFmt = new Intl.NumberFormat(undefined, { style: 'currency', currency: 'USD', maximumFractionDigits: 0 });
    const max = Math.max(1, ...data.quarters.map((q) => q.grossAmount));
    const barWidth = 56, gap = 22, chartHeight = 90, labelHeight = 34;
    const svgWidth = data.quarters.length * (barWidth + gap);

    const bars = data.quarters.map((q, i) => {
        const h = Math.round((q.grossAmount / max) * chartHeight);
        const x = i * (barWidth + gap);
        return `
            <text x="${x + barWidth / 2}" y="${chartHeight - h - 6}" text-anchor="middle" class="landing-graph-value">${q.grossAmount > 0 ? usdFmt.format(q.grossAmount) : ''}</text>
            <rect x="${x}" y="${chartHeight - h}" width="${barWidth}" height="${Math.max(h, 1)}" rx="4" class="landing-graph-bar"></rect>
            <text x="${x + barWidth / 2}" y="${chartHeight + 20}" text-anchor="middle" class="landing-graph-label">Q${q.quarter}</text>
        `;
    }).join('');

    container.innerHTML = `
        <p class="save-note">${year} - ${usdFmt.format(data.totalGross)} total across ${data.gigCount} gig${data.gigCount === 1 ? '' : 's'}</p>
        <svg viewBox="0 0 ${svgWidth} ${chartHeight + labelHeight}" width="100%" height="${chartHeight + labelHeight}">${bars}</svg>
    `;
}

// --- SuperAdmin: Bands (superAdminOnly - see WIDGETS' note) ---
async function loadBandsWidget() {
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
}

// --- SuperAdmin: Platform stats (superAdminOnly - see WIDGETS' note) ---
// Whole-platform stats, not scoped to whichever band happens to be active
// in the switcher - replaces the old Connectivity Status widget, which
// only ever reflected one band and wasn't a meaningful "how's the
// platform doing" view for a SuperAdmin.
async function loadPlatformStatsWidget() {
    const [statsRes, pendingRes] = await Promise.all([
        fetch('/api/superadmin/stats'),
        fetch('/api/song-edit-requests/pending-count')
    ]);
    const stats = statsRes.ok ? await statsRes.json() : {};
    const pending = pendingRes.ok ? await pendingRes.json() : { count: 0 };

    const tiles = [
        { label: 'Active bands', value: stats.activeBands ?? '—' },
        { label: 'Archived bands', value: stats.archivedBands ?? '—' },
        { label: 'Total users', value: stats.totalUsers ?? '—' },
        { label: 'Signed up, last 30 days', value: stats.signupsLast30Days ?? '—' },
        { label: 'Pending song reviews', value: pending.count ?? 0, href: '/notifications' }
    ];

    document.getElementById('landing-platform-stats').innerHTML = tiles.map((t) => `
        <${t.href ? 'a href="' + t.href + '"' : 'div'} class="landing-stat-tile">
            <span class="landing-stat-value">${t.value}</span>
            <span class="landing-stat-label">${escapeHtml(t.label)}</span>
        </${t.href ? 'a' : 'div'}>
    `).join('');
}

// --- Song Catalog (open to any user, not bandSpecific - see WIDGETS' note) ---
// "Summarize and navigate," not browse in place - band-admin.js's own
// Song Catalog section already has the real search/filter/edit UI, so this
// widget stays a small pointer to it rather than duplicating that grid.
async function loadSongCatalogWidget() {
    const container = document.getElementById('landing-song-catalog-stats');
    const res = await fetch('/api/songs');
    if (!res.ok) {
        // Belt-and-suspenders, same idea as the Accounting Graph widget's
        // own fallback: GET /api/songs still requires a resolvable active
        // Band today for anyone but a SuperAdmin (see SongsController), so
        // a member with no Band selected and more than one membership can
        // land here. Explain plainly rather than a blank stat row.
        container.innerHTML = '<p class="save-note">—</p>';
        return;
    }
    const songs = await res.json();
    const underReview = songs.filter((s) => s.status === 'PendingReview' || s.pendingEditRequestId).length;

    const tiles = [
        { label: 'Songs in catalog', value: songs.length },
        { label: 'Under review', value: underReview }
    ];
    container.innerHTML = tiles.map((t) => `
        <div class="landing-stat-tile">
            <span class="landing-stat-value">${t.value}</span>
            <span class="landing-stat-label">${escapeHtml(t.label)}</span>
        </div>
    `).join('');
}

// --- Customize Widgets: pick which show, drag to reorder ---
function initCustomizeModal() {
    const backdrop = document.getElementById('landing-customize-modal-backdrop');
    const list = document.getElementById('landing-customize-list');
    const status = document.getElementById('landing-customize-status');

    function renderList() {
        // Visible widgets first (in their saved order), then every hidden
        // one appended in its shipped default order - so a widget the
        // user has hidden is still there to re-check, without needing its
        // own separate "add back" affordance.
        const pickable = WIDGETS.filter((w) => (!w.adminOnly || isAdminGlobal) && (!w.superAdminOnly || isSuperAdminGlobal)).map((w) => w.key);
        const hiddenInDefaultOrder = pickable.filter((k) => !dashboardWidgets.includes(k));
        const orderedKeys = [...dashboardWidgets.filter((k) => pickable.includes(k)), ...hiddenInDefaultOrder];

        list.innerHTML = '';
        for (const key of orderedKeys) {
            const widget = WIDGETS.find((w) => w.key === key);
            const li = document.createElement('li');
            li.className = 'landing-customize-item';
            li.draggable = true;
            li.dataset.key = key;
            li.innerHTML = `
                <span class="drag-handle" title="Drag to reorder">&#9776;</span>
                <label><input type="checkbox" ${dashboardWidgets.includes(key) ? 'checked' : ''}> ${escapeHtml(widget.label)}</label>
            `;
            list.appendChild(li);
        }
    }

    let dragged = null;
    list.addEventListener('dragstart', (e) => {
        dragged = e.target.closest('.landing-customize-item');
        dragged?.classList.add('dragging');
    });
    list.addEventListener('dragend', () => {
        dragged?.classList.remove('dragging');
        dragged = null;
        list.querySelectorAll('.drag-over').forEach((el) => el.classList.remove('drag-over'));
    });
    list.addEventListener('dragover', (e) => {
        e.preventDefault();
        const target = e.target.closest('.landing-customize-item');
        if (!target || target === dragged) return;
        list.querySelectorAll('.drag-over').forEach((el) => el.classList.remove('drag-over'));
        target.classList.add('drag-over');
    });
    list.addEventListener('drop', (e) => {
        e.preventDefault();
        const target = e.target.closest('.landing-customize-item');
        target?.classList.remove('drag-over');
        if (!target || !dragged || target === dragged) return;
        const rect = target.getBoundingClientRect();
        const before = e.clientY < rect.top + rect.height / 2;
        target.insertAdjacentElement(before ? 'beforebegin' : 'afterend', dragged);
    });

    document.getElementById('landing-customize-btn').addEventListener('click', () => {
        status.textContent = '';
        renderList();
        backdrop.hidden = false;
    });
    document.getElementById('landing-customize-modal-close').addEventListener('click', () => { backdrop.hidden = true; });
    backdrop.addEventListener('click', (e) => { if (e.target === backdrop) backdrop.hidden = true; });

    document.getElementById('landing-customize-save-btn').addEventListener('click', async () => {
        const widgets = [...list.querySelectorAll('.landing-customize-item')]
            .filter((li) => li.querySelector('input').checked)
            .map((li) => li.dataset.key);

        status.textContent = 'Saving...';
        const res = await fetch('/api/profile/dashboard-widgets', {
            method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ widgets })
        });
        if (!res.ok) {
            const body = await res.json().catch(() => ({}));
            status.textContent = body.error || 'Could not save. Try again.';
            return;
        }
        // Reload rather than patch the DOM live - a widget just turned on
        // needs its own data fetched and its detail-modal click handler
        // attached, which is exactly what a normal page load already does.
        location.reload();
    });
}

init();
