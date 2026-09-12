let instruments = [];
let repertoire = [];
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

    document.getElementById('repertoire-heading').textContent = me.activeBandName ? `${me.activeBandName} Repertoire` : 'Repertoire';

    if (!isAdmin) {
        document.getElementById('repertoire-instruments-section').querySelector('form').hidden = true;
        document.querySelector('.repertoire-main section:nth-of-type(4)').hidden = true; // "Add a song" (now the 4th section - see repertoire.html)
    }

    await loadInstruments();
    await loadRepertoire();
    await loadSpotifyPlaylists();
}

// --- Spotify playlists (real songs, not text/photo promo posts - see
// SpotifyController's doc comment) ---
function spotifyTrackUriFromUrl(url) {
    const match = /open\.spotify\.com\/track\/([a-zA-Z0-9]+)/.exec(url || '');
    return match ? `spotify:track:${match[1]}` : null;
}

async function loadSpotifyPlaylists() {
    const statusRes = await fetch('/api/spotify/status');
    const { connected } = statusRes.ok ? await statusRes.json() : { connected: false };
    document.getElementById('spotify-not-connected-note').hidden = connected;
    document.getElementById('spotify-playlists-content').hidden = !connected;
    if (!connected) return;

    const res = await fetch('/api/spotify/playlists');
    const list = document.getElementById('spotify-playlist-list');
    if (!res.ok) { list.innerHTML = '<p class="save-note">Could not load playlists.</p>'; return; }
    const playlists = await res.json();

    list.innerHTML = '';
    if (playlists.length === 0) {
        list.innerHTML = '<p class="save-note">No playlists yet - create one below.</p>';
        return;
    }
    for (const playlist of playlists) list.appendChild(renderSpotifyPlaylist(playlist));
}

function renderSpotifyPlaylist(playlist) {
    const card = document.createElement('div');
    card.className = 'spotify-playlist-card';

    const header = document.createElement('div');
    header.className = 'spotify-playlist-header';
    header.innerHTML = `
        <a href="${playlist.url}" target="_blank" rel="noopener"><strong>${escapeHtml(playlist.name)}</strong></a>
        <span class="save-note">${playlist.trackCount} track${playlist.trackCount === 1 ? '' : 's'}</span>
        <button type="button" class="remove-btn">Remove playlist</button>
    `;
    header.querySelector('.remove-btn').addEventListener('click', async () => {
        if (!confirm(`Remove "${playlist.name}" from Spotify?`)) return;
        const res = await fetch(`/api/spotify/playlists/${playlist.id}`, { method: 'DELETE' });
        if (res.ok) await loadSpotifyPlaylists();
    });
    card.appendChild(header);

    const addRow = document.createElement('div');
    addRow.className = 'spotify-playlist-add-row';
    const songsWithSpotify = repertoire.filter((en) => spotifyTrackUriFromUrl(en.song.spotifyUrl));
    if (songsWithSpotify.length === 0) {
        addRow.innerHTML = '<span class="save-note">No repertoire songs have a Spotify link yet.</span>';
    } else {
        const select = document.createElement('select');
        select.innerHTML = songsWithSpotify.map((en) =>
            `<option value="${en.song.id}">${escapeHtml(en.song.title)}${en.song.originalArtist ? ' - ' + escapeHtml(en.song.originalArtist) : ''}</option>`
        ).join('');
        addRow.appendChild(select);

        const addBtn = document.createElement('button');
        addBtn.type = 'button';
        addBtn.textContent = 'Add song';
        addBtn.addEventListener('click', async () => {
            const entry = songsWithSpotify.find((en) => en.song.id === select.value);
            const trackUri = spotifyTrackUriFromUrl(entry.song.spotifyUrl);
            addBtn.disabled = true;
            const res = await fetch(`/api/spotify/playlists/${playlist.id}/tracks`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ trackUri })
            });
            addBtn.disabled = false;
            if (res.ok) await loadSpotifyPlaylists();
            else { const body = await res.json().catch(() => ({})); alert(body.error || 'Could not add that song.'); }
        });
        addRow.appendChild(addBtn);
    }
    card.appendChild(addRow);

    return card;
}

document.getElementById('spotify-create-playlist-form').addEventListener('submit', async (e) => {
    e.preventDefault();
    const form = e.target;
    const status = document.getElementById('spotify-create-playlist-status');
    const res = await fetch('/api/spotify/playlists', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ name: form.name.value.trim(), description: form.description.value.trim() || null, public: true })
    });
    const body = await res.json();
    if (res.ok) {
        status.textContent = '';
        form.reset();
        await loadSpotifyPlaylists();
    } else {
        status.textContent = body.error || 'Could not create playlist.';
    }
});

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
}

