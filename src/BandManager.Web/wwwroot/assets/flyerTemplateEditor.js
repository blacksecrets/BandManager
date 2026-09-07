// Template Edit modal - structurally the same drag/font/color editor as
// flyerEditor.js's Flyer Editor, but simpler: no gig context, no typed
// values, just each field's default position/font/color/visibility plus
// the template's own name. Self-injecting, only ever used from the
// Catalog page.
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
    backdrop.id = 'flyer-template-editor-backdrop';
    backdrop.className = 'detail-modal-backdrop';
    backdrop.hidden = true;
    backdrop.innerHTML = `
        <div class="flyer-template-editor-modal">
            <button type="button" class="detail-modal-close" id="flyer-template-editor-close">&times;</button>
            <div id="flyer-template-editor-body"></div>
        </div>
    `;
    document.body.appendChild(backdrop);

    let resolvePromise = null;
    function close(result) {
        backdrop.hidden = true;
        if (resolvePromise) { resolvePromise(result); resolvePromise = null; }
    }
    document.getElementById('flyer-template-editor-close').addEventListener('click', () => close(null));
    backdrop.addEventListener('click', (e) => { if (e.target.id === 'flyer-template-editor-backdrop') close(null); });

    window.openFlyerTemplateEditor = function openFlyerTemplateEditor(templateId) {
        return new Promise(async (resolve) => {
            resolvePromise = resolve;
            const body = document.getElementById('flyer-template-editor-body');
            body.innerHTML = '<p class="save-note">Loading...</p>';
            backdrop.hidden = false;

            const [templateRes, fontsRes] = await Promise.all([
                fetch(`/api/flyer-templates/${templateId}`),
                fetch('/api/flyer-templates/fonts')
            ]);
            if (!templateRes.ok) { body.innerHTML = '<p class="save-note">Could not load the template.</p>'; return; }
            const template = await templateRes.json();
            const fonts = fontsRes.ok ? await fontsRes.json() : [];
            const fields = template.fields.map((f) => ({ ...f }));

            body.innerHTML = `
                <h2>Edit Template</h2>
                <label>Template name <input type="text" id="flyer-template-name" value="${escapeHtml(template.name)}"></label>
                <div class="flyer-editor-layout">
                    <div class="flyer-preview-wrap">
                        <img id="flyer-template-preview-bg" src="${catalogFileUrl(template.backgroundFilePath)}" alt="Flyer background">
                        <div id="flyer-template-preview-fields"></div>
                    </div>
                    <div class="flyer-field-list" id="flyer-template-field-list"></div>
                </div>
                <button type="button" id="flyer-template-save-btn">Save</button>
                <p id="flyer-template-editor-status" class="save-note"></p>
            `;

            const bgImg = document.getElementById('flyer-template-preview-bg');
            const previewFields = document.getElementById('flyer-template-preview-fields');
            const fieldList = document.getElementById('flyer-template-field-list');

            function renderFieldList() {
                fieldList.innerHTML = '';
                fields.forEach((field) => {
                    const row = document.createElement('div');
                    row.className = 'flyer-field-row';
                    const fontOptions = fonts.map((f) => `<option value="${f.key}" ${field.fontFamily === f.key ? 'selected' : ''}>${escapeHtml(f.label)}</option>`).join('');
                    row.innerHTML = `
                        <label class="checkbox-label"><input type="checkbox" data-visible ${field.defaultVisible ? 'checked' : ''}> ${escapeHtml(field.label)}</label>
                        ${field.type === 'Text' ? `<select data-font>${fontOptions}</select><input type="color" data-color value="${field.color || '#ffffff'}">` : '<span class="save-note">(image)</span>'}
                    `;
                    row.querySelector('[data-visible]').addEventListener('change', (e) => { field.defaultVisible = e.target.checked; renderPreview(); });
                    const fontSelect = row.querySelector('[data-font]');
                    if (fontSelect) fontSelect.addEventListener('change', (e) => { field.fontFamily = e.target.value; renderPreview(); });
                    const colorInput = row.querySelector('[data-color]');
                    if (colorInput) colorInput.addEventListener('input', (e) => { field.color = e.target.value; renderPreview(); });
                    fieldList.appendChild(row);
                });
            }

            function renderPreview() {
                previewFields.innerHTML = '';
                fields.forEach((field) => {
                    if (!field.defaultVisible) return;
                    const el = document.createElement('div');
                    el.className = 'flyer-preview-field' + (field.type === 'Image' ? ' flyer-preview-field-image' : '');
                    el.style.left = `${field.x * bgImg.clientWidth}px`;
                    el.style.top = `${field.y * bgImg.clientHeight}px`;
                    if (field.type === 'Text') {
                        el.textContent = field.label;
                        el.style.color = field.color || '#ffffff';
                        el.style.fontSize = `${(field.fontSize || 0.04) * bgImg.clientHeight}px`;
                        el.style.fontFamily = `var(--flyer-font-${field.fontFamily || 'oswald-bold'})`;
                    } else {
                        el.textContent = field.label;
                        el.style.width = `${(field.fontSize || 0.1) * bgImg.clientHeight * 2}px`;
                        el.style.height = `${(field.fontSize || 0.1) * bgImg.clientHeight}px`;
                    }
                    el.addEventListener('pointerdown', (e) => startFieldDrag(e, el, field));
                    previewFields.appendChild(el);
                });
            }

            function startFieldDrag(e, el, field) {
                e.preventDefault();
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

            bgImg.addEventListener('load', renderPreview);
            new ResizeObserver(renderPreview).observe(bgImg);

            renderFieldList();
            renderPreview();

            document.getElementById('flyer-template-save-btn').addEventListener('click', async () => {
                const status = document.getElementById('flyer-template-editor-status');
                status.textContent = 'Saving...';
                const res = await fetch(`/api/flyer-templates/${templateId}`, {
                    method: 'PUT',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({
                        name: document.getElementById('flyer-template-name').value.trim(),
                        fields: fields.map((f) => ({
                            key: f.key, label: f.label, type: f.type, x: f.x, y: f.y,
                            fontSize: f.fontSize, fontFamily: f.fontFamily, color: f.color,
                            defaultVisible: f.defaultVisible
                        }))
                    })
                });
                const resBody = await res.json().catch(() => ({}));
                if (!res.ok) { status.textContent = resBody.error || 'Could not save.'; return; }
                close(resBody);
            });
        });
    };
})();
