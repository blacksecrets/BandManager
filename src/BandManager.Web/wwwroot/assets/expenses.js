// Personal reimbursable-expense log - GET /api/expenses is always the
// caller's own rows (see ExpensesController's doc comment). The Band
// Admin oversight view (mark things reimbursed across every member)
// lives on Band Accounting instead, not here - this page is "my own
// expenses," matching My Accounting's own framing.
function escapeHtmlExpenses(str) {
    const div = document.createElement('div');
    div.textContent = str == null ? '' : String(str);
    return div.innerHTML;
}

let expensesCache = [];
let editingExpenseId = null;
const usdFmtExpenses = new Intl.NumberFormat(undefined, { style: 'currency', currency: 'USD' });

async function loadExpenses() {
    const me = await fetch('/api/profile/me').then((r) => r.json());
    const noBand = document.getElementById('expenses-no-band');
    const content = document.getElementById('expenses-content');
    if (!me.activeBandRole) {
        noBand.hidden = false;
        content.hidden = true;
        return;
    }
    noBand.hidden = true;
    content.hidden = false;

    const res = await fetch('/api/expenses');
    expensesCache = res.ok ? await res.json() : [];

    window.DataGrid.render(document.getElementById('expenses-grid'), {
        columns: [
            { key: 'purchaseDate', label: 'Date', render: (e) => escapeHtmlExpenses(e.purchaseDate) },
            { key: 'purpose', label: 'Purpose', render: (e) => escapeHtmlExpenses(e.purpose) },
            { key: 'vendor', label: 'Vendor', render: (e) => escapeHtmlExpenses(e.vendor || '—') },
            { key: 'amount', label: 'Amount', sortValue: (e) => e.amount, render: (e) => usdFmtExpenses.format(e.amount) },
            { key: 'receipt', label: 'Receipt', sortable: false, render: (e) => e.receiptUrl ? `<a href="${escapeHtmlExpenses(e.receiptUrl)}" target="_blank" rel="noopener">View</a>` : '—' },
            { key: 'isReimbursed', label: 'Status', sortValue: (e) => (e.isReimbursed ? 1 : 0), render: (e) => e.isReimbursed ? '<span class="status-badge configured">Reimbursed</span>' : '<span class="status-badge not-configured">Not yet</span>' },
            { key: 'edit', label: '', sortable: false, searchable: false, render: (e) => `<button type="button" class="expense-edit-btn" data-id="${e.id}">Edit</button>` }
        ],
        rows: expensesCache, getRowId: (e) => e.id,
        defaultSortKey: 'purchaseDate', defaultSortDir: 'desc', emptyMessage: 'No expenses logged yet.'
    });

    document.getElementById('expenses-grid').addEventListener('click', (e) => {
        if (!e.target.classList.contains('expense-edit-btn')) return;
        openExpenseModal(expensesCache.find((x) => x.id === e.target.dataset.id));
    });

    populateExportYearSelect();
}

// Tax-prep spreadsheet export - the year list is built from whatever years
// this member actually has expenses in (plus the current year, even with
// none yet, so the dropdown is never empty for a brand-new member).
function populateExportYearSelect() {
    const select = document.getElementById('expenses-export-year');
    const currentYear = new Date().getFullYear();
    const years = new Set([currentYear, ...expensesCache.map((e) => parseInt(e.purchaseDate.slice(0, 4), 10))]);
    const sorted = [...years].sort((a, b) => b - a);
    select.innerHTML = sorted.map((y) => `<option value="${y}">${y}</option>`).join('');
    select.value = String(currentYear);
}

document.getElementById('expenses-export-btn').addEventListener('click', () => {
    const year = document.getElementById('expenses-export-year').value;
    window.open(`/api/expenses/export?year=${encodeURIComponent(year)}`, '_blank');
});

function closeExpenseModal() { document.getElementById('expense-modal-backdrop').hidden = true; }
document.getElementById('expense-modal-close').addEventListener('click', closeExpenseModal);
document.getElementById('expense-modal-backdrop').addEventListener('click', (e) => { if (e.target.id === 'expense-modal-backdrop') closeExpenseModal(); });

