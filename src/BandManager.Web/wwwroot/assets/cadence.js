const WEEKDAYS = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];
const KIND_LABELS = {
    recurring: 'Recurring',
    gig_countdown: 'Gig countdown',
    gig_event: 'Gig event (manual)',
    gig_cover_photo: 'Cover photo (next gig)'
};

let platforms = [];
let contentTypes = [];
let contentTypesByPlatform = {}; // platform.id -> content_types[], prefetched so the
                                  // dropdown never shows the full unfiltered list even
                                  // briefly (it used to, via a fetch-after-render race)
let rules = [];
// render() runs again after every add/edit/delete/active-toggle (loadAll
// rebuilds #cadence-board from scratch each time) - without remembering
// which sections were open, every action would snap every section back
// to collapsed, including the one you're actively working in.
const expandedPlatforms = new Set();

function escapeHtml(str) {
    return String(str).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
}

async function loadAll() {
    const [platformsRes, rulesRes, typesRes] = await Promise.all([
        fetch('/api/platforms'),
        fetch('/api/cadence-rules'),
        fetch('/api/content-types')
    ]);
    platforms = await platformsRes.json();
    rules = await rulesRes.json();
    contentTypes = await typesRes.json();

    const perPlatform = await Promise.all(
        platforms.map((p) => fetch(`/api/platforms/${p.id}/content-types`).then((r) => r.json()))
    );
    contentTypesByPlatform = Object.fromEntries(
        platforms.map((p, i) => [p.id, perPlatform[i].length > 0 ? perPlatform[i] : contentTypes])
    );

    render();
}

function scheduleSummary(rule) {
    if (rule.kind === 'gig_countdown') return 'Weekly, from booking until the show';
    if (rule.kind === 'gig_event') return 'Once per gig (manual)';
    if (rule.kind === 'gig_cover_photo') return 'Whenever the soonest upcoming gig changes';
    if (!rule.schedule_days) return '';
    if (rule.schedule_type === 'monthly') {
        return rule.schedule_days.map((d) => (d === 'end-of-month' ? 'End of month' : `Day ${d}`)).join(', ');
    }
    return rule.schedule_days.join(', ');
}

// One collapsible row per platform - same .platform-section/.section-toggle/
// .chevron/.platform-section-body classes as the main dashboard and the
// Setup and Registration page, reused as-is rather than a parallel layout.
function render() {
    const board = document.getElementById('cadence-board');
    board.innerHTML = '';

    const rulesByPlatform = new Map();
    for (const rule of rules) {
        if (!rulesByPlatform.has(rule.platform_id)) rulesByPlatform.set(rule.platform_id, []);
        rulesByPlatform.get(rule.platform_id).push(rule);
    }

    for (const platform of platforms) {
        const platformRules = rulesByPlatform.get(platform.id) || [];
        const expanded = expandedPlatforms.has(platform.id);

        const section = document.createElement('section');
        section.className = 'platform-section';
        section.dataset.platform = platform.id;

        const activeCount = platformRules.filter((r) => r.active).length;
        const summary = platformRules.length === 0
            ? 'No rules yet'
            : `${platformRules.length} rule${platformRules.length === 1 ? '' : 's'}${activeCount < platformRules.length ? ` · ${activeCount} active` : ''}`;

        const header = document.createElement('div');
        header.className = 'platform-section-header';

        const toggleBtn = document.createElement('button');
        toggleBtn.type = 'button';
        toggleBtn.className = 'section-toggle';
        toggleBtn.innerHTML = `
            <span class="chevron">${expanded ? '▾' : '▸'}</span>
            <span class="platform-section-title">${escapeHtml(platform.display_name)}</span>
            <span class="platform-section-summary">${summary}</span>
        `;
        header.appendChild(toggleBtn);

        const addBtn = document.createElement('button');
        addBtn.type = 'button';
        addBtn.className = 'add-rule-btn';
        addBtn.textContent = '+ Add rule';
        header.appendChild(addBtn);

        section.appendChild(header);

        const body = document.createElement('div');
        body.className = 'platform-section-body';
        body.hidden = !expanded;

        const list = document.createElement('div');
        list.className = 'cadence-rule-list';
        if (platformRules.length === 0) {
            list.innerHTML = '<p class="section-empty-note">No cadence rules yet.</p>';
        }
        for (const rule of platformRules) {
            list.appendChild(renderRuleRow(rule));
        }
        body.appendChild(list);

        section.appendChild(body);
        board.appendChild(section);

        toggleBtn.addEventListener('click', () => {
            const nowExpanded = body.hidden; // about to become open
            body.hidden = !nowExpanded;
            toggleBtn.querySelector('.chevron').textContent = nowExpanded ? '▾' : '▸';
            if (nowExpanded) expandedPlatforms.add(platform.id);
            else expandedPlatforms.delete(platform.id);
        });

        addBtn.addEventListener('click', () => {
            // Adding a rule implies wanting to see the section - open it
            // if it's collapsed, rather than inserting a form into a
            // hidden body where nothing visibly happens.
            if (body.hidden) {
                body.hidden = false;
                toggleBtn.querySelector('.chevron').textContent = '▾';
                expandedPlatforms.add(platform.id);
            }
            const existingForm = body.querySelector('.cadence-form');
            if (existingForm) { existingForm.remove(); return; }
            body.appendChild(renderRuleForm(platform, null));
        });
    }
}

