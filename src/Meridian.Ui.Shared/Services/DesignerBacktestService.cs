using System.Globalization;
using System.Text.Json;
using Meridian.Backtesting.Engine;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Domain.Models;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Operations;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Services;
using Meridian.Contracts.Workstation;
using Meridian.Core.Serialization;
using Meridian.Domain.Events;
using Meridian.Storage.Archival;
using Meridian.Storage.Replay;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Live.Designer;
using Meridian.Strategies.Models;
using Meridian.Strategies.Services;
using Meridian.Ui.Shared.Evidence;

namespace Meridian.Ui.Shared.Services;

/// <summary>Runs the hosted Designer's bounded daily-equity replay against retained, exact inputs.</summary>
public sealed class DesignerBacktestService(
    string dataRoot,
    Func<BacktestRequest, BacktestEngine> engineFactory,
    IStrategyRepository runs,
    IEvidenceArtifactStore evidence,
    StrategyDesignService designs,
    IOperationalTradingCalendar calendar,
    ISecurityIdentityQueryService? securities,
    ISecurityCorporateActionsQueryService? corporateActions)
{
    private const string DailyEquityDataset = "provider-bars/equities/daily";
    private const int MaxReplayDays = 366;
    private readonly SemaphoreSlim _replayAdmission = new(1, 1);

    public async Task<StrategyRunEntry> RunAsync(
        StrategyDesignDocument document,
        StrategyRunRepositoryScope scope,
        string actor,
        Guid accountId,
        DateOnly from,
        DateOnly to,
        StrategyRunEvidenceLoop evidenceLoop,
        CancellationToken ct)
    {
        if (!string.Equals(document.DatasetReference, DailyEquityDataset, StringComparison.Ordinal))
            throw new DesignerBacktestBlockedException("Choose the daily equity bars dataset.");

        if (from > to || to.DayNumber - from.DayNumber >= MaxReplayDays ||
            to >= DateOnly.FromDateTime(DateTime.UtcNow))
            throw new DesignerBacktestBlockedException("Choose a completed historical window of up to 366 calendar days.");

        if (!await _replayAdmission.WaitAsync(0, ct).ConfigureAwait(false))
            throw new DesignerBacktestBlockedException("Another Designer backtest is running. Try again when it completes.");

        try
        {
            return await ExecuteAsync(document, scope, actor, accountId, from, to, evidenceLoop, ct)
                .ConfigureAwait(false);
        }
        finally
        {
            _replayAdmission.Release();
        }
    }

    private async Task<StrategyRunEntry> ExecuteAsync(
        StrategyDesignDocument document,
        StrategyRunRepositoryScope scope,
        string actor,
        Guid accountId,
        DateOnly from,
        DateOnly to,
        StrategyRunEvidenceLoop evidenceLoop,
        CancellationToken ct)
    {
        var validation = designs.Validate(document);
        if (!DesignerDocumentLiveSource.TryCreatePaperStrategy(document, validation, out var strategy, out var reason))
            throw new DesignerBacktestBlockedException(reason ?? "The design has no executable paper strategy.");

        var manifests = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var reference in evidenceLoop.RetainedEvidenceReferences)
        {
            if (!EvidenceVaultReference.TryParseCanonical(reference, out var vaultId))
                throw new DesignerBacktestBlockedException("Choose a retained Evidence Vault manifest.");

            var manifest = await evidence.TryOpenManifestByVaultIdAsync(vaultId, scope.TenantId!, scope.CompanyId!, ct)
                .ConfigureAwait(false);
            if (manifest is null)
                throw new DesignerBacktestBlockedException("The selected Evidence Vault manifest is unavailable in this scope.");
            using (manifest.Content)
                manifests[reference] = await Sha256Digest.ComputeAsync(manifest.Content, ct).ConfigureAwait(false);
        }

        if (manifests.Count == 0)
            throw new DesignerBacktestBlockedException("Select at least one retained Evidence Vault manifest.");

        var symbol = document.Universe.Single();
        var snapshot = await CaptureReplayAsync(scope, symbol, from, to, ct).ConfigureAwait(false);
        using var replayDirectory = new ReplayDirectory(snapshot, symbol);
        await replayDirectory.StageAsync(ct).ConfigureAwait(false);
        var request = new BacktestRequest(
            from, to,
            Symbols: [symbol],
            DataRoot: replayDirectory.Root,
            DefaultExecutionModel: ExecutionModel.BarMidpoint,
            SlippageBasisPoints: 0m,
            AdjustForCorporateActions: false,
            FailOnUnknownSymbols: true,
            FillTiming: FillTiming.NextBar);
        var realism = request.ToRealismDescriptor();
        var realismHash = DesignerStrategyVersion.ComputeRealismHash(realism);
        var provenance = DataProvenance.Simulated.Token();

        var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [DesignerStrategyVersion.TenantKey] = scope.TenantId!,
            [DesignerStrategyVersion.CompanyKey] = scope.CompanyId!,
            [DesignerDocumentLiveSource.DesignerDocumentParameterKey] = document.DocumentId,
            [DesignerStrategyVersion.DraftRevisionKey] = document.Revision!,
            [DesignerStrategyVersion.VersionKey] = DesignerStrategyVersion.Compute(document, realismHash),
            [DesignerStrategyVersion.RealismKey] = realismHash,
            [DesignerStrategyVersion.DataSnapshotKey] = snapshot.Hash,
            [DesignerStrategyVersion.DataProvenanceKey] = provenance,
            ["symbols"] = symbol,
            ["from"] = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["to"] = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["manifestHashes"] = JsonSerializer.Serialize(manifests.OrderBy(static pair => pair.Key, StringComparer.Ordinal)),
            ["engineVersion"] = typeof(BacktestEngine).Module.ModuleVersionId.ToString("D")
        };

        // A stored bar's provider text alone cannot attest its origin. This bounded replay lane
        // carries simulation provenance until the dataset has independently verified authority.
        var scopedStrategyId = "strategy-design-" + Sha256Digest.ComputeUtf8(
            JsonSerializer.Serialize(new[] { scope.TenantId, scope.CompanyId, document.DocumentId }));
        var entry = StrategyRunEntry.StartForFundAccount(
            scopedStrategyId, document.Name, RunType.Backtest, accountId,
            datasetReference: document.DatasetReference,
            feedReference: "designer-daily-equity:v1",
            parameterSet: parameters,
            operatorAcceptanceCriteria: evidenceLoop.OperatorAcceptanceCriteria,
            retainedEvidenceReferences: evidenceLoop.RetainedEvidenceReferences,
            accountingRecordReferences: evidenceLoop.AccountingRecordReferences,
            approvalReferences: evidenceLoop.ApprovalReferences,
            paperValidationReferences: evidenceLoop.PaperValidationReferences,
            governedReportReferences: evidenceLoop.GovernedReportReferences,
            executionRealism: realism) with
        {
            ActorId = actor,
            DataProvenanceToken = provenance
        };

        await runs.RecordRunAsync(entry, ct).ConfigureAwait(false);
        try
        {
            var result = await engineFactory(request).RunAsync(request, strategy!, ct: ct).ConfigureAwait(false);
            if (result.TotalEventsProcessed != snapshot.EventCount || !result.Universe.SetEquals([symbol]))
                throw new InvalidDataException("The native replay did not consume the exact retained dataset.");

            ct.ThrowIfCancellationRequested();
            var completed = entry.Complete(result with { Request = result.Request with { DataRoot = snapshot.RetainedPath } });
            await runs.RecordRunAsync(completed, CancellationToken.None).ConfigureAwait(false);
            return completed;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await runs.RecordRunAsync(entry.Cancel(reason: "Designer replay was cancelled."), CancellationToken.None)
                .ConfigureAwait(false);
            throw;
        }
        catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or IOException)
        {
            await runs.RecordRunAsync(entry.Fail(reason: "The native replay did not complete."), CancellationToken.None)
                .ConfigureAwait(false);
            throw new DesignerBacktestBlockedException("The native replay could not complete. Check the retained run and input data.", ex);
        }
    }

    private async Task<ReplaySnapshot> CaptureReplayAsync(
        StrategyRunRepositoryScope scope, string symbol, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var end = new DateTimeOffset(to.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);
        var instrument = securities is null ? null : await securities.GetByIdentifierAsync(
            SecurityIdentifierKind.Ticker, symbol, null, ct, end).ConfigureAwait(false);
        if (instrument is null || instrument.Status != SecurityStatusDto.Active ||
            !string.Equals(instrument.AssetClass, "equity", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(instrument.Currency, "USD", StringComparison.OrdinalIgnoreCase) ||
            corporateActions is null)
            throw new DesignerBacktestBlockedException("An active USD equity and corporate-action coverage are required in Security Master.");

        var actions = await corporateActions.GetCorporateActionsAsync(instrument.SecurityId, ct).ConfigureAwait(false);
        if (actions.Any(action => (action.ExDate >= from && action.ExDate <= to) ||
                (action.PayDate >= from && action.PayDate <= to)))
            throw new DesignerBacktestBlockedException("Choose a window without corporate actions for this Designer replay.");

        var symbolRoot = Path.Combine(dataRoot, symbol);
        var source = new JsonlReplayer(symbolRoot,
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { [symbol] = 0 });
        var bars = new SortedDictionary<DateOnly, MarketEvent>();
        await foreach (var item in source.ReadEventsAsync(ct).ConfigureAwait(false))
        {
            if (item.Payload is not HistoricalBar bar || bar.SessionDate < from || bar.SessionDate > to)
                continue;
            if (bar.PriceBasis != HistoricalPriceBasis.Raw || !calendar.IsTradingDay(bar.SessionDate) ||
                !bars.TryAdd(bar.SessionDate, item))
                throw new DesignerBacktestBlockedException("The replay needs one unadjusted daily bar per trading session.");
        }

        if (bars.Count < 2 || Enumerable.Range(0, to.DayNumber - from.DayNumber + 1)
                .Select(from.AddDays)
                .Any(day => calendar.IsTradingDay(day) && !bars.ContainsKey(day)))
            throw new DesignerBacktestBlockedException("Historical daily bars are missing for the requested trading sessions.");

        var lines = string.Join('\n', bars.Values.Select(item => JsonSerializer.Serialize(item, MarketDataJsonContext.HighPerformanceOptions))) + "\n";
        var hash = Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(new
        {
            instrument.SecurityId,
            instrument.Version,
            CorporateActionCount = 0,
            BarsSha256 = Sha256Digest.ComputeUtf8(lines)
        }));
        var scopeHash = Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(new[] { scope.TenantId, scope.CompanyId }));
        var root = Path.Combine(dataRoot, "workstation", "designer-replay", scopeHash);
        var path = Path.Combine(root, $"{hash}.snapshot");

        Directory.CreateDirectory(root);
        await AtomicFileWriter.WriteAsync(path, lines, ct).ConfigureAwait(false);
        return new ReplaySnapshot(path, hash, bars.Count, bars.Keys.First(), lines);
    }

    private sealed record ReplaySnapshot(string RetainedPath, string Hash, int EventCount, DateOnly FirstBar, string Lines);

    private sealed class ReplayDirectory(ReplaySnapshot snapshot, string symbol) : IDisposable
    {
        public string Root { get; } = Directory.CreateTempSubdirectory("meridian-designer-replay-").FullName;

        public async Task StageAsync(CancellationToken ct)
        {
            var symbolRoot = Path.Combine(Root, symbol);
            Directory.CreateDirectory(symbolRoot);
            await AtomicFileWriter.WriteAsync(
                Path.Combine(symbolRoot, $"{symbol}_bars_{snapshot.FirstBar:yyyy-MM-dd}.jsonl"), snapshot.Lines, ct)
                .ConfigureAwait(false);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}

public sealed class DesignerBacktestBlockedException(string message, Exception? cause = null) : Exception(message, cause);
