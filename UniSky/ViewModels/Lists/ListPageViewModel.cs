using System;
using CommunityToolkit.Mvvm.ComponentModel;
using FishyFlip.Lexicon.App.Bsky.Graph;
using FishyFlip.Models;
using UniSky.Services.Navigation;

namespace UniSky.ViewModels.Lists;

public partial class ListPageViewModel : ViewModelBase
{
    [ObservableProperty]
    private UserListViewModel list;
    [ObservableProperty]
    private string name = string.Empty;
    [ObservableProperty]
    private string description = string.Empty;

    public ListMemberCollection Members { get; }

    public ListPageViewModel(INavigationContext navigation, ATUri uri, ListView list = null)
        : base(navigation)
    {
        List = list is null ? null : new UserListViewModel(navigation, list);
        if (List is not null)
        {
            Name = List.Name;
            Description = List.Description;
        }
        Members = new ListMemberCollection(this, uri);
    }

    internal void Populate(ListView list)
    {
        if (List is null)
            List = new UserListViewModel(Navigation, list);
        else
            List.Populate(list);

        Name = List.Name;
        Description = List.Description;
    }

    internal void OnLoadError(Exception exception)
        => SetErrored(exception);

    internal void OnLoadError(ATError error)
        => SetErrored(error);
}