function renderRuleRow(rule) {
    const row = document.createElement('div');
    row.className = 'cadence-rule-row' + (rule.active ? '' : ' inactive');
    row.innerHTML = `
        <div class="cadence-rule-main">
            <span class="cadence-kind-badge">${KIND_LABELS[rule.kind] || rule.kind}</span>
            <strong>${escapeHtml(rule.content_type_id)}</strong> - ${escapeHtml(rule.category)}
            <p class="cadence-rule-desc">${escapeHtml(rule.description)}</p>
            <p class="cadence-rule-schedule">${escapeHtml(scheduleSummary(rule))}${rule.owner ? ` &middot; ${escapeHtml(rule.owner)}` : ''}</p>
        </div>
        <div class="cadence-rule-actions">
            <label class="checkbox-label"><input type="checkbox" class="rule-active-toggle" ${rule.active ? 'checked' : ''}> Active</label>
            <button type="button" class="edit-rule-btn">Edit</button>
            <button type="button" class="remove-btn delete-rule-btn">Delete</button>
        </div>
    `;

    row.querySelector('.rule-active-toggle').addEventListener('change', async (e) => {
        await fetch(`/api/cadence-rules/${rule.id}`, {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ active: e.target.checked })
        });
        await loadAll();
    });

    row.querySelector('.delete-rule-btn').addEventListener('click', async () => {
        if (!confirm('Delete this cadence rule? Existing generated items are kept - only future generation stops.')) return;
        await fetch(`/api/cadence-rules/${rule.id}`, { method: 'DELETE' });
        await loadAll();
    });

    row.querySelector('.edit-rule-btn').addEventListener('click', () => {
        const platform = platforms.find((p) => p.id === rule.platform_id);
        const existingForm = row.parentElement.querySelector('.cadence-form');
        if (existingForm) existingForm.remove();
        const form = renderRuleForm(platform, rule);
        row.after(form);
    });

    return row;
}

