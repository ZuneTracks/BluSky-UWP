using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using UniSky.Controls.Overlay;
using UniSky.Controls.Sheet;
using UniSky.Services;
using UniSky.ViewModels.Compose;
using UniSky.ViewModels.Posts;
using Windows.ApplicationModel.DataTransfer;
using Windows.ApplicationModel.Resources;
using Windows.Foundation;
using Windows.Foundation.Metadata;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;

// The User Control item template is documented at https://go.microsoft.com/fwlink/?LinkId=234236

namespace UniSky.Controls.Compose;

public sealed partial class ComposeSheet : SheetControl
{
    private readonly ResourceLoader strings;

    public ComposeViewModel ViewModel
    {
        get => (ComposeViewModel)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register("ViewModel", typeof(ComposeViewModel), typeof(ComposeSheet), new PropertyMetadata(null));

    public ComposeSheet()
         : base()
    {
        this.InitializeComponent();
        this.Showing += OnShowing;
        this.Shown += OnShown;
        this.Hiding += OnHiding;
        this.Hidden += OnHidden;

        if (ApiInformation.IsEventPresent(typeof(UIElement).FullName, "PreviewKeyDown"))
        {
            PrimaryTextBox.PreviewKeyDown += OnPreviewKeyDown;
        }

        this.strings = ResourceLoader.GetForCurrentView();
    }

    protected override void OnBottomInsetsChanged(double leftInset, double rightInset)
    {
        FooterContainer.Padding = new Thickness(leftInset, 0, rightInset, 2);
    }

    public bool Not(bool b, bool a)
        => !a && !b;

    private void OnShowing(IOverlayControl sender, OverlayShowingEventArgs e)
    {
        if (Window.Current.Content is FrameworkElement element)
        {
            element.AllowDrop = true;
            element.DragEnter += HandleDrag;
            element.DragOver += HandleDrag;
            element.DragLeave += HandleDrag;
            element.Drop += HandleDrop;
        }

        if (e.Parameter is ComposeSheetOptions options)
        {
            this.ViewModel = ActivatorUtilities.CreateInstance<ComposeViewModel>(ServiceContainer.Scoped, options, Controller);
        }
        else if (e.Parameter is PostViewModel replyTo)
        {
            this.ViewModel = ActivatorUtilities.CreateInstance<ComposeViewModel>(ServiceContainer.Scoped, new ComposeSheetOptions(ReplyTo: replyTo, Quote: null), Controller);
        }
        else
        {
            this.ViewModel = ActivatorUtilities.CreateInstance<ComposeViewModel>(ServiceContainer.Scoped, Controller);
        }
    }

    private void OnHidden(IOverlayControl sender, RoutedEventArgs args)
    {
        if (Window.Current.Content is FrameworkElement element)
        {
            element.AllowDrop = false;
            element.DragEnter -= HandleDrag;
            element.DragOver -= HandleDrag;
            element.DragLeave -= HandleDrag;
            element.Drop -= HandleDrop;
        }
    }

    private void OnShown(IOverlayControl sender, RoutedEventArgs args)
    {
        PrimaryTextBox.Focus(FocusState.Programmatic);
    }

    private async void OnHiding(IOverlayControl sender, OverlayHidingEventArgs e)
    {
        var deferral = e.GetDeferral();
        try
        {
            if (ViewModel.IsDirty)
            {
                var discardDraftDialog = new ComposeDiscardDraftDialog();
                if (Controller != null && ApiInformation.IsApiContractPresent(typeof(UniversalApiContract).FullName, 8))
                    discardDraftDialog.XamlRoot = Controller.Root.XamlRoot;

                if (await discardDraftDialog.ShowAsync() != ContentDialogResult.Primary)
                {
                    e.Cancel = true;
                    return;
                }
            }
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void HandleDrag(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.Bitmap) ||
            e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = strings.GetString("UploadToBluesky");
            e.DragUIOverride.IsCaptionVisible = true;
        }
        else if (e.DataView.Contains(StandardDataFormats.Text) ||
                 e.DataView.Contains(StandardDataFormats.WebLink))
        {
            e.AcceptedOperation = DataPackageOperation.Link;
        }
    }

    private void HandleDrop(object sender, DragEventArgs e)
    {
        e.Handled = ViewModel.HandleDrop(e.DataView);
    }

    private void PrimaryTextBox_Paste(object sender, TextControlPasteEventArgs e)
    {
        e.Handled = ViewModel.HandlePaste();
    }

    private CancellationTokenSource mentionSearchCts;
    private int mentionStart = -1;

    // Finds an "@partial" token ending at the caret; returns the index of '@' or -1.
    private static int FindMentionStart(string text, int caret, out string query)
    {
        query = null;
        if (caret < 0 || caret > text.Length)
            return -1;

        var i = caret - 1;
        while (i >= 0 && (char.IsLetterOrDigit(text[i]) || text[i] is '.' or '-' or '_'))
            i--;

        if (i < 0 || text[i] != '@')
            return -1;
        if (i > 0 && (char.IsLetterOrDigit(text[i - 1]) || text[i - 1] is '_' or '@'))
            return -1;

        query = text.Substring(i + 1, caret - i - 1);
        return i;
    }

    private async void PrimaryTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        mentionSearchCts?.Cancel();
        var start = FindMentionStart(PrimaryTextBox.Text, PrimaryTextBox.SelectionStart, out var query);
        if (start < 0 || string.IsNullOrEmpty(query))
        {
            HideMentionSuggestions();
            return;
        }

        var cts = mentionSearchCts = new CancellationTokenSource();
        try
        {
            await Task.Delay(250, cts.Token);

            var protocol = ServiceContainer.Scoped.GetRequiredService<IProtocolService>().Protocol;
            var result = await protocol.Actor.SearchActorsTypeaheadAsync(query, 6, cancellationToken: cts.Token);
            if (cts.IsCancellationRequested)
                return;

            var suggestions = result.IsT0 && result.AsT0?.Actors is { } actors
                ? actors.Where(a => a?.Handle != null)
                        .Select(a => new MentionSuggestion(a.Handle.ToString(), a.DisplayName, a.Avatar))
                        .ToList()
                : [];

            if (suggestions.Count == 0)
            {
                HideMentionSuggestions();
                return;
            }

            mentionStart = start;
            MentionSuggestionsList.ItemsSource = suggestions;
            MentionSuggestionsList.Visibility = Visibility.Visible;
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            HideMentionSuggestions();
        }
    }

