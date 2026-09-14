let gigs = [];
let selectedGigRef = null;
let bandMembers = [];
let isBandAdmin = false;
let currentUserId = null;

// Buttons that only a BandAdmin/SuperAdmin can actually complete
// server-side by default - hidden (not just left clickable-then-rejected)
// for a plain member so there's no dead-end click, and so the page
// visually separates "yours to use" from "admin only" instead of showing
// all nine gig-detail actions with identical weight. Left visible either
// way: Copy/Assign a Setlist, Print Setlist, Save as Spotify Playlist, Gig
// Prep, and Accounting (its own view/edit split already exists) - every
// band member can genuinely use those. Each DOM id maps to a ControlKey
// the DB-driven visibility system (/api/control-visibility) can override
// per band - see ControlVisibilityDefaults.cs for the shipped default,
// which matches this hardcoded fallback exactly.
const ADMIN_ONLY_BUTTON_IDS = {
    'add-gig-btn': 'gig-sets:add-gig-btn',
    'gig-set-edit-btn': 'gig-sets:edit-btn',
    'gig-set-flyer-btn': 'gig-sets:flyer-btn',
    'gig-set-select-flyer-btn': 'gig-sets:select-flyer-btn',
    'gig-set-archive-btn': 'gig-sets:archive-btn'
};

function escapeHtml(str) {
    const div = document.createElement('div');
    div.textContent = str ?? '';
    return div.innerHTML;
}

function formatLength(seconds) {
    if (seconds == null) return '';
    const m = Math.floor(seconds / 60);
    const s = seconds % 60;
    return `${m}:${String(s).padStart(2, '0')}`;
}

async function init() {
    const res = await fetch('/api/profile/me');
    const me = await res.json();
    const hasBand = !!me.activeBandRole;

    document.getElementById('gig-sets-no-band').hidden = hasBand;
    document.getElementById('gig-sets-content').hidden = !hasBand;
    if (!hasBand) return;

    isBandAdmin = !!me.isAdmin;
    currentUserId = me.id;
    let visibility = {};
    try {
        const visRes = await fetch('/api/control-visibility');
        if (visRes.ok) visibility = await visRes.json();
    } catch { /* fall through to the hardcoded default below */ }
    for (const [id, controlKey] of Object.entries(ADMIN_ONLY_BUTTON_IDS)) {
        const el = document.getElementById(id);
        if (!el) continue;
        el.hidden = controlKey in visibility ? !visibility[controlKey] : !isBandAdmin;
    }

    await loadGigs();
    const membersRes = await fetch('/api/profile/band-members');
    bandMembers = membersRes.ok ? await membersRes.json() : [];

    // Sticky "current gig" - auto-open whichever gig this member was last
    // looking at (here, or in the Flyer Editor/Catalog), anywhere. A
    // stale ref (archived/deleted since) just silently finds nothing and
    // leaves the page on its normal unselected state.
    if (me.lastSelectedGigRef) {
        const sticky = gigs.find((g) => g.gigRef === me.lastSelectedGigRef);
        if (sticky) await selectGig(sticky);
    }
}

function writeLastSelectedGig(gigRef) {
    fetch('/api/profile/last-selected-gig', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ gigRef })
    }).catch(() => {}); // best-effort - a personal convenience setting, never worth blocking on
}

document.getElementById('add-gig-btn').addEventListener('click', async () => {
    const created = await window.openAddGigModal();
    if (!created) return;
    await loadGigs();
    const gig = gigs.find((g) => g.gigRef === created.gigRef);
    if (gig) await selectGig(gig);
});

async function loadGigs() {
    const upcomingBox = document.getElementById('gig-list-upcoming');
    const pastBox = document.getElementById('gig-list-past');
    const res = await fetch('/api/gig-sets/gigs');
    if (!res.ok) { upcomingBox.innerHTML = '<p class="save-note">Could not load gigs.</p>'; return; }
    gigs = await res.json();

    renderGigList(upcomingBox, gigs.filter((g) => !g.isPast));
    renderGigList(pastBox, gigs.filter((g) => g.isPast));
    if (gigs.filter((g) => !g.isPast).length === 0) upcomingBox.innerHTML = '<p class="save-note">No upcoming gigs yet.</p>';
}

function renderGigList(box, list) {
    box.innerHTML = '';
    for (const gig of list) {
        const btn = document.createElement('button');
        btn.type = 'button';
        btn.className = 'gig-list-item' + (gig.gigRef === selectedGigRef ? ' selected' : '');
        btn.innerHTML = `
            <div class="gig-title">${escapeHtml(gig.title)}</div>
            <div class="gig-meta">${escapeHtml(gig.venue || '')} ${escapeHtml(gig.date || '')} ${escapeHtml(gig.time || '')}</div>
            <div class="gig-song-count">${gig.songCount} song${gig.songCount === 1 ? '' : 's'}</div>
        `;
        btn.addEventListener('click', () => selectGig(gig));
        box.appendChild(btn);
    }
}

async function selectGig(gig) {
    selectedGigRef = gig.gigRef;
    writeLastSelectedGig(gig.gigRef);
    [...document.querySelectorAll('.gig-list-item')].forEach((el) => el.classList.remove('selected'));
    await loadGigs(); // re-render with the new selection highlighted

    document.getElementById('gig-set-detail').hidden = false;
    document.getElementById('gig-set-title').textContent = gig.title;
    document.getElementById('gig-set-meta').textContent = [gig.venue, gig.date, gig.time].filter(Boolean).join(' · ');
    updateSetSummary();

    await loadArtifactPanel();
    await loadRideCoordination();
}

// Song count/duration are already on the gig object (ListGigs computes
// them server-side) - no need to fetch the setlist itself just to show
// this compact line, only when the editor modal actually opens.
function updateSetSummary() {
    const summary = document.getElementById('gig-set-summary');
    const gig = gigs.find((g) => g.gigRef === selectedGigRef);
    if (!summary || !gig) return;
    summary.textContent = gig.songCount === 0
        ? 'No songs in this set yet.'
        : `${gig.songCount} song${gig.songCount === 1 ? '' : 's'} - ${gig.duration} total`;
}

// --- "Everything associated with this gig" ---
async function loadArtifactPanel() {
    const panel = document.getElementById('gig-artifact-panel');
    const res = await fetch(`/api/gig-sets/${encodeURIComponent(selectedGigRef)}/items`);
    if (!res.ok) { panel.innerHTML = ''; return; }
    const data = await res.json();
    panel.innerHTML = renderArtifactPanelHtml(data);
}

function renderArtifactPanelHtml(data) {
    const flyerHtml = data.flyer
        ? `<div class="gig-flyer-block"><h4>Flyer</h4><img src="/flyer-cache/${encodeURIComponent(data.gig.flyerMain || '')}" alt="Flyer" class="gig-flyer-thumb">
           ${data.flyer.sourceImageLabel ? `<p class="save-note">Built from: ${escapeHtml(data.flyer.sourceImageLabel)}</p>` : '<p class="save-note">Its source image has since been deleted - it can no longer be re-opened in the editor.</p>'}</div>`
        : '<p class="save-note">No flyer for this gig yet.</p>';

    const itemsHtml = data.items.length === 0
        ? '<p class="save-note">No scheduled posts tied to this gig.</p>'
        : `<ul class="gig-items-list">${data.items.map((i) => `
            <li>${escapeHtml(i.category)} - ${escapeHtml(i.contentType)}${i.postedAt ? ` (posted ${new Date(i.postedAt).toLocaleDateString()})` : ' (not posted yet)'}
                ${i.artifacts.length ? `<ul>${i.artifacts.map((a) => `<li>${escapeHtml(a.artifactType)}${a.textValue ? ': ' + escapeHtml(a.textValue) : ''}</li>`).join('')}</ul>` : ''}
            </li>`).join('')}</ul>`;

    return `<h3>Everything for this gig</h3>${flyerHtml}<h4>Scheduled posts</h4>${itemsHtml}`;
}

// --- Ride Coordination (D11) ---
let rideCoordinationMeeting = { meetingPoint: '', meetingTime: '' };

