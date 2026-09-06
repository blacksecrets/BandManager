async function loadSuperAdminPage() {
    const res = await fetch('/api/profile/me');
    const me = await res.json();
    if (!me.isSuperAdmin) {
        document.getElementById('superadmin-denied').hidden = false;
        return;
    }

    document.getElementById('superadmin-bands-section').hidden = false;
    document.getElementById('superadmin-users-section').hidden = false;
    document.getElementById('song-search-section').hidden = false;
    document.getElementById('admin-branding-section').hidden = false;

    loadAllUsersPicker();
    // Awaited before loadAllUsers - the manage-row's "add to a band" picker
    // reads allBandsCache, which loadSuperAdminBands populates.
    await loadSuperAdminBands();
    loadAllUsers();
    loadSongSearchCredentials();
    loadBranding();
}

// --- Song search credentials (YouTube + Spotify) ---
async function loadSongSearchCredentials() {
    const res = await fetch('/api/superadmin/song-search-credentials');
    if (!res.ok) return;
    const { youTube, spotify } = await res.json();

    const youTubeBadge = document.getElementById('youtube-status-badge');
    const youTubeForm = document.getElementById('youtube-cred-form');
    if (youTube) {
        youTubeBadge.textContent = 'Configured';
        youTubeBadge.classList.add('configured');
        youTubeForm.apiKey.value = youTube.apiKey || '';
        document.getElementById('youtube-disconnect-tool').hidden = false;
    } else {
        youTubeBadge.textContent = 'Not configured';
        youTubeBadge.classList.remove('configured');
        document.getElementById('youtube-disconnect-tool').hidden = true;
    }

    const spotifyBadge = document.getElementById('spotify-status-badge');
    const spotifyForm = document.getElementById('spotify-cred-form');
    if (spotify) {
        spotifyBadge.textContent = 'Configured';
        spotifyBadge.classList.add('configured');
        spotifyForm.clientId.value = spotify.clientId || '';
        spotifyForm.clientSecret.value = spotify.clientSecret || '';
        document.getElementById('spotify-disconnect-tool').hidden = false;
    } else {
        spotifyBadge.textContent = 'Not configured';
        spotifyBadge.classList.remove('configured');
        document.getElementById('spotify-disconnect-tool').hidden = true;
    }
}

document.getElementById('youtube-cred-form').addEventListener('submit', async (e) => {
    e.preventDefault();
    const form = e.target;
    const status = document.getElementById('youtube-cred-status');
    const res = await fetch('/api/superadmin/song-search-credentials/youtube', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ apiKey: form.apiKey.value.trim() })
    });
    const body = await res.json();
    status.textContent = res.ok ? 'Saved.' : (body.error || 'Could not save.');
    if (res.ok) loadSongSearchCredentials();
});

document.getElementById('youtube-disconnect-btn').addEventListener('click', async () => {
    if (!confirm('Remove the YouTube API key? Song search will stop returning YouTube results until a new one is saved.')) return;
    await fetch('/api/superadmin/song-search-credentials/youtube', { method: 'DELETE' });
    document.getElementById('youtube-cred-form').reset();
    loadSongSearchCredentials();
});

document.getElementById('spotify-cred-form').addEventListener('submit', async (e) => {
    e.preventDefault();
    const form = e.target;
    const status = document.getElementById('spotify-cred-status');
    const res = await fetch('/api/superadmin/song-search-credentials/spotify', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ clientId: form.clientId.value.trim(), clientSecret: form.clientSecret.value.trim() })
    });
    const body = await res.json();
    status.textContent = res.ok ? 'Saved.' : (body.error || 'Could not save.');
    if (res.ok) loadSongSearchCredentials();
});

document.getElementById('spotify-disconnect-btn').addEventListener('click', async () => {
    if (!confirm('Remove the Spotify credentials? Song search will stop returning Spotify results until new ones are saved.')) return;
    await fetch('/api/superadmin/song-search-credentials/spotify', { method: 'DELETE' });
    document.getElementById('spotify-cred-form').reset();
    loadSongSearchCredentials();
});

function escapeHtml(str) {
    const div = document.createElement('div');
    div.textContent = str;
    return div.innerHTML;
}

// --- Bands ---
let allBandsCache = [];

