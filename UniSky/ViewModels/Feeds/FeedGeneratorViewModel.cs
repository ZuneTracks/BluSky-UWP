using CommunityToolkit.Mvvm.ComponentModel;
using FishyFlip.Lexicon.App.Bsky.Feed;
using FishyFlip.Models;

namespace UniSky.ViewModels.Feeds;

public sealed partial class FeedGeneratorViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PinLabel))]
    private bool isPinned;

    public FeedGeneratorViewModel(GeneratorView generator, bool isPinned = false)
    {
        Generator = generator;
        this.isPinned = isPinned;
    }

    public GeneratorView Generator { get; }

    public ATUri Uri => Generator.Uri;

    public string Name
        => string.IsNullOrWhiteSpace(Generator.DisplayName) ? Uri.ToString() : Generator.DisplayName;

    public string Description => Generator.Description ?? string.Empty;

    public string Creator
        => Generator.Creator is { } creator
            ? creator.DisplayName ?? creator.Handle.ToString()
            : string.Empty;

    public string PinLabel => IsPinned ? "Unpin" : "Pin";
}