async function loadRideCoordination() {
    const res = await fetch(`/api/ride-coordination/${encodeURIComponent(selectedGigRef)}`);
    const data = res.ok ? await res.json() : { meetingPoint: null, meetingTime: null, drivers: [] };
    rideCoordinationMeeting = { meetingPoint: data.meetingPoint || '', meetingTime: data.meetingTime || '' };

    const display = document.getElementById('ride-coordination-meeting-display');
    display.textContent = data.meetingPoint || data.meetingTime
        ? `Meeting at ${data.meetingPoint || '?'}${data.meetingTime ? ` - ${data.meetingTime}` : ''}`
        : 'No meeting point set yet.';

    const list = document.getElementById('ride-coordination-driver-list');
    list.innerHTML = data.drivers.length === 0
        ? '<li class="save-note">Nobody has offered to drive yet.</li>'
        : data.drivers.map((d) => `<li>${escapeHtml(d.name)}${d.seatsAvailable != null ? ` - ${d.seatsAvailable} seat${d.seatsAvailable === 1 ? '' : 's'} open` : ''}${d.note ? ` (${escapeHtml(d.note)})` : ''}</li>`).join('');

    const mine = data.drivers.find((d) => d.userId === currentUserId);
    const checkbox = document.getElementById('ride-coordination-im-driving');
    const fields = document.getElementById('ride-coordination-self-fields');
    checkbox.checked = !!mine;
    fields.hidden = !mine;
    document.getElementById('ride-coordination-seats').value = mine?.seatsAvailable ?? '';
    document.getElementById('ride-coordination-note').value = mine?.note ?? '';
}

document.getElementById('ride-coordination-meeting-edit-btn').addEventListener('click', () => {
    const form = document.getElementById('ride-coordination-meeting-form');
    form.meetingPoint.value = rideCoordinationMeeting.meetingPoint;
    form.meetingTime.value = rideCoordinationMeeting.meetingTime;
    form.hidden = false;
});
document.getElementById('ride-coordination-meeting-cancel-btn').addEventListener('click', () => {
    document.getElementById('ride-coordination-meeting-form').hidden = true;
});
document.getElementById('ride-coordination-meeting-form').addEventListener('submit', async (e) => {
    e.preventDefault();
    const form = e.target;
    await fetch(`/api/ride-coordination/${encodeURIComponent(selectedGigRef)}/meeting-point`, {
        method: 'PUT', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ meetingPoint: form.meetingPoint.value, meetingTime: form.meetingTime.value })
    });
    form.hidden = true;
    await loadRideCoordination();
});

document.getElementById('ride-coordination-im-driving').addEventListener('change', async (e) => {
    document.getElementById('ride-coordination-self-fields').hidden = !e.target.checked;
    if (!e.target.checked) {
        await fetch(`/api/ride-coordination/${encodeURIComponent(selectedGigRef)}/driving`, { method: 'DELETE' });
        await loadRideCoordination();
    }
});
document.getElementById('ride-coordination-save-btn').addEventListener('click', async () => {
    const seatsRaw = document.getElementById('ride-coordination-seats').value;
    await fetch(`/api/ride-coordination/${encodeURIComponent(selectedGigRef)}/driving`, {
        method: 'PUT', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
            seatsAvailable: seatsRaw === '' ? null : Number(seatsRaw),
            note: document.getElementById('ride-coordination-note').value
        })
    });
    await loadRideCoordination();
});

// --- View/Edit Setlist (opens the shared setlist-builder modal - see
// assets/setlistEditor.js, also used by the Calendar's Rehearsal modal) ---
document.getElementById('gig-set-view-edit-btn').addEventListener('click', () => {
    const gig = gigs.find((g) => g.gigRef === selectedGigRef);
    window.openSetlistEditor(selectedGigRef, {
        title: `Set - ${gig ? gig.title : ''}`,
        onSaved: async () => { await loadGigs(); updateSetSummary(); }
    });
});

// --- Copy or Assign a Setlist ---
// Grid of every OTHER gig's setlist (past or future - a setlist is just
// as likely to come from an upcoming gig, e.g. same tour/week, as from a
// past one) *and* every floating setlist (one not tied to any gig - see
// GigSet.IsFloating), sortable/paginated same as Catalog's Details view.
// Clicking a row previews its songs below; Confirm behavior depends on
// the row: a gig row copies its songs into this gig's set (same
// POST /import-from this always used, additive - nothing is replaced);
// a floating row hands the whole setlist over outright (POST
// /assign-floating - "no longer floating" afterward). Nope just clears
// the preview so another row can be tried, Cancel closes with no action.
const IMPORT_GIG_PAGE_SIZE = 10;
let importGigSortKey = 'date';
let importGigSortDir = 'asc';
let importGigPage = 1;
let importGigPreviewRef = null;
let floatingSetlists = [];

function closeImportGigModal() { document.getElementById('import-gig-modal-backdrop').hidden = true; }
document.getElementById('import-gig-modal-close').addEventListener('click', closeImportGigModal);
document.getElementById('import-gig-modal-backdrop').addEventListener('click', (e) => { if (e.target.id === 'import-gig-modal-backdrop') closeImportGigModal(); });

function importGigCandidates() {
    const dir = importGigSortDir === 'asc' ? 1 : -1;
    const gigRows = gigs.filter((g) => g.gigRef !== selectedGigRef).map((g) => ({ ...g, isFloating: false }));
    const floatingRows = floatingSetlists.map((f) => ({
        gigRef: f.gigRef, title: null, name: f.name, venue: null, date: null, sortDate: '',
        durationSeconds: f.durationSeconds, duration: f.duration, isFloating: true
    }));
    return [...gigRows, ...floatingRows].sort((a, b) => {
        if (importGigSortKey === 'venue') return dir * (a.venue || '').localeCompare(b.venue || '');
        if (importGigSortKey === 'title') return dir * (a.title || a.name || '').localeCompare(b.title || b.name || '');
        if (importGigSortKey === 'duration') return dir * ((a.durationSeconds || 0) - (b.durationSeconds || 0));
        return dir * (a.sortDate || '').localeCompare(b.sortDate || '');
    });
}

function importGigSortArrow(key) {
    if (importGigSortKey !== key) return '';
    return importGigSortDir === 'asc' ? ' &#9650;' : ' &#9660;';
}

function renderImportGigModal() {
    const body = document.getElementById('import-gig-modal-body');
    const all = importGigCandidates();
    const totalPages = Math.max(1, Math.ceil(all.length / IMPORT_GIG_PAGE_SIZE));
    importGigPage = Math.min(Math.max(1, importGigPage), totalPages);
    const pageItems = all.slice((importGigPage - 1) * IMPORT_GIG_PAGE_SIZE, importGigPage * IMPORT_GIG_PAGE_SIZE);

    body.innerHTML = `
        <h2>Copy or Assign a Setlist</h2>
        ${all.length === 0 ? '<p class="save-note">There are no other setlists yet.</p>' : `
            <table class="user-table import-gig-table">
                <thead>
                    <tr>
                        <th data-sort-key="title" class="sortable">Name${importGigSortArrow('title')}</th>
                        <th data-sort-key="venue" class="sortable">Venue Name${importGigSortArrow('venue')}</th>
                        <th data-sort-key="date" class="sortable">Date${importGigSortArrow('date')}</th>
                        <th data-sort-key="duration" class="sortable">Duration${importGigSortArrow('duration')}</th>
                    </tr>
                </thead>
                <tbody>
                    ${pageItems.map((g) => `
                        <tr class="import-gig-row${g.gigRef === importGigPreviewRef ? ' selected' : ''}" data-gig-ref="${escapeHtml(g.gigRef)}">
                            <td>${g.isFloating ? `<strong>${escapeHtml(g.name)}</strong> <span class="unassigned-badge">Unassigned</span>` : escapeHtml(g.title)}</td>
                            <td>${escapeHtml(g.venue || '')}</td>
                            <td>${escapeHtml(g.date || '')}</td>
                            <td>${g.duration || '0:00'}</td>
                        </tr>
                    `).join('')}
                </tbody>
            </table>
            <div class="import-gig-pagination">
                <button type="button" id="import-gig-page-prev" ${importGigPage <= 1 ? 'disabled' : ''}>&laquo; Prev</button>
                <span>Page ${importGigPage} of ${totalPages}</span>
                <button type="button" id="import-gig-page-next" ${importGigPage >= totalPages ? 'disabled' : ''}>Next &raquo;</button>
            </div>
            <div id="import-gig-preview"></div>
        `}
        <div class="cred-form-buttons">
            <button type="button" id="import-gig-cancel-btn">Cancel</button>
        </div>
    `;

    if (all.length > 0) {
        body.querySelectorAll('th[data-sort-key]').forEach((th) => {
            th.addEventListener('click', () => {
                const key = th.dataset.sortKey;
                if (importGigSortKey === key) importGigSortDir = importGigSortDir === 'asc' ? 'desc' : 'asc';
                else { importGigSortKey = key; importGigSortDir = key === 'duration' ? 'desc' : 'asc'; }
                importGigPage = 1;
                renderImportGigModal();
            });
        });
        body.querySelector('#import-gig-page-prev').addEventListener('click', () => { importGigPage--; renderImportGigModal(); });
        body.querySelector('#import-gig-page-next').addEventListener('click', () => { importGigPage++; renderImportGigModal(); });
        body.querySelectorAll('.import-gig-row').forEach((row) => {
            row.addEventListener('click', () => {
                importGigPreviewRef = row.dataset.gigRef;
                renderImportGigModal();
            });
        });
        renderImportGigPreview();
    }

    body.querySelector('#import-gig-cancel-btn').addEventListener('click', closeImportGigModal);
}

