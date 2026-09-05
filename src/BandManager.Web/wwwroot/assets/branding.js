// Shared across every page (including login/setup, which is why the
// backing endpoint is public) - applies an admin-uploaded background image
// and/or logo if either has been set, and does nothing otherwise.
(async function applyBranding() {
    try {
        const res = await fetch('/api/profile/branding');
        if (!res.ok) return;
        const { logoUrl, backgroundUrl } = await res.json();

        if (backgroundUrl) {
            document.body.style.backgroundImage =
                `linear-gradient(rgba(0,0,0,0.6), rgba(0,0,0,0.6)), url('${backgroundUrl}')`;
            document.body.style.backgroundSize = 'cover';
            document.body.style.backgroundPosition = 'center';
            document.body.style.backgroundAttachment = 'fixed';
            document.body.style.backgroundRepeat = 'no-repeat';
        }

        if (logoUrl) {
            for (const slot of document.querySelectorAll('.brand-logo-slot')) {
                slot.innerHTML = `<img src="${logoUrl}" alt="Logo" class="brand-logo">`;
            }
        }
    } catch {
        // No branding set, or the request failed - just show the plain UI.
    }
})();
