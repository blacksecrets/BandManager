let gigs = [];
let selectedGigRef = null;
let currentSongs = [];
let repertoireSongs = [];
let bandMembers = [];

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

function parseLength(text) {
    const trimmed = (text || '').trim();
    if (!trimmed) return null;
    const match = trimmed.match(/^(\d+):([0-5]?\d)$/);
    if (!match) return null;
    return parseInt(match[1], 10) * 60 + parseInt(match[2], 10);
}

async function init() {
    const res = await fetch('/api/profile/me');
    const me = await res.json();
    const hasBand = !!me.activeBandRole;

    document.getElementById('gig-sets-no-band').hidden = hasBand;
    document.getElementById('gig-sets-content').hidden = !hasBand;
    if (!hasBand) return;

    await loadGigs();
    await loadRepertoireForPicker();
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

    await loadSet();
    await loadArtifactPanel();
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

// --- The setlist grid itself ---
async function loadSet() {
    const res = await fetch(`/api/gig-sets/${encodeURIComponent(selectedGigRef)}`);
    if (!res.ok) return;
    const data = await res.json();
    currentSongs = data.songs;
    renderSet();
    renderRepertoirePanel();
}

function renderSet() {
    const list = document.getElementById('gig-set-song-list');
    list.innerHTML = '';
    if (currentSongs.length === 0) {
        list.innerHTML = '<p class="save-note">No songs in this set yet - drag from the repertoire, or add manually / from a web search.</p>';
    } else {
        currentSongs.forEach((song, index) => {
            const li = document.createElement('li');
            li.className = 'gig-set-song-row';
            li.dataset.id = song.id;

            const handle = document.createElement('span');
            handle.className = 'drag-handle';
            handle.textContent = '⠿';
            handle.addEventListener('pointerdown', (e) => startReorderDrag(e, li, song));
            li.appendChild(handle);

            const indexSpan = document.createElement('span');
            indexSpan.className = 'song-index';
            indexSpan.textContent = `${index + 1}.`;
            li.appendChild(indexSpan);

            const titleBlock = document.createElement('span');
            titleBlock.className = 'song-title-block';
            titleBlock.innerHTML = `${escapeHtml(song.title)}${song.isManual ? ' <em>(manual)</em>' : ''}<small>${escapeHtml(song.originalArtist || '')}${song.key ? ' · ' + escapeHtml(song.key) : ''}${song.lengthSeconds != null ? ' · ' + formatLength(song.lengthSeconds) : ''}</small>`;
            li.appendChild(titleBlock);

            const removeBtn = document.createElement('button');
            removeBtn.type = 'button';
            removeBtn.className = 'remove-btn';
            removeBtn.textContent = 'Remove';
            removeBtn.addEventListener('click', () => removeSong(song.id));
            li.appendChild(removeBtn);

            list.appendChild(li);
        });
    }

    document.querySelectorAll('.gig-set-total-row').forEach((el) => el.remove());
    const totalSeconds = currentSongs.reduce((sum, s) => sum + (s.lengthSeconds || 0), 0);
    const missing = currentSongs.filter((s) => s.lengthSeconds == null).length;
    const totalRow = document.createElement('div');
    totalRow.className = 'gig-set-total-row';
    const h = Math.floor(totalSeconds / 3600);
    const m = Math.floor((totalSeconds % 3600) / 60);
    const s = totalSeconds % 60;
    const totalText = h > 0 ? `${h}:${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}` : `${m}:${String(s).padStart(2, '0')}`;
    totalRow.innerHTML = `<span>${currentSongs.length} song${currentSongs.length === 1 ? '' : 's'}</span><span>Total: ${totalText}${missing ? ` (${missing} missing length)` : ''}</span>`;
    list.after(totalRow);
}

// Drag-reorder within the setlist grid - manual pointer-events, same
// pattern as repertoire.js's startCatalogRowDrag/dashboard.js's
// startSectionDrag (native HTML5 drag-and-drop was rejected there as
// unreliable for a drag-from-handle gesture; pointer events also get
// touch support for free).
function startReorderDrag(e, li, song) {
    e.preventDefault();
    li.classList.add('dragging');
    const list = document.getElementById('gig-set-song-list');
    let targetLi = null;
    let insertBefore = true;

    function clearIndicators() {
        list.querySelectorAll('.drag-over-top, .drag-over-bottom').forEach((el) => el.classList.remove('drag-over-top', 'drag-over-bottom'));
    }

    function onPointerMove(ev) {
        const el = document.elementFromPoint(ev.clientX, ev.clientY);
        const row = el && el.closest('.gig-set-song-row');
        clearIndicators();
        if (!row || row === li) { targetLi = null; return; }
        const rect = row.getBoundingClientRect();
        insertBefore = ev.clientY < rect.top + rect.height / 2;
        row.classList.add(insertBefore ? 'drag-over-top' : 'drag-over-bottom');
        targetLi = row;
    }

    function cleanup() {
        document.removeEventListener('pointermove', onPointerMove);
        document.removeEventListener('pointerup', onPointerUp);
        document.removeEventListener('pointercancel', onPointerUp);
        li.classList.remove('dragging');
        clearIndicators();
    }

    async function onPointerUp() {
        const target = targetLi;
        const before = insertBefore;
        cleanup();
        if (!target) return;

        const fromIndex = currentSongs.findIndex((s) => s.id === song.id);
        let toIndex = currentSongs.findIndex((s) => s.id === target.dataset.id);
        if (fromIndex === toIndex) return;
        const reordered = [...currentSongs];
        const [moved] = reordered.splice(fromIndex, 1);
        toIndex = reordered.findIndex((s) => s.id === target.dataset.id);
        reordered.splice(before ? toIndex : toIndex + 1, 0, moved);
        currentSongs = reordered;
        renderSet();

        await fetch(`/api/gig-sets/${encodeURIComponent(selectedGigRef)}/reorder`, {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ ids: currentSongs.map((s) => s.id) })
        });
    }

    document.addEventListener('pointermove', onPointerMove);
    document.addEventListener('pointerup', onPointerUp);
    document.addEventListener('pointercancel', onPointerUp);
}

