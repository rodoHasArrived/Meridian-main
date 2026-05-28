using System.Threading;
using System.Threading.Tasks;

namespace Meridian.Wpf.Contracts;

/// <summary>
/// Defines the visible-page lifetime for WPF pages that own polling, streaming, or subscriptions.
/// </summary>
public interface IPageActivationLifetime
{
    bool IsActive { get; }

    CancellationToken ActivationToken { get; }

    Task ActivateAsync(CancellationToken ct = default);

    void Deactivate();
}
