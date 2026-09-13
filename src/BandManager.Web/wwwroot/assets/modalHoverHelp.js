// Adds hover-help text to every icon-only "x" modal-close button
// (.detail-modal-close) app-wide - self-injecting like
// collapsibleSections.js, loaded once via topNav.js so every page that
// already has the shared nav shell gets this for free, no per-page markup
// needed. A MutationObserver (not just a one-time pass) is required here,
// unlike collapsibleSections.js's static Profile/Band Admin sections -
// most of these close buttons are built at runtime inside a template
// string when a modal first opens (flyerEditor.js, addGigModal.js,
// assignee.js, songReview.js, stagePlotEditor.js, ...), so they don't
// exist in the DOM yet at page load.
(function () {
    function labelFor(btn) {
        // Best-effort: name what the button closes, using the modal's own
        // heading when there is one, so hovering the X says "Close Edit
        // Gig" instead of a bare, context-free "Close" on every modal.
        // Starts the climb from the button's PARENT, not the button
        // itself - the button's own class is "detail-modal-close", which
        // [class*="modal"] would otherwise match immediately via
        // closest()'s self-inclusive search, stopping one level too soon.
        const modal = btn.parentElement?.closest('.detail-modal, [class*="modal"]');
        const heading = modal?.querySelector('h2, h3')?.textContent?.trim();
        return heading ? `Close ${heading}` : 'Close';
    }

    function apply(root) {
        const buttons = root.matches?.('.detail-modal-close')
            ? [root]
            : [...(root.querySelectorAll?.('.detail-modal-close') || [])];
        for (const btn of buttons) {
            if (!btn.title) btn.title = labelFor(btn);
        }
    }

    apply(document);

    new MutationObserver((mutations) => {
        for (const m of mutations) {
            for (const node of m.addedNodes) {
                if (node.nodeType === 1) apply(node);
            }
        }
    }).observe(document.body, { childList: true, subtree: true });
})();
