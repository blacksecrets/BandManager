// Shared setlist-builder modal - extracted from gig-sets.js so Gig
// Management and the Calendar's Rehearsal modal can both open the exact
// same editor on any GigSet, real (keyed by a Gig's own Ref) or floating
// (keyed by a synthetic ref - see GigSet.IsFloating). Same cross-page
// convention catalog.js's window.openCatalogPicker/buildMediaSlotControl
// already establish: this file owns the logic, but every consuming page
// still includes the #setlist-modal-backdrop markup itself verbatim.
//
// window.openSetlistEditor(gigRef, { title, onSaved }) is the one entry
// point - onSaved fires after every song add/remove/reorder, so the
// calling page can refresh whatever summary (song count, duration...) it
// shows outside the modal.
(function () {
    let currentGigRef = null;
    let currentSongs = [];
    let repertoireSongs = [];
    let onSavedCallback = null;

    function escapeHtmlSetlist(str) {
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

    function notifySaved() { onSavedCallback?.(); }

    // --- The setlist grid itself ---
    async function loadSet() {
        const res = await fetch(`/api/gig-sets/${encodeURIComponent(currentGigRef)}`);
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
                titleBlock.innerHTML = `${escapeHtmlSetlist(song.title)}${song.isManual ? ' <em>(manual)</em>' : ''}<small>${escapeHtmlSetlist(song.originalArtist || '')}${song.key ? ' · ' + escapeHtmlSetlist(song.key) : ''}${song.lengthSeconds != null ? ' · ' + formatLength(song.lengthSeconds) : ''}</small>`;
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

        // Same counts, mirrored onto a compact summary line the calling
        // page may show outside the modal (Gig Management's card) -
        // a no-op wherever that element doesn't exist (e.g. the Calendar).
        const summary = document.getElementById('gig-set-summary');
        if (summary) {
            summary.textContent = currentSongs.length === 0
                ? 'No songs in this set yet.'
                : `${currentSongs.length} song${currentSongs.length === 1 ? '' : 's'} - ${totalText} total${missing ? ` (${missing} missing length)` : ''}`;
        }
    }

    // Drag-reorder within the setlist grid - manual pointer-events, same
    // pattern as repertoire.js's startCatalogRowDrag/dashboard.js's
    // startSectionDrag.
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

            await fetch(`/api/gig-sets/${encodeURIComponent(currentGigRef)}/reorder`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ ids: currentSongs.map((s) => s.id) })
            });
            notifySaved();
        }

        document.addEventListener('pointermove', onPointerMove);
        document.addEventListener('pointerup', onPointerUp);
        document.addEventListener('pointercancel', onPointerUp);
    }

    async function removeSong(id) {
        const res = await fetch(`/api/gig-sets/${encodeURIComponent(currentGigRef)}/songs/${id}`, { method: 'DELETE' });
        if (res.ok) {
            await loadSet();
            notifySaved();
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
                <span>${escapeHtmlSetlist(entry.song.title)}<small>${escapeHtmlSetlist(entry.song.originalArtist || '')}</small></span>
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
            const res = await fetch(`/api/gig-sets/${encodeURIComponent(currentGigRef)}/songs`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ songId: song.id })
            });
            if (res.ok) { await loadSet(); notifySaved(); }
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
        if (!currentGigRef) return;

        const lengthSeconds = parseLength(form.length.value);
        if (form.length.value.trim() && lengthSeconds === null) {
            status.textContent = 'Length must be in mm:ss format, like 4:32.';
            return;
        }

        const res = await fetch(`/api/gig-sets/${encodeURIComponent(currentGigRef)}/manual-songs`, {
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
            notifySaved();
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
                    <div class="song-web-result-text">${escapeHtmlSetlist(item.title)}<small>${escapeHtmlSetlist(item.artist || '')}</small></div>
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

    // --- Modal open/close ---
    function closeSetlistModal() { document.getElementById('setlist-modal-backdrop').hidden = true; }
    document.getElementById('setlist-modal-close').addEventListener('click', closeSetlistModal);
    document.getElementById('setlist-modal-backdrop').addEventListener('click', (e) => { if (e.target.id === 'setlist-modal-backdrop') closeSetlistModal(); });

    window.openSetlistEditor = async function openSetlistEditor(gigRef, options) {
        const { title, onSaved } = options || {};
        currentGigRef = gigRef;
        onSavedCallback = onSaved || null;
        document.getElementById('setlist-modal-title').textContent = title || 'Set';
        document.getElementById('setlist-modal-backdrop').hidden = false;
        await loadRepertoireForPicker();
        await loadSet();
    };
})();
