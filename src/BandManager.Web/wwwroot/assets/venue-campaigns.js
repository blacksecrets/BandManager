let isAdmin = false;
let gridRows = [];
let cadenceSteps = [];

function escapeHtml(str) {
    const div = document.createElement('div');
    div.textContent = str ?? '';
    return div.innerHTML;
}

function fmtDate(iso) {
    if (!iso) return '—';
    return new Date(iso).toLocaleDateString();
}

const STATUS_LABELS = {
    NotStarted: 'Not started',
    Active: 'Active',
    Paused: 'Paused (tentative)',
    CompleteBooked: 'Booked',
    CompleteRejected: 'Rejected'
};

async function init() {
    const res = await fetch('/api/profile/me');
    const me = await res.json();
    const hasBand = !!me.activeBandRole;
    isAdmin = !!me.isAdmin;

    document.getElementById('venue-campaigns-no-band').hidden = hasBand;
    document.getElementById('venue-campaigns-content').hidden = !hasBand;
    if (!hasBand) return;

    document.getElementById('cadence-steps-section').hidden = !isAdmin;

    await loadCadenceSteps();
    await loadGrid();
}

// --- Grid ---
async function loadGrid() {
    const res = await fetch('/api/venue-campaigns');
    gridRows = res.ok ? await res.json() : [];
    renderGrid();
}

function renderGrid() {
    const tbody = document.getElementById('venue-grid-body');
    tbody.innerHTML = '';
    if (gridRows.length === 0) {
        tbody.innerHTML = '<tr><td colspan="6">No venues yet - add one above to get started.</td></tr>';
        return;
    }
    for (const row of gridRows) {
        const tr = document.createElement('tr');
        tr.className = 'venue-grid-row' + (row.dueNow ? ' due-now' : '');
        tr.innerHTML = `
            <td>${escapeHtml(row.venueName)}</td>
            <td><span class="venue-status-badge status-${row.status}">${STATUS_LABELS[row.status] || row.status}</span></td>
            <td>${row.dueNow ? `<span class="due-badge">Due now (${escapeHtml(row.currentStepType || '')})</span>` : (row.dueDate ? `due ${fmtDate(row.dueDate)}` : '—')}</td>
            <td>${fmtDate(row.lastCommunicationAt)}</td>
            <td>${escapeHtml(row.primaryContactName || '')}${row.primaryContactEmail ? ` <small>${escapeHtml(row.primaryContactEmail)}</small>` : ''}</td>
            <td></td>
        `;
        const btn = document.createElement('button');
        btn.type = 'button';
        btn.className = 'remove-btn';
        btn.textContent = row.campaignId ? 'View' : 'Start campaign';
        btn.addEventListener('click', () => row.campaignId ? openCampaignDetail(row.campaignId) : startCampaign(row.venueId));
        tr.lastElementChild.appendChild(btn);
        tr.addEventListener('click', (e) => { if (e.target === btn) return; if (row.campaignId) openCampaignDetail(row.campaignId); });
        tbody.appendChild(tr);
    }
}

async function startCampaign(venueId) {
    const res = await fetch(`/api/venue-campaigns/${venueId}/start`, { method: 'POST' });
    const body = await res.json();
    if (!res.ok) { alert(body.error || 'Could not start campaign.'); return; }
    await loadGrid();
    openCampaignDetail(body.campaignId);
}

// --- Add venue ---
document.getElementById('venue-add-form').addEventListener('submit', async (e) => {
    e.preventDefault();
    const form = e.target;
    const status = document.getElementById('venue-add-status');

    const res = await fetch('/api/venues', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
            name: form.name.value.trim(),
            addressLine1: form.addressLine1.value.trim(),
            city: form.city.value.trim(),
            state: form.state.value.trim(),
            postalCode: form.postalCode.value.trim(),
            phone: form.phone.value.trim(),
            website: form.website.value.trim()
        })
    });
    const body = await res.json();
    if (!res.ok) { status.textContent = body.error || 'Could not add venue.'; return; }

    const contactName = form.contactName.value.trim();
    const contactEmail = form.contactEmail.value.trim();
    const contactPhone = form.contactPhone.value.trim();
    if (contactName || contactEmail || contactPhone) {
        await fetch(`/api/venues/${body.id}/contacts`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ name: contactName, email: contactEmail, phone: contactPhone, isPrimary: true })
        });
    }
    status.textContent = 'Venue added.';
    form.reset();
    await loadGrid();
});

