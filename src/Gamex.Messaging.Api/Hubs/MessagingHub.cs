using Gamex.Messaging.Api.Auth;
using Gamex.Messaging.Application.Abstractions;
using Gamex.Messaging.Application.Conversations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Gamex.Messaging.Api.Hubs;

/// <summary>
/// Receive-only hub (SPEC-messaging §4): clients send messages via REST. Each connection joins <c>user:{userId}</c>.
/// Running more than one instance needs a SignalR backplane (e.g. Redis) so pushes reach every instance's clients.
/// </summary>
[Authorize]
internal sealed class MessagingHub : Hub
{
    public static string UserGroup(Guid userId) => $"user:{userId}";

    public override async Task OnConnectedAsync()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(Context.User!.UserId()));
        await base.OnConnectedAsync();
    }
}

internal sealed class SignalRNotifier(IHubContext<MessagingHub> hub) : IRealtimeNotifier
{
    public const string MessageReceived = "MessageReceived";
    public const string ConversationRead = "ConversationRead";

    public Task MessageReceivedAsync(IReadOnlyCollection<Guid> userIds, RealtimeMessageDto message, CancellationToken cancellationToken) =>
        hub.Clients.Groups(userIds.Distinct().Select(MessagingHub.UserGroup).ToList()).SendAsync(MessageReceived, message, cancellationToken);

    public Task ConversationReadAsync(Guid userId, Guid conversationId, CancellationToken cancellationToken) =>
        hub.Clients.Group(MessagingHub.UserGroup(userId)).SendAsync(ConversationRead, new { conversationId }, cancellationToken);
}
