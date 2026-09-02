using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FishyFlip;
using Microsoft.Extensions.Logging;
using UniSky.Services;
using UniSky.Services.Navigation;
using UniSky.Helpers;
using Windows.UI.Core;
using Windows.UI.Xaml;

namespace UniSky.ViewModels.Chats;

public sealed partial class ChatsPageViewModel : ViewModelBase
{
    private const int PageSize = 50;
    private const int MaxPageAttempts = 10;

    private readonly IChatService chatService;
    private readonly IProtocolService protocolService;
    private readonly ILogger<ChatsPageViewModel> logger;
    private readonly CoreDispatcher dispatcher = Window.Current.Dispatcher;
    private readonly SemaphoreSlim conversationsSemaphore = new(1, 1);
    private readonly SemaphoreSlim messagesSemaphore = new(1, 1);
    private readonly SemaphoreSlim followersSemaphore = new(1, 1);
    private readonly HashSet<string> conversationIds = [];
    private readonly HashSet<string> messageIds = [];
    private readonly HashSet<string> followerDids = [];
    private int loaded;
    private string conversationsCursor;
    private string messagesCursor;
    private string followersCursor;
    private bool conversationsExhausted;
    private bool messagesExhausted;
    private bool followersExhausted;
    private bool isFirstMessagesPage;

    public ChatsPageViewModel(
        INavigationContext navigation,
        IChatService chatService,
        IProtocolService protocolService,
        ILogger<ChatsPageViewModel> logger)
        : base(navigation)
    {
        this.chatService = chatService;
        this.protocolService = protocolService;
        this.logger = logger;
    }

