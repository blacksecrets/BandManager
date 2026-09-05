// Adds a show/hide eyeball button to every password field on the page.
// Self-contained (injects its own CSS) so any page just needs one
// <script> tag - no matching stylesheet link to remember.
(function () {
    const EYE_OPEN = '<svg viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8Z"/><circle cx="12" cy="12" r="3"/></svg>';
    const EYE_CLOSED = '<svg viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M17.94 17.94A10.94 10.94 0 0 1 12 20c-7 0-11-8-11-8a20.3 20.3 0 0 1 5.06-6.06M9.9 4.24A10.94 10.94 0 0 1 12 4c7 0 11 8 11 8a20.3 20.3 0 0 1-3.22 4.44M1 1l22 22"/><path d="M14.12 14.12a3 3 0 1 1-4.24-4.24"/></svg>';

    // Every page this runs on already has its own "button" styling (red
    // fill, bold text, margins...) that would otherwise leak in here via
    // higher-specificity selectors like ".auth-box button" - reset
    // everything first, then apply only what this control needs.
    const style = document.createElement('style');
    style.textContent = `
        .password-field { position: relative; display: flex; }
        .password-field input { width: 100%; box-sizing: border-box; padding-right: 36px; }
        button.password-toggle-btn {
            all: unset;
            position: absolute;
            right: 6px;
            top: 50%;
            transform: translateY(-50%);
            box-sizing: border-box;
            padding: 4px;
            cursor: pointer;
            color: #999;
            display: flex;
            align-items: center;
            justify-content: center;
        }
        button.password-toggle-btn:hover { color: #eee; }
        button.password-toggle-btn svg { display: block; }
    `;
    document.head.appendChild(style);

    function addToggle(input) {
        if (input.dataset.toggleAdded) return;
        input.dataset.toggleAdded = 'true';

        const wrapper = document.createElement('div');
        wrapper.className = 'password-field';
        input.parentNode.insertBefore(wrapper, input);
        wrapper.appendChild(input);

        const btn = document.createElement('button');
        btn.type = 'button';
        btn.className = 'password-toggle-btn';
        btn.setAttribute('aria-label', 'Show password');
        btn.tabIndex = -1; // don't disrupt the natural tab order between password fields
        btn.innerHTML = EYE_OPEN;
        wrapper.appendChild(btn);

        btn.addEventListener('click', () => {
            const revealing = input.type === 'password';
            input.type = revealing ? 'text' : 'password';
            btn.innerHTML = revealing ? EYE_CLOSED : EYE_OPEN;
            btn.setAttribute('aria-label', revealing ? 'Hide password' : 'Show password');
        });
    }

    function init() {
        document.querySelectorAll('input[type="password"]').forEach(addToggle);
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }

    // Some pages (settings.html's accordion) clone <template> content in
    // after this initial scan already ran, once an async fetch resolves -
    // without this, a password field that only exists after that point
    // (e.g. a Page Access Token field inside a just-expanded platform row)
    // would never get a toggle button at all. addToggle is idempotent
    // (dataset.toggleAdded guard above), so this can't double-process
    // anything the initial scan already handled.
    new MutationObserver((mutations) => {
        for (const mutation of mutations) {
            for (const node of mutation.addedNodes) {
                if (node.nodeType !== Node.ELEMENT_NODE) continue;
                if (node.matches?.('input[type="password"]')) addToggle(node);
                node.querySelectorAll?.('input[type="password"]').forEach(addToggle);
            }
        }
    }).observe(document.body, { childList: true, subtree: true });
})();
