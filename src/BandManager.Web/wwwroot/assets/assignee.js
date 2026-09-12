// Shared "who's responsible for this" component - the band-member picker
// and the clickable-name-badges display, used by both dashboard.js (task
// tiles) and cadence.js (rule templates). Builds its own modal/popover DOM
// on demand rather than relying on page-specific markup (unlike
// catalog.js's openCatalogPicker), so it works unmodified on any page that
// includes this script plus dashboard.css (for the shared .detail-modal
// look).

let bandMembersCache = null;
async function fetchBandMembers() {
    if (bandMembersCache) return bandMembersCache;
    const res = await fetch('/api/profile/band-members');
    bandMembersCache = res.ok ? await res.json() : [];
    return bandMembersCache;
}
// Call after switching bands (topNav/bandSwitcher already reload the page
// on switch, so this is mostly a safety net for same-page cache staleness).
window.invalidateBandMembersCache = () => { bandMembersCache = null; };

// Resolves with an array of up to `max` user ids, or null if cancelled.
window.openBandMemberPicker = function openBandMemberPicker({ max = 2, current = [] } = {}) {
    return new Promise(async (resolve) => {
        const members = await fetchBandMembers();
        let selected = current.filter(Boolean);
        let settled = false;

        const backdrop = document.createElement('div');
        backdrop.className = 'detail-modal-backdrop';
        backdrop.innerHTML = `
            <div class="detail-modal assignee-picker-modal">
                <button type="button" class="detail-modal-close">&times;</button>
                <h2>Assign up to ${max}</h2>
                <div class="assignee-picker-grid"></div>
                <div class="assignee-picker-actions">
                    <button type="button" class="assignee-picker-confirm">Save</button>
                </div>
            </div>
        `;
        document.body.appendChild(backdrop);

        function finish(value) {
            if (settled) return;
            settled = true;
            backdrop.remove();
            resolve(value);
        }

        const grid = backdrop.querySelector('.assignee-picker-grid');
        function renderGrid() {
            grid.innerHTML = '';
            if (members.length === 0) {
                grid.innerHTML = '<p class="catalog-empty-note">No other band members yet.</p>';
                return;
            }
            for (const m of members) {
                const tile = document.createElement('button');
                tile.type = 'button';
                const isSelected = selected.includes(m.id);
                tile.className = 'assignee-picker-tile' + (isSelected ? ' selected' : '');
                tile.textContent = m.firstName;
                tile.disabled = !isSelected && selected.length >= max;
                tile.addEventListener('click', () => {
                    selected = isSelected ? selected.filter((id) => id !== m.id) : [...selected, m.id];
                    renderGrid();
                });
                grid.appendChild(tile);
            }
        }
        renderGrid();

        backdrop.querySelector('.detail-modal-close').addEventListener('click', () => finish(null));
        backdrop.addEventListener('click', (e) => { if (e.target === backdrop) finish(null); });
        backdrop.querySelector('.assignee-picker-confirm').addEventListener('click', () => finish(selected));
    });
};

// Renders up to 2 clickable first-name badges (click -> full profile modal,
// via UserAvatar's own shared document-level click handler - see
// userAvatar.js)
// plus, when editable, a Reassign/Assign button that opens the picker and
// calls onReassign(newIds) with the result. The returned element updates
// itself in place after a successful reassign.
window.renderAssigneeBadges = function renderAssigneeBadges({ assigneeUserId1, assigneeUserId2, editable = true, onReassign }) {
    const wrap = document.createElement('span');
    wrap.className = 'assignee-badges';
    let ids = [assigneeUserId1, assigneeUserId2].filter(Boolean);

    async function render() {
        wrap.innerHTML = '';
        const members = await fetchBandMembers();
        const byId = Object.fromEntries(members.map((m) => [m.id, m]));

        if (ids.length === 0) {
            const span = document.createElement('span');
            span.className = 'assignee-unassigned';
            span.textContent = 'unassigned';
            wrap.appendChild(span);
        } else {
            ids.forEach((id, i) => {
                if (i > 0) wrap.appendChild(document.createTextNode(', '));
                const member = byId[id];
                const span = document.createElement('span');
                span.innerHTML = member
                    ? UserAvatar.renderWithName({ id: member.id, name: member.firstName, avatarUrl: member.avatarUrl }, 20)
                    : 'Unknown';
                wrap.appendChild(span.firstElementChild || span);
            });
        }

        if (editable) {
            const reassignBtn = document.createElement('button');
            reassignBtn.type = 'button';
            reassignBtn.className = 'assignee-reassign-btn';
            reassignBtn.textContent = ids.length ? 'Reassign' : 'Assign';
            reassignBtn.addEventListener('click', async (e) => {
                e.stopPropagation();
                const result = await window.openBandMemberPicker({ max: 2, current: ids });
                if (result === null) return;
                ids = result;
                await onReassign(ids[0] || null, ids[1] || null);
                await render();
            });
            wrap.appendChild(reassignBtn);
        }
    }

    render();
    return wrap;
};
