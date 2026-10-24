namespace Gamex.Messaging.Application.Conversations;

public sealed record OtherUserDto(Guid Id, string DisplayName, string? AvatarUrl);

public sealed record LastMessageDto(string Body, DateTime SentAt, bool IsOwn);

/// <summary>Item of <c>GET /conversations</c> (SPEC-messaging §3.1).</summary>
public sealed record ConversationSummaryDto(
    Guid Id,
    Guid ListingId,
    string GameTitle,
    OtherUserDto OtherUser,
    LastMessageDto? LastMessage,
    int UnreadCount);

public sealed record ConversationDto(
    Guid Id,
    Guid ListingId,
    string GameTitle,
    OtherUserDto OtherUser,
    DateTime CreatedAt,
    int UnreadCount);

public sealed record MessageDto(Guid Id, Guid ConversationId, Guid SenderId, string Body, DateTime SentAt, DateTime? ReadAt, bool IsOwn);

/// <summary>SignalR <c>MessageReceived</c> payload: the full message plus its conversation (not viewer-relative).</summary>
public sealed record RealtimeMessageDto(Guid Id, Guid ConversationId, Guid SenderId, string SenderName, string Body, DateTime SentAt);

public sealed record CursorPage<T>(IReadOnlyList<T> Items, string? NextCursor);

/// <summary>The authenticated caller; the name comes from the JWT <c>name</c> claim.</summary>
public sealed record Caller(Guid Id, string Name);

public sealed record StartConversationCommand(Guid? ListingId, string? ContactToken, string? InitialMessage);
