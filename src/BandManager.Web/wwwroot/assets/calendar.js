let currentDate = new Date(); // anchor date - a day in the visible month/week, or the visible day itself
let currentView = 'month'; // 'month' | 'week' | 'day'
let entriesByDate = {}; // "yyyy-MM-dd" -> array of entries
let availabilityByDate = {}; // "yyyy-MM-dd" -> array of {userId, firstName, status, note}
let bandMembers = []; // [{id, firstName}]
let me = null; // {id, isAdmin, ...}

const STATUSES = ['Available', 'Tentative', 'Unavailable'];

function escapeHtml(str) {
    const div = document.createElement('div');
    div.textContent = str ?? '';
    return div.innerHTML;
}

function pad2(n) { return String(n).padStart(2, '0'); }
function isoDate(d) { return `${d.getFullYear()}-${pad2(d.getMonth() + 1)}-${pad2(d.getDate())}`; }

async function init() {
    const res = await fetch('/api/profile/me');
    me = await res.json();
    const hasBand = !!me.activeBandRole;

    document.getElementById('calendar-no-band').hidden = hasBand;
    document.getElementById('calendar-content').hidden = !hasBand;
    if (!hasBand) return;

    document.getElementById('calendar-prev-month').addEventListener('click', () => { stepDate(-1); render(); });
    document.getElementById('calendar-next-month').addEventListener('click', () => { stepDate(1); render(); });
    document.getElementById('calendar-today-btn').addEventListener('click', () => { currentDate = new Date(); render(); });

    for (const tab of document.querySelectorAll('.calendar-view-tab')) {
        tab.classList.toggle('active', tab.dataset.view === currentView);
        tab.addEventListener('click', () => {
            currentView = tab.dataset.view;
            for (const t of document.querySelectorAll('.calendar-view-tab')) t.classList.toggle('active', t === tab);
            render();
        });
    }

    if (me.isAdmin) {
        const btn = document.getElementById('calendar-recurring-btn');
        btn.hidden = false;
        btn.addEventListener('click', openRecurringModal);
    }

    const membersRes = await fetch('/api/profile/band-members');
    bandMembers = membersRes.ok ? await membersRes.json() : [];

    initSyncPanel();

    await render();
}

async function initSyncPanel() {
    const res = await fetch('/api/calendar/feed-token');
    if (!res.ok) return;
    const { url } = await res.json();
    document.getElementById('calendar-feed-url').value = url;

    document.getElementById('calendar-feed-copy').addEventListener('click', async () => {
        const status = document.getElementById('calendar-feed-copy-status');
        try {
            await navigator.clipboard.writeText(url);
            status.textContent = 'Copied.';
        } catch {
            status.textContent = 'Could not copy automatically - select and copy the URL manually.';
        }
    });

    document.getElementById('calendar-export-btn').addEventListener('click', () => {
        location.href = '/api/calendar/export.ics';
    });

    await initExternalCalendarButtons();
}

async function initExternalCalendarButtons() {
    const [providersRes, connectionsRes] = await Promise.all([
        fetch('/api/external-calendar/providers'),
        fetch('/api/external-calendar/connections')
    ]);
    const providers = providersRes.ok ? await providersRes.json() : { google: false, outlook: false };
    const connections = connectionsRes.ok ? await connectionsRes.json() : [];
    const connectedProviders = new Set(connections.map((c) => c.provider.toLowerCase()));

    for (const provider of ['google', 'outlook']) {
        const connectBtn = document.getElementById(`calendar-connect-${provider}`);
        const disconnectBtn = document.getElementById(`calendar-disconnect-${provider}`);
        const setupNote = document.getElementById(`calendar-${provider}-setup-note`);
        if (!providers[provider]) {
            connectBtn.hidden = true;
            disconnectBtn.hidden = true;
            setupNote.hidden = false;
            continue;
        }
        setupNote.hidden = true;

        const isConnected = connectedProviders.has(provider);
        connectBtn.hidden = isConnected;
        disconnectBtn.hidden = !isConnected;

        connectBtn.onclick = () => { location.href = `/api/external-calendar/${provider}/connect`; };
        disconnectBtn.onclick = async () => {
            await fetch(`/api/external-calendar/${provider}`, { method: 'DELETE' });
            await initExternalCalendarButtons();
        };
    }
}

