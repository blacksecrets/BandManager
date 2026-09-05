const params = new URLSearchParams(location.search);
const userId = params.get('userId');
const token = params.get('token');
const form = document.getElementById('reset-password-form');
const status = document.getElementById('status');

function showStatus(message, isError) {
    status.textContent = message;
    status.classList.toggle('error-text', isError);
    status.hidden = false;
}

if (!userId || !token) {
    showStatus('This reset link is invalid or incomplete. Request a new one from the login page.', true);
    form.querySelectorAll('input, button').forEach((el) => (el.disabled = true));
} else {
    form.addEventListener('submit', async (e) => {
        e.preventDefault();
        if (form.newPassword.value !== form.confirmPassword.value) {
            showStatus("New password and confirmation don't match.", true);
            return;
        }

        const res = await fetch('/api/auth/reset-password', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ userId, token, newPassword: form.newPassword.value })
        });
        const body = await res.json();
        if (res.ok) {
            showStatus('Password reset. You can log in with your new password now.', false);
            form.querySelectorAll('input, button').forEach((el) => (el.disabled = true));
        } else {
            showStatus(body.error || 'Could not reset password.', true);
        }
    });
}
