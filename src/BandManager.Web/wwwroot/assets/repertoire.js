let instruments = [];
let repertoire = [];
let isAdmin = false;
let pendingWebResult = null; // {title, artist, url, source} - last-clicked web search hit, prefilled into the new-song form

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

    document.getElementById('repertoire-no-band').hidden = hasBand;
    document.getElementById('repertoire-content').hidden = !hasBand;
    if (!hasBand) return;

    if (!isAdmin) {
        document.getElementById('repertoire-instruments-section').querySelector('form').hidden = true;
        document.querySelector('.repertoire-main section:nth-of-type(2)').hidden = true; // "Add a song"
    }

    await loadInstruments();
    await loadRepertoire();
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
    const labels = ['Title', 'Original Artist', ...instruments.map((i) => i.name), 'Key', 'Length', 'Links', 'Status', ''];
    for (const label of labels) {
        const th = document.createElement('th');
        th.textContent = label;
        row.appendChild(th);
    }
}

async function loadRepertoire() {
    const res = await fetch('/api/repertoire');
    if (!res.ok) return;
    repertoire = await res.json();
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
        td.colSpan = 6 + instruments.length;
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
    });
}

init();
