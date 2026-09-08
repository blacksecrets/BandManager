// Which <template> holds a platform's hand-authored detail content -
// reserved for platforms with real bespoke tooling beyond a plain
// credential form (Facebook's auto-setup/verify/cover-photo modal).
// Everything else renders generically from platform.credential_fields
// (see renderGenericCredForm) - a platform with no entry here and no
// credential_fields is purely informational (no form, just instructions).
const TEMPLATE_FOR_PLATFORM = {
    facebook: 'tpl-meta',
    spotify: 'tpl-spotify',
    youtube: 'tpl-youtube',
    tiktok: 'tpl-tiktok'
};

let platformsById = {};
// platform id -> function that re-applies that platform's field
// visibility (radio-controlled fields like Instagram's igAccessToken) -
// populated per-form as renderGenericCredForm builds it, called again
// after loadSavedCredentialValues prefills a saved mode (setting a
// RadioNodeList's .value doesn't fire 'change' on its own).
const visibilityReapplyByPlatform = {};

function escapeHtml(str) {
    const div = document.createElement('div');
    div.textContent = str;
    return div.innerHTML;
}

async function loadPlatforms() {
    const res = await fetch('/api/platforms');
    // Not logged in, or no active band selected yet - same class of bug
    // dashboard.js's loadItems had (see there for the fuller explanation):
    // without this, res.json() on the error body throws and the page is
    // stuck on its initial "Loading platforms..." forever.
    if (res.status === 401) {
        location.href = '/login.html';
        return;
    }
    if (res.status === 403 || res.status === 400) {
        document.getElementById('platform-accordion').textContent = 'Select a band from the switcher above to continue.';
        return;
    }
    const platforms = await res.json();
    platformsById = Object.fromEntries(platforms.map((p) => [p.id, p]));

    renderPlatformAccordion(platforms);
    // Everything below queries elements that only exist once the accordion
    // (and the <template> content cloned into it) is in the DOM - wiring
    // any of this earlier would silently find nothing.
    wireInstructionsToggles();
    wireOnboardToggles();
    wireDisconnectButtons();
    wireCredForms();
    wireMetaManualForm();
    wireCoverPhotoTool();
    wireAutoSetupTool();
    wireVerifyTool();
    wireMetaSetupModal();
    loadMetaAppConfig();
    loadSavedCredentialValues();
    wireReusePickers();
    wireSpotifyConnect();
    wireYouTubeConnect();
    wireTikTokConnect();

    // The main dashboard's "Set up X automation" links land here as
    // #meta-section/#gbp-section/#website-section - open (and scroll to)
    // that row instead of leaving it collapsed under an anchor that no
    // longer auto-reveals anything on its own.
    if (location.hash) {
        const target = document.querySelector(location.hash);
        if (target?.classList.contains('platform-section')) {
            const rowId = target.dataset.platformRow;
            expandRow(rowId);
            target.scrollIntoView({ behavior: 'smooth', block: 'start' });
        }
    }
}

