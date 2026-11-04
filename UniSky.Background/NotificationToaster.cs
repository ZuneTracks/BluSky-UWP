using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FishyFlip;
using FishyFlip.Lexicon.App.Bsky.Feed;
using FishyFlip.Lexicon.App.Bsky.Notification;
using FishyFlip.Models;
using Humanizer;
using Microsoft.Extensions.Logging;
using Microsoft.Toolkit.Uwp.Notifications;
using UniSky.Models;
using UniSky.Models.Notifications;
using UniSky.Services;
using Windows.ApplicationModel.Resources;
using Windows.UI.Notifications;
using Notification = FishyFlip.Lexicon.App.Bsky.Notification.Notification;

namespace UniSky.Background;

/// <summary>
/// Polls an account's notifications from the background task and raises local toasts for the
/// ones that arrived since the last run. Stands in for WNS push, which needs a server.
/// </summary>
internal sealed class NotificationToaster
{
    private const string LastNotifiedKeyPrefix = "LastNotifiedAt_v1_";
    private const string NotificationsRoute = "unisky://notifications";
    private const int FetchLimit = 30;
    private const int MaxToasts = 5;
    private const int HydrationChunkSize = 25;

    private static readonly string[] HiddenLabels = ["!hide", "!takedown"];

    private readonly ISettingsService settings;
    private readonly ILogger logger;
    private readonly NotificationOptions options;
    private readonly bool showAccount;
    private readonly ResourceLoader strings;

    public NotificationToaster(ISettingsService settings, ILogger logger, NotificationOptions options, bool showAccount)
    {
        this.settings = settings;
        this.logger = logger;
        this.options = options;
        this.showAccount = showAccount;

        try
        {
            strings = ResourceLoader.GetForViewIndependentUse();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load string resources for toasts");
        }
    }

    public static bool IsEnabled(NotificationOptions options)
        => (options & NotificationOptions.ExcludeEverything) != NotificationOptions.ExcludeEverything;

    /// <summary>
    /// Forgets where an account got up to, so re-enabling notifications doesn't replay the backlog.
    /// </summary>
    public static void Reset(ISettingsService settings, string did)
        => settings.TryDelete(LastNotifiedKeyPrefix + did);

    public async Task ShowNewAsync(ATProtocol protocol, SessionModel session, int unreadCount)
    {
        var key = LastNotifiedKeyPrefix + session.DID;
        var hasMarker = settings.TryRead<long>(key, out var markerTicks);
        if (hasMarker && unreadCount <= 0)
            return;

        var response = (await protocol.Notification
            .ListNotificationsAsync(limit: FetchLimit)
            .ConfigureAwait(false))
            .HandleResult();

        var notifications = response?.Notifications?
            .Where(n => n?.IndexedAt is not null && n.Cid is not null && n.Author?.Did is not null)
            .ToList() ?? [];

        var newest = notifications.Count > 0
            ? notifications.Max(n => AsUtc(n.IndexedAt.Value))
            : DateTime.MinValue;

        if (!hasMarker)
        {
            // first run for this account: treat everything already there as seen
            settings.Save(key, (newest > DateTime.MinValue ? newest : DateTime.UtcNow).Ticks);
            return;
        }

        var marker = new DateTime(markerTicks, DateTimeKind.Utc);
        if (newest > marker)
            settings.Save(key, newest.Ticks);

        var fresh = notifications
            .Where(n => n.IsRead != true && AsUtc(n.IndexedAt.Value) > marker && !IsHidden(n))
            .ToList();

        if (fresh.Count == 0)
            return;

        var groups = NotificationGrouper.Group(fresh)
            .Where(IsAllowed)
            .ToList();

        if (groups.Count == 0)
            return;

        var shown = groups.Take(MaxToasts).ToList();
        var subjects = await HydrateSubjectsAsync(protocol, shown).ConfigureAwait(false);
        var notifier = ToastNotificationManager.CreateToastNotifier();

        if (groups.Count > shown.Count)
        {
            var overflow = groups.Count - shown.Count;
            var builder = new ToastContentBuilder()
                .AddArgument("Record", NotificationsRoute)
                .AddText(string.Format(GetString("Notification_ToastOverflow", "{0} more notifications"), overflow));

            Show(notifier, builder, session, "overflow");
        }

        // the API returns newest first; show oldest first so the newest ends up on top
        for (var i = shown.Count - 1; i >= 0; i--)
        {
            try
            {
                Show(notifier, BuildToast(shown[i], subjects), session, shown[i].Key);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to show toast for {Uri}", shown[i].Head.Uri);
            }
        }
    }

