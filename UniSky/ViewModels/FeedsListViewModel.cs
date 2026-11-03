using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using FishyFlip;
using FishyFlip.Lexicon;
using FishyFlip.Lexicon.App.Bsky.Actor;
using FishyFlip.Lexicon.App.Bsky.Feed;
using FishyFlip.Lexicon.App.Bsky.Unspecced;
using FishyFlip.Models;
using FishyFlip.Tools;
using Microsoft.Extensions.Logging;
using UniSky.Extensions;
using UniSky.Messages;
using UniSky.Models.Feeds;
using UniSky.Navigation;
using UniSky.Services;
using UniSky.Services.Navigation;
using UniSky.ViewModels.Feeds;

namespace UniSky.ViewModels;

public partial class FeedsListViewModel : ViewModelBase
{
    private const int PageSize = 25;

    private readonly IProtocolService protocolService;
    private readonly ILogger<FeedsListViewModel> logger;
    private readonly SemaphoreSlim preferenceLock = new(1, 1);
    private List<ATObject> preferences = [];
    private string discoverCursor;
    private string activeQuery;
    private int discoverVersion;

    [ObservableProperty]
    private bool hasSavedFeeds;

    [ObservableProperty]
    private string customFeedUri = string.Empty;

    [ObservableProperty]
    private string searchQuery = string.Empty;

    [ObservableProperty]
    private bool hasMoreFeeds;

