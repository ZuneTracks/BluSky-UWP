using System.Threading.Tasks;
using FishyFlip;
using UniSky.Models;

namespace UniSky.Services;

public interface IProtocolService
{
    ATProtocol Protocol { get; }

    Task RefreshSessionAsync(SessionModel sessionModel);

    /// <summary>
    /// Authenticates with the stored session, only rotating tokens if the access token has expired.
    /// </summary>
    Task EnsureSessionAsync(SessionModel sessionModel);

    /// <summary>
    /// Picks up tokens rotated by another process and renews the current session shortly before it expires.
    /// </summary>
    Task SyncSessionAsync();

    void SetProtocol(ATProtocol protocol);
}