// --- CSV imports ---
document.getElementById('venue-csv-upload').addEventListener('change', async () => {
    const input = document.getElementById('venue-csv-upload');
    if (!input.files[0]) return;
    const status = document.getElementById('venue-import-status');
    const errorsBox = document.getElementById('venue-import-errors');
    errorsBox.innerHTML = '';
    status.textContent = 'Importing...';

    const form = new FormData();
    form.append('file', input.files[0]);
    const res = await fetch('/api/venues/import', { method: 'POST', body: form });
    const body = await res.json();
    input.value = '';

    if (res.ok) {
        status.textContent = `${body.added} venue(s) added, ${body.skippedDuplicates} skipped (already exists).`;
        await loadGrid();
        return;
    }
    status.textContent = body.error || 'Import failed.';
    renderRowErrors(errorsBox, body.rowErrors);
});

document.getElementById('comm-csv-upload').addEventListener('change', async () => {
    const input = document.getElementById('comm-csv-upload');
    if (!input.files[0]) return;
    const status = document.getElementById('comm-import-status');
    const errorsBox = document.getElementById('comm-import-errors');
    errorsBox.innerHTML = '';
    status.textContent = 'Importing...';

    const form = new FormData();
    form.append('file', input.files[0]);
    const res = await fetch('/api/venues/import-communications', { method: 'POST', body: form });
    const body = await res.json();
    input.value = '';

    if (res.ok) {
        status.textContent = `${body.imported} communication(s) imported, ${body.skippedUnknownVenue} skipped (venue not found).`;
        await loadGrid();
        return;
    }
    status.textContent = body.error || 'Import failed.';
    renderRowErrors(errorsBox, body.rowErrors);
});

function renderRowErrors(box, rowErrors) {
    if (!Array.isArray(rowErrors) || rowErrors.length === 0) return;
    const table = document.createElement('table');
    table.className = 'user-table';
    table.innerHTML = '<thead><tr><th>Row</th><th>Column</th><th>Problem</th></tr></thead>';
    const tbody = document.createElement('tbody');
    for (const e of rowErrors) {
        const tr = document.createElement('tr');
        tr.innerHTML = `<td>${e.row}</td><td>${escapeHtml(e.column)}</td><td>${escapeHtml(e.message)}</td>`;
        tbody.appendChild(tr);
    }
    table.appendChild(tbody);
    box.appendChild(table);
}

// --- Cadence steps (Band Admin) ---
async function loadCadenceSteps() {
    const res = await fetch('/api/venue-cadence-steps');
    cadenceSteps = res.ok ? await res.json() : [];
    renderCadenceSteps();
}

function renderCadenceSteps() {
    const box = document.getElementById('cadence-steps-list');
    if (!box) return;
    box.innerHTML = '';
    if (cadenceSteps.length === 0) {
        box.innerHTML = '<p class="save-note">No cadence steps yet - add the first one (the initial contact) below.</p>';
    }
    for (const step of cadenceSteps) {
        const row = document.createElement('div');
        row.className = 'cadence-step-row' + (step.active ? '' : ' inactive');
        row.innerHTML = `
            <strong>Step ${step.stepNumber}</strong> - ${escapeHtml(step.type)}${step.stepNumber > 1 ? `, ${step.daysAfterPrevious} day(s) after previous` : ' (initial contact)'}
            ${step.defaultSubject ? `<br><em>${escapeHtml(step.defaultSubject)}</em>` : ''}
            <p class="save-note">${escapeHtml(step.defaultBody).slice(0, 140)}${step.defaultBody.length > 140 ? '...' : ''}</p>
        `;
        const removeBtn = document.createElement('button');
        removeBtn.type = 'button';
        removeBtn.className = 'remove-btn';
        removeBtn.textContent = 'Remove';
        removeBtn.addEventListener('click', async () => {
            if (!confirm(`Remove step ${step.stepNumber}? Campaigns already past this step are unaffected.`)) return;
            await fetch(`/api/venue-cadence-steps/${step.id}`, { method: 'DELETE' });
            await loadCadenceSteps();
        });
        row.appendChild(removeBtn);
        box.appendChild(row);
    }
}

