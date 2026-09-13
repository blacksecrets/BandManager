// Profile > My Expenses > Travel - cadence/vehicle (independently savable)
// plus the Trips DataGrid and its +New Trip / edit modal. See
// TravelController.cs and Travel.cs for the server-side shape and the
// reasoning behind the home-address-snapshot behavior.
function escapeHtmlTravel(str) {
    const div = document.createElement('div');
    div.textContent = str ?? '';
    return div.innerHTML;
}

// --- Cadence + vehicle ---

async function loadTravelProfile() {
    const res = await fetch('/api/travel/profile');
    if (!res.ok) return;
    const p = await res.json();

    const cadenceForm = document.getElementById('travel-cadence-form');
    const checked = cadenceForm.querySelector(`input[name="cadence"][value="${p.cadence}"]`);
    if (checked) checked.checked = true;

    const vehicleForm = document.getElementById('travel-vehicle-form');
    vehicleForm.vehicleMake.value = p.vehicleMake || '';
    vehicleForm.vehicleModel.value = p.vehicleModel || '';
    vehicleForm.vehicleYear.value = p.vehicleYear || '';
    vehicleForm.startingMileage.value = p.startingMileage ?? '';

    return p;
}

document.getElementById('travel-cadence-form').addEventListener('submit', async (e) => {
    e.preventDefault();
    const form = e.target;
    const cadence = form.querySelector('input[name="cadence"]:checked')?.value;
    const status = document.getElementById('travel-cadence-status');
    if (!cadence) { status.textContent = 'Pick a cadence first.'; return; }
    const res = await fetch('/api/travel/cadence', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ cadence })
    });
    status.textContent = res.ok ? 'Cadence saved.' : 'Could not save.';
    if (res.ok) renderTravelTripsGrid();
});

document.getElementById('travel-vehicle-form').addEventListener('submit', async (e) => {
    e.preventDefault();
    const form = e.target;
    const status = document.getElementById('travel-vehicle-status');
    const res = await fetch('/api/travel/vehicle', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
            vehicleMake: form.vehicleMake.value.trim(),
            vehicleModel: form.vehicleModel.value.trim(),
            vehicleYear: form.vehicleYear.value ? Number(form.vehicleYear.value) : null,
            startingMileage: form.startingMileage.value ? Number(form.startingMileage.value) : null
        })
    });
    status.textContent = res.ok ? 'Vehicle saved.' : 'Could not save.';
});

// --- Trips grid ---

let travelTrips = [];
let travelCadence = 'Yearly';
let travelTripsGrid = null;

// The current tax period's bounds, for the footer total - this calendar
// year, or this calendar quarter, depending on the saved cadence.
function currentPeriodBounds() {
    const now = new Date();
    if (travelCadence === 'Quarterly') {
        const qStartMonth = Math.floor(now.getMonth() / 3) * 3;
        return { start: new Date(now.getFullYear(), qStartMonth, 1), end: new Date(now.getFullYear(), qStartMonth + 3, 1), label: 'this quarter' };
    }
    return { start: new Date(now.getFullYear(), 0, 1), end: new Date(now.getFullYear() + 1, 0, 1), label: 'this year' };
}

function tripEndpointDisplay(name, addressLine1, city, state) {
    if (name === 'Home') return 'Home';
    const parts = [name, [city, state].filter(Boolean).join(', ')].filter(Boolean);
    return parts.join(' - ') || addressLine1 || '—';
}

async function loadTravelGigs() {
    const res = await fetch('/api/travel/gigs');
    return res.ok ? await res.json() : [];
}

