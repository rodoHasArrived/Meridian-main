namespace Meridian.Contracts.Etl;

/// <summary>
/// Retains an imported source stream and returns its staged location and content checksum.
/// The caller owns the source stream; persistence is supplied by application composition.
/// </summary>
public interface IEtlStagingStore
{
    Task<EtlStagedFile> StageAsync(
        string jobId,
        EtlRemoteFile file,
        Stream sourceStream,
        CancellationToken ct = default);
}