// One row per platform (same pattern as the main dashboard's collapsible
// platform sections - same CSS classes, reused as-is rather than
// inventing a parallel layout). Facebook and Instagram are two separate
// rows, each independently configured.
function renderPlatformAccordion(platforms) {
    const container = document.getElementById('platform-accordion');
    container.innerHTML = '';

    for (const platform of platforms) {
        const isMeta = platform.id === 'facebook';
        const rowId = isMeta ? 'meta' : platform.id;
        const rowLabel = isMeta ? 'Facebook' : platform.display_name;

        const section = document.createElement('section');
        section.className = 'platform-section';
        section.dataset.platformRow = rowId;
        // Matches the anchor ids the main dashboard's "Set up X automation"
        // links (SETTINGS_ANCHOR in dashboard.js) already point at.
        section.id = rowId === 'googleBusiness' ? 'gbp-section' : `${rowId}-section`;

        const header = document.createElement('div');
        header.className = 'platform-section-header';

        const toggleBtn = document.createElement('button');
        toggleBtn.type = 'button';
        toggleBtn.className = 'section-toggle';
        toggleBtn.innerHTML = `
            <span class="chevron">&#9656;</span>
            <span class="platform-section-title">${escapeHtml(rowLabel)}</span>
        `;
        header.appendChild(toggleBtn);

        // "We post here" - independent of whether it's automated (see
        // Account.IsOnboarded's doc comment). Real for every platform,
        // including ones with no API at all (currently just TikTok,
        // pending its own developer audit) - those just never grow any
        // connect UI under it.
        const onboardLabel = document.createElement('label');
        onboardLabel.className = 'onboard-toggle-label';
        onboardLabel.title = 'We post to this platform';
        onboardLabel.innerHTML = `<input type="checkbox" class="onboard-toggle" data-platform="${platform.id}"> <span>We post here</span>`;
        onboardLabel.addEventListener('click', (e) => e.stopPropagation());
        header.appendChild(onboardLabel);

        if (platform.credential_fields || TEMPLATE_FOR_PLATFORM[platform.id]) {
            const badge = document.createElement('span');
            badge.className = 'status-badge';
            badge.dataset.platform = platform.id;
            badge.textContent = 'checking...';
            header.appendChild(badge);
        } else {
            const badge = document.createElement('span');
            badge.className = 'status-badge info-badge';
            badge.textContent = 'No connection needed';
            header.appendChild(badge);
        }

        section.appendChild(header);

        const body = document.createElement('div');
        body.className = 'platform-section-body';
        body.hidden = true;

        // Instructions are collapsed on their own, separate from whatever
        // form/tooling sits below - most visits here are "fill in the
        // form," not "read the steps again."
        if (platform.setup_instructions) {
            const instrWrap = document.createElement('div');
            instrWrap.className = 'instructions-toggle-wrap';
            instrWrap.innerHTML = `
                <button type="button" class="instructions-toggle">Instructions <span class="chevron">&#9656;</span></button>
                <div class="instructions-panel" hidden>${platform.setup_instructions}</div>
            `;
            body.appendChild(instrWrap);
        }

        const templateId = TEMPLATE_FOR_PLATFORM[platform.id];
        if (templateId) {
            const tpl = document.getElementById(templateId);
            if (tpl) body.appendChild(tpl.content.cloneNode(true));
        } else if (platform.credential_fields) {
            body.appendChild(renderGenericCredForm(platform));
        } else {
            const note = document.createElement('p');
            note.className = 'tile-note';
            note.textContent = 'No usable posting API for this - "Fart it out" just records that you posted it yourself.';
            body.appendChild(note);
        }

        section.appendChild(body);
        container.appendChild(section);

        toggleBtn.addEventListener('click', () => {
            body.hidden = !body.hidden;
            toggleBtn.querySelector('.chevron').innerHTML = body.hidden ? '&#9656;' : '&#9662;';
        });
    }
}

// Builds a full credential form (fields, save button, status line, plus a
// disconnect-tool row) purely from platform.credential_fields - adding a
// field, changing its type, or making one conditionally required (like
// Instagram's igAccessToken) is a PlatformSeedData.cs edit, not new HTML/
// JS here. Reserved for platforms without real bespoke tooling - Facebook
// keeps its own hand-authored template (see TEMPLATE_FOR_PLATFORM).
function renderGenericCredForm(platform) {
    const wrap = document.createElement('div');

    const form = document.createElement('form');
    form.className = 'cred-form';
    form.dataset.platform = platform.id;
    form.innerHTML = `<h3>${escapeHtml(platform.display_name)} <span class="status-badge" data-platform="${platform.id}">checking...</span></h3>`;

    for (const field of platform.credential_fields) {
        form.appendChild(renderCredField(field));
    }

    const submitBtn = document.createElement('button');
    submitBtn.type = 'submit';
    submitBtn.textContent = `Save ${platform.display_name} credentials`;
    form.appendChild(submitBtn);

    const status = document.createElement('p');
    status.className = 'save-note cred-form-status';
    status.setAttribute('data-cred-status', '');
    form.appendChild(status);

    wrap.appendChild(form);

    const disconnect = document.createElement('div');
    disconnect.className = 'disconnect-tool';
    disconnect.setAttribute('data-disconnect-tool', platform.id);
    disconnect.hidden = true;
    disconnect.innerHTML = `<button type="button" class="disconnect-btn" data-disconnect="${platform.id}">Disconnect ${escapeHtml(platform.display_name)}</button>`;
    wrap.appendChild(disconnect);

    const reapply = wireFieldVisibility(form, platform.credential_fields);
    reapply();
    visibilityReapplyByPlatform[platform.id] = reapply;

    return wrap;
}

function renderCredField(field) {
    if (field.type === 'Radio') {
        const group = document.createElement('div');
        group.className = 'cred-radio-group';
        (field.options || []).forEach((opt, i) => {
            const label = document.createElement('label');
            label.className = 'radio-label';
            label.innerHTML = `<input type="radio" name="${field.name}" value="${escapeHtml(opt.value)}" ${i === 0 ? 'checked' : ''}> ${escapeHtml(opt.label)}` +
                (opt.hint ? ` <span class="field-hint">${escapeHtml(opt.hint)}</span>` : '');
            group.appendChild(label);
        });
        return group;
    }

    const inputType = field.type === 'Password' ? 'password' : field.type === 'Url' ? 'url' : 'text';
    const label = document.createElement('label');
    label.innerHTML = `${escapeHtml(field.label)}` +
        (field.hint ? ` <span class="field-hint">(${escapeHtml(field.hint)})</span>` : '') +
        `<input type="${inputType}" name="${field.name}" ${field.required ? 'required' : ''}` +
        (field.placeholder ? ` placeholder="${escapeHtml(field.placeholder)}"` : '') +
        ` autocomplete="${inputType === 'password' ? 'new-password' : 'off'}">`;
    return label;
}

