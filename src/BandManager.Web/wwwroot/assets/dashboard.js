const board = document.getElementById('board');
const cardTemplate = document.getElementById('card-template');

const ARTIFACT_LABELS = {
    caption: 'Caption text',
    photo: 'Photo',
    video: 'Video',
    flyer: 'Flyer image',
    audio: 'Audio',
    event_url: 'Event URL'
};
const TEXT_ARTIFACT_TYPES = new Set(['caption', 'event_url']);
const ARTIFACT_ACCEPT = { photo: 'image/*', flyer: 'image/*', video: 'video/*', audio: 'audio/*' };

// Content types offered in the ad-hoc form - fetched from the DB
// (content_types table, gig-only ones already excluded server-side) so
// this stays in sync with whatever's editable via the Cadence page,
// instead of a hardcoded list that can drift from it. Used as a fallback
// if the per-platform lookup below fails for any reason.
let ADHOC_CONTENT_TYPES = [];
async function loadAdhocContentTypes() {
    const res = await fetch('/api/content-types');
    const types = await res.json();
    const gigOnly = new Set(['Event Post', 'Event Listing', 'Reminder Push']);
    ADHOC_CONTENT_TYPES = types.map((t) => t.id).filter((id) => !gigOnly.has(id));
}

// platform display_name -> id (e.g. "Google Business Profile" -> "googleBusiness"),
// needed to filter the ad-hoc dropdown to what that specific platform can
// actually do (same platform_content_types table the Cadence editor uses).
let platformIdByName = {};
// Board section order - drag-and-drop reordered, persisted server-side via
// platforms.sort_order (same column /api/platforms already sorts by).
// PLATFORM_ORDER below is only the fallback for the very first paint,
// before this has loaded.
let platformDisplayOrder = [];
async function loadPlatformIds() {
    const res = await fetch('/api/platforms');
    const platforms = await res.json();
    platformIdByName = Object.fromEntries(platforms.map((p) => [p.display_name, p.id]));
    platformDisplayOrder = platforms.map((p) => p.display_name);
}

async function adhocContentTypesForPlatform(platform) {
    const platformId = platformIdByName[platform];
    if (!platformId) return ADHOC_CONTENT_TYPES;
    const res = await fetch(`/api/platforms/${platformId}/content-types`);
    const types = await res.json();
    const gigOnly = new Set(['Event Post', 'Event Listing', 'Reminder Push']);
    const filtered = types.map((t) => t.id).filter((id) => !gigOnly.has(id));
    return filtered.length > 0 ? filtered : ADHOC_CONTENT_TYPES;
}

// Where "Setup X" should send you on the Settings page.
const SETTINGS_ANCHOR = {
    facebook: '/settings#meta-section',
    instagram: '/settings#meta-section',
    googleBusiness: '/settings#gbp-section',
    website: '/settings#website-section'
};
const PLATFORM_LABEL = {
    facebook: 'Facebook',
    instagram: 'Instagram',
    googleBusiness: 'Google Business Profile',
    website: 'Website'
};

// Matches the order of the band's actual content calendar; anything not
// listed here (there shouldn't be anything) falls in alphabetically after.
const PLATFORM_ORDER = [
    'Facebook', 'Instagram', 'TikTok', 'YouTube',
    'Bandsintown/Songkick', 'Google Business Profile', 'Spotify/Apple Music', 'Website'
];

let configuredPlatforms = {};
const expandedPlatforms = new Set(); // survives across the periodic refresh
const expandedFutureWork = new Set(); // same, but for the "Future work" dropdowns
const expandedAdhoc = new Set(); // same, but for the "+ Ad-hoc post" forms
const expandedAddCalendar = new Set(); // same, but for the "+ Add Calendar Listing" form (Website section only)
const expandedAddMedia = new Set(); // same, but for the "+ Add Media" form (Website section only)
const expandedAddGallery = new Set(); // same, but for the "+ Add Gallery Image" form (Website section only)
const expandedPosted = new Set(); // same, but for the "Posted" dropdowns
const expandedCancelled = new Set(); // same, but for the "Cancelled" dropdowns

// What actually needs doing right now vs. what's just scheduled ahead.
const ACTIONABLE_COLORS = new Set(['red', 'neutral', 'yellow', 'complete', 'done-pending']);

async function loadItems() {
    const [itemsRes, credsRes] = await Promise.all([
        fetch('/api/items'),
        fetch('/api/settings/credentials')
    ]);
    const items = await itemsRes.json();
    configuredPlatforms = await credsRes.json();
    if (ADHOC_CONTENT_TYPES.length === 0) await loadAdhocContentTypes();
    if (Object.keys(platformIdByName).length === 0) await loadPlatformIds();
    render(items);
}

function groupByPlatform(items) {
    const groups = new Map();
    for (const item of items) {
        if (!groups.has(item.platform)) groups.set(item.platform, []);
        groups.get(item.platform).push(item);
    }
    const orderSource = platformDisplayOrder.length > 0 ? platformDisplayOrder : PLATFORM_ORDER;
    const known = orderSource.filter((p) => groups.has(p));
    const rest = [...groups.keys()].filter((p) => !orderSource.includes(p)).sort();
    return [...known, ...rest].map((platform) => ({ platform, items: groups.get(platform) }));
}

// Drag-and-drop reorder: dropping the dragged section onto another one
// inserts it immediately before that target, then persists the full new
// order. Re-fetches afterward rather than reordering the DOM in place -
// simpler, and this only happens on an infrequent manual action.
async function reorderPlatforms(draggedPlatform, targetPlatform) {
    const newOrder = platformDisplayOrder.filter((p) => p !== draggedPlatform);
    const targetIndex = newOrder.indexOf(targetPlatform);
    newOrder.splice(targetIndex, 0, draggedPlatform);
    platformDisplayOrder = newOrder;

    const idOrder = newOrder.map((name) => platformIdByName[name]).filter(Boolean);
    await fetch('/api/platforms/reorder', {
        method: 'PATCH',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ order: idOrder })
    });
    loadItems();
}

// Manual pointer-events drag instead of native HTML5 drag-and-drop -
// native drag-and-drop (dragstart/dragover/drop) turned out to be
// unreliable for a "drag from a small handle, move a large container"
// pattern in real-world Chrome, even though it worked fine when tested
// via synthetic DragEvents. Pointer events track the same way across
// mouse, touch, and pen, so this also gets touch support for free.
function startSectionDrag(e, section, platform) {
    e.preventDefault();
    section.classList.add('dragging');
    let currentTarget = null;

    function onPointerMove(ev) {
        const el = document.elementFromPoint(ev.clientX, ev.clientY);
        const overSection = el && el.closest('.platform-section');
        if (currentTarget && currentTarget !== overSection) {
            currentTarget.classList.remove('drag-over');
            currentTarget = null;
        }
        if (overSection && overSection !== section) {
            overSection.classList.add('drag-over');
            currentTarget = overSection;
        }
    }

    function cleanup() {
        document.removeEventListener('pointermove', onPointerMove);
        document.removeEventListener('pointerup', onPointerUp);
        document.removeEventListener('pointercancel', onPointerUp);
        section.classList.remove('dragging');
        if (currentTarget) currentTarget.classList.remove('drag-over');
    }

    function onPointerUp() {
        const targetPlatform = currentTarget ? currentTarget.dataset.platform : null;
        cleanup();
        if (targetPlatform && targetPlatform !== platform) {
            reorderPlatforms(platform, targetPlatform);
        }
    }

    document.addEventListener('pointermove', onPointerMove);
    document.addEventListener('pointerup', onPointerUp);
    document.addEventListener('pointercancel', onPointerUp);
}

function render(items) {
    board.innerHTML = '';
    if (items.length === 0) {
        board.textContent = 'Nothing scheduled yet.';
        return;
    }
    for (const group of groupByPlatform(items)) {
        board.appendChild(renderPlatformSection(group.platform, group.items));
    }
}

function escapeHtml(str) {
    return String(str).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
}

// item.artifacts comes back newest-first (see api.js's getArtifacts). The
// backend now replaces rather than accumulates on re-upload, so there's
// normally at most one row per type - but this stays defensive against
// any leftover duplicate from before that fix (or any other way two rows
// for the same type end up on one item), by keeping the first (newest)
// one seen rather than the last, which is what Object.fromEntries would
// do on a newest-first array - silently showing the stale value instead.
function latestArtifactsByType(artifacts) {
    const byType = {};
    for (const a of artifacts) {
        if (!(a.artifact_type in byType)) byType[a.artifact_type] = a;
    }
    return byType;
}

function formatDueDate(dueDate) {
    if (!dueDate) return 'as soon as booked';
    const d = new Date(dueDate + 'T00:00:00');
    return d.toLocaleDateString(undefined, { month: 'short', day: 'numeric' });
}

