using FishyFlip.Lexicon;
using FishyFlip.Lexicon.App.Bsky.Actor;
using FishyFlip.Models;
using UniSky.Models.Feeds;

namespace UniSky.Navigation.Test;

public class SavedFeedPreferencesTests
{
    private static readonly ATUri FeedUri = ATUri.Create("at://did:plc:feed/app.bsky.feed.generator/custom");

    [Fact]
    public void Missing_or_empty_preferences_have_no_pinned_feeds()
    {
        Assert.Empty(SavedFeedPreferences.GetPinnedFeedUris(null));
        Assert.Empty(SavedFeedPreferences.GetPinnedFeedUris([]));
        Assert.Empty(SavedFeedPreferences.GetPinnedFeedUris(
        [
            new SavedFeedsPrefV2 { Items = [] }
        ]));
    }

    [Fact]
    public void Pinning_a_feed_creates_and_updates_the_saved_feed_preference()
    {
        var added = SavedFeedPreferences.WithPinnedFeed([], FeedUri);
        var addedPreference = Assert.IsType<SavedFeedsPrefV2>(Assert.Single(added));
        var addedFeed = Assert.Single(addedPreference.Items);

        Assert.Equal("feed", addedFeed.TypeValue);
        Assert.Equal(FeedUri.ToString(), addedFeed.Value);
        Assert.True(addedFeed.Pinned);
        Assert.Matches("^[2-7a-z]{13}$", addedFeed.Id);

        var updated = SavedFeedPreferences.WithPinnedFeed(added, FeedUri);
        var updatedPreference = Assert.IsType<SavedFeedsPrefV2>(Assert.Single(updated));
        Assert.Single(updatedPreference.Items);
        Assert.Equal(addedFeed.Id, updatedPreference.Items[0].Id);
    }

    [Fact]
    public void Saving_fills_in_missing_ids_on_existing_entries()
    {
        var legacy = new SavedFeed { TypeValue = "timeline", Value = "following", Pinned = true };
        var updated = SavedFeedPreferences.WithPinnedFeed([new SavedFeedsPrefV2 { Items = [legacy] }], FeedUri);
        var savedFeeds = Assert.IsType<SavedFeedsPrefV2>(Assert.Single(updated));

        Assert.Equal(2, savedFeeds.Items.Count);
        Assert.All(savedFeeds.Items, item => Assert.False(string.IsNullOrWhiteSpace(item.Id)));
        Assert.NotEqual(savedFeeds.Items[0].Id, savedFeeds.Items[1].Id);
    }

    [Fact]
    public void Removing_a_feed_preserves_other_saved_feed_entries()
    {
        var following = new SavedFeed { TypeValue = "timeline", Value = "following", Pinned = true };
        var feed = new SavedFeed { TypeValue = "feed", Value = FeedUri.ToString(), Pinned = true };
        List<ATObject> preferences =
        [
            new SavedFeedsPrefV2 { Items = [following, feed] }
        ];

        var updated = SavedFeedPreferences.WithoutFeed(preferences, FeedUri);
        var savedFeeds = Assert.IsType<SavedFeedsPrefV2>(Assert.Single(updated));

        Assert.Single(savedFeeds.Items);
        Assert.Same(following, savedFeeds.Items[0]);
    }
}
