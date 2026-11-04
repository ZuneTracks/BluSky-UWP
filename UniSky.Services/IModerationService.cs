using System.Collections.Generic;
using System.Threading.Tasks;
using FishyFlip.Lexicon.App.Bsky.Labeler;
using FishyFlip.Models;
using UniSky.Moderation;

namespace UniSky.Services;

public interface IModerationService
{
    ModerationOptions ModerationOptions { get; set; }

    Task ConfigureModerationAsync();

    /// <summary>
    /// Adds a post to the account's hidden posts preference and filters it immediately.
    /// </summary>
    Task HidePostAsync(ATUri uri);

    bool TryGetDisplayNameForLabeler(InterpretedLabelValueDefinition labelDef, out string displayName);

    bool TryGetLocalisedStringsForLabel(InterpretedLabelValueDefinition labelDef, out LabelStrings label);
}