async function removeInstrument(id, name) {
    if (!confirm(`Stop tracking tuning for "${name}"? Any tunings already saved for it stay on the songs, just hidden here.`)) return;
    const res = await fetch(`/api/repertoire/instruments/${id}`, { method: 'DELETE' });
    if (res.ok) await loadInstruments();
}

const addInstrumentForm = document.getElementById('add-instrument-form');
const addInstrumentRoleSelect = document.getElementById('add-instrument-role-select');
const addInstrumentCustomName = document.getElementById('add-instrument-custom-name');

if (addInstrumentRoleSelect) {
    addInstrumentRoleSelect.addEventListener('change', () => {
        const isOther = addInstrumentRoleSelect.value === '__other__';
        addInstrumentCustomName.hidden = !isOther;
        addInstrumentCustomName.required = isOther;
        if (isOther) addInstrumentCustomName.focus();
    });
}

if (addInstrumentForm) {
    addInstrumentForm.addEventListener('submit', async (e) => {
        e.preventDefault();
        const form = e.target;
        const status = document.getElementById('add-instrument-status');
        const name = addInstrumentRoleSelect.value === '__other__'
            ? addInstrumentCustomName.value.trim()
            : addInstrumentRoleSelect.value;
        if (!name) { status.textContent = 'Select an instrument, or choose "Other" and type one in.'; return; }

        const res = await fetch('/api/repertoire/instruments', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ name })
        });
        const body = await res.json();
        if (res.ok) {
            status.textContent = '';
            form.reset();
            addInstrumentCustomName.hidden = true;
            addInstrumentCustomName.required = false;
            await loadInstruments();
        } else {
            status.textContent = body.error || 'Could not add that instrument.';
        }
    });
}

// --- Repertoire grid (DataGrid) ---
let repertoireGrid = null;
const repertoireFilterCheckboxes = ['repertoire-filter-new', 'repertoire-filter-inprogress', 'repertoire-filter-ready']
    .map((id) => document.getElementById(id));

function checkedStatuses() {
    const map = { 'repertoire-filter-new': 'New', 'repertoire-filter-inprogress': 'InProgress', 'repertoire-filter-ready': 'Ready' };
    return repertoireFilterCheckboxes.filter((cb) => cb.checked).map((cb) => map[cb.id]);
}

function filteredRepertoire() {
    const statuses = checkedStatuses();
    return statuses.length === 0 ? repertoire : repertoire.filter((e) => statuses.includes(e.status));
}

for (const cb of repertoireFilterCheckboxes) cb.addEventListener('change', renderRepertoireGrid);

