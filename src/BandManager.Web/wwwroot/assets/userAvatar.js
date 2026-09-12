// Shared "who is this" building block - one round avatar (photo or
// colored initials) + one profile modal (avatar, contact info, every
// Band + role), reused everywhere a user is shown instead of each page
// rolling its own. Self-injecting like topNav.js/branding.js: include
// this one script tag, then call UserAvatar.render(...)/nameLink(...) to
// drop in a clickable avatar or name - a single document-level click
// listener (registered once, below) opens the modal for any of them,
// so no page needs to wire its own click handlers.
//
// Access control lives entirely server-side (ProfileController.GetUserProfile
// - shares a Band, is yourself, or SuperAdmin) - this file just calls that
// endpoint and shows whatever it returns (or its error) as-is.
window.UserAvatar = (function () {
    const COLOR_RAMPS = ['accent', 'structure', 'success', 'danger'];

    function colorFor(seed) {
        let hash = 0;
        for (let i = 0; i < seed.length; i++) hash = (hash * 31 + seed.charCodeAt(i)) >>> 0;
        return COLOR_RAMPS[hash % COLOR_RAMPS.length];
    }

    function initials(name) {
        const parts = String(name || '?').trim().split(/\s+/).filter(Boolean);
        const letters = parts.slice(0, 2).map((w) => w[0]).join('');
        return (letters || '?').toUpperCase();
    }

    function escapeHtml(str) {
        const div = document.createElement('div');
        div.textContent = str == null ? '' : String(str);
        return div.innerHTML;
    }

    // user: { id, name, avatarUrl } - callers map their own endpoint's
    // shape (band-members' `firstName`, etc.) into this before calling.
    function render(user, sizePx) {
        sizePx = sizePx || 28;
        const color = colorFor(user.id || user.name || '?');
        const fontSize = Math.max(10, Math.round(sizePx * 0.4));
        const style = `width:${sizePx}px;height:${sizePx}px;font-size:${fontSize}px;flex-shrink:0;`;
        if (user.avatarUrl) {
            return `<span class="user-avatar-trigger user-avatar-img" data-user-id="${escapeHtml(user.id)}" style="${style}background-image:url('${escapeHtml(user.avatarUrl)}')" title="${escapeHtml(user.name)}"></span>`;
        }
        return `<span class="user-avatar-trigger user-avatar-initials c-${color}" data-user-id="${escapeHtml(user.id)}" style="${style}" title="${escapeHtml(user.name)}">${escapeHtml(initials(user.name))}</span>`;
    }

    function nameLink(user) {
        return `<span class="user-avatar-trigger user-name-trigger" data-user-id="${escapeHtml(user.id)}">${escapeHtml(user.name)}</span>`;
    }

    // Avatar + name together, the common case in a list row.
    function renderWithName(user, sizePx) {
        return `<span class="user-chip">${render(user, sizePx)}${nameLink(user)}</span>`;
    }

    let modalEl = null;
    function ensureModal() {
        if (modalEl) return modalEl;
        modalEl = document.createElement('div');
        modalEl.className = 'user-profile-modal-backdrop';
        modalEl.hidden = true;
        modalEl.innerHTML = `
            <div class="user-profile-modal">
                <button type="button" class="user-profile-modal-close" aria-label="Close">&times;</button>
                <div class="user-profile-modal-body">Loading...</div>
            </div>
        `;
        modalEl.addEventListener('click', (e) => { if (e.target === modalEl) close(); });
        modalEl.querySelector('.user-profile-modal-close').addEventListener('click', close);
        document.body.appendChild(modalEl);
        return modalEl;
    }

    function close() {
        if (modalEl) modalEl.hidden = true;
    }

    async function open(userId) {
        const modal = ensureModal();
        const body = modal.querySelector('.user-profile-modal-body');
        body.innerHTML = 'Loading...';
        modal.hidden = false;

        const res = await fetch(`/api/profile/${encodeURIComponent(userId)}`);
        if (modal.hidden) return; // closed again before this resolved
        if (res.status === 403) {
            body.innerHTML = '<p class="save-note">You can only view the profile of someone you share a band with.</p>';
            return;
        }
        if (!res.ok) {
            body.innerHTML = '<p class="save-note">Could not load this profile.</p>';
            return;
        }
        const u = await res.json();
        const color = colorFor(u.id);
        const avatarHtml = u.avatarUrl
            ? `<span class="user-profile-avatar-large user-avatar-img" style="background-image:url('${escapeHtml(u.avatarUrl)}')"></span>`
            : `<span class="user-profile-avatar-large user-avatar-initials c-${color}">${escapeHtml(initials(u.displayName))}</span>`;

        const bandsHtml = u.bands.length === 0
            ? '<p class="save-note">Not a member of any band.</p>'
            : `<ul class="user-profile-band-list">${u.bands.map((b) => `<li><span>${escapeHtml(b.bandName)}</span><span class="user-profile-band-role">${escapeHtml(b.role)}</span></li>`).join('')}</ul>`;

        body.innerHTML = `
            <div class="user-profile-header">
                ${avatarHtml}
                <div>
                    <h3>${escapeHtml(u.displayName)}${u.isSuperAdmin ? ' <span class="user-profile-superadmin-tag">SuperAdmin</span>' : ''}</h3>
                    ${u.email ? `<p class="save-note">${escapeHtml(u.email)}</p>` : ''}
                    ${u.cellNumber ? `<p class="save-note">${escapeHtml(u.cellNumber)}</p>` : ''}
                </div>
            </div>
            <h4>Bands</h4>
            ${bandsHtml}
        `;
    }

    document.addEventListener('click', (e) => {
        const trigger = e.target.closest('.user-avatar-trigger');
        if (!trigger) return;
        const userId = trigger.getAttribute('data-user-id');
        if (userId) open(userId);
    });

    return { render, nameLink, renderWithName, close };
})();
