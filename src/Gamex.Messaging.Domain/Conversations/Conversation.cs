namespace Gamex.Messaging.Domain.Conversations;

/// <summary>Participant names are snapshots: the initiator's from their JWT, the owner's from the listing contact token.</summary>
public sealed record Participant(Guid Id, string Name, string? AvatarUrl);

/// <summary>A conversation between a user and a listing's owner, always about that listing.</summary>
public sealed class Conversation
{
    public const int MaxBodyLength = 2000;

    private Conversation()
    {
        ListingGameTitle = null!;
        InitiatorName = null!;
        RecipientName = null!;
    }

    public Guid Id { get; private set; }
    public Guid ListingId { get; private set; }
    public string ListingGameTitle { get; private set; }
    public Guid InitiatorId { get; private set; }
    public string InitiatorName { get; private set; }
    public string? InitiatorAvatarUrl { get; private set; }

    /// <summary>The listing owner.</summary>
    public Guid RecipientId { get; private set; }
    public string RecipientName { get; private set; }
    public string? RecipientAvatarUrl { get; private set; }
    public DateTime CreatedAt { get; private set; }

    /// <summary>Orders the conversation list.</summary>
    public DateTime LastMessageAt { get; private set; }

    public static Conversation Start(Guid listingId, string gameTitle, Participant initiator, Participant owner, DateTime now)
    {
        if (initiator.Id == owner.Id)
        {
            throw new InvalidOperationException("A listing owner cannot start a conversation with themselves.");
        }

        var at = Timestamps.ToStoragePrecision(now);
        return new Conversation
        {
            Id = Guid.CreateVersion7(),
            ListingId = listingId,
            ListingGameTitle = gameTitle,
            InitiatorId = initiator.Id,
            InitiatorName = initiator.Name,
            InitiatorAvatarUrl = initiator.AvatarUrl,
            RecipientId = owner.Id,
            RecipientName = owner.Name,
            RecipientAvatarUrl = owner.AvatarUrl,
            CreatedAt = at,
            LastMessageAt = at,
        };
    }

    public bool IsParticipant(Guid userId) => userId == InitiatorId || userId == RecipientId;

    public Participant Other(Guid userId) => userId == InitiatorId
        ? new Participant(RecipientId, RecipientName, RecipientAvatarUrl)
        : new Participant(InitiatorId, InitiatorName, InitiatorAvatarUrl);

    /// <summary>Keeps the sender's display name current (it comes from their latest JWT).</summary>
    public Message Post(Guid senderId, string senderName, string body, DateTime now)
    {
        if (!IsParticipant(senderId))
        {
            throw new InvalidOperationException("Only participants can post.");
        }

        var text = body?.Trim() ?? "";
        if (text.Length is 0 or > MaxBodyLength)
        {
            throw new ArgumentException($"Message body must be 1–{MaxBodyLength} characters.", nameof(body));
        }

        if (senderId == InitiatorId)
        {
            InitiatorName = senderName;
        }
        else
        {
            RecipientName = senderName;
        }

        var message = Message.Create(Id, senderId, text, now);
        LastMessageAt = message.CreatedAt;
        return message;
    }
}

public sealed class Message
{
    private Message()
    {
        Body = null!;
    }

    public Guid Id { get; private set; }
    public Guid ConversationId { get; private set; }
    public Guid SenderId { get; private set; }
    public string Body { get; private set; }

    /// <summary>Null = unread by the recipient.</summary>
    public DateTime? ReadAt { get; private set; }
    public DateTime CreatedAt { get; private set; }

    internal static Message Create(Guid conversationId, Guid senderId, string body, DateTime now) => new()
    {
        Id = Guid.CreateVersion7(),
        ConversationId = conversationId,
        SenderId = senderId,
        Body = body,
        CreatedAt = Timestamps.ToStoragePrecision(now),
    };
}
