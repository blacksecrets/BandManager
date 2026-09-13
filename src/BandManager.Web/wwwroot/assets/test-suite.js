// SuperAdmin's Test Suite page - the manual QA checklist (TestSuiteController)
// plus the automated regression runner (RegressionTestsController).
function escapeHtmlTestSuite(str) {
    const div = document.createElement('div');
    div.textContent = str == null ? '' : String(str);
    return div.innerHTML;
}

const STATUS_LABELS = { NotTested: 'Not Tested', Pass: 'Pass', Fail: 'Fail', Blocked: 'Blocked' };
let testCasesCache = [];

async function initTestSuite() {
    const me = await fetch('/api/profile/me').then((r) => r.json());
    if (!me.isSuperAdmin) {
        document.getElementById('test-suite-denied').hidden = false;
        return;
    }
    document.getElementById('test-suite-content').hidden = false;
    await loadTestCases();
}

async function loadTestCases() {
    const res = await fetch('/api/test-suite/cases');
    testCasesCache = res.ok ? await res.json() : [];
    renderTestCases();
}

function renderSummary() {
    const counts = { NotTested: 0, Pass: 0, Fail: 0, Blocked: 0 };
    for (const t of testCasesCache) counts[t.status] = (counts[t.status] || 0) + 1;
    document.getElementById('test-suite-summary').textContent =
        `${testCasesCache.length} total - ${counts.Pass} passed, ${counts.Fail} failed, ${counts.Blocked} blocked, ${counts.NotTested} not tested`;
}

function renderTestCases() {
    renderSummary();
    const container = document.getElementById('test-case-areas');
    const byArea = new Map();
    for (const t of testCasesCache) {
        if (!byArea.has(t.area)) byArea.set(t.area, []);
        byArea.get(t.area).push(t);
    }

    container.innerHTML = '';
    for (const [area, cases] of byArea) {
        const passed = cases.filter((c) => c.status === 'Pass').length;
        const details = document.createElement('details');
        details.className = 'test-case-area';

        const summary = document.createElement('summary');
        summary.innerHTML = `<span>${escapeHtmlTestSuite(area)}</span><span class="test-case-area-tally">${passed}/${cases.length} passed</span>`;
        details.appendChild(summary);

        const body = document.createElement('div');
        body.className = 'test-case-area-body';
        for (const tc of cases) body.appendChild(renderTestCaseCard(tc));
        details.appendChild(body);

        container.appendChild(details);
    }
}

function renderTestCaseCard(tc) {
    const card = document.createElement('div');
    card.className = `test-case-card status-${tc.status.toLowerCase()}`;
    card.dataset.key = tc.key;

    const lastTested = tc.lastTestedAt ? new Date(tc.lastTestedAt).toLocaleString() : '';

    card.innerHTML = `
        <div class="test-case-title">${escapeHtmlTestSuite(tc.title)}</div>
        <div class="test-case-field"><strong>Steps</strong>${escapeHtmlTestSuite(tc.steps)}</div>
        <div class="test-case-field"><strong>Expected result</strong>${escapeHtmlTestSuite(tc.expectedResult)}</div>
        <div class="test-case-status-row">
            ${Object.keys(STATUS_LABELS).map((s) => `<button type="button" class="test-case-status-btn${tc.status === s ? ' active' : ''}" data-status="${s}">${STATUS_LABELS[s]}</button>`).join('')}
            <span class="test-case-last-tested">${lastTested ? `Last tested ${escapeHtmlTestSuite(lastTested)}` : ''}</span>
        </div>
        <textarea class="test-case-notes" placeholder="Notes - bugs, usability friction, anything worth flagging...">${escapeHtmlTestSuite(tc.notes || '')}</textarea>
    `;

    for (const btn of card.querySelectorAll('.test-case-status-btn')) {
        btn.addEventListener('click', () => saveTestCaseResult(tc.key, btn.dataset.status, card.querySelector('.test-case-notes').value));
    }
    const notesEl = card.querySelector('.test-case-notes');
    notesEl.addEventListener('blur', () => {
        const currentStatus = card.querySelector('.test-case-status-btn.active')?.dataset.status || 'NotTested';
        saveTestCaseResult(tc.key, currentStatus, notesEl.value);
    });

    return card;
}

async function saveTestCaseResult(key, status, notes) {
    const res = await fetch(`/api/test-suite/cases/${encodeURIComponent(key)}/result`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ status, notes })
    });
    if (!res.ok) return;
    const updated = await res.json();
    const idx = testCasesCache.findIndex((t) => t.key === key);
    if (idx >= 0) testCasesCache[idx] = updated;

    // Re-render just this card's status/tally state rather than the whole
    // list, so an in-progress Notes edit elsewhere on the page isn't lost.
    const card = document.querySelector(`.test-case-card[data-key="${CSS.escape(key)}"]`);
    if (card) {
        card.className = `test-case-card status-${updated.status.toLowerCase()}`;
        for (const btn of card.querySelectorAll('.test-case-status-btn')) {
            btn.classList.toggle('active', btn.dataset.status === updated.status);
        }
        const lastTestedEl = card.querySelector('.test-case-last-tested');
        lastTestedEl.textContent = updated.lastTestedAt ? `Last tested ${new Date(updated.lastTestedAt).toLocaleString()}` : '';
    }
    renderSummary();
    const areaTally = card?.closest('.test-case-area')?.querySelector('.test-case-area-tally');
    if (areaTally) {
        const areaCases = testCasesCache.filter((t) => t.area === testCasesCache.find((x) => x.key === key).area);
        const passed = areaCases.filter((t) => t.status === 'Pass').length;
        areaTally.textContent = `${passed}/${areaCases.length} passed`;
    }
}

document.getElementById('test-suite-reset-btn').addEventListener('click', async () => {
    if (!confirm('Reset every test case back to Not Tested? Notes will be cleared too.')) return;
    await fetch('/api/test-suite/cases/reset-all', { method: 'POST' });
    await loadTestCases();
});

// --- Automated regression tests ---

document.getElementById('run-regression-btn').addEventListener('click', async () => {
    const btn = document.getElementById('run-regression-btn');
    const runningNote = document.getElementById('regression-running-note');
    const summary = document.getElementById('regression-summary');
    const resultsEl = document.getElementById('regression-results');
    btn.disabled = true;
    runningNote.hidden = false;
    summary.textContent = '';
    resultsEl.innerHTML = '';

    try {
        const res = await fetch('/api/regression-tests/run', { method: 'POST' });
        const results = res.ok ? await res.json() : [];
        const passed = results.filter((r) => r.passed).length;
        summary.textContent = `${passed}/${results.length} checks passed.`;
        resultsEl.innerHTML = results.map((r) => `
            <div class="regression-result-row ${r.passed ? 'pass' : 'fail'}">
                <span class="regression-result-icon">${r.passed ? '✓' : '✗'}</span>
                <div>
                    <div class="regression-result-name">${escapeHtmlTestSuite(r.name)}</div>
                    <div class="regression-result-message">${escapeHtmlTestSuite(r.message)}</div>
                </div>
                <span class="regression-result-duration">${r.durationMs}ms</span>
            </div>
        `).join('');
    } finally {
        btn.disabled = false;
        runningNote.hidden = true;
    }
});

initTestSuite();