async function removeSong(id) {
    const res = await fetch(`/api/gig-sets/${encodeURIComponent(selectedGigRef)}/songs/${id}`, { method: 'DELETE' });
    if (res.ok) {
        await loadSet();
        await loadGigs();
    }
}

// --- Repertoire panel (drag source) ---
async function loadRepertoireForPicker() {
    const res = await fetch('/api/repertoire');
    if (!res.ok) return;
    repertoireSongs = await res.json();
    renderRepertoirePanel();
}

function renderRepertoirePanel() {
    const box = document.getElementById('gig-set-repertoire-list');
    if (!box) return;
    box.innerHTML = '';
    const inSetIds = new Set(currentSongs.filter((s) => s.songId).map((s) => s.songId));
    const available = repertoireSongs.filter((e) => !inSetIds.has(e.song.id));
    if (available.length === 0) {
        box.innerHTML = '<p class="save-note">Everything in the repertoire is already in this set.</p>';
        return;
    }
    for (const entry of available) {
        const row = document.createElement('div');
        row.className = 'gig-set-repertoire-item';
        row.innerHTML = `
            <span class="drag-handle">⠿</span>
            <span>${escapeHtml(entry.song.title)}<small>${escapeHtml(entry.song.originalArtist || '')}</small></span>
        `;
        row.querySelector('.drag-handle').addEventListener('pointerdown', (e) => startAddFromRepertoireDrag(e, row, entry.song));
        box.appendChild(row);
    }
}

function startAddFromRepertoireDrag(e, row, song) {
    e.preventDefault();
    row.classList.add('dragging');
    const dropZone = document.querySelector('.gig-set-list-panel');
    let over = false;

    function onPointerMove(ev) {
        const el = document.elementFromPoint(ev.clientX, ev.clientY);
        const hit = !!(el && el.closest('.gig-set-list-panel'));
        if (hit !== over) { dropZone.classList.toggle('drag-over', hit); over = hit; }
    }

    function cleanup() {
        document.removeEventListener('pointermove', onPointerMove);
        document.removeEventListener('pointerup', onPointerUp);
        document.removeEventListener('pointercancel', onPointerUp);
        row.classList.remove('dragging');
        dropZone.classList.remove('drag-over');
    }

    async function onPointerUp() {
        const dropped = over;
        cleanup();
        if (!dropped) return;
        const res = await fetch(`/api/gig-sets/${encodeURIComponent(selectedGigRef)}/songs`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ songId: song.id })
        });
        if (res.ok) { await loadSet(); await loadGigs(); }
    }

    document.addEventListener('pointermove', onPointerMove);
    document.addEventListener('pointerup', onPointerUp);
    document.addEventListener('pointercancel', onPointerUp);
}