function renderTravelTripsGrid() {
    const container = document.getElementById('travel-trips-grid');
    const columns = [
        { key: 'date', label: 'Date', sortValue: (t) => t.date, render: (t) => t.date },
        { key: 'from', label: 'From', searchValue: (t) => t.fromName || '', render: (t) => escapeHtmlTravel(tripEndpointDisplay(t.fromName, t.fromAddressLine1, t.fromCity, t.fromState)) },
        { key: 'to', label: 'To', searchValue: (t) => t.toName || '', render: (t) => escapeHtmlTravel(tripEndpointDisplay(t.toName, t.toAddressLine1, t.toCity, t.toState)) },
        { key: 'reason', label: 'Reason', searchValue: (t) => t.reasonDisplay, render: (t) => escapeHtmlTravel(t.reasonDisplay) },
        { key: 'roundTrip', label: 'Round Trip', sortable: false, searchable: false, render: (t) => t.roundTrip ? 'Round Trip' : '' },
        { key: 'distance', label: 'Distance (mi)', sortValue: (t) => t.totalMiles ?? -1, searchable: false, render: (t) => t.totalMiles != null ? t.totalMiles : '—' }
    ];

    if (travelTripsGrid) {
        travelTripsGrid.setRows(travelTrips);
        return;
    }

    travelTripsGrid = window.DataGrid.render(container, {
        columns,
        rows: travelTrips,
        getRowId: (t) => t.id,
        defaultSortKey: 'date',
        defaultSortDir: 'desc',
        emptyMessage: 'No trips logged yet - use + New Trip above to add one.',
        onRowClick: (t) => openTripModal(t),
        renderFooter: (filtered) => {
            const { start, end, label } = currentPeriodBounds();
            const inPeriod = filtered.filter((t) => { const d = new Date(t.date); return d >= start && d < end; });
            const total = inPeriod.reduce((sum, t) => sum + (t.totalMiles || 0), 0);
            return `<tr><td colspan="${columns.length}">Total mileage ${label}: ${Math.round(total * 10) / 10} mi across ${inPeriod.length} trip${inPeriod.length === 1 ? '' : 's'}</td></tr>`;
        }
    });
}

async function loadTravelTrips() {
    const res = await fetch('/api/travel/trips');
    travelTrips = res.ok ? await res.json() : [];
    renderTravelTripsGrid();
}

async function initTravel() {
    const profile = await loadTravelProfile();
    travelCadence = profile?.cadence || 'Yearly';
    await loadTravelTrips();
}

// --- Trip modal ---

let editingTripId = null;
let fromResolved = false;
let toResolved = false;
let pendingReuseEndpoint = null; // 'from' | 'to' while the reuse-location prompt is open

function tripForm() { return document.getElementById('trip-form'); }

function endpointFieldset(prefix) { return tripForm().querySelector(`fieldset[data-endpoint="${prefix}"]`); }

function setEndpointVisibility(prefix) {
    const fieldset = endpointFieldset(prefix);
    const isHome = fieldset.querySelector(`input[name="${prefix}IsHome"]`).checked;
    fieldset.querySelector('.trip-endpoint-manual').hidden = isHome;
}

function endpointComplete(prefix) {
    const form = tripForm();
    const isHome = form[`${prefix}IsHome`].checked;
    if (isHome) return true;
    return !!(form[`${prefix}Name`].value.trim() && form[`${prefix}AddressLine1`].value.trim() &&
        form[`${prefix}City`].value.trim() && form[`${prefix}State`].value.trim() && form[`${prefix}PostalCode`].value.trim());
}

function updateTripSaveButton() {
    const form = tripForm();
    const dateOk = !!form.date.value;
    const reason = form.querySelector('input[name="reason"]:checked')?.value;
    const reasonOk = reason === 'Other' ? !!form.otherReasonText.value.trim() : reason === 'Gig' ? !!form.gigRef.value : true;
    document.getElementById('trip-save-btn').disabled = !(dateOk && reasonOk && endpointComplete('from') && endpointComplete('to'));
}

function wireEndpointFieldset(prefix) {
    const fieldset = endpointFieldset(prefix);
    fieldset.querySelector(`input[name="${prefix}IsHome"]`).addEventListener('change', () => {
        setEndpointVisibility(prefix);
        updateTripSaveButton();
    });
    fieldset.querySelectorAll('.trip-endpoint-manual input').forEach((input) => {
        input.addEventListener('input', () => {
            if (input.name === `${prefix}Name`) { if (prefix === 'from') fromResolved = false; else toResolved = false; }
            updateTripSaveButton();
        });
    });
}
wireEndpointFieldset('from');
wireEndpointFieldset('to');

tripForm().querySelectorAll('input[name="reason"]').forEach((radio) => {
    radio.addEventListener('change', async () => {
        const reason = radio.value;
        document.getElementById('trip-gig-picker').hidden = reason !== 'Gig';
        document.getElementById('trip-other-reason').hidden = reason !== 'Other';
        if (reason === 'Gig') await populateGigPicker();
        updateTripSaveButton();
    });
});
tripForm().otherReasonText.addEventListener('input', updateTripSaveButton);
tripForm().date.addEventListener('input', updateTripSaveButton);

async function populateGigPicker() {
    const select = tripForm().gigRef;
    if (select.dataset.loaded) return;
    const gigs = await loadTravelGigs();
    select.innerHTML = gigs.map((g) => `<option value="${escapeHtmlTravel(g.gigRef)}">${escapeHtmlTravel(g.title)} (${g.date})</option>`).join('');
    select.dataset.loaded = '1';
}
tripForm().gigRef.addEventListener('change', updateTripSaveButton);

