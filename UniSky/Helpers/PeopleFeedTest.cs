using System;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using UniSky.Services;
using Windows.ApplicationModel.SocialInfo;
using Windows.ApplicationModel.SocialInfo.Provider;
using Windows.Foundation.Metadata;

namespace UniSky.Helpers;

#pragma warning disable CS0618 // the SocialInfo APIs are deprecated, which is what this test is checking

/// <summary>
/// Experiment: checks whether the Windows 10 Mobile People app still accepts a "What's New" feed from third-party apps.
/// </summary>
internal static class PeopleFeedTest
{
    public static async Task<string> RunAsync()
    {
        var log = new StringBuilder();

        if (!ApiInformation.IsTypePresent("Windows.ApplicationModel.SocialInfo.Provider.SocialInfoProviderManager"))
        {
            log.AppendLine("1. API present: NO. This device doesn't support People app feeds.");
            return log.ToString();
        }
        log.AppendLine("1. API present: yes");

        var session = ServiceContainer.Scoped.GetRequiredService<IProtocolService>().Protocol.Session;
        var did = session?.Did?.ToString();
        var handle = session?.Handle?.ToString();
        if (string.IsNullOrWhiteSpace(did))
        {
            log.AppendLine("2. Signed in: NO. Sign in first.");
            return log.ToString();
        }

        if (!await StepAsync(log, "2. Provision", async () => await SocialInfoProviderManager.ProvisionAsync()))
            return log.ToString();

        if (!await StepAsync(log, "3. Push test item", async () =>
        {
            var updater = await SocialInfoProviderManager.CreateSocialFeedUpdaterAsync(
                SocialFeedKind.HomeFeed, SocialFeedUpdateMode.Replace, did);

            var item = new SocialFeedItem
            {
                RemoteId = "blusky-test-" + DateTime.UtcNow.Ticks,
                Timestamp = DateTimeOffset.Now,
                TargetUri = new Uri("https://bsky.app/profile/" + (handle ?? did)),
            };
            item.Author.DisplayName = "BluSky";
            item.Author.UserName = handle ?? did;
            item.Author.RemoteId = did;
            item.PrimaryContent.Title = "BluSky test";
            item.PrimaryContent.Message = "If you can see this in the People app's What's New, the feed works.";

            updater.Items.Add(item);
            await updater.CommitAsync();
            return true;
        }))
            return log.ToString();

        await StepAsync(log, "4. Notify People app", () =>
        {
            SocialInfoProviderManager.ReportNewContentAvailable(did, SocialFeedKind.HomeFeed);
            return Task.FromResult(true);
        });

        log.AppendLine();
        log.AppendLine("Now open People > What's New and look for a 'BluSky test' item.");
        return log.ToString();
    }

    private static async Task<bool> StepAsync(StringBuilder log, string name, Func<Task<bool>> step)
    {
        try
        {
            var ok = await step();
            log.AppendLine($"{name}: {(ok ? "yes" : "returned false")}");
            return ok;
        }
        catch (Exception ex)
        {
            log.AppendLine($"{name}: FAILED 0x{ex.HResult:X8} {ex.Message}");
            return false;
        }
    }
}
