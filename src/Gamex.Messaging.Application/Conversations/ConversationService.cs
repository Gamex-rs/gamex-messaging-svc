using Gamex.Messaging.Application.Abstractions;
using Gamex.Messaging.Application.Common;
using Gamex.Messaging.Domain;
using Gamex.Messaging.Domain.Conversations;
using Microsoft.EntityFrameworkCore;

namespace Gamex.Messaging.Application.Conversations;

public interface IConversationService
{
    Task<OperationResult<CursorPage<ConversationSummaryDto>>> ListAsync(Guid userId, string? cursor, int? pageSize, CancellationToken ct);

    /// <summary>
    /// Starts a conversation about a listing proven by a contact token. If the caller already has one for this listing,
    /// returns <see cref="OperationStatus.Conflict"/> with the existing conversation as the value.
    /// </summary>
    Task<OperationResult<ConversationDto>> StartAsync(Caller caller, StartConversationCommand command, CancellationToken ct);

    Task<OperationResult<ConversationDto>> GetAsync(Guid userId, Guid conversationId, CancellationToken ct);

    Task<OperationResult<bool>> MarkReadAsync(Guid userId, Guid conversationId, CancellationToken ct);

    /// <param name="cursor">Id of the oldest message already shown (SPEC-messaging §3.2).</param>
    Task<OperationResult<CursorPage<MessageDto>>> GetMessagesAsync(Guid userId, Guid conversationId, string? cursor, int? pageSize, CancellationToken ct);

    Task<OperationResult<MessageDto>> SendAsync(Caller caller, Guid conversationId, string? body, CancellationToken ct);
}

