using System.Threading.Tasks;

namespace UniSky.Services;

/// <summary>
/// Implemented by tab root pages that should reload when their tab is shown again.
/// </summary>
internal interface IRefreshOnShow
{
    Task RefreshOnShowAsync();
}
