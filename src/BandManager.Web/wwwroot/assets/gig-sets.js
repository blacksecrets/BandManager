let gigs = [];
let selectedGigRef = null;
let currentSongs = [];
let repertoireSongs = [];
let isAdmin = false;

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
    isAdmin = !!me.isAdmin;

    document.getElementById('gig-sets-no-band').hidden = hasBand;
    document.getElementById('gig-sets-content').hidden = !hasBand;
    if (!hasBand) return;

    if (!isAdmin) document.getElementById('gig-set-add').hidden = true;

    await loadGigs();
    await loadRepertoireForPicker();
}

async function loadGigs() {
    const box = document.getElementById('gig-list');
    const res = await fetch('/api/gig-sets/gigs');
    if (!res.ok) { box.innerHTML = '<p class="save-note">Could not load gigs - is this band\'s site URL configured under Configure Web Presence?</p>'; return; }
    gigs = await res.json();

    box.innerHTML = '';
    if (gigs.length === 0) {
        box.innerHTML = '<p class="save-note">No gigs found on this band\'s site yet.</p>';
        return;
    }
    for (const gig of gigs) {
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
}

async function loadSet() {
    const res = await fetch(`/api/gig-sets/${encodeURIComponent(selectedGigRef)}`);
    if (!res.ok) return;
    const data = await res.json();
    currentSongs = data.songs;
    renderSet();
    populateAddPicker();
}

function renderSet() {
    const list = document.getElementById('gig-set-song-list');
    list.innerHTML = '';
    if (currentSongs.length === 0) {
        list.innerHTML = '<p class="save-note">No songs in this set yet - add some from the repertoire below.</p>';
        return;
    }
    currentSongs.forEach((song, index) => {
        const li = document.createElement('li');
        li.className = 'gig-set-song-row';
        li.innerHTML = `
            <span class="song-index">${index + 1}.</span>
            <span class="song-title-block">${escapeHtml(song.title)}<small>${escapeHtml(song.originalArtist || '')}${song.key ? ' · ' + escapeHtml(song.key) : ''}${song.lengthSeconds != null ? ' · ' + formatLength(song.lengthSeconds) : ''}</small></span>
        `;
        if (isAdmin) {
            const controls = document.createElement('div');
            controls.className = 'reorder-btns';

            const upBtn = document.createElement('button');
            upBtn.type = 'button';
            upBtn.textContent = '↑';
            upBtn.disabled = index === 0;
            upBtn.addEventListener('click', () => moveSong(index, index - 1));
            controls.appendChild(upBtn);

            const downBtn = document.createElement('button');
            downBtn.type = 'button';
            downBtn.textContent = '↓';
            downBtn.disabled = index === currentSongs.length - 1;
            downBtn.addEventListener('click', () => moveSong(index, index + 1));
            controls.appendChild(downBtn);

            li.appendChild(controls);

            const removeBtn = document.createElement('button');
            removeBtn.type = 'button';
            removeBtn.className = 'remove-btn';
            removeBtn.textContent = 'Remove';
            removeBtn.addEventListener('click', () => removeSong(song.songId));
            li.appendChild(removeBtn);
        }
        list.appendChild(li);
    });
}

async function moveSong(fromIndex, toIndex) {
    const reordered = [...currentSongs];
    const [moved] = reordered.splice(fromIndex, 1);
    reordered.splice(toIndex, 0, moved);
    currentSongs = reordered;
    renderSet();

    await fetch(`/api/gig-sets/${encodeURIComponent(selectedGigRef)}/reorder`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ songIds: currentSongs.map((s) => s.songId) })
    });
}

async function removeSong(songId) {
    const res = await fetch(`/api/gig-sets/${encodeURIComponent(selectedGigRef)}/songs/${songId}`, { method: 'DELETE' });
    if (res.ok) {
        await loadSet();
        await loadGigs();
    }
}

// --- Add from repertoire ---
async function loadRepertoireForPicker() {
    const res = await fetch('/api/repertoire');
    if (!res.ok) return;
    repertoireSongs = await res.json();
    populateAddPicker();
}

function populateAddPicker() {
    const select = document.getElementById('gig-set-add-select');
    if (!select) return;
    const inSetIds = new Set(currentSongs.map((s) => s.songId));
    select.innerHTML = '';
    const available = repertoireSongs.filter((e) => !inSetIds.has(e.song.id));
    if (available.length === 0) {
        select.innerHTML = '<option value="">(everything in the repertoire is already in this set)</option>';
        return;
    }
    for (const entry of available) {
        const opt = document.createElement('option');
        opt.value = entry.song.id;
        opt.textContent = `${entry.song.title}${entry.song.originalArtist ? ' — ' + entry.song.originalArtist : ''}`;
        select.appendChild(opt);
    }
}

const addBtn = document.getElementById('gig-set-add-btn');
if (addBtn) {
    addBtn.addEventListener('click', async () => {
        const select = document.getElementById('gig-set-add-select');
        const songId = select.value;
        const status = document.getElementById('gig-set-status');
        if (!songId || !selectedGigRef) return;

        const res = await fetch(`/api/gig-sets/${encodeURIComponent(selectedGigRef)}/songs`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ songId })
        });
        const body = await res.json();
        if (res.ok) {
            status.textContent = '';
            await loadSet();
            await loadGigs();
        } else {
            status.textContent = body.error || 'Could not add that song.';
        }
    });
}

init();
