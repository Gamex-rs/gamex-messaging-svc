using Gamex.Messaging.Application.Conversations;
using Gamex.Messaging.Domain.Conversations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Gamex.Messaging.Application.Abstractions;

public interface IAppDbContext
{
    DbSet<Conversation> Conversations { get; }
    DbSet<Message> Messages { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken);

    bool IsUniqueViolation(DbUpdateException exception);

    void DiscardChanges();
}

/// <summary>Listing data proven by a contact token issued by the User Library Service.</summary>
public sealed record ListingContact(Guid ListingId, Guid OwnerId, string OwnerName, string? OwnerAvatarUrl, string GameTitle);

public interface IContactTokenVerifier
{
    /// <returns>The verified contact, or <c>null</c> if the token is invalid, expired or not a contact token.</returns>
    Task<ListingContact?> VerifyAsync(string? token);
}

/// <summary><c>message.sent</c> → <c>gamex-activity-events</c> (the body is intentionally not included).</summary>
public sealed record MessageSent(
    Guid ConversationId,
    Guid MessageId,
    Guid SenderId,
    string SenderName,
    Guid RecipientId,
    Guid ListingId,
    string GameTitle,
    DateTime OccurredAt);

public interface IEventPublisher
{
    /// <summary>Publishes after commit; failures are logged, not thrown (the message is already saved).</summary>
    Task PublishAsync(MessageSent message, CancellationToken cancellationToken);
}

/// <summary>Server → client pushes over SignalR (group <c>user:{userId}</c>).</summary>
public interface IRealtimeNotifier
{
    Task MessageReceivedAsync(IReadOnlyCollection<Guid> userIds, RealtimeMessageDto message, CancellationToken cancellationToken);

    Task ConversationReadAsync(Guid userId, Guid conversationId, CancellationToken cancellationToken);
}