async function renderImportGigPreview() {
    const box = document.getElementById('import-gig-preview');
    if (!box) return;
    if (!importGigPreviewRef) { box.innerHTML = ''; return; }

    const row = importGigCandidates().find((r) => r.gigRef === importGigPreviewRef);
    const isFloating = !!(row && row.isFloating);
    box.innerHTML = '<p class="save-note">Loading setlist...</p>';
    const res = await fetch(`/api/gig-sets/${encodeURIComponent(importGigPreviewRef)}`);
    const data = res.ok ? await res.json() : { songs: [] };
    const songs = data.songs || [];

    box.innerHTML = `
        <h3>${escapeHtml(row ? (row.title || row.name) : 'Setlist')}</h3>
        <table class="user-table import-gig-setlist-table">
            <thead><tr><th>Title</th><th>Artist</th><th>Key</th><th>Length</th></tr></thead>
            <tbody>
                ${songs.map((s) => `
                    <tr>
                        <td>${escapeHtml(s.title)}${s.isManual ? ' <em>(manual)</em>' : ''}</td>
                        <td>${escapeHtml(s.originalArtist || '')}</td>
                        <td>${escapeHtml(s.key || '')}</td>
                        <td>${s.lengthSeconds != null ? formatLength(s.lengthSeconds) : ''}</td>
                    </tr>
                `).join('')}
            </tbody>
        </table>
        ${songs.length === 0 ? `<p class="save-note">${isFloating ? 'This setlist has no songs yet.' : 'This gig has no setlist.'}</p>` : ''}
        <div class="cred-form-buttons">
            <button type="button" id="import-gig-nope-btn">Nope</button>
            <button type="button" id="import-gig-confirm-btn" ${!isFloating && songs.length === 0 ? 'disabled' : ''}>${isFloating ? 'Assign to This Gig' : 'Confirm'}</button>
        </div>
    `;

    box.querySelector('#import-gig-nope-btn').addEventListener('click', () => {
        importGigPreviewRef = null;
        renderImportGigModal();
    });
    box.querySelector('#import-gig-confirm-btn').addEventListener('click', async () => {
        const url = isFloating
            ? `/api/gig-sets/${encodeURIComponent(selectedGigRef)}/assign-floating/${encodeURIComponent(importGigPreviewRef)}`
            : `/api/gig-sets/${encodeURIComponent(selectedGigRef)}/import-from/${encodeURIComponent(importGigPreviewRef)}`;
        const res2 = await fetch(url, { method: 'POST' });
        const resBody = await res2.json();
        if (res2.ok) {
            closeImportGigModal();
            await loadGigs();
            updateSetSummary();
        } else {
            alert(resBody.error || (isFloating ? 'Could not assign that setlist.' : 'Could not copy that setlist.'));
        }
    });
}

// --- Edit Gig (covers everything the Flyer Editor can also set on this
// same Gig - title/venue/address/date/times/with-acts/tickets) ---
let editGigWithRows = [];

function renderEditGigWithRows() {
    const container = document.querySelector('#edit-gig-form [data-with-list]');
    container.innerHTML = '';
    if (editGigWithRows.length === 0) editGigWithRows = [{ name: '', url: '' }];
    editGigWithRows.forEach((row, i) => {
        const rowEl = document.createElement('div');
        rowEl.className = 'with-act-row';
        rowEl.innerHTML = `
            <input type="text" class="with-act-name" placeholder="e.g. Attica - A Nirvana Tribute" maxlength="200" value="${escapeHtml(row.name)}">
            <input type="text" class="with-act-url" placeholder="https:// (optional)" maxlength="500" value="${escapeHtml(row.url)}">
            <button type="button" class="remove-with-act-btn" ${editGigWithRows.length === 1 ? 'disabled' : ''}>&times;</button>
        `;
        rowEl.querySelector('.with-act-name').addEventListener('input', (e) => { editGigWithRows[i].name = e.target.value; });
        rowEl.querySelector('.with-act-url').addEventListener('input', (e) => { editGigWithRows[i].url = e.target.value; });
        rowEl.querySelector('.remove-with-act-btn').addEventListener('click', () => { editGigWithRows.splice(i, 1); renderEditGigWithRows(); });
        container.appendChild(rowEl);
    });
}
document.getElementById('edit-gig-add-with-btn').addEventListener('click', () => { editGigWithRows.push({ name: '', url: '' }); renderEditGigWithRows(); });

function closeEditGigModal() { document.getElementById('edit-gig-modal-backdrop').hidden = true; }
document.getElementById('edit-gig-modal-close').addEventListener('click', closeEditGigModal);
document.getElementById('edit-gig-cancel-btn').addEventListener('click', closeEditGigModal);
document.getElementById('edit-gig-modal-backdrop').addEventListener('click', (e) => { if (e.target.id === 'edit-gig-modal-backdrop') closeEditGigModal(); });

document.getElementById('gig-set-edit-btn').addEventListener('click', async () => {
    if (!selectedGigRef) return;
    const status = document.getElementById('edit-gig-status');
    status.textContent = 'Loading...';
    document.getElementById('edit-gig-modal-backdrop').hidden = false;

    const res = await fetch(`/api/gigs/${encodeURIComponent(selectedGigRef)}`);
    if (!res.ok) { status.textContent = 'Could not load this gig.'; return; }
    const gig = await res.json();
    status.textContent = '';

    const form = document.getElementById('edit-gig-form');
    form.title.value = gig.title || '';
    form.venue.value = gig.venue || '';
    form.venueUrl.value = gig.venueUrl || '';
    form.address.value = gig.address || '';
    form.date.value = gig.dateIso || '';
    window.checkGigDateAvailability(form.date.value, document.getElementById('edit-gig-availability-warning'));
    form.time.value = gig.time || '';
    form.doorsTime.value = gig.doorsTime || '';
    form.openerTime.value = gig.openerTime || '';
    form.headlinerTime.value = gig.headlinerTime || '';
    form.ticketsUrl.value = gig.ticketsUrl || '';
    form.customTicketsText.value = gig.customTicketsText || '';
    const mode = gig.ticketMode || 'url';
    form.querySelectorAll('input[name="ticketMode"]').forEach((r) => { r.checked = r.value === mode; });
    form.ticketsUrl.disabled = mode !== 'url';
    form.customTicketsText.disabled = mode !== 'custom';

    editGigWithRows = (gig.with || []).map((w) => ({ name: w.name || '', url: w.url || '' }));
    renderEditGigWithRows();

    const syncNote = document.getElementById('edit-gig-flyer-sync-note');
    const syncRow = document.getElementById('edit-gig-flyer-sync-row');
    const syncLabel = document.getElementById('edit-gig-flyer-sync-label');
    const syncCheckbox = syncRow.querySelector('input[type="checkbox"]');
    if (gig.flyerTitleSync) {
        const { count, allMatch, titles } = gig.flyerTitleSync;
        const flyerWord = count === 1 ? 'flyer' : 'flyers';
        syncNote.hidden = false;
        syncNote.textContent = allMatch
            ? `${count} ${flyerWord} for this gig already use this title.`
            : `${count} ${flyerWord} for this gig - title${titles.length > 1 ? 's' : ''} there: ${titles.map((t) => `"${t}"`).join(', ')}.`;
        syncRow.hidden = false;
        syncLabel.textContent = `Also update the title on ${count} ${flyerWord} when I save`;
        syncCheckbox.checked = false;
    } else {
        syncNote.hidden = true;
        syncRow.hidden = true;
        syncCheckbox.checked = false;
    }
});

document.querySelectorAll('#edit-gig-form input[name="ticketMode"]').forEach((r) => {
    r.addEventListener('change', () => {
        const form = document.getElementById('edit-gig-form');
        form.ticketsUrl.disabled = form.ticketMode.value !== 'url';
        form.customTicketsText.disabled = form.ticketMode.value !== 'custom';
    });
});

// D10: same non-blocking availability heads-up as the Add Gig modal -
// window.checkGigDateAvailability comes from addGigModal.js, which this
// page already loads. Checked both on manual date changes and once right
// after the form is populated with the gig's existing date (that initial
// fill doesn't fire a "change" event on its own).
document.getElementById('edit-gig-form').date.addEventListener('change', (e) => {
    window.checkGigDateAvailability(e.target.value, document.getElementById('edit-gig-availability-warning'));
});

