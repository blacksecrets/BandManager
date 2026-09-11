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
    document.getElementById('band-acts-section').hidden = !hasBand;
    document.getElementById('band-gear-section').hidden = !hasBand;
    document.getElementById('band-promoters-section').hidden = !hasBand;
    if (!hasBand) return;

    loadBandInfo();
    loadBandBranding();
    loadBandRoleOptions().then(loadUsers);
    loadBandRolesMembers();
    loadActs();
    loadBandGear();
    loadPromoters();
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

// --- Acts (a band's performance configurations - see Act.cs) ---
let acts = [];
let editingActId = null;

async function loadActs() {
    const res = await fetch('/api/acts');
    acts = res.ok ? await res.json() : [];
    renderActsList();
}

function renderActsList() {
    const body = document.getElementById('acts-table-body');
    body.innerHTML = acts.map((a) => `
        <tr>
            <td>${escapeHtml(a.name)}${a.isDefault ? ' <span class="save-note">(default)</span>' : ''}</td>
            <td><button type="button" class="act-edit-btn" data-id="${a.id}">Edit</button></td>
            <td></td>
        </tr>
    `).join('') || '<tr><td colspan="3" class="save-note">No acts yet.</td></tr>';

    body.querySelectorAll('.act-edit-btn').forEach((btn) => {
        btn.addEventListener('click', () => openActModal(acts.find((a) => a.id === btn.dataset.id)));
    });
}

function closeActModal() { document.getElementById('act-modal-backdrop').hidden = true; }
document.getElementById('act-modal-close').addEventListener('click', closeActModal);
document.getElementById('act-modal-backdrop').addEventListener('click', (e) => { if (e.target.id === 'act-modal-backdrop') closeActModal(); });

function openActModal(act) {
    editingActId = act ? act.id : null;
    document.getElementById('act-modal-title').textContent = act ? 'Edit Act' : 'Add an Act';
    document.getElementById('act-form-status').textContent = '';
    const form = document.getElementById('act-form');
    form.name.value = act?.name || '';
    form.introText.value = act?.introText || '';
    form.videoNotes.value = act?.videoNotes || '';
    form.generalNotes.value = act?.generalNotes || '';
    form.techContactName.value = act?.techContactName || '';
    form.techContactPhone.value = act?.techContactPhone || '';
    form.techContactEmail.value = act?.techContactEmail || '';

    const deleteBtn = document.getElementById('act-delete-btn');
    deleteBtn.hidden = !act || act.isDefault;
    deleteBtn.onclick = async () => {
        if (!editingActId) return;
        if (!confirm(`Delete "${act.name}"? This can't be undone.`)) return;
        const res = await fetch(`/api/acts/${editingActId}`, { method: 'DELETE' });
        const resBody = await res.json().catch(() => ({}));
        if (res.ok) { closeActModal(); await loadActs(); }
        else { document.getElementById('act-form-status').textContent = resBody.error || 'Could not delete this act.'; }
    };

    // Only makes sense once the Act has an id to attach a Gear List to -
    // stays hidden for a brand-new, not-yet-saved Act.
    const gearSection = document.getElementById('act-gear-section');
    gearSection.hidden = !act;
    if (act) loadActGearChecklist(act.id);

    const stagePlotBtn = document.getElementById('act-stage-plot-btn');
    stagePlotBtn.onclick = () => window.openStagePlotEditor(act.id, act.name);

    const inputChannelsSection = document.getElementById('act-input-channels-section');
    inputChannelsSection.hidden = !act;
    if (act) loadActInputChannels(act.id);

    const monitorMixesSection = document.getElementById('act-monitor-mixes-section');
    monitorMixesSection.hidden = !act;
    if (act) loadActMonitorMixes(act.id);

    const micEqNotesSection = document.getElementById('act-mic-eq-notes-section');
    micEqNotesSection.hidden = !act;
    if (act) loadActMicEqNotes(act.id);

    const techRiderSection = document.getElementById('act-tech-rider-preview-section');
    techRiderSection.hidden = !act;
    if (act) document.getElementById('act-tech-rider-preview-link').href = `/print-tech-rider.html?actId=${act.id}`;

    document.getElementById('act-modal-backdrop').hidden = false;
}

