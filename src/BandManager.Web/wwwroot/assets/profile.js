let currentUserId = null;

async function loadMe() {
    const res = await fetch('/api/profile/me');
    const me = await res.json();
    const roleNote = me.isSuperAdmin ? ' (SuperAdmin)' : me.activeBandRole === 'BandAdmin' ? ` (Band Admin of ${me.activeBandName || 'this band'})` : '';
    document.getElementById('whoami').textContent = `Logged in as ${me.username}${roleNote}`;
    document.querySelector('#email-form input[name="email"]').value = me.email || '';

    // Band user management needs an actual active Band (BandAdmin of it,
    // or SuperAdmin) - platform branding is SuperAdmin-only and global,
    // not tied to any one Band, so it's gated separately.
    if (me.isAdmin && me.activeBandRole) {
        document.getElementById('admin-users-section').hidden = false;
        loadUsers();
    }
    if (me.isSuperAdmin) {
        document.getElementById('admin-branding-section').hidden = false;
        document.getElementById('superadmin-bands-section').hidden = false;
        loadBranding();
        loadSuperAdminBands();
    }
}

async function loadSuperAdminBands() {
    const res = await fetch('/api/superadmin/bands');
    if (!res.ok) return;
    const bands = await res.json();
    const tbody = document.getElementById('superadmin-bands-body');
    tbody.innerHTML = bands.map((b) => `
        <tr><td>${b.name}</td><td>${b.slug}</td><td>${new Date(b.createdAt).toLocaleDateString()}</td></tr>
    `).join('') || '<tr><td colspan="3">No bands yet.</td></tr>';
}

// --- Email (needed for forgot-password) ---
document.getElementById('email-form').addEventListener('submit', async (e) => {
    e.preventDefault();
    const form = e.target;
    const status = document.getElementById('email-status');

    const res = await fetch('/api/profile/email', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ email: form.email.value.trim() })
    });
    const body = await res.json();
    status.textContent = res.ok ? 'Email saved.' : (body.error || 'Could not save email.');
});

// --- Password change ---
document.getElementById('password-form').addEventListener('submit', async (e) => {
    e.preventDefault();
    const form = e.target;
    const status = document.getElementById('password-status');
    const currentPassword = form.currentPassword.value;
    const newPassword = form.newPassword.value;
    const confirmPassword = form.confirmPassword.value;

    if (newPassword !== confirmPassword) {
        status.textContent = "New password and confirmation don't match.";
        return;
    }

    const res = await fetch('/api/profile/password', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ currentPassword, newPassword })
    });
    const body = await res.json();
    if (res.ok) {
        status.textContent = 'Password changed.';
        form.reset();
    } else {
        status.textContent = body.error || 'Could not change password.';
    }
});

// --- User management (admin only) ---
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
            <td>${user.created_at}</td>
            <td></td>
        `;
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

const addUserForm = document.getElementById('add-user-form');
if (addUserForm) {
    addUserForm.addEventListener('submit', async (e) => {
        e.preventDefault();
        const form = e.target;
        const status = document.getElementById('add-user-status');

        if (form.password.value && form.password.value !== form.confirmPassword.value) {
            status.textContent = "Password and confirmation don't match.";
            return;
        }

        const body = {
            username: form.username.value,
            password: form.password.value,
            role: form.isAdmin.checked ? 'BandAdmin' : 'User'
        };
        const res = await fetch('/api/profile/users', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(body)
        });
        const result = await res.json();
        if (res.ok) {
            status.textContent = `Added ${body.username}.`;
            form.reset();
            loadUsers();
        } else {
            status.textContent = result.error || 'Could not add user.';
        }
    });
}

// --- Branding (admin only) ---
async function loadBranding() {
    const res = await fetch('/api/profile/branding');
    const { logoUrl, backgroundUrl } = await res.json();
    setBrandingPreview('logo', logoUrl);
    setBrandingPreview('background', backgroundUrl);
}

function setBrandingPreview(type, url) {
    const preview = document.getElementById(`${type}-preview`);
    const removeBtn = document.getElementById(`${type}-remove`);
    if (url) {
        preview.innerHTML = `<img src="${url}?t=${Date.now()}" alt="${type}">`;
        removeBtn.hidden = false;
    } else {
        preview.textContent = `No ${type} set`;
        removeBtn.hidden = true;
    }
}

for (const type of ['logo', 'background']) {
    const input = document.getElementById(`${type}-upload`);
    const removeBtn = document.getElementById(`${type}-remove`);
    const status = document.getElementById('branding-status');

    input.addEventListener('change', async () => {
        if (!input.files[0]) return;
        const form = new FormData();
        form.append('file', input.files[0]);
        status.textContent = `Uploading ${type}...`;
        const res = await fetch(`/api/profile/branding/${type}`, { method: 'POST', body: form });
        const body = await res.json();
        if (res.ok) {
            status.textContent = `${type[0].toUpperCase()}${type.slice(1)} updated.`;
            setBrandingPreview(type, body.url);
        } else {
            status.textContent = body.error || `Could not upload ${type}.`;
        }
        input.value = '';
    });

    removeBtn.addEventListener('click', async () => {
        if (!confirm(`Remove the current ${type}?`)) return;
        const res = await fetch(`/api/profile/branding/${type}`, { method: 'DELETE' });
        if (res.ok) setBrandingPreview(type, null);
    });
}

loadMe();
