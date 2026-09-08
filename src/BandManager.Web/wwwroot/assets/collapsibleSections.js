// Makes every top-level <section class="platform-card"> on the page
// collapsible, default collapsed - opt-in per page via this script tag
// (only Profile and Band Admin > General use it, not every page that
// happens to use .platform-card elsewhere). Self-injecting like
// topNav.js/bandSwitcher.js - wraps each section's existing children
// (everything after its <h2>) in a hideable body, rather than requiring
// each page's HTML to be restructured by hand.
(function () {
    const style = document.createElement('style');
    style.textContent = `
        .platform-card > h2 { cursor: pointer; display: flex; align-items: center; gap: 8px; user-select: none; }
        .platform-card > h2:hover { color: #fff; }
        .section-collapse-chevron { display: inline-block; font-size: 0.7em; color: #999; transition: transform 0.15s; }
        .section-collapse-body { margin-top: 10px; }
    `;
    document.head.appendChild(style);

    function makeCollapsible(section) {
        const h2 = section.querySelector(':scope > h2');
        if (!h2 || section.dataset.collapsibleInit) return;
        section.dataset.collapsibleInit = '1';

        const body = document.createElement('div');
        body.className = 'section-collapse-body';
        for (const el of [...section.children].filter((el) => el !== h2)) body.appendChild(el);
        section.appendChild(body);

        const chevron = document.createElement('span');
        chevron.className = 'section-collapse-chevron';
        chevron.innerHTML = '&#9656;';
        h2.insertBefore(chevron, h2.firstChild);

        body.hidden = true;

        h2.addEventListener('click', () => {
            body.hidden = !body.hidden;
            chevron.innerHTML = body.hidden ? '&#9656;' : '&#9662;';
        });
    }

    document.querySelectorAll('main section.platform-card').forEach(makeCollapsible);
})();