// --- Add manually (also used by "add from web search", which just
// prefills this same form - see useWebResult below) ---
document.getElementById('gig-set-manual-form').addEventListener('submit', async (e) => {
    e.preventDefault();
    const form = e.target;
    const status = document.getElementById('gig-set-manual-status');
    if (!selectedGigRef) return;

    const lengthSeconds = parseLength(form.length.value);
    if (form.length.value.trim() && lengthSeconds === null) {
        status.textContent = 'Length must be in mm:ss format, like 4:32.';
        return;
    }

    const res = await fetch(`/api/gig-sets/${encodeURIComponent(selectedGigRef)}/manual-songs`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
            title: form.title.value.trim(),
            artist: form.artist.value.trim() || null,
            lengthSeconds,
            youTubeUrl: form.youTubeUrl.value || null,
            spotifyUrl: form.spotifyUrl.value || null
        })
    });
    const body = await res.json();
    if (res.ok) {
        status.textContent = '';
        form.reset();
        await loadSet();
        await loadGigs();
    } else {
        status.textContent = body.error || 'Could not add that song.';
    }
});

// --- Add from web search ---
document.getElementById('gig-set-web-search-form').addEventListener('submit', async (e) => {
    e.preventDefault();
    const query = e.target.q.value.trim();
    if (!query) return;
    const box = document.getElementById('gig-set-web-results');
    box.innerHTML = '<p class="save-note">Searching...</p>';
    const res = await fetch(`/api/songs/search-web?q=${encodeURIComponent(query)}`);
    if (!res.ok) { box.innerHTML = '<p class="save-note">Search failed.</p>'; return; }
    const { youTube, spotify } = await res.json();
    renderWebResults(box, youTube, spotify);
});

function renderWebResults(box, youTube, spotify) {
    box.innerHTML = '';
    const grid = document.createElement('div');
    grid.className = 'song-web-results';

    const buildColumn = (title, items, urlField) => {
        const col = document.createElement('div');
        col.className = 'song-web-column';
        const heading = document.createElement('h4');
        heading.textContent = title;
        col.appendChild(heading);
        if (items.length === 0) {
            const none = document.createElement('p');
            none.className = 'save-note';
            none.textContent = 'No results (or no API key configured).';
            col.appendChild(none);
        }
        for (const item of items) {
            const row = document.createElement('div');
            row.className = 'song-web-result';
            row.innerHTML = `
                ${item.thumbnail ? `<img src="${item.thumbnail}" alt="">` : ''}
                <div class="song-web-result-text">${escapeHtml(item.title)}<small>${escapeHtml(item.artist || '')}</small></div>
            `;
            row.addEventListener('click', () => useWebResult(item, urlField));
            col.appendChild(row);
        }
        return col;
    };

    grid.appendChild(buildColumn('YouTube', youTube, 'youTubeUrl'));
    grid.appendChild(buildColumn('Spotify', spotify, 'spotifyUrl'));
    box.appendChild(grid);
}

function useWebResult(item, urlField) {
    const details = document.getElementById('gig-set-manual-form').closest('details');
    details.open = true;
    const form = document.getElementById('gig-set-manual-form');
    form.title.value = item.title;
    if (item.artist) form.artist.value = item.artist;
    form[urlField].value = item.url;
    details.scrollIntoView({ behavior: 'smooth', block: 'center' });
}

// --- Copy Setlist from Another Gig ---
// Grid of every OTHER gig (past or future - a setlist is just as likely
// to come from an upcoming gig, e.g. same tour/week, as from a past
// one), sortable/paginated same as Catalog's Details view. Clicking a
// row previews that gig's setlist below, in its own grid; Confirm
// actually copies (same POST /import-from this always used) and closes
// the modal, Nope just clears the preview so another row can be tried,
// Cancel closes with no action taken.
const IMPORT_GIG_PAGE_SIZE = 10;
let importGigSortKey = 'date';
let importGigSortDir = 'asc';
let importGigPage = 1;
let importGigPreviewRef = null;

function closeImportGigModal() { document.getElementById('import-gig-modal-backdrop').hidden = true; }
document.getElementById('import-gig-modal-close').addEventListener('click', closeImportGigModal);
document.getElementById('import-gig-modal-backdrop').addEventListener('click', (e) => { if (e.target.id === 'import-gig-modal-backdrop') closeImportGigModal(); });

