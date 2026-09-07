let instruments = [];
let repertoire = [];
let catalog = [];
let isAdmin = false;
let isSuperAdmin = false;
let pendingWebResult = null; // {title, artist, url, source} - last-clicked web search hit, prefilled into the new-song form
let myNotes = {}; // repertoireEntryId -> text, this user's own notes (see SongNotesController)

function escapeHtml(str) {
    const div = document.createElement('div');
    div.textContent = str ?? '';
    return div.innerHTML;
}

function formatLength(seconds) {
    if (!seconds && seconds !== 0) return '';
    const m = Math.floor(seconds / 60);
    const s = seconds % 60;
    return `${m}:${String(s).padStart(2, '0')}`;
}

function parseLength(text) {
    const trimmed = (text || '').trim();
    if (!trimmed) return null;
    const match = trimmed.match(/^(\d{1,2}):(\d{2})$/);
    if (!match) return null;
    return parseInt(match[1], 10) * 60 + parseInt(match[2], 10);
}

async function init() {
    const res = await fetch('/api/profile/me');
    const me = await res.json();
    const hasBand = !!me.activeBandRole;
    isAdmin = !!me.isAdmin;
    isSuperAdmin = !!me.isSuperAdmin;

    document.getElementById('repertoire-no-band').hidden = hasBand;
    document.getElementById('repertoire-content').hidden = !hasBand;
    if (!hasBand) return;

    if (!isAdmin) {
        document.getElementById('repertoire-instruments-section').querySelector('form').hidden = true;
        document.querySelector('.repertoire-main section:nth-of-type(2)').hidden = true; // "Add a song"
    }

    await loadInstruments();
    await loadRepertoire();
    await loadCatalog();
}

// --- Instruments ---
async function loadInstruments() {
    const res = await fetch('/api/repertoire/instruments');
    if (!res.ok) return;
    instruments = await res.json();
    document.getElementById('instrument-count').textContent = instruments.length;

    const list = document.getElementById('instrument-list');
    list.innerHTML = '';
    for (const inst of instruments) {
        const chip = document.createElement('span');
        chip.className = 'instrument-chip';
        chip.innerHTML = `${escapeHtml(inst.name)} `;
        if (isAdmin) {
            const removeBtn = document.createElement('button');
            removeBtn.type = 'button';
            removeBtn.textContent = '×';
            removeBtn.title = `Remove ${inst.name}`;
            removeBtn.addEventListener('click', () => removeInstrument(inst.id, inst.name));
            chip.appendChild(removeBtn);
        }
        list.appendChild(chip);
    }
    renderTableHeader();
}

async function removeInstrument(id, name) {
    if (!confirm(`Stop tracking tuning for "${name}"? Any tunings already saved for it stay on the songs, just hidden here.`)) return;
    const res = await fetch(`/api/repertoire/instruments/${id}`, { method: 'DELETE' });
    if (res.ok) { await loadInstruments(); renderRepertoireBody(); }
}

const addInstrumentForm = document.getElementById('add-instrument-form');
if (addInstrumentForm) {
    addInstrumentForm.addEventListener('submit', async (e) => {
        e.preventDefault();
        const form = e.target;
        const status = document.getElementById('add-instrument-status');
        const res = await fetch('/api/repertoire/instruments', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ name: form.name.value.trim() })
        });
        const body = await res.json();
        if (res.ok) {
            status.textContent = '';
            form.reset();
            await loadInstruments();
            renderRepertoireBody();
        } else {
            status.textContent = body.error || 'Could not add instrument.';
        }
    });
}

// --- Repertoire table ---
function renderTableHeader() {
    const row = document.getElementById('repertoire-head-row');
    row.innerHTML = '';
    const labels = ['Title', 'Original Artist', ...instruments.map((i) => i.name), 'Key', 'Length', 'Links', 'Status', 'My Note', ''];
    for (const label of labels) {
        const th = document.createElement('th');
        th.textContent = label;
        row.appendChild(th);
    }
}

async function loadRepertoire() {
    const [repRes, notesRes] = await Promise.all([
        fetch('/api/repertoire'),
        fetch('/api/song-notes')
    ]);
    if (!repRes.ok) return;
    repertoire = await repRes.json();
    myNotes = {};
    if (notesRes.ok) {
        for (const n of await notesRes.json()) myNotes[n.repertoireEntryId] = n.text;
    }
    renderRepertoireBody();
}