function renderRepertoireGrid() {
    const rows = filteredRepertoire();
    const columns = [
        { key: 'title', label: 'Title', sortValue: (e) => e.song.title, searchValue: (e) => e.song.title, render: (e) => escapeHtml(e.song.title) },
        { key: 'artist', label: 'Original Artist', sortValue: (e) => e.song.originalArtist || '', searchValue: (e) => e.song.originalArtist || '', render: (e) => escapeHtml(e.song.originalArtist || '—') },
        { key: 'album', label: 'Album', sortValue: (e) => e.song.album || '', searchValue: (e) => e.song.album || '', render: (e) => escapeHtml(e.song.album || '—') },
        { key: 'key', label: 'Key', sortValue: (e) => e.song.key || '', searchValue: (e) => e.song.key || '', render: (e) => escapeHtml(e.song.key || '—') },
        { key: 'length', label: 'Length', sortValue: (e) => e.song.lengthSeconds ?? -1, searchable: false, render: (e) => e.song.lengthSeconds != null ? formatLength(e.song.lengthSeconds) : '—' },
        {
            key: 'links', label: 'Links', sortable: false, searchable: false,
            render: (e) => `<div class="song-links">${['YouTube', 'Spotify', 'Songsterr'].map((label, i) => {
                const url = [e.song.youTubeUrl, e.song.spotifyUrl, e.song.songsterrUrl][i];
                return url ? `<a href="${url}" target="_blank" rel="noopener">${label}</a>` : `<span class="link-missing">${label}</span>`;
            }).join('')}</div>`
        },
        { key: 'status', label: 'Status', searchValue: (e) => e.status, render: (e) => e.status === 'InProgress' ? 'In Progress' : e.status }
    ];

    if (repertoireGrid) {
        repertoireGrid.setRows(rows);
    } else {
        repertoireGrid = window.DataGrid.render(document.getElementById('repertoire-grid'), {
            columns,
            rows,
            getRowId: (e) => e.id,
            searchPlaceholder: 'Search title, artist, album, key, or status...',
            defaultSortKey: 'title',
            emptyMessage: 'Nothing in the repertoire yet - add a song above to get started.',
            rowClassName: (e) => e.status === 'New' ? 'repertoire-row-new' : e.status === 'InProgress' ? 'repertoire-row-inprogress' : '',
            onRowClick: openRepertoireDetail,
            renderFooter: (filtered) => {
                const totalSeconds = filtered.reduce((sum, e) => sum + (e.song.lengthSeconds || 0), 0);
                return `<tr><td colspan="${columns.length}">Total: ${formatLength(totalSeconds)} across ${filtered.length} song${filtered.length === 1 ? '' : 's'}</td></tr>`;
            }
        });
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
    renderRepertoireGrid();
}

// --- Repertoire detail/edit modal (opened by clicking a grid row) ---
let detailEntry = null;
let detailNoteSaveTimer = null;

function openRepertoireDetail(entry) {
    detailEntry = entry;
    const song = entry.song;
    document.getElementById('repertoire-detail-title').textContent = song.title;
    document.getElementById('repertoire-detail-artist').textContent = song.originalArtist ? `by ${song.originalArtist}` : 'Original artist unknown';
    document.getElementById('repertoire-detail-album').textContent = song.album || '—';
    document.getElementById('repertoire-detail-key').textContent = song.key || '—';
    document.getElementById('repertoire-detail-length').textContent = song.lengthSeconds != null ? formatLength(song.lengthSeconds) : '—';

    const linksBox = document.getElementById('repertoire-detail-links');
    linksBox.innerHTML = '';
    for (const [label, url] of [['YouTube', song.youTubeUrl], ['Spotify', song.spotifyUrl], ['Songsterr', song.songsterrUrl]]) {
        const el = document.createElement(url ? 'a' : 'span');
        if (url) { el.href = url; el.target = '_blank'; el.rel = 'noopener'; } else { el.className = 'link-missing'; }
        el.textContent = label;
        linksBox.appendChild(el);
    }

    const statusSelect = document.getElementById('repertoire-detail-status');
    statusSelect.value = entry.status;
    statusSelect.disabled = !isAdmin;

    const tuningsBox = document.getElementById('repertoire-detail-tunings');
    tuningsBox.innerHTML = '';
    for (const inst of instruments) {
        const label = document.createElement('label');
        const current = song.tunings[inst.name] || '';
        if (isAdmin) {
            label.innerHTML = `${escapeHtml(inst.name)} <input type="text" class="tuning-input" data-instrument="${escapeHtml(inst.name)}" value="${escapeHtml(current)}" placeholder="—">`;
        } else {
            label.innerHTML = `${escapeHtml(inst.name)} <span>${escapeHtml(current || '—')}</span>`;
        }
        tuningsBox.appendChild(label);
    }

    const noteInput = document.getElementById('repertoire-detail-note');
    noteInput.value = myNotes[entry.id] || '';

    document.getElementById('repertoire-detail-remove-btn').hidden = !isAdmin;
    document.getElementById('repertoire-detail-modal-backdrop').hidden = false;
}

document.getElementById('repertoire-detail-close').addEventListener('click', () => {
    document.getElementById('repertoire-detail-modal-backdrop').hidden = true;
});
document.getElementById('repertoire-detail-modal-backdrop').addEventListener('click', (e) => {
    if (e.target.id === 'repertoire-detail-modal-backdrop') document.getElementById('repertoire-detail-modal-backdrop').hidden = true;
});

document.getElementById('repertoire-detail-status').addEventListener('change', async (e) => {
    if (!detailEntry) return;
    await fetch(`/api/repertoire/${detailEntry.id}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ status: e.target.value })
    });
    detailEntry.status = e.target.value;
    renderRepertoireGrid();
});

document.getElementById('repertoire-detail-tunings').addEventListener('change', async (e) => {
    const input = e.target.closest('.tuning-input');
    if (!input || !detailEntry) return;
    await fetch(`/api/songs/${detailEntry.song.id}/tuning`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ instrument: input.dataset.instrument, tuning: input.value.trim() })
    });
    detailEntry.song.tunings[input.dataset.instrument] = input.value.trim();
});

document.getElementById('repertoire-detail-note').addEventListener('input', (e) => {
    if (!detailEntry) return;
    const entryId = detailEntry.id;
    clearTimeout(detailNoteSaveTimer);
    detailNoteSaveTimer = setTimeout(async () => {
        const text = e.target.value.trim();
        await fetch(`/api/song-notes/${entryId}`, {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ text })
        });
        myNotes[entryId] = text;
    }, 500);
});

document.getElementById('repertoire-detail-remove-btn').addEventListener('click', async () => {
    if (!detailEntry) return;
    if (!confirm(`Remove "${detailEntry.song.title}" from the repertoire?`)) return;
    const res = await fetch(`/api/repertoire/${detailEntry.id}`, { method: 'DELETE' });
    if (res.ok) {
        document.getElementById('repertoire-detail-modal-backdrop').hidden = true;
        await loadRepertoire();
    }
});

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

// --- Add a song: create new, via the catalog-match-then-review workflow ---
// On submit: check the shared catalog for an exact title+artist match
// first (the same check the "Add to set" manual form in setlistEditor.js
// makes). A match offers using the catalog's version instead of creating
// a duplicate; no match (or "keep mine" over a match) offers submitting
// the new song to Band Manager+ for review by other bands - either way
// it's usable in this band's own repertoire immediately (see
// SongsController.ProposeNew's own doc comment).
async function resolveAndCreateSong(fields) {
    const matchRes = await fetch(`/api/songs/match?title=${encodeURIComponent(fields.title)}&artist=${encodeURIComponent(fields.originalArtist || '')}`);
    const { match } = matchRes.ok ? await matchRes.json() : { match: null };

    if (match) {
        const useExisting = confirm(`"${match.title}"${match.originalArtist ? ' by ' + match.originalArtist : ''} is already in the shared catalog. Use that instead of creating a new entry?`);
        if (useExisting) return match;
    }

    const submitForReview = confirm(`Submit "${fields.title}" to Band Manager+ for review, so other bands can find it in the shared catalog too?\n\nEither way you can use it in this band's own repertoire right away - "Cancel" just adds it here without flagging it for other bands yet.`);
    const res = await fetch(submitForReview ? '/api/songs/propose-new' : '/api/songs', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(fields)
    });
    const body = await res.json();
    if (!res.ok) throw new Error(body.error || 'Could not create song.');
    return body;
}

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

        const fields = {
            title: form.title.value.trim(),
            originalArtist: form.originalArtist.value.trim() || null,
            album: form.album.value.trim() || null,
            key: form.key.value.trim() || null,
            lengthSeconds,
            youTubeUrl: form.youTubeUrl.value.trim() || null,
            spotifyUrl: form.spotifyUrl.value.trim() || null,
            songsterrUrl: form.songsterrUrl.value.trim() || null
        };

        let song;
        try {
            song = await resolveAndCreateSong(fields);
        } catch (err) {
            status.textContent = err.message;
            return;
        }

        const addRes = await fetch('/api/repertoire', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ songId: song.id })
        });
        const addBody = await addRes.json();
        if (!addRes.ok) { status.textContent = addBody.error || 'Could not add it to the repertoire.'; return; }

        status.textContent = '';
        form.reset();
        document.getElementById('new-song-details').open = false;
        document.getElementById('song-web-results').innerHTML = '';
        await loadRepertoire();
    });
}

