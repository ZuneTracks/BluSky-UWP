using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FishyFlip;
using FishyFlip.Lexicon;
using FishyFlip.Lexicon.App.Bsky.Graph;
using FishyFlip.Models;
using FishyFlip.Tools;
using Microsoft.Extensions.DependencyInjection;
using UniSky.Services;
using GraphList = FishyFlip.Lexicon.App.Bsky.Graph.List;

namespace UniSky.ViewModels.Lists;

/// <summary>
/// Writes list and list membership records to the signed-in user's repository.
/// Failures are thrown as <see cref="ListActionException"/> with a user-readable message.
/// </summary>
internal static class ListActions
{
    public const int MaxNameLength = 64;
    public const int MaxDescriptionLength = 300;

    private static ATProtocol Protocol
        => ServiceContainer.Scoped.GetRequiredService<IProtocolService>().Protocol;

    public static ATDid SessionDid
        => Protocol.Session?.Did;

    public static bool IsOwnedBySession(ListView list)
        => list?.Creator?.Did is ATDid creator
            && SessionDid is ATDid me
            && creator.ToString() == me.ToString();

    public static async Task<ATUri> CreateAsync(string purpose, string name, string description)
    {
        var result = await Protocol.Graph.CreateListAsync(
            purpose,
            name.Trim(),
            NullIfEmpty(description),
            createdAt: DateTime.UtcNow).ConfigureAwait(false);

        return Unwrap(result).Uri;
    }

    public static async Task UpdateAsync(ATUri list, string name, string description)
    {
        var protocol = Protocol;
        var existing = Unwrap(await protocol.Graph.GetListAsync(protocol.Session.Did, list.Rkey).ConfigureAwait(false));
        if (existing.Value is not GraphList record)
            throw new ListActionException("This list could not be found.");

        record.Name = name.Trim();
        record.Description = NullIfEmpty(description);
        // Facet byte ranges point into the old description text, so they can't be kept.
        record.DescriptionFacets = null;

        Unwrap(await protocol.Graph.PutListAsync(list.Rkey, record, swapRecord: existing.Cid).ConfigureAwait(false));
    }

    public static async Task DeleteAsync(ATUri list)
    {
        var protocol = Protocol;
        var itemKeys = new List<string>();
        string cursor = null;
        do
        {
            var page = Unwrap(await protocol.Graph.GetListAsync(list, 100, cursor).ConfigureAwait(false));
            foreach (var item in page.Items)
            {
                if (item.Uri?.Rkey is string rkey)
                    itemKeys.Add(rkey);
            }

            cursor = page.Items.Count == 0 ? null : page.Cursor;
        }
        while (!string.IsNullOrWhiteSpace(cursor));

        foreach (var rkey in itemKeys)
            Unwrap(await protocol.Graph.DeleteListitemAsync(rkey).ConfigureAwait(false));

        Unwrap(await protocol.Graph.DeleteListAsync(list.Rkey).ConfigureAwait(false));
    }

    public static async Task<ATUri> AddMemberAsync(ATUri list, ATDid subject)
    {
        var result = await Protocol.Graph.CreateListitemAsync(subject, list, DateTime.UtcNow).ConfigureAwait(false);
        return Unwrap(result).Uri;
    }

    public static async Task RemoveMemberAsync(ATUri listItem)
    {
        Unwrap(await Protocol.Graph.DeleteListitemAsync(listItem.Rkey).ConfigureAwait(false));
    }

    private static string NullIfEmpty(string value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static T Unwrap<T>(Result<T> result)
    {
        if (result.IsT0)
            return result.AsT0;

        var error = result.AsT1;
        throw new ListActionException(error?.Detail?.Message
            ?? error?.StatusCode.ToString()
            ?? "Something went wrong.");
    }
}

internal sealed class ListActionException(string message) : Exception(message);
