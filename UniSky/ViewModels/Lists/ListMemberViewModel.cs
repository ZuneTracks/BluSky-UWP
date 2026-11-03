using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FishyFlip.Lexicon;
using FishyFlip.Models;
using UniSky.Services.Navigation;
using UniSky.ViewModels.Profile;

namespace UniSky.ViewModels.Lists;

/// <summary>
/// A profile shown as a member of a list, keeping the list item record needed to remove it.
/// </summary>
public partial class ListMemberViewModel : ProfileViewModel
{
    private readonly Func<ListMemberViewModel, Task> remove;

    [ObservableProperty]
    private bool canRemove;

    public ListMemberViewModel(INavigationContext navigation, ATObject profile, ATUri itemUri, bool canRemove, Func<ListMemberViewModel, Task> remove)
        : base(navigation, profile)
    {
        ItemUri = itemUri;
        CanRemove = canRemove && itemUri != null;
        this.remove = remove;
    }

    public ATUri ItemUri { get; }

    [RelayCommand]
    private Task RemoveAsync()
        => remove?.Invoke(this) ?? Task.CompletedTask;
}
