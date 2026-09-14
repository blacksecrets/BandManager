// Band Chat - modal, multi-tab (Full Band + SideChats) real-time chat.
// Self-contained: topNav.js injects this script tag + chat.css on every
// authenticated page (same self-injecting convention as modalHoverHelp.js)
// and calls window.ChatWidget.init(me) once it's loaded. This file loads
// the vendored SignalR client itself before connecting, builds its own
// modal/bubble DOM (appended to <body>, independent of whatever page it's
// opened from), and owns its own state end to end.
window.ChatWidget = (function () {
    let me = null;
    let bandMembersCache = [];
    let openThreads = [];
    let activeThreadId = null;
    const messagesByThread = new Map();
    const readStateByThread = new Map();
    let connection = null;
    const joinedThreadIds = new Set();
    let typingTimers = new Map(); // threadId -> {timeoutId, names: Set}
    let pendingMentions = new Map(); // threadId -> Map(userId -> name), cleared per-thread on send
    let pendingFile = null;
    let normalSize = { width: 480, height: 620 };

    function escapeHtml(str) {
        const div = document.createElement('div');
        div.textContent = str == null ? '' : String(str);
        return div.innerHTML;
    }

    function loadScript(src) {
        return new Promise((resolve, reject) => {
            if (document.querySelector(`script[src="${src}"]`)) { resolve(); return; }
            const s = document.createElement('script');
            s.src = src;
            s.onload = () => resolve();
            s.onerror = () => reject(new Error(`Failed to load ${src}`));
            document.head.appendChild(s);
        });
    }

    // --- Modal DOM ---
    let backdropEl, modalEl, bubbleEl, bubbleBadgeEl, tabbarEl, messagesEl, typingLineEl, seenLineEl,
        composeTextEl, mentionMenuEl, addMemberRowEl, bottombarEl;

    function buildDom() {
        backdropEl = document.createElement('div');
        backdropEl.className = 'chat-modal-backdrop';
        backdropEl.hidden = true;

        modalEl = document.createElement('div');
        modalEl.className = 'chat-modal';
        modalEl.style.width = normalSize.width + 'px';
        modalEl.style.height = normalSize.height + 'px';

        const titlebar = document.createElement('div');
        titlebar.className = 'chat-titlebar';
        titlebar.innerHTML = `
            <span class="chat-titlebar-label">Band Chat</span>
            <span class="chat-titlebar-spacer"></span>
            <button type="button" class="chat-min-btn" title="Minimize">–</button>
            <button type="button" class="chat-max-btn" title="Maximize">□</button>
            <button type="button" class="chat-restore-btn" title="Restore" hidden>❑</button>
            <button type="button" class="chat-close-btn" title="Close">&times;</button>
        `;
        modalEl.appendChild(titlebar);

        tabbarEl = document.createElement('div');
        tabbarEl.className = 'chat-tabbar';
        modalEl.appendChild(tabbarEl);

        const body = document.createElement('div');
        body.className = 'chat-body';

        messagesEl = document.createElement('div');
        messagesEl.className = 'chat-messages';
        body.appendChild(messagesEl);

        typingLineEl = document.createElement('div');
        typingLineEl.className = 'chat-typing-line';
        body.appendChild(typingLineEl);

        seenLineEl = document.createElement('div');
        seenLineEl.className = 'chat-seen-line';
        body.appendChild(seenLineEl);

        const compose = document.createElement('div');
        compose.className = 'chat-compose';
        compose.innerHTML = `
            <button type="button" class="chat-attach-btn" title="Attach a file">📎</button>
            <input type="file" class="chat-file-input" hidden accept="image/*,.pdf,.doc,.docx,.txt">
            <textarea class="chat-compose-text" rows="1" placeholder="Message..." maxlength="4000"></textarea>
            <button type="button" class="chat-emoji-btn" title="Emoji">😊</button>
            <button type="button" class="chat-send-btn">Send</button>
        `;
        mentionMenuEl = document.createElement('div');
        mentionMenuEl.className = 'chat-mention-menu';
        mentionMenuEl.hidden = true;
        compose.appendChild(mentionMenuEl);
        body.appendChild(compose);
        composeTextEl = compose.querySelector('.chat-compose-text');

        addMemberRowEl = document.createElement('div');
        addMemberRowEl.className = 'chat-addmember-row';
        addMemberRowEl.hidden = true;
        addMemberRowEl.innerHTML = `<button type="button" class="chat-addmember-btn">+ Add Member</button>`;
        body.appendChild(addMemberRowEl);

        modalEl.appendChild(body);

        bottombarEl = document.createElement('div');
        bottombarEl.className = 'chat-bottombar';
        bottombarEl.innerHTML = `
            <button type="button" class="chat-newchat-btn">+</button>
            <button type="button" class="chat-closedchats-btn">Closed chats</button>
        `;
        modalEl.appendChild(bottombarEl);

        const resizeHandle = document.createElement('div');
        resizeHandle.className = 'chat-resize-handle';
        modalEl.appendChild(resizeHandle);

        backdropEl.appendChild(modalEl);
        document.body.appendChild(backdropEl);

        bubbleEl = document.createElement('div');
        bubbleEl.className = 'chat-bubble-minimized';
        bubbleEl.title = 'Band Chat';
        bubbleEl.hidden = true;
        bubbleEl.innerHTML = `💬<span class="chat-bubble-minimized-badge" hidden></span>`;
        bubbleBadgeEl = bubbleEl.querySelector('.chat-bubble-minimized-badge');
        document.body.appendChild(bubbleEl);

        wireChrome(titlebar, resizeHandle);
        wireTabbar();
        wireCompose(compose);
        wireBottombar();

        bubbleEl.addEventListener('click', () => { bubbleEl.hidden = true; backdropEl.hidden = false; });
    }

    // --- Window chrome: minimize (collapse to bubble, state stays alive
    // since the modal DOM is only hidden, never torn down - scroll
    // position and draft text survive for free), maximize/restore, close,
    // corner-drag resize. ---
    function wireChrome(titlebar, resizeHandle) {
        titlebar.querySelector('.chat-min-btn').addEventListener('click', () => {
            backdropEl.hidden = true;
            bubbleEl.hidden = false;
        });
        titlebar.querySelector('.chat-close-btn').addEventListener('click', () => {
            backdropEl.hidden = true;
            bubbleEl.hidden = true;
        });
        titlebar.querySelector('.chat-max-btn').addEventListener('click', () => {
            modalEl.classList.add('maximized');
            titlebar.querySelector('.chat-max-btn').hidden = true;
            titlebar.querySelector('.chat-restore-btn').hidden = false;
        });
        titlebar.querySelector('.chat-restore-btn').addEventListener('click', () => {
            modalEl.classList.remove('maximized');
            titlebar.querySelector('.chat-max-btn').hidden = false;
            titlebar.querySelector('.chat-restore-btn').hidden = true;
        });

        resizeHandle.addEventListener('pointerdown', (e) => {
            e.preventDefault();
            const startX = e.clientX, startY = e.clientY;
            const startW = modalEl.offsetWidth, startH = modalEl.offsetHeight;
            function onMove(ev) {
                const w = Math.max(340, startW + (ev.clientX - startX));
                const h = Math.max(320, startH + (ev.clientY - startY));
                modalEl.style.width = w + 'px';
                modalEl.style.height = h + 'px';
                normalSize = { width: w, height: h };
            }
            function onUp() {
                document.removeEventListener('pointermove', onMove);
                document.removeEventListener('pointerup', onUp);
                try { localStorage.setItem('chatModalSize', JSON.stringify(normalSize)); } catch { /* private mode */ }
            }
            document.addEventListener('pointermove', onMove);
            document.addEventListener('pointerup', onUp);
        });

        try {
            const saved = JSON.parse(localStorage.getItem('chatModalSize') || 'null');
            if (saved && saved.width && saved.height) {
                normalSize = saved;
                modalEl.style.width = saved.width + 'px';
                modalEl.style.height = saved.height + 'px';
            }
        } catch { /* ignore */ }
    }

    // --- Popup menu helper (tab-add / closed-chats / reactions) ---
    let openPopup = null;
    function showPopup(anchorEl, buildItems) {
        closePopup();
        const menu = document.createElement('div');
        menu.className = 'chat-popup-menu';
        const rect = anchorEl.getBoundingClientRect();
        menu.style.left = rect.left + 'px';
        menu.style.top = (rect.top - 6) + 'px';
        menu.style.transform = 'translateY(-100%)';
        buildItems(menu);
        document.body.appendChild(menu);
        openPopup = menu;
        setTimeout(() => document.addEventListener('click', closePopupOnOutsideClick), 0);
    }
    function closePopupOnOutsideClick(e) {
        if (openPopup && !openPopup.contains(e.target)) closePopup();
    }
    function closePopup() {
        if (openPopup) { openPopup.remove(); openPopup = null; }
        document.removeEventListener('click', closePopupOnOutsideClick);
    }

    // --- Tab bar ---
    function wireTabbar() {
        tabbarEl.addEventListener('click', async (e) => {
            const closeBtn = e.target.closest('.chat-tab-close');
            if (closeBtn) {
                e.stopPropagation();
                const threadId = closeBtn.closest('.chat-tab').dataset.threadId;
                const wasActive = threadId === activeThreadId;
                await fetch(`/api/chat/threads/${threadId}/close`, { method: 'POST' });
                if (wasActive) activeThreadId = null; // let loadOpenThreads pick the first remaining tab (Full Band)
                await loadOpenThreads();
                if (wasActive) await selectThread(activeThreadId);
                return;
            }
            const tab = e.target.closest('.chat-tab');
            if (tab) selectThread(tab.dataset.threadId);
        });
    }

    function renderTabbar() {
        tabbarEl.innerHTML = openThreads.map((t) => `
            <div class="chat-tab${t.id === activeThreadId ? ' active' : ''}" data-thread-id="${t.id}">
                <span class="chat-tab-label">${escapeHtml(t.displayName)}</span>
                ${t.unreadCount > 0 && t.id !== activeThreadId ? `<span class="chat-tab-unread">${t.unreadCount > 99 ? '99+' : t.unreadCount}</span>` : ''}
                ${t.type !== 'BandWide' ? '<button type="button" class="chat-tab-close" title="Close">&times;</button>' : ''}
            </div>
        `).join('');
    }

    async function loadOpenThreads() {
        const res = await fetch('/api/chat/threads');
        openThreads = res.ok ? await res.json() : [];
        for (const t of openThreads) {
            if (!joinedThreadIds.has(t.id)) await joinThreadGroup(t.id);
        }
        if (!activeThreadId && openThreads.length > 0) activeThreadId = openThreads[0].id;
        renderTabbar();
        updateBottombarVisibility();
        renderAddMemberVisibility();
        updateBadges();
    }

    async function selectThread(threadId) {
        activeThreadId = threadId;
        renderTabbar();
        renderAddMemberVisibility();
        typingLineEl.textContent = '';
        if (!messagesByThread.has(threadId)) {
            messagesEl.innerHTML = '<p class="save-note">Loading...</p>';
            const msgs = await fetch(`/api/chat/threads/${threadId}/messages`).then((r) => (r.ok ? r.json() : []));
            messagesByThread.set(threadId, msgs);
        }
        renderMessages();
        await loadReadState(threadId);
        markThreadRead(threadId);
    }

    function renderAddMemberVisibility() {
        const thread = openThreads.find((t) => t.id === activeThreadId);
        addMemberRowEl.hidden = !thread || thread.type === 'BandWide';
    }

    function updateBottombarVisibility() {
        // Bottom bar (new chat / closed chats) is always visible while a
        // band is active - nothing to toggle here today, kept as its own
        // function so future per-thread bottom controls have a home.
    }

    // --- New SideChat / Add Member: shared member-picker sub-modal ---
    function openMemberPicker({ title, excludeIds, confirmLabel, onConfirm }) {
        const backdrop = document.createElement('div');
        backdrop.className = 'chat-submodal-backdrop';
        const picked = new Set();
        const eligible = bandMembersCache.filter((m) => !excludeIds.includes(m.id));
        backdrop.innerHTML = `
            <div class="chat-submodal">
                <h3>${escapeHtml(title)}</h3>
                <div class="chat-submodal-members">
                    ${eligible.map((m) => `
                        <label class="chat-submodal-member">
                            <input type="checkbox" value="${m.id}">
                            ${escapeHtml(m.firstName)}
                        </label>
                    `).join('') || '<p class="save-note" style="padding:10px;">No other members to add.</p>'}
                </div>
                <div class="chat-submodal-buttons">
                    <button type="button" class="chat-cancel-btn">Cancel</button>
                    <button type="button" class="chat-primary-btn">${escapeHtml(confirmLabel)}</button>
                </div>
            </div>
        `;
        document.body.appendChild(backdrop);
        backdrop.addEventListener('click', (e) => { if (e.target === backdrop) backdrop.remove(); });
        backdrop.querySelector('.chat-cancel-btn').addEventListener('click', () => backdrop.remove());
        backdrop.querySelectorAll('input[type="checkbox"]').forEach((cb) => {
            cb.addEventListener('change', () => { if (cb.checked) picked.add(cb.value); else picked.delete(cb.value); });
        });
        backdrop.querySelector('.chat-primary-btn').addEventListener('click', async () => {
            if (picked.size === 0) return;
            await onConfirm([...picked]);
            backdrop.remove();
        });
    }

    function openRenamePrompt(thread) {
        const backdrop = document.createElement('div');
        backdrop.className = 'chat-submodal-backdrop';
        backdrop.innerHTML = `
            <div class="chat-submodal">
                <h3>Rename chat</h3>
                <input type="text" class="chat-rename-input" maxlength="100" value="${escapeHtml(thread.name || thread.displayName)}">
                <div class="chat-submodal-buttons">
                    <button type="button" class="chat-cancel-btn">Cancel</button>
                    <button type="button" class="chat-primary-btn">Save</button>
                </div>
            </div>
        `;
        document.body.appendChild(backdrop);
        backdrop.addEventListener('click', (e) => { if (e.target === backdrop) backdrop.remove(); });
        backdrop.querySelector('.chat-cancel-btn').addEventListener('click', () => backdrop.remove());
        backdrop.querySelector('.chat-primary-btn').addEventListener('click', async () => {
            const name = backdrop.querySelector('.chat-rename-input').value.trim();
            if (!name) return;
            await fetch(`/api/chat/threads/${thread.id}/name`, {
                method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ name })
            });
            backdrop.remove();
            await loadOpenThreads();
        });
    }

    function wireBottombar() {
        bottombarEl.querySelector('.chat-newchat-btn').addEventListener('click', (e) => {
            showPopup(e.currentTarget, (menu) => {
                const fullBandBtn = document.createElement('button');
                fullBandBtn.type = 'button';
                fullBandBtn.className = 'chat-popup-menu-item';
                fullBandBtn.textContent = 'Full Band';
                fullBandBtn.addEventListener('click', () => {
                    closePopup();
                    const bw = openThreads.find((t) => t.type === 'BandWide');
                    if (bw) selectThread(bw.id);
                });
                menu.appendChild(fullBandBtn);

                const sideChatBtn = document.createElement('button');
                sideChatBtn.type = 'button';
                sideChatBtn.className = 'chat-popup-menu-item';
                sideChatBtn.textContent = 'New SideChat...';
                sideChatBtn.addEventListener('click', () => {
                    closePopup();
                    openMemberPicker({
                        title: 'Start a SideChat',
                        excludeIds: [me.id],
                        confirmLabel: 'Start Chat',
                        onConfirm: async (userIds) => {
                            const res = await fetch('/api/chat/threads', {
                                method: 'POST', headers: { 'Content-Type': 'application/json' },
                                body: JSON.stringify({ memberUserIds: userIds })
                            });
                            if (res.ok) {
                                const thread = await res.json();
                                await loadOpenThreads();
                                await selectThread(thread.id);
                            }
                        }
                    });
                });
                menu.appendChild(sideChatBtn);
            });
        });

        bottombarEl.querySelector('.chat-closedchats-btn').addEventListener('click', async (e) => {
            // Capture currentTarget before the await - the DOM nulls it out
            // once event dispatch finishes, which happens as soon as this
            // handler yields at the await, not when the async function
            // itself completes. Using e.currentTarget after that point
            // silently passed null into showPopup, which threw inside
            // getBoundingClientRect() with no popup ever appearing.
            const anchor = e.currentTarget;
            const res = await fetch('/api/chat/threads/closed');
            const closed = res.ok ? await res.json() : [];
            showPopup(anchor, (menu) => {
                if (closed.length === 0) {
                    const empty = document.createElement('div');
                    empty.className = 'chat-popup-menu-empty';
                    empty.textContent = 'No closed chats.';
                    menu.appendChild(empty);
                    return;
                }
                for (const t of closed) {
                    const btn = document.createElement('button');
                    btn.type = 'button';
                    btn.className = 'chat-popup-menu-item';
                    btn.innerHTML = `${escapeHtml(t.displayName)}<span class="save-note">${escapeHtml(t.members.map((m) => m.name).join(', '))}</span>`;
                    btn.addEventListener('click', async () => {
                        closePopup();
                        await fetch(`/api/chat/threads/${t.id}/reopen`, { method: 'POST' });
                        await loadOpenThreads();
                        await selectThread(t.id);
                    });
                    menu.appendChild(btn);
                }
            });
        });

        addMemberRowEl.querySelector('.chat-addmember-btn').addEventListener('click', () => {
            const thread = openThreads.find((t) => t.id === activeThreadId);
            if (!thread) return;
            openMemberPicker({
                title: `Add to "${thread.displayName}"`,
                excludeIds: thread.members.map((m) => m.userId),
                confirmLabel: 'Add',
                onConfirm: async (userIds) => {
                    for (const uid of userIds) {
                        await fetch(`/api/chat/threads/${thread.id}/members`, {
                            method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ userId: uid })
                        });
                    }
                    await loadOpenThreads();
                }
            });
        });

        tabbarEl.addEventListener('dblclick', (e) => {
            const tab = e.target.closest('.chat-tab');
            if (!tab) return;
            const thread = openThreads.find((t) => t.id === tab.dataset.threadId);
            if (thread && thread.type !== 'BandWide') openRenamePrompt(thread);
        });
    }

    // --- Messages ---
    function memberName(userId) {
        if (userId === me.id) return me.firstName || 'You';
        const m = bandMembersCache.find((x) => x.id === userId);
        return m ? m.firstName : 'Someone';
    }
    function memberAvatarUrl(userId) {
        const m = bandMembersCache.find((x) => x.id === userId);
        return m ? m.avatarUrl : null;
    }

    function highlightMentions(text, mentionedUserIds) {
        let html = escapeHtml(text);
        for (const uid of mentionedUserIds || []) {
            const name = memberName(uid);
            const escaped = escapeHtml(`@${name}`);
            html = html.split(escaped).join(`<span class="mention">${escaped}</span>`);
        }
        return html;
    }

    function formatDateSep(d) {
        const today = new Date(); today.setHours(0, 0, 0, 0);
        const yesterday = new Date(today); yesterday.setDate(yesterday.getDate() - 1);
        const day = new Date(d); day.setHours(0, 0, 0, 0);
        if (day.getTime() === today.getTime()) return 'Today';
        if (day.getTime() === yesterday.getTime()) return 'Yesterday';
        return day.toLocaleDateString(undefined, { month: 'short', day: 'numeric', year: day.getFullYear() !== today.getFullYear() ? 'numeric' : undefined });
    }

    function renderMessages() {
        const msgs = messagesByThread.get(activeThreadId) || [];
        messagesEl.innerHTML = '';

        const loadOlderBtn = document.createElement('button');
        loadOlderBtn.type = 'button';
        loadOlderBtn.className = 'chat-load-older';
        loadOlderBtn.textContent = 'Load older messages';
        loadOlderBtn.addEventListener('click', () => loadOlderMessages());
        if (msgs.length >= 50) messagesEl.appendChild(loadOlderBtn);

        let lastDateKey = null, lastSenderId = null;
        for (const m of msgs) {
            const dateKey = new Date(m.createdAt).toDateString();
            if (dateKey !== lastDateKey) {
                const sep = document.createElement('div');
                sep.className = 'chat-date-sep';
                sep.textContent = formatDateSep(m.createdAt);
                messagesEl.appendChild(sep);
                lastDateKey = dateKey;
                lastSenderId = null;
            }
            messagesEl.appendChild(renderMessageRow(m, m.senderId === lastSenderId));
            lastSenderId = m.senderId;
        }
        messagesEl.scrollTop = messagesEl.scrollHeight;
        renderSeenLine();
    }

    function renderMessageRow(m, grouped) {
        const row = document.createElement('div');
        const isOwn = m.senderId === me.id;
        row.className = 'chat-row' + (isOwn ? ' own' : '') + (grouped ? ' grouped' : '');
        row.dataset.messageId = m.id;

        const avatarWrap = document.createElement('div');
        avatarWrap.className = 'chat-row-avatar';
        avatarWrap.innerHTML = window.UserAvatar ? window.UserAvatar.render({ id: m.senderId, name: m.senderName, avatarUrl: m.senderAvatarUrl }, 26) : '';
        row.appendChild(avatarWrap);

        const bodyWrap = document.createElement('div');
        bodyWrap.className = 'chat-row-body';

        if (!grouped && !isOwn) {
            const nameEl = document.createElement('div');
            nameEl.className = 'chat-sender-name';
            nameEl.textContent = m.senderName;
            bodyWrap.appendChild(nameEl);
        }

        const bubble = document.createElement('div');
        bubble.className = 'chat-bubble' + (m.isDeleted ? ' deleted' : '');
        if (m.isDeleted) {
            bubble.textContent = 'Message deleted';
        } else {
            if (m.text) bubble.innerHTML = highlightMentions(m.text, m.mentionedUserIds);
            for (const a of m.attachments || []) {
                if (a.isImage) {
                    const img = document.createElement('img');
                    img.className = 'chat-attachment-img';
                    img.src = a.url;
                    img.alt = a.originalFileName || 'Image';
                    img.addEventListener('click', () => window.open(a.url, '_blank'));
                    img.addEventListener('contextmenu', (e) => onImageContextMenu(e, a));
                    bubble.appendChild(img);
                } else {
                    const link = document.createElement('a');
                    link.className = 'chat-attachment-file';
                    link.href = a.url;
                    link.target = '_blank';
                    link.rel = 'noopener';
                    link.textContent = `📄 ${a.originalFileName || 'File'}`;
                    bubble.appendChild(link);
                }
            }
        }
        bodyWrap.appendChild(bubble);

        if (!m.isDeleted) {
            const reactionsEl = document.createElement('div');
            reactionsEl.className = 'chat-reactions';
            const counts = new Map();
            for (const r of m.reactions || []) counts.set(r.emoji, (counts.get(r.emoji) || 0) + 1);
            const myReaction = (m.reactions || []).find((r) => r.userId === me.id);
            for (const [emoji, count] of counts) {
                const pill = document.createElement('button');
                pill.type = 'button';
                pill.className = 'chat-reaction-pill' + (myReaction && myReaction.emoji === emoji ? ' mine' : '');
                pill.textContent = `${emoji} ${count}`;
                pill.addEventListener('click', () => toggleReaction(m.id, emoji, myReaction && myReaction.emoji === emoji));
                reactionsEl.appendChild(pill);
            }
            const reactBtn = document.createElement('button');
            reactBtn.type = 'button';
            reactBtn.className = 'chat-react-trigger';
            reactBtn.textContent = '+react';
            reactBtn.addEventListener('click', (e) => openReactionPicker(e.currentTarget, m.id));
            reactionsEl.appendChild(reactBtn);
            bodyWrap.appendChild(reactionsEl);

            const meta = document.createElement('div');
            meta.className = 'chat-row-meta';
            const time = new Date(m.createdAt).toLocaleTimeString(undefined, { hour: 'numeric', minute: '2-digit' });
            meta.innerHTML = `<span>${escapeHtml(time)}</span>`;
            if (isOwn) {
                const delBtn = document.createElement('button');
                delBtn.type = 'button';
                delBtn.className = 'chat-msg-delete';
                delBtn.textContent = 'Delete';
                delBtn.addEventListener('click', () => deleteMessage(m.id));
                meta.appendChild(delBtn);
            }
            bodyWrap.appendChild(meta);
        }

        row.appendChild(bodyWrap);
        return row;
    }

    // Shared emoji picker, modeled after Google Messages: a quick-access
    // row of the same handful of reaction emoji it favors, plus a
    // categorized grid underneath for everything else - one component
    // used both for reactions (openReactionPicker) and for inserting an
    // emoji into the compose box before sending (wireCompose's emoji
    // button), rather than two separate hardcoded emoji lists.
    const EMOJI_QUICK = ['👍', '❤️', '😂', '😮', '😢', '🙏'];
    const EMOJI_CATEGORIES = [
        { label: '🙂', emojis: ['😀', '😁', '😂', '🤣', '😊', '😍', '😘', '😜', '🤔', '😐', '🙄', '😴', '😭', '😡', '😱', '🥳', '😇', '😅', '🤗', '🤩', '😉', '🥰', '😬', '🤯'] },
        { label: '👍', emojis: ['👍', '👎', '👏', '🙌', '🙏', '👋', '✌️', '🤞', '💪', '🤝', '👌', '🤙', '💯', '🔥', '✨', '🎉'] },
        { label: '❤️', emojis: ['❤️', '🧡', '💛', '💚', '💙', '💜', '🖤', '🤍', '💔', '💕', '💖', '💗'] },
        { label: '🎵', emojis: ['🎵', '🎶', '🎸', '🥁', '🎤', '🎧', '🎹', '🎷'] },
        { label: '🍕', emojis: ['🍕', '🍔', '🌮', '🍺', '🍻', '☕', '🍰', '🎂'] },
        { label: '⭐', emojis: ['⭐', '✅', '❌', '❓', '❗', '💡', '⏰', '📅', '📍', '🚗', '🎯', '💰'] }
    ];

    function openEmojiPicker(anchorEl, onPick) {
        closePopup();
        const menu = document.createElement('div');
        menu.className = 'chat-emoji-picker';
        const rect = anchorEl.getBoundingClientRect();
        menu.style.left = Math.max(4, Math.min(rect.left, window.innerWidth - 260)) + 'px';
        menu.style.top = (rect.top - 6) + 'px';
        menu.style.transform = 'translateY(-100%)';

        let activeCategory = 0;
        function render() {
            menu.innerHTML = `
                <div class="chat-emoji-quick">
                    ${EMOJI_QUICK.map((e) => `<button type="button" class="chat-emoji-cell">${e}</button>`).join('')}
                </div>
                <div class="chat-emoji-tabs">
                    ${EMOJI_CATEGORIES.map((c, i) => `<button type="button" class="chat-emoji-tab${i === activeCategory ? ' active' : ''}" data-i="${i}">${c.label}</button>`).join('')}
                </div>
                <div class="chat-emoji-grid">
                    ${EMOJI_CATEGORIES[activeCategory].emojis.map((e) => `<button type="button" class="chat-emoji-cell">${e}</button>`).join('')}
                </div>
            `;
            menu.querySelectorAll('.chat-emoji-tab').forEach((tab) => {
                tab.addEventListener('click', () => { activeCategory = parseInt(tab.dataset.i, 10); render(); });
            });
            menu.querySelectorAll('.chat-emoji-cell').forEach((cell) => {
                cell.addEventListener('click', () => { closePopup(); onPick(cell.textContent); });
            });
        }
        render();

        document.body.appendChild(menu);
        openPopup = menu;
        setTimeout(() => document.addEventListener('click', closePopupOnOutsideClick), 0);
    }

    function openReactionPicker(anchorEl, messageId) {
        openEmojiPicker(anchorEl, (emoji) => setReaction(messageId, emoji));
    }

    async function toggleReaction(messageId, emoji, isMine) {
        if (isMine) await fetch(`/api/chat/messages/${messageId}/reactions`, { method: 'DELETE' });
        else await setReaction(messageId, emoji);
    }
    async function setReaction(messageId, emoji) {
        await fetch(`/api/chat/messages/${messageId}/reactions`, {
            method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ emoji })
        });
    }

    async function deleteMessage(messageId) {
        if (!confirm('Delete this message?')) return;
        await fetch(`/api/chat/messages/${messageId}`, { method: 'DELETE' });
    }

    function upsertMessage(dto) {
        const list = messagesByThread.get(dto.threadId);
        if (!list) return; // haven't loaded this thread's history yet - nothing to patch
        const idx = list.findIndex((x) => x.id === dto.id);
        if (idx >= 0) list[idx] = dto; else list.push(dto);
        if (dto.threadId === activeThreadId) renderMessages();
        refreshThreadSummary();
    }

    async function refreshThreadSummary() {
        // No single-thread GET route exposed to the client - simplest
        // correct refresh is just reloading the open-tabs list, which is
        // small (a handful of threads) and already how tab unread counts/
        // previews get updated elsewhere in this file.
        await loadOpenThreads();
    }

    async function loadOlderMessages() {
        const list = messagesByThread.get(activeThreadId) || [];
        if (list.length === 0) return;
        const oldest = list[0];
        const older = await fetch(`/api/chat/threads/${activeThreadId}/messages?before=${oldest.id}`).then((r) => (r.ok ? r.json() : []));
        messagesByThread.set(activeThreadId, [...older, ...list]);
        const prevHeight = messagesEl.scrollHeight;
        renderMessages();
        messagesEl.scrollTop = messagesEl.scrollHeight - prevHeight;
    }

    // --- Read receipts ---
    async function loadReadState(threadId) {
        const res = await fetch(`/api/chat/threads/${threadId}/read-state`);
        readStateByThread.set(threadId, res.ok ? await res.json() : []);
        renderSeenLine();
    }

    function renderSeenLine() {
        const msgs = messagesByThread.get(activeThreadId) || [];
        const last = msgs[msgs.length - 1];
        if (!last) { seenLineEl.textContent = ''; return; }
        const states = readStateByThread.get(activeThreadId) || [];
        const seenBy = states.filter((s) => s.userId !== me.id && s.lastReadAt && new Date(s.lastReadAt) >= new Date(last.createdAt))
            .map((s) => s.userName);
        seenLineEl.textContent = seenBy.length > 0 ? `Seen by ${seenBy.join(', ')}` : '';
    }

    function markThreadRead(threadId) {
        const msgs = messagesByThread.get(threadId) || [];
        const last = msgs[msgs.length - 1];
        if (!last) return;
        fetch(`/api/chat/threads/${threadId}/read`, {
            method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ lastReadMessageId: last.id })
        }).then(() => loadOpenThreads());
    }

    // --- Compose: text, mentions, attach, send ---
    function wireCompose(compose) {
        const fileInput = compose.querySelector('.chat-file-input');
        compose.querySelector('.chat-attach-btn').addEventListener('click', () => fileInput.click());
        fileInput.addEventListener('change', () => {
            pendingFile = fileInput.files[0] || null;
            compose.querySelector('.chat-attach-btn').textContent = pendingFile ? '📎✓' : '📎';
        });

        compose.querySelector('.chat-emoji-btn').addEventListener('click', (e) => {
            openEmojiPicker(e.currentTarget, (emoji) => {
                const start = composeTextEl.selectionStart ?? composeTextEl.value.length;
                const end = composeTextEl.selectionEnd ?? composeTextEl.value.length;
                composeTextEl.value = composeTextEl.value.slice(0, start) + emoji + composeTextEl.value.slice(end);
                const caret = start + emoji.length;
                composeTextEl.focus();
                composeTextEl.setSelectionRange(caret, caret);
                composeTextEl.dispatchEvent(new Event('input', { bubbles: true }));
            });
        });

        composeTextEl.addEventListener('input', () => {
            composeTextEl.style.height = 'auto';
            composeTextEl.style.height = Math.min(100, composeTextEl.scrollHeight) + 'px';
            updateMentionMenu();
            sendTypingSignal();
        });
        composeTextEl.addEventListener('keydown', (e) => {
            if (!mentionMenuEl.hidden) {
                const items = [...mentionMenuEl.querySelectorAll('.chat-mention-item')];
                let idx = items.findIndex((it) => it.classList.contains('active'));
                if (e.key === 'ArrowDown') { e.preventDefault(); idx = Math.min(items.length - 1, idx + 1); items.forEach((it) => it.classList.remove('active')); items[idx]?.classList.add('active'); return; }
                if (e.key === 'ArrowUp') { e.preventDefault(); idx = Math.max(0, idx - 1); items.forEach((it) => it.classList.remove('active')); items[idx]?.classList.add('active'); return; }
                if (e.key === 'Enter' || e.key === 'Tab') { e.preventDefault(); (items[idx] || items[0])?.click(); return; }
                if (e.key === 'Escape') { mentionMenuEl.hidden = true; return; }
            }
            if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); sendMessage(); }
        });
        compose.querySelector('.chat-send-btn').addEventListener('click', () => sendMessage());
    }

    function updateMentionMenu() {
        const caret = composeTextEl.selectionStart;
        const upToCaret = composeTextEl.value.slice(0, caret);
        const match = /@([a-zA-Z0-9._-]*)$/.exec(upToCaret);
        if (!match) { mentionMenuEl.hidden = true; return; }
        const query = match[1].toLowerCase();
        const thread = openThreads.find((t) => t.id === activeThreadId);
        const candidates = (thread ? thread.members : []).filter((m) => m.userId !== me.id && m.name.toLowerCase().startsWith(query));
        if (candidates.length === 0) { mentionMenuEl.hidden = true; return; }

        mentionMenuEl.innerHTML = candidates.map((m, i) => `
            <div class="chat-mention-item${i === 0 ? ' active' : ''}" data-user-id="${m.userId}" data-name="${escapeHtml(m.name)}">${escapeHtml(m.name)}</div>
        `).join('');
        mentionMenuEl.hidden = false;
        mentionMenuEl.querySelectorAll('.chat-mention-item').forEach((el) => {
            el.addEventListener('click', () => {
                const before = composeTextEl.value.slice(0, caret).replace(/@([a-zA-Z0-9._-]*)$/, `@${el.dataset.name} `);
                const after = composeTextEl.value.slice(caret);
                composeTextEl.value = before + after;
                composeTextEl.focus();
                const map = pendingMentions.get(activeThreadId) || new Map();
                map.set(el.dataset.userId, el.dataset.name);
                pendingMentions.set(activeThreadId, map);
                mentionMenuEl.hidden = true;
            });
        });
    }

    let typingSendTimer = null;
    function sendTypingSignal() {
        if (typingSendTimer) return;
        typingSendTimer = setTimeout(() => { typingSendTimer = null; }, 1500);
        if (connection && connection.state === 'Connected' && activeThreadId) {
            connection.invoke('Typing', activeThreadId).catch(() => {});
        }
    }

    async function sendMessage() {
        const text = composeTextEl.value.trim();
        if (!text && !pendingFile) return;
        const threadId = activeThreadId;
        if (!threadId) return;

        const mentionMap = pendingMentions.get(threadId) || new Map();
        const res = await fetch(`/api/chat/threads/${threadId}/messages`, {
            method: 'POST', headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ text: text || null, mentionedUserIds: [...mentionMap.keys()] })
        });
        if (!res.ok) return;
        const message = await res.json();
        upsertMessage(message);

        composeTextEl.value = '';
        composeTextEl.style.height = 'auto';
        pendingMentions.delete(threadId);

        if (pendingFile) {
            const formData = new FormData();
            formData.set('file', pendingFile);
            pendingFile = null;
            document.querySelector('.chat-attach-btn').textContent = '📎';
            const attRes = await fetch(`/api/chat/messages/${message.id}/attachments`, { method: 'POST', body: formData });
            if (attRes.ok) upsertMessage(await attRes.json());
        }
    }

    // --- Right-click image -> Add to Band's Catalog ---
    function onImageContextMenu(e, attachment) {
        e.preventDefault();
        const menu = document.createElement('div');
        menu.className = 'chat-context-menu';
        menu.style.left = e.clientX + 'px';
        menu.style.top = e.clientY + 'px';
        const btn = document.createElement('button');
        btn.type = 'button';
        btn.textContent = "Add to Band's Catalog";
        btn.addEventListener('click', () => { menu.remove(); openSaveToCatalogModal(attachment); });
        menu.appendChild(btn);
        document.body.appendChild(menu);
        const closeIt = (ev) => { if (!menu.contains(ev.target)) { menu.remove(); document.removeEventListener('click', closeIt); } };
        setTimeout(() => document.addEventListener('click', closeIt), 0);
    }

    function openSaveToCatalogModal(attachment) {
        const backdrop = document.createElement('div');
        backdrop.className = 'chat-submodal-backdrop';
        backdrop.innerHTML = `
            <div class="chat-submodal">
                <h3>Add to Band's Catalog</h3>
                <input type="text" class="chat-catalog-name-input" maxlength="200" placeholder="Name for this item">
                <div class="chat-submodal-error" hidden></div>
                <div class="chat-submodal-resolution" hidden></div>
                <div class="chat-submodal-buttons">
                    <button type="button" class="chat-cancel-btn">Cancel</button>
                    <button type="button" class="chat-primary-btn">Save</button>
                </div>
            </div>
        `;
        document.body.appendChild(backdrop);
        backdrop.addEventListener('click', (e) => { if (e.target === backdrop) backdrop.remove(); });
        backdrop.querySelector('.chat-cancel-btn').addEventListener('click', () => backdrop.remove());

        const nameInput = backdrop.querySelector('.chat-catalog-name-input');
        const errorEl = backdrop.querySelector('.chat-submodal-error');
        const resolutionEl = backdrop.querySelector('.chat-submodal-resolution');

        async function attemptSave(replaceExisting) {
            const name = nameInput.value.trim();
            if (!name) { errorEl.textContent = 'A name is required.'; errorEl.hidden = false; return; }
            errorEl.hidden = true;
            resolutionEl.hidden = true;
            resolutionEl.innerHTML = '';

            const res = await fetch(`/api/chat/attachments/${attachment.id}/save-to-catalog`, {
                method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ name, replaceExisting })
            });
            const body = await res.json().catch(() => ({}));
            if (res.ok) { backdrop.remove(); return; }

            errorEl.textContent = body.error || 'Could not save.';
            errorEl.hidden = false;

            if (body.duplicateContent) {
                resolutionEl.hidden = false;
                const renameBtn = document.createElement('button');
                renameBtn.type = 'button';
                renameBtn.className = 'chat-cancel-btn';
                renameBtn.textContent = `Rename existing item to "${name}"`;
                renameBtn.addEventListener('click', async () => {
                    const renameRes = await fetch(`/api/catalog/${body.existingId}`, {
                        method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ label: name })
                    });
                    const renameBody = await renameRes.json().catch(() => ({}));
                    if (!renameRes.ok) { errorEl.textContent = renameBody.error || 'Could not rename the existing item.'; return; }
                    backdrop.remove();
                });
                resolutionEl.appendChild(renameBtn);
            } else if (body.duplicateName) {
                resolutionEl.hidden = false;
                const replaceBtn = document.createElement('button');
                replaceBtn.type = 'button';
                replaceBtn.className = 'chat-cancel-btn';
                replaceBtn.textContent = 'Replace With This';
                replaceBtn.addEventListener('click', () => attemptSave(true));
                resolutionEl.appendChild(replaceBtn);
            }
        }

        backdrop.querySelector('.chat-primary-btn').addEventListener('click', () => attemptSave(false));
    }

    // --- SignalR ---
    async function joinThreadGroup(threadId) {
        if (!connection || connection.state !== 'Connected' || joinedThreadIds.has(threadId)) return;
        await connection.invoke('JoinThread', threadId).catch(() => {});
        joinedThreadIds.add(threadId);
    }

    async function connectHub() {
        await loadScript('/assets/vendor/signalr.min.js');
        connection = new signalR.HubConnectionBuilder().withUrl('/hubs/chat').withAutomaticReconnect().build();

        connection.on('MessageReceived', (dto) => upsertMessage(dto));
        connection.on('ReactionChanged', (dto) => upsertMessage(dto));
        connection.on('MessageRead', (evt) => { loadReadState(evt.threadId); });
        connection.on('ThreadCreated', () => loadOpenThreads());
        connection.on('ThreadUpdated', () => loadOpenThreads());
        connection.on('UserTyping', (threadId, userId) => {
            if (threadId !== activeThreadId || userId === me.id) return;
            const name = memberName(userId);
            typingLineEl.textContent = `${name} is typing...`;
            clearTimeout(typingTimers.get(threadId));
            typingTimers.set(threadId, setTimeout(() => { if (typingLineEl.textContent === `${name} is typing...`) typingLineEl.textContent = ''; }, 4000));
        });

        connection.onreconnected(async () => {
            joinedThreadIds.clear();
            for (const t of openThreads) await joinThreadGroup(t.id);
        });

        try {
            await connection.start();
            for (const t of openThreads) await joinThreadGroup(t.id);
        } catch { /* offline - REST polling still works */ }
    }

    // --- Badges ---
    async function pollChatUnread() {
        if (!me || !me.activeBandId) return;
        const res = await fetch('/api/chat/unread-count').catch(() => null);
        const count = res && res.ok ? (await res.json()).count : 0;
        updateBadgeEls(count);
    }
    function updateBadges() {
        const total = openThreads.reduce((sum, t) => sum + t.unreadCount, 0);
        updateBadgeEls(total);
    }
    function updateBadgeEls(count) {
        const topBadge = document.getElementById('chat-badge');
        if (topBadge) { topBadge.textContent = count > 99 ? '99+' : String(count); topBadge.hidden = count === 0; }
        if (bubbleBadgeEl) { bubbleBadgeEl.textContent = count > 99 ? '99+' : String(count); bubbleBadgeEl.hidden = count === 0; }
    }

    async function toggleModal() {
        if (!me || !me.activeBandId) return;
        if (!backdropEl) buildDom();
        if (!backdropEl.hidden) { backdropEl.hidden = true; return; }
        bubbleEl.hidden = true;
        backdropEl.hidden = false;
        if (bandMembersCache.length === 0) {
            bandMembersCache = await fetch('/api/profile/band-members').then((r) => (r.ok ? r.json() : []));
        }
        if (!connection) await connectHub();
        await loadOpenThreads();
        if (activeThreadId) await selectThread(activeThreadId);
    }

    async function init(meObj) {
        me = meObj;
        if (!me.activeBandId) return;
        pollChatUnread();
        setInterval(pollChatUnread, 60000);
    }

    return { init, toggleModal };
})();