    [ObservableProperty]
    private bool isLoadingMore;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DiscoverHeader))]
    private bool isSearching;

    [ObservableProperty]
    private bool hasNoResults;

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

    public string DiscoverHeader => IsSearching ? "SEARCH RESULTS" : "SUGGESTED FEEDS";

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
            await PutPreferencesAsync(updatedPreferences);

            syncContext.Post(() =>
            {
                var existing = FindSaved(feed.Uri);
                if (existing != null)
                    existing.IsPinned = true;
                else
                    SavedFeeds.Add(new FeedGeneratorViewModel(feed.Generator, isPinned: true));

                SuggestedFeeds.Remove(feed);
                UpdateEmptyStates();
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
    private async Task TogglePinAsync(FeedGeneratorViewModel feed)
    {
        if (feed == null)
            return;

        await preferenceLock.WaitAsync();
        try
        {
            var pinned = !feed.IsPinned;
            var updatedPreferences = SavedFeedPreferences.WithFeedPinned(preferences, feed.Uri, pinned);
            await PutPreferencesAsync(updatedPreferences);

            syncContext.Post(() => feed.IsPinned = pinned);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to change pin state of feed {FeedUri}", feed.Uri);
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
            await PutPreferencesAsync(updatedPreferences);

            syncContext.Post(() =>
            {
                SavedFeeds.Remove(feed);
                if (!IsSearching && !SuggestedFeeds.Any(existing => SameUri(existing.Uri, feed.Uri)))
                    SuggestedFeeds.Insert(0, new FeedGeneratorViewModel(feed.Generator));

                UpdateEmptyStates();
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
            var generator = result.Feeds?.FirstOrDefault(feed => SameUri(feed.Uri, uri));
            if (generator == null)
                throw new InvalidOperationException("The specified feed could not be found.");

            await SaveFeedAsync(new FeedGeneratorViewModel(generator));
            syncContext.Post(() => CustomFeedUri = string.Empty);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to add custom feed {FeedUri}", CustomFeedUri);
            SetErrored(ex);
        }
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        var query = SearchQuery?.Trim();
        activeQuery = string.IsNullOrEmpty(query) ? null : query;
        IsSearching = activeQuery != null;
        await LoadDiscoverPageAsync(reset: true);
    }

    [RelayCommand]
    private async Task ClearSearchAsync()
    {
        SearchQuery = string.Empty;
        await SearchAsync();
    }

    [RelayCommand]
    private async Task LoadMoreAsync()
    {
        if (!HasMoreFeeds || IsLoadingMore)
            return;

        await LoadDiscoverPageAsync(reset: false);
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

            var savedEntries = SavedFeedPreferences.GetSavedFeeds(preferences);
            var generators = new List<GeneratorView>();
            // getFeedGenerators accepts a limited number of URIs per request.
            foreach (var chunk in Chunk(savedEntries.Select(entry => entry.Uri), 100))
            {
                generators.AddRange((await protocol.GetFeedGeneratorsAsync(chunk)
                    .ConfigureAwait(false))
                    .HandleResult()
                    .Feeds ?? []);
            }

            var savedByUri = new Dictionary<string, GeneratorView>(StringComparer.Ordinal);
            foreach (var generator in generators)
                savedByUri[generator.Uri.ToString()] = generator;

            var saved = savedEntries
                .Where(entry => savedByUri.ContainsKey(entry.Uri.ToString()))
                .Select(entry => new FeedGeneratorViewModel(savedByUri[entry.Uri.ToString()], entry.Pinned))
                .ToList();

            syncContext.Post(() =>
            {
                SavedFeeds.Clear();
                foreach (var feed in saved)
                    SavedFeeds.Add(feed);

                UpdateEmptyStates();
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load saved feeds.");
            SetErrored(ex);
            return;
        }

        await LoadDiscoverPageAsync(reset: true);
    }

    private async Task LoadDiscoverPageAsync(bool reset)
    {
        var version = reset ? Interlocked.Increment(ref discoverVersion) : discoverVersion;
        var query = activeQuery;
        var cursor = reset ? null : discoverCursor;

        syncContext.Post(() =>
        {
            IsLoadingMore = true;
            if (reset)
            {
                SuggestedFeeds.Clear();
                HasMoreFeeds = false;
                HasNoResults = false;
            }
        });

        try
        {
            var protocol = protocolService.Protocol;
            IEnumerable<GeneratorView> feeds;
            string nextCursor;
            if (query != null)
            {
                var result = (await UnspeccedEndpoints.GetPopularFeedGeneratorsAsync(protocol, PageSize, cursor, query)
                    .ConfigureAwait(false))
                    .HandleResult();
                feeds = result.Feeds ?? [];
                nextCursor = result.Cursor;
            }
            else
            {
                var result = (await FeedEndpoints.GetSuggestedFeedsAsync(protocol, PageSize, cursor)
                    .ConfigureAwait(false))
                    .HandleResult();
                feeds = result.Feeds ?? [];
                nextCursor = result.Cursor;
            }

            var feedList = feeds.ToList();
            syncContext.Post(() =>
            {
                if (version != discoverVersion)
                    return;

                foreach (var feed in feedList)
                {
                    if (FindSaved(feed.Uri) != null || SuggestedFeeds.Any(existing => SameUri(existing.Uri, feed.Uri)))
                        continue;

                    SuggestedFeeds.Add(new FeedGeneratorViewModel(feed));
                }

                discoverCursor = nextCursor;
                HasMoreFeeds = !string.IsNullOrEmpty(nextCursor) && feedList.Count > 0;
                IsLoadingMore = false;
                UpdateEmptyStates();
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load feeds (query: {Query})", query);
            SetErrored(ex);
        }
        finally
        {
            syncContext.Post(() =>
            {
                if (version == discoverVersion)
                    IsLoadingMore = false;
            });
        }
    }

    private async Task PutPreferencesAsync(List<ATObject> updatedPreferences)
    {
        (await protocolService.Protocol.PutPreferencesAsync(updatedPreferences)
            .ConfigureAwait(false))
            .HandleResult();

        preferences = updatedPreferences;
        syncContext.Post(() => WeakReferenceMessenger.Default.Send(new PinnedFeedsChangedMessage()));
    }

    private FeedGeneratorViewModel FindSaved(ATUri uri)
        => SavedFeeds.FirstOrDefault(existing => SameUri(existing.Uri, uri));

    private void UpdateEmptyStates()
    {
        HasSavedFeeds = SavedFeeds.Count > 0;
        HasNoResults = IsSearching && SuggestedFeeds.Count == 0 && !IsLoadingMore;
    }

    private static bool SameUri(ATUri a, ATUri b)
        => string.Equals(a?.ToString(), b?.ToString(), StringComparison.Ordinal);

    private static IEnumerable<List<ATUri>> Chunk(IEnumerable<ATUri> source, int size)
    {
        var chunk = new List<ATUri>(size);
        foreach (var item in source)
        {
            chunk.Add(item);
            if (chunk.Count == size)
            {
                yield return chunk;
                chunk = new List<ATUri>(size);
            }
        }

        if (chunk.Count > 0)
            yield return chunk;
    }
}
