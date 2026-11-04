using System;
using UniSky.Services;

namespace UniSky.ViewModels.Chats;

public sealed class ChatMessageViewModel
{
    public ChatMessageViewModel(ChatMessage message, string currentUserDid, ChatConversationViewModel conversation = null)
    {
        Id = message.Id;
        Text = message.Text;
        IsMine = string.Equals(message.SenderDid, currentUserDid, StringComparison.Ordinal);
        SenderLabel = IsMine ? "You" : GetOtherParticipantName(conversation);
        SentAt = message.SentAt;
        Timestamp = message.SentAt?.ToLocalTime().ToString("g") ?? string.Empty;
    }

    public string Id { get; }
    public string Text { get; }
    public bool IsMine { get; }
    public string SenderLabel { get; }
    public DateTime? SentAt { get; }
    public string Timestamp { get; }

    private static string GetOtherParticipantName(ChatConversationViewModel conversation)
    {
        if (!string.IsNullOrWhiteSpace(conversation?.DisplayName))
            return conversation.DisplayName;

        if (!string.IsNullOrWhiteSpace(conversation?.Handle))
            return "@" + conversation.Handle.TrimStart('@');

        return "Other participant";
    }
}