// A little padding on each side so the grid's leading/trailing days from
// neighboring months still show real entries (e.g. a gig on the 1st of
// next month, visible in this month's last row).
async function loadEntries(monthStart, monthEnd) {
    const from = new Date(monthStart); from.setDate(from.getDate() - 7);
    const to = new Date(monthEnd); to.setDate(to.getDate() + 7);

    const [entriesRes, availRes] = await Promise.all([
        fetch(`/api/calendar?from=${isoDate(from)}&to=${isoDate(to)}`),
        fetch(`/api/availability?from=${isoDate(from)}&to=${isoDate(to)}`)
    ]);

    const map = {};
    if (entriesRes.ok) {
        for (const entry of await entriesRes.json()) (map[entry.date] ||= []).push(entry);
    }

    const availMap = {};
    if (availRes.ok) {
        for (const a of await availRes.json()) (availMap[a.date] ||= []).push(a);
    }
    availabilityByDate = availMap;

    return map;
}

// Moves currentDate by one unit of whatever view is currently active -
// a month for Month view, 7 days for Week, 1 day for Day - so Prev/Next
// always means "the next thing this view shows," not always "next month."
function stepDate(direction) {
    if (currentView === 'month') currentDate.setMonth(currentDate.getMonth() + direction);
    else if (currentView === 'week') currentDate.setDate(currentDate.getDate() + direction * 7);
    else currentDate.setDate(currentDate.getDate() + direction);
}

async function render() {
    document.getElementById('calendar-grid').classList.remove('calendar-grid-week');
    if (currentView === 'month') await renderMonthView();
    else if (currentView === 'week') await renderWeekView();
    else await renderDayView();
}

async function renderMonthView() {
    document.getElementById('calendar-grid').hidden = false;
    document.getElementById('calendar-day-view').hidden = true;

    const year = currentDate.getFullYear();
    const month = currentDate.getMonth();
    const monthStart = new Date(year, month, 1);
    const monthEnd = new Date(year, month + 1, 0);

    document.getElementById('calendar-month-label').textContent =
        monthStart.toLocaleDateString(undefined, { month: 'long', year: 'numeric' });

    entriesByDate = await loadEntries(monthStart, monthEnd);

    const grid = document.getElementById('calendar-grid');
    grid.innerHTML = '';

    for (const label of ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat']) {
        const head = document.createElement('div');
        head.className = 'calendar-weekday-head';
        head.textContent = label;
        grid.appendChild(head);
    }

    const firstCellDate = new Date(monthStart);
    firstCellDate.setDate(firstCellDate.getDate() - monthStart.getDay());
    const today = isoDate(new Date());

    for (let i = 0; i < 42; i++) {
        const cellDate = new Date(firstCellDate);
        cellDate.setDate(firstCellDate.getDate() + i);
        const dateStr = isoDate(cellDate);
        const inMonth = cellDate.getMonth() === month;

        const cell = document.createElement('div');
        cell.className = 'calendar-day-cell' + (inMonth ? '' : ' calendar-day-outside') + (dateStr === today ? ' calendar-day-today' : '');

        const dayNum = document.createElement('div');
        dayNum.className = 'calendar-day-num';
        dayNum.textContent = cellDate.getDate();
        cell.appendChild(dayNum);

        const dayEntries = entriesByDate[dateStr] || [];
        for (const entry of dayEntries.slice(0, 3)) {
            const chip = document.createElement('div');
            chip.className = `calendar-entry-chip calendar-entry-${entry.kind}`;
            chip.textContent = entry.kind === 'gig' ? entry.title : (entry.title || 'Rehearsal');
            cell.appendChild(chip);
        }
        if (dayEntries.length > 3) {
            const more = document.createElement('div');
            more.className = 'calendar-entry-more';
            more.textContent = `+${dayEntries.length - 3} more`;
            cell.appendChild(more);
        }

        cell.addEventListener('click', () => openDayModal(dateStr, cellDate, dayEntries));

        grid.appendChild(cell);
    }
}

