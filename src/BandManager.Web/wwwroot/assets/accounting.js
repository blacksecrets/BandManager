// Band Admin > Accounting - the "Receivables" control: two independent
// default-payee dropdowns, a roster multi-select (checkboxes, matching
// this app's existing "pick several members" convention elsewhere), and a
// percentage-split grid whose Save only enables once everything adds up
// to 100 - see AccountingController for the matching server-side checks.

function escapeHtmlAccounting(str) {
    const div = document.createElement('div');
    div.textContent = str == null ? '' : String(str);
    return div.innerHTML;
}

function memberName(m) {
    return m.firstName ? `${m.firstName} ${m.lastName || ''}`.trim() : m.email;
}

let bandMembersCache = [];
let recipientsCache = [];

async function loadAccounting() {
    const me = await fetch('/api/profile/me').then((r) => r.json());
    const noBand = document.getElementById('accounting-no-band');
    const content = document.getElementById('accounting-content');
    if (!me.activeBandRole) {
        noBand.hidden = false;
        content.hidden = true;
        return;
    }
    noBand.hidden = true;
    content.hidden = false;

    document.getElementById('payout-terms-content').hidden = false;
    await loadPayoutTerms();

    const res = await fetch('/api/accounting/receivables');
    if (!res.ok) return;
    const data = await res.json();
    bandMembersCache = data.bandMembers;
    recipientsCache = data.recipients;

    populateDefaultSelects(data);
    renderRosterCheckboxes();
    renderPercentagesGrid();

    document.getElementById('financial-summary-content').hidden = false;
    await loadFinancialSummaryYears();
}

// --- Financial Summary (D13) ---
const QUARTER_MONTHS = ['Jan-Mar', 'Apr-Jun', 'Jul-Sep', 'Oct-Dec'];
const usdFmt = new Intl.NumberFormat(undefined, { style: 'currency', currency: 'USD' });

async function loadFinancialSummaryYears() {
    const select = document.getElementById('financial-summary-year-select');
    const years = await fetch('/api/accounting/summary/years').then((r) => (r.ok ? r.json() : []));
    const currentYear = new Date().getFullYear();
    // Always includes the current year even with zero payout data yet -
    // a band just starting out shouldn't see an empty dropdown with
    // nothing to pick.
    const allYears = years.includes(currentYear) ? years : [currentYear, ...years];
    select.innerHTML = allYears.map((y) => `<option value="${y}">${y}</option>`).join('');
    select.value = String(currentYear);
    await loadFinancialSummary();
}
document.getElementById('financial-summary-year-select').addEventListener('change', loadFinancialSummary);

async function loadFinancialSummary() {
    const year = document.getElementById('financial-summary-year-select').value;
    const res = await fetch(`/api/accounting/summary?year=${year}`);
    if (!res.ok) return;
    const data = await res.json();

    const quartersBox = document.getElementById('financial-summary-quarters');
    quartersBox.innerHTML = `<div class="financial-summary-quarter-grid">${data.quarters.map((q) => `
        <div class="financial-summary-quarter-tile">
            <span class="financial-summary-quarter-label">Q${q.quarter} <span class="save-note">(${QUARTER_MONTHS[q.quarter - 1]})</span></span>
            <span class="financial-summary-quarter-amount">${usdFmt.format(q.grossAmount)}</span>
            <span class="save-note">${q.gigCount} gig${q.gigCount === 1 ? '' : 's'}</span>
        </div>`).join('')}</div>`;

    document.getElementById('financial-summary-total').textContent =
        `${data.year} total: ${usdFmt.format(data.totalGross)} across ${data.gigCount} gig${data.gigCount === 1 ? '' : 's'}`;

    DataGrid.render(document.getElementById('financial-summary-members-grid'), {
        columns: [
            { key: 'name', label: 'Member', render: (m) => escapeHtmlAccounting(m.name) },
            { key: 'percentage', label: 'Current %', render: (m) => `${m.percentage}%` },
            { key: 'totalReceived', label: 'Total', sortValue: (m) => m.totalReceived, render: (m) => usdFmt.format(m.totalReceived) }
        ],
        rows: data.byMember, getRowId: (m) => m.userId,
        searchable: false, pageSize: 1000, emptyMessage: 'No payout recipients set up yet.'
    });
}

// --- Payout Terms (D7) ---
async function loadPayoutTerms() {
    const res = await fetch('/api/accounting/payout-terms');
    const data = res.ok ? await res.json() : { note: null };
    document.getElementById('payout-terms-textarea').value = data.note || '';
}
document.getElementById('payout-terms-save-btn').addEventListener('click', async () => {
    const status = document.getElementById('payout-terms-status');
    status.textContent = 'Saving...';
    const res = await fetch('/api/accounting/payout-terms', {
        method: 'PUT', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ note: document.getElementById('payout-terms-textarea').value })
    });
    status.textContent = res.ok ? 'Saved.' : 'Could not save.';
});

