using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace UniSky.Services;

public sealed class ChatConversation
{
    public string Id { get; init; }
    public string DisplayName { get; init; }
    public string Handle { get; init; }
    public string Avatar { get; init; }
    public string LastMessage { get; init; }
    public DateTime? LastMessageAt { get; init; }
    public long UnreadCount { get; init; }
}

public sealed class ChatMessage
{
    public string Id { get; init; }
    public string Text { get; init; }
    public string SenderDid { get; init; }
    public DateTime? SentAt { get; init; }
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
    Task<ChatMessagePage> FetchMessagesAsync(string conversationId, string cursor, int limit, CancellationToken cancellationToken = default);
    Task<ChatRecipientPage> FetchFollowersAsync(string cursor, int limit, CancellationToken cancellationToken = default);
    Task<ChatConversation> StartDirectConversationAsync(string recipient, CancellationToken cancellationToken = default);
    Task<ChatMessage> SendTextMessageAsync(string conversationId, string text, CancellationToken cancellationToken = default);
    Task MarkReadAsync(string conversationId, CancellationToken cancellationToken = default);
}
