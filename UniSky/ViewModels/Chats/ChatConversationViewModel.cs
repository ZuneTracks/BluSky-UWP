using System;
using CommunityToolkit.Mvvm.ComponentModel;
using UniSky.Services;

namespace UniSky.ViewModels.Chats;

public sealed partial class ChatConversationViewModel : ObservableObject
{
    public ChatConversationViewModel(ChatConversation conversation)
    {
        Id = conversation.Id;
        DisplayName = conversation.DisplayName;
        Handle = conversation.Handle;
        Avatar = conversation.Avatar;
        Preview = conversation.LastMessage;
        Timestamp = conversation.LastMessageAt?.ToLocalTime().ToString("g") ?? string.Empty;
        UnreadCount = conversation.UnreadCount;
    }

    public string Id { get; }
    public string DisplayName { get; }
    public string Handle { get; }
    public string Avatar { get; }
    public string DisplayLabel => string.IsNullOrWhiteSpace(Handle)
        ? $"{DisplayName}: {Preview}"
        : $"{DisplayName} (@{Handle.TrimStart('@')}): {Preview}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayLabel))]
    private string preview;

    [ObservableProperty]
    private string timestamp;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnreadMessages))]
    private long unreadCount;

    public bool HasUnreadMessages => UnreadCount > 0;

    public void UpdateLastMessage(ChatMessage message)
    {
        Preview = message.Text;
        Timestamp = message.SentAt?.ToLocalTime().ToString("g") ?? string.Empty;
    }
}
