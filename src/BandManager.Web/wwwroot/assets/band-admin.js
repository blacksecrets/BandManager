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
    document.getElementById('band-info-section').hidden = !hasBand;
    document.getElementById('band-branding-section').hidden = !hasBand;
    document.getElementById('band-users-section').hidden = !hasBand;
    document.getElementById('band-roles-section').hidden = !hasBand;
    if (!hasBand) return;

    loadBandInfo();
    loadBandBranding();
    loadBandRoleOptions().then(loadUsers);
    loadBandRolesMembers();
}

// --- Band Information (name/phone/mailing address) ---

// Same US-phone formatting as profile.js's contact form - XXX-YYY-ZZZZ,
// flagged inline rather than silently accepted or silently reformatted
// wrong.
function formatBandPhone(raw) {
    let digits = raw.replace(/\D/g, '');
    if (digits.length === 11 && digits.startsWith('1')) digits = digits.slice(1);
    if (digits.length !== 10) return null;
    return `${digits.slice(0, 3)}-${digits.slice(3, 6)}-${digits.slice(6)}`;
}

let timeZonesLoaded = false;
async function loadTimeZoneOptions() {
    if (timeZonesLoaded) return;
    const select = document.getElementById('band-info-form').timeZone;
    const res = await fetch('/api/band-admin/timezones');
    if (!res.ok) return;
    const zones = await res.json();
    timeZonesLoaded = true;
    for (const tz of zones) {
        const opt = document.createElement('option');
        opt.value = tz.id;
        opt.textContent = tz.displayName;
        select.appendChild(opt);
    }
}

async function loadBandInfo() {
    await loadTimeZoneOptions();
    const res = await fetch('/api/band-admin/info');
    if (!res.ok) return;
    const info = await res.json();
    const form = document.getElementById('band-info-form');
    form.name.value = info.name || '';
    form.phone.value = info.phone || '';
    form.addressLine1.value = info.addressLine1 || '';
    form.addressLine2.value = info.addressLine2 || '';
    form.city.value = info.city || '';
    form.state.value = info.state || '';
    form.postalCode.value = info.postalCode || '';
    form.timeZone.value = info.timeZone || 'America/New_York';

    const uspsRes = await fetch('/api/address-lookup/configured');
    const { configured } = uspsRes.ok ? await uspsRes.json() : { configured: false };
    document.getElementById('band-usps-setup-note').hidden = !!configured;
    document.getElementById('band-address-validate-btn').disabled = !configured;
}

document.querySelector('#band-info-form input[name="phone"]').addEventListener('blur', (e) => {
    const errorEl = document.getElementById('band-phone-error');
    const raw = e.target.value.trim();
    if (!raw) { errorEl.hidden = true; return; }

    const formatted = formatBandPhone(raw);
    if (formatted) {
        e.target.value = formatted;
        errorEl.hidden = true;
    } else {
        errorEl.textContent = "That doesn't look like a valid 10-digit phone number.";
        errorEl.hidden = false;
    }
});

document.getElementById('band-info-form').addEventListener('submit', async (e) => {
    e.preventDefault();
    const form = e.target;
    const status = document.getElementById('band-info-status');

    if (!document.getElementById('band-phone-error').hidden) {
        status.textContent = 'Fix the phone number before saving.';
        return;
    }

    const res = await fetch('/api/band-admin/info', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
            name: form.name.value.trim(),
            phone: form.phone.value.trim(),
            addressLine1: form.addressLine1.value.trim(),
            addressLine2: form.addressLine2.value.trim(),
            city: form.city.value.trim(),
            state: form.state.value.trim(),
            postalCode: form.postalCode.value.trim(),
            timeZone: form.timeZone.value || null
        })
    });
    const body = await res.json();
    status.textContent = res.ok ? 'Band information saved.' : (body.error || 'Could not save band information.');
});

