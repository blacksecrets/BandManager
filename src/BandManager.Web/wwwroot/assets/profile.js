async function loadMe() {
    const res = await fetch('/api/profile/me');
    const me = await res.json();
    const roleNote = me.isSuperAdmin ? ' (SuperAdmin)' : me.activeBandRole === 'BandAdmin' ? ` (Band Admin of ${me.activeBandName || 'this band'})` : '';
    document.getElementById('whoami').textContent = `Logged in as ${me.username}${roleNote}`;
    document.querySelector('#username-form input[name="username"]').value = me.username || '';

    // Shows whether the login itself just landed here for that reason
    // (?mustChangePassword=1, from AuthController.Login) or a direct
    // navigation back to a still-unresolved one (me.mustChangePassword) -
    // either way, same banner.
    const stillPending = me.mustChangePassword;
    const justArrived = new URLSearchParams(location.search).get('mustChangePassword') === '1';
    document.getElementById('must-change-password-banner').hidden = !(stillPending || justArrived);
}

// --- Username (also the account's email - see UpdateUsername's doc comment) ---
document.getElementById('username-form').addEventListener('submit', async (e) => {
    e.preventDefault();
    const form = e.target;
    const status = document.getElementById('username-status');

    const res = await fetch('/api/profile/username', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ username: form.username.value.trim() })
    });
    const body = await res.json();
    status.textContent = res.ok ? 'Username saved.' : (body.error || 'Could not save username.');
    if (res.ok) loadMe();
});

// --- Password change ---
document.getElementById('password-form').addEventListener('submit', async (e) => {
    e.preventDefault();
    const form = e.target;
    const status = document.getElementById('password-status');
    const currentPassword = form.currentPassword.value;
    const newPassword = form.newPassword.value;
    const confirmPassword = form.confirmPassword.value;

    if (newPassword !== confirmPassword) {
        status.textContent = "New password and confirmation don't match.";
        return;
    }

    const res = await fetch('/api/profile/password', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ currentPassword, newPassword })
    });
    const body = await res.json();
    if (res.ok) {
        status.textContent = 'Password changed.';
        form.reset();
        document.getElementById('must-change-password-banner').hidden = true;
    } else {
        status.textContent = body.error || 'Could not change password.';
    }
});

loadMe();