async function loadActGearChecklist(actId) {
    const status = document.getElementById('act-gear-status');
    const checklist = document.getElementById('act-gear-checklist');
    status.textContent = '';
    checklist.innerHTML = '<p class="save-note">Loading...</p>';

    const [catalogRes, assignedRes] = await Promise.all([
        fetch('/api/band-gear'),
        fetch(`/api/acts/${actId}/gear`)
    ]);
    const catalog = catalogRes.ok ? await catalogRes.json() : [];
    const assigned = assignedRes.ok ? await assignedRes.json() : [];
    const assignedIds = new Set(assigned.map((g) => g.id));

    if (catalog.length === 0) {
        checklist.innerHTML = '<p class="save-note">The Gear Catalog is empty - add items to it first.</p>';
        return;
    }
    checklist.innerHTML = catalog.map((g) => `
        <label>
            <input type="checkbox" value="${g.id}" ${assignedIds.has(g.id) ? 'checked' : ''}>
            ${escapeHtml(g.type)}${g.make || g.model ? ' - ' + escapeHtml([g.make, g.model].filter(Boolean).join(' ')) : ''}
            <span class="save-note">${g.ownerName ? escapeHtml(g.ownerName) : 'Band Asset'}</span>
        </label>
    `).join('');
}

document.getElementById('act-gear-save-btn').addEventListener('click', async () => {
    if (!editingActId) return;
    const status = document.getElementById('act-gear-status');
    const ids = Array.from(document.querySelectorAll('#act-gear-checklist input:checked')).map((i) => i.value);
    status.textContent = 'Saving...';
    const res = await fetch(`/api/acts/${editingActId}/gear`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ bandGearItemIds: ids })
    });
    status.textContent = res.ok ? 'Gear List saved.' : 'Could not save the Gear List.';
});

document.getElementById('add-act-btn').addEventListener('click', () => openActModal(null));

document.getElementById('act-form').addEventListener('submit', async (e) => {
    e.preventDefault();
    const form = e.target;
    const status = document.getElementById('act-form-status');
    const body = JSON.stringify({
        name: form.name.value.trim(),
        introText: form.introText.value.trim() || null,
        videoNotes: form.videoNotes.value.trim() || null,
        generalNotes: form.generalNotes.value.trim() || null,
        techContactName: form.techContactName.value.trim() || null,
        techContactPhone: form.techContactPhone.value.trim() || null,
        techContactEmail: form.techContactEmail.value.trim() || null
    });
    const url = editingActId ? `/api/acts/${editingActId}` : '/api/acts';
    const method = editingActId ? 'PUT' : 'POST';
    const res = await fetch(url, { method, headers: { 'Content-Type': 'application/json' }, body });
    const resBody = await res.json().catch(() => ({}));
    if (res.ok) { closeActModal(); await loadActs(); }
    else { status.textContent = resBody.error || 'Could not save this act.'; }
});

// --- Gear Catalog (a band's admin-maintained gear list - see BandGearItem.cs) ---
let bandGearItems = [];
let bandMembers = [];
let gearTypes = [];
let editingBandGearId = null;

async function loadBandGear() {
    const [gearRes, typesRes, membersRes] = await Promise.all([
        fetch('/api/band-gear'),
        fetch('/api/gear/types'),
        fetch('/api/profile/band-members')
    ]);
    bandGearItems = gearRes.ok ? await gearRes.json() : [];
    gearTypes = typesRes.ok ? (await typesRes.json()).types : [];
    bandMembers = membersRes.ok ? await membersRes.json() : [];
    renderBandGearList();
}

function renderBandGearList() {
    const body = document.getElementById('band-gear-table-body');
    body.innerHTML = bandGearItems.map((g) => `
        <tr>
            <td>${escapeHtml(g.type)}</td>
            <td>${escapeHtml([g.make, g.model].filter(Boolean).join(' ')) || '<span class="save-note">-</span>'}</td>
            <td>${g.ownerName ? escapeHtml(g.ownerName) : '<span class="save-note">Band Asset</span>'}</td>
            <td><button type="button" class="band-gear-edit-btn" data-id="${g.id}">Edit</button></td>
        </tr>
    `).join('') || '<tr><td colspan="4" class="save-note">No gear in the catalog yet.</td></tr>';

    body.querySelectorAll('.band-gear-edit-btn').forEach((btn) => {
        btn.addEventListener('click', () => openBandGearModal(bandGearItems.find((g) => g.id === btn.dataset.id)));
    });
}