document.getElementById('edit-gig-form').addEventListener('submit', async (e) => {
    e.preventDefault();
    if (!selectedGigRef) return;
    const form = e.target;
    const status = document.getElementById('edit-gig-status');

    if (!confirm('Save these changes to the gig? This updates the gig everywhere it appears (Calendar, the connected site, flyers built from it).')) return;

    status.textContent = 'Saving...';
    const payload = {
        title: form.title.value.trim(),
        venue: form.venue.value.trim(),
        venueUrl: form.venueUrl.value.trim(),
        address: form.address.value.trim(),
        date: form.date.value,
        time: form.time.value.trim(),
        doorsTime: form.doorsTime.value.trim(),
        openerTime: form.openerTime.value.trim(),
        headlinerTime: form.headlinerTime.value.trim(),
        ticketMode: form.ticketMode.value,
        ticketsUrl: form.ticketsUrl.value.trim(),
        customTicketsText: form.customTicketsText.value.trim(),
        with: JSON.stringify(editGigWithRows.filter((r) => r.name.trim() || r.url.trim()).map((r) => ({ name: r.name.trim(), url: r.url.trim() }))),
        syncTitleToFlyers: form.syncTitleToFlyers.checked
    };
    const res = await fetch(`/api/gigs/${encodeURIComponent(selectedGigRef)}`, {
        method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(payload)
    });
    const body = await res.json().catch(() => ({}));
    // A 502 here still means the gig itself was saved - only the live-site
    // push failed (GigsController.Update saves before attempting that) -
    // so the UI must still refresh to the new values, same fix as
    // calendar.js's drag-reschedule needed for the identical situation.
    // A genuine validation failure (400) or a gig that's disappeared out
    // from under this modal (404) is the only case worth leaving open.
    if (res.status === 400 || res.status === 404) {
        status.textContent = body.error || 'Could not save the gig.';
        return;
    }
    closeEditGigModal();
    const savedRef = selectedGigRef;
    await loadGigs();
    const updated = gigs.find((g) => g.gigRef === savedRef);
    if (updated) await selectGig(updated);
    if (!res.ok) alert(body.error || 'Saved, but could not push the change to the site.');
});

document.getElementById('gig-set-import-btn').addEventListener('click', async () => {
    importGigSortKey = 'date';
    importGigSortDir = 'asc';
    importGigPage = 1;
    importGigPreviewRef = null;
    const res = await fetch('/api/gig-sets/floating');
    floatingSetlists = res.ok ? await res.json() : [];
    renderImportGigModal();
    document.getElementById('import-gig-modal-backdrop').hidden = false;
});

// --- Create/Edit Flyer (pick any General image, then hand off to the
// shared Flyer Editor - no separate "Flyer Template" step) ---
document.getElementById('gig-set-flyer-btn').addEventListener('click', () => {
    writeLastSelectedGig(selectedGigRef);
    // General, not Flyers - "Create Flyer" buttons live on General images,
    // this is where the actual flyer work starts.
    location.href = `/catalog.html?gigRef=${encodeURIComponent(selectedGigRef)}`;
});

// --- Select Flyer (pick which of this gig's Flyers is "the" one -
// wherever a gig's flyer is shown elsewhere, e.g. Web Presence tiles) ---
function catalogFileUrlLocal(relPath) {
    return '/' + String(relPath).replace(/\\/g, '/').replace(/^data\/catalog\//, 'catalog-files/');
}

function closeSelectFlyerModal() { document.getElementById('select-flyer-modal-backdrop').hidden = true; }
document.getElementById('select-flyer-modal-close').addEventListener('click', closeSelectFlyerModal);
document.getElementById('select-flyer-modal-backdrop').addEventListener('click', (e) => { if (e.target.id === 'select-flyer-modal-backdrop') closeSelectFlyerModal(); });

async function selectFlyer(flyerId) {
    const res = await fetch(`/api/gigs/${encodeURIComponent(selectedGigRef)}/selected-flyer`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ flyerId })
    });
    const body = await res.json().catch(() => ({}));
    if (res.ok) {
        closeSelectFlyerModal();
        await loadArtifactPanel();
    } else {
        alert(body.error || 'Could not select that flyer.');
    }
}

document.getElementById('gig-set-select-flyer-btn').addEventListener('click', async () => {
    if (!selectedGigRef) return;
    const grid = document.getElementById('select-flyer-grid');
    grid.innerHTML = '<p class="save-note">Loading flyers...</p>';
    document.getElementById('select-flyer-modal-backdrop').hidden = false;

    const [flyersRes, gigRes] = await Promise.all([
        fetch(`/api/gigs/${encodeURIComponent(selectedGigRef)}/flyers`),
        fetch(`/api/gigs/${encodeURIComponent(selectedGigRef)}`)
    ]);
    const flyers = flyersRes.ok ? await flyersRes.json() : [];
    const gig = gigRes.ok ? await gigRes.json() : { title: '', date: '' };
    if (flyers.length === 0) {
        grid.innerHTML = '<p class="save-note">No flyers have been created for this gig yet - use "Create/Edit Flyer" first.</p>';
        return;
    }

    // Assuming there's 1+ flyer per gig, and every tile here is already
    // scoped to this one gig, the "Flyer - <title>" label alone doesn't
    // change tile to tile - what does is which one is actually live, so
    // that's what gets the prominent treatment (badge + explicit
    // checkbox) rather than just distinguishing tiles by creation date.
    const gigLabel = `${gig.title || ''}${gig.date ? ', ' + gig.date : ''}`;
    grid.innerHTML = flyers.map((f) => `
        <div class="select-flyer-tile${f.isSelected ? ' selected' : ''}" data-flyer-id="${f.id}">
            <button type="button" class="select-flyer-tile-image" data-flyer-id="${f.id}">
                <img src="${catalogFileUrlLocal(f.filePath)}" alt="Flyer">
                ${f.isSelected ? '<span class="select-flyer-live-badge">Website Live</span>' : ''}
            </button>
            <span class="select-flyer-tile-label">Flyer - ${escapeHtml(gigLabel)}</span>
            <span class="save-note">Saved ${new Date(f.createdAt).toLocaleDateString()}</span>
            <label class="checkbox-label select-flyer-live-checkbox">
                <input type="checkbox" data-flyer-id="${f.id}" ${f.isSelected ? 'checked' : ''}>
                Make this the live website flyer for ${escapeHtml(gigLabel)}
            </label>
        </div>
    `).join('');

    grid.querySelectorAll('.select-flyer-tile-image').forEach((btn) => {
        btn.addEventListener('click', () => selectFlyer(btn.dataset.flyerId));
    });
    grid.querySelectorAll('.select-flyer-live-checkbox input').forEach((cb) => {
        cb.addEventListener('change', () => {
            if (cb.checked) selectFlyer(cb.dataset.flyerId);
            else cb.checked = true; // exactly one flyer is always "the" live one - unchecking has nothing to switch to
        });
    });
});

// --- Archive / Unarchive ---
// Both flows always confirm - even with nothing else tied to the gig, the
// checkbox to touch the Website Calendar still needs an explicit yes/no,
// so the "skip the modal when there's nothing to warn about" shortcut this
// used to take no longer applies.
function closeArchiveGigModal() { document.getElementById('archive-gig-modal-backdrop').hidden = true; }
document.getElementById('archive-gig-modal-close').addEventListener('click', closeArchiveGigModal);
document.getElementById('archive-gig-modal-backdrop').addEventListener('click', (e) => { if (e.target.id === 'archive-gig-modal-backdrop') closeArchiveGigModal(); });
document.getElementById('archive-gig-cancel-btn').addEventListener('click', closeArchiveGigModal);

async function doArchiveGig() {
    const removeFromWebsiteCalendar = document.getElementById('archive-gig-remove-from-calendar').checked;
    const res = await fetch(`/api/gigs/${encodeURIComponent(selectedGigRef)}/archive`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ removeFromWebsiteCalendar })
    });
    if (!res.ok) {
        const body = await res.json().catch(() => null);
        alert(body?.error || 'Could not archive this gig.');
        if (res.status !== 502) return;
    }
    closeArchiveGigModal();
    document.getElementById('gig-set-detail').hidden = true;
    selectedGigRef = null;
    await loadGigs();
}

document.getElementById('gig-set-archive-btn').addEventListener('click', async () => {
    if (!selectedGigRef) return;
    const res = await fetch(`/api/gigs/${encodeURIComponent(selectedGigRef)}/archive-preview`);
    const preview = res.ok ? await res.json() : { scheduleItems: 0, flyers: 0, setlistSongs: 0, gigPrepItems: 0 };

    const bullets = [];
    if (preview.scheduleItems > 0) bullets.push(`${preview.scheduleItems} scheduled post${preview.scheduleItems === 1 ? '' : 's'} (Web Presence)`);
    if (preview.flyers > 0) bullets.push(`${preview.flyers} flyer${preview.flyers === 1 ? '' : 's'}`);
    if (preview.setlistSongs > 0) bullets.push(`${preview.setlistSongs} song${preview.setlistSongs === 1 ? '' : 's'} in the setlist`);
    if (preview.gigPrepItems > 0) bullets.push(`${preview.gigPrepItems} gig prep item${preview.gigPrepItems === 1 ? '' : 's'}`);

    document.getElementById('archive-gig-preview-list').innerHTML = bullets.length === 0
        ? '<li>Nothing else is tied to this gig.</li>'
        : bullets.map((b) => `<li>${escapeHtml(b)}</li>`).join('');
    document.getElementById('archive-gig-remove-from-calendar').checked = false;
    document.getElementById('archive-gig-modal-backdrop').hidden = false;
});