function renderRepertoireBody() {
    const tbody = document.getElementById('repertoire-body');
    tbody.innerHTML = '';
    for (const entry of repertoire) {
        tbody.appendChild(renderRepertoireRow(entry));
    }
    if (repertoire.length === 0) {
        const tr = document.createElement('tr');
        const td = document.createElement('td');
        td.colSpan = 7 + instruments.length;
        td.textContent = 'Nothing in the repertoire yet - add a song above to get started.';
        tr.appendChild(td);
        tbody.appendChild(tr);
    }
}

function renderRepertoireRow(entry) {
    const song = entry.song;
    const tr = document.createElement('tr');

    const titleTd = document.createElement('td');
    titleTd.textContent = song.title;
    tr.appendChild(titleTd);

    const artistTd = document.createElement('td');
    artistTd.textContent = song.originalArtist || '—';
    tr.appendChild(artistTd);

    for (const inst of instruments) {
        const td = document.createElement('td');
        if (isAdmin) {
            const input = document.createElement('input');
            input.className = 'tuning-input';
            input.type = 'text';
            input.value = song.tunings[inst.name] || '';
            input.placeholder = '—';
            input.addEventListener('change', async () => {
                await fetch(`/api/songs/${song.id}/tuning`, {
                    method: 'PUT',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ instrument: inst.name, tuning: input.value.trim() })
                });
            });
            td.appendChild(input);
        } else {
            td.textContent = song.tunings[inst.name] || '—';
        }
        tr.appendChild(td);
    }

    const keyTd = document.createElement('td');
    keyTd.textContent = song.key || '—';
    tr.appendChild(keyTd);

    const lengthTd = document.createElement('td');
    lengthTd.textContent = song.lengthSeconds != null ? formatLength(song.lengthSeconds) : '—';
    tr.appendChild(lengthTd);

    const linksTd = document.createElement('td');
    const linksBox = document.createElement('div');
    linksBox.className = 'song-links';
    const linkDefs = [['YouTube', song.youTubeUrl], ['Spotify', song.spotifyUrl], ['Songsterr', song.songsterrUrl]];
    for (const [label, url] of linkDefs) {
        if (url) {
            const a = document.createElement('a');
            a.href = url;
            a.target = '_blank';
            a.rel = 'noopener';
            a.textContent = label;
            linksBox.appendChild(a);
        } else {
            const span = document.createElement('span');
            span.className = 'link-missing';
            span.textContent = label;
            linksBox.appendChild(span);
        }
    }
    linksTd.appendChild(linksBox);
    tr.appendChild(linksTd);

    const statusTd = document.createElement('td');
    if (isAdmin) {
        const select = document.createElement('select');
        select.className = 'status-select';
        select.innerHTML = `<option value="New">New</option><option value="InProgress">In Progress</option><option value="Ready">Ready</option>`;
        select.value = entry.status;
        select.addEventListener('change', async () => {
            await fetch(`/api/repertoire/${entry.id}`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ status: select.value })
            });
            entry.status = select.value;
        });
        statusTd.appendChild(select);
    } else {
        statusTd.textContent = entry.status === 'InProgress' ? 'In Progress' : entry.status;
    }
    tr.appendChild(statusTd);

    const noteTd = document.createElement('td');
    const noteInput = document.createElement('input');
    noteInput.type = 'text';
    noteInput.className = 'song-note-input';
    noteInput.placeholder = 'e.g. capo 3, watch the key change...';
    noteInput.value = myNotes[entry.id] || '';
    noteInput.title = 'Only you see this note (until you choose to print it on a setlist)';
    let noteSaveTimer = null;
    noteInput.addEventListener('input', () => {
        clearTimeout(noteSaveTimer);
        noteSaveTimer = setTimeout(async () => {
            const text = noteInput.value.trim();
            await fetch(`/api/song-notes/${entry.id}`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ text })
            });
            myNotes[entry.id] = text;
        }, 500);
    });
    noteTd.appendChild(noteInput);
    tr.appendChild(noteTd);

    const actionsTd = document.createElement('td');
    if (isAdmin) {
        const removeBtn = document.createElement('button');
        removeBtn.className = 'remove-btn';
        removeBtn.textContent = 'Remove';
        removeBtn.addEventListener('click', async () => {
            if (!confirm(`Remove "${song.title}" from the repertoire?`)) return;
            const res = await fetch(`/api/repertoire/${entry.id}`, { method: 'DELETE' });
            if (res.ok) loadRepertoire();
        });
        actionsTd.appendChild(removeBtn);
    }
    tr.appendChild(actionsTd);

    return tr;
}

