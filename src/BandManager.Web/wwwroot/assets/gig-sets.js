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
    const upcomingBox = document.getElementById('gig-list-upcoming');
    const pastBox = document.getElementById('gig-list-past');
    const res = await fetch('/api/gig-sets/gigs');
    if (!res.ok) { upcomingBox.innerHTML = '<p class="save-note">Could not load gigs - is this band\'s site URL configured under Configure Web Presence?</p>'; return; }
    gigs = await res.json();

    renderGigList(upcomingBox, gigs.filter((g) => !g.isPast));
    renderGigList(pastBox, gigs.filter((g) => g.isPast));
    if (gigs.filter((g) => !g.isPast).length === 0) upcomingBox.innerHTML = '<p class="save-note">No upcoming gigs found on this band\'s site.</p>';
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

init();