document.getElementById('archive-gig-ok-btn').addEventListener('click', doArchiveGig);

// Unarchive mirrors Archive exactly - always confirms, and its own
// checkbox opts into re-adding the gig to the Website Calendar (default
// unchecked, same "ask every time" rule as everywhere else that pushes
// live).
function closeUnarchiveGigModal() { document.getElementById('unarchive-gig-modal-backdrop').hidden = true; }
document.getElementById('unarchive-gig-modal-close').addEventListener('click', closeUnarchiveGigModal);
document.getElementById('unarchive-gig-modal-backdrop').addEventListener('click', (e) => { if (e.target.id === 'unarchive-gig-modal-backdrop') closeUnarchiveGigModal(); });
document.getElementById('unarchive-gig-cancel-btn').addEventListener('click', closeUnarchiveGigModal);

let unarchiveGigRef = null;

async function doUnarchiveGig() {
    if (!unarchiveGigRef) return;
    const addToWebsiteCalendar = document.getElementById('unarchive-gig-add-to-calendar').checked;
    const res = await fetch(`/api/gigs/${encodeURIComponent(unarchiveGigRef)}/unarchive`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ addToWebsiteCalendar })
    });
    if (!res.ok) {
        const body = await res.json().catch(() => null);
        alert(body?.error || 'Could not unarchive that gig.');
        if (res.status !== 502) return;
    }
    archivedGigs = archivedGigs.filter((g) => g.gigRef !== unarchiveGigRef);
    closeUnarchiveGigModal();
    renderArchivedGigsModal();
    await loadGigs();
}

document.getElementById('unarchive-gig-ok-btn').addEventListener('click', doUnarchiveGig);

async function openUnarchiveGigModal(gigRef) {
    unarchiveGigRef = gigRef;
    const res = await fetch(`/api/gigs/${encodeURIComponent(gigRef)}/unarchive-preview`);
    const preview = res.ok ? await res.json() : { scheduleItems: 0, flyers: 0 };

    const bullets = [];
    if (preview.scheduleItems > 0) bullets.push(`${preview.scheduleItems} scheduled post${preview.scheduleItems === 1 ? '' : 's'} (Web Presence)`);
    if (preview.flyers > 0) bullets.push(`${preview.flyers} flyer${preview.flyers === 1 ? '' : 's'}`);

    document.getElementById('unarchive-gig-preview-list').innerHTML = bullets.length === 0
        ? '<li>Nothing else was archived along with this gig.</li>'
        : bullets.map((b) => `<li>${escapeHtml(b)}</li>`).join('');
    document.getElementById('unarchive-gig-add-to-calendar').checked = false;
    document.getElementById('unarchive-gig-modal-backdrop').hidden = false;
}

// "View Archived Gigs" - same sortable/paginated grid pattern as Copy
// Setlist; each row opens the confirm-and-checkbox modal above instead of
// unarchiving directly.
const ARCHIVED_GIGS_PAGE_SIZE = 10;
let archivedGigs = [];
let archivedGigSortKey = 'date';
let archivedGigSortDir = 'desc';
let archivedGigPage = 1;

function closeArchivedGigsModal() { document.getElementById('archived-gigs-modal-backdrop').hidden = true; }
document.getElementById('archived-gigs-modal-close').addEventListener('click', closeArchivedGigsModal);
document.getElementById('archived-gigs-modal-backdrop').addEventListener('click', (e) => { if (e.target.id === 'archived-gigs-modal-backdrop') closeArchivedGigsModal(); });

function archivedGigsSorted() {
    const dir = archivedGigSortDir === 'asc' ? 1 : -1;
    return [...archivedGigs].sort((a, b) => {
        if (archivedGigSortKey === 'venue') return dir * (a.venue || '').localeCompare(b.venue || '');
        if (archivedGigSortKey === 'title') return dir * (a.title || '').localeCompare(b.title || '');
        return dir * (a.sortDate || '').localeCompare(b.sortDate || '');
    });
}

function archivedGigSortArrow(key) {
    if (archivedGigSortKey !== key) return '';
    return archivedGigSortDir === 'asc' ? ' &#9650;' : ' &#9660;';
}

function renderArchivedGigsModal() {
    const body = document.getElementById('archived-gigs-modal-body');
    const all = archivedGigsSorted();
    const totalPages = Math.max(1, Math.ceil(all.length / ARCHIVED_GIGS_PAGE_SIZE));
    archivedGigPage = Math.min(Math.max(1, archivedGigPage), totalPages);
    const pageItems = all.slice((archivedGigPage - 1) * ARCHIVED_GIGS_PAGE_SIZE, archivedGigPage * ARCHIVED_GIGS_PAGE_SIZE);

    body.innerHTML = `
        <h2>Archived Gigs</h2>
        ${all.length === 0 ? '<p class="save-note">No archived gigs.</p>' : `
            <table class="user-table import-gig-table">
                <thead>
                    <tr>
                        <th data-sort-key="title" class="sortable">Name${archivedGigSortArrow('title')}</th>
                        <th data-sort-key="venue" class="sortable">Venue Name${archivedGigSortArrow('venue')}</th>
                        <th data-sort-key="date" class="sortable">Date${archivedGigSortArrow('date')}</th>
                        <th></th>
                    </tr>
                </thead>
                <tbody>
                    ${pageItems.map((g) => `
                        <tr>
                            <td>${escapeHtml(g.title)}</td>
                            <td>${escapeHtml(g.venue || '')}</td>
                            <td>${escapeHtml(g.date || '')}</td>
                            <td>${isBandAdmin ? `<button type="button" class="archived-gig-unarchive-btn" data-gig-ref="${escapeHtml(g.gigRef)}" title="Bring this gig and all of its stuff back!">Unarchive</button>` : ''}</td>
                        </tr>
                    `).join('')}
                </tbody>
            </table>
            <div class="import-gig-pagination">
                <button type="button" id="archived-gigs-page-prev" ${archivedGigPage <= 1 ? 'disabled' : ''}>&laquo; Prev</button>
                <span>Page ${archivedGigPage} of ${totalPages}</span>
                <button type="button" id="archived-gigs-page-next" ${archivedGigPage >= totalPages ? 'disabled' : ''}>Next &raquo;</button>
            </div>
        `}
    `;

    if (all.length > 0) {
        body.querySelectorAll('th[data-sort-key]').forEach((th) => {
            th.addEventListener('click', () => {
                const key = th.dataset.sortKey;
                if (archivedGigSortKey === key) archivedGigSortDir = archivedGigSortDir === 'asc' ? 'desc' : 'asc';
                else { archivedGigSortKey = key; archivedGigSortDir = 'asc'; }
                archivedGigPage = 1;
                renderArchivedGigsModal();
            });
        });
        body.querySelector('#archived-gigs-page-prev').addEventListener('click', () => { archivedGigPage--; renderArchivedGigsModal(); });
        body.querySelector('#archived-gigs-page-next').addEventListener('click', () => { archivedGigPage++; renderArchivedGigsModal(); });
        body.querySelectorAll('.archived-gig-unarchive-btn').forEach((btn) => {
            btn.addEventListener('click', () => openUnarchiveGigModal(btn.dataset.gigRef));
        });
    }
}

document.getElementById('view-archived-gigs-btn').addEventListener('click', async () => {
    document.getElementById('archived-gigs-modal-backdrop').hidden = false;
    document.getElementById('archived-gigs-modal-body').innerHTML = '<p class="save-note">Loading...</p>';
    const res = await fetch('/api/gig-sets/gigs/archived');
    archivedGigs = res.ok ? await res.json() : [];
    archivedGigPage = 1;
    renderArchivedGigsModal();
});

// --- Print setlist ---
function closePrintSetlistModal() { document.getElementById('print-setlist-modal-backdrop').hidden = true; }
document.getElementById('print-setlist-modal-close').addEventListener('click', closePrintSetlistModal);
document.getElementById('print-setlist-modal-backdrop').addEventListener('click', (e) => { if (e.target.id === 'print-setlist-modal-backdrop') closePrintSetlistModal(); });

