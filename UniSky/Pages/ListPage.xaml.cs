using Microsoft.Extensions.DependencyInjection;
using UniSky.Navigation;
using UniSky.Services;
using UniSky.Services.Navigation;
using UniSky.ViewModels.Lists;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using GraphListView = FishyFlip.Lexicon.App.Bsky.Graph.ListView;

namespace UniSky.Pages;

public sealed partial class ListPage : Page
{
    public ListPageViewModel ViewModel
    {
        get => (ListPageViewModel)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(nameof(ViewModel), typeof(ListPageViewModel), typeof(ListPage), new PropertyMetadata(null));

    public ListPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        var safeAreaService = ServiceContainer.Scoped.GetRequiredService<ISafeAreaService>();
        safeAreaService.SetTitlebarTheme(ElementTheme.Default);
        safeAreaService.SafeAreaUpdated += OnSafeAreaUpdated;

        if (e.Parameter is not NavigationRequest request || !request.Route.TryToAtUri(out var uri))
            return;

        if (ViewModel == null)
        {
            DataContext = ViewModel = ActivatorUtilities.CreateInstance<ListPageViewModel>(
                ServiceContainer.Scoped,
                NavigationScopeHost.FindFor(Frame),
                uri,
                request.Payload as GraphListView);
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ServiceContainer.Scoped.GetRequiredService<ISafeAreaService>().SafeAreaUpdated -= OnSafeAreaUpdated;
    }

    private void OnSafeAreaUpdated(object sender, SafeAreaUpdatedEventArgs e)
    {
        var themeService = ServiceContainer.Scoped.GetRequiredService<IThemeService>();
        TitleBarPadding.Height = themeService.GetTheme() == AppTheme.SunValley
            ? new GridLength(0)
            : new GridLength(e.SafeArea.Bounds.Top);
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (ViewModel?.Members is { HasMoreItems: true } members)
            _ = members.LoadMoreItemsAsync(25);
    }
}