async function renderWeekView() {
    const grid = document.getElementById('calendar-grid');
    grid.hidden = false;
    grid.classList.add('calendar-grid-week');
    document.getElementById('calendar-day-view').hidden = true;

    const weekStart = new Date(currentDate);
    weekStart.setDate(weekStart.getDate() - weekStart.getDay());
    const weekEnd = new Date(weekStart);
    weekEnd.setDate(weekEnd.getDate() + 6);

    document.getElementById('calendar-month-label').textContent =
        `${weekStart.toLocaleDateString(undefined, { month: 'short', day: 'numeric' })} – ${weekEnd.toLocaleDateString(undefined, { month: 'short', day: 'numeric', year: 'numeric' })}`;

    entriesByDate = await loadEntries(weekStart, weekEnd);

    grid.innerHTML = '';
    for (const label of ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat']) {
        const head = document.createElement('div');
        head.className = 'calendar-weekday-head';
        head.textContent = label;
        grid.appendChild(head);
    }

    const today = isoDate(new Date());
    for (let i = 0; i < 7; i++) {
        const cellDate = new Date(weekStart);
        cellDate.setDate(weekStart.getDate() + i);
        const dateStr = isoDate(cellDate);

        const cell = document.createElement('div');
        cell.className = 'calendar-day-cell' + (dateStr === today ? ' calendar-day-today' : '');

        const dayNum = document.createElement('div');
        dayNum.className = 'calendar-day-num';
        dayNum.textContent = cellDate.getDate();
        cell.appendChild(dayNum);

        // No 3-item cap here (unlike Month view) - Week's cells are much
        // taller specifically so a full day's entries fit without truncation.
        const dayEntries = entriesByDate[dateStr] || [];
        for (const entry of dayEntries) {
            const chip = document.createElement('div');
            chip.className = `calendar-entry-chip calendar-entry-${entry.kind}`;
            chip.textContent = entry.kind === 'gig' ? entry.title : (entry.title || 'Rehearsal');
            cell.appendChild(chip);
        }

        cell.addEventListener('click', () => openDayModal(dateStr, cellDate, dayEntries));
        grid.appendChild(cell);
    }
}

// Unlike Month/Week (which drill into a day via the modal), Day view
// renders that same entry-list-plus-roster content directly on the page -
// reusing openDayModal's own markup/classes, just not inside a modal.
async function renderDayView() {
    document.getElementById('calendar-grid').hidden = true;
    const dayView = document.getElementById('calendar-day-view');
    dayView.hidden = false;

    const dateStr = isoDate(currentDate);
    document.getElementById('calendar-month-label').textContent =
        currentDate.toLocaleDateString(undefined, { weekday: 'long', month: 'long', day: 'numeric', year: 'numeric' });

    const from = new Date(currentDate); from.setDate(from.getDate() - 1);
    const to = new Date(currentDate); to.setDate(to.getDate() + 1);
    entriesByDate = await loadEntries(from, to);
    const dayEntries = entriesByDate[dateStr] || [];

    dayView.innerHTML = '';

    const list = document.createElement('div');
    list.className = 'calendar-day-entry-list';
    if (dayEntries.length === 0) {
        list.innerHTML = '<p class="save-note">Nothing scheduled.</p>';
    } else {
        for (const entry of dayEntries) {
            const row = document.createElement('div');
            row.className = `calendar-day-entry calendar-entry-${entry.kind}`;
            if (entry.kind === 'gig') {
                row.innerHTML = `
                    <strong>${escapeHtml(entry.title)}</strong>
                    <span class="save-note">${escapeHtml(entry.venue || '')}${entry.time ? ' · ' + escapeHtml(entry.time) : ''}</span>
                `;
                row.addEventListener('click', () => { location.href = `/gig-sets`; });
            } else {
                const start = new Date(entry.startsAt);
                const end = new Date(entry.endsAt);
                const timeRange = `${start.toLocaleTimeString(undefined, { hour: 'numeric', minute: '2-digit' })} – ${end.toLocaleTimeString(undefined, { hour: 'numeric', minute: '2-digit' })}`;
                row.innerHTML = `
                    <strong>${escapeHtml(entry.title || 'Rehearsal')}</strong>
                    <span class="save-note">${timeRange}${entry.location ? ' · ' + escapeHtml(entry.location) : ''}</span>
                `;
                row.style.cursor = 'pointer';
                row.addEventListener('click', () => openRehearsalModal(dateStr, entry));
            }
            list.appendChild(row);
        }
    }
    dayView.appendChild(list);

    const addBtn = document.createElement('button');
    addBtn.type = 'button';
    addBtn.className = 'calendar-add-rehearsal-btn';
    addBtn.textContent = '+ Add Rehearsal';
    addBtn.addEventListener('click', () => openRehearsalModal(dateStr, null));
    dayView.appendChild(addBtn);

    dayView.appendChild(buildAvailabilityRoster(dateStr, () => renderDayView()));
}