document.getElementById('cadence-step-add-form').addEventListener('submit', async (e) => {
    e.preventDefault();
    const form = e.target;
    const status = document.getElementById('cadence-step-add-status');
    const res = await fetch('/api/venue-cadence-steps', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
            type: form.type.value,
            daysAfterPrevious: parseInt(form.daysAfterPrevious.value, 10) || 0,
            defaultSubject: form.defaultSubject.value.trim() || null,
            defaultBody: form.defaultBody.value.trim(),
            active: true
        })
    });
    const body = await res.json();
    if (!res.ok) { status.textContent = body.error || 'Could not add step.'; return; }
    status.textContent = '';
    form.reset();
    form.daysAfterPrevious.value = 0;
    await loadCadenceSteps();
});

// --- Campaign detail modal ---
function closeVenueDetailModal() { document.getElementById('venue-detail-modal-backdrop').hidden = true; }
document.getElementById('venue-detail-modal-close').addEventListener('click', closeVenueDetailModal);
document.getElementById('venue-detail-modal-backdrop').addEventListener('click', (e) => { if (e.target.id === 'venue-detail-modal-backdrop') closeVenueDetailModal(); });

async function openCampaignDetail(campaignId) {
    const body = document.getElementById('venue-detail-modal-body');
    body.innerHTML = '<p class="save-note">Loading...</p>';
    document.getElementById('venue-detail-modal-backdrop').hidden = false;

    const res = await fetch(`/api/venue-campaigns/${campaignId}`);
    if (!res.ok) { body.innerHTML = '<p class="save-note">Could not load this campaign.</p>'; return; }
    const c = await res.json();
    renderCampaignDetail(body, c);
}

function renderCampaignDetail(body, c) {
    body.innerHTML = '';

    const header = document.createElement('div');
    header.innerHTML = `
        <h2>${escapeHtml(c.venue.name)}</h2>
        <p class="save-note">
            ${[c.venue.addressLine1, c.venue.city, c.venue.state, c.venue.postalCode].filter(Boolean).map(escapeHtml).join(', ')}
            ${c.venue.phone ? ' · ' + escapeHtml(c.venue.phone) : ''}
            ${c.venue.website ? ` · <a href="${escapeHtml(c.venue.website)}" target="_blank">Website</a>` : ''}
        </p>
        <p><span class="venue-status-badge status-${c.status}">${STATUS_LABELS[c.status] || c.status}</span></p>
    `;
    body.appendChild(header);

    if (c.venue.contacts.length > 0) {
        const contactsBox = document.createElement('div');
        contactsBox.innerHTML = '<h3>Contacts</h3>';
        for (const ct of c.venue.contacts) {
            const p = document.createElement('p');
            p.className = 'save-note';
            p.textContent = `${ct.name || '(no name)'}${ct.title ? ' - ' + ct.title : ''}${ct.email ? ' · ' + ct.email : ''}${ct.phone ? ' · ' + ct.phone : ''}${ct.isPrimary ? ' (primary)' : ''}`;
            contactsBox.appendChild(p);
        }
        body.appendChild(contactsBox);
    }

    if (c.status === 'CompleteRejected') {
        const box = document.createElement('div');
        box.className = 'venue-status-detail';
        box.innerHTML = `<p><strong>Rejected:</strong> ${escapeHtml(c.rejectionReason || '')}</p>
            <p class="save-note">${c.neverRetry ? 'Never retry.' : c.retryDate ? `Auto-retries on ${fmtDate(c.retryDate)}.` : 'No retry date set.'}</p>`;
        body.appendChild(box);
    } else if (c.status === 'CompleteBooked') {
        const box = document.createElement('div');
        box.className = 'venue-status-detail';
        box.innerHTML = `<p><strong>Booked!</strong>${c.bookedGigId ? ' Linked to a gig in Gig Management.' : ''}</p>`;
        body.appendChild(box);
    }

    // Status action buttons
    const actions = document.createElement('div');
    actions.className = 'venue-detail-actions';
    if (c.status === 'Active') {
        actions.appendChild(makeActionBtn('Mark tentative / pause', () => setStatus(c.id, 'Paused', {})));
        actions.appendChild(makeActionBtn('Mark rejected', () => openRejectForm(body, c)));
        actions.appendChild(makeActionBtn('Mark booked', () => openBookForm(body, c)));
    } else if (c.status === 'Paused') {
        actions.appendChild(makeActionBtn('Resume (active)', () => setStatus(c.id, 'Active', {})));
        actions.appendChild(makeActionBtn('Mark rejected', () => openRejectForm(body, c)));
        actions.appendChild(makeActionBtn('Mark booked', () => openBookForm(body, c)));
    } else {
        actions.appendChild(makeActionBtn('Restart campaign', async () => {
            const res = await fetch(`/api/venue-campaigns/${c.venue.id}/start`, { method: 'POST' });
            if (res.ok) { await loadGrid(); openCampaignDetail(c.id); }
        }));
    }
    body.appendChild(actions);

    // Due-step contact panel
    if (c.status === 'Active' && c.nextStep) {
        body.appendChild(renderContactPanel(c));
    } else if (c.status === 'Active' && !c.nextStep) {
        const note = document.createElement('p');
        note.className = 'save-note';
        note.textContent = 'No more cadence steps configured - add more under "Outreach cadence" if you want further automatic follow-ups, or log a manual communication below.';
        body.appendChild(note);
        body.appendChild(renderManualLogForm(c));
    }

    // History
    const historyBox = document.createElement('div');
    historyBox.innerHTML = '<h3>Communication history</h3>';
    if (c.communications.length === 0) {
        historyBox.innerHTML += '<p class="save-note">No communications logged yet.</p>';
    }
    for (const m of c.communications) {
        const row = document.createElement('div');
        row.className = 'venue-comm-row';
        row.innerHTML = `
            <strong>${escapeHtml(m.type)}</strong> · ${fmtDate(m.occurredAt)} · logged by ${escapeHtml(m.loggedByName)}
            ${m.subject ? `<div><em>${escapeHtml(m.subject)}</em></div>` : ''}
            <div class="venue-comm-body">${escapeHtml(m.body)}</div>
            ${m.outcomeNotes ? `<div class="save-note">Outcome: ${escapeHtml(m.outcomeNotes)}</div>` : ''}
        `;
        historyBox.appendChild(row);
    }
    body.appendChild(historyBox);
}

