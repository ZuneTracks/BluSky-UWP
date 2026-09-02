using Microsoft.Extensions.DependencyInjection;
using UniSky.Services;
using UniSky.Navigation;
using UniSky.ViewModels;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace UniSky.Pages;

public sealed partial class FeedsListPage : Page
{
    public FeedsListViewModel ViewModel
    {
        get => (FeedsListViewModel)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(nameof(ViewModel), typeof(FeedsListViewModel), typeof(FeedsListPage), new PropertyMetadata(null));

    public FeedsListPage()
    {
        this.InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (ViewModel == null)
            DataContext = ViewModel = ActivatorUtilities.CreateInstance<FeedsListViewModel>(
                ServiceContainer.Scoped, NavigationScopeHost.FindFor(Frame));

        var safeAreaService = ServiceContainer.Scoped.GetRequiredService<ISafeAreaService>();
        safeAreaService.SetTitlebarTheme(ElementTheme.Default);
        safeAreaService.SafeAreaUpdated += OnSafeAreaUpdated;
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);

        var safeAreaService = ServiceContainer.Scoped.GetRequiredService<ISafeAreaService>();
        safeAreaService.SafeAreaUpdated -= OnSafeAreaUpdated;
    }

    private void OnSafeAreaUpdated(object sender, SafeAreaUpdatedEventArgs e)
    {
        var themeService = ServiceContainer.Scoped.GetRequiredService<IThemeService>();
        if (themeService.GetTheme() == AppTheme.SunValley)
            TitleBarPadding.Height = new GridLength(0);
        else
            TitleBarPadding.Height = new GridLength(e.SafeArea.Bounds.Top);
    }
}
