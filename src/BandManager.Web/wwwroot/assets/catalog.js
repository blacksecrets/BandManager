// The Media Catalog. Loaded on its own standalone page (catalog.html) for
// the full browse/search/upload/delete UI, and also loaded by index.html
// purely for the exported helpers further down (window.openCatalogPicker,
// window.openVideoViewer, window.buildMediaFieldControl,
// window.buildMediaSlotControl) - this file's own page-mode wiring only
// runs if #catalog-grid actually exists, so including it elsewhere is a
// no-op beyond defining those.

function catalogFileUrl(relPath) {
    return '/' + String(relPath).replace(/\\/g, '/').replace(/^data\/catalog\//, 'catalog-files/');
}

function escapeHtmlCatalog(str) {
    return String(str).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
}

// Returns null (instead of throwing) when not logged in or no active band
// is selected yet - res.json() on that error body would otherwise throw
// and leave the page stuck on its initial "Loading..." forever (same
// class of bug dashboard.js had - see its 401/403 handling in loadItems
// for the fuller explanation). Callers must check for null before
// rendering normally.
async function fetchCatalogItems(q, category) {
    const params = new URLSearchParams();
    if (q) params.set('q', q);
    if (category) params.set('category', category);
    const res = await fetch(`/api/catalog?${params.toString()}`);
    if (res.status === 401) {
        location.href = '/login.html';
        return null;
    }
    if (res.status === 403 || res.status === 400) return null;
    return res.json();
}

// --- Standalone page mode ---

if (document.getElementById('catalog-grid')) {
    const grid = document.getElementById('catalog-grid');
    const tabsBox = document.getElementById('catalog-media-tabs');
    const searchInput = document.getElementById('catalog-search');
    const addBtn = document.getElementById('catalog-add-btn');
    const addPanel = document.getElementById('catalog-add-panel');
    const deleteBtn = document.getElementById('catalog-delete-btn');

    let activeMediaType = 'image';
    let activeImageCategory = 'general';
    let allItems = [];
    let searchQuery = '';
    const selectedIds = new Set();
    const categoryTabsBox = document.getElementById('catalog-category-tabs');
    const flyersSection = document.getElementById('catalog-flyers-section');

    function updateCounts() {
        const counts = { image: 0, video: 0, audio: 0 };
        for (const item of allItems) {
            if (counts[item.media_type] !== undefined) counts[item.media_type]++;
        }
        for (const el of tabsBox.querySelectorAll('[data-count]')) {
            el.textContent = counts[el.dataset.count] || 0;
        }
    }

    function renderTile(item) {
        const tile = document.createElement('div');
        tile.className = 'catalog-tile' + (selectedIds.has(item.id) ? ' selected' : '');
        tile.dataset.id = item.id;

        const check = document.createElement('input');
        check.type = 'checkbox';
        check.className = 'catalog-tile-check';
        check.checked = selectedIds.has(item.id);
        check.addEventListener('click', (e) => e.stopPropagation());
        check.addEventListener('change', () => {
            if (check.checked) selectedIds.add(item.id);
            else selectedIds.delete(item.id);
            tile.classList.toggle('selected', check.checked);
            deleteBtn.disabled = selectedIds.size === 0;
        });
        tile.appendChild(check);

        const media = document.createElement('div');
        media.className = 'catalog-tile-media';
        if (item.media_type === 'image') {
            const img = document.createElement('img');
            img.src = catalogFileUrl(item.thumbnail_path || item.file_path);
            img.alt = item.label || '';
            media.appendChild(img);
        } else if (item.media_type === 'video') {
            const video = document.createElement('video');
            video.src = catalogFileUrl(item.file_path);
            video.preload = 'metadata';
            video.muted = true;
            media.appendChild(video);
            const badge = document.createElement('span');
            badge.className = 'catalog-tile-badge';
            badge.textContent = '▶';
            tile.appendChild(badge);
        } else {
            media.innerHTML = '<span class="catalog-tile-audio-icon">♪</span>';
        }
        tile.appendChild(media);

        const label = document.createElement('div');
        label.className = 'catalog-tile-label';
        label.textContent = item.label || item.original_filename || `#${item.id}`;
        tile.appendChild(label);

        tile.addEventListener('click', () => openCatalogViewer(item));

        return tile;
    }

    function render() {
        updateCounts();
        categoryTabsBox.hidden = activeMediaType !== 'image';

        if (activeMediaType === 'image' && activeImageCategory === 'flyers') {
            grid.hidden = true;
            flyersSection.hidden = false;
            renderFlyersSection();
            return;
        }
        grid.hidden = false;
        flyersSection.hidden = true;

        const filtered = allItems.filter((i) => i.media_type === activeMediaType && (activeMediaType !== 'image' || i.category === 'general'));
        grid.innerHTML = '';
        if (filtered.length === 0) {
            grid.innerHTML = `<p class="catalog-empty-note">No ${activeMediaType === 'image' ? 'photos' : activeMediaType} yet.</p>`;
            return;
        }
        for (const item of filtered) grid.appendChild(renderTile(item));
    }

    // --- Flyers sub-tab: General (drag source), Flyer Templates, Generated Flyers ---
    function renderFlyersSection() {
        const generalGrid = document.getElementById('catalog-flyers-general-grid');
        const templatesGrid = document.getElementById('catalog-flyer-templates-grid');
        const flyersGrid = document.getElementById('catalog-flyers-grid');
        generalGrid.innerHTML = '';
        templatesGrid.innerHTML = '';
        flyersGrid.innerHTML = '';

        const generalImages = allItems.filter((i) => i.media_type === 'image' && i.category === 'general');
        const templates = allItems.filter((i) => i.media_type === 'image' && i.category === 'flyer-template');
        const flyers = allItems.filter((i) => i.media_type === 'image' && i.category === 'flyer');

        if (generalImages.length === 0) generalGrid.innerHTML = '<p class="catalog-empty-note">No general images yet.</p>';
        for (const item of generalImages) generalGrid.appendChild(renderDraggableGeneralTile(item));

        if (templates.length === 0) templatesGrid.innerHTML = '<p class="catalog-empty-note">No Flyer Templates yet - upload one above, or drag a general image here.</p>';
        for (const item of templates) templatesGrid.appendChild(renderTile(item));

        if (flyers.length === 0) flyersGrid.innerHTML = '<p class="catalog-empty-note">No flyers created yet.</p>';
        for (const item of flyers) flyersGrid.appendChild(renderTile(item));
    }

    function renderDraggableGeneralTile(item) {
        const tile = renderTile(item);
        const handle = document.createElement('span');
        handle.className = 'drag-handle catalog-tile-drag-handle';
        handle.textContent = '⠿';
        handle.addEventListener('pointerdown', (e) => startCatalogTileDrag(e, tile, item));
        tile.appendChild(handle);
        return tile;
    }

    // Manual pointer-events drag (matches dashboard.js's startSectionDrag
    // convention - no native HTML5 drag-and-drop) - dragging a General
    // image tile onto the Flyer Templates grid reclassifies it in place.
    function startCatalogTileDrag(e, tile, item) {
        e.preventDefault();
        tile.classList.add('dragging');
        const templatesZone = document.getElementById('catalog-flyer-templates-grid');
        let over = false;

        function onPointerMove(ev) {
            const el = document.elementFromPoint(ev.clientX, ev.clientY);
            const hit = !!(el && el.closest('#catalog-flyer-templates-grid'));
            if (hit !== over) { templatesZone.classList.toggle('drag-over', hit); over = hit; }
        }
        function cleanup() {
            document.removeEventListener('pointermove', onPointerMove);
            document.removeEventListener('pointerup', onPointerUp);
            document.removeEventListener('pointercancel', onPointerUp);
            tile.classList.remove('dragging');
            templatesZone.classList.remove('drag-over');
        }
        async function onPointerUp() {
            const dropped = over;
            cleanup();
            if (!dropped) return;
            await reclassifyAsTemplate(item);
        }
        document.addEventListener('pointermove', onPointerMove);
        document.addEventListener('pointerup', onPointerUp);
        document.addEventListener('pointercancel', onPointerUp);
    }

    async function reclassifyAsTemplate(item) {
        const reclassifyRes = await fetch(`/api/catalog/${item.id}/reclassify`, {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ category: 'flyer-template' })
        });
        if (!reclassifyRes.ok) {
            const body = await reclassifyRes.json().catch(() => ({}));
            alert(body.error || 'Could not reclassify that image.');
            return;
        }
        await fetch('/api/flyer-templates', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ backgroundCatalogItemId: item.id, name: item.label || item.original_filename || 'New Template' })
        });
        await reload();
    }

    async function reload() {
        const items = await fetchCatalogItems(searchQuery);
        if (items === null) {
            grid.textContent = 'Select a band from the switcher above to continue.';
            return;
        }
        allItems = items;
        render();
    }

    tabsBox.addEventListener('click', (e) => {
        const btn = e.target.closest('.catalog-tab');
        if (!btn) return;
        activeMediaType = btn.dataset.mediaType;
        tabsBox.querySelectorAll('.catalog-tab').forEach((t) => t.classList.toggle('active', t === btn));
        render();
    });

    categoryTabsBox.addEventListener('click', (e) => {
        const btn = e.target.closest('.catalog-tab');
        if (!btn) return;
        activeImageCategory = btn.dataset.category;
        categoryTabsBox.querySelectorAll('.catalog-tab').forEach((t) => t.classList.toggle('active', t === btn));
        render();
    });

    // A direct link to /catalog#flyers (the "Flyers" nav entry) lands
    // straight on the Flyers sub-tab instead of General.
    if (location.hash === '#flyers') {
        activeImageCategory = 'flyers';
        categoryTabsBox.querySelectorAll('.catalog-tab').forEach((t) => t.classList.toggle('active', t.dataset.category === 'flyers'));
    }

    document.getElementById('catalog-upload-template').addEventListener('change', async (e) => {
        const file = e.target.files[0];
        e.target.value = '';
        if (!file) return;
        const form = new FormData();
        form.append('file', file);
        const uploadRes = await fetch('/api/catalog/upload', { method: 'POST', body: form });
        const item = await uploadRes.json();
        if (!uploadRes.ok) { alert(item.error || 'Could not upload.'); return; }
        await reclassifyAsTemplate(item);
        const templates = await fetchCatalogItems('', 'flyer-template');
        const created = (templates || []).find((t) => t.id === item.id);
        if (created) {
            const listRes = await fetch('/api/flyer-templates');
            const list = listRes.ok ? await listRes.json() : [];
            const template = list.find((t) => t.backgroundCatalogItemId === item.id);
            if (template) await window.openFlyerTemplateEditor(template.id);
        }
        await reload();
    });

    let searchTimer = null;
    searchInput.addEventListener('input', () => {
        clearTimeout(searchTimer);
        searchTimer = setTimeout(() => {
            searchQuery = searchInput.value.trim();
            reload();
        }, 250);
    });

    deleteBtn.addEventListener('click', async () => {
        if (selectedIds.size === 0) return;
        if (!confirm(`Delete ${selectedIds.size} item(s) from the Catalog? This can't be undone - anything that already used a copy of these is unaffected.`)) return;
        await fetch('/api/catalog/delete', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ ids: [...selectedIds] })
        });
        selectedIds.clear();
        deleteBtn.disabled = true;
        reload();
    });

    function renderAddPanel() {
        addPanel.innerHTML = `
            <label>Upload a file <input type="file" id="catalog-upload-file"></label>
            <p style="margin:0;color:#999;font-size:0.8rem;">or paste a direct image/video URL</p>
            <div class="media-input-url-row">
                <input type="text" id="catalog-upload-url" placeholder="https://...">
                <button type="button" id="catalog-upload-url-btn" class="catalog-toolbar-btn">Add from URL</button>
            </div>
            <p class="save-note" id="catalog-add-status"></p>
        `;
        const statusEl = addPanel.querySelector('#catalog-add-status');
        addPanel.querySelector('#catalog-upload-file').addEventListener('change', async (e) => {
            const file = e.target.files[0];
            if (!file) return;
            statusEl.textContent = 'Uploading...';
            const form = new FormData();
            form.append('file', file);
            const res = await fetch('/api/catalog/upload', { method: 'POST', body: form });
            const result = await res.json();
            if (!res.ok) { statusEl.textContent = result.error || 'Could not upload.'; return; }
            statusEl.textContent = 'Added.';
            e.target.value = '';
            reload();
        });
        addPanel.querySelector('#catalog-upload-url-btn').addEventListener('click', async () => {
            const url = addPanel.querySelector('#catalog-upload-url').value.trim();
            if (!url) return;
            statusEl.textContent = 'Fetching...';
            const res = await fetch('/api/catalog/from-url', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ url })
            });
            const result = await res.json();
            if (!res.ok) { statusEl.textContent = result.error || 'Could not fetch that URL.'; return; }
            statusEl.textContent = 'Added.';
            addPanel.querySelector('#catalog-upload-url').value = '';
            reload();
        });
    }
    renderAddPanel();

    addBtn.addEventListener('click', () => {
        addPanel.hidden = !addPanel.hidden;
    });

    function openCatalogViewer(item) {
        const backdrop = document.getElementById('catalog-viewer-backdrop');
        const body = document.getElementById('catalog-viewer-body');
        const url = catalogFileUrl(item.file_path);
        const filename = item.original_filename || `catalog-${item.id}`;

        const isVideo = item.media_type === 'video';
        let mediaHtml = '';
        if (item.media_type === 'image') {
            mediaHtml = `<img src="${url}" alt="${escapeHtmlCatalog(item.label || '')}">`;
        } else if (isVideo) {
            mediaHtml = `<video id="catalog-viewer-video" src="${url}" controls></video><canvas id="catalog-viewer-canvas" hidden></canvas>`;
        } else {
            mediaHtml = `<audio src="${url}" controls></audio>`;
        }

        body.innerHTML = `
            ${mediaHtml}
            <p class="catalog-viewer-label">${escapeHtmlCatalog(item.label || item.original_filename || '')}</p>
            <div class="catalog-viewer-actions">
                <a href="${url}" download="${escapeHtmlCatalog(filename)}">Download</a>
                ${isVideo ? `
                    <button type="button" id="catalog-viewer-capture">Capture frame</button>
                    <button type="button" id="catalog-viewer-trim-toggle">Trim</button>
                    <button type="button" id="catalog-viewer-split-toggle">Split</button>
                ` : ''}
                ${item.category === 'flyer-template' ? `
                    <button type="button" id="catalog-viewer-edit-template">Edit Template</button>
                    <button type="button" id="catalog-viewer-create-flyer">Create Flyer from This Template</button>
                ` : ''}
                <button type="button" id="catalog-viewer-rename">Rename</button>
                <button type="button" id="catalog-viewer-delete">Delete</button>
            </div>
            ${isVideo ? `
                <div id="catalog-viewer-trim-panel" class="video-cut-panel" hidden>
                    <label>Start (sec) <input type="number" id="catalog-viewer-trim-start" min="0" step="0.1" value="0"></label>
                    <label>End (sec) <input type="number" id="catalog-viewer-trim-end" min="0" step="0.1"></label>
                    <button type="button" id="catalog-viewer-trim-export">Export trimmed clip</button>
                </div>
                <div id="catalog-viewer-split-panel" class="video-cut-panel" hidden>
                    <label>Split at (sec) <input type="number" id="catalog-viewer-split-point" min="0" step="0.1"></label>
                    <button type="button" id="catalog-viewer-split-export">Export both clips</button>
                </div>
            ` : ''}
            <p class="catalog-viewer-status" id="catalog-viewer-status"></p>
        `;

        if (isVideo) {
            const video = body.querySelector('#catalog-viewer-video');
            const canvas = body.querySelector('#catalog-viewer-canvas');
            const statusEl = body.querySelector('#catalog-viewer-status');
            const trimToggle = body.querySelector('#catalog-viewer-trim-toggle');
            const splitToggle = body.querySelector('#catalog-viewer-split-toggle');
            const trimPanel = body.querySelector('#catalog-viewer-trim-panel');
            const splitPanel = body.querySelector('#catalog-viewer-split-panel');
            const trimStart = body.querySelector('#catalog-viewer-trim-start');
            const trimEnd = body.querySelector('#catalog-viewer-trim-end');
            const trimExportBtn = body.querySelector('#catalog-viewer-trim-export');
            const splitPoint = body.querySelector('#catalog-viewer-split-point');
            const splitExportBtn = body.querySelector('#catalog-viewer-split-export');

            video.onloadedmetadata = () => {
                if (!trimEnd.value) trimEnd.value = video.duration.toFixed(1);
                if (!splitPoint.value) splitPoint.value = (video.duration / 2).toFixed(1);
            };

            trimToggle.addEventListener('click', () => {
                splitPanel.hidden = true;
                trimPanel.hidden = !trimPanel.hidden;
            });
            splitToggle.addEventListener('click', () => {
                trimPanel.hidden = true;
                splitPanel.hidden = !splitPanel.hidden;
            });

            async function fetchSourceBytes() {
                const buffer = await (await fetch(url)).arrayBuffer();
                return new Uint8Array(buffer);
            }

            trimExportBtn.addEventListener('click', async () => {
                const start = Number(trimStart.value);
                const end = Number(trimEnd.value);
                if (!(end > start)) { statusEl.textContent = 'End must be after start.'; return; }
                trimExportBtn.disabled = true;
                statusEl.textContent = 'Loading the trim tool (first use only)...';
                try {
                    const data = await fetchSourceBytes();
                    statusEl.textContent = 'Trimming...';
                    const blob = await runFfmpegCut(data, start, end);
                    statusEl.textContent = 'Saving...';
                    await uploadCutBlob(blob, `${filename.replace(/\.[^.]+$/, '')}-trim.mp4`, 'trim', statusEl, 'the trimmed clip');
                    statusEl.textContent = 'Trimmed clip saved to Catalog.';
                    reload();
                } catch (err) {
                    statusEl.textContent = err.message || 'Could not trim that clip.';
                } finally {
                    trimExportBtn.disabled = false;
                }
            });

            splitExportBtn.addEventListener('click', async () => {
                const point = Number(splitPoint.value);
                const duration = video.duration;
                if (!(point > 0 && point < duration)) { statusEl.textContent = 'Split point must be inside the video.'; return; }
                splitExportBtn.disabled = true;
                statusEl.textContent = 'Loading the split tool (first use only)...';
                try {
                    const data = await fetchSourceBytes();
                    const base = filename.replace(/\.[^.]+$/, '');
                    statusEl.textContent = 'Splitting (part 1 of 2)...';
                    const firstBlob = await runFfmpegCut(data, 0, point);
                    statusEl.textContent = 'Saving part 1...';
                    await uploadCutBlob(firstBlob, `${base}-split-1.mp4`, 'split', statusEl, 'the first clip');
                    statusEl.textContent = 'Splitting (part 2 of 2)...';
                    const secondBlob = await runFfmpegCut(data, point, duration);
                    statusEl.textContent = 'Saving part 2...';
                    await uploadCutBlob(secondBlob, `${base}-split-2.mp4`, 'split', statusEl, 'the second clip');
                    statusEl.textContent = 'Both clips saved to Catalog.';
                    reload();
                } catch (err) {
                    statusEl.textContent = err.message || 'Could not split that video.';
                } finally {
                    splitExportBtn.disabled = false;
                }
            });

            body.querySelector('#catalog-viewer-capture').addEventListener('click', () => {
                if (!video.videoWidth) {
                    statusEl.textContent = "Can't capture yet - let the video load first.";
                    return;
                }
                canvas.width = video.videoWidth;
                canvas.height = video.videoHeight;
                canvas.getContext('2d').drawImage(video, 0, 0, canvas.width, canvas.height);
                canvas.toBlob(async (blob) => {
                    if (!blob) { statusEl.textContent = 'Could not capture that frame.'; return; }
                    statusEl.textContent = 'Saving...';
                    const form = new FormData();
                    form.append('file', blob, `${filename.replace(/\.[^.]+$/, '')}-frame.jpg`);
                    form.append('source', 'frame-capture');
                    const res = await fetch('/api/catalog/upload', { method: 'POST', body: form });
                    const result = await res.json();
                    statusEl.textContent = res.ok ? 'Saved to Catalog (check the Photos tab).' : (result.error || 'Could not save.');
                    if (res.ok) reload();
                }, 'image/jpeg');
            });
        }

        if (item.category === 'flyer-template') {
            body.querySelector('#catalog-viewer-edit-template').addEventListener('click', async () => {
                const listRes = await fetch('/api/flyer-templates');
                const list = listRes.ok ? await listRes.json() : [];
                const template = list.find((t) => t.backgroundCatalogItemId === item.id);
                if (!template) { alert('Could not find that template.'); return; }
                closeCatalogViewer();
                await window.openFlyerTemplateEditor(template.id);
                reload();
            });
            body.querySelector('#catalog-viewer-create-flyer').addEventListener('click', async () => {
                const listRes = await fetch('/api/flyer-templates');
                const list = listRes.ok ? await listRes.json() : [];
                const template = list.find((t) => t.backgroundCatalogItemId === item.id);
                if (!template) { alert('Could not find that template.'); return; }
                openGigPickerForTemplate(template.id);
            });
        }

        body.querySelector('#catalog-viewer-rename').addEventListener('click', async () => {
            const label = prompt('New label:', item.label || '');
            if (!label || !label.trim()) return;
            await fetch(`/api/catalog/${item.id}`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ label: label.trim() })
            });
            closeCatalogViewer();
            reload();
        });
        body.querySelector('#catalog-viewer-delete').addEventListener('click', async () => {
            if (!confirm('Delete this item from the Catalog?')) return;
            await fetch('/api/catalog/delete', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ ids: [item.id] })
            });
            closeCatalogViewer();
            reload();
        });

        backdrop.hidden = false;
    }

    function closeCatalogViewer() {
        const backdrop = document.getElementById('catalog-viewer-backdrop');
        backdrop.hidden = true;
        document.getElementById('catalog-viewer-body').innerHTML = '';
    }

    // --- Gig picker for "Create Flyer from This Template" ---
    function closeGigPicker() { document.getElementById('gig-picker-modal-backdrop').hidden = true; }
    document.getElementById('gig-picker-modal-close').addEventListener('click', closeGigPicker);
    document.getElementById('gig-picker-modal-backdrop').addEventListener('click', (e) => { if (e.target.id === 'gig-picker-modal-backdrop') closeGigPicker(); });

    async function openGigPickerForTemplate(templateId) {
        const body = document.getElementById('gig-picker-modal-body');
        body.innerHTML = '<h2>Choose a Gig</h2><p class="save-note">Loading...</p>';
        document.getElementById('gig-picker-modal-backdrop').hidden = false;

        const res = await fetch('/api/gig-sets/gigs');
        if (!res.ok) { body.innerHTML = '<h2>Choose a Gig</h2><p class="save-note">Could not load gigs.</p>'; return; }
        const allGigs = await res.json();
        if (allGigs.length === 0) {
            body.innerHTML = '<h2>Choose a Gig</h2><p class="save-note">No gigs found on this band\'s site.</p>';
            return;
        }
        const upcoming = allGigs.filter((g) => !g.isPast);
        const past = allGigs.filter((g) => g.isPast);
        const optionsHtml = (list) => list.map((g) => `<option value="${g.gigRef}">${escapeHtmlCatalog(g.title)} - ${escapeHtmlCatalog(g.date || '')}</option>`).join('');
        body.innerHTML = `
            <h2>Choose a Gig</h2>
            <select id="gig-picker-select">
                ${upcoming.length ? `<optgroup label="Upcoming">${optionsHtml(upcoming)}</optgroup>` : ''}
                ${past.length ? `<optgroup label="Past">${optionsHtml(past)}</optgroup>` : ''}
            </select>
            <button type="button" id="gig-picker-confirm">Continue</button>
        `;
        document.getElementById('gig-picker-confirm').addEventListener('click', async () => {
            const gigRef = document.getElementById('gig-picker-select').value;
            closeGigPicker();
            const result = await window.openFlyerEditor({ templateId, gigRef });
            if (result) reload();
        });
    }

    document.getElementById('catalog-viewer-close').addEventListener('click', closeCatalogViewer);
    document.getElementById('catalog-viewer-backdrop').addEventListener('click', (e) => {
        if (e.target.id === 'catalog-viewer-backdrop') closeCatalogViewer();
    });
    document.addEventListener('keydown', (e) => {
        if (e.key === 'Escape') closeCatalogViewer();
    });

    reload();
}

