// Thin reminder banner shown at the top of every authenticated page while
// Test Mode is on - self-contained so any page just needs one <script>
// tag (same pattern as password-toggle.js/branding.js). Relies on
// .test-mode-banner already being defined in dashboard.css, which every
// page that includes this script also links.
//
// Exposed on window so a page that changes Test Mode itself (the toggle
// button on the settings page) can update the banner immediately instead
// of it only catching up on the next full page load.
function setTestModeBanner(enabled) {
    const existing = document.querySelector('.test-mode-banner');
    if (enabled) {
        if (existing) return;
        const banner = document.createElement('div');
        banner.className = 'test-mode-banner';
        banner.textContent = 'TEST MODE';
        document.body.insertBefore(banner, document.body.firstChild);
    } else if (existing) {
        existing.remove();
    }
}
window.setTestModeBanner = setTestModeBanner;

fetch('/api/settings/test-mode').then((r) => r.json()).then((state) => {
    setTestModeBanner(state.enabled);
});
