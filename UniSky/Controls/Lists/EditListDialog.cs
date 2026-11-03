using System;
using System.Threading.Tasks;
using FishyFlip.Lexicon.App.Bsky.Graph;
using UniSky.ViewModels.Lists;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace UniSky.Controls.Lists;

/// <summary>
/// Collects a list's name, description and (when creating) its type.
/// </summary>
public sealed class EditListDialog : ContentDialog
{
    private readonly TextBox nameBox;
    private readonly TextBox descriptionBox;
    private readonly ComboBox purposeBox;

    public EditListDialog(bool isNew, string name = "", string description = "")
    {
        Title = isNew ? "New list" : "Edit list";
        PrimaryButtonText = isNew ? "Create" : "Save";
        SecondaryButtonText = "Cancel";
        PrimaryButtonStyle = (Style)Application.Current.Resources["AccentButtonStyle"];

        nameBox = new TextBox
        {
            Header = "Name",
            PlaceholderText = "e.g. Friends",
            MaxLength = ListActions.MaxNameLength,
            Text = name ?? string.Empty
        };
        nameBox.TextChanged += (_, _) => UpdatePrimaryButton();

        descriptionBox = new TextBox
        {
            Header = "Description (optional)",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MaxLength = ListActions.MaxDescriptionLength,
            Height = 110,
            Margin = new Thickness(0, 12, 0, 0),
            Text = description ?? string.Empty
        };

        var panel = new StackPanel { MinWidth = 280 };
        panel.Children.Add(nameBox);
        panel.Children.Add(descriptionBox);

        if (isNew)
        {
            purposeBox = new ComboBox
            {
                Header = "Type",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 12, 0, 0)
            };
            purposeBox.Items.Add(new ComboBoxItem { Content = "Curated list (people to follow or browse)", Tag = ListPurpose.Curatelist });
            purposeBox.Items.Add(new ComboBoxItem { Content = "Moderation list (people to mute or block)", Tag = ListPurpose.Modlist });
            purposeBox.SelectedIndex = 0;
            panel.Children.Add(purposeBox);
        }

        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        UpdatePrimaryButton();
    }

    public string ListName
        => nameBox.Text.Trim();

    public string ListDescription
        => descriptionBox.Text.Trim();

    public string Purpose
        => (purposeBox?.SelectedItem as ComboBoxItem)?.Tag as string ?? ListPurpose.Curatelist;

    private void UpdatePrimaryButton()
        => IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(nameBox.Text);
}

internal static class ListDialogs
{
    public static async Task ShowErrorAsync(string title, Exception exception)
    {
        var message = exception is ListActionException
            ? exception.Message
            : (exception.InnerException ?? exception).Message;

        await TryShowAsync(new ContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = "OK"
        });
    }

    public static async Task<bool> ConfirmAsync(string title, string message, string confirmText)
    {
        var result = await TryShowAsync(new ContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = confirmText,
            SecondaryButtonText = "Cancel"
        });

        return result == ContentDialogResult.Primary;
    }

    private static async Task<ContentDialogResult> TryShowAsync(ContentDialog dialog)
    {
        try
        {
            return await dialog.ShowAsync();
        }
        catch (Exception)
        {
            // Another dialog is already open; only one can be shown at a time.
            return ContentDialogResult.None;
        }
    }
}