// --- Exported helpers, usable from any page that links this file ---

function mediaTypeForAccept(accept) {
    if (!accept) return 'image';
    if (accept.startsWith('video')) return 'video';
    if (accept.startsWith('audio')) return 'audio';
    return 'image';
}

// Three small mode buttons (Upload / Pick from Catalog / Paste URL) - the
// shared piece of both buildMediaFieldControl and buildMediaSlotControl.
// onModeChange gets called with 'file' | 'catalog' | 'url' whenever one is
// clicked; the caller decides what that click actually does (catalog and
// url need to await a picker/show a row, so this doesn't assume anything
// about active-state beyond letting the caller call setActive itself).
function createMediaModeButtons(container, onModeChange) {
    const modes = document.createElement('div');
    modes.className = 'media-input-modes';
    const buttons = {};
    for (const [key, text] of [['file', 'Upload'], ['catalog', 'Pick from Catalog'], ['url', 'Paste URL']]) {
        const btn = document.createElement('button');
        btn.type = 'button';
        btn.textContent = text;
        btn.addEventListener('click', () => onModeChange(key));
        modes.appendChild(btn);
        buttons[key] = btn;
    }
    container.appendChild(modes);
    return buttons;
}

// Opens a single-select instance of the Catalog grid in a shared modal.
// Resolves with the chosen item, or null on cancel.
window.openCatalogPicker = function openCatalogPicker({ mediaType }) {
    return new Promise((resolve) => {
        const backdrop = document.getElementById('catalog-picker-modal-backdrop');
        const grid = document.getElementById('catalog-picker-grid');
        const searchInput = document.getElementById('catalog-picker-search');
        const closeBtn = document.getElementById('catalog-picker-close');
        const cancelBtn = document.getElementById('catalog-picker-cancel');

        let settled = false;
        function finish(value) {
            if (settled) return;
            settled = true;
            backdrop.hidden = true;
            grid.innerHTML = '';
            searchInput.value = '';
            closeBtn.removeEventListener('click', onCancel);
            cancelBtn.removeEventListener('click', onCancel);
            backdrop.removeEventListener('click', onBackdropClick);
            document.removeEventListener('keydown', onKeydown);
            resolve(value);
        }
        function onCancel() { finish(null); }
        function onBackdropClick(e) { if (e.target === backdrop) finish(null); }
        function onKeydown(e) { if (e.key === 'Escape') finish(null); }

        async function renderGrid(q) {
            const items = ((await fetchCatalogItems(q)) || []).filter((i) => i.media_type === mediaType);
            grid.innerHTML = '';
            if (items.length === 0) {
                grid.innerHTML = '<p class="catalog-empty-note">Nothing here yet.</p>';
                return;
            }
            for (const item of items) {
                const tile = document.createElement('div');
                tile.className = 'catalog-tile';
                const media = document.createElement('div');
                media.className = 'catalog-tile-media';
                if (item.media_type === 'image') {
                    const img = document.createElement('img');
                    img.src = catalogFileUrl(item.thumbnail_path || item.file_path);
                    media.appendChild(img);
                } else if (item.media_type === 'video') {
                    const video = document.createElement('video');
                    video.src = catalogFileUrl(item.file_path);
                    video.preload = 'metadata';
                    video.muted = true;
                    media.appendChild(video);
                } else {
                    media.innerHTML = '<span class="catalog-tile-audio-icon">♪</span>';
                }
                tile.appendChild(media);
                const label = document.createElement('div');
                label.className = 'catalog-tile-label';
                label.textContent = item.label || item.original_filename || `#${item.id}`;
                tile.appendChild(label);
                tile.addEventListener('click', () => finish(item));
                grid.appendChild(tile);
            }
        }

        let searchTimer = null;
        searchInput.oninput = () => {
            clearTimeout(searchTimer);
            searchTimer = setTimeout(() => renderGrid(searchInput.value.trim()), 250);
        };

        closeBtn.addEventListener('click', onCancel);
        cancelBtn.addEventListener('click', onCancel);
        backdrop.addEventListener('click', onBackdropClick);
        document.addEventListener('keydown', onKeydown);

        backdrop.hidden = false;
        renderGrid('');
    });
};