    private void HideMentionSuggestions()
    {
        mentionStart = -1;
        MentionSuggestionsList.Visibility = Visibility.Collapsed;
        MentionSuggestionsList.ItemsSource = null;
    }

    private void MentionSuggestionsList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is MentionSuggestion suggestion)
            InsertMention(suggestion);
    }

    private void InsertMention(MentionSuggestion suggestion)
    {
        var text = PrimaryTextBox.Text;
        var caret = PrimaryTextBox.SelectionStart;
        var start = FindMentionStart(text, caret, out _);
        if (start < 0)
            start = mentionStart;
        if (start < 0 || start > text.Length)
            return;

        var end = Math.Max(start, Math.Min(caret, text.Length));
        var insert = "@" + suggestion.Handle + " ";
        mentionSearchCts?.Cancel();
        PrimaryTextBox.Text = text.Substring(0, start) + insert + text.Substring(end);
        PrimaryTextBox.SelectionStart = start + insert.Length;
        HideMentionSuggestions();
        PrimaryTextBox.Focus(FocusState.Programmatic);
    }

    private void MentionButton_Click(object sender, RoutedEventArgs e)
    {
        var text = PrimaryTextBox.Text ?? "";
        var caret = Math.Min(PrimaryTextBox.SelectionStart + PrimaryTextBox.SelectionLength, text.Length);
        var insert = caret > 0 && !char.IsWhiteSpace(text[caret - 1]) ? " @" : "@";
        PrimaryTextBox.Text = text.Substring(0, caret) + insert + text.Substring(caret);
        PrimaryTextBox.SelectionStart = caret + insert.Length;
        PrimaryTextBox.Focus(FocusState.Programmatic);
    }

    private async void OnPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var coreWindow = CoreWindow.GetForCurrentThread();
        if (MentionSuggestionsList.Visibility == Visibility.Visible)
        {
            if (e.Key is VirtualKey.Tab or VirtualKey.Enter &&
                !coreWindow.GetAsyncKeyState(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down) &&
                MentionSuggestionsList.ItemsSource is IList<MentionSuggestion> { Count: > 0 } items)
            {
                e.Handled = true;
                InsertMention(items[0]);
                return;
            }

            if (e.Key == VirtualKey.Escape)
            {
                e.Handled = true;
                HideMentionSuggestions();
                return;
            }
        }

        if (e.Key == VirtualKey.Enter)
        {
            var state = coreWindow.GetAsyncKeyState(VirtualKey.Control);
            if (state.HasFlag(CoreVirtualKeyStates.Down))
            {
                if (!ViewModel.CanPost)
                    return;

                e.Handled = true;

                await ViewModel.PostAsync();
            }
        }
    }
}