function makeActionBtn(label, onClick) {
    const btn = document.createElement('button');
    btn.type = 'button';
    btn.textContent = label;
    btn.addEventListener('click', onClick);
    return btn;
}

async function setStatus(campaignId, status, extra) {
    const res = await fetch(`/api/venue-campaigns/${campaignId}/status`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ status, rejectionReason: null, retryDate: null, neverRetry: false, bookedGigId: null, ...extra })
    });
    if (res.ok) {
        await loadGrid();
        openCampaignDetail(campaignId);
    } else {
        const err = await res.json().catch(() => ({}));
        alert(err.error || 'Could not update status.');
    }
}

function openRejectForm(body, c) {
    const box = document.createElement('div');
    box.className = 'venue-status-detail';
    box.innerHTML = `
        <label>Reason <input type="text" id="reject-reason-input" maxlength="1000"></label>
        <label>Retry date (optional) <input type="date" id="reject-retry-input"></label>
        <label class="checkbox-label"><input type="checkbox" id="reject-never-input"> Never retry</label>
        <button type="button" id="reject-confirm-btn">Confirm rejection</button>
    `;
    body.appendChild(box);
    document.getElementById('reject-confirm-btn').addEventListener('click', () => {
        const reason = document.getElementById('reject-reason-input').value.trim();
        if (!reason) { alert('A reason is required.'); return; }
        const neverRetry = document.getElementById('reject-never-input').checked;
        const retryDate = document.getElementById('reject-retry-input').value || null;
        setStatus(c.id, 'CompleteRejected', { rejectionReason: reason, retryDate: neverRetry ? null : retryDate, neverRetry });
    });
}

function openBookForm(body, c) {
    const box = document.createElement('div');
    box.className = 'venue-status-detail';
    box.innerHTML = `
        <p class="save-note">Add the show now, using this venue's name and address - marking it booked and linking the gig happen together.</p>
        <button type="button" id="book-add-gig-btn">Add the Gig</button>
        <button type="button" id="book-confirm-btn">Mark booked (no gig link)</button>
    `;
    body.appendChild(box);
    document.getElementById('book-add-gig-btn').addEventListener('click', async () => {
        const created = await window.openAddGigModal({ prefillVenue: c.venue });
        if (!created) return;
        await setStatus(c.id, 'CompleteBooked', { bookedGigId: created.gigId });
    });
    document.getElementById('book-confirm-btn').addEventListener('click', () => setStatus(c.id, 'CompleteBooked', {}));
}

