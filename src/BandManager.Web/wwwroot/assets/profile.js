async function loadMe() {
    const res = await fetch('/api/profile/me');
    const me = await res.json();
    const roleNote = me.isSuperAdmin ? ' (SuperAdmin)' : me.activeBandRole === 'BandAdmin' ? ` (Band Admin of ${me.activeBandName || 'this band'})` : '';
    document.getElementById('whoami').textContent = `Logged in as ${me.username}${roleNote}`;
    document.querySelector('#username-form input[name="username"]').value = me.username || '';
    document.querySelector('#name-form input[name="firstName"]').value = me.firstName || '';
    document.querySelector('#name-form input[name="lastName"]').value = me.lastName || '';

    const contactForm = document.getElementById('contact-form');
    contactForm.cellNumber.value = me.cellNumber || '';
    contactForm.addressLine1.value = me.addressLine1 || '';
    contactForm.addressLine2.value = me.addressLine2 || '';
    contactForm.city.value = me.city || '';
    contactForm.state.value = me.state || '';
    contactForm.postalCode.value = me.postalCode || '';

    // Shows whether the login itself just landed here for that reason
    // (?mustChangePassword=1, from AuthController.Login) or a direct
    // navigation back to a still-unresolved one (me.mustChangePassword) -
    // either way, same banner.
    const stillPending = me.mustChangePassword;
    const justArrived = new URLSearchParams(location.search).get('mustChangePassword') === '1';
    document.getElementById('must-change-password-banner').hidden = !(stillPending || justArrived);
}

// --- Username (also the account's email - see UpdateUsername's doc comment) ---
document.getElementById('username-form').addEventListener('submit', async (e) => {
    e.preventDefault();
    const form = e.target;
    const status = document.getElementById('username-status');

    const res = await fetch('/api/profile/username', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ username: form.username.value.trim() })
    });
    const body = await res.json();
    if (!res.ok) {
        status.textContent = body.error || 'Could not save username.';
        return;
    }
    // Doesn't apply immediately - a confirm link went to the new address
    // (see ProfileController.UpdateUsername); reset the field back to the
    // still-current username rather than showing the requested one as if
    // it already took effect.
    status.textContent = body.pending
        ? `Check ${body.pendingEmail} for a confirmation link - this account keeps its current username/email until you click it.`
        : 'That\'s already this account\'s username.';
    loadMe();
});

document.getElementById('name-form').addEventListener('submit', async (e) => {
    e.preventDefault();
    const form = e.target;
    const status = document.getElementById('name-status');

    const res = await fetch('/api/profile/name', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ firstName: form.firstName.value.trim(), lastName: form.lastName.value.trim() })
    });
    const body = await res.json();
    status.textContent = res.ok ? 'Name saved.' : (body.error || 'Could not save name.');
});

// --- Contact info / address ---
async function loadUspsConfigured() {
    const res = await fetch('/api/address-lookup/configured');
    const { configured } = res.ok ? await res.json() : { configured: false };
    document.getElementById('usps-setup-note').hidden = !!configured;
    document.getElementById('address-validate-btn').disabled = !configured;
}

document.getElementById('contact-form').addEventListener('submit', async (e) => {
    e.preventDefault();
    const form = e.target;
    const status = document.getElementById('contact-status');

    const res = await fetch('/api/profile/contact', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
            cellNumber: form.cellNumber.value.trim(),
            addressLine1: form.addressLine1.value.trim(),
            addressLine2: form.addressLine2.value.trim(),
            city: form.city.value.trim(),
            state: form.state.value.trim(),
            postalCode: form.postalCode.value.trim()
        })
    });
    status.textContent = res.ok ? 'Contact info saved.' : 'Could not save contact info.';
});

document.getElementById('address-validate-btn').addEventListener('click', async () => {
    const form = document.getElementById('contact-form');
    const suggestionBox = document.getElementById('address-suggestion');
    const status = document.getElementById('contact-status');

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
        status.textContent = 'No standardized match found - your address will be saved exactly as typed.';
        return;
    }

    const m = body.match;
    suggestionBox.innerHTML = `
        <p class="save-note">USPS suggests:</p>
        <p>${escapeHtmlProfile(m.streetAddress)}${m.secondaryAddress ? ' ' + escapeHtmlProfile(m.secondaryAddress) : ''}<br>
        ${escapeHtmlProfile(m.city)}, ${escapeHtmlProfile(m.state)} ${escapeHtmlProfile(m.zipCode)}${m.zipPlus4 ? '-' + escapeHtmlProfile(m.zipPlus4) : ''}</p>
        <button type="button" id="address-use-suggestion-btn">Use this address</button>
    `;
    suggestionBox.hidden = false;
    document.getElementById('address-use-suggestion-btn').addEventListener('click', () => {
        form.addressLine1.value = m.streetAddress;
        form.addressLine2.value = m.secondaryAddress || '';
        form.city.value = m.city;
        form.state.value = m.state;
        form.postalCode.value = m.zipPlus4 ? `${m.zipCode}-${m.zipPlus4}` : m.zipCode;
        suggestionBox.hidden = true;
    });
});