// The Website section mixes Calendar Listing, Media Item, and Gallery
// Image tiles - this remembers which of those the user last chose to
// see, so the section doesn't have to show all three at once.
let websiteTileFilter = 'all'; // 'all' | 'calendar' | 'media' | 'gallery'
const WEBSITE_FILTER_CONTENT_TYPE = { calendar: 'Calendar Listing', media: 'Media Item', gallery: 'Gallery Image' };

function renderPlatformSection(platform, itemsIn) {
    const items = platform === 'Website' && websiteTileFilter !== 'all'
        ? itemsIn.filter((i) => i.content_type === WEBSITE_FILTER_CONTENT_TYPE[websiteTileFilter])
        : itemsIn;

    // Every item in a platform group shares the same credential_platform,
    // so the whole section is either set up or it isn't - no need to check
    // item by item. Not being set up no longer hides the section - items
    // just fall back to manual instructions (same as a no-API platform)
    // until automation is connected.
    const credKey = itemsIn[0].credential_platform;
    const automationConfigured = !credKey || configuredPlatforms[credKey]?.configured;
    const accountLabel = itemsIn[0].account_label;
    const sectionTitle = accountLabel && accountLabel !== platform
        ? `${platform} <span class="platform-account-label">— ${escapeHtml(accountLabel)}</span>`
        : platform;
    const expanded = expandedPlatforms.has(platform);
    const futureOpen = expandedFutureWork.has(platform);
    const postedOpen = expandedPosted.has(platform);
    const cancelledOpen = expandedCancelled.has(platform);

    // Only what needs action now becomes a tile; not-yet-due items go into
    // the "Future work" summary instead. Posted/auto-handled/cancelled
    // items aren't shown in either - they're resolved, not upcoming.
    const actionable = items.filter((i) => ACTIONABLE_COLORS.has(i.status.color));
    const future = items.filter((i) => i.status.color === 'grey');
    const posted = items.filter((i) => i.status.color === 'done');
    const cancelled = items.filter((i) => i.status.color === 'cancelled');

    const section = document.createElement('section');
    section.className = 'platform-section' + (automationConfigured ? '' : ' needs-setup');
    section.dataset.platform = platform;

    const dueCount = actionable.filter((i) => i.status.color === 'red' || i.status.color === 'yellow').length;
    const readyCount = actionable.filter((i) => i.status.ready).length;
    const summary = actionable.length === 0
        ? 'Nothing due right now'
        : `${dueCount ? `${dueCount} due` : ''}${dueCount && readyCount ? ' · ' : ''}${readyCount ? `${readyCount} ready` : ''}`;

    const headerRow = document.createElement('div');
    headerRow.className = 'platform-section-header';

    const dragHandle = document.createElement('span');
    dragHandle.className = 'drag-handle';
    dragHandle.textContent = '⠿';
    dragHandle.title = 'Drag to reorder';
    dragHandle.addEventListener('pointerdown', (e) => startSectionDrag(e, section, platform));
    headerRow.appendChild(dragHandle);

    const toggleBtn = document.createElement('button');
    toggleBtn.type = 'button';
    toggleBtn.className = 'section-toggle';
    toggleBtn.innerHTML = `
        <span class="chevron">${expanded ? '▾' : '▸'}</span>
        <span class="platform-section-title">${sectionTitle}</span>
        <span class="platform-section-summary">${summary}</span>
    `;
    headerRow.appendChild(toggleBtn);

    if (platform === 'Website') {
        const filterSelect = document.createElement('select');
        filterSelect.className = 'website-tile-filter';
        filterSelect.title = 'Show only Calendar, Media, or Gallery tiles';
        filterSelect.innerHTML = `
            <option value="all">Show All</option>
            <option value="calendar">Calendar</option>
            <option value="media">Media</option>
            <option value="gallery">Gallery</option>
        `;
        filterSelect.value = websiteTileFilter;
        filterSelect.addEventListener('click', (e) => e.stopPropagation());
        filterSelect.addEventListener('change', () => {
            websiteTileFilter = filterSelect.value;
            loadItems();
        });
        headerRow.appendChild(filterSelect);
    }

    let futurePanel = null;
    if (future.length > 0) {
        const futureBtn = document.createElement('button');
        futureBtn.type = 'button';
        futureBtn.className = 'future-work-toggle';
        futureBtn.textContent = `Future work (${future.length}) ${futureOpen ? '▴' : '▾'}`;
        headerRow.appendChild(futureBtn);

        futurePanel = document.createElement('div');
        futurePanel.className = 'future-work-panel';
        futurePanel.hidden = !futureOpen;
        futurePanel.innerHTML = `<ul>${future
            .map((i) => `<li><strong>${formatDueDate(i.due_date)}</strong> — ${i.content_type}: ${i.example || ''}</li>`)
            .join('')}</ul>`;

        futureBtn.addEventListener('click', () => {
            const nowOpen = futurePanel.hidden; // about to become open
            futurePanel.hidden = !nowOpen;
            futureBtn.textContent = `Future work (${future.length}) ${nowOpen ? '▴' : '▾'}`;
            if (nowOpen) expandedFutureWork.add(platform);
            else expandedFutureWork.delete(platform);
        });
    }

    let postedPanel = null;
    if (posted.length > 0) {
        const postedBtn = document.createElement('button');
        postedBtn.type = 'button';
        postedBtn.className = 'future-work-toggle';
        postedBtn.textContent = `Posted (${posted.length}) ${postedOpen ? '▴' : '▾'}`;
        headerRow.appendChild(postedBtn);

        postedPanel = document.createElement('div');
        postedPanel.className = 'future-work-panel posted-panel';
        postedPanel.hidden = !postedOpen;
        const list = document.createElement('ul');
        for (const item of posted) {
            const li = document.createElement('li');
            li.innerHTML = `<strong>${escapeHtml((item.posted_at || '').slice(0, 10))}</strong> — ${escapeHtml(item.content_type)}: ${escapeHtml(item.example || '')} `;
            const reopenBtn = document.createElement('button');
            reopenBtn.type = 'button';
            reopenBtn.className = 'reopen-btn';
            reopenBtn.textContent = 'Reopen';
            reopenBtn.addEventListener('click', async () => {
                if (!confirm('Mark this not done? It will show as due again.')) return;
                await fetch(`/api/items/${item.id}/reopen`, { method: 'POST' });
                loadItems();
            });
            li.appendChild(reopenBtn);
            list.appendChild(li);
        }
        postedPanel.appendChild(list);

        postedBtn.addEventListener('click', () => {
            const nowOpen = postedPanel.hidden; // about to become open
            postedPanel.hidden = !nowOpen;
            postedBtn.textContent = `Posted (${posted.length}) ${nowOpen ? '▴' : '▾'}`;
            if (nowOpen) expandedPosted.add(platform);
            else expandedPosted.delete(platform);
        });
    }

    let cancelledPanel = null;
    if (cancelled.length > 0) {
        const cancelledBtn = document.createElement('button');
        cancelledBtn.type = 'button';
        cancelledBtn.className = 'future-work-toggle';
        cancelledBtn.textContent = `Cancelled (${cancelled.length}) ${cancelledOpen ? '▴' : '▾'}`;
        headerRow.appendChild(cancelledBtn);

        cancelledPanel = document.createElement('div');
        cancelledPanel.className = 'future-work-panel posted-panel';
        cancelledPanel.hidden = !cancelledOpen;
        const list = document.createElement('ul');
        for (const item of cancelled) {
            const li = document.createElement('li');
            li.innerHTML = `<strong>${escapeHtml((item.posted_at || '').slice(0, 10))}</strong> — ${escapeHtml(item.content_type)}: ${escapeHtml(item.example || '')} `;
            const reopenBtn = document.createElement('button');
            reopenBtn.type = 'button';
            reopenBtn.className = 'reopen-btn';
            reopenBtn.textContent = 'Reopen';
            reopenBtn.addEventListener('click', async () => {
                if (!confirm('Un-cancel this? It will show as due again.')) return;
                await fetch(`/api/items/${item.id}/reopen`, { method: 'POST' });
                loadItems();
            });
            li.appendChild(reopenBtn);
            list.appendChild(li);
        }
        cancelledPanel.appendChild(list);

        cancelledBtn.addEventListener('click', () => {
            const nowOpen = cancelledPanel.hidden; // about to become open
            cancelledPanel.hidden = !nowOpen;
            cancelledBtn.textContent = `Cancelled (${cancelled.length}) ${nowOpen ? '▴' : '▾'}`;
            if (nowOpen) expandedCancelled.add(platform);
            else expandedCancelled.delete(platform);
        });
    }

    // Not being connected doesn't hide the section anymore - it just means
    // items fall back to manual instructions instead of "Fart it out."
    // Still surface a link to connect automation, without gating anything.
    if (!automationConfigured) {
        const setupLink = document.createElement('a');
        setupLink.className = 'setup-automation-link';
        setupLink.href = SETTINGS_ANCHOR[credKey];
        setupLink.textContent = `Set up ${PLATFORM_LABEL[credKey]} automation`;
        headerRow.appendChild(setupLink);
    }

    // The generic ad-hoc form (Category/"What is it"/Due) only makes sense
    // for platforms whose content types are actual one-off posting tasks.
    // Website's registered content types are Calendar Listing/Media Item/
    // Gallery Image - living site entries, not tasks - so that form never
    // belonged here: Media/Gallery already get their own proper forms
    // below, and Calendar Listing has no creation path at all (a new gig
    // is always added by hand directly in calendar.js; this dashboard only
    // ever edits one that already exists, see siteEditor.js).
    let adhocBtn = null;
    if (platform !== 'Website') {
        adhocBtn = document.createElement('button');
        adhocBtn.type = 'button';
        adhocBtn.className = 'adhoc-toggle';
        adhocBtn.title = 'Add an ad-hoc posting task for this platform';
        adhocBtn.textContent = '+ Ad-hoc post';
        headerRow.appendChild(adhocBtn);
    }

    // Calendar/Media/Gallery entries only ever exist on the Website
    // platform - a brand-new one comes into being here. Which button(s)
    // show follows the Calendar/Media/Gallery filter above - no point
    // offering "+ Add Gallery Image" while looking at only the Media tiles.
    let addCalendarBtn = null;
    let addCalendarPanel = null;
    let addMediaBtn = null;
    let addMediaPanel = null;
    let addGalleryBtn = null;
    let addGalleryPanel = null;
    if (platform === 'Website') {
        const showCalendarBtn = websiteTileFilter === 'all' || websiteTileFilter === 'calendar';
        const showMediaBtn = websiteTileFilter === 'all' || websiteTileFilter === 'media';
        const showGalleryBtn = websiteTileFilter === 'all' || websiteTileFilter === 'gallery';
        if (showCalendarBtn) {
            addCalendarBtn = document.createElement('button');
            addCalendarBtn.type = 'button';
            addCalendarBtn.className = 'adhoc-toggle';
            addCalendarBtn.title = 'Add a new show to the calendar';
            addCalendarBtn.textContent = '+ Add Calendar Listing';
            headerRow.appendChild(addCalendarBtn);
        }
        if (showMediaBtn) {
            addMediaBtn = document.createElement('button');
            addMediaBtn.type = 'button';
            addMediaBtn.className = 'adhoc-toggle';
            addMediaBtn.title = 'Add a new item to the Media section';
            addMediaBtn.textContent = '+ Add Media';
            headerRow.appendChild(addMediaBtn);
        }
        if (showGalleryBtn) {
            addGalleryBtn = document.createElement('button');
            addGalleryBtn.type = 'button';
            addGalleryBtn.className = 'adhoc-toggle';
            addGalleryBtn.title = 'Add a new photo to the Gallery section';
            addGalleryBtn.textContent = '+ Add Gallery Image';
            headerRow.appendChild(addGalleryBtn);
        }
    }

    section.appendChild(headerRow);
    if (futurePanel) section.appendChild(futurePanel);
    if (postedPanel) section.appendChild(postedPanel);
    if (cancelledPanel) section.appendChild(cancelledPanel);

    if (adhocBtn) {
        const adhocOpen = expandedAdhoc.has(platform);
        const adhocPanel = renderAdhocForm(platform, adhocOpen);
        section.appendChild(adhocPanel);
        adhocBtn.addEventListener('click', () => {
            const nowOpen = adhocPanel.hidden; // about to become open
            adhocPanel.hidden = !nowOpen;
            if (nowOpen) expandedAdhoc.add(platform);
            else expandedAdhoc.delete(platform);
        });
    }

    if (addCalendarBtn) {
        const calendarOpen = expandedAddCalendar.has(platform);
        addCalendarPanel = renderAddCalendarForm(calendarOpen);
        section.appendChild(addCalendarPanel);
        addCalendarBtn.addEventListener('click', () => {
            const nowOpen = addCalendarPanel.hidden;
            addCalendarPanel.hidden = !nowOpen;
            if (nowOpen) expandedAddCalendar.add(platform);
            else expandedAddCalendar.delete(platform);
        });
    }

    if (addMediaBtn) {
        const mediaOpen = expandedAddMedia.has(platform);
        addMediaPanel = renderAddMediaForm(mediaOpen);
        section.appendChild(addMediaPanel);
        addMediaBtn.addEventListener('click', () => {
            const nowOpen = addMediaPanel.hidden;
            addMediaPanel.hidden = !nowOpen;
            if (nowOpen) expandedAddMedia.add(platform);
            else expandedAddMedia.delete(platform);
        });
    }

    if (addGalleryBtn) {
        const galleryOpen = expandedAddGallery.has(platform);
        addGalleryPanel = renderAddGalleryForm(galleryOpen);
        section.appendChild(addGalleryPanel);
        addGalleryBtn.addEventListener('click', () => {
            const nowOpen = addGalleryPanel.hidden;
            addGalleryPanel.hidden = !nowOpen;
            if (nowOpen) expandedAddGallery.add(platform);
            else expandedAddGallery.delete(platform);
        });
    }

    const body = document.createElement('div');
    body.className = 'platform-section-body';
    body.hidden = !expanded;
    if (actionable.length === 0) {
        body.innerHTML = '<p class="section-empty-note">Nothing due right now.</p>';
    } else {
        for (const item of actionable) {
            body.appendChild(renderCard(item));
        }
    }
    section.appendChild(body);

    toggleBtn.addEventListener('click', () => {
        const nowExpanded = body.hidden; // about to become expanded
        body.hidden = !nowExpanded;
        toggleBtn.querySelector('.chevron').textContent = nowExpanded ? '▾' : '▸';
        if (nowExpanded) expandedPlatforms.add(platform);
        else expandedPlatforms.delete(platform);
    });

    return section;
}