// A field with visibleWhen is only shown (and only required) while its
// controlling field currently equals that value - generalizes what used
// to be Instagram-specific applyInstagramModeVisibility/
// wireInstagramModeToggle to any field on any platform. Returns a reapply
// function so a later programmatic prefill (which doesn't fire 'change')
// can re-run the same logic.
function wireFieldVisibility(form, fields) {
    const dependents = fields.filter((f) => f.visibleWhen);

    function reapply() {
        for (const field of dependents) {
            const controller = form.elements[field.visibleWhen.field];
            const currentValue = controller instanceof RadioNodeList ? controller.value : controller?.value;
            const visible = currentValue === field.visibleWhen.value;
            const el = form.elements[field.name];
            const wrapperLabel = el?.closest('label');
            if (wrapperLabel) wrapperLabel.hidden = !visible;
            if (el) el.required = visible && field.required;
        }
    }

    for (const field of dependents) {
        const controller = form.elements[field.visibleWhen.field];
        if (!controller) continue;
        const inputs = controller instanceof RadioNodeList ? Array.from(controller) : [controller];
        for (const input of inputs) input.addEventListener('change', reapply);
    }

    return reapply;
}

// "We post here" - see the checkbox's own doc comment in renderPlatformAccordion.
function wireOnboardToggles() {
    for (const toggle of document.querySelectorAll('.onboard-toggle[data-platform]')) {
        toggle.addEventListener('change', async () => {
            toggle.disabled = true;
            await fetch(`/api/settings/credentials/${toggle.dataset.platform}/onboarded`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ onboarded: toggle.checked })
            });
            toggle.disabled = false;
        });
    }
}

// Same connect/disconnect shape for both - the two providers only differ
// in their API route prefix and which page "using it" happens on.
function wireOAuthConnectTemplate(prefix, apiBase) {
    const connectBtn = document.getElementById(`${prefix}-connect-btn`);
    const disconnectTool = document.getElementById(`${prefix}-disconnect-tool`);
    const disconnectBtn = document.getElementById(`${prefix}-disconnect-btn`);
    const setupNote = document.getElementById(`${prefix}-setup-note`);
    const connectedNote = document.getElementById(`${prefix}-connected-note`);
    if (!connectBtn) return;

    (async () => {
        const [configuredRes, statusRes] = await Promise.all([fetch(`${apiBase}/configured`), fetch(`${apiBase}/status`)]);
        const { configured } = configuredRes.ok ? await configuredRes.json() : { configured: false };
        const { connected } = statusRes.ok ? await statusRes.json() : { connected: false };

        setupNote.hidden = configured;
        connectBtn.hidden = !configured || connected;
        connectedNote.hidden = !connected;
        disconnectTool.hidden = !connected;
    })();

    const displayName = prefix.charAt(0).toUpperCase() + prefix.slice(1);
    connectBtn.addEventListener('click', () => { location.href = `${apiBase}/connect`; });
    disconnectBtn?.addEventListener('click', async () => {
        if (!confirm(`Disconnect ${displayName}? Saved credentials are cleared - reconnecting later means going through the connect flow again.`)) return;
        await fetch(`${apiBase}/disconnect`, { method: 'DELETE' });
        connectBtn.hidden = false;
        connectedNote.hidden = true;
        disconnectTool.hidden = true;
    });
}

function wireSpotifyConnect() { wireOAuthConnectTemplate('spotify', '/api/spotify'); }
function wireYouTubeConnect() { wireOAuthConnectTemplate('youtube', '/api/youtube'); }
function wireTikTokConnect() { wireOAuthConnectTemplate('tiktok', '/api/tiktok'); }

function wireInstructionsToggles() {
    for (const btn of document.querySelectorAll('.instructions-toggle')) {
        const panel = btn.nextElementSibling;
        btn.addEventListener('click', () => {
            panel.hidden = !panel.hidden;
            btn.querySelector('.chevron').innerHTML = panel.hidden ? '&#9656;' : '&#9662;';
        });
    }
}