async function loadAllUsersPicker() {
    const res = await fetch('/api/superadmin/all-users');
    if (!res.ok) return;
    const users = await res.json();
    const select = document.getElementById('add-band-admin-choice');
    for (const u of users) {
        const opt = document.createElement('option');
        opt.value = u.username;
        opt.textContent = u.username;
        select.appendChild(opt);
    }
}

async function loadSuperAdminBands() {
    const res = await fetch('/api/superadmin/bands');
    if (!res.ok) return;
    const bands = await res.json();
    allBandsCache = bands;
    renderAddUserBandCheckboxes();
    const tbody = document.getElementById('superadmin-bands-body');
    tbody.innerHTML = '';
    for (const b of bands) {
        const tr = document.createElement('tr');
        tr.innerHTML = `
            <td>${b.name}</td>
            <td>${b.slug}</td>
            <td>${new Date(b.createdAt).toLocaleDateString()}</td>
            <td>${b.isArchived ? 'Archived' : 'Active'}</td>
            <td></td>
        `;
        const actionBtn = document.createElement('button');
        actionBtn.className = 'remove-btn';
        actionBtn.textContent = b.isArchived ? 'Unarchive' : 'Archive';
        actionBtn.addEventListener('click', () => toggleBandArchive(b.id, b.name, b.isArchived));
        tr.lastElementChild.appendChild(actionBtn);
        tbody.appendChild(tr);
    }
    if (bands.length === 0) tbody.innerHTML = '<tr><td colspan="5">No bands yet.</td></tr>';
}

async function toggleBandArchive(id, name, isArchived) {
    const verb = isArchived ? 'unarchive' : 'archive';
    const warning = isArchived
        ? `Unarchive ${name}? Its members will be able to select it again.`
        : `Archive ${name}? Its Band Admins and Users will no longer be able to select it (their other bands, if any, are unaffected). Nothing is deleted - you can unarchive it later.`;
    if (!confirm(warning)) return;
    const res = await fetch(`/api/superadmin/bands/${id}/${verb}`, { method: 'POST' });
    const body = await res.json();
    if (!res.ok) {
        alert(body.error || `Could not ${verb} that band.`);
        return;
    }
    loadSuperAdminBands();
}

document.getElementById('add-band-form').addEventListener('submit', async (e) => {
    e.preventDefault();
    const form = e.target;
    const status = document.getElementById('add-band-status');
    const name = form.name.value.trim();
    const slug = name.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/(^-|-$)/g, '');
    const existingAdminUsername = form.adminChoice.value || null;
    const newAdminUsername = form.newAdminUsername.value.trim() || null;

    const res = await fetch('/api/superadmin/bands', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ name, slug, existingAdminUsername, newAdminUsername })
    });
    const result = await res.json();
    if (res.ok) {
        status.textContent = `Added ${name}. Use the band switcher (top right) to switch into it.`;
        form.reset();
        loadSuperAdminBands();
    } else {
        status.textContent = result.error || 'Could not create band.';
    }
});

// --- All users ---
async function loadAllUsers() {
    const res = await fetch('/api/superadmin/users');
    if (!res.ok) return;
    const users = await res.json();
    const tbody = document.getElementById('all-users-body');
    tbody.innerHTML = '';
    for (const u of users) {
        const bandsSummary = u.memberships.length
            ? u.memberships.map((m) => `${m.bandName}${m.isArchived ? ' (archived)' : ''} (${m.role})`).join(', ')
            : '—';

        const tr = document.createElement('tr');
        tr.innerHTML = `
            <td>${escapeHtml(u.username)}${u.isSelf ? ' (you)' : ''}</td>
            <td>${u.isSuperAdmin ? 'SuperAdmin' : '—'}</td>
            <td>${escapeHtml(bandsSummary)}</td>
            <td></td>
        `;
        const manageBtn = document.createElement('button');
        manageBtn.className = 'remove-btn';
        manageBtn.textContent = 'Manage';
        tr.lastElementChild.appendChild(manageBtn);
        tbody.appendChild(tr);

        const detailTr = document.createElement('tr');
        detailTr.className = 'user-manage-row';
        detailTr.hidden = true;
        const detailTd = document.createElement('td');
        detailTd.colSpan = 4;
        detailTr.appendChild(detailTd);
        tbody.appendChild(detailTr);

        manageBtn.addEventListener('click', () => {
            const opening = detailTr.hidden;
            detailTr.hidden = !opening;
            manageBtn.textContent = opening ? 'Close' : 'Manage';
            if (opening) renderUserManage(detailTd, u);
        });
    }
    if (users.length === 0) tbody.innerHTML = '<tr><td colspan="4">No users yet.</td></tr>';
}