function todayDateInputValue() {
    const d = new Date();
    const pad = (n) => String(n).padStart(2, '0');
    return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
}

function renderAdhocForm(platform, open) {
    const form = document.createElement('form');
    form.className = 'adhoc-form';
    form.hidden = !open;
    form.innerHTML = `
        <label>Content type
            <select name="contentType">${ADHOC_CONTENT_TYPES.map((t) => `<option value="${escapeHtml(t)}">${escapeHtml(t)}</option>`).join('')}</select>
        </label>
        <label>Category <input type="text" name="category" placeholder="e.g. Band News/Updates" maxlength="200" required></label>
        <label>What is it <input type="text" name="example" placeholder="Short description" maxlength="500" required></label>
        <label>Due <input type="date" name="dueDate" value="${todayDateInputValue()}"></label>
        <div class="adhoc-form-buttons">
            <button type="submit">Add</button>
            <button type="button" class="cancel-btn">Cancel</button>
        </div>
        <p class="save-note adhoc-status"></p>
    `;

    const contentTypeSelect = form.querySelector('[name="contentType"]');
    adhocContentTypesForPlatform(platform).then((types) => {
        contentTypeSelect.innerHTML = types.map((t) => `<option value="${escapeHtml(t)}">${escapeHtml(t)}</option>`).join('');
    });

    form.querySelector('.cancel-btn').addEventListener('click', () => {
        form.reset();
        form.querySelector('[name="dueDate"]').value = todayDateInputValue();
        form.hidden = true;
        expandedAdhoc.delete(platform);
    });

    form.addEventListener('submit', async (e) => {
        e.preventDefault();
        const statusEl = form.querySelector('.adhoc-status');
        const body = { platform, ...Object.fromEntries(new FormData(form).entries()) };

        const res = await fetch('/api/items/adhoc', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(body)
        });
        const result = await res.json();
        if (!res.ok) {
            statusEl.textContent = result.error || 'Could not add item.';
            return;
        }

        form.reset();
        form.querySelector('[name="dueDate"]').value = todayDateInputValue();
        expandedAdhoc.delete(platform);
        expandedPlatforms.add(platform); // so the new item is visible right away
        loadItems();
    });

    return form;
}

