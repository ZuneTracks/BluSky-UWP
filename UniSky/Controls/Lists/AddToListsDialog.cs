using System;
using System.Collections.Generic;
using System.Linq;
using FishyFlip.Lexicon.App.Bsky.Graph;
using FishyFlip.Models;
using Microsoft.Extensions.DependencyInjection;
using UniSky.Services;
using UniSky.ViewModels.Lists;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using ListView = FishyFlip.Lexicon.App.Bsky.Graph.ListView;

namespace UniSky.Controls.Lists;

/// <summary>
/// Shows the signed-in user's lists with a checkbox per list; toggling adds or removes <c>subject</c> immediately.
/// </summary>
public sealed class AddToListsDialog : ContentDialog
{
    private sealed class Membership
    {
        public ListView List { get; set; }
        public ATUri ItemUri { get; set; }
    }

    private readonly ATDid subject;
    private readonly StackPanel listsPanel = new();
    private readonly ProgressRing progress = new() { IsActive = true, Width = 32, Height = 32, Margin = new Thickness(16) };
    private readonly TextBlock statusBlock = new() { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
    private bool suppressToggle;

    public AddToListsDialog(ATDid subject, string displayName)
    {
        this.subject = subject;

        Title = "Add to lists";
        PrimaryButtonText = "Done";

        var root = new StackPanel { MinWidth = 280 };
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            root.Children.Add(new TextBlock
            {
                Text = $"Choose which of your lists {displayName} belongs to.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12)
            });
        }

        root.Children.Add(progress);
        root.Children.Add(listsPanel);
        root.Children.Add(statusBlock);

        Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Opened += OnOpened;
    }

    private async void OnOpened(ContentDialog sender, ContentDialogOpenedEventArgs args)
    {
        Opened -= OnOpened;

        try
        {
            var protocol = ServiceContainer.Scoped.GetRequiredService<IProtocolService>().Protocol;
            var memberships = new List<Membership>();
            string cursor = null;
            do
            {
                var result = await protocol.Graph.GetListsWithMembershipAsync(subject, 100, cursor);
                if (!result.IsT0 || result.AsT0 is not { } page)
                    throw new ListActionException(result.IsT1 ? result.AsT1?.Detail?.Message ?? result.AsT1?.StatusCode.ToString() : "Your lists could not be loaded.");

                memberships.AddRange(page.ListsWithMembership
                    .Where(m => m.List?.Uri != null)
                    .Select(m => new Membership { List = m.List, ItemUri = m.ListItem?.Uri }));

                cursor = page.ListsWithMembership.Count == 0 ? null : page.Cursor;
            }
            while (!string.IsNullOrWhiteSpace(cursor));

            foreach (var membership in memberships)
                listsPanel.Children.Add(CreateCheckBox(membership));

            if (memberships.Count == 0)
                ShowStatus("You don't have any lists yet. Create one from the Lists page in the menu.");
        }
        catch (Exception ex)
        {
            ShowStatus((ex is ListActionException ? ex : ex.InnerException ?? ex).Message);
        }
        finally
        {
            progress.IsActive = false;
            progress.Visibility = Visibility.Collapsed;
        }
    }

    private CheckBox CreateCheckBox(Membership membership)
    {
        var label = membership.List.Name ?? string.Empty;
        if (membership.List.Purpose == ListPurpose.Modlist)
            label += " (moderation)";

        var checkBox = new CheckBox
        {
            Content = label,
            IsChecked = membership.ItemUri != null,
            Tag = membership
        };
        checkBox.Checked += OnToggled;
        checkBox.Unchecked += OnToggled;
        return checkBox;
    }

    private async void OnToggled(object sender, RoutedEventArgs e)
    {
        if (suppressToggle || sender is not CheckBox { Tag: Membership membership } checkBox)
            return;

        var add = checkBox.IsChecked == true;
        checkBox.IsEnabled = false;
        statusBlock.Visibility = Visibility.Collapsed;

        try
        {
            if (add && membership.ItemUri == null)
            {
                membership.ItemUri = await ListActions.AddMemberAsync(membership.List.Uri, subject);
            }
            else if (!add && membership.ItemUri != null)
            {
                await ListActions.RemoveMemberAsync(membership.ItemUri);
                membership.ItemUri = null;
            }
        }
        catch (Exception ex)
        {
            suppressToggle = true;
            checkBox.IsChecked = !add;
            suppressToggle = false;
            ShowStatus((ex is ListActionException ? ex : ex.InnerException ?? ex).Message);
        }
        finally
        {
            checkBox.IsEnabled = true;
        }
    }

    private void ShowStatus(string text)
    {
        statusBlock.Text = text;
        statusBlock.Visibility = Visibility.Visible;
    }
}