function populateBandGearSelects() {
    const typeSelect = document.getElementById('band-gear-type-select');
    typeSelect.innerHTML = gearTypes.map((t) => `<option value="${escapeHtml(t)}">${escapeHtml(t)}</option>`).join('');

    const ownerSelect = document.getElementById('band-gear-owner-select');
    ownerSelect.innerHTML = '<option value="">Band Asset (jointly owned)</option>' +
        bandMembers.map((m) => `<option value="${m.id}">${escapeHtml(m.firstName)}</option>`).join('');
}

function closeBandGearModal() { document.getElementById('band-gear-modal-backdrop').hidden = true; }
document.getElementById('band-gear-modal-close').addEventListener('click', closeBandGearModal);
document.getElementById('band-gear-modal-backdrop').addEventListener('click', (e) => { if (e.target.id === 'band-gear-modal-backdrop') closeBandGearModal(); });

function openBandGearModal(item) {
    editingBandGearId = item ? item.id : null;
    populateBandGearSelects();
    document.getElementById('band-gear-modal-title').textContent = item ? 'Edit Gear' : 'New Band Asset';
    document.getElementById('band-gear-form-status').textContent = '';
    const form = document.getElementById('band-gear-form');
    form.type.value = item?.type || gearTypes[0] || '';
    form.make.value = item?.make || '';
    form.model.value = item?.model || '';
    form.lengthInches.value = item?.lengthInches ?? '';
    form.widthInches.value = item?.widthInches ?? '';
    form.depthInches.value = item?.depthInches ?? '';
    form.weightPounds.value = item?.weightPounds ?? '';
    form.ownerUserId.value = item?.ownerUserId || '';

    const deleteBtn = document.getElementById('band-gear-delete-btn');
    deleteBtn.hidden = !item;
    deleteBtn.onclick = async () => {
        if (!editingBandGearId) return;
        if (!confirm(`Remove this item from the Gear Catalog? This can't be undone.`)) return;
        const res = await fetch(`/api/band-gear/${editingBandGearId}`, { method: 'DELETE' });
        if (res.ok) { closeBandGearModal(); await loadBandGear(); }
    };

    document.getElementById('band-gear-modal-backdrop').hidden = false;
}

document.getElementById('add-band-gear-btn').addEventListener('click', () => openBandGearModal(null));

document.getElementById('band-gear-form').addEventListener('submit', async (e) => {
    e.preventDefault();
    const form = e.target;
    const status = document.getElementById('band-gear-form-status');
    const num = (v) => v.trim() === '' ? null : Number(v);
    const body = JSON.stringify({
        type: form.type.value,
        make: form.make.value.trim() || null,
        model: form.model.value.trim() || null,
        lengthInches: num(form.lengthInches.value),
        widthInches: num(form.widthInches.value),
        depthInches: num(form.depthInches.value),
        weightPounds: num(form.weightPounds.value),
        ownerUserId: form.ownerUserId.value || null
    });
    const url = editingBandGearId ? `/api/band-gear/${editingBandGearId}` : '/api/band-gear';
    const method = editingBandGearId ? 'PUT' : 'POST';
    const res = await fetch(url, { method, headers: { 'Content-Type': 'application/json' }, body });
    const resBody = await res.json().catch(() => ({}));
    if (res.ok) { closeBandGearModal(); await loadBandGear(); }
    else { status.textContent = resBody.error || 'Could not save this item.'; }
});

// --- Copy from a Member's Gear (two-step: pick member, then pick item) ---
function closeCopyGearModal() {
    document.getElementById('copy-gear-modal-backdrop').hidden = true;
    document.getElementById('copy-gear-member-select').value = '';
    document.getElementById('copy-gear-member-items').innerHTML = '';
    document.getElementById('copy-gear-status').textContent = '';
}
document.getElementById('copy-gear-modal-close').addEventListener('click', closeCopyGearModal);
document.getElementById('copy-gear-modal-backdrop').addEventListener('click', (e) => { if (e.target.id === 'copy-gear-modal-backdrop') closeCopyGearModal(); });