document.getElementById('band-address-validate-btn').addEventListener('click', async () => {
    const form = document.getElementById('band-info-form');
    const suggestionBox = document.getElementById('band-address-suggestion');
    const status = document.getElementById('band-info-status');

    if (!form.addressLine1.value.trim()) { status.textContent = 'Enter a street address first.'; return; }

    status.textContent = 'Checking...';
    const params = new URLSearchParams({
        streetAddress: form.addressLine1.value.trim(),
        secondaryAddress: form.addressLine2.value.trim(),
        city: form.city.value.trim(),
        state: form.state.value.trim(),
        zipCode: form.postalCode.value.trim()
    });
    const res = await fetch(`/api/address-lookup?${params.toString()}`);
    const body = res.ok ? await res.json() : { match: null };
    status.textContent = '';

    if (!body.match) {
        suggestionBox.hidden = true;
        status.textContent = 'No standardized match found - the address will be saved exactly as typed.';
        return;
    }

    const m = body.match;
    suggestionBox.innerHTML = `
        <p class="save-note">USPS suggests:</p>
        <p>${escapeHtml(m.streetAddress)}${m.secondaryAddress ? ' ' + escapeHtml(m.secondaryAddress) : ''}<br>
        ${escapeHtml(m.city)}, ${escapeHtml(m.state)} ${escapeHtml(m.zipCode)}${m.zipPlus4 ? '-' + escapeHtml(m.zipPlus4) : ''}</p>
        <button type="button" id="band-address-use-suggestion-btn">Use this address</button>
    `;
    suggestionBox.hidden = false;
    document.getElementById('band-address-use-suggestion-btn').addEventListener('click', () => {
        form.addressLine1.value = m.streetAddress;
        form.addressLine2.value = m.secondaryAddress || '';
        form.city.value = m.city;
        form.state.value = m.state;
        form.postalCode.value = m.zipPlus4 ? `${m.zipCode}-${m.zipPlus4}` : m.zipCode;
        suggestionBox.hidden = true;
    });
});

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

function escapeHtml(str) {
    const div = document.createElement('div');
    div.textContent = str ?? '';
    return div.innerHTML;
}

// --- Band Roles (who plays/does what - separate from the Repertoire
// page's Instrument Tunings list, which tracks per-song tuning notes,
// not who's assigned to anything) ---
let bandRolesCurrentMemberId = null;

async function loadBandRolesMembers() {
    const res = await fetch('/api/profile/band-members');
    const members = res.ok ? await res.json() : [];
    const select = document.getElementById('band-roles-member-select');
    select.innerHTML = '<option value="" disabled selected>Select a member...</option>' +
        members.map((m) => `<option value="${m.id}">${escapeHtml(m.firstName)}</option>`).join('');
}

function renderBandRolesCheckboxes(checkedRoles) {
    const checked = new Set(checkedRoles || []);
    const box = document.getElementById('band-roles-checkboxes');
    box.innerHTML = bandRoleOptions.map((r) =>
        `<label><input type="checkbox" name="roles" value="${escapeHtml(r)}" ${checked.has(r) ? 'checked' : ''}> ${escapeHtml(r)}</label>`
    ).join('');
}

document.getElementById('band-roles-member-select').addEventListener('change', async (e) => {
    bandRolesCurrentMemberId = e.target.value;
    document.getElementById('band-roles-status').textContent = '';
    const res = await fetch('/api/profile/users');
    const users = res.ok ? await res.json() : [];
    const user = users.find((u) => u.id === bandRolesCurrentMemberId);
    renderBandRolesCheckboxes(user ? user.roles : []);
});

document.getElementById('band-roles-add-custom-btn').addEventListener('click', () => {
    const name = prompt('Role name (e.g. "Merch Table Lead"):');
    if (!name || !name.trim()) return;
    const trimmed = name.trim();
    if (bandRoleOptions.includes(trimmed)) {
        const box = document.getElementById('band-roles-checkboxes');
        const existing = [...box.querySelectorAll('input[name="roles"]')].find((i) => i.value === trimmed);
        if (existing) existing.checked = true;
        return;
    }
    if (!confirm(`Add "${trimmed}" as a new role for this band? It'll be available to pick for any member from now on.`)) return;
    bandRoleOptions.push(trimmed);
    const box = document.getElementById('band-roles-checkboxes');
    const label = document.createElement('label');
    label.innerHTML = `<input type="checkbox" name="roles" value="${escapeHtml(trimmed)}" checked> ${escapeHtml(trimmed)}`;
    box.appendChild(label);
});

document.getElementById('band-roles-form').addEventListener('submit', async (e) => {
    e.preventDefault();
    const status = document.getElementById('band-roles-status');
    if (!bandRolesCurrentMemberId) { status.textContent = 'Select a band member first.'; return; }

    const roles = [...document.querySelectorAll('#band-roles-checkboxes input:checked')].map((i) => i.value);
    const res = await fetch(`/api/profile/users/${bandRolesCurrentMemberId}/roles`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ roles })
    });
    const body = await res.json();
    status.textContent = res.ok ? 'Saved.' : (body.error || 'Could not save roles.');
    if (res.ok) loadUsers();
});

loadBandAdmin();