// --- Add a song: search the shared catalog ---
let dbSearchTimeout = null;
const dbSearchInput = document.getElementById('song-db-search');
if (dbSearchInput) {
    dbSearchInput.addEventListener('input', () => {
        clearTimeout(dbSearchTimeout);
        dbSearchTimeout = setTimeout(() => runDbSearch(dbSearchInput.value.trim()), 300);
    });
}

async function runDbSearch(query) {
    const box = document.getElementById('song-db-results');
    if (!query) { box.innerHTML = ''; return; }
    const res = await fetch(`/api/songs/search?q=${encodeURIComponent(query)}`);
    if (!res.ok) return;
    const results = await res.json();
    const inRepertoireIds = new Set(repertoire.map((e) => e.song.id));

    box.innerHTML = '';
    if (results.length === 0) {
        box.innerHTML = '<p class="save-note">No matches in the shared catalog - try the web search below, or add it as a new song.</p>';
        return;
    }
    for (const song of results) {
        const row = document.createElement('div');
        row.className = 'song-result-row';
        const already = inRepertoireIds.has(song.id);
        row.innerHTML = `<div class="song-result-info">${escapeHtml(song.title)}<small>${escapeHtml(song.originalArtist || '')}</small></div>`;
        const btn = document.createElement('button');
        btn.type = 'button';
        btn.textContent = already ? 'Already added' : 'Add to repertoire';
        btn.disabled = already;
        btn.addEventListener('click', async () => {
            const res = await fetch('/api/repertoire', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ songId: song.id })
            });
            if (res.ok) {
                box.innerHTML = '';
                dbSearchInput.value = '';
                await loadRepertoire();
            } else {
                const body = await res.json().catch(() => ({}));
                alert(body.error || 'Could not add that song.');
            }
        });
        row.appendChild(btn);
        box.appendChild(row);
    }
}

// --- Add a song: web search (YouTube + Spotify) ---
const webSearchForm = document.getElementById('song-web-search-form');
if (webSearchForm) {
    webSearchForm.addEventListener('submit', async (e) => {
        e.preventDefault();
        const query = webSearchForm.query.value.trim();
        if (!query) return;

        document.getElementById('songsterr-search-link').href = `https://www.songsterr.com/a/wa/search?pattern=${encodeURIComponent(query)}`;

        const box = document.getElementById('song-web-results');
        box.innerHTML = '<p class="save-note">Searching...</p>';
        const res = await fetch(`/api/songs/search-web?q=${encodeURIComponent(query)}`);
        if (!res.ok) { box.innerHTML = '<p class="save-note">Search failed.</p>'; return; }
        const { youTube, spotify } = await res.json();
        renderWebResults(box, youTube, spotify);
    });
}

function renderWebResults(box, youTube, spotify) {
    box.innerHTML = '';
    const grid = document.createElement('div');
    grid.className = 'song-web-results';

    const buildColumn = (title, items, sourceLabel) => {
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
            row.addEventListener('click', () => useWebResult(item, sourceLabel));
            col.appendChild(row);
        }
        return col;
    };

    grid.appendChild(buildColumn('YouTube', youTube, 'youTubeUrl'));
    grid.appendChild(buildColumn('Spotify', spotify, 'spotifyUrl'));
    box.appendChild(grid);
}

function useWebResult(item, urlField) {
    const details = document.getElementById('new-song-details');
    details.open = true;
    const form = document.getElementById('new-song-form');
    if (!form.title.value) form.title.value = item.title;
    if (!form.originalArtist.value && item.artist) form.originalArtist.value = item.artist;
    if (!form.album.value && item.album) form.album.value = item.album;
    form[urlField].value = item.url;
    details.scrollIntoView({ behavior: 'smooth', block: 'center' });
}

