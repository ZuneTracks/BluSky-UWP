using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using UniSky.Navigation;
using UniSky.Helpers;
using UniSky.Services;
using UniSky.Services.Navigation;
using UniSky.ViewModels.Chats;
using Windows.Foundation;
using Windows.Foundation.Metadata;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Navigation;

using MUXC = Microsoft.UI.Xaml.Controls;

namespace UniSky.Pages;

public sealed partial class ChatsPage : Page, IScrollToTop
{
    private const double WideLayoutMinimumWidth = 1200;

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

        var showConversationOnly = ActualWidth < WideLayoutMinimumWidth &&
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
}
