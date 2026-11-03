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

    private async void EditListButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel == null)
            return;

        var dialog = new EditListDialog(false, ViewModel.Name, ViewModel.Description);
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;

        try
        {
            await ViewModel.EditAsync(dialog.ListName, dialog.ListDescription);
        }
        catch (Exception ex)
        {
            await ListDialogs.ShowErrorAsync("Couldn't save list", ex);
        }
    }

    private async void DeleteListButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel == null)
            return;

        if (!await ListDialogs.ConfirmAsync("Delete list?",
            $"\"{ViewModel.Name}\" will be permanently deleted. This can't be undone.",
            "Delete"))
            return;

        try
        {
            await ViewModel.DeleteAsync();
        }
        catch (Exception ex)
        {
            await ListDialogs.ShowErrorAsync("Couldn't delete list", ex);
        }
    }

    private async void AddMemberBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput || ViewModel == null)
            return;

        var query = sender.Text;
        var results = await ViewModel.SearchPeopleAsync(query);
        if (sender.Text == query)
            sender.ItemsSource = results;
    }

    private void AddMemberBox_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        // Keep the typed text; adding happens in QuerySubmitted.
    }

    private async void AddMemberBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (ViewModel == null || args.ChosenSuggestion is not ActorSuggestion suggestion)
            return;

        try
        {
            if (await ViewModel.AddMemberAsync(suggestion))
            {
                sender.Text = string.Empty;
                sender.ItemsSource = null;
            }
        }
        catch (Exception ex)
        {
            await ListDialogs.ShowErrorAsync("Couldn't add to list", ex);
        }
    }
}
