using System;
using System.Linq;
using System.Net.Http.Headers;
using System.ServiceModel;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FishyFlip;
using FishyFlip.Events;
using FishyFlip.Lexicon.Com.Atproto.Server;
using FishyFlip.Models;
using FishyFlip.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UniSky.Models;
using UniSky.Moderation;
using Windows.System.UserProfile;

namespace UniSky.Services;

public class ProtocolService(ILogger<ProtocolService> logger) : IProtocolService
{
    private readonly SemaphoreSlim refreshTokenSemaphore = new SemaphoreSlim(1, 1);

    private ATProtocol _protocol = null;
    private DateTimeOffset _lastRefreshed;

    public ATProtocol Protocol
        => _protocol ?? throw new InvalidOperationException("Protocol not yet initialized.");

    public void SetProtocol(ATProtocol protocol)
    {
        if (_protocol != null)
        {
            _protocol.SessionUpdated -= OnSessionUpdated;
        }

        protocol.SessionUpdated += OnSessionUpdated;

        protocol.Client.DefaultRequestHeaders.AcceptLanguage.Clear();
        foreach (var item in GlobalizationPreferences.Languages)
        {
            var idx = item.IndexOf('-');
            if (idx > 0)
            {
                var langCode = item.Substring(0, idx);
                protocol.Client.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue(langCode));
            }

            protocol.Client.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue(item));
        }

        _protocol = protocol;
    }

    public async Task RefreshSessionAsync(SessionModel sessionModel)
    {
        //var protocol = Protocol;

        // make sure we're not doing this multiple times
        await refreshTokenSemaphore.WaitAsync();

        try
        {
            // dont refresh more than once per hour
            // TODO: could maybe use the session expiry date passed but that seems a little bit prone to failure
            if ((DateTimeOffset.Now - _lastRefreshed) < TimeSpan.FromHours(1))
                return;

            _lastRefreshed = DateTimeOffset.Now;

            await DoRefreshAsync(logger, GetLatestSession(sessionModel))
                .ConfigureAwait(false);
        }
        finally
        {
            refreshTokenSemaphore.Release();
        }
    }

    public async Task EnsureSessionAsync(SessionModel sessionModel)
    {
        await refreshTokenSemaphore.WaitAsync();

        try
        {
            var latest = GetLatestSession(sessionModel);
            if (!IsAccessTokenValid(latest.AccessJwt, TimeSpan.FromMinutes(5)))
            {
                await DoRefreshAsync(logger, latest)
                    .ConfigureAwait(false);
                return;
            }

            // refresh tokens are single use, rotating them here would invalidate the copy held by the app
            var protocol = new ATProtocolBuilder(Protocol.Options)
                .Build();

            var session = await protocol.AuthenticateWithPasswordSessionResultAsync(latest.Session)
                .ConfigureAwait(false);
            if (session == null)
                throw new InvalidOperationException("Authentication failed!");

            SetProtocol(protocol);
        }
        finally
        {
            refreshTokenSemaphore.Release();
        }
    }

    public async Task SyncSessionAsync()
    {
        if (_protocol?.SessionManager is not PasswordSessionManager manager || manager.Session == null)
            return;

        await refreshTokenSemaphore.WaitAsync();

        try
        {
            var current = manager.Session;
            var sessionService = ServiceContainer.Scoped.GetRequiredService<ISessionService>();
            if (sessionService.TryFindSession(current.Did.Handler, out var stored)
                && !string.IsNullOrEmpty(stored.RefreshJwt)
                && stored.RefreshJwt != current.RefreshJwt)
            {
                logger.LogInformation("Session tokens were updated elsewhere, using stored tokens.");
                current = stored.Session.Session;
                manager.SetSession(current);
            }

            if (IsAccessTokenValid(current.AccessJwt, TimeSpan.FromMinutes(10)))
                return;

            // renew on a temporary protocol, FishyFlip swaps the shared client's bearer to the refresh token while refreshing
            var temporaryProtocol = new ATProtocolBuilder(_protocol.Options)
                .Build();

            await temporaryProtocol.AuthenticateWithPasswordSessionResultAsync(new AuthSession(
                new Session(current.Did,
                            current.DidDoc,
                            current.Handle,
                            current.Email,
                            current.RefreshJwt,
                            current.RefreshJwt,
                            current.ExpiresIn)))
                .ConfigureAwait(false);

            var refreshed = (await temporaryProtocol.RefreshSessionAsync()
                .ConfigureAwait(false))
                .HandleResult();

            manager.SetSession(new Session(refreshed.Did,
                                           refreshed.DidDoc ?? current.DidDoc,
                                           refreshed.Handle,
                                           current.Email,
                                           refreshed.AccessJwt,
                                           refreshed.RefreshJwt,
                                           DateTime.Now + TimeSpan.FromHours(2)));

            _lastRefreshed = DateTimeOffset.Now;
            logger.LogInformation("Successfully renewed session.");
        }
        finally
        {
            refreshTokenSemaphore.Release();
        }
    }

    private static SessionModel GetLatestSession(SessionModel sessionModel)
    {
        // the background task and the app run in separate processes, so always use the newest saved tokens
        var sessionService = ServiceContainer.Scoped.GetService<ISessionService>();
        if (sessionService != null && sessionService.TryFindSession(sessionModel.DID, out var latest))
            return latest;

        return sessionModel;
    }

    internal static bool IsAccessTokenValid(string jwt, TimeSpan margin)
    {
        try
        {
            var parts = jwt?.Split('.');
            if (parts == null || parts.Length < 2)
                return false;

            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + ((4 - (payload.Length % 4)) % 4), '=');

            using var document = JsonDocument.Parse(Convert.FromBase64String(payload));
            if (!document.RootElement.TryGetProperty("exp", out var exp) || !exp.TryGetInt64(out var seconds))
                return false;

            return DateTimeOffset.FromUnixTimeSeconds(seconds) - margin > DateTimeOffset.UtcNow;
        }
        catch
        {
            return false;
        }
    }

    private async Task DoRefreshAsync(ILogger<ProtocolService> logger, SessionModel sessionModel)
    {
        // to ensure the session gets refreshed properly:
        // - initially authenticate the client with the refresh token
        // - refresh the sesssion
        // - reauthenticate with the new session
        var temporaryProtocol = new ATProtocolBuilder(Protocol.Options)
            .Build();

        var sessionRefresh = sessionModel.Session.Session;
        var refreshSession = new AuthSession(
            new Session(sessionRefresh.Did,
                        sessionRefresh.DidDoc,
                        sessionRefresh.Handle,
                        sessionRefresh.Email,
                        sessionRefresh.RefreshJwt,
                        sessionRefresh.RefreshJwt,
                        sessionRefresh.ExpiresIn));

        await temporaryProtocol.AuthenticateWithPasswordSessionResultAsync(refreshSession)
            .ConfigureAwait(false);

        var refreshedSession = (await temporaryProtocol.RefreshSessionAsync()
            .ConfigureAwait(false))
            .HandleResult();

        var authSession2 = new AuthSession(
                new Session(refreshedSession.Did,
                            refreshedSession.DidDoc ?? sessionRefresh.DidDoc,
                            refreshedSession.Handle,
                            sessionRefresh.Email,
                            refreshedSession.AccessJwt,
                            refreshedSession.RefreshJwt,
                            DateTime.Now + TimeSpan.FromHours(2)));

        var session2 = await temporaryProtocol.AuthenticateWithPasswordSessionResultAsync(authSession2)
            .ConfigureAwait(false);
        if (session2 == null)
            throw new InvalidOperationException("Authentication failed!");

        logger.LogInformation("Successfully refreshed session.");

        var sessionModel2 = new SessionModel(true, sessionModel.Service, authSession2.Session, authSession2);
        var sessionService = ServiceContainer.Scoped.GetRequiredService<ISessionService>();
        sessionService.SaveSession(sessionModel2);

        SetProtocol(temporaryProtocol);

        var moderationService = ServiceContainer.Default.GetService<IModerationService>();
        if (moderationService != null)
        {
            await moderationService.ConfigureModerationAsync()
                .ConfigureAwait(false);
        }
    }

    private void OnSessionUpdated(object sender, SessionUpdatedEventArgs e)
    {
        logger.LogInformation("Session updated, saving new tokens!");

        var session = new SessionModel(true, e.InstanceUri.Host.ToLowerInvariant(), e.Session.Session, e.Session);
        var sessionService = ServiceContainer.Scoped.GetRequiredService<ISessionService>();
        sessionService.SaveSession(session);
    }
}
