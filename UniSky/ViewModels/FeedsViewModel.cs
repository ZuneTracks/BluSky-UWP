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
using FishyFlip.Lexicon.App.Bsky.Actor;
using FishyFlip.Lexicon.App.Bsky.Feed;
using FishyFlip.Models;
using FishyFlip.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UniSky.Controls.Compose;
using UniSky.Extensions;
using UniSky.Messages;
using UniSky.Models.Feeds;
using UniSky.Services;
using UniSky.Services.Navigation;
using UniSky.ViewModels.Feeds;

namespace UniSky.ViewModels;

public partial class FeedsViewModel : ViewModelBase
{
    private readonly IProtocolService protocolService;
    private readonly ILogger<FeedsViewModel> logger;
    private readonly ATUri requestedFeedUri;
    private readonly GeneratorView requestedFeed;
    private int homeFeedsVersion;

    [ObservableProperty]
    private int selectedFeed;

    public FeedsViewModel(
        INavigationContext navigation,
        IProtocolService protocolService,
        ILogger<FeedsViewModel> logger,
        ATUri requestedFeedUri = null,
        GeneratorView requestedFeed = null)
        : base(navigation)
    {
        this.protocolService = protocolService;
        this.logger = logger;
        this.requestedFeedUri = requestedFeedUri;
        this.requestedFeed = requestedFeed;

        Feeds = [];

        if (requestedFeedUri == null)
        {
            WeakReferenceMessenger.Default.Register<PinnedFeedsChangedMessage>(this,
                (recipient, message) => _ = Task.Run(((FeedsViewModel)recipient).ReloadHomeFeedsAsync));
        }

        Task.Run(LoadAsync);
    }

    public ObservableCollection<FeedViewModel> Feeds { get; }

    [RelayCommand]
    public async Task Post()
    {
        var sheetsService = ServiceContainer.Scoped.GetRequiredService<ISheetService>();
        await sheetsService.ShowAsync<ComposeSheet>();
    }

    private async Task LoadAsync()
    {
        try
        {
            var protocol = protocolService.Protocol;
            if (requestedFeedUri != null)
            {
                var generator = requestedFeed;
                if (generator == null)
                {
                    var result = (await protocol.GetFeedGeneratorsAsync([requestedFeedUri])
                        .ConfigureAwait(false))
                        .HandleResult();
                    generator = result.Feeds?.FirstOrDefault(feed => feed.Uri.ToString() == requestedFeedUri.ToString());
                }

                if (generator == null)
                    throw new InvalidOperationException("The requested feed could not be found.");

                syncContext.Post(() =>
                    Feeds.Add(new FeedViewModel(Navigation, FeedType.Custom, generator.Uri, generator, protocolService)));
                return;
            }

            await LoadHomeFeedsAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch feeds!");
            this.SetErrored(ex);
        }
    }

    private async Task ReloadHomeFeedsAsync()
    {
        try
        {
            await LoadHomeFeedsAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to refresh pinned feeds!");
        }
    }

    private async Task LoadHomeFeedsAsync()
    {
        var version = Interlocked.Increment(ref homeFeedsVersion);
        var protocol = protocolService.Protocol;
        var prefs = (await protocol.GetPreferencesAsync()
            .ConfigureAwait(false))
            .HandleResult();

        var pinnedUris = SavedFeedPreferences.GetPinnedFeedUris(prefs.Preferences);
        var generators = pinnedUris.Count == 0
            ? []
            : ((await protocol.GetFeedGeneratorsAsync(pinnedUris.ToList())
                .ConfigureAwait(false))
                .HandleResult()
                .Feeds ?? []);
        var generatorsByUri = new Dictionary<string, GeneratorView>(StringComparer.Ordinal);
        foreach (var generator in generators)
            generatorsByUri[generator.Uri.ToString()] = generator;

        syncContext.Post(() =>
        {
            if (version != homeFeedsVersion)
                return;

            var selected = SelectedFeed >= 0 && SelectedFeed < Feeds.Count ? Feeds[SelectedFeed] : null;
            var desired = new List<FeedViewModel>();
            foreach (var feedUri in pinnedUris)
            {
                if (!generatorsByUri.TryGetValue(feedUri.ToString(), out var generator))
                    continue;

                var existing = Feeds.FirstOrDefault(feed => feed.Type == FeedType.Custom
                    && feed.FeedUri?.ToString() == generator.Uri.ToString());
                desired.Add(existing ?? new FeedViewModel(Navigation, FeedType.Custom, generator.Uri, generator, protocolService));
            }

            if (desired.Count == 0)
            {
                desired.Add(Feeds.FirstOrDefault(feed => feed.Type == FeedType.Following)
                    ?? new FeedViewModel(Navigation, FeedType.Following, null, null, protocolService));
            }

            // Update in place so already-loaded tabs keep their posts and scroll position.
            for (var i = Feeds.Count - 1; i >= 0; i--)
            {
                if (!desired.Contains(Feeds[i]))
                    Feeds.RemoveAt(i);
            }

            for (var i = 0; i < desired.Count; i++)
            {
                var index = Feeds.IndexOf(desired[i]);
                if (index == i)
                    continue;

                if (index >= 0)
                    Feeds.Move(index, i);
                else
                    Feeds.Insert(i, desired[i]);
            }

            var selectedIndex = selected == null ? -1 : Feeds.IndexOf(selected);
            SelectedFeed = selectedIndex >= 0 ? selectedIndex : 0;
        });
    }
}