document.getElementById('gig-set-print-btn').addEventListener('click', () => {
    const gig = gigs.find((g) => g.gigRef === selectedGigRef);
    if (!selectedGigRef || !gig || gig.songCount === 0) { alert('Add some songs to this set first.'); return; }
    const list = document.getElementById('print-setlist-user-list');
    list.innerHTML = '';
    for (const member of bandMembers) {
        const label = document.createElement('label');
        label.className = 'checkbox-label';
        label.innerHTML = `<input type="checkbox" value="${member.id}"> ${escapeHtml(member.firstName)}'s notes`;
        list.appendChild(label);
    }
    document.getElementById('print-setlist-modal-backdrop').hidden = false;
});

document.getElementById('print-setlist-go-btn').addEventListener('click', () => {
    const checked = [...document.querySelectorAll('#print-setlist-user-list input:checked')].map((i) => i.value);
    closePrintSetlistModal();
    const params = new URLSearchParams({ gigRef: selectedGigRef });
    if (checked.length) params.set('notesFrom', checked.join(','));
    window.open(`/print-setlist.html?${params.toString()}`, '_blank');
});

// --- Save as Spotify Playlist (real songs, not a promo post - see
// SpotifyController.CreatePlaylistFromGig's doc comment) ---
document.getElementById('gig-set-spotify-btn').addEventListener('click', async () => {
    const gig = gigs.find((g) => g.gigRef === selectedGigRef);
    if (!selectedGigRef || !gig || gig.songCount === 0) { alert('Add some songs to this set first.'); return; }
    const statusRes = await fetch('/api/spotify/status');
    const { connected } = statusRes.ok ? await statusRes.json() : { connected: false };
    if (!connected) { alert('Spotify isn\'t connected for this band yet - a Band Admin can connect it under Band Admin > Configure Web Presence.'); return; }

    const btn = document.getElementById('gig-set-spotify-btn');
    btn.disabled = true;
    btn.textContent = 'Uploading...';
    const res = await fetch(`/api/spotify/playlists/from-gig/${encodeURIComponent(selectedGigRef)}`, { method: 'POST' });
    const result = await res.json();
    btn.disabled = false;
    btn.textContent = 'Save as Spotify Playlist';

    if (res.ok) {
        const skippedNote = result.skipped > 0 ? ` (${result.skipped} song${result.skipped === 1 ? '' : 's'} skipped - no Spotify link yet)` : '';
        alert(`Created "${result.name}" with ${result.trackCount} song${result.trackCount === 1 ? '' : 's'}${skippedNote}.\n${result.url}`);
    } else {
        alert(result.error || 'Could not create the playlist.');
    }
});

// --- Gig Prep (per-user, per-gig checklist) ---
let gigPrepItems = [];
let gigPrepActiveType = 0;

function closeGigPrepModal() { document.getElementById('gig-prep-modal-backdrop').hidden = true; }
document.getElementById('gig-prep-modal-close').addEventListener('click', closeGigPrepModal);
document.getElementById('gig-prep-modal-backdrop').addEventListener('click', (e) => { if (e.target.id === 'gig-prep-modal-backdrop') closeGigPrepModal(); });

document.getElementById('gig-set-prep-btn').addEventListener('click', async () => {
    if (!selectedGigRef) return;
    document.getElementById('gig-prep-modal-backdrop').hidden = false;
    gigPrepActiveType = 0;
    const res = await fetch(`/api/gig-prep/${encodeURIComponent(selectedGigRef)}`);
    gigPrepItems = res.ok ? await res.json() : [];
    await renderGigPrepTabsAndList();
});

// Async, and always re-fetches /api/gear when the Packing tab is showing -
// no cached gear list, so a gear item added/edited on the Profile page in
// another tab (or removed here) shows up correctly without a reload.
async function renderGigPrepTabsAndList() {
    renderGigPrepTabs(document.getElementById('gig-prep-tabs'), gigPrepActiveType, (type) => {
        gigPrepActiveType = type;
        renderGigPrepTabsAndList();
    });
    renderGigPrepListPanel();

    const gearPanel = document.getElementById('gig-prep-gear-panel');
    gearPanel.hidden = gigPrepActiveType !== 1; // Packing
    if (!gearPanel.hidden) {
        const gearRes = await fetch('/api/gear');
        const gear = gearRes.ok ? await gearRes.json() : [];
        const existingTexts = new Set(gigPrepItems.filter((i) => i.listType === 1).map((i) => i.text));
        renderGigPrepGearPanel(
            document.getElementById('gig-prep-gear-list'),
            gear,
            existingTexts,
            document.getElementById('gig-prep-list'),
            addGigPrepItemFromGear
        );
    }
}

function renderGigPrepListPanel() {
    const items = gigPrepItems.filter((i) => i.listType === gigPrepActiveType);
    renderGigPrepList(document.getElementById('gig-prep-list'), items, {
        showCheckbox: true,
        onToggle: async (id, checked) => {
            await fetch(`/api/gig-prep/${encodeURIComponent(selectedGigRef)}/items/${id}/check`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(checked)
            });
            const item = gigPrepItems.find((i) => i.id === id);
            if (item) item.isChecked = checked;
        },
        onRemove: async (id) => {
            await fetch(`/api/gig-prep/${encodeURIComponent(selectedGigRef)}/items/${id}`, { method: 'DELETE' });
            gigPrepItems = gigPrepItems.filter((i) => i.id !== id);
            await renderGigPrepTabsAndList();
        },
        onReorder: async (ids) => {
            const byId = new Map(gigPrepItems.filter((i) => i.listType === gigPrepActiveType).map((i) => [i.id, i]));
            const others = gigPrepItems.filter((i) => i.listType !== gigPrepActiveType);
            gigPrepItems = [...others, ...ids.map((id) => byId.get(id))];
            renderGigPrepListPanel();
            await fetch(`/api/gig-prep/${encodeURIComponent(selectedGigRef)}/reorder`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ listType: gigPrepActiveType, ids })
            });
        }
    });
}

async function addGigPrepItem(text, saveAsDefault) {
    const res = await fetch(`/api/gig-prep/${encodeURIComponent(selectedGigRef)}/items`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ listType: gigPrepActiveType, text, saveAsDefault: !!saveAsDefault })
    });
    if (res.ok) {
        gigPrepItems.push(await res.json());
        await renderGigPrepTabsAndList();
    }
}

function addGigPrepItemFromGear(gear) {
    addGigPrepItem(gigPrepGearLabel(gear), false);
}

document.getElementById('gig-prep-add-form').addEventListener('submit', async (e) => {
    e.preventDefault();
    const form = e.target;
    const text = form.text.value.trim();
    if (!text) return;
    await addGigPrepItem(text, form.saveAsDefault.checked);
    form.reset();
});

document.getElementById('gig-prep-load-default-btn').addEventListener('click', async () => {
    if (!selectedGigRef) return;
    if (!confirm("This will replace everything currently on this gig's checklist - keep going?")) return;
    const res = await fetch(`/api/gig-prep/${encodeURIComponent(selectedGigRef)}/load-default`, { method: 'POST' });
    if (res.ok) {
        gigPrepItems = await res.json();
        await renderGigPrepTabsAndList();
    } else {
        const result = await res.json().catch(() => ({}));
        alert(result.error || 'Could not load your default checklist.');
    }
});

// --- Copy from another Act or Band: pick any Act from any Band the
// person is a member of, preview its default checklist read-only, then
// commit via the same load-default endpoint LoadDefault uses, just with
// an explicit fromActId instead of this gig's own Act. ---
let gigPrepCopyGrid = null;

document.getElementById('gig-prep-copy-btn').addEventListener('click', async () => {
    document.getElementById('gig-prep-copy-modal-backdrop').hidden = false;
    const res = await fetch('/api/gig-prep/copy-sources');
    const sources = res.ok ? await res.json() : [];

    const columns = [
        { key: 'bandName', label: 'Band' },
        { key: 'actName', label: 'Act' }
    ];
    const container = document.getElementById('gig-prep-copy-grid');
    if (gigPrepCopyGrid) {
        gigPrepCopyGrid.setRows(sources);
    } else {
        gigPrepCopyGrid = window.DataGrid.render(container, {
            columns,
            rows: sources,
            getRowId: (s) => s.actId,
            defaultSortKey: 'bandName',
            emptyMessage: "You're not a member of any band with an Act yet.",
            onRowClick: (s) => openGigPrepCopyPreview(s)
        });
    }
});

function closeGigPrepCopyModal() { document.getElementById('gig-prep-copy-modal-backdrop').hidden = true; }
document.getElementById('gig-prep-copy-close').addEventListener('click', closeGigPrepCopyModal);
document.getElementById('gig-prep-copy-modal-backdrop').addEventListener('click', (e) => { if (e.target.id === 'gig-prep-copy-modal-backdrop') closeGigPrepCopyModal(); });