internal sealed class ConversationService(
    IAppDbContext db,
    IContactTokenVerifier contactTokens,
    IEventPublisher events,
    IRealtimeNotifier realtime,
    TimeProvider time) : IConversationService
{
    public const int DefaultConversationPageSize = 20;
    public const int DefaultMessagePageSize = 30;
    public const int MaxMessagePageSize = 100;

    private DateTime Now => time.GetUtcNow().UtcDateTime;

    public async Task<OperationResult<CursorPage<ConversationSummaryDto>>> ListAsync(Guid userId, string? cursor, int? pageSize, CancellationToken ct)
    {
        if (pageSize is < 1 or > 50)
        {
            return OperationResult<CursorPage<ConversationSummaryDto>>.Invalid("pageSize", "Must be between 1 and 50.");
        }

        var query = db.Conversations.AsNoTracking().Where(c => c.InitiatorId == userId || c.RecipientId == userId);
        if (cursor is not null)
        {
            if (!Cursor.TryDecode(cursor, out var at, out var lastId))
            {
                return OperationResult<CursorPage<ConversationSummaryDto>>.Invalid("cursor", "Invalid cursor.");
            }

            var last = lastId.ToString();
#pragma warning disable CA1309 // Translated to SQL by EF; the StringComparison overload is not translatable.
            query = query.Where(c => c.LastMessageAt < at || (c.LastMessageAt == at && string.Compare(c.Id.ToString(), last) < 0));
#pragma warning restore CA1309
        }

        var size = pageSize ?? DefaultConversationPageSize;
        var page = await query.OrderByDescending(c => c.LastMessageAt).ThenByDescending(c => c.Id).Take(size + 1).ToListAsync(ct);
        var conversations = page.Take(size).ToList();
        var ids = conversations.Select(c => c.Id).ToList();

        var unread = await db.Messages.AsNoTracking()
            .Where(m => ids.Contains(m.ConversationId) && m.SenderId != userId && m.ReadAt == null)
            .GroupBy(m => m.ConversationId)
            .Select(g => new { ConversationId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ConversationId, x => x.Count, ct);

        // The conversation's LastMessageAt is its newest message's timestamp.
        var lastMessages = (await (from m in db.Messages.AsNoTracking()
                                   join c in db.Conversations on m.ConversationId equals c.Id
                                   where ids.Contains(c.Id) && m.CreatedAt == c.LastMessageAt
                                   select m).ToListAsync(ct))
            .GroupBy(m => m.ConversationId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(m => m.Id).First());

        var items = conversations.Select(c =>
        {
            var other = c.Other(userId);
            var lastMessage = lastMessages.TryGetValue(c.Id, out var m) ? new LastMessageDto(m.Body, m.CreatedAt, m.SenderId == userId) : null;
            return new ConversationSummaryDto(c.Id, c.ListingId, c.ListingGameTitle, new OtherUserDto(other.Id, other.Name, other.AvatarUrl), lastMessage, unread.GetValueOrDefault(c.Id));
        }).ToList();

        var next = page.Count > size ? Cursor.Encode(conversations[^1].LastMessageAt, conversations[^1].Id) : null;
        return OperationResult<CursorPage<ConversationSummaryDto>>.Ok(new CursorPage<ConversationSummaryDto>(items, next));
    }

    public async Task<OperationResult<ConversationDto>> StartAsync(Caller caller, StartConversationCommand command, CancellationToken ct)
    {
        var bodyError = ValidateBody(command.InitialMessage, "initialMessage");
        if (bodyError is not null)
        {
            return bodyError.As<ConversationDto>();
        }

        var contact = await contactTokens.VerifyAsync(command.ContactToken);
        if (contact is null)
        {
            return OperationResult<ConversationDto>.Invalid("contactToken", "Missing, invalid or expired. Open the listing again.");
        }

        if (command.ListingId is { } listingId && listingId != contact.ListingId)
        {
            return OperationResult<ConversationDto>.Invalid("listingId", "Does not match the contact token.");
        }

        if (contact.OwnerId == caller.Id)
        {
            return OperationResult<ConversationDto>.Invalid("listingId", "You cannot start a conversation about your own listing.");
        }

        var existing = await db.Conversations.AsNoTracking()
            .SingleOrDefaultAsync(c => c.ListingId == contact.ListingId && c.InitiatorId == caller.Id, ct);
        if (existing is not null)
        {
            return await ConflictWithExistingAsync(caller.Id, existing, ct);
        }

        var conversation = Conversation.Start(
            contact.ListingId,
            contact.GameTitle,
            new Participant(caller.Id, caller.Name, null),
            new Participant(contact.OwnerId, contact.OwnerName, contact.OwnerAvatarUrl),
            Now);
        var message = conversation.Post(caller.Id, caller.Name, command.InitialMessage!, Now);
        db.Conversations.Add(conversation);
        db.Messages.Add(message);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (db.IsUniqueViolation(ex))
        {
            // A concurrent request started it first.
            db.DiscardChanges();
            var winner = await db.Conversations.AsNoTracking().SingleAsync(c => c.ListingId == contact.ListingId && c.InitiatorId == caller.Id, ct);
            return await ConflictWithExistingAsync(caller.Id, winner, ct);
        }

        await AnnounceAsync(conversation, message, caller, ct);
        return OperationResult<ConversationDto>.Ok(ToDto(conversation, caller.Id, unreadCount: 0));
    }

    public async Task<OperationResult<ConversationDto>> GetAsync(Guid userId, Guid conversationId, CancellationToken ct)
    {
        var (conversation, failure) = await LoadForParticipantAsync(userId, conversationId, tracking: false, ct);
        if (conversation is null)
        {
            return failure!.As<ConversationDto>();
        }

        return OperationResult<ConversationDto>.Ok(ToDto(conversation, userId, await UnreadAsync(userId, conversationId, ct)));
    }

    public async Task<OperationResult<bool>> MarkReadAsync(Guid userId, Guid conversationId, CancellationToken ct)
    {
        var (conversation, failure) = await LoadForParticipantAsync(userId, conversationId, tracking: false, ct);
        if (conversation is null)
        {
            return failure!.As<bool>();
        }

        var now = Timestamps.ToStoragePrecision(Now);
        var updated = await db.Messages
            .Where(m => m.ConversationId == conversationId && m.SenderId != userId && m.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.ReadAt, now), ct);

        if (updated > 0)
        {
            await realtime.ConversationReadAsync(conversation.Other(userId).Id, conversationId, ct);
        }

        return OperationResult<bool>.Ok(true);
    }

    public async Task<OperationResult<CursorPage<MessageDto>>> GetMessagesAsync(Guid userId, Guid conversationId, string? cursor, int? pageSize, CancellationToken ct)
    {
        if (pageSize is < 1 or > MaxMessagePageSize)
        {
            return OperationResult<CursorPage<MessageDto>>.Invalid("pageSize", $"Must be between 1 and {MaxMessagePageSize}.");
        }

        var (conversation, failure) = await LoadForParticipantAsync(userId, conversationId, tracking: false, ct);
        if (conversation is null)
        {
            return failure!.As<CursorPage<MessageDto>>();
        }

        var query = db.Messages.AsNoTracking().Where(m => m.ConversationId == conversationId);
        if (cursor is not null)
        {
            var anchor = Guid.TryParse(cursor, out var anchorId)
                ? await db.Messages.AsNoTracking().Where(m => m.Id == anchorId && m.ConversationId == conversationId).Select(m => (DateTime?)m.CreatedAt).SingleOrDefaultAsync(ct)
                : null;
            if (anchor is not { } at)
            {
                return OperationResult<CursorPage<MessageDto>>.Invalid("cursor", "Unknown message id.");
            }

            var last = anchorId.ToString();
#pragma warning disable CA1309 // Translated to SQL by EF; the StringComparison overload is not translatable.
            query = query.Where(m => m.CreatedAt < at || (m.CreatedAt == at && string.Compare(m.Id.ToString(), last) < 0));
#pragma warning restore CA1309
        }

        var size = pageSize ?? DefaultMessagePageSize;
        var page = await query.OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id).Take(size + 1).ToListAsync(ct);
        var items = page.Take(size).Select(m => ToDto(m, userId)).ToList();
        var next = page.Count > size ? items[^1].Id.ToString() : null;
        return OperationResult<CursorPage<MessageDto>>.Ok(new CursorPage<MessageDto>(items, next));
    }

    public async Task<OperationResult<MessageDto>> SendAsync(Caller caller, Guid conversationId, string? body, CancellationToken ct)
    {
        var bodyError = ValidateBody(body, "body");
        if (bodyError is not null)
        {
            return bodyError.As<MessageDto>();
        }

        var (conversation, failure) = await LoadForParticipantAsync(caller.Id, conversationId, tracking: true, ct);
        if (conversation is null)
        {
            return failure!.As<MessageDto>();
        }

        var message = conversation.Post(caller.Id, caller.Name, body!, Now);
        db.Messages.Add(message);
        await db.SaveChangesAsync(ct);

        await AnnounceAsync(conversation, message, caller, ct);
        return OperationResult<MessageDto>.Ok(ToDto(message, caller.Id));
    }

    private async Task<(Conversation? Conversation, OperationResult<bool>? Failure)> LoadForParticipantAsync(
        Guid userId, Guid conversationId, bool tracking, CancellationToken ct)
    {
        var query = tracking ? db.Conversations : db.Conversations.AsNoTracking();
        var conversation = await query.SingleOrDefaultAsync(c => c.Id == conversationId, ct);
        if (conversation is null)
        {
            return (null, OperationResult<bool>.NotFound($"No conversation with id {conversationId}."));
        }

        return conversation.IsParticipant(userId)
            ? (conversation, null)
            : (null, OperationResult<bool>.Forbidden("You are not a participant in this conversation"));
    }

    private async Task AnnounceAsync(Conversation conversation, Message message, Caller sender, CancellationToken ct)
    {
        var recipient = conversation.Other(sender.Id);
        await events.PublishAsync(
            new MessageSent(conversation.Id, message.Id, sender.Id, sender.Name, recipient.Id, conversation.ListingId, conversation.ListingGameTitle, message.CreatedAt),
            ct);

        // Both sides: the recipient gets the new message, the sender's other devices stay in sync.
        await realtime.MessageReceivedAsync(
            [recipient.Id, sender.Id],
            new RealtimeMessageDto(message.Id, conversation.Id, sender.Id, sender.Name, message.Body, message.CreatedAt),
            ct);
    }

    private async Task<OperationResult<ConversationDto>> ConflictWithExistingAsync(Guid userId, Conversation existing, CancellationToken ct)
    {
        var dto = ToDto(existing, userId, await UnreadAsync(userId, existing.Id, ct));
        return OperationResult<ConversationDto>.Conflict("You already have a conversation about this listing.") with { Value = dto };
    }

    private Task<int> UnreadAsync(Guid userId, Guid conversationId, CancellationToken ct) =>
        db.Messages.CountAsync(m => m.ConversationId == conversationId && m.SenderId != userId && m.ReadAt == null, ct);

    private static OperationResult<bool>? ValidateBody(string? body, string field) =>
        body?.Trim().Length is >= 1 and <= Conversation.MaxBodyLength
            ? null
            : OperationResult<bool>.Invalid(field, $"Must be 1–{Conversation.MaxBodyLength} characters.");

    private static ConversationDto ToDto(Conversation c, Guid userId, int unreadCount)
    {
        var other = c.Other(userId);
        return new ConversationDto(c.Id, c.ListingId, c.ListingGameTitle, new OtherUserDto(other.Id, other.Name, other.AvatarUrl), c.CreatedAt, unreadCount);
    }

    private static MessageDto ToDto(Message m, Guid userId) =>
        new(m.Id, m.ConversationId, m.SenderId, m.Body, m.CreatedAt, m.ReadAt, m.SenderId == userId);
}