document.getElementById('copy-band-gear-btn').addEventListener('click', () => {
    const select = document.getElementById('copy-gear-member-select');
    select.innerHTML = '<option value="">Choose a member...</option>' +
        bandMembers.map((m) => `<option value="${m.id}">${escapeHtml(m.firstName)}</option>`).join('');
    document.getElementById('copy-gear-member-items').innerHTML = '';
    document.getElementById('copy-gear-status').textContent = '';
    document.getElementById('copy-gear-modal-backdrop').hidden = false;
});

document.getElementById('copy-gear-member-select').addEventListener('change', async (e) => {
    const itemsEl = document.getElementById('copy-gear-member-items');
    const status = document.getElementById('copy-gear-status');
    itemsEl.innerHTML = '';
    status.textContent = '';
    if (!e.target.value) return;

    status.textContent = 'Loading their gear...';
    const res = await fetch(`/api/band-gear/member-gear/${e.target.value}`);
    const items = res.ok ? await res.json() : [];
    status.textContent = '';

    if (items.length === 0) {
        itemsEl.innerHTML = '<p class="save-note">This member has no personal gear listed yet.</p>';
        return;
    }
    itemsEl.innerHTML = `<div class="band-checkbox-list">` + items.map((g) => `
        <div>
            ${escapeHtml(g.type)}${g.make || g.model ? ' - ' + escapeHtml([g.make, g.model].filter(Boolean).join(' ')) : ''}
            <button type="button" class="copy-gear-item-btn" data-id="${g.id}">Copy in</button>
        </div>
    `).join('') + `</div>`;
    itemsEl.querySelectorAll('.copy-gear-item-btn').forEach((btn) => {
        btn.addEventListener('click', async () => {
            status.textContent = 'Copying...';
            const copyRes = await fetch(`/api/band-gear/from-member/${btn.dataset.id}`, { method: 'POST' });
            const body = await copyRes.json().catch(() => ({}));
            if (copyRes.ok) { closeCopyGearModal(); await loadBandGear(); }
            else { status.textContent = body.error || 'Could not copy this item.'; }
        });
    });
});

// --- Promoters (a band's reusable booking-contact roster - see Promoter.cs) ---
let promoters = [];
let editingPromoterId = null;

async function loadPromoters() {
    const res = await fetch('/api/promoters');
    promoters = res.ok ? await res.json() : [];
    renderPromotersList();
}

function renderPromotersList() {
    const body = document.getElementById('promoters-table-body');
    body.innerHTML = promoters.map((p) => `
        <tr>
            <td>${escapeHtml(p.name)}</td>
            <td>${escapeHtml(p.company || '')}</td>
            <td><button type="button" class="promoter-edit-btn" data-id="${p.id}">Edit</button></td>
        </tr>
    `).join('') || '<tr><td colspan="3" class="save-note">No promoters yet.</td></tr>';

    body.querySelectorAll('.promoter-edit-btn').forEach((btn) => {
        btn.addEventListener('click', () => openPromoterModal(promoters.find((p) => p.id === btn.dataset.id)));
    });
}

function closePromoterModal() { document.getElementById('promoter-modal-backdrop').hidden = true; }
document.getElementById('promoter-modal-close').addEventListener('click', closePromoterModal);
document.getElementById('promoter-modal-backdrop').addEventListener('click', (e) => { if (e.target.id === 'promoter-modal-backdrop') closePromoterModal(); });

function openPromoterModal(promoter) {
    editingPromoterId = promoter ? promoter.id : null;
    document.getElementById('promoter-modal-title').textContent = promoter ? 'Edit Promoter' : 'Add a Promoter';
    document.getElementById('promoter-form-status').textContent = '';
    const form = document.getElementById('promoter-form');
    form.name.value = promoter?.name || '';
    form.company.value = promoter?.company || '';
    form.phone.value = promoter?.phone || '';
    form.email.value = promoter?.email || '';

    const deleteBtn = document.getElementById('promoter-delete-btn');
    deleteBtn.hidden = !promoter;
    deleteBtn.onclick = async () => {
        if (!editingPromoterId) return;
        if (!confirm(`Delete "${promoter.name}"? This can't be undone.`)) return;
        const res = await fetch(`/api/promoters/${editingPromoterId}`, { method: 'DELETE' });
        if (res.ok) { closePromoterModal(); await loadPromoters(); }
    };

    document.getElementById('promoter-modal-backdrop').hidden = false;
}

