// Shared Stage Plot editor modal - opened from an Act's edit view in Band
// Admin. Self-injecting like flyerEditor.js, so no per-page markup
// duplication - only stagePlotEditor.css needs to be linked by the host
// page. Drag/rotate mechanic mirrors flyerEditor.js's field editor:
// fractional 0.0-1.0 X/Y, pointer-event drag, a rotate handle.
(function () {
    function escapeHtml(str) {
        const div = document.createElement('div');
        div.textContent = str ?? '';
        return div.innerHTML;
    }

    const backdrop = document.createElement('div');
    backdrop.id = 'stage-plot-editor-backdrop';
    backdrop.className = 'detail-modal-backdrop';
    backdrop.hidden = true;
    backdrop.innerHTML = `
        <div class="stage-plot-editor-modal">
            <button type="button" class="detail-modal-close" id="stage-plot-editor-close">&times;</button>
            <div id="stage-plot-editor-body"></div>
        </div>
    `;
    document.body.appendChild(backdrop);

    function close() { backdrop.hidden = true; }
    document.getElementById('stage-plot-editor-close').addEventListener('click', close);
    backdrop.addEventListener('click', (e) => { if (e.target.id === 'stage-plot-editor-backdrop') close(); });

    function describeGear(g) {
        const dims = [g.lengthInches, g.widthInches, g.depthInches].filter((n) => n != null);
        const parts = [];
        if (dims.length) parts.push(`${dims.join('x')}in`);
        if (g.weightPounds != null) parts.push(`${g.weightPounds}lb`);
        return parts.join(', ');
    }

    window.openStagePlotEditor = async function openStagePlotEditor(actId, actName) {
        const body = document.getElementById('stage-plot-editor-body');
        body.innerHTML = '<p class="save-note">Loading...</p>';
        backdrop.hidden = false;

        const [gearListRes, plotRes] = await Promise.all([
            fetch(`/api/acts/${actId}/gear`),
            fetch(`/api/acts/${actId}/stage-plot`)
        ]);
        const gearList = gearListRes.ok ? await gearListRes.json() : [];
        let placedItems = plotRes.ok ? await plotRes.json() : [];

        body.innerHTML = `
            <h2>Stage Plot: ${escapeHtml(actName)}</h2>
            <div class="stage-plot-layout">
                <div class="stage-plot-available" id="stage-plot-available">
                    <h3>Gear List</h3>
                    <p class="save-note">Click an item to place it on the stage.</p>
                    <div id="stage-plot-available-list"></div>
                </div>
                <div class="stage-plot-diagram-wrap">
                    <div class="stage-plot-diagram" id="stage-plot-diagram">
                        <span class="stage-plot-zone-label stage-plot-zone-ul">Upstage Left</span>
                        <span class="stage-plot-zone-label stage-plot-zone-uc">Upstage Center</span>
                        <span class="stage-plot-zone-label stage-plot-zone-ur">Upstage Right</span>
                        <span class="stage-plot-zone-label stage-plot-zone-dl">Downstage Left</span>
                        <span class="stage-plot-zone-label stage-plot-zone-dc">Downstage Center</span>
                        <span class="stage-plot-zone-label stage-plot-zone-dr">Downstage Right</span>
                        <div id="stage-plot-items"></div>
                    </div>
                    <p class="save-note">Drag a placed item to move it &middot; drag its &#8635; handle to rotate &middot; click &times; to remove it.</p>
                </div>
            </div>
            <div class="stage-plot-legend">
                <h3>Gear Legend</h3>
                <ol id="stage-plot-legend-list"></ol>
            </div>
            <div class="stage-plot-render-preview">
                <h3>Tech Rider Preview</h3>
                <p class="save-note">The exact image that will appear in this Act's Tech Rider.</p>
                <img id="stage-plot-render-img" alt="Rendered stage plot">
            </div>
        `;

        const diagram = document.getElementById('stage-plot-diagram');
        const itemsLayer = document.getElementById('stage-plot-items');

        function renderAvailable() {
            const placedGearIds = new Set(placedItems.map((i) => i.bandGearItemId));
            const list = document.getElementById('stage-plot-available-list');
            const unplaced = gearList.filter((g) => !placedGearIds.has(g.id));
            list.innerHTML = unplaced.map((g) => `
                <button type="button" class="stage-plot-available-item" data-id="${g.id}">
                    ${escapeHtml(g.type)}${g.make || g.model ? ' - ' + escapeHtml([g.make, g.model].filter(Boolean).join(' ')) : ''}
                </button>
            `).join('') || '<p class="save-note">Everything on this Act\'s Gear List is already placed.</p>';

            list.querySelectorAll('.stage-plot-available-item').forEach((btn) => {
                btn.addEventListener('click', () => placeItem(btn.dataset.id));
            });
        }

        function renderLegend() {
            const legend = document.getElementById('stage-plot-legend-list');
            const sorted = [...placedItems].sort((a, b) => a.visibleId - b.visibleId);
            legend.innerHTML = sorted.map((i) => `
                <li value="${i.visibleId}">${escapeHtml(i.type)}${i.make || i.model ? ' - ' + escapeHtml([i.make, i.model].filter(Boolean).join(' ')) : ''}${describeGear(i) ? ` - ${escapeHtml(describeGear(i))}` : ''}</li>
            `).join('') || '<li class="save-note" style="list-style:none">Nothing placed yet.</li>';
        }

        function renderItems() {
            itemsLayer.innerHTML = '';
            placedItems.forEach((item) => {
                const el = document.createElement('div');
                el.className = 'stage-plot-item';
                el.style.left = `${item.x * 100}%`;
                el.style.top = `${item.y * 100}%`;
                el.style.transform = `rotate(${item.rotation || 0}deg)`;
                el.title = `${item.type}${item.make || item.model ? ' - ' + [item.make, item.model].filter(Boolean).join(' ') : ''}`;
                el.innerHTML = `
                    <span class="stage-plot-item-badge">${item.visibleId}</span>
                    <button type="button" class="stage-plot-item-remove" title="Remove">&times;</button>
                    <span class="stage-plot-item-rotate" title="Rotate">&#8635;</span>
                `;
                el.querySelector('.stage-plot-item-remove').addEventListener('click', (e) => { e.stopPropagation(); removeItem(item.id); });
                el.querySelector('.stage-plot-item-rotate').addEventListener('pointerdown', (e) => startRotate(e, el, item));
                el.addEventListener('pointerdown', (e) => startDrag(e, el, item));
                itemsLayer.appendChild(el);
            });
        }

        function refreshRenderPreview() {
            document.getElementById('stage-plot-render-img').src = `/api/acts/${actId}/stage-plot/render?t=${Date.now()}`;
        }

        function renderAll() {
            renderAvailable();
            renderItems();
            renderLegend();
            refreshRenderPreview();
        }

        async function placeItem(bandGearItemId) {
            const res = await fetch(`/api/acts/${actId}/stage-plot/items`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ bandGearItemId, x: 0.5, y: 0.5 })
            });
            if (!res.ok) return;
            const saved = await res.json();
            placedItems.push(saved);
            renderAll();
        }

        async function removeItem(itemId) {
            await fetch(`/api/acts/${actId}/stage-plot/items/${itemId}`, { method: 'DELETE' });
            placedItems = placedItems.filter((i) => i.id !== itemId);
            renderAll();
        }

        async function saveItem(item) {
            await fetch(`/api/acts/${actId}/stage-plot/items/${item.id}`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ x: item.x, y: item.y, rotation: item.rotation || 0 })
            });
        }

        function startDrag(e, el, item) {
            if (e.target !== el) return; // let the remove/rotate handles' own listeners handle themselves
            e.preventDefault();
            e.stopPropagation();
            const rect = diagram.getBoundingClientRect();
            const startX = e.clientX, startY = e.clientY;
            const startLeft = item.x * rect.width, startTop = item.y * rect.height;
            function onMove(ev) {
                const newLeft = Math.min(rect.width, Math.max(0, startLeft + (ev.clientX - startX)));
                const newTop = Math.min(rect.height, Math.max(0, startTop + (ev.clientY - startY)));
                el.style.left = `${(newLeft / rect.width) * 100}%`;
                el.style.top = `${(newTop / rect.height) * 100}%`;
                item.x = newLeft / rect.width;
                item.y = newTop / rect.height;
            }
            function onUp() {
                document.removeEventListener('pointermove', onMove);
                document.removeEventListener('pointerup', onUp);
                saveItem(item);
                renderLegend();
                refreshRenderPreview();
            }
            document.addEventListener('pointermove', onMove);
            document.addEventListener('pointerup', onUp);
        }

        function startRotate(e, el, item) {
            e.preventDefault();
            e.stopPropagation();
            function angleFor(ev) {
                const rect = el.getBoundingClientRect();
                const originX = rect.left + rect.width / 2, originY = rect.top + rect.height / 2;
                return Math.atan2(ev.clientY - originY, ev.clientX - originX) * (180 / Math.PI);
            }
            const startAngle = angleFor(e);
            const startRotation = item.rotation || 0;
            function onMove(ev) {
                item.rotation = Math.round(startRotation + (angleFor(ev) - startAngle));
                el.style.transform = `rotate(${item.rotation}deg)`;
            }
            function onUp() {
                document.removeEventListener('pointermove', onMove);
                document.removeEventListener('pointerup', onUp);
                saveItem(item);
                refreshRenderPreview();
            }
            document.addEventListener('pointermove', onMove);
            document.addEventListener('pointerup', onUp);
        }

        renderAll();
    };
})();