function renderUserManage(container, u) {
    container.innerHTML = '';
    const block = document.createElement('div');
    block.className = 'user-manage-block';

    // --- SuperAdmin toggle ---
    const superRow = document.createElement('div');
    superRow.className = 'membership-row';
    const superBtn = document.createElement('button');
    superBtn.className = 'remove-btn';
    superBtn.textContent = u.isSuperAdmin ? 'Remove SuperAdmin access' : 'Make SuperAdmin';
    superBtn.disabled = u.isSelf;
    superBtn.title = u.isSelf ? "You can't change your own SuperAdmin access." : '';
    superBtn.addEventListener('click', async () => {
        const next = !u.isSuperAdmin;
        if (!confirm(next ? `Make ${u.username} a SuperAdmin?` : `Remove ${u.username}'s SuperAdmin access? They keep any band memberships they already have.`)) return;
        const res = await fetch(`/api/superadmin/users/${u.id}/superadmin`, {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ isSuperAdmin: next })
        });
        const body = await res.json();
        if (!res.ok) { alert(body.error || 'Could not change SuperAdmin access.'); return; }
        loadAllUsers();
    });
    superRow.appendChild(superBtn);
    block.appendChild(superRow);

    // --- Existing band memberships ---
    const membershipsHeading = document.createElement('h4');
    membershipsHeading.textContent = 'Band memberships';
    block.appendChild(membershipsHeading);

    if (u.memberships.length === 0) {
        const none = document.createElement('p');
        none.className = 'save-note';
        none.textContent = 'Not a member of any band yet.';
        block.appendChild(none);
    }
    for (const m of u.memberships) {
        const row = document.createElement('div');
        row.className = 'membership-row';

        const label = document.createElement('span');
        label.textContent = m.bandName;
        row.appendChild(label);
        if (m.isArchived) {
            const tag = document.createElement('span');
            tag.className = 'archived-tag';
            tag.textContent = '(archived)';
            row.appendChild(tag);
        }

        const roleSelect = document.createElement('select');
        roleSelect.innerHTML = `<option value="User">User</option><option value="BandAdmin">Band Admin</option>`;
        roleSelect.value = m.role;
        roleSelect.addEventListener('change', async () => {
            const res = await fetch(`/api/superadmin/users/${u.id}/bands/${m.bandId}`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ role: roleSelect.value })
            });
            const body = await res.json();
            if (!res.ok) {
                alert(body.error || 'Could not change role.');
                roleSelect.value = m.role;
                return;
            }
            loadAllUsers();
        });
        row.appendChild(roleSelect);

        const removeBtn = document.createElement('button');
        removeBtn.className = 'remove-btn';
        removeBtn.textContent = 'Remove from band';
        removeBtn.addEventListener('click', async () => {
            if (!confirm(`Remove ${u.username} from ${m.bandName}?`)) return;
            const res = await fetch(`/api/superadmin/users/${u.id}/bands/${m.bandId}`, { method: 'DELETE' });
            const body = await res.json();
            if (!res.ok) { alert(body.error || 'Could not remove from band.'); return; }
            loadAllUsers();
        });
        row.appendChild(removeBtn);

        block.appendChild(row);
    }

    // --- Add to another band (non-SuperAdmin only, per the request - a
    // SuperAdmin sees/manages every band without needing a membership) ---
    if (!u.isSuperAdmin) {
        const memberBandIds = new Set(u.memberships.map((m) => m.bandId));
        const available = allBandsCache.filter((b) => !b.isArchived && !memberBandIds.has(b.id));
        if (available.length > 0) {
            const addHeading = document.createElement('h4');
            addHeading.textContent = 'Add to a band';
            block.appendChild(addHeading);

            const addRow = document.createElement('div');
            addRow.className = 'add-membership-row';
            const bandSelect = document.createElement('select');
            for (const b of available) {
                const opt = document.createElement('option');
                opt.value = b.id;
                opt.textContent = b.name;
                bandSelect.appendChild(opt);
            }
            const roleSelect = document.createElement('select');
            roleSelect.innerHTML = `<option value="User">User</option><option value="BandAdmin">Band Admin</option>`;
            const addBtn = document.createElement('button');
            addBtn.className = 'remove-btn';
            addBtn.textContent = 'Add';
            addBtn.addEventListener('click', async () => {
                const res = await fetch(`/api/superadmin/users/${u.id}/bands`, {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ bandId: bandSelect.value, role: roleSelect.value })
                });
                const body = await res.json();
                if (!res.ok) { alert(body.error || 'Could not add to that band.'); return; }
                loadAllUsers();
            });
            addRow.appendChild(bandSelect);
            addRow.appendChild(roleSelect);
            addRow.appendChild(addBtn);
            block.appendChild(addRow);
        }
    }

    // --- Delete account ---
    const deleteRow = document.createElement('div');
    deleteRow.className = 'membership-row';
    const deleteBtn = document.createElement('button');
    deleteBtn.className = 'remove-btn';
    deleteBtn.textContent = 'Delete account';
    deleteBtn.disabled = u.isSelf;
    deleteBtn.title = u.isSelf ? "You can't delete your own account." : '';
    deleteBtn.addEventListener('click', async () => {
        if (!confirm(`Permanently delete ${u.username}'s account? This removes every band membership they have. This can't be undone.`)) return;
        const res = await fetch(`/api/superadmin/users/${u.id}`, { method: 'DELETE' });
        const body = await res.json();
        if (!res.ok) { alert(body.error || 'Could not delete that account.'); return; }
        loadAllUsers();
    });
    deleteRow.appendChild(deleteBtn);
    block.appendChild(deleteRow);

    container.appendChild(block);
}

