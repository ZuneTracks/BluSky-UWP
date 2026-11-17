using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using UniSky.Services;

namespace UniSky.ViewModels.Chats;

public sealed partial class ChatMessageViewModel : ObservableObject
{
    private readonly string currentUserDid;

    public ChatMessageViewModel(ChatMessage message, string currentUserDid, ChatConversationViewModel conversation = null)
    {
        this.currentUserDid = currentUserDid;
        Id = message.Id;
        ConversationId = conversation?.Id;
        Text = message.Text;
        SenderDid = message.SenderDid;
        IsMine = string.Equals(message.SenderDid, currentUserDid, StringComparison.Ordinal);
        SenderLabel = IsMine ? "You" : GetOtherParticipantName(conversation);
        SentAt = message.SentAt;
        Timestamp = message.SentAt?.ToLocalTime().ToString("g") ?? string.Empty;
        UpdateReactions(message.Reactions);
    }

    public string Id { get; }
    public string ConversationId { get; }
    public string Text { get; }
    public string SenderDid { get; }
    public bool IsMine { get; }
    public bool IsNotMine => !IsMine;
    public string SenderLabel { get; }
    public DateTime? SentAt { get; }
    public string Timestamp { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReactions))]
    private string reactionSummary;

    public bool HasReactions => !string.IsNullOrEmpty(ReactionSummary);

    public IReadOnlyCollection<string> MyReactions { get; private set; } = [];

    public void UpdateReactions(IReadOnlyList<ChatReaction> reactions)
    {
        reactions ??= [];
        MyReactions = new HashSet<string>(reactions
            .Where(reaction => string.Equals(reaction.SenderDid, currentUserDid, StringComparison.Ordinal))
            .Select(reaction => reaction.Value));
        ReactionSummary = string.Join("  ", reactions
            .GroupBy(reaction => reaction.Value)
            .Select(group => group.Count() > 1 ? $"{group.Key} {group.Count()}" : group.Key));
    }

    private static string GetOtherParticipantName(ChatConversationViewModel conversation)
    {
        if (!string.IsNullOrWhiteSpace(conversation?.DisplayName))
            return conversation.DisplayName;

        if (!string.IsNullOrWhiteSpace(conversation?.Handle))
            return "@" + conversation.Handle.TrimStart('@');

        return "Other participant";
    }
}
