// Shared between profile.js (the Gig Prep Defaults editor) and
// gig-sets.js (a specific gig's Gig Prep checklist) - both render the
// same shape (3 tabs, a reorderable checklist, optionally a "drag from
// gear" panel for Packing). Manual pointer-events drag, same pattern as
// gig-sets.js's own setlist reorder (native HTML5 drag/drop was rejected
// there as unreliable for a drag-from-handle gesture).

const GIG_PREP_LIST_TYPES = [
    { value: 0, label: 'Pre-gig' },
    { value: 1, label: 'Packing' },
    { value: 2, label: 'Post-gig' }
];

function gigPrepEscapeHtml(str) {
    const div = document.createElement('div');
    div.textContent = str ?? '';
    return div.innerHTML;
}

function renderGigPrepTabs(tabsEl, activeType, onSwitch) {
    tabsEl.innerHTML = '';
    for (const t of GIG_PREP_LIST_TYPES) {
        const btn = document.createElement('button');
        btn.type = 'button';
        btn.className = 'gig-prep-tab' + (t.value === activeType ? ' active' : '');
        btn.textContent = t.label;
        btn.addEventListener('click', () => onSwitch(t.value));
        tabsEl.appendChild(btn);
    }
}

// items: this tab's items only, in order: [{id, text, isChecked?}]
// options: { showCheckbox, onToggle(id, checked), onRemove(id), onReorder(idsInNewOrder) }
function renderGigPrepList(listEl, items, options) {
    listEl.innerHTML = '';
    if (items.length === 0) {
        listEl.innerHTML = '<p class="save-note">Nothing here yet.</p>';
        return;
    }
    for (const item of items) {
        const li = document.createElement('li');
        li.className = 'gig-prep-item';
        li.dataset.id = item.id;

        const handle = document.createElement('span');
        handle.className = 'drag-handle';
        handle.textContent = '⠿';
        handle.addEventListener('pointerdown', (e) => startGigPrepReorderDrag(e, li, item, listEl, items, options));
        li.appendChild(handle);

        if (options.showCheckbox) {
            const cb = document.createElement('input');
            cb.type = 'checkbox';
            cb.checked = !!item.isChecked;
            cb.addEventListener('change', () => options.onToggle(item.id, cb.checked));
            li.appendChild(cb);
        }

        const text = document.createElement('span');
        text.className = 'gig-prep-item-text';
        text.textContent = item.text;
        if (options.showCheckbox && item.isChecked) text.classList.add('gig-prep-item-done');
        li.appendChild(text);

        const removeBtn = document.createElement('button');
        removeBtn.type = 'button';
        removeBtn.className = 'remove-btn';
        removeBtn.textContent = 'Remove';
        removeBtn.addEventListener('click', () => options.onRemove(item.id));
        li.appendChild(removeBtn);

        listEl.appendChild(li);
    }
}

function startGigPrepReorderDrag(e, li, item, listEl, items, options) {
    e.preventDefault();
    li.classList.add('dragging');
    let targetLi = null;
    let insertBefore = true;

    function clearIndicators() {
        listEl.querySelectorAll('.drag-over-top, .drag-over-bottom').forEach((el) => el.classList.remove('drag-over-top', 'drag-over-bottom'));
    }

    function onPointerMove(ev) {
        const el = document.elementFromPoint(ev.clientX, ev.clientY);
        const row = el && el.closest('.gig-prep-item');
        clearIndicators();
        if (!row || row === li || row.parentElement !== listEl) { targetLi = null; return; }
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

    function onPointerUp() {
        const target = targetLi;
        const before = insertBefore;
        cleanup();
        if (!target) return;
        const fromIndex = items.findIndex((s) => s.id === item.id);
        let toIndex = items.findIndex((s) => s.id === target.dataset.id);
        if (fromIndex === toIndex) return;
        const reordered = [...items];
        const [moved] = reordered.splice(fromIndex, 1);
        toIndex = reordered.findIndex((s) => s.id === target.dataset.id);
        reordered.splice(before ? toIndex : toIndex + 1, 0, moved);
        options.onReorder(reordered.map((s) => s.id));
    }

    document.addEventListener('pointermove', onPointerMove);
    document.addEventListener('pointerup', onPointerUp);
    document.addEventListener('pointercancel', onPointerUp);
}

function gigPrepGearLabel(g) {
    const parts = [g.make, g.model].filter(Boolean).join(' ');
    return parts ? `${g.type}: ${parts}` : g.type;
}

// Renders a draggable-into-the-list panel of the user's gear, for the
// Packing tab. dropZoneEl is the <ol> the item should land in when
// dropped; onAdd(gear) is called both on drop and on a plain click (drag
// isn't discoverable on touch, so click is the fallback that always works).
function renderGigPrepGearPanel(panelEl, gear, dropZoneEl, onAdd) {
    panelEl.innerHTML = '';
    if (gear.length === 0) {
        panelEl.innerHTML = '<p class="save-note">No gear in your profile yet.</p>';
        return;
    }
    for (const g of gear) {
        const row = document.createElement('div');
        row.className = 'gig-prep-gear-item';
        row.textContent = gigPrepGearLabel(g);
        row.addEventListener('click', () => onAdd(g));
        row.addEventListener('pointerdown', (e) => {
            e.preventDefault();
            row.classList.add('dragging');
            let over = false;

            function onPointerMove(ev) {
                const el = document.elementFromPoint(ev.clientX, ev.clientY);
                const hit = !!(el && el.closest('.gig-prep-list') === dropZoneEl);
                if (hit !== over) { dropZoneEl.classList.toggle('drag-over', hit); over = hit; }
            }

            function cleanup() {
                document.removeEventListener('pointermove', onPointerMove);
                document.removeEventListener('pointerup', onPointerUp);
                document.removeEventListener('pointercancel', onPointerUp);
                row.classList.remove('dragging');
                dropZoneEl.classList.remove('drag-over');
            }

            function onPointerUp() {
                const dropped = over;
                cleanup();
                if (dropped) onAdd(g);
            }

            document.addEventListener('pointermove', onPointerMove);
            document.addEventListener('pointerup', onPointerUp);
            document.addEventListener('pointercancel', onPointerUp);
        });
        panelEl.appendChild(row);
    }
}
