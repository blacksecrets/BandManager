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

    // Image-type fields only ever store a bare catalogItemId in field.value
    // (see FlyerRenderer.cs) - not a displayable URL - so both the live
    // preview and a freshly-picked image need one lookup to resolve it.
    // Result is cached on the field itself (field.imageUrl) since the id
    // never changes without going through this same function again.
    async function resolveImageFieldUrl(field) {
        if (!field.value) { field.imageUrl = null; return; }
        try {
            const res = await fetch(`/api/catalog/${field.value}`);
            if (!res.ok) { field.imageUrl = null; return; }
            const item = await res.json();
            field.imageUrl = catalogFileUrl(item.file_path);
        } catch {
            field.imageUrl = null;
        }
    }

    // SuperAdmin-uploaded custom fonts (key "custom-<id>") aren't in
    // flyerEditor.css's static @font-face rules like the 7 bundled ones -
    // this injects one on first use of each, so the live preview can
    // render them too. Keyed by font key so a reopen doesn't duplicate.
    const injectedCustomFonts = new Set();
    function ensureCustomFontFace(font) {
        if (!font.fileUrl || injectedCustomFonts.has(font.key)) return;
        injectedCustomFonts.add(font.key);
        const style = document.createElement('style');
        style.textContent = `@font-face { font-family: 'flyer-${font.key}'; src: url('${font.fileUrl}') format('${font.format || 'truetype'}'); }`;
        document.head.appendChild(style);
    }
    function cssFontFamily(fonts, key) {
        const font = fonts.find((f) => f.key === key);
        return font && font.fileUrl ? `'flyer-${font.key}', sans-serif` : `var(--flyer-font-${key || 'oswald-bold'})`;
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
        // Every field starts unchecked - the band admin opts each one in
        // explicitly rather than starting from a pre-filled flyer.
        const fields = knownFields.map((f) => ({ ...f, included: false }));
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
            const base = withTemplateField || { label: 'With', type: 'Text', x: 0.06, y: 0.5, fontSize: 0.05, fontFamily: 'oswald-bold', color: '#ffffff', rotation: 0 };
            result.push({ ...base, key: `with-${i}`, label: `With ${i + 1}`, value: act.name || '', included: false });
        });
        if (withActs.length === 0 && withTemplateField) {
            result.push({ ...withTemplateField, value: '', included: false });
        }
        return result;
    }

    // flyerId (editing an already-generated flyer) is mutually exclusive
    // with catalogItemId/gigRef (starting a fresh one) - when given, the
    // background image and gig are resolved from the flyer itself, and
    // its saved field values/positions are the starting point instead of
    // the blank known-fields default. Saving updates that same Flyer row
    // in place (see FlyersController.Update) instead of creating a new one.
    window.openFlyerEditor = function openFlyerEditor({ catalogItemId, gigRef, flyerId } = {}) {
        return new Promise(async (resolve) => {
            resolvePromise = resolve;
            const body = document.getElementById('flyer-editor-body');
            body.innerHTML = '<p class="save-note">Loading...</p>';
            backdrop.hidden = false;

            let existingFields = null;
            if (flyerId) {
                const flyerRes = await fetch(`/api/flyers/${flyerId}`);
                if (!flyerRes.ok) {
                    const errBody = await flyerRes.json().catch(() => ({}));
                    body.innerHTML = `<p class="save-note">${escapeHtml(errBody.error || 'Could not load that flyer.')}</p>`;
                    return;
                }
                const flyer = await flyerRes.json();
                catalogItemId = flyer.sourceCatalogItemId;
                gigRef = flyer.gigRef;
                existingFields = flyer.fields;
            }

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

            // Sticky "current gig" - opening the editor for a gig (fresh,
            // or via editing an existing flyer) makes it the most
            // recently referenced one everywhere else too.
            fetch('/api/profile/last-selected-gig', {
                method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ gigRef })
            }).catch(() => {});

            const fonts = fontsRes.ok ? await fontsRes.json() : [];
            fonts.forEach(ensureCustomFontFace);
            const knownFields = await knownFieldsRes.json();

            let fields = existingFields || buildInitialFields(knownFields, gig);
            await Promise.all(fields.filter((f) => f.type === 'Image' && f.value).map(resolveImageFieldUrl));

            body.innerHTML = `
                <h2>${flyerId ? 'Edit Flyer' : 'Create Flyer'}: ${escapeHtml(gig.title)}</h2>
                <div class="flyer-editor-layout">
                    <div class="flyer-preview-wrap">
                        <img id="flyer-preview-bg" src="${catalogFileUrl(image.file_path)}" alt="Flyer background">
                        <div id="flyer-preview-fields"></div>
                    </div>
                    <div class="flyer-field-grid" id="flyer-field-grid"></div>
                </div>
                <button type="button" id="flyer-add-with-btn">+ Add another "With"</button>
                <button type="button" id="flyer-add-presented-by-btn">+ Add another Presented By</button>
                <button type="button" id="flyer-add-image-btn">+ Add an image</button>
                <label class="checkbox-label" id="flyer-publish-label">
                    <input type="checkbox" id="flyer-publish-checkbox"> Publish this flyer to the band's live website now
                </label>
                <button type="button" id="flyer-save-btn">Save</button>
                <p id="flyer-editor-status" class="save-note"></p>
            `;

            const bgImg = document.getElementById('flyer-preview-bg');
            const previewFields = document.getElementById('flyer-preview-fields');
            const fieldGrid = document.getElementById('flyer-field-grid');

            // Which rows are expanded - kept separate from `fields` itself
            // so it never rides along in the save payload. Every row starts
            // collapsed.
            const expandedKeys = new Set();

            function renderFieldList() {
                fieldGrid.innerHTML = '';
                fields.forEach((field) => {
                    const row = document.createElement('div');
                    row.className = 'flyer-field-row';
                    const expanded = expandedKeys.has(field.key);
                    // Selected font pinned to the top (so it's visible
                    // without scrolling a long list), then a disabled
                    // separator, then every font alphabetically below -
                    // bundled and custom fonts sorted together, matching
                    // how GET /api/flyers/fonts already returns them.
                    const selectedFont = fonts.find((f) => f.key === field.fontFamily);
                    const sortedFonts = [...fonts].sort((a, b) => a.label.localeCompare(b.label));
                    const fontOptions =
                        (selectedFont ? `<option value="${selectedFont.key}" selected>${escapeHtml(selectedFont.label)}</option><option disabled>──────</option>` : '') +
                        sortedFonts.map((f) => `<option value="${f.key}">${escapeHtml(f.label)}</option>`).join('');
                    row.innerHTML = `
                        <div class="flyer-field-row-header">
                            <button type="button" class="flyer-field-row-toggle" data-toggle aria-expanded="${expanded}">${expanded ? '&#9662;' : '&#9656;'}</button>
                            <label class="checkbox-label"><input type="checkbox" data-included ${field.included ? 'checked' : ''}> ${escapeHtml(field.label)}</label>
                        </div>
                        <div class="flyer-field-row-body" data-body ${expanded ? '' : 'hidden'}>
                            ${field.type === 'Image'
                                ? `<p class="flyer-field-image-status">${field.value ? 'Image chosen - pick another below to replace it.' : 'No image chosen yet.'}</p><div data-media-slot></div>`
                                : `<input type="text" data-value placeholder="Text" value="${escapeHtml(field.value || '')}">`}
                            ${field.type === 'Text' ? `<select data-font>${fontOptions}</select><input type="color" data-color value="${field.color || '#ffffff'}">` : ''}
                            ${field.type === 'Text' ? `
                                <span class="flyer-style-toggles">
                                    <label class="checkbox-label" title="Bold"><input type="checkbox" data-bold ${field.bold ? 'checked' : ''}> B</label>
                                    <label class="checkbox-label" title="Italic"><input type="checkbox" data-italic ${field.italic ? 'checked' : ''}> I</label>
                                    <label class="checkbox-label" title="Underline"><input type="checkbox" data-underline ${field.underline ? 'checked' : ''}> U</label>
                                </span>` : ''}
                            ${field.type === 'Image' ? `
                                <span class="flyer-style-toggles flyer-style-toggles-image">
                                    <label class="checkbox-label" title="Skew"><input type="checkbox" data-skew ${field.skew ? 'checked' : ''}> Skew</label>
                                </span>` : ''}
                            <span class="flyer-field-hint">drag to move &middot; drag &#8690; to resize &middot; drag &#8635; to rotate</span>
                        </div>
                    `;
                    row.querySelector('[data-toggle]').addEventListener('click', () => {
                        if (expandedKeys.has(field.key)) expandedKeys.delete(field.key);
                        else expandedKeys.add(field.key);
                        renderFieldList();
                    });
                    row.querySelector('[data-included]').addEventListener('change', (e) => { field.included = e.target.checked; renderPreview(); });
                    const valueInput = row.querySelector('[data-value]');
                    if (valueInput) valueInput.addEventListener('input', (e) => { field.value = e.target.value; renderPreview(); });
                    const mediaSlot = row.querySelector('[data-media-slot]');
                    if (mediaSlot) {
                        mediaSlot.appendChild(buildMediaSlotControl({
                            accept: 'image/*',
                            onResolved: async (resolved) => {
                                let catalogItemId;
                                if (resolved.mode === 'catalogItemId') {
                                    catalogItemId = resolved.id;
                                } else if (resolved.mode === 'file') {
                                    const form = new FormData();
                                    form.append('file', resolved.file);
                                    const res = await fetch('/api/catalog/upload', { method: 'POST', body: form });
                                    const body = await res.json().catch(() => ({}));
                                    if (!res.ok) { alert(body.error || 'Could not upload that image.'); return; }
                                    catalogItemId = body.id;
                                } else if (resolved.mode === 'url') {
                                    const res = await fetch('/api/catalog/from-url', {
                                        method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ url: resolved.url })
                                    });
                                    const body = await res.json().catch(() => ({}));
                                    if (!res.ok) { alert(body.error || 'Could not fetch that URL.'); return; }
                                    catalogItemId = body.id;
                                }
                                if (!catalogItemId) return;
                                field.value = catalogItemId;
                                await resolveImageFieldUrl(field);
                                renderFieldList();
                                renderPreview();
                            }
                        }));
                    }
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
                    const skewInput = row.querySelector('[data-skew]');
                    if (skewInput) skewInput.addEventListener('change', (e) => { field.skew = e.target.checked; renderPreview(); });
                    fieldGrid.appendChild(row);
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
                    // -14.04deg matches FlyerRenderer's canvas.Skew(-0.25f, 0)
                    // shear factor (atan(-0.25) in degrees) - kept in sync so
                    // the live preview lines up with the final render.
                    const skewTerm = field.type === 'Image' && field.skew ? ' skewX(-14.04deg)' : '';
                    el.style.transform = `rotate(${field.rotation || 0}deg)${skewTerm}`;
                    if (field.type === 'Text') {
                        el.textContent = field.value || field.label;
                        el.style.color = field.color || '#ffffff';
                        el.style.fontSize = `${(field.fontSize || 0.04) * bgImg.clientHeight}px`;
                        el.style.fontFamily = cssFontFamily(fonts, field.fontFamily);
                        el.style.fontWeight = field.bold ? 'bold' : 'normal';
                        el.style.fontStyle = field.italic ? 'italic' : 'normal';
                        el.style.textDecoration = field.underline ? 'underline' : 'none';
                    } else {
                        el.textContent = field.imageUrl ? '' : '(no image chosen)';
                        el.style.width = `${(field.fontSize || 0.1) * bgImg.clientHeight * 2}px`;
                        el.style.height = `${(field.fontSize || 0.1) * bgImg.clientHeight}px`;
                        el.style.backgroundImage = field.imageUrl ? `url(${field.imageUrl})` : 'none';
                        el.style.backgroundSize = 'contain';
                        el.style.backgroundRepeat = 'no-repeat';
                        el.style.backgroundPosition = 'center';
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

            // An arbitrary extra image, on top of the fixed known-fields
            // logo slots (presentedBy-N-logo) - same insertion pattern as
            // "+ Add another With", just a single Image field each time.
            // Its picker (buildMediaSlotControl, wired in renderFieldList
            // above) also registers whatever's picked into the Catalog,
            // same as everywhere else in the app a new image is added.
            document.getElementById('flyer-add-image-btn').addEventListener('click', () => {
                const nextIndex = fields.filter((f) => f.key.startsWith('image-')).length;
                fields.push({
                    key: `image-${nextIndex}`, label: `Image ${nextIndex + 1}`, type: 'Image',
                    x: 0.06, y: 0.06, fontSize: 0.12, fontFamily: null, color: null, included: true, value: '',
                    bold: false, italic: false, underline: false, rotation: 0, skew: false
                });
                expandedKeys.add(`image-${nextIndex}`);
                renderFieldList();
                renderPreview();
            });

            document.getElementById('flyer-add-with-btn').addEventListener('click', () => {
                const last = [...fields].reverse().find((f) => f.key.startsWith('with-'));
                const nextIndex = fields.filter((f) => f.key.startsWith('with-')).length;
                fields.push({
                    key: `with-${nextIndex}`, label: `With ${nextIndex + 1}`, type: 'Text',
                    x: last ? last.x : 0.06, y: last ? last.y + 0.07 : 0.5,
                    fontSize: last ? last.fontSize : 0.05, fontFamily: last ? last.fontFamily : 'oswald-bold',
                    color: last ? last.color : '#ffffff', included: false, value: '',
                    bold: last ? !!last.bold : false, italic: last ? !!last.italic : false, underline: last ? !!last.underline : false,
                    rotation: last ? (last.rotation || 0) : 0
                });
                renderFieldList();
                renderPreview();
            });

            // Mirrors "+ Add another With" above, but grows a whole group
            // of 3 fields at once (name/URL/logo) since a "Presented By"
            // credit is always that trio together - keyed
            // presentedBy-<groupIndex>-name/url/logo so each group stays
            // distinguishable in the saved Fields JSON.
            document.getElementById('flyer-add-presented-by-btn').addEventListener('click', () => {
                const groupIndexes = new Set(
                    fields.filter((f) => f.key.startsWith('presentedBy-')).map((f) => Number(f.key.split('-')[1]))
                );
                const lastIndex = groupIndexes.size ? Math.max(...groupIndexes) : null;
                const nextIndex = groupIndexes.size;
                const lastName = lastIndex === null ? null : fields.find((f) => f.key === `presentedBy-${lastIndex}-name`);
                const lastUrl = lastIndex === null ? null : fields.find((f) => f.key === `presentedBy-${lastIndex}-url`);
                const lastLogo = lastIndex === null ? null : fields.find((f) => f.key === `presentedBy-${lastIndex}-logo`);
                const baseY = lastName ? lastName.y + 0.12 : 0.8;

                fields.push({
                    key: `presentedBy-${nextIndex}-name`, label: `Presented By ${nextIndex + 1} (name)`, type: 'Text',
                    x: lastName ? lastName.x : 0.06, y: baseY,
                    fontSize: lastName ? lastName.fontSize : 0.05, fontFamily: lastName ? lastName.fontFamily : 'oswald-bold',
                    color: lastName ? lastName.color : '#ffffff', included: false, value: '',
                    bold: lastName ? !!lastName.bold : false, italic: lastName ? !!lastName.italic : false, underline: lastName ? !!lastName.underline : false,
                    rotation: lastName ? (lastName.rotation || 0) : 0
                });
                fields.push({
                    key: `presentedBy-${nextIndex}-url`, label: `Presented By ${nextIndex + 1} (URL)`, type: 'Text',
                    x: lastUrl ? lastUrl.x : 0.06, y: baseY + 0.05,
                    fontSize: lastUrl ? lastUrl.fontSize : 0.03, fontFamily: lastUrl ? lastUrl.fontFamily : 'oswald-bold',
                    color: lastUrl ? lastUrl.color : '#ffffff', included: false, value: '',
                    bold: lastUrl ? !!lastUrl.bold : false, italic: lastUrl ? !!lastUrl.italic : false, underline: lastUrl ? !!lastUrl.underline : false,
                    rotation: lastUrl ? (lastUrl.rotation || 0) : 0
                });
                fields.push({
                    key: `presentedBy-${nextIndex}-logo`, label: `Presented By ${nextIndex + 1} (logo)`, type: 'Image',
                    x: lastLogo ? lastLogo.x : 0.06, y: baseY + 0.1,
                    fontSize: lastLogo ? lastLogo.fontSize : 0.12, fontFamily: null, color: null, included: false, value: '',
                    bold: false, italic: false, underline: false, rotation: lastLogo ? (lastLogo.rotation || 0) : 0,
                    skew: lastLogo ? !!lastLogo.skew : false
                });
                renderFieldList();
                renderPreview();
            });

            document.getElementById('flyer-save-btn').addEventListener('click', async () => {
                const status = document.getElementById('flyer-editor-status');
                const publish = document.getElementById('flyer-publish-checkbox').checked;
                status.textContent = publish
                    ? 'Saving... this can take several seconds while it publishes to the site.'
                    : 'Saving to your Catalog...';
                const payload = {
                    sourceCatalogItemId: catalogItemId,
                    gigRef,
                    publish,
                    fields: fields.map((f) => ({
                        key: f.key, label: f.label, type: f.type, x: f.x, y: f.y,
                        fontSize: f.fontSize, fontFamily: f.fontFamily, color: f.color,
                        included: f.included, value: f.value || null,
                        bold: !!f.bold, italic: !!f.italic, underline: !!f.underline,
                        rotation: f.rotation || 0, skew: !!f.skew
                    }))
                };
                const res = await fetch(flyerId ? `/api/flyers/${flyerId}` : '/api/flyers', {
                    method: flyerId ? 'PUT' : 'POST',
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