// One button can cover more than one platform id (Facebook+Instagram
// share a single disconnect action, comma-separated in data-disconnect),
// since they're saved from the same manual form and re-connecting either
// means re-running that same setup anyway.
function wireDisconnectButtons() {
    for (const btn of document.querySelectorAll('.disconnect-btn[data-disconnect]')) {
        const platformIds = btn.dataset.disconnect.split(',');
        btn.addEventListener('click', async () => {
            const label = btn.textContent.replace(/^Disconnect /, '');
            if (!confirm(`Disconnect ${label}? Saved credentials are cleared - reconnecting later means re-entering them from scratch. Nothing already pushed to the live site is undone.`)) return;
            for (const id of platformIds) {
                await fetch(`/api/settings/credentials/${id}`, { method: 'DELETE' });
            }
            loadStatus();
        });
    }
}

// 'off' = no credentials saved, 'warn' = saved but the last connection
// check failed (or nothing has run yet to know either way isn't treated
// as warn - only a confirmed failure is), 'ok' = saved and last verified.
function badgeState(info) {
    if (!info?.configured) return 'off';
    if (info.verified === false) return 'warn';
    if (info?.expired || info?.expiringSoon) return 'warn';
    return 'ok';
}

// A verification failure already gets its own generic "Needs attention" -
// this is specifically for when the *only* problem is an expiring/expired
// token, where naming the actual date is more useful than a generic label.
function expiryOverrideText(info) {
    if (!info || info.verified === false) return null;
    if (info.expired) return `Token expired ${info.tokenExpiresAt}`;
    if (info.expiringSoon) return `Token expires ${info.tokenExpiresAt}`;
    return null;
}

function applyBadge(badge, state, textOverride) {
    badge.classList.toggle('configured', state === 'ok');
    badge.classList.toggle('not-configured', state === 'off');
    badge.classList.toggle('needs-attention', state === 'warn');
    badge.textContent = textOverride || (state === 'ok' ? 'Configured' : state === 'warn' ? 'Needs attention' : 'Not configured');
}

// Turns a verifyConnection()-shaped result ({ok, pageName, steps} or the
// {ok:false, error} short-circuit when the Page couldn't even be reached)
// into one readable sentence, reused everywhere a verification outcome is shown.
function formatVerificationMessage(v) {
    if (!v) return '';
    if (!v.steps) return v.error || 'unknown error';
    const parts = [];
    parts.push(v.steps.post.ok ? 'test post: ok' : `test post: failed (${v.steps.post.error})`);
    if (v.steps.cover.skipped) {
        parts.push(`cover photo: skipped (${v.steps.cover.skipped})`);
    } else {
        parts.push(v.steps.cover.ok ? 'cover photo: ok' : `cover photo: failed (${v.steps.cover.error})`);
    }
    return parts.join(', ');
}

