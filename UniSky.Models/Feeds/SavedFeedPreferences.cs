using System;
using System.Collections.Generic;
using System.Linq;
using FishyFlip.Lexicon;
using FishyFlip.Lexicon.App.Bsky.Actor;
using FishyFlip.Models;

namespace UniSky.Models.Feeds;

/// <summary>
/// Safely reads and updates the saved-feeds preference while preserving unrelated preferences.
/// </summary>
public static class SavedFeedPreferences
{
    public static IReadOnlyList<ATUri> GetPinnedFeedUris(IEnumerable<ATObject>? preferences)
    {
        var items = preferences?
            .OfType<SavedFeedsPrefV2>()
            .SelectMany(preference => preference.Items ?? [])
            ?? [];

        return items
            .Where(item => item is { TypeValue: "feed", Pinned: true }
                && ATUri.TryCreate(item.Value, out _))
            .Select(item => ATUri.Create(item.Value))
            .Distinct()
            .ToList();
    }

    public static List<ATObject> WithPinnedFeed(IEnumerable<ATObject>? preferences, ATUri uri)
    {
        if (uri == null)
            throw new ArgumentNullException(nameof(uri));

        return Update(preferences, items =>
        {
            var existing = items.FirstOrDefault(item => IsFeed(item, uri));
            if (existing is null)
            {
                items.Add(new SavedFeed
                {
                    TypeValue = "feed",
                    Value = uri.ToString(),
                    Pinned = true
                });
            }
            else
            {
                existing.Pinned = true;
            }
        });
    }

    public static List<ATObject> WithoutFeed(IEnumerable<ATObject>? preferences, ATUri uri)
    {
        if (uri == null)
            throw new ArgumentNullException(nameof(uri));

        return Update(preferences, items => items.RemoveAll(item => IsFeed(item, uri)));
    }

    private static List<ATObject> Update(IEnumerable<ATObject>? preferences, Action<List<SavedFeed>> update)
    {
        var result = preferences?.ToList() ?? [];
        var savedFeedsIndex = result.FindIndex(preference => preference is SavedFeedsPrefV2);
        var items = savedFeedsIndex >= 0
            ? ((SavedFeedsPrefV2)result[savedFeedsIndex]).Items?.ToList() ?? []
            : [];

        update(items);

        var savedFeeds = new SavedFeedsPrefV2 { Items = items };
        if (savedFeedsIndex >= 0)
            result[savedFeedsIndex] = savedFeeds;
        else
            result.Add(savedFeeds);

        return result;
    }

    private static bool IsFeed(SavedFeed item, ATUri uri)
        => item.TypeValue == "feed"
            && string.Equals(item.Value, uri.ToString(), StringComparison.Ordinal);
}
