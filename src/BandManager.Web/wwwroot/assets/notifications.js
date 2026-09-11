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

async function loadPendingReviews() {
    const res = await fetch('/api/song-edit-requests/pending');
    if (!res.ok) return;
    const requests = await res.json();
    const tbody = document.getElementById('pending-review-body');
    tbody.innerHTML = '';
    for (const r of requests) {
        const tr = document.createElement('tr');
        tr.innerHTML = `
            <td>${escapeHtml(r.songTitle)}</td>
            <td>${escapeHtml(r.requestedByFirstName)}</td>
            <td>${escapeHtml(r.bandName)}</td>
            <td>${timeAgo(r.createdAt)}</td>
            <td></td>
        `;
        const reviewBtn = document.createElement('button');
        reviewBtn.textContent = 'Review';
        reviewBtn.addEventListener('click', () => {
            window.openReviewSummary({ requestId: r.id, isSuperAdmin: true, onResolved: async () => { await loadPendingReviews(); await loadNotifications(); } });
        });
        tr.lastElementChild.appendChild(reviewBtn);
        tbody.appendChild(tr);
    }
    if (requests.length === 0) tbody.innerHTML = '<tr><td colspan="5">Nothing awaiting review.</td></tr>';
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
