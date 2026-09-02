using System;
using UniSky.Services;

namespace UniSky.ViewModels.Chats;

public sealed class ChatMessageViewModel
{
    public ChatMessageViewModel(ChatMessage message, string currentUserDid)
    {
        Id = message.Id;
        Text = message.Text;
        IsMine = string.Equals(message.SenderDid, currentUserDid, StringComparison.Ordinal);
        SenderLabel = IsMine ? "You" : "Other participant";
        SentAt = message.SentAt;
        Timestamp = message.SentAt?.ToLocalTime().ToString("g") ?? string.Empty;
    }

    public string Id { get; }
    public string Text { get; }
    public bool IsMine { get; }
    public string SenderLabel { get; }
    public DateTime? SentAt { get; }
    public string Timestamp { get; }
}
