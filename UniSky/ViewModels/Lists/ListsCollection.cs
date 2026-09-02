using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using FishyFlip.Lexicon.App.Bsky.Graph;
using FishyFlip.Models;
using FishyFlip.Tools;
using Microsoft.Extensions.DependencyInjection;
using UniSky.Moderation;
using UniSky.Services;
using Windows.Foundation;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Data;

namespace UniSky.ViewModels.Lists;

public sealed class ListsCollection : ObservableCollection<UserListViewModel>, ISupportIncrementalLoading
{
    private readonly SemaphoreSlim semaphore = new(1, 1);
    private readonly CoreDispatcher dispatcher = Window.Current.Dispatcher;
    private readonly HashSet<string> ids = [];
    private readonly ListsPageViewModel parent;
    private readonly IProtocolService protocolService = ServiceContainer.Scoped.GetRequiredService<IProtocolService>();
    private readonly IModerationService moderationService = ServiceContainer.Scoped.GetRequiredService<IModerationService>();
    private string cursor;

    public ListsCollection(ListsPageViewModel parent)
    {
        this.parent = parent;
    }

    public bool HasMoreItems { get; private set; } = true;

    public async Task RefreshAsync()
    {
        if (!await semaphore.WaitAsync(10))
            return;

        try
        {
            cursor = null;
            ids.Clear();
            HasMoreItems = true;
            await dispatcher.RunAsync(CoreDispatcherPriority.Normal, Clear);
            await LoadMoreAsync(25);
        }
        finally
        {
            semaphore.Release();
        }
    }

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
            var response = await protocolService.Protocol.Graph.GetListsAsync(
                protocolService.Protocol.Session.Did,
                limit: count,
                cursor: cursor).ConfigureAwait(false);
            if (!response.IsT0 || response.AsT0 is not { } results)
            {
                HasMoreItems = false;
                parent.OnLoadError(response.AsT1);
                return new LoadMoreItemsResult();
            }

            cursor = results.Cursor;
            await dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                foreach (var list in results.Lists)
                {
                    if (list.Uri == null || !ids.Add(list.Uri.ToString()))
                        continue;

                    var item = new UserListViewModel(parent.Navigation, list);
                    if (item.IsVisible(moderationService))
                        Add(item);
                }
            });

            if (results.Lists.Count == 0 || string.IsNullOrWhiteSpace(cursor))
                HasMoreItems = false;

            return new LoadMoreItemsResult { Count = (uint)results.Lists.Count };
        }
        catch (Exception exception)
        {
            HasMoreItems = false;
            parent.OnLoadError(exception);
            return new LoadMoreItemsResult();
        }
        finally
        {
            parent.IsEmpty = Count == 0;
        }
    }
}