document.getElementById('add-promoter-btn').addEventListener('click', () => openPromoterModal(null));

document.getElementById('promoter-form').addEventListener('submit', async (e) => {
    e.preventDefault();
    const form = e.target;
    const status = document.getElementById('promoter-form-status');
    const body = JSON.stringify({
        name: form.name.value.trim(),
        company: form.company.value.trim() || null,
        phone: form.phone.value.trim() || null,
        email: form.email.value.trim() || null
    });
    const url = editingPromoterId ? `/api/promoters/${editingPromoterId}` : '/api/promoters';
    const method = editingPromoterId ? 'PUT' : 'POST';
    const res = await fetch(url, { method, headers: { 'Content-Type': 'application/json' }, body });
    const resBody = await res.json().catch(() => ({}));
    if (res.ok) { closePromoterModal(); await loadPromoters(); }
    else { status.textContent = resBody.error || 'Could not save this promoter.'; }
});

// --- Tech Rider: Input / Mic Splitter Channel List (see TechRiderInputChannel.cs) ---
async function loadActInputChannels(actId) {
    const res = await fetch(`/api/acts/${actId}/input-channels`);
    const channels = res.ok ? await res.json() : [];
    renderInputChannelsTable(channels, actId);
}

function inputChannelRowHtml(c) {
    return `
        <td><input type="number" class="ic-channel-number" style="width:48px" value="${c.channelNumber ?? ''}"></td>
        <td><input type="text" class="ic-source" placeholder="e.g. Lead Vocal" value="${escapeHtml(c.source || '')}"></td>
        <td><input type="text" class="ic-mic" placeholder="e.g. Shure SM58" value="${escapeHtml(c.micRecommendation || '')}"></td>
        <td><select class="ic-provided-by">
            <option value="">-</option>
            <option value="Venue" ${c.providedBy === 'Venue' ? 'selected' : ''}>Venue</option>
            <option value="Band" ${c.providedBy === 'Band' ? 'selected' : ''}>Band</option>
            <option value="Either" ${c.providedBy === 'Either' ? 'selected' : ''}>Either</option>
        </select></td>
        <td><input type="text" class="ic-notes" value="${escapeHtml(c.positioningNotes || '')}"></td>
        <td>
            <button type="button" class="ic-save-btn">Save</button>
            <button type="button" class="ic-delete-btn">Delete</button>
        </td>
    `;
}

function wireInputChannelRow(tr, actId) {
    tr.querySelector('.ic-save-btn').addEventListener('click', async () => {
        const body = JSON.stringify({
            channelNumber: Number(tr.querySelector('.ic-channel-number').value) || 0,
            source: tr.querySelector('.ic-source').value.trim(),
            micRecommendation: tr.querySelector('.ic-mic').value.trim() || null,
            providedBy: tr.querySelector('.ic-provided-by').value || null,
            positioningNotes: tr.querySelector('.ic-notes').value.trim() || null
        });
        const id = tr.dataset.id;
        const url = id ? `/api/acts/${actId}/input-channels/${id}` : `/api/acts/${actId}/input-channels`;
        const method = id ? 'PUT' : 'POST';
        const res = await fetch(url, { method, headers: { 'Content-Type': 'application/json' }, body });
        if (res.ok) await loadActInputChannels(actId);
    });
    tr.querySelector('.ic-delete-btn').addEventListener('click', async () => {
        const id = tr.dataset.id;
        if (!id) { tr.remove(); return; }
        await fetch(`/api/acts/${actId}/input-channels/${id}`, { method: 'DELETE' });
        await loadActInputChannels(actId);
    });
}

function renderInputChannelsTable(channels, actId) {
    const body = document.getElementById('act-input-channels-body');
    body.innerHTML = '';
    channels.forEach((c) => {
        const tr = document.createElement('tr');
        tr.dataset.id = c.id;
        tr.innerHTML = inputChannelRowHtml(c);
        wireInputChannelRow(tr, actId);
        body.appendChild(tr);
    });
}

