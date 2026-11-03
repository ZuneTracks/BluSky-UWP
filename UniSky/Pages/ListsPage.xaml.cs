using System;
using Microsoft.Extensions.DependencyInjection;
using UniSky.Controls.Lists;
using UniSky.Navigation;
using UniSky.Services;
using UniSky.Services.Navigation;
using UniSky.ViewModels.Lists;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace UniSky.Pages;

public sealed partial class ListsPage : Page
{
    public ListsPageViewModel ViewModel
    {
        get => (ListsPageViewModel)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(nameof(ViewModel), typeof(ListsPageViewModel), typeof(ListsPage), new PropertyMetadata(null));

    public ListsPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        var safeAreaService = ServiceContainer.Scoped.GetRequiredService<ISafeAreaService>();
        safeAreaService.SetTitlebarTheme(ElementTheme.Default);
        safeAreaService.SafeAreaUpdated += OnSafeAreaUpdated;

        if (ViewModel == null)
            DataContext = ViewModel = ActivatorUtilities.CreateInstance<ListsPageViewModel>(
                ServiceContainer.Scoped,
                NavigationScopeHost.FindFor(Frame));
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

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.Lists.RefreshAsync();
    }

    private async void NewListButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new EditListDialog(true);
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;

        try
        {
            await ViewModel.CreateListAsync(dialog.Purpose, dialog.ListName, dialog.ListDescription);
        }
        catch (Exception ex)
        {
            await ListDialogs.ShowErrorAsync("Couldn't create list", ex);
        }
    }
}