function renderAddCalendarForm(open) {
    const form = document.createElement('form');
    form.className = 'adhoc-form';
    form.hidden = !open;
    form.innerHTML = `
        <label>Title <input type="text" name="title" maxlength="200" required></label>
        <label>Venue <input type="text" name="venue" maxlength="200" required></label>
        <label>Venue link (optional) <input type="text" name="venueUrl" placeholder="https://" maxlength="500"></label>
        <label>Address <input type="text" name="address" placeholder="e.g. 119 North Loudoun Street, Winchester, VA" maxlength="300" required></label>
        <label>Date <input type="date" name="date" required></label>
        <label>Time (optional) <input type="text" name="time" placeholder="e.g. Doors: 7PM - Show: 8PM" maxlength="100"></label>
        <label>With (optional) <input type="text" name="withArtists" placeholder="e.g. Attica - A Nirvana Tribute" maxlength="200"></label>
        <label>With link (optional) <input type="text" name="withArtistsUrl" placeholder="https://" maxlength="500"></label>
        <fieldset class="ticket-mode-fieldset">
            <legend>Ticket info (optional - can be added later)</legend>
            <label class="radio-label"><input type="radio" name="ticketMode" value="url" checked> Tickets URL</label>
            <input type="text" name="ticketsUrl" maxlength="500" placeholder="https://...">
            <label class="radio-label"><input type="radio" name="ticketMode" value="free"> Free Admission</label>
            <label class="radio-label"><input type="radio" name="ticketMode" value="custom"> Custom Tickets Button text</label>
            <input type="text" name="customTicketsText" maxlength="60" placeholder='e.g. "$5 Door Cover"'>
        </fieldset>
        <label>Flyer (optional - can be added later)</label>
        <div class="media-field-slot" data-media-field="flyer"></div>
        <div class="adhoc-form-buttons">
            <button type="submit">Add</button>
            <button type="button" class="cancel-btn">Cancel</button>
        </div>
        <p class="save-note add-calendar-status"></p>
    `;
    form.querySelector('[data-media-field="flyer"]').appendChild(buildMediaFieldControl({ fieldName: 'flyer', accept: 'image/*' }));

    function updateTicketModeUI() {
        const mode = form.ticketMode.value;
        form.ticketsUrl.disabled = mode !== 'url';
        form.customTicketsText.disabled = mode !== 'custom';
    }
    form.querySelectorAll('input[name="ticketMode"]').forEach((r) => r.addEventListener('change', updateTicketModeUI));
    updateTicketModeUI();

    form.querySelector('.cancel-btn').addEventListener('click', () => {
        form.reset();
        form.hidden = true;
        expandedAddCalendar.delete('Website');
    });

    form.addEventListener('submit', async (e) => {
        e.preventDefault();
        const statusEl = form.querySelector('.add-calendar-status');
        statusEl.textContent = 'Adding...';

        const res = await fetch('/api/gigs', { method: 'POST', body: new FormData(form) });
        const result = await res.json();
        if (!res.ok) {
            statusEl.textContent = result.error || 'Could not add the gig.';
            return;
        }

        form.reset();
        updateTicketModeUI();
        expandedAddCalendar.delete('Website');
        expandedPlatforms.add('Website'); // so the new item is visible right away
        loadItems();
    });

    return form;
}

function renderAddMediaForm(open) {
    const form = document.createElement('form');
    form.className = 'adhoc-form';
    form.hidden = !open;
    form.innerHTML = `
        <label>Title <input type="text" name="title" maxlength="200" required></label>
        <label>YouTube or SoundCloud link <input type="text" name="mediaUrl" placeholder="https://..." required></label>
        <label>Tile art (optional - leave blank to use YouTube/SoundCloud's own thumbnail)</label>
        <div class="media-field-slot" data-media-field="art"></div>
        <div class="adhoc-form-buttons">
            <button type="submit">Add</button>
            <button type="button" class="cancel-btn">Cancel</button>
        </div>
        <p class="save-note add-media-status"></p>
    `;
    form.querySelector('[data-media-field="art"]').appendChild(buildMediaFieldControl({ fieldName: 'art', accept: 'image/*' }));

    form.querySelector('.cancel-btn').addEventListener('click', () => {
        form.reset();
        form.hidden = true;
        expandedAddMedia.delete('Website');
    });

    form.addEventListener('submit', async (e) => {
        e.preventDefault();
        const statusEl = form.querySelector('.add-media-status');
        statusEl.textContent = 'Adding...';

        const res = await fetch('/api/media', { method: 'POST', body: new FormData(form) });
        const result = await res.json();
        if (!res.ok) {
            statusEl.textContent = result.error || 'Could not add media item.';
            return;
        }

        form.reset();
        expandedAddMedia.delete('Website');
        expandedPlatforms.add('Website'); // so the new item is visible right away
        loadItems();
    });

    return form;
}

function renderAddGalleryForm(open) {
    const form = document.createElement('form');
    form.className = 'adhoc-form';
    form.hidden = !open;
    form.innerHTML = `
        <label>Caption <input type="text" name="alt" maxlength="200" required></label>
        <label>Photo</label>
        <div class="media-field-slot" data-media-field="photo"></div>
        <div class="adhoc-form-buttons">
            <button type="submit">Add</button>
            <button type="button" class="cancel-btn">Cancel</button>
        </div>
        <p class="save-note add-gallery-status"></p>
    `;
    form.querySelector('[data-media-field="photo"]').appendChild(buildMediaFieldControl({ fieldName: 'photo', accept: 'image/png,image/jpeg' }));

    form.querySelector('.cancel-btn').addEventListener('click', () => {
        form.reset();
        form.hidden = true;
        expandedAddGallery.delete('Website');
    });

    form.addEventListener('submit', async (e) => {
        e.preventDefault();
        const statusEl = form.querySelector('.add-gallery-status');

        if (!mediaFieldToFormData(form, 'photo', 'photo')) {
            statusEl.textContent = 'A photo is required.';
            return;
        }
        statusEl.textContent = 'Adding...';

        const res = await fetch('/api/gallery', { method: 'POST', body: new FormData(form) });
        const result = await res.json();
        if (!res.ok) {
            statusEl.textContent = result.error || 'Could not add gallery image.';
            return;
        }

        form.reset();
        expandedAddGallery.delete('Website');
        expandedPlatforms.add('Website'); // so the new item is visible right away
        loadItems();
    });

    return form;
}

function renderCard(item) {
    const node = cardTemplate.content.cloneNode(true);
    const card = node.querySelector('.card');
    card.dataset.id = item.id;
    card.classList.add(`status-${item.status.color}`);
    if (item.status.ready) card.classList.add('ready');

    // Top-left names the specific thing this tile is ("Gallery Image",
    // "Event Post", ...) instead of which platform it's on - the platform
    // is already the section this tile sits under, so repeating it here
    // was pure noise. No owner name shown at all anymore (same reasoning).
    node.querySelector('.platform').textContent = item.content_type;
    const linkedTitle = item.gig_title || item.media_title || item.gallery_title;
    const contentTypeEl = node.querySelector('.content-type');
    if (linkedTitle) {
        // Just the linked title - the type itself is already up top-left,
        // so "Gallery Image — <name>" would say it twice.
        contentTypeEl.textContent = linkedTitle;
    } else {
        contentTypeEl.remove();
    }
    // Category/example are pure clutter on the compact tile - the type is
    // already shown up top-left, and example (the "what to make" guidance)
    // resurfaces where it's actually useful: the Instructions alert for
    // no-api items, or the detail/edit view for everything else.
    node.querySelector('.category').remove();
    node.querySelector('.example').remove();
    node.querySelector('.due').textContent = item.status.label;

    const owedBadge = node.querySelector('.owed-badge');
    if (item.artifacts_owed) owedBadge.hidden = false;

    const artifactsBox = node.querySelector('.artifacts-needed');
    const presentByType = latestArtifactsByType(item.artifacts);
    for (const type of item.required_artifacts) {
        artifactsBox.appendChild(renderArtifactSlot(type, presentByType[type]));
    }

    const fartBtn = node.querySelector('.fart-btn');
    const manualBtn = node.querySelector('.manual-btn');
    const archiveBtn = node.querySelector('.archive-btn');
    const actionsBox = node.querySelector('.card-actions');
    const doneAlready = item.status.color === 'done' || item.status.color === 'auto';

    if (item.content_type === 'Calendar Listing') {
        // Not a one-time post - a living record kept accurate on the
        // live site. No "Fart it out"/"Mark Done" lifecycle; just an
        // editor that pushes straight to calendar.js.
        fartBtn.remove();
        manualBtn.remove();
        archiveBtn.remove();
        const editBtn = document.createElement('button');
        editBtn.type = 'button';
        editBtn.className = 'edit-site-btn';
        editBtn.textContent = 'Edit';
        editBtn.addEventListener('click', () => openWebsiteEditForm(item));
        actionsBox.appendChild(editBtn);
    } else if (item.content_type === 'Media Item') {
        // Same idea as Calendar Listing - a living entry, not a one-time
        // post, edited through its own form.
        fartBtn.remove();
        manualBtn.remove();
        archiveBtn.remove();
        const editBtn = document.createElement('button');
        editBtn.type = 'button';
        editBtn.className = 'edit-site-btn';
        editBtn.textContent = 'Edit';
        editBtn.addEventListener('click', () => openMediaEditForm(item));
        actionsBox.appendChild(editBtn);
    } else if (item.content_type === 'Gallery Image') {
        // Same idea again - a living entry, edited through its own form.
        fartBtn.remove();
        manualBtn.remove();
        archiveBtn.remove();
        const editBtn = document.createElement('button');
        editBtn.type = 'button';
        editBtn.className = 'edit-site-btn';
        editBtn.textContent = 'Edit';
        editBtn.addEventListener('click', () => openGalleryEditForm(item));
        actionsBox.appendChild(editBtn);
    } else if (item.status.color === 'done-pending') {
        // Posted for real (via "Fart it out") but not yet archived off the
        // board - see fartItOut's confirm prompt. The fart button becomes
        // a plain "Posted: <when>" readout, not an action; Mark Done no
        // longer makes sense (it's already posted); Archive closes it out
        // as done (not cancelled) and removes the tile.
        fartBtn.disabled = true;
        fartBtn.classList.remove('ready');
        fartBtn.textContent = item.status.label;
        manualBtn.remove();
        archiveBtn.textContent = 'Archive';
        archiveBtn.classList.add('confirm');
        archiveBtn.addEventListener('click', () => archivePostedItem(item.id));
    } else if (doneAlready) {
        fartBtn.remove();
        manualBtn.remove();
        archiveBtn.remove();
    } else if (!item.automated) {
        // No live automation path for this one right now - either the
        // platform has no API at all, or it does but isn't connected yet.
        // "Fart it out" would be pretending there's a call to make, so
        // show instructions instead, and "Mark Done" is the only real
        // completion step, not an "outside the system" alternative.
        fartBtn.remove();
        manualBtn.textContent = 'Mark Done';
        manualBtn.addEventListener('click', () => markDoneManual(item));
        archiveBtn.addEventListener('click', () => cancelItem(item));

        if (item.manual_instructions) {
            const instructionsBtn = document.createElement('button');
            instructionsBtn.type = 'button';
            instructionsBtn.className = 'instructions-btn';
            instructionsBtn.textContent = 'Instructions';
            instructionsBtn.addEventListener('click', () => showInstructions(item));
            actionsBox.insertBefore(instructionsBtn, manualBtn);
        }
    } else {
        if (item.status.ready) {
            fartBtn.disabled = false;
            fartBtn.classList.add('ready');
        }
        fartBtn.addEventListener('click', () => fartItOut(item.id, fartBtn));
        manualBtn.addEventListener('click', () => markDoneManual(item));
        archiveBtn.addEventListener('click', () => cancelItem(item));
    }

    // The flyer/tile art (or, for any other platform's tile, whatever
    // image artifact was uploaded for it) as a scaled-down background - a
    // visual preview of what's actually there, right on the tile. No
    // image yet is itself useful information (it's exactly what's
    // missing), so it's left alone rather than covered up: the existing
    // status-yellow ring/red ring already applies in that case.
    const tileImage = tileImageUrl(item);
    if (tileImage) {
        card.classList.add('has-tile-image');
        // A dark gradient layered into the same background-image stack
        // (not a separate overlay element) keeps the text over it
        // readable regardless of how bright the flyer/art is, with no
        // extra DOM/z-index bookkeeping needed.
        card.style.backgroundImage = `linear-gradient(rgba(20,20,20,0.72), rgba(20,20,20,0.72)), url(${JSON.stringify(tileImage)})`;
    }

    card.addEventListener('click', (e) => {
        // .artifacts-needed is status-only now (no controls of its own) -
        // clicking it opens the modal same as the rest of the tile. Its
        // one interactive child, the view/download link, already stops
        // its own propagation. .card-actions still has real buttons.
        if (e.target.closest('.card-actions')) return;
        if (item.content_type === 'Calendar Listing') openWebsiteEditForm(item);
        else if (item.content_type === 'Media Item') openMediaEditForm(item);
        else if (item.content_type === 'Gallery Image') openGalleryEditForm(item);
        else openDetailModal(item);
    });

    return node;
}