function closeDayModal() { document.getElementById('calendar-day-modal-backdrop').hidden = true; }
document.getElementById('calendar-day-modal-close').addEventListener('click', closeDayModal);
document.getElementById('calendar-day-modal-backdrop').addEventListener('click', (e) => { if (e.target.id === 'calendar-day-modal-backdrop') closeDayModal(); });

function openDayModal(dateStr, dateObj, entries) {
    const body = document.getElementById('calendar-day-modal-body');
    const heading = dateObj.toLocaleDateString(undefined, { weekday: 'long', month: 'long', day: 'numeric', year: 'numeric' });
    body.innerHTML = `<h2>${escapeHtml(heading)}</h2>`;

    const list = document.createElement('div');
    list.className = 'calendar-day-entry-list';
    for (const entry of entries) {
        const row = document.createElement('div');
        row.className = `calendar-day-entry calendar-entry-${entry.kind}`;
        if (entry.kind === 'gig') {
            row.innerHTML = `
                <strong>${escapeHtml(entry.title)}</strong>
                <span class="save-note">${escapeHtml(entry.venue || '')}${entry.time ? ' · ' + escapeHtml(entry.time) : ''}</span>
            `;
            row.addEventListener('click', () => { location.href = `/gig-sets`; });
        } else {
            const start = new Date(entry.startsAt);
            const end = new Date(entry.endsAt);
            const timeRange = `${start.toLocaleTimeString(undefined, { hour: 'numeric', minute: '2-digit' })} – ${end.toLocaleTimeString(undefined, { hour: 'numeric', minute: '2-digit' })}`;
            row.innerHTML = `
                <strong>${escapeHtml(entry.title || 'Rehearsal')}</strong>
                <span class="save-note">${timeRange}${entry.location ? ' · ' + escapeHtml(entry.location) : ''}</span>
            `;
            row.style.cursor = 'pointer';
            row.addEventListener('click', () => openRehearsalModal(dateStr, entry));
        }
        list.appendChild(row);
    }
    body.appendChild(list);

    const addBtn = document.createElement('button');
    addBtn.type = 'button';
    addBtn.className = 'calendar-add-rehearsal-btn';
    addBtn.textContent = '+ Add Rehearsal';
    addBtn.addEventListener('click', () => openRehearsalModal(dateStr, null));
    body.appendChild(addBtn);

    body.appendChild(buildAvailabilityRoster(dateStr, () => openDayModal(dateStr, dateObj, entries)));

    document.getElementById('calendar-day-modal-backdrop').hidden = false;
}

// --- One-off rehearsal add/edit/delete (any band member) ---

function closeRehearsalModal() { document.getElementById('calendar-rehearsal-modal-backdrop').hidden = true; }
document.getElementById('calendar-rehearsal-modal-close').addEventListener('click', closeRehearsalModal);
document.getElementById('calendar-rehearsal-modal-backdrop').addEventListener('click', (e) => { if (e.target.id === 'calendar-rehearsal-modal-backdrop') closeRehearsalModal(); });

function toTimeInput(d) { return `${pad2(d.getHours())}:${pad2(d.getMinutes())}`; }