// For whole-form-submits-on-submit spots. Renders a visible
// <input type="file" name="{fieldName}"> plus sibling hidden inputs
// (catalogItemId, url - unprefixed, since a form only ever has one media
// field) so the form's existing new FormData(form) submit handler needs
// no changes at all - only the markup that used to be a bare file input
// gets replaced with a call to this.
window.buildMediaFieldControl = function buildMediaFieldControl({ fieldName, accept }) {
    const wrap = document.createElement('div');
    wrap.className = 'media-input-control';

    const fileInput = document.createElement('input');
    fileInput.type = 'file';
    fileInput.name = fieldName;
    if (accept) fileInput.accept = accept;

    const catalogInput = document.createElement('input');
    catalogInput.type = 'hidden';
    catalogInput.name = 'catalogItemId';

    const urlRow = document.createElement('div');
    urlRow.className = 'media-input-url-row';
    urlRow.hidden = true;
    const urlInput = document.createElement('input');
    urlInput.type = 'text';
    urlInput.name = 'url';
    urlInput.placeholder = 'https://...';
    urlRow.appendChild(urlInput);

    const currentLabel = document.createElement('div');
    currentLabel.className = 'media-input-current';

    function setMode(mode) {
        fileInput.hidden = mode !== 'file';
        urlRow.hidden = mode !== 'url';
        buttons.file.classList.toggle('active', mode === 'file');
        buttons.catalog.classList.toggle('active', mode === 'catalog');
        buttons.url.classList.toggle('active', mode === 'url');
        if (mode !== 'file') fileInput.value = '';
        if (mode !== 'catalog') { catalogInput.value = ''; currentLabel.textContent = ''; }
        if (mode !== 'url') urlInput.value = '';
    }

    const buttons = createMediaModeButtons(wrap, async (clicked) => {
        if (clicked === 'catalog') {
            const picked = await window.openCatalogPicker({ mediaType: mediaTypeForAccept(accept) });
            if (!picked) return;
            catalogInput.value = picked.id;
            currentLabel.textContent = `Catalog: ${picked.label || picked.original_filename || ('#' + picked.id)}`;
            setMode('catalog');
        } else {
            setMode(clicked);
        }
    });

    wrap.appendChild(fileInput);
    wrap.appendChild(catalogInput);
    wrap.appendChild(urlRow);
    wrap.appendChild(currentLabel);
    setMode('file');

    // Keeps the mode buttons/visibility in sync when the enclosing form is
    // reset (form.reset() already clears the inputs' values natively -
    // this just fixes up the visual state to match).
    queueMicrotask(() => {
        const form = wrap.closest('form');
        if (form) form.addEventListener('reset', () => setMode('file'));
    });

    return wrap;
};