// --- Bulk-import songs from CSV (moved here from Band Admin - open to
// any band member, not just Band Admins, same as adding a song by hand) ---
document.getElementById('repertoire-csv-upload').addEventListener('change', async () => {
    const input = document.getElementById('repertoire-csv-upload');
    if (!input.files[0]) return;
    const status = document.getElementById('repertoire-import-status');
    const errorsBox = document.getElementById('repertoire-import-errors');
    errorsBox.innerHTML = '';
    status.textContent = 'Importing...';

    const form = new FormData();
    form.append('file', input.files[0]);

    const res = await fetch('/api/repertoire/import', { method: 'POST', body: form });
    const body = await res.json();
    input.value = '';

    if (res.ok) {
        status.textContent = `${body.newSongsAdded} new song(s) added (pending review), ${body.changesSubmittedForReview} change(s) submitted for review, ${body.unchanged} already matched exactly.` +
            (body.alreadyPendingSkipped ? ` ${body.alreadyPendingSkipped} skipped - already has an edit under review.` : '');
        await loadRepertoire();
        return;
    }

    status.textContent = body.error || 'Import failed.';
    if (Array.isArray(body.rowErrors) && body.rowErrors.length > 0) {
        const table = document.createElement('table');
        table.className = 'user-table';
        table.innerHTML = '<thead><tr><th>Row</th><th>Column</th><th>Problem</th></tr></thead>';
        const tbody = document.createElement('tbody');
        for (const e of body.rowErrors) {
            const tr = document.createElement('tr');
            tr.innerHTML = `<td>${e.row}</td><td>${escapeHtml(e.column)}</td><td>${escapeHtml(e.message)}</td>`;
            tbody.appendChild(tr);
        }
        table.appendChild(tbody);
        errorsBox.appendChild(table);
    }
});

init();
