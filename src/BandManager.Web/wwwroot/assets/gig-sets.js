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
}

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
           ${data.flyer.flyerTemplateName ? `<p class="save-note">Built from template: ${escapeHtml(data.flyer.flyerTemplateName)}</p>` : '<p class="save-note">Not built from a saved template.</p>'}</div>`
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

// --- Import from Past Gig ---
function closeImportGigModal() { document.getElementById('import-gig-modal-backdrop').hidden = true; }
document.getElementById('import-gig-modal-close').addEventListener('click', closeImportGigModal);
document.getElementById('import-gig-modal-backdrop').addEventListener('click', (e) => { if (e.target.id === 'import-gig-modal-backdrop') closeImportGigModal(); });

document.getElementById('gig-set-import-btn').addEventListener('click', () => {
    const body = document.getElementById('import-gig-modal-body');
    const past = gigs.filter((g) => g.isPast && g.gigRef !== selectedGigRef);
    body.innerHTML = '<h2>Import from Past Gig</h2>';
    if (past.length === 0) {
        body.innerHTML += '<p class="save-note">No past gigs to import from.</p>';
    } else {
        const list = document.createElement('div');
        for (const gig of past) {
            const row = document.createElement('div');
            row.className = 'import-gig-row';
            row.innerHTML = `<span>${escapeHtml(gig.title)} - ${escapeHtml(gig.date || '')} (${gig.songCount} song${gig.songCount === 1 ? '' : 's'})</span>`;
            const viewBtn = document.createElement('button');
            viewBtn.type = 'button';
            viewBtn.textContent = 'View';
            const details = document.createElement('div');
            details.className = 'import-gig-details';
            details.hidden = true;
            viewBtn.addEventListener('click', async () => {
                const opening = details.hidden;
                details.hidden = !opening;
                if (opening && !details.dataset.loaded) {
                    const res = await fetch(`/api/gig-sets/${encodeURIComponent(gig.gigRef)}/items`);
                    details.innerHTML = res.ok ? renderArtifactPanelHtml(await res.json()) : '<p class="save-note">Could not load.</p>';
                    details.dataset.loaded = '1';
                }
            });
            const importBtn = document.createElement('button');
            importBtn.type = 'button';
            importBtn.textContent = 'Import this set';
            importBtn.disabled = gig.songCount === 0;
            importBtn.addEventListener('click', async () => {
                const res = await fetch(`/api/gig-sets/${encodeURIComponent(selectedGigRef)}/import-from/${encodeURIComponent(gig.gigRef)}`, { method: 'POST' });
                const resBody = await res.json();
                if (res.ok) {
                    closeImportGigModal();
                    await loadSet();
                    await loadGigs();
                } else {
                    alert(resBody.error || 'Could not import that set.');
                }
            });
            row.appendChild(viewBtn);
            row.appendChild(importBtn);
            row.appendChild(details);
            list.appendChild(row);
        }
        body.appendChild(list);
    }
    document.getElementById('import-gig-modal-backdrop').hidden = false;
});

// --- Create/Edit Flyer (pick a template, then hand off to the shared Flyer Editor) ---
function closeTemplatePickerModal() { document.getElementById('template-picker-modal-backdrop').hidden = true; }
document.getElementById('template-picker-modal-close').addEventListener('click', closeTemplatePickerModal);
document.getElementById('template-picker-modal-backdrop').addEventListener('click', (e) => { if (e.target.id === 'template-picker-modal-backdrop') closeTemplatePickerModal(); });

document.getElementById('gig-set-flyer-btn').addEventListener('click', async () => {
    const body = document.getElementById('template-picker-modal-body');
    body.innerHTML = '<h2>Choose a Flyer Template</h2><p class="save-note">Loading...</p>';
    document.getElementById('template-picker-modal-backdrop').hidden = false;

    const res = await fetch('/api/flyer-templates');
    if (!res.ok) { body.innerHTML = '<h2>Choose a Flyer Template</h2><p class="save-note">Could not load templates.</p>'; return; }
    const templates = await res.json();
    body.innerHTML = '<h2>Choose a Flyer Template</h2>';
    if (templates.length === 0) {
        body.innerHTML += '<p class="save-note">No Flyer Templates yet - create one from the Catalog\'s Flyers section first.</p>';
        return;
    }
    for (const t of templates) {
        const btn = document.createElement('button');
        btn.type = 'button';
        btn.className = 'template-picker-row';
        btn.textContent = t.name;
        btn.addEventListener('click', async () => {
            closeTemplatePickerModal();
            const result = await window.openFlyerEditor({ templateId: t.id, gigRef: selectedGigRef });
            if (result) await loadArtifactPanel();
        });
        body.appendChild(btn);
    }
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

init();
