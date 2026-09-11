function escapeHtml(str) {
    const div = document.createElement('div');
    div.textContent = str ?? '';
    return div.innerHTML;
}

const params = new URLSearchParams(location.search);
const actId = params.get('actId');

function describeGear(g) {
    const dims = [g.lengthInches, g.widthInches, g.depthInches].filter((n) => n != null);
    const parts = [];
    if (dims.length) parts.push(`${dims.join('x')}in`);
    if (g.weightPounds != null) parts.push(`${g.weightPounds}lb`);
    return parts.join(', ');
}

function render(data) {
    const sheet = document.getElementById('print-tech-rider-sheet');
    let html = `<h1>${escapeHtml(data.bandName)} Tech Rider</h1>`;
    html += `<p class="print-tech-rider-meta">${escapeHtml(data.actName)}</p>`;

    if (data.introText) html += `<p class="print-tech-rider-overview">${escapeHtml(data.introText)}</p>`;

    if (data.hasStagePlot) {
        html += `
            <h2>Stage Plot</h2>
            <img class="print-tech-rider-stage-plot" src="${data.stagePlotImageUrl}" alt="Stage plot">
            <ol class="print-tech-rider-legend">
                ${data.gearLegend.map((g) => `<li value="${g.visibleId}">${escapeHtml(g.type)}${g.make || g.model ? ' - ' + escapeHtml([g.make, g.model].filter(Boolean).join(' ')) : ''}${describeGear(g) ? ` - ${escapeHtml(describeGear(g))}` : ''}</li>`).join('')}
            </ol>
        `;
    }

    if (data.inputChannels.length > 0) {
        html += `
            <h2>Input / Mic Splitter Channel List</h2>
            <table class="print-tech-rider-table">
                <thead><tr><th>#</th><th>Source</th><th>Mic</th><th>Provided By</th><th>Positioning Notes</th></tr></thead>
                <tbody>
                    ${data.inputChannels.map((c) => `
                        <tr>
                            <td>${c.channelNumber}</td>
                            <td>${escapeHtml(c.source)}</td>
                            <td>${escapeHtml(c.micRecommendation || '')}</td>
                            <td>${escapeHtml(c.providedBy || '')}</td>
                            <td>${escapeHtml(c.positioningNotes || '')}</td>
                        </tr>
                    `).join('')}
                </tbody>
            </table>
        `;
    }

    if (data.videoNotes) html += `<h2>Video Note</h2><p>${escapeHtml(data.videoNotes)}</p>`;

    if (data.micEqNotes.length > 0) {
        html += '<h2>FOH EQ Notes</h2>';
        for (const note of data.micEqNotes) {
            html += `<h3>${escapeHtml(note.micModel)}${note.context ? ' - ' + escapeHtml(note.context) : ''}</h3>`;
            if (note.frequencyRows.length > 0) {
                html += `
                    <table class="print-tech-rider-table">
                        <thead><tr><th>Frequency</th><th>EQ Move</th><th>Reason</th></tr></thead>
                        <tbody>
                            ${note.frequencyRows.map((r) => `
                                <tr><td>${escapeHtml(r.frequency)}</td><td>${escapeHtml(r.eqMove || '')}</td><td>${escapeHtml(r.reason || '')}</td></tr>
                            `).join('')}
                        </tbody>
                    </table>
                `;
            }
            if (note.generalNotes) html += `<p>${escapeHtml(note.generalNotes)}</p>`;
        }
    }

    if (data.monitorMixes.length > 0) {
        html += `
            <h2>Monitor Mix</h2>
            <table class="print-tech-rider-table">
                <thead><tr><th>Position</th><th>Mix</th></tr></thead>
                <tbody>
                    ${data.monitorMixes.map((m) => `<tr><td>${escapeHtml(m.position)}</td><td>${escapeHtml(m.mixDescription)}</td></tr>`).join('')}
                </tbody>
            </table>
        `;
    }

    if (data.generalNotes) html += `<h2>General Notes</h2><p>${escapeHtml(data.generalNotes)}</p>`;

    const contactLines = [data.techContactName, data.techContactPhone, data.techContactEmail].filter(Boolean);
    if (contactLines.length > 0) {
        html += `<h2>Contact Information</h2><p>${contactLines.map(escapeHtml).join('<br>')}</p>`;
    }

    sheet.innerHTML = html;
}

async function init() {
    const sheet = document.getElementById('print-tech-rider-sheet');
    if (!actId) { sheet.textContent = 'No Act specified.'; return; }

    const res = await fetch(`/api/acts/${actId}/tech-rider`);
    if (!res.ok) { sheet.textContent = 'Could not load this Tech Rider.'; return; }
    render(await res.json());
}

document.getElementById('print-now-btn').addEventListener('click', () => window.print());

init();
