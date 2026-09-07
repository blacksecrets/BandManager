function escapeHtml(str) {
    const div = document.createElement('div');
    div.textContent = str ?? '';
    return div.innerHTML;
}

const LIST_TYPE_LABELS = { 0: 'Pre-gig Checklist', 1: 'Packing Checklist', 2: 'Post-gig Checklist' };

const params = new URLSearchParams(location.search);
const gigRef = params.get('gigRef');
const onlyListType = params.has('listType') ? parseInt(params.get('listType'), 10) : null;

let lastGig = null;
let lastItems = [];

async function init() {
    const sheet = document.getElementById('print-gig-prep-sheet');
    if (!gigRef) { sheet.textContent = 'No gig specified.'; return; }

    const [gigRes, itemsRes, prefRes] = await Promise.all([
        fetch(`/api/gigs/${encodeURIComponent(gigRef)}`),
        fetch(`/api/gig-prep/${encodeURIComponent(gigRef)}`),
        fetch('/api/print-preferences')
    ]);

    lastGig = gigRes.ok ? await gigRes.json() : null;
    lastItems = itemsRes.ok ? await itemsRes.json() : [];
    const prefs = prefRes.ok ? await prefRes.json() : { fontFamily: 'Arial', bold: true, italic: false, fontSizePt: 14, lineSpacing: 'Double', numberLines: true };

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

function renderOneList(p, listType) {
    const items = lastItems.filter((i) => i.listType === listType);
    let html = `<h2 class="print-gig-prep-section-title">${escapeHtml(LIST_TYPE_LABELS[listType])}</h2>`;
    if (items.length === 0) {
        html += '<p>Nothing on this list.</p>';
        return html;
    }
    html += p.numberLines ? '<ol class="print-setlist-list print-gig-prep-list">' : '<ul class="print-setlist-list no-numbers print-gig-prep-list">';
    for (const item of items) {
        html += `<li><span class="print-gig-prep-box">${item.isChecked ? '☑' : '☐'}</span>${escapeHtml(item.text)}</li>`;
    }
    html += p.numberLines ? '</ol>' : '</ul>';
    return html;
}

function render() {
    const p = currentPrefsFromForm();
    const sheet = document.getElementById('print-gig-prep-sheet');
    sheet.style.fontFamily = p.fontFamily;
    sheet.style.fontWeight = p.bold ? 'bold' : 'normal';
    sheet.style.fontStyle = p.italic ? 'italic' : 'normal';
    sheet.style.fontSize = `${p.fontSizePt}pt`;
    sheet.classList.remove('spacing-single', 'spacing-double', 'spacing-none');
    sheet.classList.add(p.lineSpacing === 'Single' ? 'spacing-single' : p.lineSpacing === 'None' ? 'spacing-none' : 'spacing-double');

    let html = `<h1>${escapeHtml(lastGig ? lastGig.title : 'Gig Prep')}</h1>`;
    if (lastGig) {
        const meta = [lastGig.venue, lastGig.date, lastGig.time].filter(Boolean).join(' · ');
        if (meta) html += `<p class="print-setlist-meta">${escapeHtml(meta)}</p>`;
    }

    if (onlyListType !== null) {
        html += renderOneList(p, onlyListType);
    } else {
        html += renderOneList(p, 0) + '<div class="print-gig-prep-page-break"></div>' + renderOneList(p, 1) + '<div class="print-gig-prep-page-break"></div>' + renderOneList(p, 2);
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