function openExpenseModal(expense) {
    editingExpenseId = expense ? expense.id : null;
    document.getElementById('expense-modal-title').textContent = expense ? 'Edit Expense' : 'Add an Expense';
    document.getElementById('expense-form-status').textContent = '';
    const form = document.getElementById('expense-form');
    form.purpose.value = expense?.purpose || '';
    form.vendor.value = expense?.vendor || '';
    form.vendorUrl.value = expense?.vendorUrl || '';
    form.purchaseDate.value = expense?.purchaseDate || new Date().toISOString().slice(0, 10);
    form.amount.value = expense?.amount ?? '';

    const deleteBtn = document.getElementById('expense-delete-btn');
    deleteBtn.hidden = !expense;
    deleteBtn.onclick = async () => {
        if (!editingExpenseId) return;
        if (!confirm(`Delete this expense ("${expense.purpose}")? This can't be undone.`)) return;
        const res = await fetch(`/api/expenses/${editingExpenseId}`, { method: 'DELETE' });
        if (res.ok) { closeExpenseModal(); await loadExpenses(); }
    };

    // Receipt upload only makes sense once the expense has an id to
    // attach the file to - a brand-new, unsaved expense hides this
    // section until the first Save creates the record.
    const receiptSection = document.getElementById('expense-receipt-section');
    receiptSection.hidden = !expense;
    const previewBox = document.getElementById('expense-receipt-preview');
    if (expense) {
        document.getElementById('expense-receipt-status').textContent = expense.receiptUrl ? 'A receipt is attached - choosing a new file or taking a new photo replaces it.' : 'No receipt attached yet.';
        if (expense.receiptUrl && isImageReceiptUrl(expense.receiptUrl)) {
            document.getElementById('expense-receipt-preview-img').src = expense.receiptUrl;
            previewBox.hidden = false;
        } else {
            previewBox.hidden = true;
        }
    } else {
        previewBox.hidden = true;
    }

    document.getElementById('expense-modal-backdrop').hidden = false;
}

function isImageReceiptUrl(url) {
    return /\.(jpe?g|png|gif|webp|bmp)$/i.test(url);
}

document.getElementById('add-expense-btn').addEventListener('click', () => openExpenseModal(null));

document.getElementById('expense-form').addEventListener('submit', async (e) => {
    e.preventDefault();
    const form = e.target;
    const status = document.getElementById('expense-form-status');
    const body = JSON.stringify({
        purpose: form.purpose.value.trim(),
        vendor: form.vendor.value.trim() || null,
        vendorUrl: form.vendorUrl.value.trim() || null,
        purchaseDate: form.purchaseDate.value,
        amount: parseFloat(form.amount.value)
    });
    const url = editingExpenseId ? `/api/expenses/${editingExpenseId}` : '/api/expenses';
    const method = editingExpenseId ? 'PUT' : 'POST';
    const res = await fetch(url, { method, headers: { 'Content-Type': 'application/json' }, body });
    const resBody = await res.json().catch(() => ({}));
    if (!res.ok) { status.textContent = resBody.error || 'Could not save this expense.'; return; }

    if (!editingExpenseId) {
        // A brand-new expense now has an id - reopen the same modal in
        // edit mode so the receipt uploader (which needs that id)
        // becomes available immediately, instead of making the user
        // close and reopen it themselves.
        await loadExpenses();
        openExpenseModal(resBody);
        return;
    }
    closeExpenseModal();
    await loadExpenses();
});

// Shared by both the plain file-picker and the camera-capture flow below
// (the camera hands back a cropped-JPEG File too, same shape) - one
// upload path, one place that keeps the instant preview and the grid in
// sync.
async function uploadReceiptFile(file) {
    if (!file || !editingExpenseId) return;
    const status = document.getElementById('expense-receipt-status');
    status.textContent = 'Uploading...';
    const formData = new FormData();
    formData.set('file', file);
    const res = await fetch(`/api/expenses/${editingExpenseId}/receipt`, { method: 'POST', body: formData });
    const resBody = await res.json().catch(() => ({}));
    if (!res.ok) { status.textContent = resBody.error || 'Could not upload receipt.'; return; }
    status.textContent = 'Receipt attached.';
    // Instant preview from the file just uploaded, rather than waiting on
    // a fresh fetch round-trip to get the same bytes back as a thumbnail.
    if (file.type.startsWith('image/')) {
        document.getElementById('expense-receipt-preview-img').src = URL.createObjectURL(file);
        document.getElementById('expense-receipt-preview').hidden = false;
    }
    await loadExpenses();
}

document.getElementById('expense-receipt-input').addEventListener('change', (e) => {
    uploadReceiptFile(e.target.files[0]);
});

document.getElementById('expense-receipt-camera-btn').addEventListener('click', async () => {
    const file = await window.ReceiptCamera.open();
    if (file) await uploadReceiptFile(file);
});

loadExpenses();
