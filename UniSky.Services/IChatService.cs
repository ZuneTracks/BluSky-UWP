using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace UniSky.Services;

public sealed class ChatConversation
{
    public string Id { get; init; }
    public string OtherDid { get; init; }
    public string DisplayName { get; init; }
    public string Handle { get; init; }
    public string Avatar { get; init; }
    public string LastMessage { get; init; }
    public DateTime? LastMessageAt { get; init; }
    public long UnreadCount { get; init; }
    public bool IsMuted { get; init; }
    public bool IsRequest { get; init; }
}

public sealed class ChatReaction
{
    public string Value { get; init; }
    public string SenderDid { get; init; }
}

public sealed class ChatMessage
{
    public string Id { get; init; }
    public string Text { get; init; }
    public string SenderDid { get; init; }
    public DateTime? SentAt { get; init; }
    public IReadOnlyList<ChatReaction> Reactions { get; init; } = [];
}

public sealed class ChatConversationPage
{
    public string Cursor { get; init; }
    public IReadOnlyList<ChatConversation> Conversations { get; init; }
}

public sealed class ChatMessagePage
{
    public string Cursor { get; init; }
    public IReadOnlyList<ChatMessage> Messages { get; init; }
}

public sealed class ChatRecipient
{
    public string Did { get; init; }
    public string DisplayName { get; init; }
    public string Handle { get; init; }
    public string DisplayLabel => string.IsNullOrWhiteSpace(Handle)
        ? DisplayName
        : $"{DisplayName} (@{Handle.TrimStart('@')})";
}

public sealed class ChatRecipientPage
{
    public string Cursor { get; init; }
    public IReadOnlyList<ChatRecipient> Recipients { get; init; }
}

public interface IChatService
{
    Task<ChatConversationPage> FetchConversationsAsync(string cursor, int limit, CancellationToken cancellationToken = default);
    Task<ChatConversationPage> FetchRequestsAsync(string cursor, int limit, CancellationToken cancellationToken = default);
    Task<ChatMessagePage> FetchMessagesAsync(string conversationId, string cursor, int limit, CancellationToken cancellationToken = default);
    Task<ChatRecipientPage> FetchFollowersAsync(string cursor, int limit, CancellationToken cancellationToken = default);
    Task<ChatConversation> StartDirectConversationAsync(string recipient, CancellationToken cancellationToken = default);
    Task<ChatMessage> SendTextMessageAsync(string conversationId, string text, CancellationToken cancellationToken = default);
    Task MarkReadAsync(string conversationId, CancellationToken cancellationToken = default);
    Task AcceptConversationAsync(string conversationId, CancellationToken cancellationToken = default);
    Task LeaveConversationAsync(string conversationId, CancellationToken cancellationToken = default);
    Task SetConversationMutedAsync(string conversationId, bool muted, CancellationToken cancellationToken = default);
    Task<ChatMessage> SetReactionAsync(string conversationId, string messageId, string value, bool add, CancellationToken cancellationToken = default);
    Task DeleteMessageForSelfAsync(string conversationId, string messageId, CancellationToken cancellationToken = default);
    Task ReportMessageAsync(string senderDid, string conversationId, string messageId, string reasonType, string details, CancellationToken cancellationToken = default);
    Task BlockAsync(string did, CancellationToken cancellationToken = default);
}