// For the detail modal's artifact rows (renderDetailArtifactRow) - the
// only place this control lives now (the tile face went back to a plain
// status display; see renderArtifactSlot). onResolved gets called with
// {mode:'file', file} / {mode:'catalogItemId', id} / {mode:'url', url};
// the caller builds its own FormData/POST, branching on mode to decide
// which field to append.
//
// All three ways in are visible at once - no more "Paste URL" toggle
// hiding a row that had no room to actually show in a narrow spot; here
// there's a real modal, so the URL box just sits below "Pick from
// Catalog" all the time, with its own dedicated button.
window.buildMediaSlotControl = function buildMediaSlotControl({ accept, label, onResolved }) {
    const wrap = document.createElement('div');
    wrap.className = 'media-input-control';

    const mediaType = mediaTypeForAccept(accept);
    const mediaTypeLabel = label || (mediaType.charAt(0).toUpperCase() + mediaType.slice(1));

    const fileInput = document.createElement('input');
    fileInput.type = 'file';
    fileInput.hidden = true;
    if (accept) fileInput.accept = accept;
    fileInput.addEventListener('click', (e) => e.stopPropagation());
    fileInput.addEventListener('change', () => {
        if (fileInput.files[0]) onResolved({ mode: 'file', file: fileInput.files[0] });
        fileInput.value = '';
    });

    const buttonRow = document.createElement('div');
    buttonRow.className = 'media-input-modes';

    const uploadBtn = document.createElement('button');
    uploadBtn.type = 'button';
    uploadBtn.textContent = 'Upload from Local';
    uploadBtn.addEventListener('click', (e) => { e.stopPropagation(); fileInput.click(); });

    const catalogBtn = document.createElement('button');
    catalogBtn.type = 'button';
    catalogBtn.textContent = 'Pick from Catalog';
    catalogBtn.addEventListener('click', async (e) => {
        e.stopPropagation();
        const picked = await window.openCatalogPicker({ mediaType });
        if (picked) onResolved({ mode: 'catalogItemId', id: picked.id });
    });

    buttonRow.appendChild(uploadBtn);
    buttonRow.appendChild(catalogBtn);

    const urlRow = document.createElement('div');
    urlRow.className = 'media-input-url-row';
    const urlInput = document.createElement('input');
    urlInput.type = 'text';
    urlInput.placeholder = 'https://...';
    urlInput.addEventListener('click', (e) => e.stopPropagation());
    const urlBtn = document.createElement('button');
    urlBtn.type = 'button';
    urlBtn.textContent = `Upload ${mediaTypeLabel} from URL`;
    urlBtn.addEventListener('click', (e) => {
        e.stopPropagation();
        const url = urlInput.value.trim();
        if (!url) return;
        onResolved({ mode: 'url', url });
        urlInput.value = '';
    });
    urlRow.appendChild(urlInput);
    urlRow.appendChild(urlBtn);

    wrap.appendChild(buttonRow);
    wrap.appendChild(urlRow);
    wrap.appendChild(fileInput);
    wrap.addEventListener('click', (e) => e.stopPropagation());
    return wrap;
};

