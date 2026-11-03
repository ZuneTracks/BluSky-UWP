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

        // app.bsky.actor.defs#savedFeed requires an id on every entry.
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.Id))
                item.Id = NewTid();
        }

        var savedFeeds = new SavedFeedsPrefV2 { Items = items };
        if (savedFeedsIndex >= 0)
            result[savedFeedsIndex] = savedFeeds;
        else
            result.Add(savedFeeds);

        return result;
    }

    private const string TidAlphabet = "234567abcdefghijklmnopqrstuvwxyz";
    private static readonly object tidLock = new();
    private static readonly long tidClockId = new Random().Next(0, 1024);
    private static long lastTidMicroseconds;

    /// <summary>
    /// Creates an AT Protocol TID: 53 bits of microseconds since the Unix epoch plus a 10-bit clock id,
    /// encoded as 13 base32-sortable characters.
    /// </summary>
    internal static string NewTid()
    {
        long micros;
        lock (tidLock)
        {
            micros = (DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).Ticks / 10;
            if (micros <= lastTidMicroseconds)
                micros = lastTidMicroseconds + 1;
            lastTidMicroseconds = micros;
        }

        var value = (micros << 10) | tidClockId;
        var chars = new char[13];
        for (var i = 12; i >= 0; i--)
        {
            chars[i] = TidAlphabet[(int)(value & 31)];
            value >>= 5;
        }

        return new string(chars);
    }

    private static bool IsFeed(SavedFeed item, ATUri uri)
        => item.TypeValue == "feed"
            && string.Equals(item.Value, uri.ToString(), StringComparison.Ordinal);
}
