using System;
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

    internal void OnLoadError(ATError error)
        => SetErrored(error);
}