// --- ffmpeg.wasm (Trim/Split only) - lazy-loaded, never on page load ---
//
// @ffmpeg/ffmpeg's UMD build isn't an ES module, so a real dynamic
// import() doesn't apply cleanly to it - this achieves the same "only
// download it if someone actually clicks Trim/Split" behavior by
// injecting a plain <script> tag on first use instead, which is what
// lets the library's own automatic-publicPath detection (it reads
// document.currentScript.src) find the sibling 814.ffmpeg.js chunk
// correctly. coreURL/wasmURL point at the vendored single-threaded
// ffmpeg-core build, served same-origin via the existing /assets route -
// no CDN, no CORS/blob-URL workaround needed.
let ffmpegInstancePromise = null;
function loadScriptOnce(src) {
    return new Promise((resolve, reject) => {
        if ([...document.scripts].some((s) => s.src.endsWith(src))) return resolve();
        const script = document.createElement('script');
        script.src = src;
        script.onload = () => resolve();
        script.onerror = () => reject(new Error(`Could not load ${src}`));
        document.head.appendChild(script);
    });
}
async function getFfmpeg() {
    if (!ffmpegInstancePromise) {
        ffmpegInstancePromise = (async () => {
            await loadScriptOnce('/assets/vendor/ffmpeg/ffmpeg.js');
            const ffmpeg = new window.FFmpegWASM.FFmpeg();
            await ffmpeg.load({
                coreURL: '/assets/vendor/ffmpeg/ffmpeg-core.js',
                wasmURL: '/assets/vendor/ffmpeg/ffmpeg-core.wasm'
            });
            return ffmpeg;
        })();
    }
    return ffmpegInstancePromise;
}