function escapeHtmlProfile(str) {
    const div = document.createElement('div');
    div.textContent = str ?? '';
    return div.innerHTML;
}

// --- Gear list ---
let gearDefaultSettings = {};

async function loadGearTypes() {
    const res = await fetch('/api/gear/types');
    if (!res.ok) return;
    const { types, defaultSettings } = await res.json();
    gearDefaultSettings = defaultSettings;
    const select = document.getElementById('gear-add-type');
    select.innerHTML = types.map((t) => `<option value="${escapeHtmlProfile(t)}">${escapeHtmlProfile(t)}</option>`).join('');
}

async function loadGear() {
    const res = await fetch('/api/gear');
    const gear = res.ok ? await res.json() : [];
    const box = document.getElementById('gear-list');
    box.innerHTML = '';
    if (gear.length === 0) {
        box.innerHTML = '<p class="save-note">No gear added yet.</p>';
        return;
    }
    for (const item of gear) box.appendChild(renderGearItem(item));
}

function renderGearItem(item) {
    const card = document.createElement('div');
    card.className = 'gear-item-card';

    const header = document.createElement('div');
    header.className = 'gear-item-header';
    header.innerHTML = `
        <input type="text" class="gear-field" data-field="type" value="${escapeHtmlProfile(item.type)}" placeholder="Type">
        <input type="text" class="gear-field" data-field="make" value="${escapeHtmlProfile(item.make || '')}" placeholder="Make">
        <input type="text" class="gear-field" data-field="model" value="${escapeHtmlProfile(item.model || '')}" placeholder="Model">
        <button type="button" class="remove-btn gear-remove-btn">Remove</button>
    `;
    card.appendChild(header);

    const specs = document.createElement('div');
    specs.className = 'gear-item-specs';
    specs.innerHTML = `
        <label>L (in) <input type="number" step="0.01" class="gear-field" data-field="lengthInches" value="${item.lengthInches ?? ''}"></label>
        <label>W (in) <input type="number" step="0.01" class="gear-field" data-field="widthInches" value="${item.widthInches ?? ''}"></label>
        <label>D (in) <input type="number" step="0.01" class="gear-field" data-field="depthInches" value="${item.depthInches ?? ''}"></label>
        <label>Weight (lb) <input type="number" step="0.01" class="gear-field" data-field="weightPounds" value="${item.weightPounds ?? ''}"></label>
    `;
    card.appendChild(specs);

    async function saveGearFields() {
        const fields = {};
        for (const input of card.querySelectorAll('.gear-field')) {
            const f = input.dataset.field;
            if (['lengthInches', 'widthInches', 'depthInches', 'weightPounds'].includes(f)) {
                fields[f] = input.value.trim() === '' ? null : parseFloat(input.value);
            } else {
                fields[f] = input.value.trim();
            }
        }
        await fetch(`/api/gear/${item.id}`, {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(fields)
        });
    }
    for (const input of card.querySelectorAll('.gear-field')) {
        input.addEventListener('change', saveGearFields);
    }

    card.querySelector('.gear-remove-btn').addEventListener('click', async () => {
        if (!confirm(`Remove this ${item.type}${item.make ? ' (' + item.make + (item.model ? ' ' + item.model : '') + ')' : ''}?`)) return;
        await fetch(`/api/gear/${item.id}`, { method: 'DELETE' });
        loadGear();
    });

    const settingsBox = document.createElement('div');
    settingsBox.className = 'gear-settings-list';
    for (const s of item.settings) settingsBox.appendChild(renderGearSetting(item.id, s));
    card.appendChild(settingsBox);

    const addSettingForm = document.createElement('form');
    addSettingForm.className = 'gear-add-setting-form';
    addSettingForm.innerHTML = `
        <input type="text" name="name" placeholder="Setting name (e.g. Gain)" maxlength="100">
        <input type="text" name="value" placeholder="Value">
        <button type="submit">Add setting</button>
    `;
    addSettingForm.addEventListener('submit', async (e) => {
        e.preventDefault();
        const name = addSettingForm.name.value.trim();
        if (!name) return;
        const res = await fetch(`/api/gear/${item.id}/settings`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ name, value: addSettingForm.value.value.trim() || null })
        });
        if (res.ok) {
            const setting = await res.json();
            settingsBox.appendChild(renderGearSetting(item.id, setting));
            addSettingForm.reset();
        }
    });
    card.appendChild(addSettingForm);

    return card;
}