function populateDefaultSelects(data) {
    const form = document.getElementById('payout-defaults-form');
    const optionsHtml = '<option value="">(none selected)</option>' +
        bandMembersCache.map((m) => `<option value="${m.userId}">${escapeHtmlAccounting(memberName(m))}</option>`).join('');
    form.defaultGigPayeeUserId.innerHTML = optionsHtml;
    form.defaultMerchPayeeUserId.innerHTML = optionsHtml;
    form.defaultGigPayeeUserId.value = data.defaultGigPayeeUserId || '';
    form.defaultMerchPayeeUserId.value = data.defaultMerchPayeeUserId || '';
}

document.getElementById('payout-defaults-form').addEventListener('submit', async (e) => {
    e.preventDefault();
    const form = e.target;
    const status = document.getElementById('payout-defaults-status');
    const res = await fetch('/api/accounting/receivables/defaults', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
            defaultGigPayeeUserId: form.defaultGigPayeeUserId.value || null,
            defaultMerchPayeeUserId: form.defaultMerchPayeeUserId.value || null
        })
    });
    const body = await res.json().catch(() => ({}));
    status.textContent = res.ok ? 'Saved.' : (body.error || 'Could not save.');
});

function renderRosterCheckboxes() {
    const container = document.getElementById('payout-roster-checkboxes');
    const selectedIds = new Set(recipientsCache.map((r) => r.userId));
    container.innerHTML = bandMembersCache.map((m) => `
        <label><input type="checkbox" value="${m.userId}" ${selectedIds.has(m.userId) ? 'checked' : ''}> ${escapeHtmlAccounting(memberName(m))}</label>
    `).join('');
}

document.getElementById('payout-roster-save-btn').addEventListener('click', async () => {
    const status = document.getElementById('payout-roster-status');
    const checked = [...document.querySelectorAll('#payout-roster-checkboxes input:checked')].map((i) => i.value);
    const res = await fetch('/api/accounting/receivables/roster', {
        method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ userIds: checked })
    });
    if (!res.ok) { status.textContent = 'Could not save selection.'; return; }
    status.textContent = 'Saved.';
    const refreshed = await fetch('/api/accounting/receivables').then((r) => r.json());
    recipientsCache = refreshed.recipients;
    renderPercentagesGrid();
});

function renderPercentagesGrid() {
    const container = document.getElementById('payout-percentages-grid');
    const columns = [
        { key: 'firstName', label: 'First name', sortable: false },
        { key: 'lastName', label: 'Last name', sortable: false },
        { key: 'email', label: 'Email', sortable: false },
        {
            key: 'percentage', label: '%', sortable: false,
            render: (r) => `<input type="number" min="0" max="100" step="0.01" class="payout-percentage-input" data-user-id="${r.userId}" value="${r.percentage}">`
        }
    ];
    DataGrid.render(container, {
        columns, rows: recipientsCache, getRowId: (r) => r.userId,
        searchable: false, pageSize: 1000, emptyMessage: 'No one selected yet - pick members above.'
    });
    recomputeRemaining();
}

// Delegated on the stable container (registered once, not re-wired per
// render) so it survives DataGrid rebuilding its own tbody on every
// setRows/sort call - see userAvatar.js's own click-delegation for the
// same reasoning.
document.getElementById('payout-percentages-grid').addEventListener('input', (e) => {
    if (e.target.classList.contains('payout-percentage-input')) recomputeRemaining();
});

function recomputeRemaining() {
    const inputs = [...document.querySelectorAll('.payout-percentage-input')];
    const total = inputs.reduce((sum, inp) => sum + (parseFloat(inp.value) || 0), 0);
    const remaining = Math.round((100 - total) * 100) / 100;
    const saveBtn = document.getElementById('payout-percentages-save-btn');
    const label = document.getElementById('payout-percentages-remaining');
    const balanced = Math.abs(remaining) < 0.005;
    saveBtn.disabled = !balanced || inputs.length === 0;
    label.className = 'payout-remaining-note ' + (remaining >= 0 ? 'remaining' : 'overallocated');
    label.textContent = inputs.length === 0 ? '' : (remaining >= 0 ? `Remaining: ${remaining}%` : `Overallocated: ${Math.abs(remaining)}%`);
}

document.getElementById('payout-percentages-save-btn').addEventListener('click', async () => {
    const status = document.getElementById('payout-percentages-status');
    const inputs = [...document.querySelectorAll('.payout-percentage-input')];
    const percentages = inputs.map((inp) => ({ userId: inp.dataset.userId, percentage: parseFloat(inp.value) || 0 }));
    const res = await fetch('/api/accounting/receivables/percentages', {
        method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ percentages })
    });
    const body = await res.json().catch(() => ({}));
    if (!res.ok) { status.textContent = body.error || 'Could not save.'; return; }
    status.textContent = 'Saved.';
    recipientsCache = recipientsCache.map((r) => {
        const match = percentages.find((p) => p.userId === r.userId);
        return match ? { ...r, percentage: match.percentage } : r;
    });
});

loadAccounting();