// One fast stream-copy cut (-ss <start> -to <end> -c copy) - snaps to the
// nearest keyframe rather than a frame-exact cut (normal and fine for
// social clips), and is far faster than re-encoding. Shared by both Trim
// (one call) and Split (two calls). Returns a Blob.
async function runFfmpegCut(uint8Data, startSeconds, endSeconds) {
    const ffmpeg = await getFfmpeg();
    const inputName = 'input.mp4';
    const outputName = `output-${Date.now()}.mp4`;
    // writeFile transfers (detaches) the buffer it's given to the worker -
    // .slice() hands over a fresh copy instead, so the caller's original
    // data survives and can be reused for a second cut (Split needs the
    // same source bytes twice; sharing the same ArrayBuffer across both
    // calls previously detached it on the first write and broke the second).
    await ffmpeg.writeFile(inputName, uint8Data.slice());
    await ffmpeg.exec(['-ss', String(startSeconds), '-to', String(endSeconds), '-i', inputName, '-c', 'copy', outputName]);
    const data = await ffmpeg.readFile(outputName);
    await ffmpeg.deleteFile(inputName);
    await ffmpeg.deleteFile(outputName);
    return new Blob([data.buffer], { type: 'video/mp4' });
}

async function uploadCutBlob(blob, filename, source, statusEl, label) {
    const form = new FormData();
    form.append('file', blob, filename);
    form.append('source', source);
    const res = await fetch('/api/catalog/upload', { method: 'POST', body: form });
    const result = await res.json();
    if (!res.ok) throw new Error(result.error || `Could not save ${label}.`);
    return result.item;
}