function tileImageUrl(item) {
    if (item.content_type === 'Media Item' && item.media && item.media.thumbnail) {
        return /^https?:\/\//i.test(item.media.thumbnail) ? item.media.thumbnail : `/flyer-cache/${item.media.thumbnail}`;
    }
    if (item.content_type === 'Gallery Image' && item.gallery && item.gallery.thumb) {
        return `/flyer-cache/${item.gallery.thumb}`;
    }
    // Whatever image artifact was actually uploaded for this specific post
    // (a Facebook Photo Album's photo, an Instagram Feed Post's own flyer
    // copy, ...) - takes priority over the gig's flyer below since it's
    // what's actually going out on this post, though in practice they're
    // usually the same image. Video/audio artifacts don't get this:
    // there's no thumbnail-generation for either (Jimp only handles still
    // images), so a video-only tile just stays a plain card.
    const imageArtifact = item.artifacts.find((a) => IMAGE_ARTIFACT_TYPES.has(a.artifact_type) && a.file_path);
    if (imageArtifact) return artifactUrl(imageArtifact.file_path);
    // Any gig-linked item without an image artifact of its own yet - a
    // Facebook Event Post, a Bandsintown listing/reminder, an Instagram
    // countdown post before its own flyer's been uploaded, and the
    // Website Calendar Listing tile itself - falls back to the gig's own
    // flyer, same image the Website tile shows directly.
    if (item.gig && item.gig.flyerMain) {
        return `/flyer-cache/${item.gig.flyerMain}`;
    }
    return null;
}

// Status-only on the tile face - no add/edit controls here anymore (that
// used to include a 3-mode file control whose "Paste URL" toggle had no
// visible room to actually paste anything in a compact tile, and a
// prompt()-based caption editor that worked but was a completely
// different mechanism from the file one). Adding/replacing anything now
// happens exclusively through the detail modal (renderDetailArtifactRow),
// reached by clicking the tile - one consistent editing surface instead
// of two.
function renderArtifactSlot(type, artifact) {
    const filled = !!artifact;
    const slot = document.createElement('div');
    slot.className = 'artifact-slot' + (filled ? ' filled' : '');
    const label = document.createElement('label');
    label.textContent = (filled ? '✓ ' : '+ ') + (ARTIFACT_LABELS[type] || type);
    slot.appendChild(label);

    if (filled && artifact.file_path) {
        const url = artifactUrl(artifact.file_path);
        const filename = artifact.file_path.split(/[\\/]/).pop();
        const viewLink = document.createElement('a');
        viewLink.className = 'artifact-view-link';
        if (IMAGE_ARTIFACT_TYPES.has(type)) {
            viewLink.href = '#';
            viewLink.textContent = 'view image';
            viewLink.addEventListener('click', (e) => { e.preventDefault(); e.stopPropagation(); openImagePopup(url, filename); });
        } else if (type === 'video') {
            viewLink.href = '#';
            viewLink.textContent = 'view video';
            viewLink.addEventListener('click', (e) => { e.preventDefault(); e.stopPropagation(); openVideoViewer(url, filename); });
        } else {
            viewLink.href = url;
            viewLink.target = '_blank';
            viewLink.rel = 'noopener';
            viewLink.download = '';
            viewLink.textContent = 'download';
            viewLink.addEventListener('click', (e) => e.stopPropagation());
        }
        slot.appendChild(viewLink);
    }

    return slot;
}

async function fartItOut(itemId, btn) {
    // Disabled synchronously, before the request even goes out - a second
    // click while the first is still in flight (no visible response yet
    // to signal "already working on it") posted the same thing twice.
    if (btn) btn.disabled = true;

    let res, body;
    try {
        res = await fetch(`/api/items/${itemId}/fart`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: '{}' });
        body = await res.json();
    } catch (err) {
        // A network-level failure (not just an error response) - without
        // this, the button would stay disabled forever with no feedback,
        // since nothing past this point would ever run.
        alert(`Couldn't post: ${err.message}. It may or may not have gone through - check before retrying.`);
        if (btn) btn.disabled = false;
        loadItems();
        return;
    }
    if (!res.ok || !body.ok) {
        alert(`Couldn't post: ${body.error || 'unknown error'}`);
        if (btn) btn.disabled = false; // failed - let them retry
        loadItems();
        return;
    }
    // Posted for real - stays on the board (see statusEngine's
    // 'done-pending' state) until this is confirmed, so a successful post
    // never just silently disappears.
    if (confirm('Posted! Archive this task now?')) {
        await fetch(`/api/items/${itemId}/archive-posted`, { method: 'POST' });
    }
    loadItems();
}

async function archivePostedItem(itemId) {
    await fetch(`/api/items/${itemId}/archive-posted`, { method: 'POST' });
    loadItems();
}

function showInstructions(item) {
    const text = fillPlaceholders(item.manual_instructions, item);
    // Rough heuristic - the instructions are written as plain sentences,
    // not pre-bulleted, so split on sentence boundaries to get a
    // reasonable step-by-step list rather than one dense paragraph.
    const steps = text.split(/(?<=[.!?])\s+/).filter((s) => s.trim());
    const header = `${item.content_type}${item.gig_title ? ' — ' + item.gig_title : ''}`;
    // What to make (item.example) isn't shown on the tile anymore - this
    // is where it resurfaces for no-api items, right alongside the steps
    // for actually posting it.
    const hint = item.example ? `${item.example}\n\n` : '';
    alert(`${header}\n\n${hint}${steps.map((s) => `• ${s}`).join('\n')}`);
}

function fillPlaceholders(text, item) {
    return text
        .replace(/\{title\}/g, item.gig?.title || item.example || '')
        .replace(/\{venue\}/g, item.gig?.venue || '')
        .replace(/\{date\}/g, item.gig?.date || '')
        .replace(/\{tickets_url\}/g, item.gig?.ticketsUrl || '');
}

