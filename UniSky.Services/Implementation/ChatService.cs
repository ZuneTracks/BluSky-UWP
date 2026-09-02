using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FishyFlip;
using FishyFlip.Lexicon;
using FishyFlip.Lexicon.App.Bsky.Actor;
using FishyFlip.Lexicon.Chat.Bsky.Convo;
using FishyFlip.Lexicon.App.Bsky.Graph;
using FishyFlip.Models;

namespace UniSky.Services;

public sealed class ChatService(
    IProtocolService protocolService) : IChatService
{
    public async Task<ChatConversationPage> FetchConversationsAsync(
        string cursor, int limit, CancellationToken cancellationToken = default)
    {
        var protocol = protocolService.Protocol;
        var response = (await protocol.ChatBskyConvo
            .ListConvosAsync(limit: Math.Clamp(limit, 1, 100), cursor: cursor, status: "accepted", cancellationToken: cancellationToken)
            .ConfigureAwait(false))
            .HandleResult();

        var myDid = protocol.Session?.Did?.ToString();
        var conversations = (response?.Convos ?? [])
            .Where(conversation => IsDirectConversation(conversation, myDid))
            .Select(conversation => ToConversation(conversation, myDid))
            .ToList();

        return new ChatConversationPage
        {
            Cursor = response?.Cursor,
            Conversations = conversations,
        };
    }

    public async Task<ChatMessagePage> FetchMessagesAsync(
        string conversationId, string cursor, int limit, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
            throw new ArgumentException("A conversation id is required.", nameof(conversationId));

        var response = (await protocolService.Protocol.ChatBskyConvo
            .GetMessagesAsync(conversationId, limit: Math.Clamp(limit, 1, 100), cursor: cursor, cancellationToken: cancellationToken)
            .ConfigureAwait(false))
            .HandleResult();

        return new ChatMessagePage
        {
            Cursor = response?.Cursor,
            Messages = (response?.Messages ?? [])
                .OfType<MessageView>()
                .Select(ToMessage)
                .ToList(),
        };
    }

    public async Task<ChatRecipientPage> FetchFollowersAsync(
        string cursor, int limit, CancellationToken cancellationToken = default)
    {
        var protocol = protocolService.Protocol;
        var currentUserDid = protocol.Session?.Did
            ?? throw new InvalidOperationException("You must be signed in to load followers.");
        var response = (await protocol
            .GetFollowersAsync(currentUserDid, limit: Math.Clamp(limit, 1, 100), cursor: cursor, cancellationToken: cancellationToken)
            .ConfigureAwait(false))
            .HandleResult();

        return new ChatRecipientPage
        {
            Cursor = response?.Cursor,
            Recipients = (response?.Followers ?? [])
                .Where(profile => profile?.Did is not null)
                .Select(profile => new ChatRecipient
                {
                    Did = profile.Did.ToString(),
                    DisplayName = string.IsNullOrWhiteSpace(profile.DisplayName)
                        ? profile.Handle?.ToString() ?? "Unknown user"
                        : profile.DisplayName,
                    Handle = profile.Handle?.ToString(),
                })
                .ToList(),
        };
    }

    public async Task<ChatConversation> StartDirectConversationAsync(
        string recipient, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(recipient))
            throw new ArgumentException("A recipient handle or DID is required.", nameof(recipient));

        var protocol = protocolService.Protocol;
        if (!ATIdentifier.TryCreate(recipient.Trim().TrimStart('@'), out var identifier))
            throw new ArgumentException("Enter a valid Bluesky handle or DID.", nameof(recipient));

        var recipientDid = identifier as ATDid;
        if (recipientDid is null)
        {
            var profile = (await protocol
                .GetProfileAsync(identifier, cancellationToken: cancellationToken)
                .ConfigureAwait(false))
                .HandleResult();
            recipientDid = profile?.Did
                ?? throw new InvalidOperationException("The recipient could not be resolved.");
        }

        if (string.Equals(recipientDid.ToString(), protocol.Session?.Did?.ToString(), StringComparison.Ordinal))
            throw new ArgumentException("You cannot start a chat with yourself.", nameof(recipient));

        var response = (await protocol.ChatBskyConvo
            .GetConvoForMembersAsync([recipientDid], cancellationToken: cancellationToken)
            .ConfigureAwait(false))
            .HandleResult();

        if (response?.Convo is null)
            throw new InvalidOperationException("The chat service did not return a conversation.");
        if (!IsDirectConversation(response.Convo, protocol.Session?.Did?.ToString()))
            throw new InvalidOperationException("The recipient is not available for a one-to-one direct conversation.");

        return ToConversation(response.Convo, protocol.Session?.Did?.ToString());
    }

    public async Task<ChatMessage> SendTextMessageAsync(
        string conversationId, string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
            throw new ArgumentException("A conversation id is required.", nameof(conversationId));
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("A message cannot be empty.", nameof(text));

        var message = (await protocolService.Protocol.ChatBskyConvo
            .SendMessageAsync(conversationId, new MessageInput(text), cancellationToken)
            .ConfigureAwait(false))
            .HandleResult();

        if (message is null)
            throw new InvalidOperationException("The chat service did not return the sent message.");

        return ToMessage(message);
    }

    public async Task MarkReadAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
            throw new ArgumentException("A conversation id is required.", nameof(conversationId));

        _ = (await protocolService.Protocol.ChatBskyConvo
            .UpdateReadAsync(conversationId, cancellationToken: cancellationToken)
            .ConfigureAwait(false))
            .HandleResult();
    }

    private static bool IsDirectConversation(ConvoView conversation, string myDid)
        => conversation?.Members?.Count == 2 &&
           !string.IsNullOrWhiteSpace(conversation.Id) &&
           !string.IsNullOrWhiteSpace(myDid) &&
           conversation.Members.All(member => member?.Did is not null) &&
           conversation.Members.Any(member => string.Equals(member?.Did?.ToString(), myDid, StringComparison.Ordinal));

    private static ChatConversation ToConversation(ConvoView conversation, string myDid)
    {
        var otherMember = conversation.Members
            .First(member => !string.Equals(member?.Did?.ToString(), myDid, StringComparison.Ordinal));
        var lastMessage = conversation.LastMessage as MessageView;

        return new ChatConversation
        {
            Id = conversation.Id,
            DisplayName = string.IsNullOrWhiteSpace(otherMember.DisplayName)
                ? otherMember.Handle?.ToString() ?? "Unknown user"
                : otherMember.DisplayName,
            Handle = otherMember.Handle?.ToString(),
            Avatar = otherMember.Avatar,
            LastMessage = lastMessage?.Text
                ?? (conversation.LastMessage is null ? "No messages yet" : "Last message unavailable"),
            LastMessageAt = lastMessage?.SentAt,
            UnreadCount = conversation.UnreadCount,
        };
    }

    private static ChatMessage ToMessage(MessageView message)
        => new()
        {
            Id = message.Id,
            Text = string.IsNullOrWhiteSpace(message.Text) ? "Unsupported message content" : message.Text,
            SenderDid = message.Sender?.Did?.ToString(),
            SentAt = message.SentAt,
        };
}
