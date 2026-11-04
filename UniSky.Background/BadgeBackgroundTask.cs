using System;
using System.Linq;
using FishyFlip;
using FishyFlip.Lexicon.App.Bsky.Notification;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UniSky.Services;
using Windows.ApplicationModel.Background;

namespace UniSky.Background;

public sealed class BadgeBackgroundTask : IBackgroundTask
{
    public BadgeBackgroundTask()
    {
        var collection = new ServiceCollection();
        collection.AddLogging(c => c.AddDebug()
            .SetMinimumLevel(LogLevel.Trace));

        collection.AddSingleton<IProtocolService, ProtocolService>();
        collection.AddSingleton<ISettingsService, SettingsService>();
        collection.AddSingleton<ITypedSettings, TypedSettingsService>();

        collection.AddTransient<ILoginService, LoginService>();
        collection.AddTransient<ISessionService, SessionService>();
        collection.AddTransient<IBadgeService, BadgeService>();

        ServiceContainer.Default.ConfigureServices(collection.BuildServiceProvider());
    }

    public async void Run(IBackgroundTaskInstance taskInstance)
    {
        var deferral = taskInstance.GetDeferral();
        var logger = ServiceContainer.Default.GetRequiredService<ILogger<BadgeBackgroundTask>>();
        try
        {
            var badgeCount = 0;
            var badgeService = ServiceContainer.Default.GetRequiredService<IBadgeService>();
            var sessionService = ServiceContainer.Default.GetRequiredService<ISessionService>();
            var protocolService = ServiceContainer.Default.GetRequiredService<IProtocolService>();
            var atLogger = ServiceContainer.Default.GetRequiredService<ILogger<ATProtocol>>();
            var settings = ServiceContainer.Default.GetRequiredService<ISettingsService>();
            var options = ServiceContainer.Default.GetRequiredService<ITypedSettings>().NotificationOptions;
            var notificationsEnabled = NotificationToaster.IsEnabled(options);

            var sessions = sessionService.EnumerateAllSessions().ToList();
            var toaster = new NotificationToaster(settings, logger, options, showAccount: sessions.Count > 1);

            foreach (var session in sessions)
            {
                if (!notificationsEnabled)
                    NotificationToaster.Reset(settings, session.DID);

                try
                {
                    protocolService.SetProtocol(new ATProtocolBuilder()
                        .WithLogger(atLogger)
                        .EnableAutoRenewSession(true)
                        .WithUserAgent(Constants.UserAgent)
                        .Build());

                    await protocolService.RefreshSessionAsync(session);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to configure session for {Did}", session.DID);
                    continue;
                }

                try
                {
                    var protocol = protocolService.Protocol;
                    var notifications = (await protocol.GetUnreadCountAsync()
                        .ConfigureAwait(false))
                        .HandleResult();

                    var unread = (int)Math.Min(notifications.Count, int.MaxValue);
                    badgeCount += unread;

                    if (notificationsEnabled)
                        await toaster.ShowNewAsync(protocol, session, unread).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to fetch notifications for {Did}", session.DID);
                    continue;
                }
            }

            badgeService.UpdateBadge(badgeCount);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"Failed to run {nameof(BadgeBackgroundTask)}");
        }
        finally
        {
            deferral.Complete();
        }
    }
}