async function markDoneManual(item) {
    const steps = item.manual_instructions ? fillPlaceholders(item.manual_instructions, item) : null;
    const confirmMessage = steps
        ? `${steps}\n\nDone with all of that? This marks it posted and stops it showing as due.`
        : 'Mark this posted outside the dashboard? It’ll stop showing as due.';
    if (!confirm(confirmMessage)) return;

    // The Event Post is what later posts reference via {event_url} - ask
    // for it right here, at the moment it's most natural to have it handy.
    if (item.content_type === 'Event Post') {
        const url = prompt('Paste the Facebook Event URL (used to link it from later countdown posts). Leave blank to skip.');
        if (url === null) return; // Cancel means abort the whole thing, not "skip the URL"
        if (url.trim()) {
            await fetch(`/api/items/${item.id}/upload`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ artifactType: 'event_url', text: url.trim() })
            });
        }
    }

    await fetch(`/api/items/${item.id}/mark-done-manual`, { method: 'POST' });
    loadItems();
}

// For something that's genuinely never going to be posted (the show got
// cancelled, a post idea got dropped) - distinct from Mark Done so the
// "Posted" and "Cancelled" lists downstream stay honest about what
// actually happened, not just "no longer showing as due."
async function cancelItem(item) {
    if (!confirm('Cancel/archive this? It stops showing as due and is recorded as cancelled, not posted.')) return;
    await fetch(`/api/items/${item.id}/cancel`, { method: 'POST' });
    loadItems();
}

// --- Tile detail view ---

function artifactUrl(filePath) {
    return '/' + filePath.replace(/\\/g, '/').replace(/^data\//, '');
}

// Shows the gig's own site data (flyer, venue, date, ticket link) as
// grab-and-go reference material - relevant even when this item has no
// artifacts of its own to upload (e.g. an Event Post, where the "content"
// is just the show details typed into Facebook's own event form).
function renderGigReference(gig) {
    const box = document.createElement('div');
    box.className = 'detail-gig-reference';

    const heading = document.createElement('h3');
    heading.textContent = 'From the site';
    box.appendChild(heading);

    if (gig.flyerMain) {
        const flyerRow = document.createElement('p');
        flyerRow.textContent = 'Flyer: ';
        const link = document.createElement('a');
        link.href = '#';
        link.textContent = 'view image';
        const url = `/flyer-cache/${gig.flyerMain}`;
        const filename = gig.flyerMain.split(/[\\/]/).pop();
        link.addEventListener('click', (e) => { e.preventDefault(); openImagePopup(url, filename); });
        flyerRow.appendChild(link);
        box.appendChild(flyerRow);
    }

    // Same ticketMode logic as calendar.js's own render (with the same
    // legacy fallback for gigs that predate the field).
    const ticketMode = gig.ticketMode || (gig.freeAdmission ? 'free' : (gig.customTicketsText ? 'custom' : 'url'));
    const ticketsValue = ticketMode === 'free' ? 'Free Admission' : ticketMode === 'custom' ? gig.customTicketsText : gig.ticketsUrl;
    const ticketsIsLink = ticketMode === 'url' && !!gig.ticketsUrl;

    const fields = [
        ['Venue', gig.venue, gig.venueUrl],
        ['Address', gig.address],
        ['Date', gig.date],
        ['Time', gig.time],
        ['With', gig.withArtists, gig.withArtistsUrl],
        ['Tickets', ticketsValue, ticketsIsLink ? ticketsValue : null]
    ];
    for (const [label, value, linkUrl] of fields) {
        if (!value) continue;
        const row = document.createElement('p');
        if (linkUrl) {
            row.innerHTML = `${label}: <a href="${escapeHtml(linkUrl)}" target="_blank" rel="noopener">${escapeHtml(value)}</a>`;
        } else {
            row.textContent = `${label}: ${value}`;
        }
        box.appendChild(row);
    }

    return box;
}

function renderDetailArtifactRow(item, type, artifact) {
    const row = document.createElement('div');
    row.className = 'detail-artifact-row' + (artifact ? ' filled' : '');

    const label = ARTIFACT_LABELS[type] || type;

    if (TEXT_ARTIFACT_TYPES.has(type)) {
        row.classList.add('text-row');
        // A real textarea instead of the browser's native prompt() -
        // prompt() submits the whole dialog on Enter instead of starting a
        // new line, so anything typed after a line break (a blank line
        // before hashtags, say) never made it into the saved text at all.
        const info = document.createElement('div');
        info.className = 'detail-artifact-info';
        info.textContent = label;
        row.appendChild(info);

        const suggested = type === 'caption' && item.gig_title ? item.example : '';
        const textarea = document.createElement('textarea');
        textarea.className = 'detail-artifact-textarea';
        textarea.rows = 4;
        textarea.value = (artifact && artifact.text_value) || suggested || '';
        textarea.addEventListener('click', (e) => e.stopPropagation());
        row.appendChild(textarea);

        const saveBtn = document.createElement('button');
        saveBtn.type = 'button';
        saveBtn.textContent = artifact ? 'Save' : 'Add';
        saveBtn.addEventListener('click', async (e) => {
            e.stopPropagation();
            const text = textarea.value.trim();
            if (!text) return;
            await fetch(`/api/items/${item.id}/upload`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ artifactType: type, text })
            });
            closeDetailModal();
            loadItems();
        });
        row.appendChild(saveBtn);
    } else {
        const info = document.createElement('div');
        info.className = 'detail-artifact-info';
        if (artifact && artifact.file_path) {
            const url = artifactUrl(artifact.file_path);
            const filename = artifact.file_path.split(/[\\/]/).pop();
            info.textContent = `${label}: `;
            const link = document.createElement('a');
            if (IMAGE_ARTIFACT_TYPES.has(type)) {
                link.href = '#';
                link.textContent = 'view image';
                link.addEventListener('click', (e) => { e.preventDefault(); openImagePopup(url, filename); });
            } else if (type === 'video') {
                link.href = '#';
                link.textContent = 'view video';
                link.addEventListener('click', (e) => { e.preventDefault(); openVideoViewer(url, filename); });
            } else {
                link.href = url;
                link.target = '_blank';
                link.rel = 'noopener';
                link.download = '';
                link.textContent = 'download';
            }
            info.appendChild(link);
        } else {
            info.textContent = `${label}: not added yet`;
        }
        row.appendChild(info);

        const control = buildMediaSlotControl({
            accept: ARTIFACT_ACCEPT[type],
            label: ARTIFACT_LABELS[type],
            onResolved: async (resolved) => {
                const form = new FormData();
                form.append('artifactType', type);
                if (resolved.mode === 'file') form.append('file', resolved.file);
                else if (resolved.mode === 'catalogItemId') form.append('catalogItemId', resolved.id);
                else if (resolved.mode === 'url') form.append('url', resolved.url);
                const res = await fetch(`/api/items/${item.id}/upload`, { method: 'POST', body: form });
                const body = await res.json().catch(() => ({}));
                if (!res.ok) { alert(body.error || 'Could not add this.'); return; }
                closeDetailModal();
                loadItems();
            }
        });
        row.appendChild(control);
    }

    return row;
}

function openDetailModal(item) {
    const backdrop = document.getElementById('detail-modal-backdrop');
    const body = document.getElementById('detail-modal-body');
    body.innerHTML = '';

    const title = document.createElement('h2');
    title.textContent = item.gig_title ? `${item.content_type} — ${item.gig_title}` : item.content_type;
    body.appendChild(title);

    const meta = document.createElement('p');
    meta.className = 'detail-meta';
    meta.textContent = `${item.platform} · ${item.category} · ${item.owner || 'unassigned'} · ${item.status.label}`;
    body.appendChild(meta);

    if (item.example) {
        const example = document.createElement('p');
        example.className = 'detail-example';
        example.textContent = item.example;
        body.appendChild(example);
    }

    if (item.gig_issues && item.gig_issues.length > 0) {
        const issues = document.createElement('div');
        issues.className = 'detail-gig-issues';
        issues.innerHTML = `Still incomplete on the site itself:<ul>${item.gig_issues.map((i) => `<li>${escapeHtml(i)}</li>`).join('')}</ul>`;
        body.appendChild(issues);
    }

    if (item.gig) {
        body.appendChild(renderGigReference(item.gig));

        // The gig record itself (venue/date/tickets/flyer) isn't this
        // post's own artifact - it lives on the underlying show, edited
        // through the exact same form the Website platform's Calendar
        // Listing tile uses. Surfacing it here too means a Facebook/
        // Instagram/Bandsintown tile that's flagging "no ticket link on
        // the site yet" can actually be fixed from where you're looking
        // at it, instead of having to go hunt down the matching Website
        // tile - openWebsiteEditForm only needs item.gig/gig_ref/gig_title,
        // all of which every gig-linked item already carries regardless
        // of platform.
        const editGigBtn = document.createElement('button');
        editGigBtn.type = 'button';
        editGigBtn.className = 'edit-site-btn';
        editGigBtn.textContent = 'Edit show details (venue, date, tickets, flyer...)';
        editGigBtn.addEventListener('click', () => openWebsiteEditForm(item));
        body.appendChild(editGigBtn);
    }

    const artifactList = document.createElement('div');
    artifactList.className = 'detail-artifact-list';
    const presentByType = latestArtifactsByType(item.artifacts);
    const allTypes = new Set([...item.required_artifacts, ...Object.keys(presentByType)]);
    for (const type of allTypes) {
        artifactList.appendChild(renderDetailArtifactRow(item, type, presentByType[type]));
    }
    body.appendChild(artifactList);

    backdrop.hidden = false;
}

