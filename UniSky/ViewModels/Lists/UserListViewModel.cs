using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FishyFlip.Lexicon.App.Bsky.Graph;
using UniSky.Moderation;
using UniSky.Navigation;
using UniSky.Services;
using UniSky.Services.Navigation;

namespace UniSky.ViewModels.Lists;

public partial class UserListViewModel : ViewModelBase
{
    private ListView view;

    [ObservableProperty]
    private string name;
    [ObservableProperty]
    private string description;
    [ObservableProperty]
    private string avatarUrl;
    [ObservableProperty]
    private string creatorName;
    [ObservableProperty]
    private string creatorHandle;

    public ListView View => view;

    public UserListViewModel(INavigationContext navigation, ListView view)
        : base(navigation)
    {
        Populate(view);
    }

    public void Populate(ListView value)
    {
        view = value;
        Name = value.Name ?? string.Empty;
        Description = value.Description ?? string.Empty;
        AvatarUrl = value.Avatar ?? string.Empty;
        CreatorName = value.Creator?.DisplayName ?? value.Creator?.Handle?.ToString() ?? string.Empty;
        CreatorHandle = value.Creator?.Handle?.ToString() ?? string.Empty;
    }

    [RelayCommand]
    private void OpenList()
        => Navigate(Routes.List(view?.Uri, view));

    public bool IsVisible(IModerationService moderationService)
        => view != null
            && !new Moderator(moderationService.ModerationOptions)
                .ModerateUserList(view)
                .GetUI(ModerationContext.ProfileList)
                .Filter;
}