async function loadStatus() {
    const res = await fetch('/api/settings/credentials');
    // No active band (or logged out) - loadPlatforms already showed a
    // message in that case, chained right before this via
    // loadPlatforms().then(loadStatus); nothing further to update here.
    if (!res.ok) return;
    const data = await res.json();

    for (const badge of document.querySelectorAll('.status-badge[data-platform]')) {
        const platform = badge.dataset.platform;
        applyBadge(badge, badgeState(data[platform]), expiryOverrideText(data[platform]));
    }

    for (const toggle of document.querySelectorAll('.onboard-toggle[data-platform]')) {
        toggle.checked = !!data[toggle.dataset.platform]?.onboarded;
    }

    // The modal's own top-of-modal status indicator - same badgeState
    // logic as everything else, just its own dedicated element (not
    // data-platform, so the loop above doesn't already touch it).
    const modalFbBadge = document.getElementById('meta-setup-status-facebook');
    if (modalFbBadge) applyBadge(modalFbBadge, badgeState(data.facebook), expiryOverrideText(data.facebook));

    const expiryInput = document.getElementById('meta-token-expiry');
    if (expiryInput) expiryInput.value = data.facebook?.tokenExpiresAt || '';

    // A visible warning outside the modal too, not just a quiet badge -
    // same "stays visible until fixed" treatment as the verification-error
    // banner below. Instagram's own expiry (only meaningful in standalone
    // mode - linked mode has no token of its own) surfaces through its
    // own row's badge instead, via the generic expiryOverrideText loop above.
    const metaExpiryWarning = document.getElementById('meta-token-expiry-warning');
    if (metaExpiryWarning) {
        const msg = expiryOverrideText(data.facebook);
        if (msg) {
            metaExpiryWarning.textContent = `${msg} - reconnect before it stops working.`;
            metaExpiryWarning.hidden = false;
            expandRow('meta');
        } else {
            metaExpiryWarning.hidden = true;
        }
    }

    const openModalBtn = document.getElementById('open-meta-setup-modal-btn');
    if (openModalBtn) {
        openModalBtn.textContent = badgeState(data.facebook) === 'ok' ? 'Manage Facebook setup' : 'Set up Facebook';
    }

    // Accordion rows: status shows entirely through the badge now (no more
    // dimming/opacity on the whole row - see settings.css) - this just
    // tracks which row is "broken" so it can be force-expanded below.
    for (const row of document.querySelectorAll('.platform-section[data-platform-row]')) {
        const rowId = row.dataset.platformRow;
        const state = badgeState(data[rowId === 'meta' ? 'facebook' : rowId]);
        row.classList.toggle('connected', state === 'ok');
        row.classList.toggle('needs-attention', state === 'warn');
    }

    // A platform's own disconnect action only makes sense once there's
    // something to disconnect.
    for (const tool of document.querySelectorAll('[data-disconnect-tool]')) {
        tool.hidden = !data[tool.dataset.disconnectTool]?.configured;
    }
    const metaDisconnect = document.getElementById('meta-disconnect-tool');
    if (metaDisconnect) metaDisconnect.hidden = !data.facebook?.configured;

    const coverTool = document.getElementById('cover-photo-tool');
    if (coverTool) {
        coverTool.hidden = !data.facebook?.configured;
        if (data.facebook?.configured) loadFlyerOptions();
    }

    const verifyTool = document.getElementById('verify-connection-tool');
    if (verifyTool) verifyTool.hidden = !data.facebook?.configured;

    const connectedPage = document.getElementById('meta-connected-page');
    const connectedPageName = document.getElementById('meta-connected-page-name');
    if (connectedPage && connectedPageName) {
        if (data.facebook?.configured && data.facebook.label) {
            connectedPageName.textContent = data.facebook.label;
            connectedPage.hidden = false;
        } else {
            connectedPage.hidden = true;
        }
    }

    // A confirmed-broken connection stays visible until it's fixed - don't
    // let it hide behind a collapsed row or a quiet badge.
    const metaError = document.getElementById('meta-verification-error');
    if (metaError) {
        if (data.facebook?.verified === false) {
            metaError.textContent = `Connection check failed: ${data.facebook.verification_error || 'unknown error'}`;
            metaError.hidden = false;
            expandRow('meta');
        } else {
            metaError.hidden = true;
        }
    }
}

// Used when something needs to be visible right away (a broken
// connection) rather than waiting for the user to click the row open.
function expandRow(rowId) {
    const row = document.querySelector(`.platform-section[data-platform-row="${rowId}"]`);
    if (!row) return;
    const body = row.querySelector('.platform-section-body');
    const chevron = row.querySelector('.section-toggle .chevron');
    body.hidden = false;
    if (chevron) chevron.innerHTML = '&#9662;';
}

// Saves one platform's credentials and returns the parsed response.
// Throws (with the server's error message) on a non-2xx response.
async function saveCredentials(platform, body) {
    const res = await fetch(`/api/settings/credentials/${platform}`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body)
    });
    const result = await res.json().catch(() => ({}));
    if (!res.ok) throw new Error(result.error || 'unknown error');
    return result;
}

// Formats a save outcome for one platform, reused by both the merged
// Meta form and (for consistency) anywhere else a verification result
// needs to be shown.
function describeSaveOutcome(label, result) {
    if (!result.verification) return `${label} saved.`;
    if (!result.verification.ok) {
        return `${label} saved, but the connection check failed: ${formatVerificationMessage(result.verification)}. Fix this before relying on it to post.`;
    }
    // Not every platform's check identifies a specific connected account
    // (pageName) - Website's just confirms the site's reachable and the
    // GitHub token/repo actually work, with nothing named to report back.
    return result.verification.pageName
        ? `${label} saved and verified - connected as "${result.verification.pageName}".`
        : `${label} saved and verified.`;
}

// Bandsintown's save can come back with siteUpdateError even on ok:true
// (credentials saved, but the push to bandsintown.js failed) - worth its
// own message rather than reading as a plain, unqualified success.
function describeBandsintownOutcome(result) {
    return result.siteUpdateError
        ? `Saved here, but couldn't update the site: ${result.siteUpdateError}`
        : 'Saved and pushed to the live site.';
}

