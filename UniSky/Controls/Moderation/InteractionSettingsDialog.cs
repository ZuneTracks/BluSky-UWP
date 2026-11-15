using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FishyFlip;
using FishyFlip.Lexicon;
using FishyFlip.Lexicon.App.Bsky.Feed;
using FishyFlip.Lexicon.Com.Atproto.Repo;
using FishyFlip.Models;
using FishyFlip.Tools;
using Microsoft.Extensions.DependencyInjection;
using UniSky.Services;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;

namespace UniSky.Controls.Moderation;

/// <summary>
/// Edits who can reply to (threadgate) and quote (postgate) one of the user's own posts.
/// </summary>
internal static class InteractionSettingsDialog
{
    private const string ThreadgateCollection = "app.bsky.feed.threadgate";
    private const string PostgateCollection = "app.bsky.feed.postgate";

    /// <summary>
    /// Returns true if settings were saved.
    /// </summary>
    public static async Task<bool> ShowAsync(ATUri postUri, bool isReply)
    {
        var protocol = ServiceContainer.Scoped.GetRequiredService<IProtocolService>().Protocol;
        var did = protocol.Session?.Did;
        if (did == null)
            return false;

        var rkey = postUri.Rkey;

        var everyone = new RadioButton { Content = "Everyone", GroupName = "ReplyGate" };
        var nobody = new RadioButton { Content = "Nobody", GroupName = "ReplyGate" };
        var custom = new RadioButton { Content = "Only these people:", GroupName = "ReplyGate" };
        var mentioned = new CheckBox { Content = "People you mention", Margin = new Thickness(28, 0, 0, 0) };
        var following = new CheckBox { Content = "People you follow", Margin = new Thickness(28, 0, 0, 0) };
        var followers = new CheckBox { Content = "Your followers", Margin = new Thickness(28, 0, 0, 0) };
        var allowQuotes = new CheckBox { Content = "Allow quote posts", Margin = new Thickness(0, 16, 0, 0) };

        var status = new TextBlock
        {
            Text = "Loading...",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.7,
            Margin = new Thickness(0, 12, 0, 0)
        };

        var panel = new StackPanel();
        if (isReply)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "Reply settings are chosen by the author of the thread.",
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.7
            });
        }
        else
        {
            panel.Children.Add(new TextBlock { Text = "Who can reply" });
            panel.Children.Add(everyone);
            panel.Children.Add(nobody);
            panel.Children.Add(custom);
            panel.Children.Add(mentioned);
            panel.Children.Add(following);
            panel.Children.Add(followers);
        }
        panel.Children.Add(allowQuotes);
        panel.Children.Add(status);

        var dialog = new ContentDialog
        {
            Title = "Post interaction settings",
            Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            PrimaryButtonText = "Save",
            SecondaryButtonText = "Cancel",
            IsPrimaryButtonEnabled = false
        };

        Threadgate threadgate = null;
        Postgate postgate = null;
        List<ListRule> listRules = [];
        var loaded = false;

        void UpdateCustomState()
        {
            var isCustom = custom.IsChecked == true;
            mentioned.IsEnabled = following.IsEnabled = followers.IsEnabled = isCustom;

            var hasCustomRule = mentioned.IsChecked == true || following.IsChecked == true
                || followers.IsChecked == true || listRules.Count > 0;
            dialog.IsPrimaryButtonEnabled = loaded && (isReply || !isCustom || hasCustomRule);
        }

        foreach (var control in new ToggleButton[] { everyone, nobody, custom, mentioned, following, followers })
        {
            control.Checked += (_, _) => UpdateCustomState();
            control.Unchecked += (_, _) => UpdateCustomState();
        }

        dialog.Opened += async (_, _) =>
        {
            try
            {
                if (!isReply)
                    threadgate = await GetRecordAsync<Threadgate>(protocol, did, ThreadgateCollection, rkey);
                postgate = await GetRecordAsync<Postgate>(protocol, did, PostgateCollection, rkey);

                var allow = threadgate?.Allow;
                listRules = allow?.OfType<ListRule>().ToList() ?? [];
                if (allow == null)
                {
                    everyone.IsChecked = true;
                }
                else if (allow.Count == 0)
                {
                    nobody.IsChecked = true;
                }
                else
                {
                    custom.IsChecked = true;
                    mentioned.IsChecked = allow.OfType<MentionRule>().Any();
                    following.IsChecked = allow.OfType<FollowingRule>().Any();
                    followers.IsChecked = allow.OfType<FollowerRule>().Any();
                }

                allowQuotes.IsChecked = postgate?.EmbeddingRules?.OfType<DisableRule>().Any() != true;

                status.Text = listRules.Count > 0
                    ? "Replies are also allowed from members of a list. That setting is kept when you save."
                    : "";
                status.Visibility = string.IsNullOrEmpty(status.Text) ? Visibility.Collapsed : Visibility.Visible;
                loaded = true;
                UpdateCustomState();
            }
            catch (Exception ex)
            {
                status.Text = "Couldn't load the current settings: " + ex.Message;
            }
        };

        dialog.PrimaryButtonClick += async (_, args) =>
        {
            var deferral = args.GetDeferral();
            try
            {
                status.Text = "Saving...";
                status.Visibility = Visibility.Visible;

                if (!isReply)
                {
                    List<ATObject> allow = null;
                    if (nobody.IsChecked == true)
                    {
                        allow = [];
                    }
                    else if (custom.IsChecked == true)
                    {
                        allow = [];
                        if (mentioned.IsChecked == true) allow.Add(new MentionRule());
                        if (following.IsChecked == true) allow.Add(new FollowingRule());
                        if (followers.IsChecked == true) allow.Add(new FollowerRule());
                        allow.AddRange(listRules);
                    }

                    var hiddenReplies = threadgate?.HiddenReplies;
                    if (allow == null && (hiddenReplies == null || hiddenReplies.Count == 0))
                    {
                        if (threadgate != null)
                            (await protocol.DeleteRecordAsync(did, ThreadgateCollection, rkey)).HandleResult();
                    }
                    else
                    {
                        var record = new Threadgate(postUri, allow, threadgate?.CreatedAt ?? DateTime.UtcNow, hiddenReplies);
                        (await protocol.PutRecordAsync(did, ThreadgateCollection, rkey, record)).HandleResult();
                    }
                }

                var detached = postgate?.DetachedEmbeddingUris;
                if (allowQuotes.IsChecked == true && (detached == null || detached.Count == 0))
                {
                    if (postgate != null)
                        (await protocol.DeleteRecordAsync(did, PostgateCollection, rkey)).HandleResult();
                }
                else
                {
                    var record = new Postgate(postUri, postgate?.CreatedAt ?? DateTime.UtcNow, detached,
                        allowQuotes.IsChecked == true ? [] : [new DisableRule()]);
                    (await protocol.PutRecordAsync(did, PostgateCollection, rkey, record)).HandleResult();
                }
            }
            catch (Exception ex)
            {
                args.Cancel = true;
                status.Text = "Couldn't save: " + ex.Message;
            }
            finally
            {
                deferral.Complete();
            }
        };

        try
        {
            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }
        catch
        {
            // Another dialog is already open; only one can be shown at a time.
            return false;
        }
    }

    private static async Task<T> GetRecordAsync<T>(ATProtocol protocol, ATDid did, string collection, string rkey)
        where T : ATObject
    {
        var (output, error) = await protocol.GetRecordAsync(did, collection, rkey);
        if (error != null)
        {
            if (error.Detail?.Error == "RecordNotFound" || error.StatusCode == 404)
                return null;

            throw new ATNetworkErrorException(error);
        }

        return output?.Value as T;
    }
}
