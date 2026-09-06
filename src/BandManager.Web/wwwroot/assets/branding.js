// Shared across every page (including login/setup) - applies whichever
// branding is currently in effect: the active Band's own (background,
// logo, favicon) once one is selected, or the BandManager shell's own
// (set from the SuperAdmin page) when none is. A hard switch, not a
// per-field merge - once a Band is active its branding is what's shown,
// even for a field it hasn't set (matches "Band Branding... should be set
// for all pages" while a Band is selected, vs. the shell's own branding
// being explicitly for when one isn't).
(async function applyBranding() {
    try {
        const meRes = await fetch('/api/profile/me');
        const me = meRes.ok ? await meRes.json() : null;
        const hasBand = !!(me && me.activeBandRole);

        const res = await fetch(hasBand ? '/api/band-admin/branding' : '/api/profile/branding');
        if (!res.ok) return;
        const { logoUrl, backgroundUrl, faviconUrl } = await res.json();

        if (backgroundUrl) {
            // 0.6 wasn't dark enough for an arbitrary uploaded photo - most
            // of this app's own cards/sections are only lightly tinted
            // (assumed a plain dark body underneath, not a busy image), so
            // a bright/high-contrast background photo could genuinely
            // compete with and obscure text through them. 0.88 keeps the
            // image recognizable as a subtle decorative backdrop without
            // that risk, regardless of what gets uploaded.
            document.body.style.backgroundImage =
                `linear-gradient(rgba(0,0,0,0.88), rgba(0,0,0,0.88)), url('${backgroundUrl}')`;
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

        let iconLink = document.querySelector('link[rel="icon"]');
        if (faviconUrl) {
            if (!iconLink) {
                iconLink = document.createElement('link');
                iconLink.rel = 'icon';
                document.head.appendChild(iconLink);
            }
            iconLink.href = faviconUrl;
        } else if (iconLink) {
            iconLink.remove();
        }
    } catch {
        // No branding set, or the request failed - just show the plain UI.
    }
})();
