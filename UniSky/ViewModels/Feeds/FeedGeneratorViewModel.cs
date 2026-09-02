using FishyFlip.Lexicon.App.Bsky.Feed;
using FishyFlip.Models;

namespace UniSky.ViewModels.Feeds;

public sealed class FeedGeneratorViewModel
{
    public FeedGeneratorViewModel(GeneratorView generator)
    {
        Generator = generator;
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
}
