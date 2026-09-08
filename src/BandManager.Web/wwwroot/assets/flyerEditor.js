// Shared Flyer Editor modal - used from Catalog (a General image's "Create
// Flyer" button) and from Gig Management ("Create/Edit Flyer" on a
// selected gig). Self-injecting like bandSwitcher.js/songReview.js, so no
// per-page markup duplication - only flyerEditor.css needs to be linked by
// the host page.
//
// There is no "Flyer Template" - any Catalog image (catalogItemId) can be
// the background directly, seeded with the app's known-fields default
// layout (GET /api/flyers/known-fields) rather than a saved template row.
(function () {
    function escapeHtml(str) {
        const div = document.createElement('div');
        div.textContent = str ?? '';
        return div.innerHTML;
    }
    function catalogFileUrl(relPath) {
        return '/' + String(relPath || '').replace(/\\/g, '/').replace(/^data\/catalog\//, 'catalog-files/');
    }

    const backdrop = document.createElement('div');
    backdrop.id = 'flyer-editor-backdrop';
    backdrop.className = 'detail-modal-backdrop';
    backdrop.hidden = true;
    backdrop.innerHTML = `
        <div class="flyer-editor-modal">
            <button type="button" class="detail-modal-close" id="flyer-editor-close">&times;</button>
            <div id="flyer-editor-body"></div>
        </div>
    `;
    document.body.appendChild(backdrop);

    let resolvePromise = null;
    function close(result) {
        backdrop.hidden = true;
        if (resolvePromise) { resolvePromise(result); resolvePromise = null; }
    }
    document.getElementById('flyer-editor-close').addEventListener('click', () => close(null));
    backdrop.addEventListener('click', (e) => { if (e.target.id === 'flyer-editor-backdrop') close(null); });

    function deriveTicketsText(gig) {
        if (gig.freeAdmission) return 'Free Admission';
        if (gig.customTicketsText) return gig.customTicketsText;
        if (gig.ticketsUrl) return 'Tickets Available';
        return '';
    }

    // Builds the working field list for this flyer: starts from the
    // known-fields default layout, prepopulating Value from known gig data
    // where a mapping exists, and expanding with-0..with-N to match
    // however many With-acts this gig actually has.
    function buildInitialFields(knownFields, gig) {
        const fields = knownFields.map((f) => ({ ...f, included: f.defaultVisible }));
        const valueFor = (key) => {
            switch (key) {
                case 'title': return gig.title || '';
                case 'date': return gig.date || '';
                case 'doorsTime': return gig.doorsTime || '';
                case 'openerTime': return gig.openerTime || '';
                case 'headlinerTime': return gig.headlinerTime || '';
                case 'venue': return gig.venue || '';
                case 'address': return gig.address || '';
                case 'tickets': return deriveTicketsText(gig);
                default: return '';
            }
        };
        const withActs = gig.with || [];
        const withTemplateField = fields.find((f) => f.key === 'with-0');
        const result = [];
        for (const f of fields) {
            if (f.key.startsWith('with-')) continue; // handled below
            result.push({ ...f, value: valueFor(f.key) });
        }
        withActs.forEach((act, i) => {
            const base = withTemplateField || { label: 'With', type: 'Text', x: 0.06, y: 0.5, fontSize: 0.05, fontFamily: 'oswald-bold', color: '#ffffff', included: true, rotation: 0 };
            result.push({ ...base, key: `with-${i}`, label: `With ${i + 1}`, value: act.name || '', included: base.included !== false });
        });
        if (withActs.length === 0 && withTemplateField) {
            result.push({ ...withTemplateField, value: '', included: false });
        }
        return result;
    }

    window.openFlyerEditor = function openFlyerEditor({ catalogItemId, gigRef }) {
        return new Promise(async (resolve) => {
            resolvePromise = resolve;
            const body = document.getElementById('flyer-editor-body');
            body.innerHTML = '<p class="save-note">Loading...</p>';
            backdrop.hidden = false;

            const [imageRes, gigRes, fontsRes, knownFieldsRes] = await Promise.all([
                fetch(`/api/catalog/${catalogItemId}`),
                fetch(`/api/gigs/${encodeURIComponent(gigRef)}`),
                fetch('/api/flyers/fonts'),
                fetch('/api/flyers/known-fields')
            ]);
            if (!imageRes.ok || !gigRes.ok || !knownFieldsRes.ok) {
                body.innerHTML = '<p class="save-note">Could not load the image or gig.</p>';
                return;
            }
            const image = await imageRes.json();
            const gig = await gigRes.json();
            const fonts = fontsRes.ok ? await fontsRes.json() : [];
            const knownFields = await knownFieldsRes.json();

            let fields = buildInitialFields(knownFields, gig);

            body.innerHTML = `
                <h2>Create Flyer: ${escapeHtml(gig.title)}</h2>
                <div class="flyer-editor-layout">
                    <div class="flyer-preview-wrap">
                        <img id="flyer-preview-bg" src="${catalogFileUrl(image.file_path)}" alt="Flyer background">
                        <div id="flyer-preview-fields"></div>
                    </div>
                    <div class="flyer-field-list" id="flyer-field-list"></div>
                </div>
                <button type="button" id="flyer-add-with-btn">+ Add another "With"</button>
                <button type="button" id="flyer-save-btn">Save</button>
                <p id="flyer-editor-status" class="save-note"></p>
            `;

            const bgImg = document.getElementById('flyer-preview-bg');
            const previewFields = document.getElementById('flyer-preview-fields');
            const fieldList = document.getElementById('flyer-field-list');

            function renderFieldList() {
                fieldList.innerHTML = '';
                fields.forEach((field, i) => {
                    const row = document.createElement('div');
                    row.className = 'flyer-field-row';
                    const fontOptions = fonts.map((f) => `<option value="${f.key}" ${field.fontFamily === f.key ? 'selected' : ''}>${escapeHtml(f.label)}</option>`).join('');
                    row.innerHTML = `
                        <label class="checkbox-label"><input type="checkbox" data-included ${field.included ? 'checked' : ''}> ${escapeHtml(field.label)}</label>
                        ${field.type === 'Image'
                            ? `<button type="button" data-pick-logo>${field.value ? 'Change image' : 'Choose image'}</button>`
                            : `<input type="text" data-value value="${escapeHtml(field.value || '')}">`}
                        ${field.type === 'Text' ? `<select data-font>${fontOptions}</select><input type="color" data-color value="${field.color || '#ffffff'}">` : ''}
                        ${field.type === 'Text' ? `
                            <span class="flyer-style-toggles">
                                <label class="checkbox-label" title="Bold"><input type="checkbox" data-bold ${field.bold ? 'checked' : ''}> B</label>
                                <label class="checkbox-label" title="Italic"><input type="checkbox" data-italic ${field.italic ? 'checked' : ''}> I</label>
                                <label class="checkbox-label" title="Underline"><input type="checkbox" data-underline ${field.underline ? 'checked' : ''}> U</label>
                            </span>` : ''}
                        <span class="flyer-field-hint">drag to move &middot; drag &#8690; to resize &middot; drag &#8635; to rotate</span>
                    `;
                    row.querySelector('[data-included]').addEventListener('change', (e) => { field.included = e.target.checked; renderPreview(); });
                    const valueInput = row.querySelector('[data-value]');
                    if (valueInput) valueInput.addEventListener('input', (e) => { field.value = e.target.value; renderPreview(); });
                    const pickBtn = row.querySelector('[data-pick-logo]');
                    if (pickBtn) pickBtn.addEventListener('click', async () => {
                        const picked = await window.openCatalogPicker({ mediaType: 'image' });
                        if (picked) { field.value = picked.id; renderPreview(); }
                    });
                    const fontSelect = row.querySelector('[data-font]');
                    if (fontSelect) fontSelect.addEventListener('change', (e) => { field.fontFamily = e.target.value; renderPreview(); });
                    const colorInput = row.querySelector('[data-color]');
                    if (colorInput) colorInput.addEventListener('input', (e) => { field.color = e.target.value; renderPreview(); });
                    const boldInput = row.querySelector('[data-bold]');
                    if (boldInput) boldInput.addEventListener('change', (e) => { field.bold = e.target.checked; renderPreview(); });
                    const italicInput = row.querySelector('[data-italic]');
                    if (italicInput) italicInput.addEventListener('change', (e) => { field.italic = e.target.checked; renderPreview(); });
                    const underlineInput = row.querySelector('[data-underline]');
                    if (underlineInput) underlineInput.addEventListener('change', (e) => { field.underline = e.target.checked; renderPreview(); });
                    fieldList.appendChild(row);
                });
            }

            function renderPreview() {
                previewFields.innerHTML = '';
                fields.forEach((field) => {
                    if (!field.included) return;
                    const el = document.createElement('div');
                    el.className = 'flyer-preview-field' + (field.type === 'Image' ? ' flyer-preview-field-image' : '');
                    el.style.left = `${field.x * bgImg.clientWidth}px`;
                    el.style.top = `${field.y * bgImg.clientHeight}px`;
                    el.style.transform = `rotate(${field.rotation || 0}deg)`;
                    if (field.type === 'Text') {
                        el.textContent = field.value || field.label;
                        el.style.color = field.color || '#ffffff';
                        el.style.fontSize = `${(field.fontSize || 0.04) * bgImg.clientHeight}px`;
                        el.style.fontFamily = `var(--flyer-font-${field.fontFamily || 'oswald-bold'})`;
                        el.style.fontWeight = field.bold ? 'bold' : 'normal';
                        el.style.fontStyle = field.italic ? 'italic' : 'normal';
                        el.style.textDecoration = field.underline ? 'underline' : 'none';
                    } else {
                        el.textContent = field.value ? '' : '(no image chosen)';
                        el.style.width = `${(field.fontSize || 0.1) * bgImg.clientHeight * 2}px`;
                        el.style.height = `${(field.fontSize || 0.1) * bgImg.clientHeight}px`;
                    }
                    el.addEventListener('pointerdown', (e) => startFieldDrag(e, el, field));

                    const resizeHandle = document.createElement('span');
                    resizeHandle.className = 'flyer-field-resize-handle';
                    resizeHandle.addEventListener('pointerdown', (e) => startFieldResize(e, el, field));
                    el.appendChild(resizeHandle);

                    const rotateHandle = document.createElement('span');
                    rotateHandle.className = 'flyer-field-rotate-handle';
                    rotateHandle.addEventListener('pointerdown', (e) => startFieldRotate(e, el, field));
                    el.appendChild(rotateHandle);

                    previewFields.appendChild(el);
                });
            }

            function startFieldDrag(e, el, field) {
                if (e.target !== el) return; // let the resize/rotate handles' own listeners handle themselves
                e.preventDefault();
                e.stopPropagation();
                const startX = e.clientX, startY = e.clientY;
                const startLeft = el.offsetLeft, startTop = el.offsetTop;
                function onMove(ev) {
                    el.style.left = `${startLeft + (ev.clientX - startX)}px`;
                    el.style.top = `${startTop + (ev.clientY - startY)}px`;
                }
                function onUp() {
                    document.removeEventListener('pointermove', onMove);
                    document.removeEventListener('pointerup', onUp);
                    field.x = el.offsetLeft / bgImg.clientWidth;
                    field.y = el.offsetTop / bgImg.clientHeight;
                }
                document.addEventListener('pointermove', onMove);
                document.addEventListener('pointerup', onUp);
            }

            // FontSize doubles as the "size" for both a Text field (literal
            // font size) and an Image field (logo height, width follows its
            // own aspect ratio) - see FlyerRenderer.cs - so resizing just
            // scales that one number, proportional to vertical drag
            // distance. No separate width/height concept needed.
            function startFieldResize(e, el, field) {
                e.preventDefault();
                e.stopPropagation();
                const startY = e.clientY;
                const startSize = field.fontSize || (field.type === 'Image' ? 0.1 : 0.04);
                function onMove(ev) {
                    const deltaFraction = (ev.clientY - startY) / bgImg.clientHeight;
                    field.fontSize = Math.max(0.01, startSize + deltaFraction);
                    renderPreview();
                }
                function onUp() {
                    document.removeEventListener('pointermove', onMove);
                    document.removeEventListener('pointerup', onUp);
                }
                document.addEventListener('pointermove', onMove);
                document.addEventListener('pointerup', onUp);
            }

            // Rotates around the field's own anchor (its top-left X/Y,
            // matching FlyerRenderer.cs's canvas.RotateDegrees pivot) -
            // angle tracks the pointer's position relative to that anchor,
            // so it feels like grabbing the rotate handle and swinging it.
            function startFieldRotate(e, el, field) {
                e.preventDefault();
                e.stopPropagation();
                function angleFor(ev) {
                    const rect = el.getBoundingClientRect();
                    const originX = rect.left, originY = rect.top;
                    return Math.atan2(ev.clientY - originY, ev.clientX - originX) * (180 / Math.PI);
                }
                const startAngle = angleFor(e);
                const startRotation = field.rotation || 0;
                function onMove(ev) {
                    field.rotation = Math.round(startRotation + (angleFor(ev) - startAngle));
                    el.style.transform = `rotate(${field.rotation}deg)`;
                }
                function onUp() {
                    document.removeEventListener('pointermove', onMove);
                    document.removeEventListener('pointerup', onUp);
                }
                document.addEventListener('pointermove', onMove);
                document.addEventListener('pointerup', onUp);
            }

            bgImg.addEventListener('load', renderPreview);
            new ResizeObserver(renderPreview).observe(bgImg);

            renderFieldList();
            renderPreview();

            document.getElementById('flyer-add-with-btn').addEventListener('click', () => {
                const last = [...fields].reverse().find((f) => f.key.startsWith('with-'));
                const nextIndex = fields.filter((f) => f.key.startsWith('with-')).length;
                fields.push({
                    key: `with-${nextIndex}`, label: `With ${nextIndex + 1}`, type: 'Text',
                    x: last ? last.x : 0.06, y: last ? last.y + 0.07 : 0.5,
                    fontSize: last ? last.fontSize : 0.05, fontFamily: last ? last.fontFamily : 'oswald-bold',
                    color: last ? last.color : '#ffffff', included: true, value: '',
                    bold: last ? !!last.bold : false, italic: last ? !!last.italic : false, underline: last ? !!last.underline : false,
                    rotation: last ? (last.rotation || 0) : 0
                });
                renderFieldList();
                renderPreview();
            });

            document.getElementById('flyer-save-btn').addEventListener('click', async () => {
                const status = document.getElementById('flyer-editor-status');
                status.textContent = 'Saving...';
                const payload = {
                    sourceCatalogItemId: catalogItemId,
                    gigRef,
                    fields: fields.map((f) => ({
                        key: f.key, label: f.label, type: f.type, x: f.x, y: f.y,
                        fontSize: f.fontSize, fontFamily: f.fontFamily, color: f.color,
                        included: f.included, value: f.value || null,
                        bold: !!f.bold, italic: !!f.italic, underline: !!f.underline,
                        rotation: f.rotation || 0
                    }))
                };
                const res = await fetch('/api/flyers', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify(payload)
                });
                const resBody = await res.json().catch(() => ({}));
                if (!res.ok) { status.textContent = resBody.error || 'Could not save the flyer.'; return; }
                close(resBody);
            });
        });
    };
})();