let gigPrepCopySource = null;
let gigPrepCopyPreviewItems = [];
let gigPrepCopyPreviewActiveType = 0;

async function openGigPrepCopyPreview(source) {
    gigPrepCopySource = source;
    gigPrepCopyPreviewActiveType = 0;
    document.getElementById('gig-prep-copy-preview-title').textContent = `${source.bandName}: ${source.actName}`;
    document.getElementById('gig-prep-copy-preview-use-btn').textContent = `Use this one for ${source.bandName}: ${source.actName}`;
    document.getElementById('gig-prep-copy-preview-modal-backdrop').hidden = false;

    const res = await fetch(`/api/gig-prep/act-default/${source.actId}`);
    gigPrepCopyPreviewItems = res.ok ? await res.json() : [];
    renderGigPrepCopyPreview();
}

function renderGigPrepCopyPreview() {
    renderGigPrepTabs(document.getElementById('gig-prep-copy-preview-tabs'), gigPrepCopyPreviewActiveType, (type) => {
        gigPrepCopyPreviewActiveType = type;
        renderGigPrepCopyPreview();
    });
    const items = gigPrepCopyPreviewItems.filter((i) => i.listType === gigPrepCopyPreviewActiveType);
    renderGigPrepList(document.getElementById('gig-prep-copy-preview-list'), items, { readOnly: true });
}

function closeGigPrepCopyPreviewModal() { document.getElementById('gig-prep-copy-preview-modal-backdrop').hidden = true; }
document.getElementById('gig-prep-copy-preview-close').addEventListener('click', closeGigPrepCopyPreviewModal);
document.getElementById('gig-prep-copy-preview-cancel-btn').addEventListener('click', closeGigPrepCopyPreviewModal);
document.getElementById('gig-prep-copy-preview-modal-backdrop').addEventListener('click', (e) => { if (e.target.id === 'gig-prep-copy-preview-modal-backdrop') closeGigPrepCopyPreviewModal(); });

document.getElementById('gig-prep-copy-preview-use-btn').addEventListener('click', async () => {
    if (!selectedGigRef || !gigPrepCopySource) return;
    const res = await fetch(`/api/gig-prep/${encodeURIComponent(selectedGigRef)}/load-default?fromActId=${gigPrepCopySource.actId}`, { method: 'POST' });
    if (res.ok) {
        gigPrepItems = await res.json();
        closeGigPrepCopyPreviewModal();
        closeGigPrepCopyModal();
        await renderGigPrepTabsAndList();
    } else {
        const result = await res.json().catch(() => ({}));
        alert(result.error || 'Could not copy that checklist.');
    }
});

document.getElementById('gig-prep-print-one-btn').addEventListener('click', () => {
    const params = new URLSearchParams({ gigRef: selectedGigRef, listType: gigPrepActiveType });
    window.open(`/print-gig-prep.html?${params.toString()}`, '_blank');
});
document.getElementById('gig-set-prompter-btn').addEventListener('click', () => {
    if (!selectedGigRef) return;
    window.open(`/prompter.html?gigRef=${encodeURIComponent(selectedGigRef)}`, '_blank');
});

document.getElementById('gig-prep-print-all-btn').addEventListener('click', () => {
    const params = new URLSearchParams({ gigRef: selectedGigRef });
    window.open(`/print-gig-prep.html?${params.toString()}`, '_blank');
});

// --- Load Crew (who sets up/tears down what) - shared with the whole
// band, unlike Gig Prep above which is deliberately private per member.
// Reuses gigPrepShared.js's tab/list/drag-reorder rendering (it's generic
// over the item shape) but with its own 2-tab set and an assignee
// dropdown injected per row via renderGigPrepList's renderExtra hook. ---

const LOAD_CREW_LIST_TYPES = [
    { value: 0, label: 'Load-In' },
    { value: 1, label: 'Load-Out' }
];

function renderLoadCrewTabs(tabsEl, activeType, onSwitch) {
    tabsEl.innerHTML = '';
    for (const t of LOAD_CREW_LIST_TYPES) {
        const btn = document.createElement('button');
        btn.type = 'button';
        btn.className = 'gig-prep-tab' + (t.value === activeType ? ' active' : '');
        btn.textContent = t.label;
        btn.addEventListener('click', () => onSwitch(t.value));
        tabsEl.appendChild(btn);
    }
}

function renderLoadCrewAssignee(item, li, onAssign) {
    const select = document.createElement('select');
    select.className = 'load-crew-assignee-select';
    select.innerHTML = '<option value="">(unassigned)</option>' +
        bandMembers.map((m) => `<option value="${m.id}">${escapeHtml(m.firstName)}</option>`).join('');
    select.value = item.assigneeUserId || '';
    select.addEventListener('change', () => onAssign(item.id, select.value || null));
    li.appendChild(select);
}

let loadCrewItems = [];
let loadCrewActiveType = 0;
let loadCrewGigActId = null;

function closeLoadCrewModal() { document.getElementById('load-crew-modal-backdrop').hidden = true; }
document.getElementById('load-crew-modal-close').addEventListener('click', closeLoadCrewModal);
document.getElementById('load-crew-modal-backdrop').addEventListener('click', (e) => { if (e.target.id === 'load-crew-modal-backdrop') closeLoadCrewModal(); });

document.getElementById('gig-set-load-crew-btn').addEventListener('click', async () => {
    if (!selectedGigRef) return;
    document.getElementById('load-crew-modal-backdrop').hidden = false;
    loadCrewActiveType = 0;

    const gigRes = await fetch(`/api/gigs/${encodeURIComponent(selectedGigRef)}`);
    loadCrewGigActId = gigRes.ok ? (await gigRes.json()).actId : null;
    document.getElementById('load-crew-edit-defaults-btn').hidden = !isBandAdmin || !loadCrewGigActId;

    const res = await fetch(`/api/load-crew/${encodeURIComponent(selectedGigRef)}`);
    loadCrewItems = res.ok ? await res.json() : [];
    renderLoadCrewTabsAndList();
});

function renderLoadCrewTabsAndList() {
    renderLoadCrewTabs(document.getElementById('load-crew-tabs'), loadCrewActiveType, (type) => {
        loadCrewActiveType = type;
        renderLoadCrewTabsAndList();
    });
    const items = loadCrewItems.filter((i) => i.listType === loadCrewActiveType);
    renderGigPrepList(document.getElementById('load-crew-list'), items, {
        showCheckbox: true,
        onToggle: async (id, checked) => {
            await fetch(`/api/load-crew/${encodeURIComponent(selectedGigRef)}/items/${id}/check`, {
                method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(checked)
            });
            const item = loadCrewItems.find((i) => i.id === id);
            if (item) item.isChecked = checked;
        },
        onRemove: async (id) => {
            await fetch(`/api/load-crew/${encodeURIComponent(selectedGigRef)}/items/${id}`, { method: 'DELETE' });
            loadCrewItems = loadCrewItems.filter((i) => i.id !== id);
            renderLoadCrewTabsAndList();
        },
        onReorder: async (ids) => {
            const byId = new Map(loadCrewItems.filter((i) => i.listType === loadCrewActiveType).map((i) => [i.id, i]));
            const others = loadCrewItems.filter((i) => i.listType !== loadCrewActiveType);
            loadCrewItems = [...others, ...ids.map((id) => byId.get(id))];
            renderLoadCrewTabsAndList();
            await fetch(`/api/load-crew/${encodeURIComponent(selectedGigRef)}/reorder`, {
                method: 'PUT', headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ listType: loadCrewActiveType, ids })
            });
        },
        renderExtra: (item, li) => renderLoadCrewAssignee(item, li, async (id, assigneeUserId) => {
            await fetch(`/api/load-crew/${encodeURIComponent(selectedGigRef)}/items/${id}/assignee`, {
                method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(assigneeUserId)
            });
            const found = loadCrewItems.find((i) => i.id === id);
            if (found) found.assigneeUserId = assigneeUserId;
        })
    });
}

document.getElementById('load-crew-add-form').addEventListener('submit', async (e) => {
    e.preventDefault();
    const form = e.target;
    const text = form.text.value.trim();
    if (!text) return;
    const res = await fetch(`/api/load-crew/${encodeURIComponent(selectedGigRef)}/items`, {
        method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ listType: loadCrewActiveType, text })
    });
    if (res.ok) {
        loadCrewItems.push(await res.json());
        form.reset();
        renderLoadCrewTabsAndList();
    }
});

// --- Load Crew default template (per Act, Band Admin only) ---

let loadCrewDefaultItems = [];
let loadCrewDefaultsActiveType = 0;

