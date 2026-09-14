using BandManager.Web.Auth;
using BandManager.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace BandManager.Web.Hubs;

/// <summary>
/// Real-time relay for Band Chat. Persistence and validation live in
/// IChatService (see its own doc comment) - this Hub only re-validates
/// thread membership before letting a connection join a thread's group
/// (never trusts a client-supplied threadId blindly), then relays typing
/// signals to that group. New messages/reactions/deletes/read-receipts are
/// NOT pushed from Hub methods - ChatController pushes them via
/// IHubContext&lt;ChatHub&gt; after persisting, so there's exactly one place
/// each action happens (matches NotificationsController.SendBroadcast's
/// write-then-fan-out shape). Personal events (added to a new SideChat,
/// unread count changed) use SignalR's built-in Clients.User(...)
/// targeting - this works with zero custom IUserIdProvider because this
/// app's auth cookie already carries the user's id as ClaimTypes.
/// NameIdentifier, which is exactly what the default user-id provider
/// reads.
/// </summary>
[Authorize]
public class ChatHub(IChatService chatService) : Hub
{
    private static HashSet<Guid> JoinedThreads(HubCallerContext context)
    {
        if (context.Items["JoinedThreads"] is not HashSet<Guid> set)
        {
            set = [];
            context.Items["JoinedThreads"] = set;
        }
        return set;
    }

    public async Task JoinThread(Guid threadId)
    {
        var userId = Context.User?.GetUserId();
        if (userId is null) return;
        if (!await chatService.IsMemberAsync(threadId, userId.Value)) return;

        await Groups.AddToGroupAsync(Context.ConnectionId, $"thread-{threadId}");
        JoinedThreads(Context).Add(threadId);
    }

    public async Task LeaveThread(Guid threadId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"thread-{threadId}");
        JoinedThreads(Context).Remove(threadId);
    }

    // No persisted state, no DB round trip - the client already debounces
    // keystrokes before calling this; the receiving client resolves
    // userId to a display name itself from the thread's own member list
    // it already has loaded, so this stays a cheap pure relay.
    public async Task Typing(Guid threadId)
    {
        var userId = Context.User?.GetUserId();
        if (userId is null || !JoinedThreads(Context).Contains(threadId)) return;
        await Clients.OthersInGroup($"thread-{threadId}").SendAsync("UserTyping", threadId, userId.Value);
    }
}