function resetTripForm() {
    const form = tripForm();
    form.reset();
    form.date.value = new Date().toISOString().slice(0, 10);
    form.fromIsHome.checked = true;
    form.toIsHome.checked = false;
    setEndpointVisibility('from');
    setEndpointVisibility('to');
    document.getElementById('trip-gig-picker').hidden = true;
    document.getElementById('trip-other-reason').hidden = true;
    document.getElementById('trip-status').textContent = '';
    tripForm().gigRef.dataset.loaded = '';
    fromResolved = false;
    toResolved = false;
    editingTripId = null;
}

function openTripModal(trip) {
    resetTripForm();
    const form = tripForm();
    if (trip) {
        editingTripId = trip.id;
        document.getElementById('trip-modal-title').textContent = 'Edit Trip';
        form.date.value = trip.date;
        form.fromIsHome.checked = trip.fromIsHome;
        form.fromName.value = trip.fromIsHome ? '' : (trip.fromName || '');
        form.fromAddressLine1.value = trip.fromAddressLine1 || '';
        form.fromCity.value = trip.fromCity || '';
        form.fromState.value = trip.fromState || '';
        form.fromPostalCode.value = trip.fromPostalCode || '';
        form.toIsHome.checked = trip.toIsHome;
        form.toName.value = trip.toIsHome ? '' : (trip.toName || '');
        form.toAddressLine1.value = trip.toAddressLine1 || '';
        form.toCity.value = trip.toCity || '';
        form.toState.value = trip.toState || '';
        form.toPostalCode.value = trip.toPostalCode || '';
        form.roundTrip.checked = trip.roundTrip;
        const reasonRadio = form.querySelector(`input[name="reason"][value="${trip.reason}"]`);
        if (reasonRadio) reasonRadio.checked = true;
        document.getElementById('trip-gig-picker').hidden = trip.reason !== 'Gig';
        document.getElementById('trip-other-reason').hidden = trip.reason !== 'Other';
        form.otherReasonText.value = trip.otherReasonText || '';
        setEndpointVisibility('from');
        setEndpointVisibility('to');
        fromResolved = true; // already a saved, resolved address - no reuse-prompt needed unless the name is edited
        toResolved = true;
        if (trip.reason === 'Gig') {
            populateGigPicker().then(() => { form.gigRef.value = trip.gigRef || ''; updateTripSaveButton(); });
        }
    } else {
        document.getElementById('trip-modal-title').textContent = 'New Trip';
    }
    updateTripSaveButton();
    document.getElementById('trip-modal-backdrop').hidden = false;
}

document.getElementById('travel-new-trip-btn').addEventListener('click', () => openTripModal(null));

function closeTripModal() { document.getElementById('trip-modal-backdrop').hidden = true; }
document.getElementById('trip-modal-close').addEventListener('click', closeTripModal);
document.getElementById('trip-cancel-btn').addEventListener('click', closeTripModal);
document.getElementById('trip-modal-backdrop').addEventListener('click', (e) => { if (e.target.id === 'trip-modal-backdrop') closeTripModal(); });

function endpointInput(prefix) {
    const form = tripForm();
    const isHome = form[`${prefix}IsHome`].checked;
    return {
        isHome,
        name: isHome ? null : form[`${prefix}Name`].value.trim(),
        addressLine1: isHome ? null : form[`${prefix}AddressLine1`].value.trim(),
        city: isHome ? null : form[`${prefix}City`].value.trim(),
        state: isHome ? null : form[`${prefix}State`].value.trim(),
        postalCode: isHome ? null : form[`${prefix}PostalCode`].value.trim()
    };
}

// Checks the band's location book for a manually-entered endpoint's name
// before it's ever sent to save - returns true if it's fine to proceed,
// false if the reuse-location prompt is now open and save should wait for
// the user's answer.
async function checkLocationReuse(prefix) {
    const form = tripForm();
    if (form[`${prefix}IsHome`].checked) return true;
    const resolved = prefix === 'from' ? fromResolved : toResolved;
    if (resolved) return true;

    const name = form[`${prefix}Name`].value.trim();
    if (!name) return true;

    const res = await fetch(`/api/travel/locations/find?name=${encodeURIComponent(name)}`);
    const body = res.ok ? await res.json() : { found: false };
    if (!body.found) {
        if (prefix === 'from') fromResolved = true; else toResolved = true;
        return true;
    }

    pendingReuseEndpoint = prefix;
    document.getElementById('trip-reuse-location-text').textContent =
        `"${body.name}" is already saved for this band at ${body.addressLine1}, ${body.city}, ${body.state} ${body.postalCode}. Use that address?`;
    document.getElementById('trip-reuse-location-modal-backdrop').hidden = false;
    return false;
}

