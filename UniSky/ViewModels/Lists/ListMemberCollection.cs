using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using FishyFlip.Lexicon.App.Bsky.Actor;
using FishyFlip.Models;
using FishyFlip.Tools;
using Microsoft.Extensions.DependencyInjection;
using UniSky.Moderation;
using UniSky.Services;
using UniSky.ViewModels.Profile;
using Windows.Foundation;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Data;

namespace UniSky.ViewModels.Lists;

public sealed class ListMemberCollection : ObservableCollection<ProfileViewModel>, ISupportIncrementalLoading
{
    private readonly SemaphoreSlim semaphore = new(1, 1);
    private readonly CoreDispatcher dispatcher = Window.Current.Dispatcher;
    private readonly HashSet<string> ids = [];
    private readonly ListPageViewModel parent;
    private readonly ATUri uri;
    private readonly IProtocolService protocolService = ServiceContainer.Scoped.GetRequiredService<IProtocolService>();
    private readonly IModerationService moderationService = ServiceContainer.Scoped.GetRequiredService<IModerationService>();
    private string cursor;

    public ListMemberCollection(ListPageViewModel parent, ATUri uri)
    {
        this.parent = parent;
        this.uri = uri;
    }

    public bool HasMoreItems { get; private set; } = true;

    public IAsyncOperation<LoadMoreItemsResult> LoadMoreItemsAsync(uint count)
        => Task.Run(async () =>
        {
            await semaphore.WaitAsync();
            try
            {
                return await LoadMoreAsync((int)count);
            }
            finally
            {
                semaphore.Release();
            }
        }).AsAsyncOperation();

    private async Task<LoadMoreItemsResult> LoadMoreAsync(int count)
    {
        parent.Error = null;
        count = Math.Clamp(count, 5, 100);

        using var loading = parent.GetLoadingContext();
        try
        {
            var response = await protocolService.Protocol.Graph.GetListAsync(uri, limit: count, cursor: cursor)
                .ConfigureAwait(false);
            if (!response.IsT0 || response.AsT0 is not { } results)
            {
                HasMoreItems = false;
                parent.OnLoadError(response.AsT1);
                return new LoadMoreItemsResult();
            }

            cursor = results.Cursor;
            parent.Populate(results.List);
            await dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                foreach (var item in results.Items)
                {
                    if (item.Subject is not ProfileView profile || !ids.Add(profile.Did.ToString()))
                        continue;

                    if (new Moderator(moderationService.ModerationOptions)
                        .ModerateProfile(profile)
                        .GetUI(ModerationContext.ProfileList)
                        .Filter)
                        continue;

                    Add(new ProfileViewModel(parent.Navigation, profile));
                }
            });

            if (results.Items.Count == 0 || string.IsNullOrWhiteSpace(cursor))
                HasMoreItems = false;

            return new LoadMoreItemsResult { Count = (uint)results.Items.Count };
        }
        catch (Exception exception)
        {
            HasMoreItems = false;
            parent.OnLoadError(exception);
            return new LoadMoreItemsResult();
        }
    }
}
