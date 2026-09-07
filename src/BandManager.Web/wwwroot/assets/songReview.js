// Shared song-edit-request review modal, used by repertoire.js (clicking a
// catalog row's "under review" badge) and notifications.js (clicking a
// queue item). Self-injecting like bandSwitcher.js/topNav.js, reusing the
// .detail-modal-backdrop/.detail-modal classes already defined in
// dashboard.css rather than a third copy of that CSS - pages using this
// must link dashboard.css.
(function () {
    function escapeHtml(str) {
        const div = document.createElement('div');
        div.textContent = str ?? '';
        return div.innerHTML;
    }

    const backdrop = document.createElement('div');
    backdrop.id = 'review-modal-backdrop';
    backdrop.className = 'detail-modal-backdrop';
    backdrop.hidden = true;
    backdrop.innerHTML = `
        <div class="detail-modal">
            <button type="button" class="detail-modal-close" id="review-modal-close">&times;</button>
            <div id="review-modal-body"></div>
        </div>
    `;
    document.body.appendChild(backdrop);

    function close() { backdrop.hidden = true; }
    document.getElementById('review-modal-close').addEventListener('click', close);
    backdrop.addEventListener('click', (e) => { if (e.target.id === 'review-modal-backdrop') close(); });

    const FIELD_LABELS = {
        Title: 'Title', OriginalArtist: 'Original Artist', Album: 'Album', Key: 'Key',
        LengthSeconds: 'Length (seconds)', YouTubeUrl: 'YouTube URL', SpotifyUrl: 'Spotify URL', SongsterrUrl: 'Songsterr URL'
    };

    // opts: { songId, requestId, isSuperAdmin, onResolved }
    // Non-SuperAdmin (or missing requestId): read-only summary keyed by
    // songId. SuperAdmin with a requestId: full diff + approve/reject.
    window.openReviewSummary = async function openReviewSummary(opts) {
        const body = document.getElementById('review-modal-body');
        body.innerHTML = '<p class="save-note">Loading...</p>';
        backdrop.hidden = false;

        if (opts.isSuperAdmin && opts.requestId) {
            const res = await fetch(`/api/song-edit-requests/${opts.requestId}`);
            if (!res.ok) { body.innerHTML = '<p class="save-note">Could not load this request.</p>'; return; }
            const detail = await res.json();
            renderReviewForm(body, detail, opts.onResolved);
        } else {
            const res = await fetch(`/api/song-edit-requests/${opts.songId}/summary`);
            if (!res.ok) { body.innerHTML = '<p class="save-note">Could not load this request.</p>'; return; }
            const summary = await res.json();
            renderReadOnlySummary(body, summary);
        }
    };

    function renderReadOnlySummary(body, summary) {
        body.innerHTML = `
            <h2>Edit under review</h2>
            <p class="detail-meta">Proposed by ${escapeHtml(summary.requestedByFirstName)} (${escapeHtml(summary.bandName)})</p>
            <p class="detail-example">Fields: ${summary.changedFields.map((f) => escapeHtml(FIELD_LABELS[f] || f)).join(', ')}</p>
        `;
    }

    function renderReviewForm(body, detail, onResolved) {
        body.innerHTML = '';

        const title = document.createElement('h2');
        title.textContent = `Review edit: ${detail.songTitle}`;
        body.appendChild(title);

        const meta = document.createElement('p');
        meta.className = 'detail-meta';
        meta.textContent = `Proposed by ${detail.requestedByFirstName} (${detail.bandName})`;
        body.appendChild(meta);

        const table = document.createElement('table');
        table.className = 'user-table';
        table.innerHTML = '<thead><tr><th>Field</th><th>Current</th><th>Proposed</th></tr></thead>';
        const tbody = document.createElement('tbody');
        for (const c of detail.changedFields) {
            const tr = document.createElement('tr');
            tr.innerHTML = `<td>${escapeHtml(FIELD_LABELS[c.field] || c.field)}</td><td>${escapeHtml(c.oldValue) || '—'}</td><td>${escapeHtml(c.newValue) || '—'}</td>`;
            tbody.appendChild(tr);
        }
        table.appendChild(tbody);
        body.appendChild(table);

        const actions = document.createElement('div');
        actions.className = 'review-actions';

        const approveBtn = document.createElement('button');
        approveBtn.type = 'button';
        approveBtn.textContent = 'Approve...';
        approveBtn.addEventListener('click', () => showResponseForm('approve'));

        const rejectBtn = document.createElement('button');
        rejectBtn.type = 'button';
        rejectBtn.className = 'remove-btn';
        rejectBtn.textContent = 'Reject...';
        rejectBtn.addEventListener('click', () => showResponseForm('reject'));

        actions.appendChild(approveBtn);
        actions.appendChild(rejectBtn);
        body.appendChild(actions);

        function showResponseForm(kind) {
            actions.hidden = true;
            const form = document.createElement('div');
            form.className = 'review-response-form';

            const textarea = document.createElement('textarea');
            textarea.rows = 3;
            if (kind === 'approve') {
                textarea.value = `Your edit for "${detail.songTitle}" has been accepted`;
            } else {
                textarea.maxLength = 250;
            }

            const counter = document.createElement('p');
            counter.className = 'save-note';
            if (kind === 'reject') {
                const updateCounter = () => { counter.textContent = `${textarea.value.length}/250`; };
                textarea.addEventListener('input', updateCounter);
                updateCounter();
            }

            const sendBtn = document.createElement('button');
            sendBtn.type = 'button';
            sendBtn.textContent = 'Send';
            const status = document.createElement('p');
            status.className = 'save-note';

            sendBtn.addEventListener('click', async () => {
                const message = textarea.value.trim();
                if (!message) { status.textContent = 'A message is required.'; return; }
                const res = await fetch(`/api/song-edit-requests/${detail.id}/${kind}`, {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ message })
                });
                const responseBody = await res.json().catch(() => ({}));
                if (!res.ok) { status.textContent = responseBody.error || 'Could not send.'; return; }
                close();
                window.dispatchEvent(new CustomEvent('notif-changed'));
                if (onResolved) onResolved();
            });

            form.appendChild(textarea);
            if (kind === 'reject') form.appendChild(counter);
            form.appendChild(sendBtn);
            form.appendChild(status);
            body.appendChild(form);
        }
    }
})();