// --- Add a song: create new ---
const newSongForm = document.getElementById('new-song-form');
if (newSongForm) {
    newSongForm.addEventListener('submit', async (e) => {
        e.preventDefault();
        const form = e.target;
        const status = document.getElementById('new-song-status');
        const lengthSeconds = parseLength(form.length.value);
        if (form.length.value.trim() && lengthSeconds === null) {
            status.textContent = 'Length must be in mm:ss format, like 4:32.';
            return;
        }

        const createRes = await fetch('/api/songs', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                title: form.title.value.trim(),
                originalArtist: form.originalArtist.value.trim() || null,
                album: form.album.value.trim() || null,
                key: form.key.value.trim() || null,
                lengthSeconds,
                youTubeUrl: form.youTubeUrl.value.trim() || null,
                spotifyUrl: form.spotifyUrl.value.trim() || null,
                songsterrUrl: form.songsterrUrl.value.trim() || null
            })
        });
        const created = await createRes.json();
        if (!createRes.ok) { status.textContent = created.error || 'Could not create song.'; return; }

        const addRes = await fetch('/api/repertoire', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ songId: created.id })
        });
        const addBody = await addRes.json();
        if (!addRes.ok) { status.textContent = addBody.error || 'Song created, but could not add it to the repertoire.'; return; }

        status.textContent = '';
        form.reset();
        document.getElementById('new-song-details').open = false;
        document.getElementById('song-web-results').innerHTML = '';
        await loadRepertoire();
        await loadCatalog();
    });
}

// --- Full Song Catalog ---
async function loadCatalog() {
    const res = await fetch('/api/songs');
    if (!res.ok) return;
    catalog = await res.json();
    renderCatalogBody();
}

function renderCatalogBody() {
    const tbody = document.getElementById('catalog-body');
    tbody.innerHTML = '';
    for (const song of catalog) {
        tbody.appendChild(renderCatalogRow(song));
        tbody.appendChild(renderCatalogEditRow(song));
    }
    if (catalog.length === 0) {
        const tr = document.createElement('tr');
        const td = document.createElement('td');
        td.colSpan = 8;
        td.textContent = 'The catalog is empty.';
        tr.appendChild(td);
        tbody.appendChild(tr);
    }
}

function renderCatalogRow(song) {
    const tr = document.createElement('tr');
    tr.dataset.songId = song.id;

    const handleTd = document.createElement('td');
    if (isAdmin) {
        const handle = document.createElement('span');
        handle.className = 'drag-handle';
        handle.textContent = '⠿';
        handle.addEventListener('pointerdown', (e) => startCatalogRowDrag(e, tr, song));
        handleTd.appendChild(handle);
    }
    tr.appendChild(handleTd);

    const titleTd = document.createElement('td');
    titleTd.textContent = song.title;
    if (song.pendingEditRequestId) {
        const badge = document.createElement('button');
        badge.type = 'button';
        badge.className = 'under-review-badge';
        badge.textContent = 'Under review';
        badge.addEventListener('click', () => {
            window.openReviewSummary({ songId: song.id, requestId: song.pendingEditRequestId, isSuperAdmin, onResolved: loadCatalog });
        });
        titleTd.appendChild(document.createElement('br'));
        titleTd.appendChild(badge);
    }
    tr.appendChild(titleTd);

    const artistTd = document.createElement('td');
    artistTd.textContent = song.originalArtist || '—';
    tr.appendChild(artistTd);

    const albumTd = document.createElement('td');
    albumTd.textContent = song.album || '—';
    tr.appendChild(albumTd);

    const keyTd = document.createElement('td');
    keyTd.textContent = song.key || '—';
    tr.appendChild(keyTd);

    const lengthTd = document.createElement('td');
    lengthTd.textContent = song.lengthSeconds != null ? formatLength(song.lengthSeconds) : '—';
    tr.appendChild(lengthTd);

    const linksTd = document.createElement('td');
    const linksBox = document.createElement('div');
    linksBox.className = 'song-links';
    for (const [label, url] of [['YouTube', song.youTubeUrl], ['Spotify', song.spotifyUrl], ['Songsterr', song.songsterrUrl]]) {
        if (url) {
            const a = document.createElement('a');
            a.href = url; a.target = '_blank'; a.rel = 'noopener'; a.textContent = label;
            linksBox.appendChild(a);
        } else {
            const span = document.createElement('span');
            span.className = 'link-missing';
            span.textContent = label;
            linksBox.appendChild(span);
        }
    }
    linksTd.appendChild(linksBox);
    tr.appendChild(linksTd);

    const actionsTd = document.createElement('td');
    const editBtn = document.createElement('button');
    editBtn.type = 'button';
    editBtn.textContent = 'Edit';
    editBtn.disabled = !!song.pendingEditRequestId;
    editBtn.title = song.pendingEditRequestId ? 'This song already has an edit under review.' : '';
    editBtn.addEventListener('click', () => {
        const editRow = tr.nextElementSibling;
        const opening = editRow.hidden;
        editRow.hidden = !opening;
        editBtn.textContent = opening ? 'Close' : 'Edit';
    });
    actionsTd.appendChild(editBtn);
    tr.appendChild(actionsTd);

    return tr;
}

