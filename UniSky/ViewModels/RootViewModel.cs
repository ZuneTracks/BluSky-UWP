using System;
using System.Threading.Tasks;
using FishyFlip;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UniSky.Extensions;
using UniSky.Pages;
using UniSky.Services;
using Windows.UI.Xaml;

namespace UniSky.ViewModels;

public interface IRootNavigator
{
    Task GoToHomeAsync(string did = null);
    Task GoToLoginAsync();
}

public partial class RootViewModel : ViewModelBase, IRootNavigator
{
    private readonly IServiceProvider services;
    private readonly ILogger<RootViewModel> logger;
    private readonly ISettingsService settingsService;
    private readonly IProtocolService protocolService;
    private readonly ISessionService sessionService;
    private readonly INavigationService navigationService;

    private readonly DispatcherTimer refreshTokenTimer;

    public RootViewModel(IServiceProvider services,
                         ILogger<RootViewModel> logger,
                         ISettingsService settingsService,
                         IProtocolService protocolService,
                         ISessionService sessionService,
                         INavigationServiceLocator navigationServiceLocator)
    {
        this.services = services;
        this.logger = logger;
        this.protocolService = protocolService;
        this.settingsService = settingsService;
        this.sessionService = sessionService;
        this.navigationService = navigationServiceLocator.GetNavigationService("Root");

        this.refreshTokenTimer = new DispatcherTimer() { Interval = TimeSpan.FromMinutes(5) };
        this.refreshTokenTimer.Tick += OnRefreshTokenTimerTick;
        Application.Current.Resuming += OnRefreshTokenTimerTick;

        if (!settingsService.TryRead<string>("LastUsedUser", out var session))
        {
            Task.Run(() => GoToLoginAsync());
            return;
        }

        Task.Run(() => GoToHomeAsync());
    }

    public async Task GoToHomeAsync(string did = null)
    {
        using var loading = this.GetLoadingContext();

        try
        {
            if (did == null && !settingsService.TryRead("LastUsedUser", out did))
            {
                await GoToLoginAsync();
                return;
            }

            if (!sessionService.TryFindSession(did, out var session))
            {
                await GoToLoginAsync();
                return;
            }

            settingsService.Save<string>("LastUsedUser", did);

            var logger = services.GetRequiredService<ILogger<ATProtocol>>();
            var protocol = new ATProtocolBuilder()
                .WithLogger(logger)
                .EnableAutoRenewSession(true)
                .WithUserAgent(Constants.UserAgent)
                .Build();

            protocolService.SetProtocol(protocol);

            await protocolService.RefreshSessionAsync(session);

            syncContext.Post(() =>
            {
                navigationService.Navigate<HomePage>(CreateHomeViewModel());
                refreshTokenTimer.Start();
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to authenticate!");
            await GoToLoginAsync();
            return;
        }
    }

    public Task GoToLoginAsync()
    {
        syncContext.Post(() => navigationService.Navigate<LoginPage>(CreateLoginViewModel()));
        return Task.CompletedTask;
    }

    private async void OnRefreshTokenTimerTick(object sender, object e)
    {
        try
        {
            await protocolService.SyncSessionAsync()
                                 .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // a failed renewal is retried on the next tick, FishyFlip also renews on expired token errors
            logger.LogError(ex, "Failed to renew session!");
        }
    }

    private LoginViewModel CreateLoginViewModel()
        => ActivatorUtilities.CreateInstance<LoginViewModel>(ServiceContainer.Scoped, (IRootNavigator)this);
    private HomeViewModel CreateHomeViewModel()
        => ActivatorUtilities.CreateInstance<HomeViewModel>(ServiceContainer.Scoped);
}
