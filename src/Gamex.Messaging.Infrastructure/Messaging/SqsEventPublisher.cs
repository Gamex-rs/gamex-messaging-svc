using System.Text.Json;
using Amazon.SQS;
using Amazon.SQS.Model;
using Gamex.Messaging.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Gamex.Messaging.Infrastructure.Messaging;

internal sealed class MessagingOptions
{
    /// <summary><c>SQS_ACTIVITY_EVENTS_URL</c>. Events are dropped (logged) when empty.</summary>
    public string? ActivityEventsQueueUrl { get; init; }
}

/// <summary>Publishes <c>message.sent</c> to <c>gamex-activity-events</c> (SPEC-messaging §6).</summary>
internal sealed partial class SqsEventPublisher(IAmazonSQS sqs, MessagingOptions options, ILogger<SqsEventPublisher> logger) : IEventPublisher
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task PublishAsync(MessageSent message, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.ActivityEventsQueueUrl))
        {
            LogNotConfigured(logger);
            return;
        }

        var body = JsonSerializer.Serialize(new
        {
            eventType = "message.sent",
            conversationId = message.ConversationId,
            messageId = message.MessageId,
            senderId = message.SenderId,
            senderName = message.SenderName,
            recipientId = message.RecipientId,
            listingId = message.ListingId,
            gameTitle = message.GameTitle,
            occurredAt = message.OccurredAt,
        }, Json);

        try
        {
            await sqs.SendMessageAsync(new SendMessageRequest
            {
                QueueUrl = options.ActivityEventsQueueUrl,
                MessageBody = body,
                MessageAttributes = new Dictionary<string, MessageAttributeValue>
                {
                    ["eventType"] = new() { DataType = "String", StringValue = "message.sent" },
                },
            }, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (AmazonSQSException ex)
        {
            LogSendFailed(logger, ex, message.MessageId);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Publishing message.sent for {MessageId} failed")]
    private static partial void LogSendFailed(ILogger logger, Exception exception, Guid messageId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "SQS_ACTIVITY_EVENTS_URL not configured; message.sent dropped")]
    private static partial void LogNotConfigured(ILogger logger);
}