    public ObservableCollection<ChatConversationViewModel> Conversations { get; } = [];
    public ObservableCollection<ChatMessageViewModel> Messages { get; } = [];
    public ObservableCollection<ChatRecipient> Followers { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedConversation))]
    [NotifyPropertyChangedFor(nameof(HasMoreMessages))]
    [NotifyPropertyChangedFor(nameof(CanSend))]
    private ChatConversationViewModel selectedConversation;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSend))]
    private string draft;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSend))]
    private bool isSending;

    [ObservableProperty]
    private string sendError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStartConversation))]
    private string recipient;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStartConversation))]
    private bool isStartingConversation;

    [ObservableProperty]
    private bool isConversationListEmpty;

    [ObservableProperty]
    private bool isMessagesEmpty;

    [ObservableProperty]
    private bool isFollowersEmpty;

    public bool HasSelectedConversation => SelectedConversation != null;
    public bool CanSend => HasSelectedConversation && !IsSending && !string.IsNullOrWhiteSpace(Draft);
    public bool CanStartConversation => !IsStartingConversation && !string.IsNullOrWhiteSpace(Recipient);
    public bool HasMoreConversations => !conversationsExhausted;
    public bool HasMoreMessages => HasSelectedConversation && !messagesExhausted;
    public bool HasMoreFollowers => !followersExhausted;

    public async Task EnsureLoadedAsync()
    {
        if (Interlocked.Exchange(ref loaded, 1) != 0)
            return;

        await RefreshAsync().ConfigureAwait(false);
        await LoadFollowersAsync().ConfigureAwait(false);
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        var selectedId = SelectedConversation?.Id;
        await RefreshConversationsAsync().ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(selectedId))
            return;

        var refreshedSelection = Conversations.FirstOrDefault(conversation => conversation.Id == selectedId);
        if (refreshedSelection is not null)
        {
            SelectedConversation = null;
            await SelectConversationAsync(refreshedSelection).ConfigureAwait(false);
        }
    }

    [RelayCommand]
    public Task LoadMoreConversationsAsync()
        => LoadConversationsAsync();

    [RelayCommand]
    public Task LoadMoreMessagesAsync()
        => LoadMessagesAsync();

    [RelayCommand]
    public Task LoadMoreFollowersAsync()
        => LoadFollowersAsync();

    [RelayCommand]
    private Task StartConversationAsync()
        => StartConversationCoreAsync(Recipient);

    public Task StartConversationForFollowerAsync(ChatRecipient follower)
        => StartConversationCoreAsync(follower?.Did);

    public void CloseConversation()
        => SelectedConversation = null;

    public void ReportFailure(Exception exception)
        => SetErrored(exception);

    private async Task StartConversationCoreAsync(string recipient)
    {
        recipient = recipient?.Trim();
        if (string.IsNullOrWhiteSpace(recipient) || !await conversationsSemaphore.WaitAsync(0))
            return;

        try
        {
            IsStartingConversation = true;
            ClearError();

            var started = await chatService.StartDirectConversationAsync(recipient).ConfigureAwait(false);
            ChatConversationViewModel conversation = null;
            await dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                conversation = Conversations.FirstOrDefault(item => item.Id == started.Id);
                if (conversation is not null)
                    return;

                conversation = new ChatConversationViewModel(started);
                conversationIds.Add(conversation.Id);
                Conversations.Insert(0, conversation);
                IsConversationListEmpty = false;
            });

            await dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => Recipient = string.Empty);
            await SelectConversationAsync(conversation).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            SetErrored(exception);
        }
        finally
        {
            IsStartingConversation = false;
            conversationsSemaphore.Release();
        }
    }

    private async Task LoadFollowersAsync()
    {
        if (followersExhausted || !await followersSemaphore.WaitAsync(0))
            return;

        try
        {
            var page = await chatService.FetchFollowersAsync(followersCursor, PageSize).ConfigureAwait(false);
            followersCursor = page.Cursor;
            followersExhausted = string.IsNullOrWhiteSpace(followersCursor);

            var followers = page.Recipients
                .Where(follower => !string.IsNullOrWhiteSpace(follower.Did) && followerDids.Add(follower.Did))
                .ToList();
            if (followers.Count > 0)
                await dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    foreach (var follower in followers)
                        Followers.Add(follower);
                });
        }
        catch (Exception exception)
        {
            SetErrored(exception);
        }
        finally
        {
            IsFollowersEmpty = Followers.Count == 0;
            OnPropertyChanged(nameof(HasMoreFollowers));
            followersSemaphore.Release();
        }
    }

    [RelayCommand]
    private async Task SendAsync()
    {
        var conversation = SelectedConversation;
        var text = Draft?.Trim();
        var currentUserDid = CurrentUserDid;
        if (conversation is null || string.IsNullOrWhiteSpace(text) || !await messagesSemaphore.WaitAsync(0))
            return;

        try
        {
            IsSending = true;
            SendError = null;
            await DiagnosticLog.WriteAsync($"Sending message to conversation {conversation.Id}.");

            var sent = await chatService.SendTextMessageAsync(conversation.Id, text).ConfigureAwait(false);
            await DiagnosticLog.WriteAsync($"Message sent to conversation {conversation.Id}; updating the UI.");
            var message = new ChatMessageViewModel(sent, currentUserDid);
            await dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                if (SelectedConversation?.Id != conversation.Id)
                    return;

                if (messageIds.Add(message.Id))
                    Messages.Add(message);

                conversation.UpdateLastMessage(sent);
                if (string.Equals(Draft?.Trim(), text, StringComparison.Ordinal))
                    Draft = string.Empty;
            });
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to send a message to conversation {ConversationId}", conversation.Id);
            await DiagnosticLog.WriteExceptionAsync($"Sending message to conversation {conversation.Id} failed", exception);
            await dispatcher.RunAsync(CoreDispatcherPriority.Normal,
                () => SendError = "Message could not be sent. Please try again.");
        }
        finally
        {
            await dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => IsSending = false);
            messagesSemaphore.Release();
        }
    }

    public async Task SelectConversationAsync(ChatConversationViewModel conversation)
    {
        if (conversation is null || SelectedConversation?.Id == conversation.Id)
            return;

        SelectedConversation = conversation;
        messagesCursor = null;
        messagesExhausted = false;
        isFirstMessagesPage = true;
        messageIds.Clear();
        IsMessagesEmpty = false;
        OnPropertyChanged(nameof(HasMoreMessages));

        await dispatcher.RunAsync(CoreDispatcherPriority.Normal, Messages.Clear);
        await LoadMessagesAsync().ConfigureAwait(false);
    }

    private async Task RefreshConversationsAsync()
    {
        if (!await conversationsSemaphore.WaitAsync(0))
            return;

        try
        {
            conversationsCursor = null;
            conversationsExhausted = false;
            conversationIds.Clear();
            IsConversationListEmpty = false;
            await dispatcher.RunAsync(CoreDispatcherPriority.Normal, Conversations.Clear);
            await LoadConversationsCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            conversationsSemaphore.Release();
        }
    }

    private async Task LoadConversationsAsync()
    {
        if (conversationsExhausted || !await conversationsSemaphore.WaitAsync(0))
            return;

        try
        {
            await LoadConversationsCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            conversationsSemaphore.Release();
        }
    }

    private async Task LoadConversationsCoreAsync()
    {
        using var loading = GetLoadingContext();
        ClearError();

        try
        {
            var added = 0;
            for (var attempt = 0; !conversationsExhausted && added == 0 && attempt < MaxPageAttempts; attempt++)
            {
                var page = await chatService.FetchConversationsAsync(conversationsCursor, PageSize).ConfigureAwait(false);
                conversationsCursor = page.Cursor;
                conversationsExhausted = string.IsNullOrWhiteSpace(conversationsCursor);

                var pageItems = page.Conversations
                    .Where(conversation => !string.IsNullOrWhiteSpace(conversation.Id) && conversationIds.Add(conversation.Id))
                    .Select(conversation => new ChatConversationViewModel(conversation))
                    .ToList();

                added += pageItems.Count;
                if (pageItems.Count > 0)
                    await dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                    {
                        foreach (var item in pageItems)
                            Conversations.Add(item);
                    });
            }
        }
        catch (Exception exception)
        {
            SetErrored(exception);
        }
        finally
        {
            IsConversationListEmpty = Conversations.Count == 0;
            OnPropertyChanged(nameof(HasMoreConversations));
        }
    }

    private async Task LoadMessagesAsync()
    {
        var conversation = SelectedConversation;
        if (conversation is null || messagesExhausted || !await messagesSemaphore.WaitAsync(0))
            return;

        try
        {
            using var loading = GetLoadingContext();
            ClearError();

            var added = 0;
            for (var attempt = 0; !messagesExhausted && added == 0 && attempt < MaxPageAttempts; attempt++)
            {
                var page = await chatService.FetchMessagesAsync(conversation.Id, messagesCursor, PageSize).ConfigureAwait(false);
                if (SelectedConversation?.Id != conversation.Id)
                    return;

                messagesCursor = page.Cursor;
                messagesExhausted = string.IsNullOrWhiteSpace(messagesCursor);

                var pageItems = page.Messages
                    .Where(message => !string.IsNullOrWhiteSpace(message.Id) && messageIds.Add(message.Id))
                    .Select(message => new ChatMessageViewModel(message, CurrentUserDid))
                    .OrderBy(message => message.SentAt)
                    .ToList();

                added += pageItems.Count;
                if (pageItems.Count > 0)
                {
                    var insertAtBeginning = !isFirstMessagesPage;
                    await dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                    {
                        if (insertAtBeginning)
                        {
                            for (var i = pageItems.Count - 1; i >= 0; i--)
                                Messages.Insert(0, pageItems[i]);
                        }
                        else
                        {
                            foreach (var item in pageItems)
                                Messages.Add(item);
                        }
                    });
                }

                isFirstMessagesPage = false;
            }

            IsMessagesEmpty = Messages.Count == 0;
            if (conversation.UnreadCount > 0)
                await MarkConversationReadAsync(conversation).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            SetErrored(exception);
        }
        finally
        {
            OnPropertyChanged(nameof(HasMoreMessages));
            messagesSemaphore.Release();
        }
    }

    private async Task MarkConversationReadAsync(ChatConversationViewModel conversation)
    {
        try
        {
            await chatService.MarkReadAsync(conversation.Id).ConfigureAwait(false);
            if (SelectedConversation?.Id == conversation.Id)
                await dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => conversation.UnreadCount = 0);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to mark conversation {ConversationId} as read", conversation.Id);
        }
    }

    private string CurrentUserDid => protocolService.Protocol.Session?.Did?.ToString();

    private void ClearError()
        => syncContext.Post(_ => Error = null, null);
}
