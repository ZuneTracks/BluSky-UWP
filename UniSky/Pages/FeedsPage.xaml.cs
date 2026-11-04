using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Toolkit.Uwp.UI.Extensions;
using UniSky.Navigation;
using UniSky.Services;
using UniSky.Services.Navigation;
using UniSky.ViewModels;
using UniSky.ViewModels.Feeds;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Navigation;

using MUXC = Microsoft.UI.Xaml.Controls;

namespace UniSky.Pages;

public sealed partial class FeedsPage : Page, IScrollToTop
{
    private static readonly TimeSpan AutoRefreshInterval = TimeSpan.FromMinutes(2);

    private readonly DispatcherTimer autoRefreshTimer = new DispatcherTimer() { Interval = AutoRefreshInterval };
    private readonly ITypedSettings settings = ServiceContainer.Default.GetRequiredService<ITypedSettings>();
    private NavigationRoute currentRoute;
    private bool isActive;
    private bool isCheckingForNewPosts;
    private DateTimeOffset lastCheck = DateTimeOffset.MinValue;

    public FeedsViewModel ViewModel
    {
        get => (FeedsViewModel)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register("ViewModel", typeof(FeedsViewModel), typeof(FeedsPage), new PropertyMetadata(null));

    public FeedsPage()
    {
        this.InitializeComponent();
        this.autoRefreshTimer.Tick += OnAutoRefreshTimerTick;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        var request = e.Parameter as NavigationRequest;
        var route = request?.Route;
        if (this.ViewModel == null || !Equals(currentRoute, route))
        {
            currentRoute = route;
            var scope = NavigationScopeHost.FindFor(this.Frame);
            if (route?.Kind == RouteKinds.Feed && route.TryToAtUri(out var uri))
            {
                this.ViewModel = ActivatorUtilities.CreateInstance<FeedsViewModel>(
                    ServiceContainer.Scoped, scope, uri, request.Payload as FishyFlip.Lexicon.App.Bsky.Feed.GeneratorView);
            }
            else
            {
                this.ViewModel = ActivatorUtilities.CreateInstance<FeedsViewModel>(ServiceContainer.Scoped, scope);
            }

            // The frame reuses one cached instance of this page for Home and every feed route, and
            // x:Bind defaults to OneTime, so rebind or the pivot keeps showing the previous feed.
            this.Bindings.Update();
        }

        var safeAreaService = ServiceContainer.Scoped.GetRequiredService<ISafeAreaService>();
        safeAreaService.SafeAreaUpdated += OnSafeAreaUpdated;

        isActive = true;
        settings.SettingChanged += OnSettingChanged;
        Window.Current.VisibilityChanged += OnWindowVisibilityChanged;
        UpdateAutoRefreshTimer();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ServiceContainer.Scoped.GetRequiredService<ISafeAreaService>().SafeAreaUpdated -= OnSafeAreaUpdated;

        isActive = false;
        settings.SettingChanged -= OnSettingChanged;
        Window.Current.VisibilityChanged -= OnWindowVisibilityChanged;
        UpdateAutoRefreshTimer();
    }

    private void OnSettingChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ITypedSettings.AutoRefreshFeeds))
            return;

        _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, UpdateAutoRefreshTimer);
    }

    private void OnWindowVisibilityChanged(object sender, VisibilityChangedEventArgs e)
    {
        UpdateAutoRefreshTimer();

        if (e.Visible && autoRefreshTimer.IsEnabled && DateTimeOffset.Now - lastCheck >= AutoRefreshInterval)
            _ = CheckForNewPostsAsync();
    }

    private void UpdateAutoRefreshTimer()
    {
        var shouldRun = isActive && settings.AutoRefreshFeeds && Window.Current.Visible;
        if (shouldRun && !autoRefreshTimer.IsEnabled)
            autoRefreshTimer.Start();
        else if (!shouldRun && autoRefreshTimer.IsEnabled)
            autoRefreshTimer.Stop();
    }

    private async void OnAutoRefreshTimerTick(object sender, object e)
    {
        await CheckForNewPostsAsync();
    }

    private async Task CheckForNewPostsAsync()
    {
        if (isCheckingForNewPosts || FeedsPivot.SelectedItem is not FeedViewModel feed)
            return;

        isCheckingForNewPosts = true;
        lastCheck = DateTimeOffset.Now;

        try
        {
            if (!await feed.CheckForNewPostsAsync() || !isActive)
                return;

            // Only reload in place when the user is at the top, so we never move their reading position.
            var scrollViewer = GetFeedScrollViewer(feed);
            if (scrollViewer != null && scrollViewer.VerticalOffset < 1)
                await feed.RefreshAsync();
        }
        finally
        {
            isCheckingForNewPosts = false;
        }
    }

    private async void NewPostsButton_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not FeedViewModel feed)
            return;

        await feed.RefreshAsync();
        GetFeedScrollViewer(feed)?.ChangeView(0, 0, null, true);
    }

    private ScrollViewer GetFeedScrollViewer(FeedViewModel feed)
    {
        var container = FeedsPivot.ContainerFromItem(feed) as FrameworkElement;
        var list = container?.FindDescendantByName("PART_FeedList") as ListView;
        return list?.FindDescendant<ScrollViewer>();
    }

    private void OnSafeAreaUpdated(object sender, SafeAreaUpdatedEventArgs e)
    {
        var themeService = ServiceContainer.Scoped.GetRequiredService<IThemeService>();
        if (themeService.GetTheme() == AppTheme.SunValley)
            RootGrid.Padding = new Thickness(0);
        else
            RootGrid.Padding = new Thickness(0, e.SafeArea.Bounds.Top, 0, 0);
    }

    private async void OnRefreshRequested(MUXC.RefreshContainer sender, MUXC.RefreshRequestedEventArgs args)
    {
        var feed = (FeedViewModel)sender.DataContext;

        var deferral = args.GetDeferral();
        await feed.RefreshAsync(deferral);
    }

    private void PivotHeaderText_Tapped(object sender, TappedRoutedEventArgs e)
    {
        ScrollToTop();
    }
    
    private void FeedList_Loaded(object sender, RoutedEventArgs e)
    {
        var list = (ListView)sender;

        _ = ConnectedAnimations.TryLandBackAsync(ConnectedAnimations.ThreadPost, list, "PrimaryContent");
        _ = ConnectedAnimations.TryLandBackAsync(ConnectedAnimations.ProfileAvatar, list, "ProfileEllipse");
    }

    private async void RefreshAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (FeedsPivot.SelectedItem is not FeedViewModel feedVm)
            return;

        await feedVm.RefreshAsync();
    }

    public void ScrollToTop()
    {
        var feedsList = FeedsPivot.ContainerFromItem(FeedsPivot.SelectedItem)
            .FindDescendantByName("PART_FeedList");

        if (feedsList is not ListView lv)
            return;

        var scrollView = lv.FindDescendant<ScrollViewer>();
        if (scrollView is null)
            return;

        scrollView.ChangeView(0, 0, null);
    }
}
