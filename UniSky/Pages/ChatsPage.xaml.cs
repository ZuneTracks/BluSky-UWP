using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using UniSky.Controls.Lists;
using UniSky.Controls.Moderation;
using UniSky.Navigation;
using UniSky.Helpers;
using UniSky.Services;
using UniSky.Services.Navigation;
using UniSky.ViewModels.Chats;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Foundation.Metadata;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Navigation;

using MUXC = Microsoft.UI.Xaml.Controls;

namespace UniSky.Pages;

public sealed partial class ChatsPage : Page, IScrollToTop, IRefreshOnShow
{
    // measured against the page rather than the window so the navigation pane doesn't skew it
    private const double WideLayoutMinimumWidth = 720;

    public ChatsPageViewModel ViewModel
    {
        get => (ChatsPageViewModel)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(nameof(ViewModel), typeof(ChatsPageViewModel), typeof(ChatsPage), new PropertyMetadata(null));

    public ChatsPage()
    {
        InitializeComponent();
        SizeChanged += OnSizeChanged;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        var safeAreaService = ServiceContainer.Scoped.GetRequiredService<ISafeAreaService>();
        safeAreaService.SetTitlebarTheme(ElementTheme.Default);
        safeAreaService.SafeAreaUpdated += OnSafeAreaUpdated;

        if (ViewModel is null)
            DataContext = ViewModel = ActivatorUtilities.CreateInstance<ChatsPageViewModel>(
                ServiceContainer.Scoped, NavigationScopeHost.FindFor(Frame));

        UpdateChatLayout();
        _ = LoadChatsAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ServiceContainer.Scoped.GetRequiredService<ISafeAreaService>().SafeAreaUpdated -= OnSafeAreaUpdated;
    }

    public void ScrollToTop()
    {
        if (ConversationsList?.Items.Count > 0)
            ConversationsList.ScrollIntoView(ConversationsList.Items[0]);
    }

    private void OnSafeAreaUpdated(object sender, SafeAreaUpdatedEventArgs e)
    {
        var themeService = ServiceContainer.Scoped.GetRequiredService<IThemeService>();
        TitleBarPadding.Height = themeService.GetTheme() == AppTheme.SunValley
            ? new GridLength(0)
            : new GridLength(e.SafeArea.Bounds.Top);
    }

    private async void ConversationsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0 && e.AddedItems[0] is ChatConversationViewModel conversation)
        {
            var selection = ViewModel.SelectConversationAsync(conversation);
            UpdateChatLayout();
            await selection;
        }
    }

    private async void OnRefreshRequested(MUXC.RefreshContainer sender, MUXC.RefreshRequestedEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            await ViewModel.RefreshAsync();
        }
        finally
        {
            deferral.Complete();
        }
    }

    private async void RefreshAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await ViewModel.RefreshAsync();
    }

    private async void MessageTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter ||
            !Windows.UI.Core.CoreWindow.GetForCurrentThread().GetKeyState(Windows.System.VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down))
        {
            return;
        }

        e.Handled = true;
        await ViewModel.SendCommand.ExecuteAsync(null);
    }

    private async void LoadMoreConversations_Click(object sender, RoutedEventArgs e)
        => await ViewModel.LoadMoreConversationsAsync();

    private async void LoadMoreMessages_Click(object sender, RoutedEventArgs e)
        => await ViewModel.LoadMoreMessagesAsync();

    private async void FollowersList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0 && e.AddedItems[0] is ChatRecipient follower)
        {
            await ViewModel.StartConversationForFollowerAsync(follower);
            UpdateChatLayout();
        }

        if (FollowersList is not null)
            FollowersList.SelectedItem = null;
    }

    private async void LoadMoreFollowers_Click(object sender, RoutedEventArgs e)
        => await ViewModel.LoadMoreFollowersAsync();

    private void ChatsTab_Click(object sender, RoutedEventArgs e)
        => SetInbox(showRequests: false);

    private void RequestsTab_Click(object sender, RoutedEventArgs e)
        => SetInbox(showRequests: true);

    private void SetInbox(bool showRequests)
    {
        ConversationsList.SelectedItem = null;
        ViewModel.ShowRequests = showRequests;

        // clicking the active tab would otherwise uncheck it
        ChatsTabButton.IsChecked = !showRequests;
        RequestsTabButton.IsChecked = showRequests;
        UpdateChatLayout();
    }

    private void ViewProfile_Click(object sender, RoutedEventArgs e)
        => ViewModel.ViewProfile();

    private async void ToggleMute_Click(object sender, RoutedEventArgs e)
        => await RunActionAsync("Couldn't update mute setting", ViewModel.ToggleMuteAsync);

    private async void AcceptRequest_Click(object sender, RoutedEventArgs e)
        => await RunActionAsync("Couldn't accept request", ViewModel.AcceptRequestAsync);

    private async void LeaveConversation_Click(object sender, RoutedEventArgs e)
    {
        var conversation = ViewModel.SelectedConversation;
        if (conversation is null)
            return;

        var isRequest = conversation.IsRequest;
        if (!await ListDialogs.ConfirmAsync(
            isRequest ? "Delete request?" : "Leave conversation?",
            "You won't see this conversation anymore. The other person will still be able to see it.",
            isRequest ? "Delete" : "Leave"))
            return;

        ConversationsList.SelectedItem = null;
        await RunActionAsync("Couldn't leave conversation", ViewModel.LeaveConversationAsync);
        UpdateChatLayout();
    }

    private async void BlockAccount_Click(object sender, RoutedEventArgs e)
    {
        var conversation = ViewModel.SelectedConversation;
        if (conversation is null)
            return;

        var name = string.IsNullOrWhiteSpace(conversation.Handle) ? conversation.DisplayName : "@" + conversation.Handle.TrimStart('@');
        if (!await ListDialogs.ConfirmAsync(
            $"Block {name}?",
            "Blocked accounts can't message you, reply to your posts, mention you, or interact with you.",
            "Block"))
            return;

        await RunActionAsync("Couldn't block account", ViewModel.BlockAsync);
        UpdateChatLayout();
    }

    private async void ReactMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: ChatMessageViewModel message } item)
            await RunActionAsync("Couldn't add reaction", () => ViewModel.ToggleReactionAsync(message, item.Text));
    }

    private void CopyMessage_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem { Tag: ChatMessageViewModel message })
            return;

        var package = new DataPackage();
        package.SetText(message.Text ?? string.Empty);
        Clipboard.SetContent(package);
    }

    private async void DeleteMessage_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem { Tag: ChatMessageViewModel message })
            return;

        if (!await ListDialogs.ConfirmAsync(
            "Delete message?",
            "This message will be deleted for you. The other person will still be able to see it.",
            "Delete"))
            return;

        await RunActionAsync("Couldn't delete message", () => ViewModel.DeleteMessageForSelfAsync(message));
    }

    private async void ReportMessage_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem { Tag: ChatMessageViewModel message })
            return;

        var report = await ReportPostDialog.ShowAsync("message");
        if (report is null)
            return;

        try
        {
            await ViewModel.ReportMessageAsync(message, report.ReasonType, report.Details);
            await ReportPostDialog.ShowSubmittedAsync();
        }
        catch (System.Exception exception)
        {
            await ListDialogs.ShowErrorAsync("Couldn't send report", exception);
        }
    }

    private static async Task RunActionAsync(string errorTitle, System.Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (System.Exception exception)
        {
            await ListDialogs.ShowErrorAsync(errorTitle, exception);
        }
    }

    private void BackToConversations_Click(object sender, RoutedEventArgs e)
    {
        ConversationsList.SelectedItem = null;
        ViewModel.CloseConversation();
        UpdateChatLayout();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
        => UpdateChatLayout();

    private void UpdateChatLayout()
    {
        if (InboxPane is null)
            return;

        var isWide = ActualWidth >= WideLayoutMinimumWidth;
        VisualStateManager.GoToState(this, isWide ? "Wide" : "Small", false);

        var showConversationOnly = !isWide &&
            ViewModel?.HasSelectedConversation == true;
        InboxPane.Visibility = showConversationOnly ? Visibility.Collapsed : Visibility.Visible;
    }

    private async Task LoadChatsAsync()
    {
        try
        {
            await DiagnosticLog.WriteAsync("Chats page opened.");
            await ViewModel.EnsureLoadedAsync();
            await DiagnosticLog.WriteAsync("Chats page initial load completed.");
        }
        catch (System.Exception exception)
        {
            await DiagnosticLog.WriteExceptionAsync("Chats page initial load failed", exception);
            ViewModel.ReportFailure(exception);
        }
    }

    public async Task RefreshOnShowAsync()
    {
        try
        {
            if (ViewModel != null)
                await ViewModel.RefreshAsync();
        }
        catch { }
    }
}