function renderAddUserBandCheckboxes() {
    const container = document.getElementById('add-user-bands-checkboxes');
    if (!container) return;
    container.innerHTML = '';
    const list = document.createElement('div');
    list.className = 'band-checkbox-list';
    for (const b of allBandsCache.filter((b) => !b.isArchived)) {
        const label = document.createElement('label');
        label.innerHTML = `<input type="checkbox" value="${b.id}"> ${escapeHtml(b.name)}`;
        list.appendChild(label);
    }
    container.appendChild(list);
}

const addUserLevelSelect = document.getElementById('add-user-level');
const addUserBandsPicker = document.getElementById('add-user-bands-picker');
function updateAddUserBandsVisibility() {
    addUserBandsPicker.hidden = addUserLevelSelect.value === 'SuperAdmin';
}
addUserLevelSelect.addEventListener('change', updateAddUserBandsVisibility);
updateAddUserBandsVisibility();

document.getElementById('add-user-form').addEventListener('submit', async (e) => {
    e.preventDefault();
    const form = e.target;
    const status = document.getElementById('add-user-status');
    const username = form.username.value.trim();
    const level = addUserLevelSelect.value;
    const isSuperAdmin = level === 'SuperAdmin';
    const role = level === 'BandAdmin' ? 'BandAdmin' : 'User';

    const memberships = isSuperAdmin ? [] : [...document.querySelectorAll('#add-user-bands-checkboxes input:checked')]
        .map((cb) => ({ bandId: cb.value, role }));

    const res = await fetch('/api/superadmin/users', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ username, isSuperAdmin, memberships })
    });
    const result = await res.json();
    if (res.ok) {
        status.textContent = `Added ${username} - a temporary password was sent to them.`;
        form.reset();
        updateAddUserBandsVisibility();
        loadAllUsers();
    } else {
        status.textContent = result.error || 'Could not add user.';
    }
});

// --- Platform branding (BandManager shell, moved from profile.js) ---
async function loadBranding() {
    const res = await fetch('/api/profile/branding');
    const { logoUrl, backgroundUrl, faviconUrl } = await res.json();
    setBrandingPreview('logo', logoUrl);
    setBrandingPreview('background', backgroundUrl);
    setBrandingPreview('favicon', faviconUrl);
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

for (const type of ['logo', 'background', 'favicon']) {
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

loadSuperAdminPage();