function openRehearsalModal(dateStr, entry) {
    document.getElementById('calendar-rehearsal-modal-title').textContent = entry ? 'Edit Rehearsal' : 'Add Rehearsal';
    document.getElementById('calendar-rehearsal-title').value = entry?.title || '';
    document.getElementById('calendar-rehearsal-location').value = entry?.location || '';

    if (entry) {
        const start = new Date(entry.startsAt);
        const end = new Date(entry.endsAt);
        document.getElementById('calendar-rehearsal-start').value = toTimeInput(start);
        document.getElementById('calendar-rehearsal-end').value = toTimeInput(end);
    } else {
        document.getElementById('calendar-rehearsal-start').value = '19:00';
        document.getElementById('calendar-rehearsal-end').value = '21:00';
    }

    const deleteBtn = document.getElementById('calendar-rehearsal-delete');
    deleteBtn.hidden = !entry;
    deleteBtn.onclick = async () => {
        if (!entry) return;
        await fetch(`/api/rehearsals/${entry.id}`, { method: 'DELETE' });
        closeRehearsalModal();
        closeDayModal();
        await render();
    };

    const form = document.getElementById('calendar-rehearsal-form');
    form.onsubmit = async (e) => {
        e.preventDefault();
        const startTime = document.getElementById('calendar-rehearsal-start').value;
        const endTime = document.getElementById('calendar-rehearsal-end').value;
        const startsAt = new Date(`${dateStr}T${startTime}:00`);
        const endsAt = new Date(`${dateStr}T${endTime}:00`);
        const body = JSON.stringify({
            title: document.getElementById('calendar-rehearsal-title').value.trim() || null,
            location: document.getElementById('calendar-rehearsal-location').value.trim() || null,
            startsAt: startsAt.toISOString(),
            endsAt: endsAt.toISOString()
        });
        const url = entry ? `/api/rehearsals/${entry.id}` : '/api/rehearsals';
        const method = entry ? 'PUT' : 'POST';
        const res = await fetch(url, { method, headers: { 'Content-Type': 'application/json' }, body });
        if (!res.ok) { const err = await res.json().catch(() => ({})); alert(err.error || 'Could not save rehearsal.'); return; }
        closeRehearsalModal();
        closeDayModal();
        await render();
    };

    document.getElementById('calendar-rehearsal-modal-backdrop').hidden = false;
}

// --- Recurring rehearsal schedule (BandAdmin only) ---

function closeRecurringModal() { document.getElementById('calendar-recurring-modal-backdrop').hidden = true; }
document.getElementById('calendar-recurring-modal-close').addEventListener('click', closeRecurringModal);
document.getElementById('calendar-recurring-modal-backdrop').addEventListener('click', (e) => { if (e.target.id === 'calendar-recurring-modal-backdrop') closeRecurringModal(); });

async function openRecurringModal() {
    await loadRecurringRules();
    document.getElementById('calendar-recurring-modal-backdrop').hidden = false;
}

async function loadRecurringRules() {
    const list = document.getElementById('calendar-recurring-list');
    list.innerHTML = '';
    const res = await fetch('/api/recurring-rehearsal-rules');
    const rules = res.ok ? await res.json() : [];

    if (rules.length === 0) {
        list.innerHTML = '<p class="save-note">No recurring rehearsals set up yet.</p>';
    }

    for (const rule of rules) {
        const row = document.createElement('div');
        row.className = 'calendar-roster-row';
        const label = document.createElement('span');
        label.className = 'calendar-roster-name';
        label.textContent = `${rule.dayOfWeek} ${rule.startTime} (${rule.durationMinutes}m)${rule.location ? ' · ' + rule.location : ''}${rule.active ? '' : ' — inactive'}`;
        row.appendChild(label);

        const actions = document.createElement('span');
        const toggleBtn = document.createElement('button');
        toggleBtn.type = 'button';
        toggleBtn.textContent = rule.active ? 'Deactivate' : 'Activate';
        toggleBtn.addEventListener('click', async () => {
            await fetch(`/api/recurring-rehearsal-rules/${rule.id}`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ dayOfWeek: rule.dayOfWeek, startTime: rule.startTime, durationMinutes: rule.durationMinutes, location: rule.location, active: !rule.active })
            });
            await loadRecurringRules();
            await render();
        });
        actions.appendChild(toggleBtn);

        const deleteBtn = document.createElement('button');
        deleteBtn.type = 'button';
        deleteBtn.textContent = 'Delete';
        deleteBtn.style.marginLeft = '6px';
        deleteBtn.addEventListener('click', async () => {
            await fetch(`/api/recurring-rehearsal-rules/${rule.id}`, { method: 'DELETE' });
            await loadRecurringRules();
            await render();
        });
        actions.appendChild(deleteBtn);

        row.appendChild(actions);
        list.appendChild(row);
    }
}

