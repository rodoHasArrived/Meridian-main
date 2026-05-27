using Meridian.Contracts.Domain.Models;
using Meridian.Contracts.Options;

namespace Meridian.Application.Options;

public interface IOptionChainImportService
{
    /// <summary>
    /// Persists a chain snapshot and returns the normalized chain identifier, underlying symbol, and projected contracts.
    /// </summary>
    Task<OptionChainSnapshotDto> ImportSnapshotAsync(OptionChainSnapshot snapshot, CancellationToken ct = default);
}
