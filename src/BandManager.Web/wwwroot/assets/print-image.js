// Standalone print page for a single Catalog image (flyer or regular
// photo) - opened via window.open('/print-image.html?catalogItemId=...')
// from the Catalog viewer modal's "Print" button. Same shape as
// print-setlist.js/print-gig-prep.js: a .no-print controls bar, a content
// sheet, and a plain window.print() call - no PDF library, relies on the
// browser's own print dialog plus @media print in print-image.css.

function catalogFileUrlPrint(relPath) {
    return '/' + String(relPath || '').replace(/\\/g, '/').replace(/^data\/catalog\//, 'catalog-files/');
}

async function init() {
    const params = new URLSearchParams(location.search);
    const catalogItemId = params.get('catalogItemId');
    const titleEl = document.getElementById('print-image-title');
    const sheet = document.getElementById('print-image-sheet');
    const printBtn = document.getElementById('print-now-btn');

    if (!catalogItemId) {
        titleEl.textContent = 'No image specified.';
        printBtn.disabled = true;
        return;
    }

    const res = await fetch(`/api/catalog/${catalogItemId}`);
    if (!res.ok) {
        titleEl.textContent = 'Could not load that image.';
        printBtn.disabled = true;
        return;
    }
    const item = await res.json();
    const label = item.label || item.original_filename || 'Image';

    document.title = `Print - ${label}`;
    titleEl.textContent = label;
    sheet.innerHTML = `<img src="${catalogFileUrlPrint(item.file_path)}" alt="${label.replace(/"/g, '&quot;')}">`;

    printBtn.addEventListener('click', () => window.print());
}

init();
