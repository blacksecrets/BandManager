function escapeHtml(str) {
    const div = document.createElement('div');
    div.textContent = str ?? '';
    return div.innerHTML;
}

const params = new URLSearchParams(location.search);
const gigRef = params.get('gigRef');
const notesFromIds = (params.get('notesFrom') || '').split(',').filter(Boolean);

let lastSongs = [];
let lastGig = null;
let lastEntryIdBySongId = {};
let lastNotesByUser = {};
let lastMemberById = {};

async function init() {
    const sheet = document.getElementById('print-setlist-sheet');
    if (!gigRef) { sheet.textContent = 'No gig specified.'; return; }

    const [gigRes, setRes, prefRes, repRes, membersRes] = await Promise.all([
        fetch(`/api/gigs/${encodeURIComponent(gigRef)}`),
        fetch(`/api/gig-sets/${encodeURIComponent(gigRef)}`),
        fetch('/api/print-preferences'),
        fetch('/api/repertoire'),
        fetch('/api/profile/band-members')
    ]);

    lastGig = gigRes.ok ? await gigRes.json() : null;
    const set = setRes.ok ? await setRes.json() : { songs: [] };
    lastSongs = set.songs || [];
    const prefs = prefRes.ok ? await prefRes.json() : { fontFamily: 'Arial', bold: true, italic: false, fontSizePt: 14, lineSpacing: 'Double', numberLines: true };
    const repertoire = repRes.ok ? await repRes.json() : [];
    const members = membersRes.ok ? await membersRes.json() : [];

    lastEntryIdBySongId = {};
    for (const entry of repertoire) lastEntryIdBySongId[entry.song.id] = entry.id;

    lastMemberById = Object.fromEntries(members.map((m) => [m.id, m]));

    lastNotesByUser = {};
    await Promise.all(notesFromIds.map(async (userId) => {
        const res = await fetch(`/api/song-notes/by-user/${userId}`);
        const list = res.ok ? await res.json() : [];
        lastNotesByUser[userId] = Object.fromEntries(list.map((n) => [n.repertoireEntryId, n.text]));
    }));

    applyPrefsToForm(prefs);
    render();
}

function applyPrefsToForm(prefs) {
    document.getElementById('pref-font').value = prefs.fontFamily;
    document.getElementById('pref-bold').checked = prefs.bold;
    document.getElementById('pref-italic').checked = prefs.italic;
    document.getElementById('pref-size').value = prefs.fontSizePt;
    document.getElementById('pref-spacing').value = prefs.lineSpacing;
    document.getElementById('pref-numbers').checked = prefs.numberLines;
}

function currentPrefsFromForm() {
    return {
        fontFamily: document.getElementById('pref-font').value,
        bold: document.getElementById('pref-bold').checked,
        italic: document.getElementById('pref-italic').checked,
        fontSizePt: parseInt(document.getElementById('pref-size').value, 10) || 14,
        lineSpacing: document.getElementById('pref-spacing').value,
        numberLines: document.getElementById('pref-numbers').checked
    };
}

function render() {
    const p = currentPrefsFromForm();
    const sheet = document.getElementById('print-setlist-sheet');
    sheet.style.fontFamily = p.fontFamily;
    sheet.style.fontWeight = p.bold ? 'bold' : 'normal';
    sheet.style.fontStyle = p.italic ? 'italic' : 'normal';
    sheet.style.fontSize = `${p.fontSizePt}pt`;
    sheet.classList.remove('spacing-single', 'spacing-double', 'spacing-none');
    sheet.classList.add(p.lineSpacing === 'Single' ? 'spacing-single' : p.lineSpacing === 'None' ? 'spacing-none' : 'spacing-double');

    let html = `<h1>${escapeHtml(lastGig ? lastGig.title : 'Setlist')}</h1>`;
    if (lastGig) {
        const meta = [lastGig.venue, lastGig.date, lastGig.time].filter(Boolean).join(' · ');
        if (meta) html += `<p class="print-setlist-meta">${escapeHtml(meta)}</p>`;
    }

    if (lastSongs.length === 0) {
        html += '<p>No songs in this set.</p>';
    } else {
        html += p.numberLines ? '<ol class="print-setlist-list">' : '<ul class="print-setlist-list no-numbers">';
        for (const song of lastSongs) {
            const entryId = song.songId ? lastEntryIdBySongId[song.songId] : null;
            let notesHtml = '';
            for (const userId of Object.keys(lastNotesByUser)) {
                const text = entryId ? lastNotesByUser[userId][entryId] : null;
                if (text) {
                    const member = lastMemberById[userId];
                    notesHtml += `<div class="print-setlist-note">${escapeHtml(member ? member.firstName : 'Note')}: ${escapeHtml(text)}</div>`;
                }
            }
            html += `<li>${escapeHtml(song.title)}${song.originalArtist ? ' - ' + escapeHtml(song.originalArtist) : ''}${notesHtml}</li>`;
        }
        html += p.numberLines ? '</ol>' : '</ul>';
    }

    sheet.innerHTML = html;
}

for (const id of ['pref-font', 'pref-bold', 'pref-italic', 'pref-size', 'pref-spacing', 'pref-numbers']) {
    document.getElementById(id).addEventListener('change', render);
}

document.getElementById('pref-save-btn').addEventListener('click', async () => {
    const status = document.getElementById('pref-status');
    const res = await fetch('/api/print-preferences', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(currentPrefsFromForm())
    });
    status.textContent = res.ok ? 'Saved as your default.' : 'Could not save.';
});

document.getElementById('print-now-btn').addEventListener('click', () => window.print());

init();
