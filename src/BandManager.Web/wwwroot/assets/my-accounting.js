// "My <band>" > Accounting - a member's own payout status across every
// gig in the active band, read-only. See AccountingController.GetMyPayouts.
function escapeHtmlMyAccounting(str) {
    const div = document.createElement('div');
    div.textContent = str == null ? '' : String(str);
    return div.innerHTML;
}

async function loadMyAccounting() {
    const me = await fetch('/api/profile/me').then((r) => r.json());
    const noBand = document.getElementById('my-accounting-no-band');
    const content = document.getElementById('my-accounting-content');
    if (!me.activeBandRole) {
        noBand.hidden = false;
        content.hidden = true;
        return;
    }
    noBand.hidden = true;
    content.hidden = false;

    const termsRes = await fetch('/api/accounting/payout-terms');
    const terms = termsRes.ok ? await termsRes.json() : { note: null };
    const termsBox = document.getElementById('my-payout-terms-content');
    if (terms.note) {
        document.getElementById('my-payout-terms-note').textContent = terms.note;
        termsBox.hidden = false;
    } else {
        termsBox.hidden = true;
    }

    const res = await fetch('/api/accounting/my-payouts');
    const rows = res.ok ? await res.json() : [];

    const columns = [
        { key: 'date', label: 'Date', sortValue: (r) => r.date, render: (r) => r.date },
        { key: 'gig', label: 'Gig', searchValue: (r) => r.gigTitle, render: (r) => escapeHtmlMyAccounting(r.gigTitle) },
        { key: 'amount', label: 'Your amount', sortValue: (r) => r.amount ?? -1, searchable: false, render: (r) => r.amount != null ? `$${r.amount.toFixed(2)}` : '—' },
        { key: 'paidType', label: 'Paid by', searchable: false, render: (r) => r.payoutType || '—' },
        { key: 'isPaid', label: 'Status', searchable: false, render: (r) => r.isPaid ? '<span class="status-badge configured">Paid</span>' : '<span class="status-badge not-configured">Not yet paid</span>' }
    ];

    window.DataGrid.render(document.getElementById('my-payouts-grid'), {
        columns,
        rows,
        getRowId: (r) => r.gigRef,
        defaultSortKey: 'date',
        defaultSortDir: 'desc',
        emptyMessage: 'No payout history yet for this band.'
    });
}

loadMyAccounting();
