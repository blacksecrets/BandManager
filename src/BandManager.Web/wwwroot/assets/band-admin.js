async function loadBandAdmin() {
    const res = await fetch('/api/profile/me');
    const me = await res.json();
    const noBandEl = document.getElementById('band-admin-no-band');

    if (!me.isAdmin) {
        noBandEl.textContent = 'Band Admin access is required to view this page.';
        noBandEl.hidden = false;
        return;
    }

    const hasBand = !!me.activeBandRole;
    noBandEl.textContent = 'Select a band from the switcher above to manage it.';
    noBandEl.hidden = hasBand;
    document.getElementById('band-repertoire-import-section').hidden = !hasBand;
    document.getElementById('band-branding-section').hidden = !hasBand;
    document.getElementById('band-users-section').hidden = !hasBand;
    if (!hasBand) return;

    loadBandBranding();
    loadBandRoleOptions().then(loadUsers);
}

// --- Branding (this band's own logo/background/favicon/accent color) ---
async function loadBandBranding() {
    const res = await fetch('/api/band-admin/branding');
    if (!res.ok) return;
    const { logoUrl, backgroundUrl, faviconUrl, accentColor } = await res.json();
    setBandAssetPreview('logo', logoUrl);
    setBandAssetPreview('background', backgroundUrl);
    setBandAssetPreview('favicon', faviconUrl);
    document.getElementById('band-accent-color').value = accentColor || '';
}

function setBandAssetPreview(type, url) {
    const preview = document.getElementById(`band-${type}-preview`);
    const removeBtn = document.getElementById(`band-${type}-remove`);
    if (url) {
        preview.innerHTML = `<img src="${url}?t=${Date.now()}" alt="${type}">`;
        removeBtn.hidden = false;
    } else {
        preview.textContent = `No ${type} set`;
        removeBtn.hidden = true;
    }
}

for (const type of ['logo', 'background', 'favicon']) {
    const input = document.getElementById(`band-${type}-upload`);
    const removeBtn = document.getElementById(`band-${type}-remove`);

    input.addEventListener('change', async () => {
        if (!input.files[0]) return;
        const form = new FormData();
        form.append('file', input.files[0]);
        const status = document.getElementById('band-branding-status');
        status.textContent = `Uploading ${type}...`;
        const res = await fetch(`/api/band-admin/branding/${type}`, { method: 'POST', body: form });
        const body = await res.json();
        if (res.ok) {
            status.textContent = `${type[0].toUpperCase()}${type.slice(1)} updated.`;
            setBandAssetPreview(type, body.url);
        } else {
            status.textContent = body.error || `Could not upload ${type}.`;
        }
        input.value = '';
    });

    removeBtn.addEventListener('click', async () => {
        if (!confirm(`Remove this band's ${type}?`)) return;
        const res = await fetch(`/api/band-admin/branding/${type}`, { method: 'DELETE' });
        if (res.ok) setBandAssetPreview(type, null);
    });
}

document.getElementById('band-accent-save').addEventListener('click', async () => {
    const status = document.getElementById('band-branding-status');
    const color = document.getElementById('band-accent-color').value.trim();
    const res = await fetch('/api/band-admin/branding/accent-color', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ color })
    });
    const body = await res.json();
    status.textContent = res.ok ? 'Accent color saved.' : (body.error || 'Could not save accent color.');
});

// --- User management (moved from profile.js) ---
let bandRoleOptions = [];

async function loadBandRoleOptions() {
    const res = await fetch('/api/profile/band-roles');
    bandRoleOptions = res.ok ? await res.json() : [];
    const box = document.getElementById('add-user-roles');
    box.innerHTML = bandRoleOptions.map((r) => `<label><input type="checkbox" name="roles" value="${r}"> ${r}</label>`).join('');
}

async function loadUsers() {
    const res = await fetch('/api/profile/users');
    if (!res.ok) return;
    const users = await res.json();

    const meRes = await fetch('/api/profile/me');
    const me = await meRes.json();

    const tbody = document.getElementById('user-table-body');
    tbody.innerHTML = '';
    for (const user of users) {
        const isSelf = user.username === me.username;
        const tr = document.createElement('tr');
        tr.innerHTML = `
            <td>${user.username}${isSelf ? ' (you)' : ''}</td>
            <td>${user.is_admin ? 'Admin' : 'User'}</td>
            <td class="user-roles-cell">${(user.roles || []).join(', ') || '—'}</td>
            <td>${user.email_confirmed ? 'Verified' : 'Unverified'}</td>
            <td>${user.created_at}</td>
            <td></td>
        `;
        const editRolesBtn = document.createElement('button');
        editRolesBtn.className = 'remove-btn';
        editRolesBtn.textContent = 'Edit roles';
        editRolesBtn.addEventListener('click', () => openEditRolesModal(user));
        tr.lastElementChild.appendChild(editRolesBtn);
        if (!user.email_confirmed) {
            const verifyBtn = document.createElement('button');
            verifyBtn.className = 'remove-btn';
            verifyBtn.textContent = 'Verify email';
            verifyBtn.addEventListener('click', () => verifyUserEmail(user.id));
            tr.lastElementChild.appendChild(verifyBtn);
        }
        if (!isSelf) {
            const delBtn = document.createElement('button');
            delBtn.className = 'remove-btn';
            delBtn.textContent = 'Remove';
            delBtn.addEventListener('click', () => removeUser(user.id, user.username));
            tr.lastElementChild.appendChild(delBtn);
        }
        tbody.appendChild(tr);
    }
}

