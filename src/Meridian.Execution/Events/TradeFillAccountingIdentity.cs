using Meridian.Contracts.Integrity;

namespace Meridian.Execution.Events;

/// <summary>Shared deterministic identities for runtime and canonical accounting lots.</summary>
internal static class TradeFillAccountingIdentity
{
    internal static Guid Create(string postingScope, Guid fillId, string component)
        => new(Sha256Digest.ComputeBytesUtf8($"{postingScope}|{fillId:D}|{component}")[..16]);
}