// New in BandManager (no old-app equivalent - the old app only ever had
// one tenant, so there was never another band's credentials to reuse).
// For a BandAdmin of more than one band, offers to prefill a platform's
// form from whichever OTHER band(s) they've already set that platform up
// on - never a shared/linked row, just a one-time client-side prefill;
// saving still writes an independent Account under the active band.
async function wireReusePickers() {
    for (const form of document.querySelectorAll('.cred-form[data-platform]:not(#meta-manual-form)')) {
        const platform = form.dataset.platform;
        let options;
        try {
            const res = await fetch(`/api/settings/credentials/${platform}/reusable`);
            if (!res.ok) continue;
            options = await res.json();
        } catch {
            continue;
        }
        if (!options || options.length === 0) continue;

        const picker = document.createElement('label');
        picker.className = 'reuse-picker';
        picker.innerHTML = `
            Copy from another band you administer
            <select><option value="">Choose a band...</option>${options
                .map((o) => `<option value="${escapeHtml(o.bandId)}">${escapeHtml(o.bandName)}</option>`)
                .join('')}</select>
        `;
        const select = picker.querySelector('select');
        select.addEventListener('change', () => {
            const chosen = options.find((o) => o.bandId === select.value);
            if (!chosen) return;
            for (const [field, value] of Object.entries(chosen.values)) {
                if (form.elements[field]) form.elements[field].value = value;
            }
        });

        const heading = form.querySelector('h3');
        if (heading) heading.after(picker);
        else form.prepend(picker);
    }
}

function wireCredForms() {
    for (const form of document.querySelectorAll('.cred-form:not(#meta-manual-form):not(#test-mode-form)')) {
        form.addEventListener('submit', async (e) => {
            e.preventDefault();
            const platform = form.dataset.platform;
            const statusEl = form.querySelector('[data-cred-status]');
            const body = Object.fromEntries(new FormData(form).entries());

            try {
                const result = await saveCredentials(platform, body);
                // Not form.reset() - that would blank every field, including
                // the ones that aren't secrets (siteBaseUrl, githubOwner,
                // githubRepo, ...) and are actually useful to see confirmed
                // as saved. Re-pulling from the server (the same call the
                // page makes on load) shows exactly what's now on record,
                // including for fields this form doesn't even have.
                await loadSavedCredentialValues();
                if (statusEl) {
                    statusEl.textContent = platform === 'bandsintown' ? describeBandsintownOutcome(result) : describeSaveOutcome('Credentials', result);
                    statusEl.classList.toggle('error-text', (!!result.verification && !result.verification.ok) || !!result.siteUpdateError);
                }
            } catch (err) {
                alert(`Couldn't save: ${err.message}`);
                return;
            }

            loadStatus();
        });
    }
}

function wireMetaManualForm() {
    const metaManualForm = document.getElementById('meta-manual-form');
    if (!metaManualForm) return;
    metaManualForm.addEventListener('submit', async (e) => {
        e.preventDefault();
        const statusEl = metaManualForm.querySelector('[data-cred-status]');
        const { pageId, pageAccessToken } = Object.fromEntries(new FormData(metaManualForm).entries());
        const tokenExpiresAt = document.getElementById('meta-token-expiry')?.value || '';

        try {
            const fbResult = await saveCredentials('facebook', { pageId, pageAccessToken, tokenExpiresAt });
            metaManualForm.reset();
            if (statusEl) {
                statusEl.textContent = describeSaveOutcome('Facebook', fbResult);
                statusEl.classList.toggle('error-text', !!(fbResult.verification && !fbResult.verification.ok));
            }
        } catch (err) {
            if (statusEl) {
                statusEl.textContent = `Couldn't save: ${err.message}`;
                statusEl.classList.add('error-text');
            }
        }

        loadStatus();
    });
}

async function loadFlyerOptions() {
    const select = document.getElementById('cover-flyer-select');
    const res = await fetch('/api/facebook/cover/flyers');
    const flyers = await res.json();

    select.innerHTML = '';
    if (flyers.length === 0) {
        select.innerHTML = '<option value="">No upcoming flyers found</option>';
        return;
    }
    for (const flyer of flyers) {
        const opt = document.createElement('option');
        opt.value = flyer.flyerMain;
        opt.textContent = `${flyer.title} (${flyer.date})`;
        select.appendChild(opt);
    }
}

let lastPreviewFile = null;

function wireCoverPhotoTool() {
    const coverPreviewBtn = document.getElementById('cover-preview-btn');
    const coverPublishBtn = document.getElementById('cover-publish-btn');
    const coverPreviewBox = document.getElementById('cover-preview-box');
    const coverPreviewImg = document.getElementById('cover-preview-img');
    const coverStatus = document.getElementById('cover-status');

    if (coverPreviewBtn) {
        coverPreviewBtn.addEventListener('click', async () => {
            const flyerMain = document.getElementById('cover-flyer-select').value;
            if (!flyerMain) return;
            coverStatus.textContent = 'Generating preview...';
            const res = await fetch('/api/facebook/cover/preview', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ flyerMain })
            });
            const body = await res.json();
            if (!res.ok) {
                coverStatus.textContent = `Couldn't generate preview: ${body.error}`;
                return;
            }
            lastPreviewFile = body.fileName;
            coverPreviewImg.src = body.previewUrl + `?t=${Date.now()}`;
            coverPreviewBox.hidden = false;
            coverStatus.textContent = 'Preview generated - nothing has been posted to Facebook yet.';
        });
    }

    if (coverPublishBtn) {
        coverPublishBtn.addEventListener('click', async () => {
            if (!lastPreviewFile) return;
            if (!confirm('Set this as the live Facebook Page cover photo now?')) return;
            coverStatus.textContent = 'Setting cover photo on Facebook...';
            const res = await fetch('/api/facebook/cover/publish', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ fileName: lastPreviewFile })
            });
            const body = await res.json();
            coverStatus.textContent = res.ok
                ? 'Cover photo updated on Facebook.'
                : `Couldn't set cover photo: ${body.error}`;
        });
    }
}

