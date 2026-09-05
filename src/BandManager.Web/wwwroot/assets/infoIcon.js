// Wires up `<span class="info-icon" data-help="...">i</span>` into a small
// circled-"i" button that toggles a prose popover on click. Self-contained
// (injects its own CSS, no stylesheet to remember) and picks up icons added
// later via a MutationObserver, so it works inside anything injected after
// page load too - a modal, a cloned <template>, an accordion body.
(function () {
    const style = document.createElement('style');
    style.textContent = `
        .info-icon {
            display: inline-flex;
            align-items: center;
            justify-content: center;
            width: 16px;
            height: 16px;
            border-radius: 50%;
            border: 1px solid #888;
            color: #aaa;
            font-size: 11px;
            font-style: italic;
            font-family: Georgia, serif;
            line-height: 1;
            cursor: pointer;
            position: relative;
            user-select: none;
            flex-shrink: 0;
        }
        .info-icon:hover, .info-icon.open { border-color: #ccc; color: #eee; }
        .info-popover {
            position: absolute;
            z-index: 50;
            top: calc(100% + 6px);
            left: 0;
            width: max-content;
            max-width: 320px;
            background: #222;
            border: 1px solid #555;
            border-radius: 6px;
            padding: 10px 12px;
            color: #ddd;
            font-size: 0.82rem;
            font-style: normal;
            font-weight: normal;
            font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif;
            line-height: 1.4;
            box-shadow: 0 4px 16px rgba(0, 0, 0, 0.4);
        }
        .info-popover[hidden] { display: none; }
    `;
    document.head.appendChild(style);

    let openIcon = null;
    let openPopover = null;

    function closeOpenPopover() {
        if (openPopover) openPopover.remove();
        if (openIcon) openIcon.classList.remove('open');
        openIcon = null;
        openPopover = null;
    }

    function wireIcon(icon) {
        if (icon.dataset.infoWired) return;
        icon.dataset.infoWired = 'true';
        // Not just a11y - a plain <span> has no accessible role, so tools
        // (and screen readers) can't target it as an interactive element.
        icon.setAttribute('role', 'button');
        icon.setAttribute('tabindex', '0');
        icon.setAttribute('aria-label', 'More info');
        icon.addEventListener('keydown', (e) => {
            if (e.key === 'Enter' || e.key === ' ') {
                e.preventDefault();
                icon.click();
            }
        });

        icon.addEventListener('click', (e) => {
            e.stopPropagation();
            const alreadyOpenForThis = openIcon === icon;
            closeOpenPopover();
            if (alreadyOpenForThis) return;

            const popover = document.createElement('div');
            popover.className = 'info-popover';
            popover.textContent = icon.dataset.help || '';
            icon.appendChild(popover);
            icon.classList.add('open');
            openIcon = icon;
            openPopover = popover;
        });
    }

    function init() {
        document.querySelectorAll('.info-icon').forEach(wireIcon);
    }

    document.addEventListener('click', closeOpenPopover);

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }

    new MutationObserver((mutations) => {
        for (const mutation of mutations) {
            for (const node of mutation.addedNodes) {
                if (node.nodeType !== Node.ELEMENT_NODE) continue;
                if (node.matches?.('.info-icon')) wireIcon(node);
                node.querySelectorAll?.('.info-icon').forEach(wireIcon);
            }
        }
    }).observe(document.body, { childList: true, subtree: true });
})();