function renderContactPanel(c) {
    const step = c.nextStep;
    const panel = document.createElement('div');
    panel.className = 'venue-contact-panel';
    panel.innerHTML = `<h3>${c.dueNow ? 'Due now' : `Due ${fmtDate(c.dueDate)}`}: Step ${step.stepNumber} (${escapeHtml(step.type)})</h3>`;

    const primaryContact = c.venue.contacts.find((ct) => ct.isPrimary) || c.venue.contacts[0];

    if (step.type === 'Email') {
        const form = document.createElement('form');
        form.className = 'cred-form';
        form.innerHTML = `
            <label>To <input type="email" name="to" value="${escapeHtml(primaryContact?.email || '')}"></label>
            <label>Cc <input type="email" name="cc"></label>
            <label>Bcc <input type="email" name="bcc"></label>
            <label>From <input type="email" name="from"></label>
            <label>Subject <input type="text" name="subject" value="${escapeHtml(step.subject || '')}"></label>
            <label>Body <textarea name="body" rows="6">${escapeHtml(step.body)}</textarea></label>
            <label>Outcome notes (optional) <input type="text" name="outcomeNotes"></label>
            <div class="venue-detail-actions">
                <button type="submit" name="action" value="send">Send email</button>
                <button type="submit" name="action" value="log">Log without sending</button>
            </div>
            <p class="save-note">Sending goes through this app's email delivery - see Setup if messages aren't being delivered for real yet.</p>
        `;
        form.addEventListener('submit', async (e) => {
            e.preventDefault();
            const sendEmail = e.submitter?.value === 'send';
            await fetch(`/api/venue-campaigns/${c.id}/communications`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    type: 'Email', to: form.to.value.trim(), cc: form.cc.value.trim(), bcc: form.bcc.value.trim(),
                    from: form.from.value.trim(), subject: form.subject.value.trim(), body: form.body.value,
                    sendEmail, outcomeNotes: form.outcomeNotes.value.trim()
                })
            });
            await loadGrid();
            openCampaignDetail(c.id);
        });
        panel.appendChild(form);
    } else {
        const form = document.createElement('form');
        form.className = 'cred-form';
        form.innerHTML = `
            <label>Script (editable) <textarea name="body" rows="6">${escapeHtml(step.body)}</textarea></label>
            <label>Outcome notes <input type="text" name="outcomeNotes" placeholder="What happened on the call?"></label>
            <button type="submit">Log completed call</button>
        `;
        form.addEventListener('submit', async (e) => {
            e.preventDefault();
            await fetch(`/api/venue-campaigns/${c.id}/communications`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    type: 'Phone', to: primaryContact?.phone || null, from: null, subject: null,
                    body: form.body.value, sendEmail: false, outcomeNotes: form.outcomeNotes.value.trim()
                })
            });
            await loadGrid();
            openCampaignDetail(c.id);
        });
        panel.appendChild(form);
    }
    return panel;
}

function renderManualLogForm(c) {
    const panel = document.createElement('div');
    panel.className = 'venue-contact-panel';
    panel.innerHTML = '<h3>Log a communication</h3>';
    const form = document.createElement('form');
    form.className = 'cred-form';
    form.innerHTML = `
        <label>Type <select name="type"><option value="Email">Email</option><option value="Phone">Phone</option></select></label>
        <label>To <input type="text" name="to"></label>
        <label>Subject <input type="text" name="subject"></label>
        <label>Body / notes <textarea name="body" rows="4" required></textarea></label>
        <label>Outcome notes <input type="text" name="outcomeNotes"></label>
        <button type="submit">Log it</button>
    `;
    form.addEventListener('submit', async (e) => {
        e.preventDefault();
        await fetch(`/api/venue-campaigns/${c.id}/communications`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                type: form.type.value, to: form.to.value.trim(), subject: form.subject.value.trim(),
                body: form.body.value, sendEmail: false, outcomeNotes: form.outcomeNotes.value.trim()
            })
        });
        await loadGrid();
        openCampaignDetail(c.id);
    });
    panel.appendChild(form);
    return panel;
}

init();
