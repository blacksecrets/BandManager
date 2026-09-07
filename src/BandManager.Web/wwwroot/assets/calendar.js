let currentMonth = new Date(); // any Date within the visible month
let entriesByDate = {}; // "yyyy-MM-dd" -> array of entries

function escapeHtml(str) {
    const div = document.createElement('div');
    div.textContent = str ?? '';
    return div.innerHTML;
}

function pad2(n) { return String(n).padStart(2, '0'); }
function isoDate(d) { return `${d.getFullYear()}-${pad2(d.getMonth() + 1)}-${pad2(d.getDate())}`; }

async function init() {
    const res = await fetch('/api/profile/me');
    const me = await res.json();
    const hasBand = !!me.activeBandRole;

    document.getElementById('calendar-no-band').hidden = hasBand;
    document.getElementById('calendar-content').hidden = !hasBand;
    if (!hasBand) return;

    document.getElementById('calendar-prev-month').addEventListener('click', () => { currentMonth.setMonth(currentMonth.getMonth() - 1); render(); });
    document.getElementById('calendar-next-month').addEventListener('click', () => { currentMonth.setMonth(currentMonth.getMonth() + 1); render(); });
    document.getElementById('calendar-today-btn').addEventListener('click', () => { currentMonth = new Date(); render(); });

    await render();
}

// A little padding on each side so the grid's leading/trailing days from
// neighboring months still show real entries (e.g. a gig on the 1st of
// next month, visible in this month's last row).
async function loadEntries(monthStart, monthEnd) {
    const from = new Date(monthStart); from.setDate(from.getDate() - 7);
    const to = new Date(monthEnd); to.setDate(to.getDate() + 7);
    const res = await fetch(`/api/calendar?from=${isoDate(from)}&to=${isoDate(to)}`);
    if (!res.ok) return {};
    const entries = await res.json();
    const map = {};
    for (const entry of entries) {
        (map[entry.date] ||= []).push(entry);
    }
    return map;
}

async function render() {
    const year = currentMonth.getFullYear();
    const month = currentMonth.getMonth();
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

        if (dayEntries.length > 0) {
            cell.addEventListener('click', () => openDayModal(dateStr, cellDate, dayEntries));
        }

        grid.appendChild(cell);
    }
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
        }
        list.appendChild(row);
    }
    body.appendChild(list);
    document.getElementById('calendar-day-modal-backdrop').hidden = false;
}

init();
