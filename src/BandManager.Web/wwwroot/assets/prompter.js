// Stage teleprompter. Pedal control needs zero special code per the
// AirTurn research behind this feature (see the Fast Follow Roadmap
// artifact): AirTurn pedals pair at the OS level like a wireless
// keyboard and send plain keydown events (Page Up/Down or arrow keys
// by default) - this page just listens for them like any other key
// press. A song's lyrics are split into "sections" on blank lines
// (set on its Repertoire detail page) - each pedal press advances one
// section, and advancing past a song's last section moves to the next
// song in the set, so one pedal handles a whole gig without switching
// songs by hand.

function escapeHtmlPrompter(str) {
    const div = document.createElement('div');
    div.textContent = str == null ? '' : String(str);
    return div.innerHTML;
}

const gigRef = new URLSearchParams(location.search).get('gigRef');
let songs = [];
let songIndex = 0;
let sectionIndex = 0;

function sectionsOf(song) {
    const text = (song.lyricsText || '').trim();
    if (!text) return [];
    return text.split(/\n\s*\n/).map((s) => s.trim()).filter(Boolean);
}

async function loadPrompter() {
    if (!gigRef) {
        document.getElementById('prompter-song-title').textContent = 'No gig specified.';
        return;
    }
    const res = await fetch(`/api/gig-sets/${encodeURIComponent(gigRef)}`);
    const data = res.ok ? await res.json() : { songs: [] };
    songs = data.songs || [];

    if (songs.length === 0) {
        document.getElementById('prompter-song-title').textContent = 'This gig has no songs in its setlist yet.';
        return;
    }

    renderSongList();
    render();
}

function renderSongList() {
    const box = document.getElementById('prompter-songlist');
    box.innerHTML = songs.map((s, i) => `
        <button type="button" class="prompter-songlist-item${i === songIndex ? ' active' : ''}" data-index="${i}">
            ${escapeHtmlPrompter(s.title)}
            ${sectionsOf(s).length === 0 ? '<span class="save-note">No lyrics yet</span>' : ''}
        </button>`).join('');
}

document.getElementById('prompter-songlist').addEventListener('click', (e) => {
    const btn = e.target.closest('.prompter-songlist-item');
    if (!btn) return;
    songIndex = parseInt(btn.dataset.index, 10);
    sectionIndex = 0;
    document.getElementById('prompter-songlist').hidden = true;
    render();
});

document.getElementById('prompter-songlist-toggle').addEventListener('click', () => {
    const box = document.getElementById('prompter-songlist');
    box.hidden = !box.hidden;
});

function render() {
    const song = songs[songIndex];
    document.getElementById('prompter-song-title').textContent = `${songIndex + 1}/${songs.length} - ${song.title}`;
    document.getElementById('prompter-artist').textContent = song.originalArtist || '';

    const sections = sectionsOf(song);
    const lyricsBox = document.getElementById('prompter-lyrics');
    if (sections.length === 0) {
        lyricsBox.innerHTML = '<p class="prompter-empty">No lyrics added for this song yet - add them from its Repertoire detail page.</p>';
    } else {
        lyricsBox.innerHTML = sections.map((s, i) =>
            `<div class="prompter-section${i === sectionIndex ? ' current' : ''}">${escapeHtmlPrompter(s)}</div>`).join('');
        const current = lyricsBox.querySelector('.prompter-section.current');
        if (current) current.scrollIntoView({ behavior: 'smooth', block: 'start' });
    }

    document.getElementById('prompter-position').textContent = sections.length
        ? `Section ${sectionIndex + 1} of ${sections.length}` : '';
    document.getElementById('prompter-prev-btn').disabled = songIndex === 0 && sectionIndex === 0;
    document.getElementById('prompter-next-btn').disabled = songIndex === songs.length - 1 && sectionIndex >= sections.length - 1;

    renderSongList();
}

function advance(direction) {
    const sections = sectionsOf(songs[songIndex]);
    if (direction > 0) {
        if (sectionIndex < sections.length - 1) { sectionIndex++; }
        else if (songIndex < songs.length - 1) { songIndex++; sectionIndex = 0; }
        else return;
    } else {
        if (sectionIndex > 0) { sectionIndex--; }
        else if (songIndex > 0) {
            songIndex--;
            sectionIndex = Math.max(sectionsOf(songs[songIndex]).length - 1, 0);
        } else return;
    }
    render();
}

document.getElementById('prompter-next-btn').addEventListener('click', () => advance(1));
document.getElementById('prompter-prev-btn').addEventListener('click', () => advance(-1));

document.addEventListener('keydown', (e) => {
    // Ignore while the help modal or song list would rather have the key
    // (e.g. someone tabbed to a button) - only Escape/Space/arrows/PageUp/
    // PageDown are ever meaningful here, so nothing else is intercepted.
    if (['ArrowRight', 'ArrowDown', 'PageDown', ' '].includes(e.key)) { e.preventDefault(); advance(1); }
    else if (['ArrowLeft', 'ArrowUp', 'PageUp'].includes(e.key)) { e.preventDefault(); advance(-1); }
    else if (e.key === 'Escape') { closeHelp(); document.getElementById('prompter-songlist').hidden = true; }
});

// --- Font size, remembered per-browser (every stage setup/eyesight differs) ---
const FONT_STEP = 0.2;
const FONT_MIN = 1.2;
const FONT_MAX = 5;
function currentFontRem() {
    const saved = parseFloat(localStorage.getItem('prompterFontRem') || '');
    return Number.isFinite(saved) ? saved : 2.4;
}
function applyFontRem(rem) {
    const clamped = Math.min(FONT_MAX, Math.max(FONT_MIN, rem));
    document.documentElement.style.setProperty('--prompter-font-size', `${clamped}rem`);
    try { localStorage.setItem('prompterFontRem', String(clamped)); } catch { /* private mode etc - just don't persist */ }
}
applyFontRem(currentFontRem());
document.getElementById('prompter-font-bigger').addEventListener('click', () => applyFontRem(currentFontRem() + FONT_STEP));
document.getElementById('prompter-font-smaller').addEventListener('click', () => applyFontRem(currentFontRem() - FONT_STEP));

// --- Help modal ---
function closeHelp() { document.getElementById('prompter-help-backdrop').hidden = true; }
document.getElementById('prompter-help-toggle').addEventListener('click', () => {
    document.getElementById('prompter-help-backdrop').hidden = false;
});
document.getElementById('prompter-help-close').addEventListener('click', closeHelp);
document.getElementById('prompter-help-backdrop').addEventListener('click', (e) => {
    if (e.target.id === 'prompter-help-backdrop') closeHelp();
});

loadPrompter();