    private void Show(ToastNotifier notifier, ToastContentBuilder builder, SessionModel session, string key)
    {
        if (showAccount && !string.IsNullOrEmpty(session.Handle))
            builder.AddAttributionText("@" + session.Handle);

        var toast = new ToastNotification(builder.GetToastContent().GetXml());

        // tags are capped at 64 characters on 15063, and CIDs are longer than that
        if (!string.IsNullOrEmpty(key))
            toast.Tag = key.Length > 16 ? key.Substring(key.Length - 16) : key;

        notifier.Show(toast);
    }

    private ToastContentBuilder BuildToast(NotificationGroup group, IReadOnlyDictionary<string, PostView> subjects)
    {
        var head = group.Head;
        var author = head.Author;
        var handle = author.Handle?.ToString();
        var name = !string.IsNullOrWhiteSpace(author.DisplayName) ? author.DisplayName : "@" + handle;

        var builder = new ToastContentBuilder()
            .AddArgument("Record", GetTarget(group))
            .AddText(BuildTitle(group, name), hintMaxLines: 2);

        var body = GetBody(group, subjects);
        if (string.IsNullOrWhiteSpace(body) && group.Kind == NotificationKind.Follow && group.Count == 1 && handle is not null)
            body = "@" + handle;

        if (!string.IsNullOrWhiteSpace(body))
            builder.AddText(body);

        if (TryGetAvatar(author.Avatar, out var avatar))
            builder.AddAppLogoOverride(avatar, ToastGenericAppLogoCrop.Circle);

        return builder;
    }

    private string BuildTitle(NotificationGroup group, string name) => group.Kind switch
    {
        NotificationKind.Reply => Format("Notification_Reply", "{0} replied to you", name),
        NotificationKind.Mention => Format("Notification_Mention", "{0} mentioned you", name),
        NotificationKind.Quote => Format("Notification_Quote", "{0} quoted your post", name),
        NotificationKind.Like => Aggregated(group, "Notification_LikedTweetSingle", "{0} liked your post",
            "Notification_LikedTweetMultiple", "{0} and {1} liked your post", name),
        NotificationKind.Repost => Aggregated(group, "Notification_RetweetSingle", "{0} reposted",
            "Notification_RetweetMultiple", "{0} and {1} reposted", name),
        NotificationKind.LikeViaRepost => Aggregated(group, "Notification_LikeViaRepostSingle", "{0} liked your repost",
            "Notification_LikeViaRepostMultiple", "{0} and {1} liked your repost", name),
        NotificationKind.RepostViaRepost => Aggregated(group, "Notification_RepostViaRepostSingle", "{0} reposted your repost",
            "Notification_RepostViaRepostMultiple", "{0} and {1} reposted your repost", name),
        NotificationKind.FeedGenLike => Aggregated(group, "Notification_FeedGenLikeSingle", "{0} liked your custom feed",
            "Notification_FeedGenLikeMultiple", "{0} and {1} liked your custom feed", name),
        NotificationKind.Follow => Aggregated(group, "Notification_Follow", "{0} followed you",
            "Notification_FollowMultiple", "{0} and {1} followed you", name),
        NotificationKind.SubscribedPost => group.Count > 1
            ? string.Format(GetString("Notification_SubscribedPostMultiple", "{1} new posts from {0}"), name, group.Count)
            : Format("Notification_SubscribedPost", "New post from {0}", name),
        NotificationKind.StarterPackJoined => Format("Notification_StarterPackJoined", "{0} signed up with your starter pack", name),
        NotificationKind.Verified => Format("Notification_Verified", "{0} verified you", name),
        NotificationKind.Unverified => Format("Notification_Unverified", "{0} removed their verification from your account", name),
        _ => Format("Notification_Unknown", "{0} interacted with you", name),
    };

    private string Format(string key, string fallback, string name)
        => string.Format(GetString(key, fallback), name);

    private string Aggregated(NotificationGroup group, string singleKey, string singleFallback,
                              string multipleKey, string multipleFallback, string name)
    {
        var count = group.All()
            .Select(n => n.Author?.Did?.ToString())
            .Where(d => d is not null)
            .Distinct()
            .Count();

        if (count <= 1)
            return Format(singleKey, singleFallback, name);

        var other = GetString("Notification_Other", "other");
        return string.Format(GetString(multipleKey, multipleFallback), name, other.ToQuantity(count - 1));
    }