// Opens the shared video viewer (#video-modal-backdrop in index.html) for
// any video - a Catalog video or, just as much, an already-uploaded
// artifact video that was never picked from the Catalog at all. "Capture
// frame" is entirely client-side (<video> + <canvas>.drawImage() +
// .toBlob()), never touching the server for the actual video decoding -
// only the captured still gets POSTed, to /api/catalog/upload with
// source: 'frame-capture', landing it in the Photos tab.
window.openVideoViewer = function openVideoViewer(url, filename) {
    const backdrop = document.getElementById('video-modal-backdrop');
    const video = document.getElementById('video-modal-video');
    const canvas = document.getElementById('video-modal-canvas');
    const downloadLink = document.getElementById('video-modal-download');
    const captureBtn = document.getElementById('video-modal-capture');
    const statusEl = document.getElementById('video-modal-status');
    const trimToggle = document.getElementById('video-modal-trim-toggle');
    const splitToggle = document.getElementById('video-modal-split-toggle');
    const trimPanel = document.getElementById('video-modal-trim-panel');
    const splitPanel = document.getElementById('video-modal-split-panel');
    const trimStart = document.getElementById('video-modal-trim-start');
    const trimEnd = document.getElementById('video-modal-trim-end');
    const trimExportBtn = document.getElementById('video-modal-trim-export');
    const splitPoint = document.getElementById('video-modal-split-point');
    const splitExportBtn = document.getElementById('video-modal-split-export');

    video.src = url;
    downloadLink.href = url;
    downloadLink.download = filename || '';
    statusEl.textContent = '';
    trimPanel.hidden = true;
    splitPanel.hidden = true;
    trimStart.value = 0;
    trimEnd.value = '';
    splitPoint.value = '';

    video.onloadedmetadata = () => {
        if (!trimEnd.value) trimEnd.value = video.duration.toFixed(1);
        if (!splitPoint.value) splitPoint.value = (video.duration / 2).toFixed(1);
    };

    trimToggle.onclick = () => {
        splitPanel.hidden = true;
        trimPanel.hidden = !trimPanel.hidden;
    };
    splitToggle.onclick = () => {
        trimPanel.hidden = true;
        splitPanel.hidden = !splitPanel.hidden;
    };

    async function fetchSourceBytes() {
        const buffer = await (await fetch(url)).arrayBuffer();
        return new Uint8Array(buffer);
    }

    trimExportBtn.onclick = async () => {
        const start = Number(trimStart.value);
        const end = Number(trimEnd.value);
        if (!(end > start)) { statusEl.textContent = 'End must be after start.'; return; }
        trimExportBtn.disabled = true;
        statusEl.textContent = 'Loading the trim tool (first use only)...';
        try {
            const data = await fetchSourceBytes();
            statusEl.textContent = 'Trimming...';
            const blob = await runFfmpegCut(data, start, end);
            statusEl.textContent = 'Saving...';
            await uploadCutBlob(blob, `${(filename || 'video').replace(/\.[^.]+$/, '')}-trim.mp4`, 'trim', statusEl, 'the trimmed clip');
            statusEl.textContent = 'Trimmed clip saved to Catalog.';
        } catch (err) {
            statusEl.textContent = err.message || 'Could not trim that clip.';
        } finally {
            trimExportBtn.disabled = false;
        }
    };

    splitExportBtn.onclick = async () => {
        const point = Number(splitPoint.value);
        const duration = video.duration;
        if (!(point > 0 && point < duration)) { statusEl.textContent = 'Split point must be inside the video.'; return; }
        splitExportBtn.disabled = true;
        statusEl.textContent = 'Loading the split tool (first use only)...';
        try {
            const data = await fetchSourceBytes();
            const base = (filename || 'video').replace(/\.[^.]+$/, '');
            statusEl.textContent = 'Splitting (part 1 of 2)...';
            const firstBlob = await runFfmpegCut(data, 0, point);
            statusEl.textContent = 'Saving part 1...';
            await uploadCutBlob(firstBlob, `${base}-split-1.mp4`, 'split', statusEl, 'the first clip');
            statusEl.textContent = 'Splitting (part 2 of 2)...';
            const secondBlob = await runFfmpegCut(data, point, duration);
            statusEl.textContent = 'Saving part 2...';
            await uploadCutBlob(secondBlob, `${base}-split-2.mp4`, 'split', statusEl, 'the second clip');
            statusEl.textContent = 'Both clips saved to Catalog.';
        } catch (err) {
            statusEl.textContent = err.message || 'Could not split that video.';
        } finally {
            splitExportBtn.disabled = false;
        }
    };

    captureBtn.onclick = () => {
        if (!video.videoWidth) {
            statusEl.textContent = "Can't capture yet - let the video load first.";
            return;
        }
        canvas.width = video.videoWidth;
        canvas.height = video.videoHeight;
        canvas.getContext('2d').drawImage(video, 0, 0, canvas.width, canvas.height);
        canvas.toBlob(async (blob) => {
            if (!blob) { statusEl.textContent = 'Could not capture that frame.'; return; }
            statusEl.textContent = 'Saving...';
            const form = new FormData();
            form.append('file', blob, `${(filename || 'frame').replace(/\.[^.]+$/, '')}-frame.jpg`);
            form.append('source', 'frame-capture');
            const res = await fetch('/api/catalog/upload', { method: 'POST', body: form });
            const result = await res.json();
            statusEl.textContent = res.ok ? 'Saved to Catalog.' : (result.error || 'Could not save.');
        }, 'image/jpeg');
    };

    function closeVideoViewer() {
        backdrop.hidden = true;
        video.pause();
        video.src = '';
    }
    document.getElementById('video-modal-close').onclick = closeVideoViewer;
    backdrop.onclick = (e) => { if (e.target === backdrop) closeVideoViewer(); };

    backdrop.hidden = false;
};

