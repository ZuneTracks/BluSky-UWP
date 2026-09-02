using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FishyFlip;
using FishyFlip.Lexicon.App.Bsky.Actor;
using FishyFlip.Lexicon.App.Bsky.Feed;
using FishyFlip.Models;
using FishyFlip.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UniSky.Controls.Compose;
using UniSky.Extensions;
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

            var prefs = (await protocol.GetPreferencesAsync()
                .ConfigureAwait(false))
                .HandleResult();

            var generatedFeeds = SavedFeedPreferences.GetPinnedFeedUris(prefs.Preferences);
            var generators = generatedFeeds.Count == 0
                ? []
                : ((await protocol.GetFeedGeneratorsAsync(generatedFeeds.ToList())
                    .ConfigureAwait(false))
                    .HandleResult()
                    .Feeds ?? []);
            var generatorsByUri = generators.ToDictionary(feed => feed.Uri.ToString(), StringComparer.Ordinal);

            syncContext.Post(() =>
            {
                foreach (var feedUri in generatedFeeds)
                {
                    if (generatorsByUri.TryGetValue(feedUri.ToString(), out var generatedFeed))
                    {
                        Feeds.Add(new FeedViewModel(Navigation, FeedType.Custom, generatedFeed.Uri, generatedFeed, this.protocolService));
                    }
                }

                if (Feeds.Count == 0)
                    Feeds.Add(new FeedViewModel(Navigation, FeedType.Following, null, null, this.protocolService));
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch feeds!");
            this.SetErrored(ex);
        }
    }
}
