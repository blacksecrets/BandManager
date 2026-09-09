// Shared "Add a Gig" modal - Gig Management's own "+ Add a Gig" button, and
// Venue Campaigns' "Mark booked" flow when a campaign turns into a real
// show (see venue-campaigns.js). Self-injecting like assignee.js/branding.js
// - just include this script tag and call window.openAddGigModal(options).
//
// options: { prefillVenue?: { id, name, addressLine1, city, state,
// postalCode, website, phone } } - used when a Venue Campaign already knows
// which venue booked the show, so it doesn't have to be picked again.
//
// Returns a Promise<{ gigRef, gigId, title } | null> - null if cancelled.

(function () {
    let backdrop = null;
    let venueModalBackdrop = null;
    let venues = [];
    let acts = [];
    let selectedVenue = null; // { id, name, addressLine1, city, state, postalCode, ... } | null
    let resolvePromise = null;

    function escapeHtml(str) {
        const div = document.createElement('div');
        div.textContent = str ?? '';
        return div.innerHTML;
    }

    function formatVenueAddress(v) {
        return [v.addressLine1, [v.city, v.state].filter(Boolean).join(', '), v.postalCode].filter(Boolean).join(' · ');
    }

    function composeGigAddress(v) {
        return [v.addressLine1, [v.city, v.state].filter(Boolean).join(', '), v.postalCode].filter(Boolean).join(', ');
    }

    // --- With-acts control (same shape as dashboard.js's buildWithActsControl -
    // duplicated here since this modal is shared across pages that don't all
    // load dashboard.js, matching this codebase's existing per-file convention.)
    function buildWithActsControl(container, initial) {
        let rows = initial.length > 0 ? initial.map((r) => ({ name: r.name || '', url: r.url || '' })) : [{ name: '', url: '' }];
        function render() {
            container.innerHTML = '';
            rows.forEach((row, i) => {
                const rowEl = document.createElement('div');
                rowEl.className = 'with-act-row';
                rowEl.innerHTML = `
                    <input type="text" class="with-act-name" placeholder="e.g. Attica - A Nirvana Tribute" maxlength="200" value="${escapeHtml(row.name)}">
                    <input type="text" class="with-act-url" placeholder="https:// (optional)" maxlength="500" value="${escapeHtml(row.url)}">
                    <button type="button" class="remove-with-act-btn" ${rows.length === 1 ? 'disabled' : ''}>&times;</button>
                `;
                rowEl.querySelector('.with-act-name').addEventListener('input', (e) => { rows[i].name = e.target.value; });
                rowEl.querySelector('.with-act-url').addEventListener('input', (e) => { rows[i].url = e.target.value; });
                rowEl.querySelector('.remove-with-act-btn').addEventListener('click', () => { rows.splice(i, 1); render(); });
                container.appendChild(rowEl);
            });
        }
        render();
        return {
            getValues: () => rows.filter((r) => r.name.trim() || r.url.trim()).map((r) => ({ name: r.name.trim(), url: r.url.trim() })),
            addRow: () => { rows.push({ name: '', url: '' }); render(); },
            reset: () => { rows = [{ name: '', url: '' }]; render(); }
        };
    }

    let withCtl = null;

    function ensureBackdrop() {
        if (backdrop) return backdrop;
        backdrop = document.createElement('div');
        backdrop.className = 'detail-modal-backdrop add-gig-modal-backdrop';
        backdrop.hidden = true;
        backdrop.innerHTML = `
            <div class="detail-modal add-gig-modal">
                <button type="button" class="detail-modal-close" data-close>&times;</button>
                <h2>Add a Gig</h2>
                <form id="add-gig-form" class="adhoc-form">
                    <label>Title <input type="text" name="title" maxlength="200" required></label>

                    <div class="add-gig-venue-section">
                        <label class="add-gig-venue-label">Venue <span class="required-star">*</span></label>
                        <div id="add-gig-venue-selected" class="add-gig-venue-selected" hidden></div>
                        <div id="add-gig-venue-picker">
                            <div id="add-gig-venue-list" class="add-gig-venue-list"><p class="save-note">Loading venues...</p></div>
                            <button type="button" id="add-gig-venue-manual-toggle">+ Enter a new venue</button>
                            <div id="add-gig-venue-manual" class="add-gig-venue-manual" hidden>
                                <label>Venue name <input type="text" id="new-venue-name" maxlength="300"></label>
                                <label>Address <input type="text" id="new-venue-address1" maxlength="200" placeholder="Street address"></label>
                                <label>City <input type="text" id="new-venue-city" maxlength="100"></label>
                                <label>State <input type="text" id="new-venue-state" maxlength="2"></label>
                                <label>ZIP <input type="text" id="new-venue-zip" maxlength="10"></label>
                                <label>Phone <input type="tel" id="new-venue-phone" maxlength="30"></label>
                                <label>Website <input type="url" id="new-venue-website" maxlength="300"></label>
                                <p class="save-note">Name and address are required - this saves as a new venue in your venue book.</p>
                            </div>
                        </div>
                        <p id="add-gig-venue-status" class="save-note"></p>
                    </div>

                    <label>Venue link (optional) <input type="text" name="venueUrl" placeholder="https://" maxlength="500"></label>
                    <label id="add-gig-act-label" hidden>Act <select name="actId" id="add-gig-act-select"></select></label>
                    <label>Date <input type="date" name="date" required></label>
                    <label>Time (optional) <input type="text" name="time" placeholder="e.g. Doors: 7PM - Show: 8PM" maxlength="100"></label>
                    <label>Doors time (optional) <input type="text" name="doorsTime" placeholder="e.g. 7:00 PM" maxlength="60"></label>
                    <label>Opener start time (optional) <input type="text" name="openerTime" placeholder="e.g. 8:00 PM" maxlength="60"></label>
                    <label>Headliner start time (optional) <input type="text" name="headlinerTime" placeholder="e.g. 9:00 PM" maxlength="60"></label>
                    <label>With / Openers (optional)</label>
                    <div class="with-acts-list" data-with-list></div>
                    <button type="button" class="add-with-btn">+ Add another "With"</button>
                    <fieldset class="ticket-mode-fieldset">
                        <legend>Ticket info (optional - can be added later)</legend>
                        <label class="radio-label"><input type="radio" name="ticketMode" value="url" checked> Tickets URL</label>
                        <input type="text" name="ticketsUrl" maxlength="500" placeholder="https://...">
                        <label class="radio-label"><input type="radio" name="ticketMode" value="free"> Free Admission</label>
                        <label class="radio-label"><input type="radio" name="ticketMode" value="custom"> Custom Tickets Button text</label>
                        <input type="text" name="customTicketsText" maxlength="60" placeholder='e.g. "$5 Door Cover"'>
                    </fieldset>
                    <div class="adhoc-form-buttons">
                        <button type="submit">Add Gig</button>
                        <button type="button" data-cancel>Cancel</button>
                    </div>
                    <p id="add-gig-status" class="save-note"></p>
                </form>
            </div>
        `;
        document.body.appendChild(backdrop);
        wireBackdrop();
        return backdrop;
    }

    function wireBackdrop() {
        backdrop.querySelector('[data-close]').addEventListener('click', () => finish(null));
        backdrop.querySelector('[data-cancel]').addEventListener('click', () => finish(null));
        backdrop.addEventListener('click', (e) => { if (e.target === backdrop) finish(null); });

        const form = document.getElementById('add-gig-form');
        withCtl = buildWithActsControl(form.querySelector('[data-with-list]'), []);
        form.querySelector('.add-with-btn').addEventListener('click', () => withCtl.addRow());

        function updateTicketModeUI() {
            form.ticketsUrl.disabled = form.ticketMode.value !== 'url';
            form.customTicketsText.disabled = form.ticketMode.value !== 'custom';
        }
        form.querySelectorAll('input[name="ticketMode"]').forEach((r) => r.addEventListener('change', updateTicketModeUI));
        updateTicketModeUI();

        document.getElementById('add-gig-venue-manual-toggle').addEventListener('click', () => {
            clearSelectedVenue();
            const manual = document.getElementById('add-gig-venue-manual');
            manual.hidden = !manual.hidden;
        });

        form.addEventListener('submit', onSubmit);
    }

    function clearSelectedVenue() {
        selectedVenue = null;
        const sel = document.getElementById('add-gig-venue-selected');
        sel.hidden = true;
        sel.innerHTML = '';
        document.getElementById('add-gig-venue-picker').hidden = false;
    }

    function showSelectedVenue(v) {
        selectedVenue = v;
        document.getElementById('add-gig-venue-manual').hidden = true;
        document.getElementById('add-gig-venue-picker').hidden = true;
        const sel = document.getElementById('add-gig-venue-selected');
        sel.hidden = false;
        sel.innerHTML = `
            <span class="add-gig-venue-selected-name">${escapeHtml(v.name)}</span>
            <span class="add-gig-venue-selected-address">${escapeHtml(formatVenueAddress(v)) || 'No address on file'}</span>
            <button type="button" id="add-gig-venue-change-btn">Change venue</button>
        `;
        sel.querySelector('#add-gig-venue-change-btn').addEventListener('click', () => clearSelectedVenue());
    }

    async function loadVenues() {
        const box = document.getElementById('add-gig-venue-list');
        try {
            const res = await fetch('/api/venues');
            venues = res.ok ? await res.json() : [];
        } catch { venues = []; }
        renderVenueList(box);
    }

    // Stays out of the way for the common single-Act band - the picker
    // only appears once there's an actual choice to make.
    async function loadActs() {
        try {
            const res = await fetch('/api/acts');
            acts = res.ok ? await res.json() : [];
        } catch { acts = []; }
        const label = document.getElementById('add-gig-act-label');
        const select = document.getElementById('add-gig-act-select');
        label.hidden = acts.length <= 1;
        select.innerHTML = acts.map((a) => `<option value="${a.id}" ${a.isDefault ? 'selected' : ''}>${escapeHtml(a.name)}</option>`).join('');
    }

    function renderVenueList(box) {
        box.innerHTML = '';
        if (venues.length === 0) {
            box.innerHTML = '<p class="save-note">No venues in your venue book yet - enter a new one below.</p>';
            return;
        }
        for (const v of venues) {
            const row = document.createElement('button');
            row.type = 'button';
            row.className = 'add-gig-venue-row';
            row.innerHTML = `
                <span class="add-gig-venue-name">${escapeHtml(v.name)}</span>
                <span class="add-gig-venue-address">${escapeHtml(formatVenueAddress(v)) || 'No address on file'}</span>
            `;
            row.addEventListener('click', () => openVenueDetailModal(v));
            box.appendChild(row);
        }
    }

    // --- Venue view/edit modal - clicking a venue in the list pops this,
    // instead of selecting it outright, so an out-of-date address can be
    // fixed right here before it's used on the new gig.
    function ensureVenueDetailModal() {
        if (venueModalBackdrop) return venueModalBackdrop;
        venueModalBackdrop = document.createElement('div');
        venueModalBackdrop.className = 'detail-modal-backdrop add-gig-venue-detail-backdrop';
        venueModalBackdrop.hidden = true;
        venueModalBackdrop.innerHTML = `
            <div class="detail-modal">
                <button type="button" class="detail-modal-close" data-venue-close>&times;</button>
                <div id="add-gig-venue-detail-body"></div>
            </div>
        `;
        document.body.appendChild(venueModalBackdrop);
        venueModalBackdrop.querySelector('[data-venue-close]').addEventListener('click', closeVenueDetailModal);
        venueModalBackdrop.addEventListener('click', (e) => { if (e.target === venueModalBackdrop) closeVenueDetailModal(); });
        return venueModalBackdrop;
    }

    function closeVenueDetailModal() { if (venueModalBackdrop) venueModalBackdrop.hidden = true; }

    function openVenueDetailModal(v) {
        const modal = ensureVenueDetailModal();
        const body = modal.querySelector('#add-gig-venue-detail-body');
        body.innerHTML = `
            <h2>${escapeHtml(v.name)}</h2>
            <form id="add-gig-venue-detail-form" class="adhoc-form">
                <label>Venue name <input type="text" name="name" maxlength="300" required value="${escapeHtml(v.name)}"></label>
                <label>Address <input type="text" name="addressLine1" maxlength="200" value="${escapeHtml(v.addressLine1 || '')}"></label>
                <label>City <input type="text" name="city" maxlength="100" value="${escapeHtml(v.city || '')}"></label>
                <label>State <input type="text" name="state" maxlength="2" value="${escapeHtml(v.state || '')}"></label>
                <label>ZIP <input type="text" name="postalCode" maxlength="10" value="${escapeHtml(v.postalCode || '')}"></label>
                <label>Phone <input type="tel" name="phone" maxlength="30" value="${escapeHtml(v.phone || '')}"></label>
                <label>Website <input type="url" name="website" maxlength="300" value="${escapeHtml(v.website || '')}"></label>
                <label>Notes <textarea name="notes" rows="2" maxlength="1000">${escapeHtml(v.notes || '')}</textarea></label>
                <div class="adhoc-form-buttons">
                    <button type="submit">Save &amp; use this venue</button>
                    <button type="button" id="add-gig-venue-use-asis-btn">Use as-is</button>
                </div>
                <p id="add-gig-venue-detail-status" class="save-note"></p>
            </form>
        `;
        modal.hidden = false;

        body.querySelector('#add-gig-venue-use-asis-btn').addEventListener('click', () => {
            closeVenueDetailModal();
            showSelectedVenue(v);
        });

        body.querySelector('#add-gig-venue-detail-form').addEventListener('submit', async (e) => {
            e.preventDefault();
            const form = e.target;
            const status = form.querySelector('#add-gig-venue-detail-status');
            const name = form.name.value.trim();
            if (!name) { status.textContent = 'Venue name is required.'; return; }
            const payload = {
                name,
                addressLine1: form.addressLine1.value.trim() || null,
                city: form.city.value.trim() || null,
                state: form.state.value.trim() || null,
                postalCode: form.postalCode.value.trim() || null,
                phone: form.phone.value.trim() || null,
                website: form.website.value.trim() || null,
                notes: form.notes.value.trim() || null
            };
            status.textContent = 'Saving...';
            const res = await fetch(`/api/venues/${v.id}`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(payload)
            });
            const updated = await res.json();
            if (!res.ok) { status.textContent = updated.error || 'Could not save.'; return; }
            const idx = venues.findIndex((x) => x.id === v.id);
            if (idx >= 0) venues[idx] = updated;
            closeVenueDetailModal();
            showSelectedVenue(updated);
        });
    }

    // --- Submit ---
    async function onSubmit(e) {
        e.preventDefault();
        const form = e.target;
        const status = document.getElementById('add-gig-status');
        const venueStatus = document.getElementById('add-gig-venue-status');
        status.textContent = '';
        venueStatus.textContent = '';

        const manualVisible = !document.getElementById('add-gig-venue-manual').hidden;
        let venueId = null;
        let venueName = null;
        let venueAddress = null;

        if (selectedVenue) {
            venueId = selectedVenue.id;
            venueName = selectedVenue.name;
            venueAddress = composeGigAddress(selectedVenue) || (selectedVenue.addressLine1 || '');
        } else if (manualVisible) {
            const name = document.getElementById('new-venue-name').value.trim();
            const address1 = document.getElementById('new-venue-address1').value.trim();
            const city = document.getElementById('new-venue-city').value.trim();
            const state = document.getElementById('new-venue-state').value.trim();
            const zip = document.getElementById('new-venue-zip').value.trim();
            if (!name || !address1 || !city || !state || !zip) {
                venueStatus.textContent = 'Venue name and full address (street, city, state, ZIP) are required.';
                return;
            }
            const payload = {
                name, addressLine1: address1, city, state, postalCode: zip,
                phone: document.getElementById('new-venue-phone').value.trim() || null,
                website: document.getElementById('new-venue-website').value.trim() || null,
                notes: null
            };
            venueStatus.textContent = 'Saving venue...';
            const vRes = await fetch('/api/venues', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(payload)
            });
            const vResult = await vRes.json();
            if (!vRes.ok) { venueStatus.textContent = vResult.error || 'Could not save the venue.'; return; }
            venueStatus.textContent = '';
            venueId = vResult.id;
            venueName = vResult.name;
            venueAddress = composeGigAddress(vResult) || address1;
        } else {
            venueStatus.textContent = 'Select a venue from the list, or enter a new one.';
            return;
        }

        status.textContent = 'Adding gig...';
        const formData = new FormData(form);
        formData.set('venue', venueName);
        formData.set('address', venueAddress);
        formData.set('venueId', venueId);
        formData.set('with', JSON.stringify(withCtl.getValues()));

        const res = await fetch('/api/gigs', { method: 'POST', body: formData });
        const result = await res.json();
        if (!res.ok) { status.textContent = result.error || 'Could not add the gig.'; return; }

        finish({ gigRef: result.id, gigId: result.gigId, title: formData.get('title') });
    }

    function resetForm() {
        const form = document.getElementById('add-gig-form');
        form.reset();
        form.ticketsUrl.disabled = false;
        form.customTicketsText.disabled = true;
        withCtl.reset();
        clearSelectedVenue();
        document.getElementById('add-gig-venue-manual').hidden = true;
        document.getElementById('add-gig-status').textContent = '';
        document.getElementById('add-gig-venue-status').textContent = '';
        ['new-venue-name', 'new-venue-address1', 'new-venue-city', 'new-venue-state', 'new-venue-zip', 'new-venue-phone', 'new-venue-website']
            .forEach((id) => { document.getElementById(id).value = ''; });
    }

    function finish(result) {
        backdrop.hidden = true;
        closeVenueDetailModal();
        if (resolvePromise) { resolvePromise(result); resolvePromise = null; }
    }

    window.openAddGigModal = function (options) {
        ensureBackdrop();
        resetForm();
        backdrop.hidden = false;
        loadVenues();
        loadActs();
        if (options && options.prefillVenue) showSelectedVenue(options.prefillVenue);
        return new Promise((resolve) => { resolvePromise = resolve; });
    };
})();
