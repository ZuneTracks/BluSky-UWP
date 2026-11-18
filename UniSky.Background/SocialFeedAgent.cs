using System;
using System.Threading.Tasks;
using Windows.ApplicationModel.AppService;
using Windows.ApplicationModel.Background;
using Windows.ApplicationModel.SocialInfo;
using Windows.ApplicationModel.SocialInfo.Provider;
using Windows.Foundation.Collections;
using Windows.Storage;

namespace UniSky.Background;

#pragma warning disable CS0618 // the SocialInfo APIs are deprecated but still present on Windows 10 Mobile

/// <summary>
/// App service the Windows 10 Mobile People app calls ("com.microsoft.windows.social-feeds") to pull feed items.
/// Protocol follows microsoft/Windows-Social-Samples SocialInfoSampleApp. Currently returns a hard-coded test item.
/// </summary>
public sealed class SocialFeedAgent : IBackgroundTask
{
    private const string LastActivityKey = "SocialFeedAgent.LastActivity";

    private const uint MajorVersion = 1;
    private const uint ProtocolVersion = MajorVersion << 16;

    private const uint GetNext = 0x10;
    private const uint DownloadHomeFeed = 0x11;
    private const uint DownloadContactFeed = 0x13;
    private const uint DownloadDashboardFeed = 0x15;
    private const uint OperationResult = 0x80;
    private const uint GoodBye = 0xF1;

    public async void Run(IBackgroundTaskInstance taskInstance)
    {
        var deferral = taskInstance.GetDeferral();
        var connection = (taskInstance.TriggerDetails as AppServiceTriggerDetails)?.AppServiceConnection;
        Record("started");

        try
        {
            while (connection != null)
            {
                var response = await connection.SendMessageAsync(new ValueSet
                {
                    ["Type"] = GetNext,
                    ["Version"] = ProtocolVersion,
                });

                if (response?.Status != AppServiceResponseStatus.Success)
                {
                    Record($"GetNext failed: {response?.Status}");
                    break;
                }

                var message = response.Message;
                var type = (uint)message["Type"];
                if (type == GoodBye)
                {
                    Record("goodbye");
                    break;
                }

                if (type != DownloadHomeFeed && type != DownloadContactFeed && type != DownloadDashboardFeed)
                {
                    Record($"unknown op 0x{type:X}");
                    break;
                }

                var operationId = (uint)message["OperationId"];
                var owner = message["OwnerRemoteId"] as string;
                uint error = 0;
                try
                {
                    await DownloadAsync(type, owner);
                    Record($"served op 0x{type:X} for {owner}");
                }
                catch (Exception ex)
                {
                    error = (uint)ex.HResult;
                    Record($"op 0x{type:X} failed 0x{ex.HResult:X8} {ex.Message}");
                }

                await connection.SendMessageAsync(new ValueSet
                {
                    ["Type"] = OperationResult,
                    ["Version"] = ProtocolVersion,
                    ["ErrorCode"] = error,
                    ["OperationId"] = operationId,
                });
            }
        }
        catch (Exception ex)
        {
            Record($"crashed 0x{ex.HResult:X8} {ex.Message}");
        }
        finally
        {
            deferral.Complete();
        }
    }

    private static async Task DownloadAsync(uint type, string owner)
    {
        var target = new Uri("https://bsky.app/");

        if (type == DownloadDashboardFeed)
        {
            var dashboard = await SocialInfoProviderManager.CreateDashboardItemUpdaterAsync(owner);
            dashboard.Content.Title = "BluSky";
            dashboard.Content.Message = "BluSky test dashboard item";
            dashboard.Timestamp = DateTimeOffset.Now;
            dashboard.TargetUri = target;
            await dashboard.CommitAsync();
            return;
        }

        var kind = type == DownloadHomeFeed ? SocialFeedKind.HomeFeed : SocialFeedKind.ContactFeed;
        var updater = await SocialInfoProviderManager.CreateSocialFeedUpdaterAsync(kind, SocialFeedUpdateMode.Replace, owner);

        var item = new SocialFeedItem
        {
            RemoteId = "blusky-test-item",
            Timestamp = DateTimeOffset.Now,
            TargetUri = target,
        };
        item.Author.DisplayName = "BluSky";
        item.Author.RemoteId = owner;
        item.PrimaryContent.Title = "BluSky test";
        item.PrimaryContent.Message = "If you can see this in What's New, the People app integration works.";

        updater.Items.Add(item);
        await updater.CommitAsync();
    }

    private static void Record(string text)
    {
        try
        {
            ApplicationData.Current.LocalSettings.Values[LastActivityKey] = $"{DateTimeOffset.Now:g}: {text}";
        }
        catch { }
    }
}
