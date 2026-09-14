// Generic reusable modal window shell - resizable, maximizable, and
// minimizable to a small floating bubble that keeps whatever the caller
// put in .bodyEl alive (never torn down, just hidden) so scroll position/
// draft state survive a minimize, same behavior chat.js's own modal
// already has. See modalShell.css's own header comment for why this is a
// fresh component rather than a retrofit of chat.js.
window.ModalShell = (function () {
    function create({ title, storageKey, bubbleIcon }) {
        const backdropEl = document.createElement('div');
        backdropEl.className = 'modalshell-backdrop';
        backdropEl.hidden = true;

        const modalEl = document.createElement('div');
        modalEl.className = 'modalshell-modal';

        const titlebar = document.createElement('div');
        titlebar.className = 'modalshell-titlebar';
        titlebar.innerHTML = `
            <span class="modalshell-titlebar-label">${title}</span>
            <span class="modalshell-titlebar-spacer"></span>
            <button type="button" class="modalshell-min-btn" title="Minimize">–</button>
            <button type="button" class="modalshell-max-btn" title="Maximize">□</button>
            <button type="button" class="modalshell-restore-btn" title="Restore" hidden>❑</button>
            <button type="button" class="modalshell-close-btn" title="Close">&times;</button>
        `;
        modalEl.appendChild(titlebar);

        const bodyEl = document.createElement('div');
        bodyEl.className = 'modalshell-body';
        modalEl.appendChild(bodyEl);

        const resizeHandle = document.createElement('div');
        resizeHandle.className = 'modalshell-resize-handle';
        modalEl.appendChild(resizeHandle);

        backdropEl.appendChild(modalEl);
        document.body.appendChild(backdropEl);

        const bubbleEl = document.createElement('div');
        bubbleEl.className = 'modalshell-bubble';
        bubbleEl.title = title;
        bubbleEl.hidden = true;
        bubbleEl.textContent = bubbleIcon || '💬';
        document.body.appendChild(bubbleEl);

        let normalSize = { width: 480, height: 480 };
        try {
            const saved = JSON.parse(localStorage.getItem(storageKey) || 'null');
            if (saved && saved.width && saved.height) normalSize = saved;
        } catch { /* ignore */ }
        modalEl.style.width = normalSize.width + 'px';
        modalEl.style.height = normalSize.height + 'px';

        function openModal() {
            backdropEl.hidden = false;
            bubbleEl.hidden = true;
        }
        function minimize() {
            backdropEl.hidden = true;
            bubbleEl.hidden = false;
        }
        function closeModal() {
            backdropEl.hidden = true;
            bubbleEl.hidden = true;
        }
        function isOpen() { return !backdropEl.hidden; }

        titlebar.querySelector('.modalshell-min-btn').addEventListener('click', minimize);
        titlebar.querySelector('.modalshell-close-btn').addEventListener('click', closeModal);
        titlebar.querySelector('.modalshell-max-btn').addEventListener('click', () => {
            modalEl.classList.add('maximized');
            titlebar.querySelector('.modalshell-max-btn').hidden = true;
            titlebar.querySelector('.modalshell-restore-btn').hidden = false;
        });
        titlebar.querySelector('.modalshell-restore-btn').addEventListener('click', () => {
            modalEl.classList.remove('maximized');
            titlebar.querySelector('.modalshell-max-btn').hidden = false;
            titlebar.querySelector('.modalshell-restore-btn').hidden = true;
        });
        bubbleEl.addEventListener('click', openModal);

        resizeHandle.addEventListener('pointerdown', (e) => {
            e.preventDefault();
            const startX = e.clientX, startY = e.clientY;
            const startW = modalEl.offsetWidth, startH = modalEl.offsetHeight;
            function onMove(ev) {
                const w = Math.max(340, startW + (ev.clientX - startX));
                const h = Math.max(280, startH + (ev.clientY - startY));
                modalEl.style.width = w + 'px';
                modalEl.style.height = h + 'px';
                normalSize = { width: w, height: h };
            }
            function onUp() {
                document.removeEventListener('pointermove', onMove);
                document.removeEventListener('pointerup', onUp);
                try { localStorage.setItem(storageKey, JSON.stringify(normalSize)); } catch { /* private mode */ }
            }
            document.addEventListener('pointermove', onMove);
            document.addEventListener('pointerup', onUp);
        });

        return { backdropEl, modalEl, bodyEl, bubbleEl, open: openModal, minimize, close: closeModal, isOpen };
    }

    return { create };
})();