document.getElementById('trip-reuse-location-yes-btn').addEventListener('click', async () => {
    const prefix = pendingReuseEndpoint;
    const res = await fetch(`/api/travel/locations/find?name=${encodeURIComponent(tripForm()[`${prefix}Name`].value.trim())}`);
    const body = await res.json();
    const form = tripForm();
    form[`${prefix}Name`].value = body.name;
    form[`${prefix}AddressLine1`].value = body.addressLine1;
    form[`${prefix}City`].value = body.city;
    form[`${prefix}State`].value = body.state;
    form[`${prefix}PostalCode`].value = body.postalCode;
    if (prefix === 'from') fromResolved = true; else toResolved = true;
    document.getElementById('trip-reuse-location-modal-backdrop').hidden = true;
    pendingReuseEndpoint = null;
    await attemptSaveTrip();
});

document.getElementById('trip-reuse-location-no-btn').addEventListener('click', () => {
    const prefix = pendingReuseEndpoint;
    document.getElementById('trip-reuse-location-modal-backdrop').hidden = true;
    pendingReuseEndpoint = null;
    tripForm()[`${prefix}Name`].focus();
});

// --- Home address completion sub-flow ---

document.getElementById('trip-home-address-cancel-btn').addEventListener('click', () => {
    document.getElementById('trip-home-address-modal-backdrop').hidden = true;
});

document.getElementById('trip-home-address-form').addEventListener('submit', async (e) => {
    e.preventDefault();
    const form = e.target;
    const status = document.getElementById('trip-home-address-status');
    const meRes = await fetch('/api/profile/me');
    const me = meRes.ok ? await meRes.json() : {};
    const res = await fetch('/api/profile/contact', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
            cellNumber: me.cellNumber || '',
            addressLine1: form.addressLine1.value.trim(),
            addressLine2: form.addressLine2.value.trim(),
            city: form.city.value.trim(),
            state: form.state.value.trim(),
            postalCode: form.postalCode.value.trim()
        })
    });
    if (!res.ok) { status.textContent = 'Could not save that address.'; return; }
    document.getElementById('trip-home-address-modal-backdrop').hidden = true;
    await attemptSaveTrip();
});

async function openHomeAddressModal() {
    const meRes = await fetch('/api/profile/me');
    const me = meRes.ok ? await meRes.json() : {};
    const form = document.getElementById('trip-home-address-form');
    form.addressLine1.value = me.addressLine1 || '';
    form.addressLine2.value = me.addressLine2 || '';
    form.city.value = me.city || '';
    form.state.value = me.state || '';
    form.postalCode.value = me.postalCode || '';
    document.getElementById('trip-home-address-status').textContent = '';
    document.getElementById('trip-home-address-modal-backdrop').hidden = false;
}

async function attemptSaveTrip() {
    const form = tripForm();
    const status = document.getElementById('trip-status');

    if (!(await checkLocationReuse('from'))) return;
    if (!(await checkLocationReuse('to'))) return;

    const reason = form.querySelector('input[name="reason"]:checked')?.value;
    const payload = {
        date: form.date.value,
        from: endpointInput('from'),
        to: endpointInput('to'),
        roundTrip: form.roundTrip.checked,
        reason,
        otherReasonText: reason === 'Other' ? form.otherReasonText.value.trim() : null,
        gigRef: reason === 'Gig' ? form.gigRef.value : null
    };

    status.textContent = 'Saving...';
    const url = editingTripId ? `/api/travel/trips/${editingTripId}` : '/api/travel/trips';
    const res = await fetch(url, {
        method: editingTripId ? 'PUT' : 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload)
    });
    const body = await res.json().catch(() => ({}));

    if (!res.ok) {
        if (body.homeAddressIncomplete) {
            status.textContent = '';
            await openHomeAddressModal();
            return;
        }
        status.textContent = body.error || 'Could not save this trip.';
        return;
    }

    closeTripModal();
    // The save response is already the fully-serialized trip - update the
    // local list and re-render from it instead of re-fetching the whole
    // list, same pattern test-suite.js's saveTestCaseResult already uses.
    const idx = travelTrips.findIndex((t) => t.id === body.id);
    if (idx >= 0) travelTrips[idx] = body; else travelTrips.unshift(body);
    renderTravelTripsGrid();
}

tripForm().addEventListener('submit', async (e) => {
    e.preventDefault();
    await attemptSaveTrip();
});

initTravel();
