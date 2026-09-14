// Compose modal - send a Notification, a real email, or both, to one or
// more band members. Field layout modeled on Gmail's Compose (To/Subject/
// Body), but the window itself uses the shared ModalShell so it resizes/
// maximizes/minimizes-to-bubble exactly like Band Chat's own modal, per
// Richard's explicit request to follow that behavior rather than Gmail's
// own minimize-to-bar. Self-injected by topNav.js the same way chat.js is.
window.ComposeWidget = (function () {
    let me = null;
    let bandMembersCache = [];
    let shell = null;
    let selectedRecipients = new Map(); // userId -> name

    function escapeHtml(str) {
        const div = document.createElement('div');
        div.textContent = str == null ? '' : String(str);
        return div.innerHTML;
    }

    function buildBody() {
        shell.bodyEl.innerHTML = `
            <div class="compose-to-row">
                <span class="compose-field-label">To</span>
                <div class="compose-to-chips">
                    <input type="text" class="compose-to-input" placeholder="Add a band member...">
                </div>
                <div class="compose-to-menu" hidden></div>
            </div>
            <input type="text" class="compose-subject-input" placeholder="Subject (optional)">
            <textarea class="compose-body-textarea" placeholder="Write your message..." maxlength="5000"></textarea>
            <div class="compose-options-row">
                <label class="compose-option"><input type="checkbox" class="compose-send-notification" checked> Notification</label>
                <label class="compose-option"><input type="checkbox" class="compose-send-email" checked> Email</label>
            </div>
            <p class="compose-status"></p>
            <div class="compose-send-row">
                <button type="button" class="compose-send-btn">Send</button>
            </div>
        `;
        wireToField();
        wireSend();
    }

    function renderChips() {
        const chipsBox = shell.bodyEl.querySelector('.compose-to-chips');
        const input = chipsBox.querySelector('.compose-to-input');
        [...chipsBox.querySelectorAll('.compose-to-chip')].forEach((c) => c.remove());
        for (const [userId, name] of selectedRecipients) {
            const chip = document.createElement('span');
            chip.className = 'compose-to-chip';
            chip.innerHTML = `${escapeHtml(name)}<button type="button" data-user-id="${userId}">&times;</button>`;
            chipsBox.insertBefore(chip, input);
        }
        chipsBox.querySelectorAll('.compose-to-chip button').forEach((btn) => {
            btn.addEventListener('click', () => { selectedRecipients.delete(btn.dataset.userId); renderChips(); });
        });
    }

    function wireToField() {
        const input = shell.bodyEl.querySelector('.compose-to-input');
        const menu = shell.bodyEl.querySelector('.compose-to-menu');

        function updateMenu() {
            const query = input.value.trim().toLowerCase();
            const candidates = bandMembersCache.filter((m) => !selectedRecipients.has(m.id)
                && (query === '' || m.firstName.toLowerCase().includes(query)));
            if (candidates.length === 0) { menu.hidden = true; return; }
            menu.innerHTML = candidates.map((m) => `<div class="compose-to-item" data-user-id="${m.id}" data-name="${escapeHtml(m.firstName)}">${escapeHtml(m.firstName)}</div>`).join('');
            menu.hidden = false;
            menu.querySelectorAll('.compose-to-item').forEach((item) => {
                item.addEventListener('click', () => {
                    selectedRecipients.set(item.dataset.userId, item.dataset.name);
                    input.value = '';
                    renderChips();
                    menu.hidden = true;
                    input.focus();
                });
            });
        }

        input.addEventListener('focus', updateMenu);
        input.addEventListener('input', updateMenu);
        input.addEventListener('keydown', (e) => {
            if (e.key === 'Backspace' && input.value === '' && selectedRecipients.size > 0) {
                const lastKey = [...selectedRecipients.keys()].pop();
                selectedRecipients.delete(lastKey);
                renderChips();
            }
        });
        document.addEventListener('click', (e) => {
            if (!shell.bodyEl.contains(e.target)) return;
            if (!e.target.closest('.compose-to-row')) menu.hidden = true;
        });
    }

    function wireSend() {
        shell.bodyEl.querySelector('.compose-send-btn').addEventListener('click', async () => {
            const statusEl = shell.bodyEl.querySelector('.compose-status');
            const subject = shell.bodyEl.querySelector('.compose-subject-input').value.trim();
            const body = shell.bodyEl.querySelector('.compose-body-textarea').value.trim();
            const sendNotification = shell.bodyEl.querySelector('.compose-send-notification').checked;
            const sendEmail = shell.bodyEl.querySelector('.compose-send-email').checked;

            if (selectedRecipients.size === 0) { statusEl.textContent = 'Add at least one recipient.'; return; }
            if (!body) { statusEl.textContent = 'Write a message first.'; return; }
            if (!sendNotification && !sendEmail) { statusEl.textContent = 'Choose Notification, Email, or both.'; return; }

            statusEl.textContent = 'Sending...';
            const res = await fetch('/api/notifications/send', {
                method: 'POST', headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ recipientUserIds: [...selectedRecipients.keys()], subject: subject || null, body, sendNotification, sendEmail })
            });
            const result = await res.json().catch(() => ({}));
            if (!res.ok) { statusEl.textContent = result.error || 'Could not send.'; return; }

            selectedRecipients = new Map();
            shell.bodyEl.querySelector('.compose-subject-input').value = '';
            shell.bodyEl.querySelector('.compose-body-textarea').value = '';
            statusEl.textContent = ''; // otherwise "Sending..." sits there forever - never overwritten on success, only on a later failure
            renderChips();
            shell.close();
        });
    }

    async function toggleModal() {
        if (!me || !me.activeBandId) return;
        if (!shell) {
            shell = window.ModalShell.create({ title: 'Compose', storageKey: 'composeModalSize', bubbleIcon: '✏️' });
            buildBody();
        }
        if (shell.isOpen()) { shell.close(); return; }
        if (bandMembersCache.length === 0) {
            bandMembersCache = await fetch('/api/profile/band-members').then((r) => (r.ok ? r.json() : []));
        }
        shell.open();
        shell.bodyEl.querySelector('.compose-to-input').focus();
    }

    function init(meObj) {
        me = meObj;
    }

    return { init, toggleModal };
})();