function renderRuleForm(platform, rule) {
    const isEdit = !!rule;
    const form = document.createElement('form');
    form.className = 'cadence-form cred-form';

    // Prefetched (see loadAll) - no async gap where the full, unfiltered
    // content-type list would flash before narrowing down to what this
    // platform actually supports.
    const platformTypes = contentTypesByPlatform[platform.id] || contentTypes;
    const kindOptions = [
        '<option value="recurring">Recurring</option>',
        '<option value="gig_countdown">Gig countdown (weekly until the show)</option>',
        '<option value="gig_event">Gig event (manual, one-time per show)</option>'
    ];
    // Cover photo rules only ever make sense for Facebook (they drive the
    // Graph API's cover-photo call directly - see scheduler.js/api.js) -
    // kept out of every other platform's Kind dropdown entirely, rather
    // than offering it and rejecting it server-side after the fact.
    if (platform.display_name === 'Facebook') {
        kindOptions.push('<option value="gig_cover_photo">Cover photo (next gig)</option>');
    }
    form.innerHTML = `
        <label>Kind
            <select name="kind" ${isEdit ? 'disabled' : ''}>
                ${kindOptions.join('')}
            </select>
        </label>
        <label>Content type
            <select name="contentTypeId" ${isEdit ? 'disabled' : ''}>
                ${platformTypes.map((t) => `<option value="${escapeHtml(t.id)}">${escapeHtml(t.id)}</option>`).join('')}
            </select>
        </label>
        <label>Category <input type="text" name="category" maxlength="200" required></label>
        <label>Description (what to make) <input type="text" name="description" maxlength="1000" required></label>
        <label>Owner <input type="text" name="owner" maxlength="100" placeholder="e.g. Bobby/Rich"></label>
        <div class="cadence-schedule-fields"></div>
        <button type="submit">${isEdit ? 'Save' : 'Add rule'}</button>
        <button type="button" class="cancel-btn">Cancel</button>
        <p class="save-note cadence-form-status"></p>
    `;

    const scheduleFields = form.querySelector('.cadence-schedule-fields');
    const kindSelect = form.querySelector('[name="kind"]');
    const contentTypeSelect = form.querySelector('[name="contentTypeId"]');

    function renderScheduleFields(kind) {
        if (kind === 'recurring') {
            scheduleFields.innerHTML = `
                <label>Schedule type
                    <select name="scheduleType">
                        <option value="weekly">Weekly</option>
                        <option value="monthly">Monthly</option>
                    </select>
                </label>
                <div class="cadence-weekly-days">
                    ${WEEKDAYS.map((d) => `<label class="checkbox-label"><input type="checkbox" name="day" value="${d}"> ${d}</label>`).join('')}
                </div>
                <div class="cadence-monthly-days" hidden>
                    <label>Day of month (1-31) <input type="number" name="monthDay" min="1" max="31"></label>
                    <label class="checkbox-label"><input type="checkbox" name="endOfMonth"> Or: end of month</label>
                </div>
            `;
            const scheduleTypeSelect = scheduleFields.querySelector('[name="scheduleType"]');
            const weeklyBox = scheduleFields.querySelector('.cadence-weekly-days');
            const monthlyBox = scheduleFields.querySelector('.cadence-monthly-days');
            scheduleTypeSelect.addEventListener('change', () => {
                weeklyBox.hidden = scheduleTypeSelect.value !== 'weekly';
                monthlyBox.hidden = scheduleTypeSelect.value !== 'monthly';
            });
        } else if (kind === 'gig_countdown') {
            scheduleFields.innerHTML = `
                <label>Suggested caption (each week - {event_url} is replaced with the Event link once it exists)
                    <textarea name="defaultTemplate" rows="2" maxlength="1000"></textarea>
                </label>
            `;
        } else if (kind === 'gig_event') {
            scheduleFields.innerHTML = `
                <label>Manual instructions (shown when marking this done)
                    <textarea name="manualInstructions" rows="3" maxlength="2000"></textarea>
                </label>
            `;
        } else if (kind === 'gig_cover_photo') {
            scheduleFields.innerHTML = `
                <p class="cadence-rule-schedule">Generates automatically whenever the soonest upcoming gig changes - no day/date to configure. The cover image is auto-built from that gig's flyer the moment one exists.</p>
                <label>Manual instructions (shown if Facebook isn't connected)
                    <textarea name="manualInstructions" rows="3" maxlength="2000"></textarea>
                </label>
            `;
        }
    }

    kindSelect.addEventListener('change', () => renderScheduleFields(kindSelect.value));
    renderScheduleFields(kindSelect.value);
    if (rule) contentTypeSelect.value = rule.content_type_id;

    if (rule) {
        kindSelect.value = rule.kind;
        renderScheduleFields(rule.kind);
        form.category.value = rule.category;
        form.description.value = rule.description;
        form.owner.value = rule.owner || '';
        if (rule.kind === 'recurring' && rule.schedule_days) {
            form.scheduleType.value = rule.schedule_type;
            form.scheduleType.dispatchEvent(new Event('change'));
            if (rule.schedule_type === 'weekly') {
                for (const cb of form.querySelectorAll('[name="day"]')) {
                    cb.checked = rule.schedule_days.includes(cb.value);
                }
            } else {
                const eom = rule.schedule_days.find((d) => d === 'end-of-month');
                if (eom) form.endOfMonth.checked = true;
                else form.monthDay.value = rule.schedule_days[0] || '';
            }
        } else if (rule.kind === 'gig_countdown' && rule.message_templates) {
            form.defaultTemplate.value = rule.message_templates.default || '';
        } else if (rule.kind === 'gig_event' || rule.kind === 'gig_cover_photo') {
            form.manualInstructions.value = rule.manual_instructions || '';
        }
    }

    form.querySelector('.cancel-btn').addEventListener('click', () => form.remove());

    form.addEventListener('submit', async (e) => {
        e.preventDefault();
        const statusEl = form.querySelector('.cadence-form-status');
        const kind = kindSelect.value;

        const body = {
            kind,
            accountId: platform.account_id,
            contentTypeId: contentTypeSelect.value,
            category: form.category.value,
            description: form.description.value,
            owner: form.owner.value
        };

        if (kind === 'recurring') {
            body.scheduleType = form.scheduleType.value;
            body.scheduleDays = body.scheduleType === 'weekly'
                ? Array.from(form.querySelectorAll('[name="day"]:checked')).map((cb) => cb.value)
                : (form.endOfMonth.checked ? ['end-of-month'] : [Number(form.monthDay.value)]);
        } else if (kind === 'gig_countdown') {
            body.messageTemplates = { default: form.defaultTemplate.value || form.description.value };
        } else if (kind === 'gig_event' || kind === 'gig_cover_photo') {
            body.manualInstructions = form.manualInstructions.value;
        }

        const url = isEdit ? `/api/cadence-rules/${rule.id}` : '/api/cadence-rules';
        const res = await fetch(url, {
            method: isEdit ? 'PUT' : 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(body)
        });
        const result = await res.json();
        if (!res.ok) {
            statusEl.textContent = result.error || 'Could not save rule.';
            return;
        }
        await loadAll();
    });

    return form;
}

loadAll();