function importGigCandidates() {
    const dir = importGigSortDir === 'asc' ? 1 : -1;
    return gigs.filter((g) => g.gigRef !== selectedGigRef).sort((a, b) => {
        if (importGigSortKey === 'venue') return dir * (a.venue || '').localeCompare(b.venue || '');
        if (importGigSortKey === 'title') return dir * (a.title || '').localeCompare(b.title || '');
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
        <h2>Copy Setlist from Another Gig</h2>
        ${all.length === 0 ? '<p class="save-note">There are no other gigs yet.</p>' : `
            <table class="user-table import-gig-table">
                <thead>
                    <tr>
                        <th data-sort-key="title" class="sortable">Name${importGigSortArrow('title')}</th>
                        <th data-sort-key="venue" class="sortable">Venue Name${importGigSortArrow('venue')}</th>
                        <th data-sort-key="date" class="sortable">Date${importGigSortArrow('date')}</th>
                    </tr>
                </thead>
                <tbody>
                    ${pageItems.map((g) => `
                        <tr class="import-gig-row${g.gigRef === importGigPreviewRef ? ' selected' : ''}" data-gig-ref="${escapeHtml(g.gigRef)}">
                            <td>${escapeHtml(g.title)}</td>
                            <td>${escapeHtml(g.venue || '')}</td>
                            <td>${escapeHtml(g.date || '')}</td>
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
                else { importGigSortKey = key; importGigSortDir = 'asc'; }
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

    const gig = gigs.find((g) => g.gigRef === importGigPreviewRef);
    box.innerHTML = '<p class="save-note">Loading setlist...</p>';
    const res = await fetch(`/api/gig-sets/${encodeURIComponent(importGigPreviewRef)}`);
    const data = res.ok ? await res.json() : { songs: [] };
    const songs = data.songs || [];

    box.innerHTML = `
        <h3>${escapeHtml(gig ? gig.title : 'Setlist')}</h3>
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
        ${songs.length === 0 ? '<p class="save-note">This gig has no setlist.</p>' : ''}
        <div class="cred-form-buttons">
            <button type="button" id="import-gig-nope-btn">Nope</button>
            <button type="button" id="import-gig-confirm-btn" ${songs.length === 0 ? 'disabled' : ''}>Confirm</button>
        </div>
    `;

    box.querySelector('#import-gig-nope-btn').addEventListener('click', () => {
        importGigPreviewRef = null;
        renderImportGigModal();
    });
    box.querySelector('#import-gig-confirm-btn').addEventListener('click', async () => {
        const res2 = await fetch(`/api/gig-sets/${encodeURIComponent(selectedGigRef)}/import-from/${encodeURIComponent(importGigPreviewRef)}`, { method: 'POST' });
        const resBody = await res2.json();
        if (res2.ok) {
            closeImportGigModal();
            await loadSet();
            await loadGigs();
        } else {
            alert(resBody.error || 'Could not copy that setlist.');
        }
    });
}

document.getElementById('gig-set-import-btn').addEventListener('click', () => {
    importGigSortKey = 'date';
    importGigSortDir = 'asc';
    importGigPage = 1;
    importGigPreviewRef = null;
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

// --- Print setlist ---
function closePrintSetlistModal() { document.getElementById('print-setlist-modal-backdrop').hidden = true; }
document.getElementById('print-setlist-modal-close').addEventListener('click', closePrintSetlistModal);
document.getElementById('print-setlist-modal-backdrop').addEventListener('click', (e) => { if (e.target.id === 'print-setlist-modal-backdrop') closePrintSetlistModal(); });

document.getElementById('gig-set-print-btn').addEventListener('click', () => {
    if (!selectedGigRef || currentSongs.length === 0) { alert('Add some songs to this set first.'); return; }
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
    if (!selectedGigRef || currentSongs.length === 0) { alert('Add some songs to this set first.'); return; }
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

async function addGigPrepItem(text) {
    const res = await fetch(`/api/gig-prep/${encodeURIComponent(selectedGigRef)}/items`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ listType: gigPrepActiveType, text })
    });
    if (res.ok) {
        gigPrepItems.push(await res.json());
        await renderGigPrepTabsAndList();
    }
}

function addGigPrepItemFromGear(gear) {
    addGigPrepItem(gigPrepGearLabel(gear));
}

document.getElementById('gig-prep-add-form').addEventListener('submit', async (e) => {
    e.preventDefault();
    const form = e.target;
    const text = form.text.value.trim();
    if (!text) return;
    await addGigPrepItem(text);
    form.reset();
});

document.getElementById('gig-prep-print-one-btn').addEventListener('click', () => {
    const params = new URLSearchParams({ gigRef: selectedGigRef, listType: gigPrepActiveType });
    window.open(`/print-gig-prep.html?${params.toString()}`, '_blank');
});
document.getElementById('gig-prep-print-all-btn').addEventListener('click', () => {
    const params = new URLSearchParams({ gigRef: selectedGigRef });
    window.open(`/print-gig-prep.html?${params.toString()}`, '_blank');
});

init();