function closeEditRolesModal() { document.getElementById('edit-roles-modal-backdrop').hidden = true; }
document.getElementById('edit-roles-modal-close').addEventListener('click', closeEditRolesModal);
document.getElementById('edit-roles-modal-backdrop').addEventListener('click', (e) => { if (e.target.id === 'edit-roles-modal-backdrop') closeEditRolesModal(); });

function openEditRolesModal(user) {
    const box = document.getElementById('edit-roles-list');
    const current = new Set(user.roles || []);
    box.innerHTML = bandRoleOptions.map((r) =>
        `<label><input type="checkbox" name="roles" value="${r}" ${current.has(r) ? 'checked' : ''}> ${r}</label>`
    ).join('');
    document.getElementById('edit-roles-save-btn').onclick = async () => {
        const roles = [...box.querySelectorAll('input:checked')].map((i) => i.value);
        await fetch(`/api/profile/users/${user.id}/roles`, {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ roles })
        });
        closeEditRolesModal();
        loadUsers();
    };
    document.getElementById('edit-roles-modal-backdrop').hidden = false;
}

async function verifyUserEmail(id) {
    const res = await fetch(`/api/profile/users/${id}/verify-email`, { method: 'POST' });
    const body = await res.json();
    if (!res.ok) {
        alert(body.error || 'Could not verify email.');
        return;
    }
    loadUsers();
}

async function removeUser(id, username) {
    if (!confirm(`Remove ${username}? They'll no longer be able to log in.`)) return;
    const res = await fetch(`/api/profile/users/${id}`, { method: 'DELETE' });
    const body = await res.json();
    if (!res.ok) {
        alert(body.error || 'Could not remove user.');
        return;
    }
    loadUsers();
}

document.getElementById('add-user-form').addEventListener('submit', async (e) => {
    e.preventDefault();
    const form = e.target;
    const status = document.getElementById('add-user-status');
    const username = form.username.value.trim();

    const roles = [...form.querySelectorAll('input[name="roles"]:checked')].map((i) => i.value);
    const res = await fetch('/api/profile/users', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ username, role: form.isAdmin.checked ? 'BandAdmin' : 'User', roles })
    });
    const result = await res.json();
    if (res.ok) {
        status.textContent = `Added ${username}. If that's a brand-new account, a temporary password was sent to them.`;
        form.reset();
        loadUsers();
    } else {
        status.textContent = result.error || 'Could not add user.';
    }
});

// --- Repertoire CSV import ---
function escapeHtml(str) {
    const div = document.createElement('div');
    div.textContent = str ?? '';
    return div.innerHTML;
}

document.getElementById('repertoire-csv-upload').addEventListener('change', async () => {
    const input = document.getElementById('repertoire-csv-upload');
    if (!input.files[0]) return;
    const status = document.getElementById('repertoire-import-status');
    const errorsBox = document.getElementById('repertoire-import-errors');
    errorsBox.innerHTML = '';
    status.textContent = 'Importing...';

    const form = new FormData();
    form.append('file', input.files[0]);

    const res = await fetch('/api/repertoire/import', { method: 'POST', body: form });
    const body = await res.json();
    input.value = '';

    if (res.ok) {
        status.textContent = `${body.newSongsAdded} new song(s) added (pending review), ${body.changesSubmittedForReview} change(s) submitted for review, ${body.unchanged} already matched exactly.` +
            (body.alreadyPendingSkipped ? ` ${body.alreadyPendingSkipped} skipped - already has an edit under review.` : '');
        return;
    }

    status.textContent = body.error || 'Import failed.';
    if (Array.isArray(body.rowErrors) && body.rowErrors.length > 0) {
        const table = document.createElement('table');
        table.className = 'user-table';
        table.innerHTML = '<thead><tr><th>Row</th><th>Column</th><th>Problem</th></tr></thead>';
        const tbody = document.createElement('tbody');
        for (const e of body.rowErrors) {
            const tr = document.createElement('tr');
            tr.innerHTML = `<td>${e.row}</td><td>${escapeHtml(e.column)}</td><td>${escapeHtml(e.message)}</td>`;
            tbody.appendChild(tr);
        }
        table.appendChild(tbody);
        errorsBox.appendChild(table);
    }
});

loadBandAdmin();