function renderGearSetting(gearId, setting) {
    const row = document.createElement('div');
    row.className = 'gear-setting-row';
    row.innerHTML = `
        <input type="text" class="gear-setting-name" value="${escapeHtmlProfile(setting.name)}">
        <input type="text" class="gear-setting-value" value="${escapeHtmlProfile(setting.value || '')}" placeholder="Value">
        <button type="button" class="remove-btn">Remove</button>
    `;
    const nameInput = row.querySelector('.gear-setting-name');
    const valueInput = row.querySelector('.gear-setting-value');

    async function save() {
        await fetch(`/api/gear/${gearId}/settings/${setting.id}`, {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ name: nameInput.value.trim(), value: valueInput.value.trim() || null })
        });
    }
    nameInput.addEventListener('change', save);
    valueInput.addEventListener('change', save);

    row.querySelector('.remove-btn').addEventListener('click', async () => {
        await fetch(`/api/gear/${gearId}/settings/${setting.id}`, { method: 'DELETE' });
        row.remove();
    });

    return row;
}

document.getElementById('gear-add-form').addEventListener('submit', async (e) => {
    e.preventDefault();
    const form = e.target;
    const status = document.getElementById('gear-add-status');

    const num = (v) => (v.trim() === '' ? null : parseFloat(v));
    const res = await fetch('/api/gear', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
            type: form.type.value,
            make: form.make.value.trim() || null,
            model: form.model.value.trim() || null,
            lengthInches: num(form.lengthInches.value),
            widthInches: num(form.widthInches.value),
            depthInches: num(form.depthInches.value),
            weightPounds: num(form.weightPounds.value)
        })
    });
    const body = await res.json();
    if (res.ok) {
        status.textContent = '';
        form.reset();
        await loadGear();
    } else {
        status.textContent = body.error || 'Could not add gear.';
    }
});

loadGearTypes();
loadGear();
loadUspsConfigured();

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
        document.getElementById('must-change-password-banner').hidden = true;
    } else {
        status.textContent = body.error || 'Could not change password.';
    }
});

// --- Notification preferences ---
const NOTIFICATION_KIND_LABELS = {
    SongEditReviewed: 'A song edit you proposed was approved/rejected',
    GigReminder: 'Upcoming gig',
    RehearsalReminder: 'Upcoming rehearsal',
    AvailabilityReminder: "Reminder to set your availability",
    ResponsibilityChanged: "You've been assigned to a task"
};

async function loadNotificationPrefs() {
    const res = await fetch('/api/notification-preferences');
    const prefs = res.ok ? await res.json() : [];
    const list = document.getElementById('notification-prefs-list');
    list.innerHTML = '';

    for (const pref of prefs) {
        const row = document.createElement('div');
        row.className = 'notification-pref-row';
        row.dataset.kind = pref.kind;
        row.innerHTML = `
            <p class="notification-pref-label">${NOTIFICATION_KIND_LABELS[pref.kind] || pref.kind}</p>
            <label class="checkbox-label"><input type="checkbox" name="email" ${pref.emailEnabled ? 'checked' : ''}> Email</label>
            <label class="checkbox-label"><input type="checkbox" name="inApp" ${pref.inAppEnabled ? 'checked' : ''}> In-app</label>
            ${pref.leadTimeApplies ? `<label>Days ahead <input type="number" name="leadTimeDays" min="0" max="60" value="${pref.leadTimeDays ?? 1}"></label>` : ''}
        `;
        list.appendChild(row);
    }
}

document.getElementById('notification-prefs-save').addEventListener('click', async () => {
    const status = document.getElementById('notification-prefs-status');
    const items = Array.from(document.querySelectorAll('.notification-pref-row')).map((row) => {
        const leadInput = row.querySelector('[name="leadTimeDays"]');
        return {
            kind: row.dataset.kind,
            emailEnabled: row.querySelector('[name="email"]').checked,
            inAppEnabled: row.querySelector('[name="inApp"]').checked,
            leadTimeDays: leadInput ? parseInt(leadInput.value, 10) : null
        };
    });

    const res = await fetch('/api/notification-preferences', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(items)
    });
    status.textContent = res.ok ? 'Notification settings saved.' : 'Could not save notification settings.';
});

loadNotificationPrefs();

loadMe();