function openWebsiteEditForm(item) {
    const backdrop = document.getElementById('detail-modal-backdrop');
    const body = document.getElementById('detail-modal-body');
    body.innerHTML = '';
    const gig = item.gig || {};

    const title = document.createElement('h2');
    title.textContent = `Edit calendar listing — ${gig.title || item.gig_title || ''}`;
    body.appendChild(title);

    if (item.gig_issues && item.gig_issues.length > 0) {
        const issues = document.createElement('div');
        issues.className = 'detail-gig-issues';
        issues.innerHTML = `Still incomplete:<ul>${item.gig_issues.map((i) => `<li>${escapeHtml(i)}</li>`).join('')}</ul>`;
        body.appendChild(issues);
    }

    // Which of these three shows on the site is its own explicit field
    // (ticketMode) - the values themselves are just reference data, kept
    // around regardless of which one is currently active. Gigs from
    // before ticketMode existed fall back to the old freeAdmission/
    // customTicketsText/ticketsUrl priority, same as calendar.js's render.
    const ticketMode = gig.ticketMode || (gig.freeAdmission ? 'free' : (gig.customTicketsText ? 'custom' : 'url'));

    const form = document.createElement('form');
    form.className = 'website-edit-form';
    form.innerHTML = `
        <label>Title <input type="text" name="title" value="${escapeHtml(gig.title || '')}" maxlength="200" required></label>
        <label>Venue <input type="text" name="venue" value="${escapeHtml(gig.venue || '')}" maxlength="200"></label>
        <label>Venue link (optional) <input type="text" name="venueUrl" value="${escapeHtml(gig.venueUrl || '')}" maxlength="500" placeholder="https:// - the venue name links here on the site"></label>
        <label>Address <input type="text" name="address" value="${escapeHtml(gig.address || '')}" maxlength="300" placeholder="e.g. 119 North Loudoun Street, Winchester, VA" required></label>
        <label>Date <input type="text" name="date" value="${escapeHtml(gig.date || '')}" maxlength="100" placeholder="e.g. Friday, October 3, 2026"></label>
        <label>Time (optional) <input type="text" name="time" value="${escapeHtml(gig.time || '')}" maxlength="100" placeholder="e.g. Doors: 7PM - Show: 8PM"></label>
        <label>With (optional) <input type="text" name="withArtists" value="${escapeHtml(gig.withArtists || '')}" maxlength="200" placeholder="e.g. Attica - A Nirvana Tribute"></label>
        <label>With link (optional) <input type="text" name="withArtistsUrl" value="${escapeHtml(gig.withArtistsUrl || '')}" maxlength="500" placeholder="https://"></label>
        <fieldset class="ticket-mode-fieldset">
            <legend>Ticket info</legend>
            <label class="radio-label"><input type="radio" name="ticketMode" value="url" ${ticketMode === 'url' ? 'checked' : ''}> Tickets URL</label>
            <input type="text" name="ticketsUrl" value="${escapeHtml(gig.ticketsUrl || '')}" maxlength="500" placeholder="https://...">
            <label class="radio-label"><input type="radio" name="ticketMode" value="free" ${ticketMode === 'free' ? 'checked' : ''}> Free Admission</label>
            <label class="radio-label"><input type="radio" name="ticketMode" value="custom" ${ticketMode === 'custom' ? 'checked' : ''}> Custom Tickets Button text</label>
            <input type="text" name="customTicketsText" value="${escapeHtml(gig.customTicketsText || '')}" maxlength="60" placeholder='e.g. "$5 Door Cover"'>
        </fieldset>
        ${gig.flyerMain ? `<p class="current-flyer">Current flyer: <a href="#" class="current-flyer-link">view image</a></p>` : ''}
        <label>${gig.flyerMain ? 'Replace' : 'Add'} flyer</label>
        <div class="media-field-slot" data-media-field="flyer"></div>
        <button type="submit">Save to site</button>
        <p class="save-note website-edit-status"></p>
    `;
    body.appendChild(form);
    form.querySelector('[data-media-field="flyer"]').appendChild(buildMediaFieldControl({ fieldName: 'flyer', accept: 'image/*' }));

    function updateTicketModeUI() {
        const mode = form.ticketMode.value;
        form.ticketsUrl.disabled = mode !== 'url';
        form.customTicketsText.disabled = mode !== 'custom';
    }
    form.querySelectorAll('input[name="ticketMode"]').forEach((r) => r.addEventListener('change', updateTicketModeUI));
    updateTicketModeUI();

    if (gig.flyerMain) {
        const url = `/flyer-cache/${gig.flyerMain}`;
        const filename = gig.flyerMain.split(/[\\/]/).pop();
        form.querySelector('.current-flyer-link').addEventListener('click', (e) => {
            e.preventDefault();
            openImagePopup(url, filename);
        });
    }

    const deleteBtn = document.createElement('button');
    deleteBtn.type = 'button';
    deleteBtn.className = 'delete-listing-btn';
    deleteBtn.textContent = 'Delete this gig';
    deleteBtn.addEventListener('click', async () => {
        if (!confirm(`Delete "${gig.title || item.gig_title}" from the site entirely? This removes it from calendar.js and every related task on every platform - there's no local-only undo.`)) return;
        const res = await fetch(`/api/gigs/${item.gig_ref}`, { method: 'DELETE' });
        if (!res.ok) {
            const result = await res.json().catch(() => ({}));
            alert(`Couldn't delete: ${result.error || 'unknown error'}`);
            return;
        }
        closeDetailModal();
        loadItems();
    });
    body.appendChild(deleteBtn);

    form.addEventListener('submit', async (e) => {
        e.preventDefault();
        const statusEl = form.querySelector('.website-edit-status');
        statusEl.textContent = 'Saving...';

        const fields = {};
        for (const key of ['title', 'venue', 'venueUrl', 'date', 'time', 'address', 'withArtists', 'withArtistsUrl']) {
            const value = form[key].value.trim();
            const original = gig[key] || '';
            if (key === 'title' || key === 'address') {
                // Both required in this form (address is rendered straight
                // into a Google Maps link on the site, same as title being
                // required), so a blank value here would only mean nothing
                // was typed yet - never send it.
                if (value && value !== original) fields[key] = value;
                continue;
            }
            // Everything else can be legitimately cleared - a blank value
            // is itself a meaningful state (an optional detail the gig no
            // longer has, or - for venue/date - shows the tile/site as
            // incomplete again), so it has to actually reach the server,
            // not get silently dropped just because it's falsy.
            if (value !== original) fields[key] = value;
        }

        // Which of Tickets URL/Free Admission/Custom text is active is its
        // own field (ticketMode) - the values themselves are never cleared
        // just because a different mode is selected, so switching away and
        // back later doesn't require retyping anything.
        const currentMode = gig.ticketMode || (gig.freeAdmission ? 'free' : (gig.customTicketsText ? 'custom' : 'url'));
        const mode = form.ticketMode.value;
        if (mode !== currentMode) fields.ticketMode = mode;

        const url = form.ticketsUrl.value.trim();
        if (url !== (gig.ticketsUrl || '')) fields.ticketsUrl = url;

        const customText = form.customTicketsText.value.trim();
        if (customText !== (gig.customTicketsText || '')) fields.customTicketsText = customText;

        const messages = [];
        let hadError = false;

        if (Object.keys(fields).length > 0) {
            const res = await fetch(`/api/gigs/${item.gig_ref}`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(fields)
            });
            const result = await res.json();
            if (!res.ok) { messages.push(`Details: ${result.error}`); hadError = true; }
            else messages.push('Details saved.');
        }

        const flyerForm = mediaFieldToFormData(form, 'flyer', 'file');
        if (flyerForm) {
            const res = await fetch(`/api/gigs/${item.gig_ref}/flyer`, { method: 'POST', body: flyerForm });
            const result = await res.json();
            if (!res.ok) { messages.push(`Flyer: ${result.error}`); hadError = true; }
            else messages.push('Flyer saved.');
        }

        if (messages.length === 0) { statusEl.textContent = 'Nothing changed.'; return; }
        statusEl.textContent = messages.join(' ');
        statusEl.classList.toggle('error-text', hadError);
        if (!hadError) loadItems();
    });

    backdrop.hidden = false;
}