function wireAutoSetupTool() {
    const autoSetupBtn = document.getElementById('auto-setup-btn');
    const autoStatus = document.getElementById('auto-setup-status');
    const autoPagePicker = document.getElementById('auto-page-picker');
    const autoPageSelect = document.getElementById('auto-page-select');
    if (!autoSetupBtn) return;

    autoSetupBtn.addEventListener('click', async () => {
        const appId = document.getElementById('auto-app-id').value.trim();
        const appSecret = document.getElementById('auto-app-secret').value.trim();
        const shortLivedToken = document.getElementById('auto-token').value.trim();
        if (!appId || !appSecret || !shortLivedToken) {
            autoStatus.textContent = 'Fill in all three fields first.';
            return;
        }

        const body = { appId, appSecret, shortLivedToken };
        if (!autoPagePicker.hidden && autoPageSelect.value) {
            body.pageId = autoPageSelect.value;
        }
        const tokenExpiresAt = document.getElementById('meta-token-expiry')?.value;
        if (tokenExpiresAt) body.tokenExpiresAt = tokenExpiresAt;

        autoStatus.textContent = 'Working...';
        autoSetupBtn.disabled = true;
        try {
            const res = await fetch('/api/facebook/auto-setup', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(body)
            });
            const result = await res.json();

            if (!res.ok) {
                autoStatus.textContent = `Couldn't finish setup: ${result.error}`;
                return;
            }

            if (result.needsPageChoice) {
                autoPageSelect.innerHTML = result.pages
                    .map((p) => `<option value="${p.id}">${p.name}</option>`)
                    .join('');
                autoPagePicker.hidden = false;
                autoSetupBtn.textContent = 'Confirm Page';
                autoStatus.textContent = 'This token manages more than one Page - pick which one, then click Confirm Page.';
                return;
            }

            const base = result.instagramLinked
                ? `Saved "${result.pageName}" for Facebook and found its linked Instagram account.`
                : `Saved "${result.pageName}" for Facebook. No linked Instagram account was found.`;

            autoStatus.textContent = result.verification?.ok
                ? `${base} Verified - the connection works.`
                : `${base} But the connection check failed: ${formatVerificationMessage(result.verification)}. Double-check the token's permissions (pages_manage_posts, pages_read_engagement) and try again.`;
            autoPagePicker.hidden = true;
            autoSetupBtn.textContent = 'Finish Setup';
            loadStatus();
        } finally {
            autoSetupBtn.disabled = false;
        }
    });
}

// The setup fields (auto + manual) live in one shared modal instead of
// always-open in the accordion body - opening it re-runs loadStatus() so
// its status indicators/expiry field always reflect what's actually saved,
// including a platform finished in a past visit (e.g. Facebook done
// already, coming back just to add Instagram).
function wireMetaSetupModal() {
    const openBtn = document.getElementById('open-meta-setup-modal-btn');
    const backdrop = document.getElementById('meta-setup-modal-backdrop');
    const closeBtn = document.getElementById('meta-setup-modal-close');
    if (!openBtn || !backdrop) return;

    openBtn.addEventListener('click', () => {
        backdrop.hidden = false;
        loadStatus();
    });
    closeBtn?.addEventListener('click', () => { backdrop.hidden = true; });
    backdrop.addEventListener('click', (e) => {
        if (e.target === backdrop) backdrop.hidden = true;
    });
}

