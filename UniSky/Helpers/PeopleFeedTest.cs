using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using UniSky.Services;
using Windows.ApplicationModel.Contacts;
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
    private const string ListName = "BluSky";

    public static async Task<string> RunAsync()
    {
        var log = new StringBuilder();
        var lastActivity = Windows.Storage.ApplicationData.Current.LocalSettings.Values["SocialFeedAgent.LastActivity"] as string;
        log.AppendLine("App service last called: " + (lastActivity ?? "never"));
        log.AppendLine();

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

        ContactList contactList = null;
        if (!await StepAsync(log, "2. Contact list", async () =>
        {
            var store = await ContactManager.RequestStoreAsync(ContactStoreAccessType.AppContactsReadWrite);
            if (store == null) return false;
            var lists = await store.FindContactListsAsync();
            contactList = lists.FirstOrDefault(l => l.DisplayName == ListName) ?? await store.CreateContactListAsync(ListName);
            return contactList != null;
        }))
            return log.ToString();

        ContactAnnotationList annotationList = null;
        if (!await StepAsync(log, "3. Annotation list", async () =>
        {
            var store = await ContactManager.RequestAnnotationStoreAsync(ContactAnnotationStoreAccessType.AppAnnotationsReadWrite);
            if (store == null) return false;
            var lists = await store.FindAnnotationListsAsync();
            annotationList = lists.FirstOrDefault() ?? await store.CreateAnnotationListAsync();
            return annotationList != null;
        }))
            return log.ToString();

        if (!await StepAsync(log, "4. Me contact + SocialFeeds", async () =>
        {
            var me = await contactList.GetMeContactAsync();
            me.Name = handle ?? did;
            me.RemoteId = did;
            await contactList.SaveContactAsync(me);

            var annotation = new ContactAnnotation
            {
                ContactId = me.Id,
                RemoteId = did,
                SupportedOperations = ContactAnnotationOperations.SocialFeeds,
            };
            return await annotationList.TrySaveAnnotationAsync(annotation);
        }))
            return log.ToString();

        if (!await StepAsync(log, "5. Provision", async () => await SocialInfoProviderManager.ProvisionAsync()))
            return log.ToString();
        await StepAsync(log, "6. Notify People app", () =>
        {
            SocialInfoProviderManager.ReportNewContentAvailable(did, SocialFeedKind.HomeFeed);
            return Task.FromResult(true);
        });

        log.AppendLine();
        log.AppendLine("Now open People > What's New and look for a 'BluSky test' item. People pulls it from the app service; reopen this test to see whether it was called.");
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