document.getElementById('act-input-channel-add-btn').addEventListener('click', () => {
    const actId = editingActId;
    if (!actId) return;
    const body = document.getElementById('act-input-channels-body');
    const tr = document.createElement('tr');
    tr.innerHTML = inputChannelRowHtml({});
    wireInputChannelRow(tr, actId);
    body.appendChild(tr);
});

// --- Tech Rider: Monitor Mix (see TechRiderMonitorMix.cs) ---
async function loadActMonitorMixes(actId) {
    const res = await fetch(`/api/acts/${actId}/monitor-mixes`);
    const mixes = res.ok ? await res.json() : [];
    renderMonitorMixesTable(mixes, actId);
}

function monitorMixRowHtml(m) {
    return `
        <td><input type="text" class="mm-position" placeholder="e.g. Lead Vocal" value="${escapeHtml(m.position || '')}"></td>
        <td><input type="text" class="mm-description" placeholder="e.g. Vocals up, light kick/bass" value="${escapeHtml(m.mixDescription || '')}"></td>
        <td>
            <button type="button" class="mm-save-btn">Save</button>
            <button type="button" class="mm-delete-btn">Delete</button>
        </td>
    `;
}

function wireMonitorMixRow(tr, actId) {
    tr.querySelector('.mm-save-btn').addEventListener('click', async () => {
        const body = JSON.stringify({
            position: tr.querySelector('.mm-position').value.trim(),
            mixDescription: tr.querySelector('.mm-description').value.trim()
        });
        const id = tr.dataset.id;
        const url = id ? `/api/acts/${actId}/monitor-mixes/${id}` : `/api/acts/${actId}/monitor-mixes`;
        const method = id ? 'PUT' : 'POST';
        const res = await fetch(url, { method, headers: { 'Content-Type': 'application/json' }, body });
        if (res.ok) await loadActMonitorMixes(actId);
    });
    tr.querySelector('.mm-delete-btn').addEventListener('click', async () => {
        const id = tr.dataset.id;
        if (!id) { tr.remove(); return; }
        await fetch(`/api/acts/${actId}/monitor-mixes/${id}`, { method: 'DELETE' });
        await loadActMonitorMixes(actId);
    });
}

function renderMonitorMixesTable(mixes, actId) {
    const body = document.getElementById('act-monitor-mixes-body');
    body.innerHTML = '';
    mixes.forEach((m) => {
        const tr = document.createElement('tr');
        tr.dataset.id = m.id;
        tr.innerHTML = monitorMixRowHtml(m);
        wireMonitorMixRow(tr, actId);
        body.appendChild(tr);
    });
}

document.getElementById('act-monitor-mix-add-btn').addEventListener('click', () => {
    const actId = editingActId;
    if (!actId) return;
    const body = document.getElementById('act-monitor-mixes-body');
    const tr = document.createElement('tr');
    tr.innerHTML = monitorMixRowHtml({});
    wireMonitorMixRow(tr, actId);
    body.appendChild(tr);
});

// --- Tech Rider: Mic EQ Notes (see TechRiderMicEqNote.cs) - per-Act, not
// a shared library, since the right EQ starting point depends on what
// this mic is miking in THIS act's own setup. ---
async function loadActMicEqNotes(actId) {
    const res = await fetch(`/api/acts/${actId}/mic-eq-notes`);
    const notes = res.ok ? await res.json() : [];
    renderMicEqNotesList(notes, actId);
}

function freqRowHtml(row) {
    row = row || {};
    return `
        <tr>
            <td><input type="text" class="meq-freq-hz" placeholder="e.g. 100-150Hz" value="${escapeHtml(row.frequency || '')}"></td>
            <td><input type="text" class="meq-freq-move" placeholder="e.g. Cut" value="${escapeHtml(row.eqMove || '')}"></td>
            <td><input type="text" class="meq-freq-reason" placeholder="e.g. Boxiness" value="${escapeHtml(row.reason || '')}"></td>
            <td><button type="button" class="meq-remove-row-btn">&times;</button></td>
        </tr>
    `;
}