function renderCatalogEditRow(song) {
    const tr = document.createElement('tr');
    tr.className = 'catalog-edit-row';
    tr.hidden = true;
    const td = document.createElement('td');
    td.colSpan = 8;

    const form = document.createElement('form');
    form.className = 'cred-form';
    form.innerHTML = `
        <label>Title <input type="text" name="title" value="${escapeHtml(song.title)}" required></label>
        <label>Original artist <input type="text" name="originalArtist" value="${escapeHtml(song.originalArtist || '')}"></label>
        <label>Album <input type="text" name="album" value="${escapeHtml(song.album || '')}"></label>
        <label>Key <input type="text" name="key" value="${escapeHtml(song.key || '')}"></label>
        <label>Length (mm:ss) <input type="text" name="length" value="${song.lengthSeconds != null ? formatLength(song.lengthSeconds) : ''}" pattern="^\\d{1,2}:\\d{2}$"></label>
        <label>YouTube URL <input type="url" name="youTubeUrl" value="${escapeHtml(song.youTubeUrl || '')}"></label>
        <label>Spotify URL <input type="url" name="spotifyUrl" value="${escapeHtml(song.spotifyUrl || '')}"></label>
        <label>Songsterr URL <input type="url" name="songsterrUrl" value="${escapeHtml(song.songsterrUrl || '')}"></label>
        <button type="submit">${isSuperAdmin ? 'Save' : 'Submit for review'}</button>
        <p class="save-note"></p>
    `;
    form.addEventListener('submit', async (e) => {
        e.preventDefault();
        const status = form.querySelector('.save-note');
        const lengthSeconds = parseLength(form.length.value);
        if (form.length.value.trim() && lengthSeconds === null) {
            status.textContent = 'Length must be in mm:ss format, like 4:32.';
            return;
        }
        const payload = {
            title: form.title.value.trim(),
            originalArtist: form.originalArtist.value.trim() || null,
            album: form.album.value.trim() || null,
            key: form.key.value.trim() || null,
            lengthSeconds,
            youTubeUrl: form.youTubeUrl.value.trim() || null,
            spotifyUrl: form.spotifyUrl.value.trim() || null,
            songsterrUrl: form.songsterrUrl.value.trim() || null
        };
        const url = isSuperAdmin ? `/api/songs/${song.id}` : `/api/songs/${song.id}/propose-edit`;
        const res = await fetch(url, {
            method: isSuperAdmin ? 'PUT' : 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(payload)
        });
        const body = await res.json();
        if (!res.ok) { status.textContent = body.error || 'Could not save.'; return; }
        status.textContent = isSuperAdmin ? 'Saved.' : 'Submitted for review.';
        await loadCatalog();
        await loadRepertoire();
    });
    td.appendChild(form);
    tr.appendChild(td);
    return tr;
}

// Manual pointer-events drag, matching dashboard.js's startSectionDrag -
// native HTML5 drag-and-drop was explicitly rejected there as unreliable
// for a drag-from-handle gesture, and pointer events get touch support for
// free, which matters here since band members use this on phones.
function startCatalogRowDrag(e, tr, song) {
    e.preventDefault();
    tr.classList.add('dragging');
    const dropZone = document.querySelector('.repertoire-table-wrap');
    let over = false;

    function onPointerMove(ev) {
        const el = document.elementFromPoint(ev.clientX, ev.clientY);
        const hit = !!(el && el.closest('.repertoire-table-wrap'));
        if (hit !== over) {
            dropZone.classList.toggle('drag-over', hit);
            over = hit;
        }
    }

    function cleanup() {
        document.removeEventListener('pointermove', onPointerMove);
        document.removeEventListener('pointerup', onPointerUp);
        document.removeEventListener('pointercancel', onPointerUp);
        tr.classList.remove('dragging');
        dropZone.classList.remove('drag-over');
    }

    async function onPointerUp() {
        const dropped = over;
        cleanup();
        if (!dropped) return;
        if (repertoire.some((en) => en.song.id === song.id)) return; // already in repertoire
        const res = await fetch('/api/repertoire', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ songId: song.id })
        });
        if (res.ok) await loadRepertoire();
    }

    document.addEventListener('pointermove', onPointerMove);
    document.addEventListener('pointerup', onPointerUp);
    document.addEventListener('pointercancel', onPointerUp);
}

init();
