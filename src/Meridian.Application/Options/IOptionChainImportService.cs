using Meridian.Contracts.Domain.Models;
using Meridian.Contracts.Options;

namespace Meridian.Application.Options;

public interface IOptionChainImportService
{
    Task<OptionChainSnapshotDto> ImportSnapshotAsync(OptionChainSnapshot snapshot, CancellationToken ct = default);
}