// For the "replace this existing image" spots (openWebsiteEditForm,
// openMediaEditForm, openGalleryEditForm), which submit the media field as
// its own separate request (a fresh FormData, not new FormData(form)) once
// the main "Save to site" PUT is done - mirrors the app's existing
// two-request pattern for those forms. Reads whichever of the three modes
// buildMediaFieldControl's inputs currently hold and repackages it under
// targetFieldName (normally 'file', matching the replace routes' own
// multer field name) for that second request. Returns null if nothing was
// provided in any of the three modes.
window.mediaFieldToFormData = function mediaFieldToFormData(form, fieldName, targetFieldName) {
    const fileInput = form.elements[fieldName];
    const catalogVal = form.elements.catalogItemId ? form.elements.catalogItemId.value : '';
    const urlVal = form.elements.url ? form.elements.url.value.trim() : '';

    if (fileInput && fileInput.files && fileInput.files[0]) {
        const fd = new FormData();
        fd.append(targetFieldName, fileInput.files[0]);
        return fd;
    }
    if (catalogVal) {
        const fd = new FormData();
        fd.append('catalogItemId', catalogVal);
        return fd;
    }
    if (urlVal) {
        const fd = new FormData();
        fd.append('url', urlVal);
        return fd;
    }
    return null;
};
