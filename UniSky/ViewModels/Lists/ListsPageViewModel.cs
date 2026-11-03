using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using FishyFlip.Models;
using UniSky.Services.Navigation;

namespace UniSky.ViewModels.Lists;

public partial class ListsPageViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool isEmpty;

    public ListsCollection Lists { get; }

    public ListsPageViewModel(INavigationContext navigation)
        : base(navigation)
    {
        Lists = new ListsCollection(this);
    }

    internal void OnLoadError(Exception exception)
        => SetErrored(exception);

    public async Task CreateListAsync(string purpose, string name, string description)
    {
        using (GetLoadingContext())
            await ListActions.CreateAsync(purpose, name, description);

        // The AppView indexes new records asynchronously; give it a moment before reloading.
        await Task.Delay(750);
        await Lists.RefreshAsync();
    }

    internal void OnLoadError(ATError error)
        => SetErrored(error);
}
