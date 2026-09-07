const params = new URLSearchParams(location.search);
const userId = params.get('userId');
const newEmail = params.get('newEmail');
const token = params.get('token');
const status = document.getElementById('status');
const backLink = document.getElementById('back-to-login');

function showStatus(message, isError) {
    status.textContent = message;
    status.classList.toggle('error-text', isError);
    backLink.hidden = false;
}

if (!userId || !newEmail || !token) {
    showStatus('This confirmation link is invalid or incomplete. Request the change again from your Profile page.', true);
} else {
    fetch('/api/auth/confirm-email-change', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ userId, newEmail, token })
    })
        .then(async (res) => {
            const body = await res.json();
            if (res.ok) {
                showStatus(`Confirmed - this account's email/username is now ${newEmail}. Log in with it below.`, false);
            } else {
                showStatus(body.error || 'Could not confirm this change.', true);
            }
        })
        .catch(() => showStatus('Could not reach the server. Try again in a moment.', true));
}
