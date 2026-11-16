using System;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;

namespace UniSky.ViewModels.Compose;

public class MentionSuggestion
{
    public string Handle { get; }
    public string AtHandle { get; }
    public string DisplayName { get; }
    public ImageSource Avatar { get; }

    public MentionSuggestion(string handle, string displayName, string avatarUrl)
    {
        Handle = handle;
        AtHandle = "@" + handle;
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? handle : displayName;
        if (Uri.TryCreate(avatarUrl, UriKind.Absolute, out var uri))
            Avatar = new BitmapImage(uri) { DecodePixelWidth = 32, DecodePixelType = DecodePixelType.Logical };
    }
}