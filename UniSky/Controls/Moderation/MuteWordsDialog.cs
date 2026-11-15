using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FishyFlip.Lexicon.App.Bsky.Actor;
using Microsoft.Extensions.DependencyInjection;
using UniSky.Services;
using Windows.UI.Text;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace UniSky.Controls.Moderation;

/// <summary>
/// Adds and removes muted words and tags (app.bsky.actor.defs#mutedWordsPref).
/// </summary>
internal static class MuteWordsDialog
{
    private static readonly (string Label, TimeSpan? Duration)[] Durations =
    [
        ("Forever", null),
        ("24 hours", TimeSpan.FromHours(24)),
        ("7 days", TimeSpan.FromDays(7)),
        ("30 days", TimeSpan.FromDays(30)),
    ];

    public static async Task ShowAsync()
    {
        var moderation = ServiceContainer.Default.GetRequiredService<IModerationService>();

        var word = new TextBox
        {
            Header = "Word or tag to mute",
            PlaceholderText = "Enter a word or tag",
            MaxLength = 100
        };

        var duration = new ComboBox
        {
            Header = "Duration",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 12, 0, 0)
        };
        foreach (var (label, _) in Durations)
            duration.Items.Add(label);
        duration.SelectedIndex = 0;

        var target = new ComboBox
        {
            Header = "Mute in",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 12, 0, 0)
        };
        target.Items.Add("Text & tags");
        target.Items.Add("Tags only");
        target.SelectedIndex = 0;

        var excludeFollowing = new CheckBox
        {
            Content = "Exclude users you follow",
            Margin = new Thickness(0, 8, 0, 0)
        };

        var add = new Button
        {
            Content = "Add",
            IsEnabled = false,
            Margin = new Thickness(0, 8, 0, 0)
        };

        var status = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0),
            Visibility = Visibility.Collapsed
        };

        var list = new StackPanel();

        var panel = new StackPanel();
        panel.Children.Add(word);
        panel.Children.Add(duration);
        panel.Children.Add(target);
        panel.Children.Add(excludeFollowing);
        panel.Children.Add(add);
        panel.Children.Add(status);
        panel.Children.Add(new TextBlock
        {
            Text = "Your muted words",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 16, 0, 4)
        });
        panel.Children.Add(list);

        var dialog = new ContentDialog
        {
            Title = "Mute words & tags",
            Content = new ScrollViewer
            {
                Content = panel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            },
            PrimaryButtonText = "Done"
        };

        void ShowStatus(string message)
        {
            status.Text = message ?? "";
            status.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
        }

        void Render(IReadOnlyList<MutedWord> words)
        {
            list.Children.Clear();
            if (words.Count == 0)
            {
                list.Children.Add(new TextBlock
                {
                    Text = "You haven't muted any words or tags yet.",
                    Opacity = 0.7,
                    TextWrapping = TextWrapping.Wrap
                });
                return;
            }

            foreach (var mute in words.OrderBy(w => w.Value, StringComparer.CurrentCultureIgnoreCase))
                list.Children.Add(CreateRow(mute));
        }

        UIElement CreateRow(MutedWord mute)
        {
            var grid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = mute.Value ?? "", TextTrimming = TextTrimming.CharacterEllipsis });
            text.Children.Add(new TextBlock
            {
                Text = Describe(mute),
                Opacity = 0.7,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            });
            grid.Children.Add(text);

            var remove = new Button
            {
                Content = new FontIcon { Glyph = "\uE74D", FontSize = 14, FontFamily = new FontFamily("Segoe MDL2 Assets") },
                Background = new SolidColorBrush(Windows.UI.Colors.Transparent),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            };
            ToolTipService.SetToolTip(remove, $"Unmute \"{mute.Value}\"");
            Grid.SetColumn(remove, 1);
            remove.Click += async (_, _) =>
            {
                remove.IsEnabled = false;
                try
                {
                    await moderation.RemoveMutedWordAsync(mute);
                    Render(await moderation.GetMutedWordsAsync());
                    ShowStatus(null);
                }
                catch (Exception ex)
                {
                    remove.IsEnabled = true;
                    ShowStatus("Couldn't unmute: " + ex.Message);
                }
            };
            grid.Children.Add(remove);

            return grid;
        }

        word.TextChanged += (_, _) => add.IsEnabled = Sanitize(word.Text).Length > 0;

        add.Click += async (_, _) =>
        {
            var value = Sanitize(word.Text);
            if (value.Length == 0)
                return;

            add.IsEnabled = false;
            try
            {
                var expires = Durations[Math.Max(0, duration.SelectedIndex)].Duration;
                var mute = new MutedWord(
                    Guid.NewGuid().ToString("N"),
                    value,
                    target.SelectedIndex == 1 ? ["tag"] : ["content", "tag"],
                    excludeFollowing.IsChecked == true ? "exclude-following" : "all",
                    expires is { } span ? DateTime.UtcNow + span : null);

                await moderation.AddMutedWordAsync(mute);
                word.Text = "";
                ShowStatus($"Muted \"{value}\". Feeds hide it the next time they refresh.");
                Render(await moderation.GetMutedWordsAsync());
            }
            catch (Exception ex)
            {
                ShowStatus("Couldn't mute: " + ex.Message);
                add.IsEnabled = Sanitize(word.Text).Length > 0;
            }
        };

        dialog.Opened += async (_, _) =>
        {
            try
            {
                Render(await moderation.GetMutedWordsAsync());
            }
            catch (Exception ex)
            {
                ShowStatus("Couldn't load muted words: " + ex.Message);
            }
        };

        try
        {
            await dialog.ShowAsync();
        }
        catch
        {
            // Another dialog is already open; only one can be shown at a time.
        }
    }

    // Matches the official client: trim, drop a leading '#', and strip line breaks.
    private static string Sanitize(string value)
        => (value ?? "").Trim().TrimStart('#').Replace("\r", "").Replace("\n", "").Trim();

    private static string Describe(MutedWord mute)
    {
        var parts = new List<string>
        {
            mute.Targets?.Contains("content") == true ? "Text & tags" : "Tags only"
        };

        if (mute.ActorTarget == "exclude-following")
            parts.Add("excludes users you follow");

        if (mute.ExpiresAt is { } expires)
        {
            var local = expires.ToLocalTime();
            parts.Add(local < DateTime.Now ? "expired" : $"until {local:g}");
        }
        else
        {
            parts.Add("forever");
        }

        return string.Join(" · ", parts);
    }
}