    private static string GetBody(NotificationGroup group, IReadOnlyDictionary<string, PostView> subjects)
    {
        if (NotificationTypes.IsFullPost(group.Kind) || group.Kind == NotificationKind.SubscribedPost)
            return (group.Head.Record as Post)?.Text;

        if (group.SubjectUri is not null &&
            subjects.TryGetValue(group.SubjectUri.ToString(), out var subject))
            return (subject.Record as Post)?.Text;

        return null;
    }

    private static string GetTarget(NotificationGroup group)
    {
        switch (group.Kind)
        {
            case NotificationKind.Follow:
            case NotificationKind.Verified:
            case NotificationKind.Unverified:
            case NotificationKind.StarterPackJoined:
                return group.Count == 1 ? "at://" + group.Head.Author.Did : NotificationsRoute;
            case NotificationKind.FeedGenLike:
                return group.SubjectUri?.ToString() ?? NotificationsRoute;
            default:
                return NotificationTypes.IsPostSubject(group.SubjectUri)
                    ? group.SubjectUri.ToString()
                    : NotificationsRoute;
        }
    }

    private async Task<IReadOnlyDictionary<string, PostView>> HydrateSubjectsAsync(ATProtocol protocol, IReadOnlyList<NotificationGroup> groups)
    {
        var posts = new Dictionary<string, PostView>();
        var uris = groups
            .Where(g => !NotificationTypes.IsFullPost(g.Kind) && NotificationTypes.IsPostSubject(g.SubjectUri))
            .Select(g => g.SubjectUri)
            .GroupBy(u => u.ToString())
            .Select(g => g.First())
            .Take(HydrationChunkSize)
            .ToList();

        if (uris.Count == 0)
            return posts;

        try
        {
            var result = (await protocol.Feed.GetPostsAsync(uris).ConfigureAwait(false))
                .HandleResult();

            foreach (var post in result?.Posts ?? [])
            {
                if (post?.Uri is not null)
                    posts[post.Uri.ToString()] = post;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch {Count} notification subject posts", uris.Count);
        }

        return posts;
    }

    private bool IsAllowed(NotificationGroup group)
    {
        if (options.HasFlag(NotificationOptions.FollowingOnly) && group.Head.Author?.Viewer?.Following is null)
            return false;

        return group.Kind switch
        {
            NotificationKind.Like or NotificationKind.FeedGenLike => !options.HasFlag(NotificationOptions.ExcludeLikes),
            NotificationKind.LikeViaRepost => !options.HasFlag(NotificationOptions.ExcludeLikes) && !options.HasFlag(NotificationOptions.ExcludeRepostLikes),
            NotificationKind.Repost => !options.HasFlag(NotificationOptions.ExcludeReposts),
            NotificationKind.RepostViaRepost => !options.HasFlag(NotificationOptions.ExcludeReposts) && !options.HasFlag(NotificationOptions.ExcludeRepostReposts),
            NotificationKind.Follow => !options.HasFlag(NotificationOptions.ExcludeNewFollowers),
            NotificationKind.Reply => !options.HasFlag(NotificationOptions.ExcludeReplies),
            NotificationKind.Mention => !options.HasFlag(NotificationOptions.ExcludeMentions),
            NotificationKind.Quote => !options.HasFlag(NotificationOptions.ExcludeQuotes),
            _ => true,
        };
    }

    private static bool IsHidden(Notification notification)
    {
        var labels = notification.Author?.Labels;
        if (labels is null)
            return false;

        foreach (var label in labels)
        {
            if (label?.Val is not null && Array.IndexOf(HiddenLabels, label.Val) >= 0)
                return true;
        }

        return false;
    }

    private static bool TryGetAvatar(string avatar, out Uri uri)
    {
        uri = null;
        if (string.IsNullOrEmpty(avatar))
            return false;

        // toasts download remote images themselves, so ask the CDN for the small version
        avatar = avatar.Replace("/img/avatar/plain/", "/img/avatar_thumbnail/plain/");
        return Uri.TryCreate(avatar, UriKind.Absolute, out uri);
    }

    private static DateTime AsUtc(DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };

    private string GetString(string key, string fallback)
    {
        try
        {
            var value = strings?.GetString(key);
            if (!string.IsNullOrEmpty(value))
                return value;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Missing toast string {Key}", key);
        }

        return fallback;
    }
}