document.getElementById('calendar-recurring-form').addEventListener('submit', async (e) => {
    e.preventDefault();
    const body = JSON.stringify({
        dayOfWeek: document.getElementById('calendar-recurring-day').value,
        startTime: document.getElementById('calendar-recurring-start').value,
        durationMinutes: parseInt(document.getElementById('calendar-recurring-duration').value, 10) || 120,
        location: document.getElementById('calendar-recurring-location').value.trim() || null,
        active: true
    });
    const res = await fetch('/api/recurring-rehearsal-rules', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body });
    if (!res.ok) { const err = await res.json().catch(() => ({})); alert(err.error || 'Could not add schedule.'); return; }
    document.getElementById('calendar-recurring-form').reset();
    document.getElementById('calendar-recurring-duration').value = 120;
    await loadRecurringRules();
    await render();
});

function buildAvailabilityRoster(dateStr, onChanged) {
    const wrap = document.createElement('div');
    wrap.className = 'calendar-roster';

    const heading = document.createElement('h3');
    heading.textContent = 'Availability';
    wrap.appendChild(heading);

    const dayAvail = availabilityByDate[dateStr] || [];
    const byUserId = {};
    for (const a of dayAvail) byUserId[a.userId] = a;

    const list = document.createElement('div');
    list.className = 'calendar-roster-list';

    for (const member of bandMembers) {
        const entry = byUserId[member.id];
        const canEdit = me.isAdmin || member.id === me.id;

        const row = document.createElement('div');
        row.className = 'calendar-roster-row';

        const name = document.createElement('span');
        name.className = 'calendar-roster-name';
        name.textContent = member.firstName;
        row.appendChild(name);

        if (canEdit) {
            const select = document.createElement('select');
            select.className = 'calendar-roster-select';
            const unsetOpt = document.createElement('option');
            unsetOpt.value = '';
            unsetOpt.textContent = 'Unset';
            select.appendChild(unsetOpt);
            for (const status of STATUSES) {
                const opt = document.createElement('option');
                opt.value = status;
                opt.textContent = status;
                select.appendChild(opt);
            }
            select.value = entry ? entry.status : '';
            select.addEventListener('change', async () => {
                if (select.value === '') {
                    await fetch(`/api/availability/${member.id}/${dateStr}`, { method: 'DELETE' });
                } else {
                    await fetch(`/api/availability/${member.id}/${dateStr}`, {
                        method: 'PUT',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ status: select.value, note: entry?.note ?? null })
                    });
                }
                await refreshAvailability();
                onChanged();
            });
            row.appendChild(select);
        } else {
            const badge = document.createElement('span');
            badge.className = `calendar-roster-badge calendar-avail-${(entry?.status || 'unset').toLowerCase()}`;
            badge.textContent = entry ? entry.status : 'Unset';
            row.appendChild(badge);
        }

        list.appendChild(row);
    }

    wrap.appendChild(list);
    return wrap;
}

async function refreshAvailability() {
    const year = currentDate.getFullYear();
    const month = currentDate.getMonth();
    const monthStart = new Date(year, month, 1);
    const monthEnd = new Date(year, month + 1, 0);
    const from = new Date(monthStart); from.setDate(from.getDate() - 7);
    const to = new Date(monthEnd); to.setDate(to.getDate() + 7);
    const availRes = await fetch(`/api/availability?from=${isoDate(from)}&to=${isoDate(to)}`);
    const availMap = {};
    if (availRes.ok) {
        for (const a of await availRes.json()) (availMap[a.date] ||= []).push(a);
    }
    availabilityByDate = availMap;
}

init();