function micEqCardHtml(n) {
    const rows = n.frequencyRows && n.frequencyRows.length ? n.frequencyRows : [{}];
    return `
        <label>Mic Model <input type="text" class="meq-mic-model" placeholder="e.g. Shure SM57" value="${escapeHtml(n.micModel || '')}"></label>
        <label>Context <input type="text" class="meq-context" placeholder="e.g. Electric Guitar Amp" value="${escapeHtml(n.context || '')}"></label>
        <table class="user-table meq-freq-table">
            <thead><tr><th>Frequency</th><th>EQ Move</th><th>Reason</th><th></th></tr></thead>
            <tbody class="meq-freq-body">${rows.map(freqRowHtml).join('')}</tbody>
        </table>
        <button type="button" class="meq-add-row-btn">+ Add Frequency Row</button>
        <label>General Notes <textarea class="meq-general-notes" rows="2">${escapeHtml(n.generalNotes || '')}</textarea></label>
        <div class="cred-form-buttons">
            <button type="button" class="meq-save-btn">Save</button>
            <button type="button" class="meq-delete-btn">Delete</button>
        </div>
        <p class="meq-status save-note"></p>
    `;
}

function wireMicEqCard(card, actId) {
    function wireRemoveButtons() {
        card.querySelectorAll('.meq-remove-row-btn').forEach((btn) => {
            btn.onclick = () => { btn.closest('tr').remove(); };
        });
    }
    wireRemoveButtons();

    card.querySelector('.meq-add-row-btn').addEventListener('click', () => {
        const tbody = card.querySelector('.meq-freq-body');
        const tr = document.createElement('tr');
        tr.innerHTML = freqRowHtml({});
        tbody.appendChild(tr);
        wireRemoveButtons();
    });

    card.querySelector('.meq-save-btn').addEventListener('click', async () => {
        const status = card.querySelector('.meq-status');
        const frequencyRows = Array.from(card.querySelectorAll('.meq-freq-body tr')).map((tr) => ({
            frequency: tr.querySelector('.meq-freq-hz').value.trim(),
            eqMove: tr.querySelector('.meq-freq-move').value.trim() || null,
            reason: tr.querySelector('.meq-freq-reason').value.trim() || null
        })).filter((r) => r.frequency);

        const body = JSON.stringify({
            micModel: card.querySelector('.meq-mic-model').value.trim(),
            context: card.querySelector('.meq-context').value.trim() || null,
            frequencyRows,
            generalNotes: card.querySelector('.meq-general-notes').value.trim() || null
        });
        const id = card.dataset.id;
        const url = id ? `/api/acts/${actId}/mic-eq-notes/${id}` : `/api/acts/${actId}/mic-eq-notes`;
        const method = id ? 'PUT' : 'POST';
        const res = await fetch(url, { method, headers: { 'Content-Type': 'application/json' }, body });
        const resBody = await res.json().catch(() => ({}));
        if (res.ok) await loadActMicEqNotes(actId);
        else status.textContent = resBody.error || 'Could not save this note.';
    });

    card.querySelector('.meq-delete-btn').addEventListener('click', async () => {
        const id = card.dataset.id;
        if (!id) { card.remove(); return; }
        await fetch(`/api/acts/${actId}/mic-eq-notes/${id}`, { method: 'DELETE' });
        await loadActMicEqNotes(actId);
    });
}

function renderMicEqNotesList(notes, actId) {
    const list = document.getElementById('act-mic-eq-notes-list');
    list.innerHTML = '';
    notes.forEach((n) => {
        const card = document.createElement('div');
        card.className = 'platform-card';
        card.dataset.id = n.id;
        card.innerHTML = micEqCardHtml(n);
        wireMicEqCard(card, actId);
        list.appendChild(card);
    });
}

document.getElementById('act-mic-eq-note-add-btn').addEventListener('click', () => {
    const actId = editingActId;
    if (!actId) return;
    const list = document.getElementById('act-mic-eq-notes-list');
    const card = document.createElement('div');
    card.className = 'platform-card';
    card.innerHTML = micEqCardHtml({});
    wireMicEqCard(card, actId);
    list.appendChild(card);
});

loadBandAdmin();