function wireVerifyTool() {
    const verifyBtn = document.getElementById('verify-connection-btn');
    const verifyStatus = document.getElementById('verify-connection-status');
    if (!verifyBtn) return;

    verifyBtn.addEventListener('click', async () => {
        verifyStatus.textContent = 'Running sanity check... this posts and deletes a test post, then re-applies the cover photo.';
        verifyBtn.disabled = true;
        try {
            const res = await fetch('/api/facebook/verify-connection', { method: 'POST' });
            const result = await res.json();
            if (!res.ok) {
                verifyStatus.textContent = `Couldn't run sanity check: ${result.error}`;
                return;
            }
            verifyStatus.textContent = result.ok
                ? `Connected as "${result.pageName}" - ${formatVerificationMessage(result)}.`
                : result.pageName
                    ? `Sanity check failed for "${result.pageName}" - ${formatVerificationMessage(result)}.`
                    : `Sanity check failed - ${formatVerificationMessage(result)}.`;
            loadStatus();
        } catch (err) {
            verifyStatus.textContent = `Couldn't run sanity check: ${err.message}`;
        } finally {
            verifyBtn.disabled = false;
        }
    });
}

// Pre-fills the auto-setup tool's App ID/Secret from what was saved last
// time, so re-running setup (a fresh token, adding Instagram, a new Page)
// doesn't mean digging them up and retyping them again.
async function loadMetaAppConfig() {
    const appIdInput = document.getElementById('auto-app-id');
    const appSecretInput = document.getElementById('auto-app-secret');
    if (!appIdInput || !appSecretInput) return;

    const res = await fetch('/api/facebook/app-config');
    const config = await res.json();
    if (config.appId) appIdInput.value = config.appId;
    if (config.appSecret) appSecretInput.value = config.appSecret;
}

// Repopulates every credential form with whatever's already saved for
// that platform - Page ID, tokens, keys, everything except the
// short-lived token (never saved at all - it's a one-time-use value with
// nothing worth remembering). Runs once at page load, same as
// loadMetaAppConfig above; a value typed here isn't itself autocomplete
// (see the autocomplete="off"/"new-password" fields), it's this app's own
// record of what was last saved.
async function loadSavedCredentialValues() {
    const res = await fetch('/api/settings/credentials/values');
    const values = await res.json();

    const metaForm = document.getElementById('meta-manual-form');
    if (metaForm && values.facebook) {
        if (metaForm.pageId) metaForm.pageId.value = values.facebook.pageId || '';
        if (metaForm.pageAccessToken) metaForm.pageAccessToken.value = values.facebook.pageAccessToken || '';
    }

    // Everything else is one form per platform with data-platform set and
    // field names matching that platform's credential_fields exactly, so
    // this stays fully generic instead of hardcoding each platform by
    // hand. A field with visibleWhen (like Instagram's "mode" radio group)
    // needs its form's reapply function re-run after prefilling - setting
    // a RadioNodeList's .value checks the matching radio but doesn't fire
    // 'change' on its own.
    for (const platform of Object.values(platformsById)) {
        if (platform.id === 'facebook' || !platform.credential_fields || !values[platform.id]) continue;
        const form = document.querySelector(`form[data-platform="${platform.id}"]`);
        if (!form) continue;
        for (const [field, value] of Object.entries(values[platform.id])) {
            if (form.elements[field]) form.elements[field].value = value;
        }
        visibilityReapplyByPlatform[platform.id]?.();
    }
}

loadPlatforms().then(loadStatus);
initTestMode();

async function initTestMode() {
    const form = document.getElementById('test-mode-form');
    const stateLine = document.getElementById('test-mode-state-line');
    const toggleBtn = document.getElementById('test-mode-toggle-btn');
    const pathInput = document.getElementById('test-mode-path');
    const statusEl = document.getElementById('test-mode-status');

    // The button itself is the on/off control now (no checkbox) - its
    // label always names the action a click will take, not the current
    // state, so "enabled" only needs tracking to know which way to flip.
    let currentlyEnabled = false;
    function applyState(enabled) {
        currentlyEnabled = enabled;
        stateLine.hidden = !enabled;
        toggleBtn.textContent = enabled ? 'Disable Test Mode' : 'Enable Test Mode';
    }

    const res = await fetch('/api/settings/test-mode');
    const state = await res.json();
    pathInput.value = state.localRepoPath || '';
    applyState(state.enabled);

    form.addEventListener('submit', async (e) => {
        e.preventDefault();
        statusEl.textContent = 'Saving...';
        const body = { enabled: !currentlyEnabled, localRepoPath: pathInput.value.trim() };
        const saveRes = await fetch('/api/settings/test-mode', {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(body)
        });
        const result = await saveRes.json();
        if (!saveRes.ok) {
            statusEl.textContent = result.error || 'Could not save.';
            return;
        }
        applyState(result.enabled);
        // testModeBanner.js's own fetch only runs once, at page load - this
        // page just changed the setting itself, so update the banner here
        // directly instead of leaving it stuck showing the old state until
        // the next full navigation.
        window.setTestModeBanner?.(result.enabled);
        statusEl.textContent = result.enabled ? 'Test Mode is on.' : 'Test Mode is off.';
    });
}
