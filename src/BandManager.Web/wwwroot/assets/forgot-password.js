document.getElementById('forgot-password-form').addEventListener('submit', async (e) => {
    e.preventDefault();
    const form = e.target;
    const status = document.getElementById('status');
    const button = form.querySelector('button[type="submit"]');

    button.disabled = true;
    await fetch('/api/auth/forgot-password', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ username: form.username.value.trim() })
    });

    // Same message regardless of what the server actually did - the API
    // itself never reveals whether the account/email exists, so the UI
    // can't either.
    status.textContent = "If that account exists and has an email on file, a reset link is on its way.";
    status.classList.remove('error-text');
    status.hidden = false;
    form.querySelector('input[name="username"]').disabled = true;
    button.remove();
});
