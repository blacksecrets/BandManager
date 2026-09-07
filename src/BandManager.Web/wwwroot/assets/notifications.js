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

async function loadNotifications() {
    const res = await fetch('/api/notifications');
    if (!res.ok) return;
    const notifications = await res.json();
    const tbody = document.getElementById('notifications-body');
    tbody.innerHTML = '';
    for (const n of notifications) {
        const tr = document.createElement('tr');
        if (!n.isRead) tr.className = 'notification-unread';
        tr.innerHTML = `
            <td>${n.isRead ? '' : '●'}</td>
            <td>${escapeHtml(n.songTitle ? `${n.songTitle}: ` : '')}${escapeHtml(n.message)}</td>
            <td>${timeAgo(n.createdAt)}</td>
        `;
        tr.addEventListener('click', async () => {
            if (n.isRead) return;
            await fetch(`/api/notifications/${n.id}/read`, { method: 'POST' });
            n.isRead = true;
            tr.className = '';
            tr.firstElementChild.textContent = '';
            window.dispatchEvent(new CustomEvent('notif-changed'));
        });
        tbody.appendChild(tr);
    }
    if (notifications.length === 0) tbody.innerHTML = '<tr><td colspan="3">No notifications yet.</td></tr>';
}

init();
