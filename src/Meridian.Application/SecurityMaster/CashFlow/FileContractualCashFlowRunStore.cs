using System.Text.Json;
using Meridian.Contracts.CashFlows;
using Meridian.Storage;

namespace Meridian.Application.SecurityMaster.CashFlow;

/// <summary>Write-once replay packets. A retry reads its retained packet before looking up live inputs.</summary>
public sealed class FileContractualCashFlowRunStore(StorageOptions options) : IContractualCashFlowRunStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly string _root = Path.Combine(options.RootPath, "contractual-cashflow-runs");

    public async Task<ContractualCashFlowRun?> GetAsync(string subjectKind, Guid subjectId, Guid runId, CancellationToken ct = default)
    {
        var path = FilePath(subjectKind, subjectId, runId);
        if (!File.Exists(path))
            return null;
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous);
        var retained = await JsonSerializer.DeserializeAsync<ContractualCashFlowRun>(input, Json, ct).ConfigureAwait(false)
            ?? throw new InvalidDataException("Empty contractual cash-flow run.");
        if (retained.SubjectKind != subjectKind || retained.SubjectId != subjectId || retained.RunId != runId
            || !ContractualCashFlowEngine.Verify(retained))
            throw new InvalidDataException("Contractual cash-flow replay packet failed identity or output verification.");
        return retained;
    }

    public async Task<ContractualCashFlowRun> SaveAsync(ContractualCashFlowRun run, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(run.Actor) || !run.Result.IsCalculated || !ContractualCashFlowEngine.Verify(run))
            throw new ArgumentException("Only a calculated, reproducible run with an authenticated actor may be retained.");
        var path = FilePath(run.SubjectKind, run.SubjectId, run.RunId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var staging = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var file = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(file, run, Json, ct).ConfigureAwait(false);
                await file.FlushAsync(ct).ConfigureAwait(false);
                file.Flush(flushToDisk: true);
            }
            try
            { File.Move(staging, path, overwrite: false); }
            catch (IOException) when (File.Exists(path))
            {
                var retained = await GetAsync(run.SubjectKind, run.SubjectId, run.RunId, ct).ConfigureAwait(false);
                if (retained is null || ContractualCashFlowEngine.Hash(retained.Request) != ContractualCashFlowEngine.Hash(run.Request))
                    throw new InvalidOperationException("Run identity is already bound to a different projection request.");
                return retained;
            }
            return run;
        }
        finally { if (File.Exists(staging)) File.Delete(staging); }
    }

    private string FilePath(string kind, Guid subjectId, Guid runId)
    {
        if (kind is not ("Security" or "Loan") || subjectId == Guid.Empty || runId == Guid.Empty)
            throw new ArgumentException("A supported subject kind and non-empty subject/run identities are required.");
        return Path.Combine(_root, kind, subjectId.ToString("N"), runId.ToString("N") + ".json");
    }
}
