using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FishyFlip;
using FishyFlip.Lexicon;
using FishyFlip.Lexicon.App.Bsky.Actor;
using FishyFlip.Lexicon.App.Bsky.Feed;
using FishyFlip.Models;
using FishyFlip.Tools;
using Microsoft.Extensions.Logging;
using UniSky.Extensions;
using UniSky.Models.Feeds;
using UniSky.Navigation;
using UniSky.Services;
using UniSky.Services.Navigation;
using UniSky.ViewModels.Feeds;

namespace UniSky.ViewModels;

public partial class FeedsListViewModel : ViewModelBase
{
    private readonly IProtocolService protocolService;
    private readonly ILogger<FeedsListViewModel> logger;
    private readonly SemaphoreSlim preferenceLock = new(1, 1);
    private List<ATObject> preferences = [];

    [ObservableProperty]
    private bool hasSavedFeeds;

    [ObservableProperty]
    private string customFeedUri = string.Empty;

    public FeedsListViewModel(
        INavigationContext navigation,
        IProtocolService protocolService,
        ILogger<FeedsListViewModel> logger)
        : base(navigation)
    {
        this.protocolService = protocolService;
        this.logger = logger;

        _ = Task.Run(LoadAsync);
    }

    public ObservableCollection<FeedGeneratorViewModel> SavedFeeds { get; } = [];

    public ObservableCollection<FeedGeneratorViewModel> SuggestedFeeds { get; } = [];

    [RelayCommand]
    public async Task RefreshAsync()
    {
        Error = null!;
        await LoadAsync();
    }

    [RelayCommand]
    private void OpenFeed(FeedGeneratorViewModel feed)
    {
        if (feed != null)
            Navigate(Routes.Feed(feed.Uri, feed.Generator));
    }

    [RelayCommand]
    private async Task SaveFeedAsync(FeedGeneratorViewModel feed)
    {
        if (feed == null)
            return;

        await preferenceLock.WaitAsync();
        try
        {
            var updatedPreferences = SavedFeedPreferences.WithPinnedFeed(preferences, feed.Uri);
            (await protocolService.Protocol.PutPreferencesAsync(updatedPreferences)
                .ConfigureAwait(false))
                .HandleResult();

            preferences = updatedPreferences;
            syncContext.Post(() =>
            {
                if (!SavedFeeds.Any(existing => existing.Uri.ToString() == feed.Uri.ToString()))
                    SavedFeeds.Add(feed);

                SuggestedFeeds.Remove(feed);
                HasSavedFeeds = SavedFeeds.Count > 0;
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to save feed {FeedUri}", feed.Uri);
            SetErrored(ex);
        }
        finally
        {
            preferenceLock.Release();
        }
    }

    [RelayCommand]
    private async Task RemoveFeedAsync(FeedGeneratorViewModel feed)
    {
        if (feed == null)
            return;

        await preferenceLock.WaitAsync();
        try
        {
            var updatedPreferences = SavedFeedPreferences.WithoutFeed(preferences, feed.Uri);
            (await protocolService.Protocol.PutPreferencesAsync(updatedPreferences)
                .ConfigureAwait(false))
                .HandleResult();

            preferences = updatedPreferences;
            syncContext.Post(() =>
            {
                SavedFeeds.Remove(feed);
                if (!SuggestedFeeds.Any(existing => existing.Uri.ToString() == feed.Uri.ToString()))
                    SuggestedFeeds.Add(feed);

                HasSavedFeeds = SavedFeeds.Count > 0;
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to remove feed {FeedUri}", feed.Uri);
            SetErrored(ex);
        }
        finally
        {
            preferenceLock.Release();
        }
    }

    [RelayCommand]
    private async Task AddCustomFeedAsync()
    {
        if (!NavigationRoute.TryParse(CustomFeedUri, out var route)
            || route.Kind != RouteKinds.Feed
            || !route.TryToAtUri(out var uri))
        {
            SetErrored(new ArgumentException("Enter a Bluesky feed link or an at:// feed URI.", nameof(CustomFeedUri)));
            return;
        }

        try
        {
            var result = (await protocolService.Protocol.GetFeedGeneratorsAsync([uri])
                .ConfigureAwait(false))
                .HandleResult();
            var generator = result.Feeds?.FirstOrDefault(feed => feed.Uri.ToString() == uri.ToString());
            if (generator == null)
                throw new InvalidOperationException("The specified feed could not be found.");

            await SaveFeedAsync(new FeedGeneratorViewModel(generator));
            CustomFeedUri = string.Empty;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to add custom feed {FeedUri}", CustomFeedUri);
            SetErrored(ex);
        }
    }

    private async Task LoadAsync()
    {
        using var loading = GetLoadingContext();
        try
        {
            var protocol = protocolService.Protocol;
            var preferenceResult = (await protocol.GetPreferencesAsync()
                .ConfigureAwait(false))
                .HandleResult();
            preferences = preferenceResult.Preferences?.ToList() ?? [];

            var pinnedFeedUris = SavedFeedPreferences.GetPinnedFeedUris(preferences);
            var generators = pinnedFeedUris.Count == 0
                ? []
                : ((await protocol.GetFeedGeneratorsAsync(pinnedFeedUris.ToList())
                    .ConfigureAwait(false))
                    .HandleResult()
                    .Feeds ?? []);
            var suggestions = ((await protocol.GetSuggestedFeedsAsync(limit: 20)
                .ConfigureAwait(false))
                .HandleResult()
                .Feeds ?? []);

            var savedByUri = generators.ToDictionary(feed => feed.Uri.ToString(), StringComparer.Ordinal);
            var saved = pinnedFeedUris
                .Where(uri => savedByUri.ContainsKey(uri.ToString()))
                .Select(uri => new FeedGeneratorViewModel(savedByUri[uri.ToString()]))
                .ToList();
            var savedUris = new HashSet<string>(saved.Select(feed => feed.Uri.ToString()), StringComparer.Ordinal);
            var suggested = suggestions
                .Where(feed => !savedUris.Contains(feed.Uri.ToString()))
                .GroupBy(feed => feed.Uri.ToString(), StringComparer.Ordinal)
                .Select(group => new FeedGeneratorViewModel(group.First()))
                .ToList();

            syncContext.Post(() =>
            {
                SavedFeeds.Clear();
                foreach (var feed in saved)
                    SavedFeeds.Add(feed);

                SuggestedFeeds.Clear();
                foreach (var feed in suggested)
                    SuggestedFeeds.Add(feed);

                HasSavedFeeds = SavedFeeds.Count > 0;
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load saved and suggested feeds.");
            SetErrored(ex);
        }
    }
}