function closeLoadCrewDefaultsModal() { document.getElementById('load-crew-defaults-modal-backdrop').hidden = true; }
document.getElementById('load-crew-defaults-close').addEventListener('click', closeLoadCrewDefaultsModal);
document.getElementById('load-crew-defaults-modal-backdrop').addEventListener('click', (e) => { if (e.target.id === 'load-crew-defaults-modal-backdrop') closeLoadCrewDefaultsModal(); });

document.getElementById('load-crew-edit-defaults-btn').addEventListener('click', async () => {
    if (!loadCrewGigActId) return;
    document.getElementById('load-crew-defaults-modal-backdrop').hidden = false;
    loadCrewDefaultsActiveType = 0;

    const actRes = await fetch(`/api/acts/${loadCrewGigActId}`);
    const act = actRes.ok ? await actRes.json() : null;
    document.getElementById('load-crew-defaults-title').textContent = act ? `Default checklist - ${act.name}` : 'Default checklist';

    const res = await fetch(`/api/load-crew/defaults/${loadCrewGigActId}`);
    loadCrewDefaultItems = res.ok ? await res.json() : [];
    renderLoadCrewDefaultsTabsAndList();
});

function renderLoadCrewDefaultsTabsAndList() {
    renderLoadCrewTabs(document.getElementById('load-crew-defaults-tabs'), loadCrewDefaultsActiveType, (type) => {
        loadCrewDefaultsActiveType = type;
        renderLoadCrewDefaultsTabsAndList();
    });
    const items = loadCrewDefaultItems.filter((i) => i.listType === loadCrewDefaultsActiveType);
    renderGigPrepList(document.getElementById('load-crew-defaults-list'), items, {
        showCheckbox: false,
        // No reorder endpoint for the default template this pass - it's
        // only ever copied wholesale onto a new gig's checklist, where
        // the per-gig checklist above (which IS reorderable) takes over.
        noDrag: true,
        onRemove: async (id) => {
            await fetch(`/api/load-crew/defaults/${id}`, { method: 'DELETE' });
            loadCrewDefaultItems = loadCrewDefaultItems.filter((i) => i.id !== id);
            renderLoadCrewDefaultsTabsAndList();
        }
    });
}

document.getElementById('load-crew-defaults-add-form').addEventListener('submit', async (e) => {
    e.preventDefault();
    const form = e.target;
    const text = form.text.value.trim();
    if (!text || !loadCrewGigActId) return;
    const res = await fetch(`/api/load-crew/defaults/${loadCrewGigActId}`, {
        method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ listType: loadCrewDefaultsActiveType, text })
    });
    if (res.ok) {
        loadCrewDefaultItems.push(await res.json());
        form.reset();
        renderLoadCrewDefaultsTabsAndList();
    }
});

// --- Accounting (per-gig payout) - viewable by any band member, editable
// by a BandAdmin/SuperAdmin only (AccountingController enforces this
// server-side too; the form/grid here just disables itself so a viewer
// isn't shown controls that would 403 on save). ---

function closeGigAccountingModal() { document.getElementById('gig-accounting-modal-backdrop').hidden = true; }
document.getElementById('gig-accounting-modal-close').addEventListener('click', closeGigAccountingModal);
document.getElementById('gig-accounting-modal-backdrop').addEventListener('click', (e) => { if (e.target.id === 'gig-accounting-modal-backdrop') closeGigAccountingModal(); });

let gigPayoutRecipientsCache = [];

document.getElementById('gig-set-accounting-btn').addEventListener('click', async () => {
    if (!selectedGigRef) return;
    document.getElementById('gig-accounting-modal-backdrop').hidden = false;

    const [me, payout] = await Promise.all([
        fetch('/api/profile/me').then((r) => r.json()),
        fetch(`/api/accounting/gigs/${encodeURIComponent(selectedGigRef)}`).then((r) => (r.ok ? r.json() : null))
    ]);
    if (!payout) { closeGigAccountingModal(); alert('Could not load accounting for this gig.'); return; }

    const isAdmin = !!me.isAdmin;
    document.getElementById('gig-payout-readonly-note').hidden = isAdmin;

    const form = document.getElementById('gig-payout-header-form');
    form.amount.value = payout.amount ?? '';
    form.paidByFirstName.value = payout.paidByFirstName || '';
    form.paidByLastName.value = payout.paidByLastName || '';
    form.paidByOrganization.value = payout.paidByOrganization || '';
    form.paidByEmail.value = payout.paidByEmail || '';
    form.paidByPhone.value = payout.paidByPhone || '';
    form.querySelectorAll('input[name="payoutType"]').forEach((r) => { r.checked = r.value === payout.payoutType; });
    form.querySelectorAll('input, button').forEach((el) => { el.disabled = !isAdmin; });

    gigPayoutRecipientsCache = payout.recipients;
    renderGigPayoutRecipientsGrid(isAdmin);
    document.getElementById('gig-payout-recipients-save-btn').hidden = !isAdmin;
    document.getElementById('gig-payout-header-status').textContent = '';
    document.getElementById('gig-payout-recipients-status').textContent = '';
});

document.getElementById('gig-payout-header-form').addEventListener('submit', async (e) => {
    e.preventDefault();
    const form = e.target;
    const status = document.getElementById('gig-payout-header-status');
    const payoutTypeInput = form.querySelector('input[name="payoutType"]:checked');
    const res = await fetch(`/api/accounting/gigs/${encodeURIComponent(selectedGigRef)}/header`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
            amount: form.amount.value === '' ? null : parseFloat(form.amount.value),
            paidByFirstName: form.paidByFirstName.value.trim() || null,
            paidByLastName: form.paidByLastName.value.trim() || null,
            paidByOrganization: form.paidByOrganization.value.trim() || null,
            paidByEmail: form.paidByEmail.value.trim() || null,
            paidByPhone: form.paidByPhone.value.trim() || null,
            payoutType: payoutTypeInput ? payoutTypeInput.value : null
        })
    });
    const body = await res.json().catch(() => ({}));
    if (!res.ok) { status.textContent = body.error || 'Could not save.'; return; }
    status.textContent = 'Saved.';
    // The recipients grid's Amount column depends on this amount - reload
    // it so the numbers reflect what was just saved.
    const payout = await fetch(`/api/accounting/gigs/${encodeURIComponent(selectedGigRef)}`).then((r) => r.json());
    gigPayoutRecipientsCache = payout.recipients;
    renderGigPayoutRecipientsGrid(true);
});

function renderGigPayoutRecipientsGrid(isAdmin) {
    const container = document.getElementById('gig-payout-recipients-grid');
    const columns = [
        { key: 'firstName', label: 'First name', sortable: false },
        { key: 'lastName', label: 'Last name', sortable: false },
        { key: 'email', label: 'Email', sortable: false },
        { key: 'amount', label: 'Amount', sortable: false, render: (r) => r.amount == null ? '—' : `$${r.amount.toFixed(2)}` },
        {
            key: 'isPaid', label: 'Paid', sortable: false,
            render: (r) => `<input type="checkbox" class="gig-payout-paid-checkbox" data-user-id="${r.userId}" ${r.isPaid ? 'checked' : ''} ${isAdmin ? '' : 'disabled'}>`
        },
        {
            key: 'payoutType', label: 'Paid via', sortable: false,
            render: (r) => `
                <select class="gig-payout-type-select" data-user-id="${r.userId}" ${isAdmin ? '' : 'disabled'}>
                    <option value="">(not set)</option>
                    <option value="Cash" ${r.payoutType === 'Cash' ? 'selected' : ''}>Cash</option>
                    <option value="Check" ${r.payoutType === 'Check' ? 'selected' : ''}>Check</option>
                    <option value="Electronic" ${r.payoutType === 'Electronic' ? 'selected' : ''}>Electronic</option>
                </select>`
        }
    ];
    DataGrid.render(container, {
        columns, rows: gigPayoutRecipientsCache, getRowId: (r) => r.userId,
        searchable: false, pageSize: 1000, emptyMessage: 'No payout recipients configured yet - set them up in Band Admin > Accounting.'
    });
}

document.getElementById('gig-payout-recipients-save-btn').addEventListener('click', async () => {
    const status = document.getElementById('gig-payout-recipients-status');
    const rows = [...document.querySelectorAll('#gig-payout-recipients-grid tbody tr')];
    const recipients = rows.map((row) => {
        const checkbox = row.querySelector('.gig-payout-paid-checkbox');
        const select = row.querySelector('.gig-payout-type-select');
        return { userId: checkbox.dataset.userId, isPaid: checkbox.checked, payoutType: select.value || null };
    });
    const res = await fetch(`/api/accounting/gigs/${encodeURIComponent(selectedGigRef)}/recipients`, {
        method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ recipients })
    });
    const body = await res.json().catch(() => ({}));
    status.textContent = res.ok ? 'Saved - anyone whose paid status changed has been notified.' : (body.error || 'Could not save.');
});

init();
