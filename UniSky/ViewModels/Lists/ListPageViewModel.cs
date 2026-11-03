using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using FishyFlip.Lexicon.App.Bsky.Actor;
using FishyFlip.Lexicon.App.Bsky.Graph;
using FishyFlip.Lexicon;
using FishyFlip.Models;
using Microsoft.Extensions.DependencyInjection;
using UniSky.Controls.Lists;
using UniSky.Services;
using UniSky.Services.Navigation;

namespace UniSky.ViewModels.Lists;

public partial class ListPageViewModel : ViewModelBase
{
    private readonly ATUri uri;

    [ObservableProperty]
    private UserListViewModel list;
    [ObservableProperty]
    private string name = string.Empty;
    [ObservableProperty]
    private string description = string.Empty;
    [ObservableProperty]
    private bool isOwner;

    public ListMemberCollection Members { get; }

    public ListPageViewModel(INavigationContext navigation, ATUri uri, ListView list = null)
        : base(navigation)
    {
        this.uri = uri;
        if (list is not null)
            Populate(list);

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
        IsOwner = ListActions.IsOwnedBySession(list);
    }

    internal ListMemberViewModel CreateMember(ATObject profile, ATUri itemUri)
        => new(Navigation, profile, itemUri, IsOwner, RemoveMemberAsync);

    public async Task EditAsync(string newName, string newDescription)
    {
        using var loading = GetLoadingContext();
        await ListActions.UpdateAsync(uri, newName, newDescription);
        Name = newName.Trim();
        Description = newDescription?.Trim() ?? string.Empty;
    }

    public async Task DeleteAsync()
    {
        using (GetLoadingContext())
            await ListActions.DeleteAsync(uri);

        if (Navigation?.CanGoBack == true)
            Navigation.GoBack();
    }

    public async Task<IReadOnlyList<ActorSuggestion>> SearchPeopleAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        var protocol = ServiceContainer.Scoped.GetRequiredService<IProtocolService>().Protocol;
        var result = await protocol.Actor.SearchActorsTypeaheadAsync(query.Trim(), 8);
        if (!result.IsT0 || result.AsT0?.Actors is not { } actors)
            return [];

        return actors
            .Where(a => a?.Did != null)
            .Select(a => new ActorSuggestion(a, Members.ContainsMember(a.Did)))
            .ToList();
    }

    /// <returns><see langword="false"/> if the person was already in the list.</returns>
    public async Task<bool> AddMemberAsync(ActorSuggestion suggestion)
    {
        if (Members.ContainsMember(suggestion.Profile.Did))
            return false;

        using var loading = GetLoadingContext();
        var itemUri = await ListActions.AddMemberAsync(uri, suggestion.Profile.Did);
        Members.InsertMember(CreateMember(suggestion.Profile, itemUri));
        return true;
    }

    private async Task RemoveMemberAsync(ListMemberViewModel member)
    {
        if (member.ItemUri == null || !member.CanRemove)
            return;

        member.CanRemove = false;
        try
        {
            await ListActions.RemoveMemberAsync(member.ItemUri);
            Members.RemoveMember(member);
        }
        catch (Exception ex)
        {
            member.CanRemove = true;
            await ListDialogs.ShowErrorAsync("Couldn't remove from list", ex);
        }
    }

    internal void OnLoadError(Exception exception)
        => SetErrored(exception);

    internal void OnLoadError(ATError error)
        => SetErrored(error);
}

public sealed class ActorSuggestion(ProfileViewBasic profile, bool isMember)
{
    public ProfileViewBasic Profile { get; } = profile;

    public string DisplayName
        => string.IsNullOrWhiteSpace(Profile.DisplayName) ? Handle : Profile.DisplayName;

    public string Handle
        => "@" + Profile.Handle;

    public string AvatarUrl
        => string.IsNullOrWhiteSpace(Profile.Avatar) ? "ms-appx:///Assets/Default/Avatar.png" : Profile.Avatar;

    public string Status
        => isMember ? "Already in this list" : Handle;

    public override string ToString()
        => Handle;
}