function openMediaEditForm(item) {
    const backdrop = document.getElementById('detail-modal-backdrop');
    const body = document.getElementById('detail-modal-body');
    body.innerHTML = '';
    const media = item.media || {};

    const title = document.createElement('h2');
    title.textContent = `Edit media — ${media.title || item.media_title || ''}`;
    body.appendChild(title);

    if (item.media_issues && item.media_issues.length > 0) {
        const issues = document.createElement('div');
        issues.className = 'detail-gig-issues';
        issues.innerHTML = `Still incomplete:<ul>${item.media_issues.map((i) => `<li>${escapeHtml(i)}</li>`).join('')}</ul>`;
        body.appendChild(issues);
    }

    const form = document.createElement('form');
    form.className = 'website-edit-form';
    form.innerHTML = `
        <label>Title <input type="text" name="title" value="${escapeHtml(media.title || '')}" maxlength="200" required></label>
        <label>YouTube or SoundCloud link <input type="text" name="mediaUrl" value="" placeholder="paste a new link to change it - leave blank to keep the current one"></label>
        ${media.thumbnail ? `<p class="current-flyer">Current tile art: <a href="#" class="current-flyer-link">view image</a></p>` : ''}
        <label>Replace tile art</label>
        <div class="media-field-slot" data-media-field="art"></div>
        <button type="submit">Save to site</button>
        <p class="save-note website-edit-status"></p>
    `;
    body.appendChild(form);
    form.querySelector('[data-media-field="art"]').appendChild(buildMediaFieldControl({ fieldName: 'art', accept: 'image/*' }));

    if (media.thumbnail) {
        // A handful of older items point straight at an external image
        // (e.g. YouTube's own thumbnail CDN) instead of a site-relative
        // path - nothing local to mirror, link to it directly.
        const isExternal = /^https?:\/\//i.test(media.thumbnail);
        const url = isExternal ? media.thumbnail : `/flyer-cache/${media.thumbnail}`;
        const filename = media.thumbnail.split(/[\\/]/).pop();
        form.querySelector('.current-flyer-link').addEventListener('click', (e) => {
            e.preventDefault();
            openImagePopup(url, filename);
        });
    }

    const deleteBtn = document.createElement('button');
    deleteBtn.type = 'button';
    deleteBtn.className = 'delete-listing-btn';
    deleteBtn.textContent = 'Delete this media item';
    deleteBtn.addEventListener('click', async () => {
        if (!confirm(`Delete "${media.title || item.media_title}" from the Media section? This removes it from the live site entirely - there's no local-only undo.`)) return;
        await fetch(`/api/media/${item.media_ref}`, { method: 'DELETE' });
        closeDetailModal();
        loadItems();
    });
    body.appendChild(deleteBtn);

    form.addEventListener('submit', async (e) => {
        e.preventDefault();
        const statusEl = form.querySelector('.website-edit-status');
        statusEl.textContent = 'Saving...';

        const fields = {};
        const newTitle = form.title.value.trim();
        if (newTitle && newTitle !== (media.title || '')) fields.title = newTitle;
        const newUrl = form.mediaUrl.value.trim();
        if (newUrl) fields.url = newUrl;

        const messages = [];
        let hadError = false;

        if (Object.keys(fields).length > 0) {
            const res = await fetch(`/api/media/${item.media_ref}`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(fields)
            });
            const result = await res.json();
            if (!res.ok) { messages.push(`Details: ${result.error}`); hadError = true; }
            else messages.push('Details saved.');
        }

        const artForm = mediaFieldToFormData(form, 'art', 'file');
        if (artForm) {
            const res = await fetch(`/api/media/${item.media_ref}/art`, { method: 'POST', body: artForm });
            const result = await res.json();
            if (!res.ok) { messages.push(`Tile art: ${result.error}`); hadError = true; }
            else messages.push('Tile art saved.');
        }

        if (messages.length === 0) { statusEl.textContent = 'Nothing changed.'; return; }
        statusEl.textContent = messages.join(' ');
        statusEl.classList.toggle('error-text', hadError);
        if (!hadError) loadItems();
    });

    backdrop.hidden = false;
}

function openGalleryEditForm(item) {
    const backdrop = document.getElementById('detail-modal-backdrop');
    const body = document.getElementById('detail-modal-body');
    body.innerHTML = '';
    const gallery = item.gallery || {};

    const title = document.createElement('h2');
    title.textContent = `Edit gallery image — ${gallery.alt || item.gallery_title || ''}`;
    body.appendChild(title);

    if (item.gallery_issues && item.gallery_issues.length > 0) {
        const issues = document.createElement('div');
        issues.className = 'detail-gig-issues';
        issues.innerHTML = `Still incomplete:<ul>${item.gallery_issues.map((i) => `<li>${escapeHtml(i)}</li>`).join('')}</ul>`;
        body.appendChild(issues);
    }

    const form = document.createElement('form');
    form.className = 'website-edit-form';
    form.innerHTML = `
        <label>Caption <input type="text" name="alt" value="${escapeHtml(gallery.alt || '')}" maxlength="200" required></label>
        ${gallery.full ? `<p class="current-flyer">Current photo: <a href="#" class="current-flyer-link">view image</a></p>` : ''}
        <label>Replace photo</label>
        <div class="media-field-slot" data-media-field="photo"></div>
        <button type="submit">Save to site</button>
        <p class="save-note website-edit-status"></p>
    `;
    body.appendChild(form);
    form.querySelector('[data-media-field="photo"]').appendChild(buildMediaFieldControl({ fieldName: 'photo', accept: 'image/png,image/jpeg' }));

    if (gallery.full) {
        const url = `/flyer-cache/${gallery.full}`;
        const filename = gallery.full.split(/[\\/]/).pop();
        form.querySelector('.current-flyer-link').addEventListener('click', (e) => {
            e.preventDefault();
            openImagePopup(url, filename);
        });
    }

    const deleteBtn = document.createElement('button');
    deleteBtn.type = 'button';
    deleteBtn.className = 'delete-listing-btn';
    deleteBtn.textContent = 'Delete this gallery image';
    deleteBtn.addEventListener('click', async () => {
        if (!confirm(`Delete "${gallery.alt || item.gallery_title}" from the Gallery section? This removes it from the live site entirely - there's no local-only undo.`)) return;
        await fetch(`/api/gallery/${item.gallery_ref}`, { method: 'DELETE' });
        closeDetailModal();
        loadItems();
    });
    body.appendChild(deleteBtn);

    form.addEventListener('submit', async (e) => {
        e.preventDefault();
        const statusEl = form.querySelector('.website-edit-status');
        statusEl.textContent = 'Saving...';

        const messages = [];
        let hadError = false;

        const newAlt = form.alt.value.trim();
        if (newAlt && newAlt !== (gallery.alt || '')) {
            const res = await fetch(`/api/gallery/${item.gallery_ref}`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ alt: newAlt })
            });
            const result = await res.json();
            if (!res.ok) { messages.push(`Caption: ${result.error}`); hadError = true; }
            else messages.push('Caption saved.');
        }

        const photoForm = mediaFieldToFormData(form, 'photo', 'file');
        if (photoForm) {
            const res = await fetch(`/api/gallery/${item.gallery_ref}/photo`, { method: 'POST', body: photoForm });
            const result = await res.json();
            if (!res.ok) { messages.push(`Photo: ${result.error}`); hadError = true; }
            else messages.push('Photo saved.');
        }

        if (messages.length === 0) { statusEl.textContent = 'Nothing changed.'; return; }
        statusEl.textContent = messages.join(' ');
        statusEl.classList.toggle('error-text', hadError);
        if (!hadError) loadItems();
    });

    backdrop.hidden = false;
}

function closeDetailModal() {
    document.getElementById('detail-modal-backdrop').hidden = true;
}

document.getElementById('detail-modal-close').addEventListener('click', closeDetailModal);
document.getElementById('detail-modal-backdrop').addEventListener('click', (e) => {
    if (e.target.id === 'detail-modal-backdrop') closeDetailModal();
});

// Image artifacts (flyer, photo) pop up full-size instead of navigating
// away - a Download button inside does the actual save. Video/audio keep
// plain download links since a lightbox doesn't make sense for those.
const IMAGE_ARTIFACT_TYPES = new Set(['photo', 'flyer']);
function openImagePopup(url, filename) {
    const backdrop = document.getElementById('image-modal-backdrop');
    const img = document.getElementById('image-modal-img');
    const dl = document.getElementById('image-modal-download');
    img.src = url;
    img.alt = filename || 'Image';
    dl.href = url;
    dl.download = filename || '';
    backdrop.hidden = false;
}
function closeImagePopup() {
    document.getElementById('image-modal-backdrop').hidden = true;
    document.getElementById('image-modal-img').src = '';
}
document.getElementById('image-modal-close').addEventListener('click', closeImagePopup);
document.getElementById('image-modal-backdrop').addEventListener('click', (e) => {
    if (e.target.id === 'image-modal-backdrop') closeImagePopup();
});

document.addEventListener('keydown', (e) => {
    if (e.key !== 'Escape') return;
    closeImagePopup();
    closeDetailModal();
});

loadItems();
setInterval(loadItems, 60000);
