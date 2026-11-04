using System;
using System.Threading.Tasks;
using FishyFlip.Lexicon.Com.Atproto.Moderation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace UniSky.Controls.Moderation;

internal record ReportPostResult(string ReasonType, string Details);

internal static class ReportPostDialog
{
    private static readonly (string Label, string ReasonType)[] Reasons =
    [
        ("Spam", ReasonType.ReasonSpam),
        ("Misleading or impersonation", ReasonType.ReasonMisleading),
        ("Unwanted sexual content", ReasonType.ReasonSexual),
        ("Anti-social behaviour or harassment", ReasonType.ReasonRude),
        ("Breaks community guidelines or the law", ReasonType.ReasonViolation),
        ("Something else", ReasonType.ReasonOther),
    ];

    /// <summary>
    /// Asks why the post is being reported. Returns null if the user cancels.
    /// </summary>
    public static async Task<ReportPostResult> ShowAsync()
    {
        var reasons = new ComboBox
        {
            Header = "Why are you reporting this post?",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            PlaceholderText = "Choose a reason"
        };

        foreach (var (label, _) in Reasons)
            reasons.Items.Add(label);

        var details = new TextBox
        {
            Header = "Additional details (optional)",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MaxLength = 2000,
            Height = 96,
            Margin = new Thickness(0, 12, 0, 0)
        };

        var panel = new StackPanel();
        panel.Children.Add(reasons);
        panel.Children.Add(details);

        var dialog = new ContentDialog
        {
            Title = "Report post",
            Content = panel,
            PrimaryButtonText = "Report",
            SecondaryButtonText = "Cancel",
            IsPrimaryButtonEnabled = false
        };

        reasons.SelectionChanged += (_, _) => dialog.IsPrimaryButtonEnabled = reasons.SelectedIndex >= 0;

        ContentDialogResult result;
        try
        {
            result = await dialog.ShowAsync();
        }
        catch
        {
            // Another dialog is already open; only one can be shown at a time.
            return null;
        }

        if (result != ContentDialogResult.Primary || reasons.SelectedIndex < 0)
            return null;

        var text = details.Text?.Trim();
        return new ReportPostResult(Reasons[reasons.SelectedIndex].ReasonType,
            string.IsNullOrEmpty(text) ? null : text);
    }

    public static async Task ShowSubmittedAsync()
    {
        try
        {
            await new ContentDialog
            {
                Title = "Report sent",
                Content = new TextBlock
                {
                    Text = "Thanks. Your report has been sent to the moderation team.",
                    TextWrapping = TextWrapping.Wrap
                },
                PrimaryButtonText = "OK"
            }.ShowAsync();
        }
        catch
        {
        }
    }
}
