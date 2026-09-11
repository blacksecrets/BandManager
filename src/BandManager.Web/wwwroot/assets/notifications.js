function escapeHtml(str) {
    const div = document.createElement('div');
    div.textContent = str ?? '';
    return div.innerHTML;
}

function timeAgo(iso) {
    return new Date(iso).toLocaleString();
}

async function init() {
    const res = await fetch('/api/profile/me');
    const me = await res.json();

    if (me.isSuperAdmin) {
        document.getElementById('pending-review-section').hidden = false;
        await loadPendingReviews();
    }
    await loadNotifications();
}

let pendingReviewGrid = null;

async function loadPendingReviews() {
    const res = await fetch('/api/song-edit-requests/pending');
    if (!res.ok) return;
    const requests = await res.json();

    async function bulkResolve(url, ids) {
        const message = prompt(`Message to include on all ${ids.length} notification(s):`, '');
        if (message === null) return; // cancelled
        if (!message.trim()) { alert('A message is required.'); return; }
        const bodyRes = await fetch(url, {
            method: 'POST', headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ ids, message: message.trim() })
        });
        const body = await bodyRes.json().catch(() => ({}));
        if (!bodyRes.ok) { alert(body.error || 'Could not resolve those requests.'); return; }
        await loadPendingReviews();
        await loadNotifications();
    }

    const columns = [
        { key: 'songTitle', label: 'Song', render: (r) => escapeHtml(r.songTitle) },
        { key: 'requestedByFirstName', label: 'Proposed by', render: (r) => escapeHtml(r.requestedByFirstName) },
        { key: 'bandName', label: 'Band', render: (r) => escapeHtml(r.bandName) },
        { key: 'createdAt', label: 'Submitted', sortValue: (r) => new Date(r.createdAt).getTime(), render: (r) => timeAgo(r.createdAt) },
        { key: 'review', label: '', sortable: false, searchable: false, render: (r) => `<button type="button" class="pending-review-btn" data-request-id="${r.id}">Review</button>` }
    ];

    if (pendingReviewGrid) {
        pendingReviewGrid.setRows(requests);
    } else {
        const container = document.getElementById('pending-review-grid');
        pendingReviewGrid = window.DataGrid.render(container, {
            columns,
            rows: requests,
            getRowId: (r) => r.id,
            checkboxes: true,
            defaultSortKey: 'createdAt',
            defaultSortDir: 'asc',
            searchPlaceholder: 'Search pending edits...',
            emptyMessage: 'Nothing awaiting review.',
            bulkActions: [
                { label: 'Accept All Checked', onClick: (ids) => bulkResolve('/api/song-edit-requests/bulk-approve', ids) },
                { label: 'Reject All Checked', onClick: (ids) => bulkResolve('/api/song-edit-requests/bulk-reject', ids) }
            ]
        });
        container.addEventListener('click', (e) => {
            const btn = e.target.closest('.pending-review-btn');
            if (!btn) return;
            e.stopPropagation();
            window.openReviewSummary({
                requestId: btn.dataset.requestId, isSuperAdmin: true,
                onResolved: async () => { await loadPendingReviews(); await loadNotifications(); }
            });
        });
    }
}

let notificationsGrid = null;

async function loadNotifications() {
    const res = await fetch('/api/notifications');
    if (!res.ok) return;
    const notifications = await res.json();

    const columns = [
        { key: 'unread', label: '', sortable: false, className: 'notification-dot-col', render: (n) => n.isRead ? '' : '●' },
        {
            key: 'message', label: 'Message',
            searchValue: (n) => `${n.songTitle ? n.songTitle + ': ' : ''}${n.message}`,
            render: (n) => `${escapeHtml(n.songTitle ? `${n.songTitle}: ` : '')}${escapeHtml(n.message)}`
        },
        { key: 'createdAt', label: 'When', sortValue: (n) => new Date(n.createdAt).getTime(), render: (n) => timeAgo(n.createdAt) }
    ];

    async function bulkAction(url, ids) {
        await fetch(url, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ ids }) });
        await loadNotifications();
        window.dispatchEvent(new CustomEvent('notif-changed'));
    }

    if (notificationsGrid) {
        notificationsGrid.setRows(notifications);
    } else {
        notificationsGrid = window.DataGrid.render(document.getElementById('notifications-grid'), {
            columns,
            rows: notifications,
            getRowId: (n) => n.id,
            checkboxes: true,
            defaultSortKey: 'createdAt',
            defaultSortDir: 'desc',
            searchPlaceholder: 'Search notifications...',
            emptyMessage: 'No notifications yet.',
            rowClassName: (n) => n.isRead ? '' : 'notification-unread',
            onRowClick: async (n) => {
                if (n.isRead) return;
                await fetch(`/api/notifications/${n.id}/read`, { method: 'POST' });
                await loadNotifications();
                window.dispatchEvent(new CustomEvent('notif-changed'));
            },
            bulkActions: [
                { label: 'Mark as read', onClick: (ids) => bulkAction('/api/notifications/mark-read', ids) },
                { label: 'Mark as unread', onClick: (ids) => bulkAction('/api/notifications/mark-unread', ids) },
                {
                    label: 'Delete', onClick: async (ids) => {
                        if (!confirm(`Delete ${ids.length} notification(s)? This can't be undone.`)) return;
                        await bulkAction('/api/notifications/delete', ids);
                    }
                }
            ]
        });
    }
}

init();
