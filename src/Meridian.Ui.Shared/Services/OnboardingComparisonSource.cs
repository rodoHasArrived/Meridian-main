using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.Execution.Sdk;
using Meridian.FinancialOperations.AccountingSystem;
using Meridian.FinancialOperations.Onboarding;
using Meridian.PortfolioRecords.Accounts;
using Meridian.Ui.Shared.Evidence;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Pins existing comparison evidence without importing, posting, resolving casework, or changing book authority.
/// Account observations always describe the selected financial account, never a substituted whole-book total.
/// </summary>
public sealed class OnboardingComparisonSource(
    AccountingSystemIntegrationService accounting,
    IAccountQueryService? accounts = null,
    ILedgerBookService? books = null,
    IFundProfileTenancyRegistry? tenancy = null,
    ProviderLedgerReconciliationService? providerComparisons = null,
    FundAccountCloseReadinessService? closeReadiness = null,
    IEvidenceArtifactStore? evidence = null,
    BrokeragePortfolioSyncService? brokerageSnapshots = null,
    BrokeragePortfolioSyncOptions? brokerageOptions = null) : IOnboardingSourceProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<IReadOnlyList<OnboardingSourceSelectionDto>> GetSelectionAsync(
        OnboardingWorkspaceDto workspace, CancellationToken ct = default)
    {
        await ValidateScopeAsync(workspace.Scope, ct).ConfigureAwait(false);
        return accounting.GetRetainedOnboardingSelections(workspace.Scope.FundProfileId,
                workspace.Scope.LedgerBookId, workspace.Scope.TenantId, workspace.Scope.CompanyId)
            .Where(choice => choice.AsOfDate >= workspace.Scope.StartDate && choice.AsOfDate <= workspace.Scope.EndDate)
            .ToArray();
    }

    public async Task<OnboardingSourceCaptureDto> CaptureAsync(
        OnboardingWorkspaceDto workspace, CaptureOnboardingComparisonRequestDto request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(request);
        var scope = workspace.Scope;
        var selectedAccounts = await ValidateScopeAsync(scope, ct).ConfigureAwait(false);
        var accountSource = accounts!; // ValidateScopeAsync has established that the read authority is available.
        if (request.AsOfDate < scope.StartDate || request.AsOfDate > scope.EndDate)
            throw new OnboardingValidationException("The comparison date is outside the bounded onboarding workspace.");

        var snapshots = new List<OnboardingSourceSnapshotDto>();
        var observations = new List<OnboardingObservationDto>();
        var missing = new List<OnboardingMissingSourceDto>();
        var contributions = new List<CloseReadinessContributionDto>();
        var closeBlockers = new List<CloseReadinessBlockerDto>();
        var completeCloseEvidence = true;
        var access = new ReconciliationBreakQueueScope(scope.TenantId, scope.CompanyId);
        var capturedAt = DateTimeOffset.UtcNow;
        AccountingOnboardingCapture? gl;
        try
        {
            gl = await accounting.CaptureRetainedOnboardingAsync(request.ProviderId, scope.FundProfileId,
                scope.LedgerBookId, request.ImportId, request.MappingProfileId, request.MappingVersion,
                request.AsOfDate, scope.TenantId, scope.CompanyId, ct).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            throw new OnboardingValidationException($"The retained GL source cannot be captured: {ex.Message}");
        }
        if (gl is null)
        {
            missing.Add(new("onboarding.gl.exact_source_missing", "ExternalGl", null,
                "The exact retained import, certified mapping, and ledger book are unavailable for this date and scope."));
        }
        else
        {
            AddSnapshot("ExternalGl", gl.Import.Summary.ImportId, gl.Import, gl.Import.Summary.EvidenceReferences);
            AddSnapshot("Mapping", gl.Mapping.ProfileId, gl.Mapping, gl.MappingEvidenceReferences);
            AddSnapshot("Ledger", scope.LedgerBookId.ToString("D"),
                new { gl.Book, gl.Periods, gl.Journals, gl.Reconciliation }, gl.Reconciliation.EvidenceReferences);
        }

        foreach (var (selectedId, account) in selectedAccounts)
        {
            ct.ThrowIfCancellationRequested();
            var membershipSnapshot = AddSnapshot("AccountMembership", selectedId, account, []);
            var timeline = await accountSource.GetBalanceTimelineAsync(account.AccountId,
                request.AsOfDate, request.AsOfDate, ct).ConfigureAwait(false);
            var balances = timeline.Where(balance => balance.AccountId == account.AccountId
                && balance.AsOfDate == request.AsOfDate).OrderBy(balance => balance.RecordedAt).ToArray();
            var positions = await accountSource.GetCustodianPositionsAsync(account.AccountId, request.AsOfDate, ct).ConfigureAwait(false);
            if (positions.Any(position => position.AccountId != account.AccountId || position.AsOfDate != request.AsOfDate))
                throw new OnboardingValidationException("The retained positions do not match the selected account and comparison date.");
            var accountSnapshot = AddSnapshot("AccountSources", selectedId,
                new { Balances = balances, Positions = positions }, balances
                    .Where(balance => !string.IsNullOrWhiteSpace(balance.ExternalReference))
                    .Select(balance => balance.ExternalReference!).ToArray());

            // Opening reconciliation is a separate retained account workflow. Its original date and
            // complete result contents remain in the payload, even when captured with a later period.
            var openingRuns = await accountSource.GetReconciliationRunsAsync(account.AccountId, ct).ConfigureAwait(false);
            var openingRun = openingRuns.Where(run => run.AccountId == account.AccountId
                    && run.AsOfDate == scope.StartDate && run.CompletedAt is not null)
                .OrderByDescending(run => run.CompletedAt).ThenBy(run => run.ReconciliationRunId).FirstOrDefault();
            if (openingRun is null)
            {
                missing.Add(new("onboarding.opening.missing", "OpeningBalance", selectedId,
                    "Retain an account opening reconciliation at the workspace start date."));
            }
            else
            {
                var openingResults = await accountSource.GetReconciliationResultsAsync(openingRun.ReconciliationRunId, ct).ConfigureAwait(false);
                if (openingResults.Any(result => result.ReconciliationRunId != openingRun.ReconciliationRunId))
                    throw new OnboardingValidationException("The opening reconciliation returned results from another run.");
                var openingSnapshot = AddSnapshot("OpeningBalance", openingRun.ReconciliationRunId.ToString("D"),
                    new { Run = openingRun, Results = openingResults }, [], scope.StartDate);
                if (openingResults.Count == 0)
                    missing.Add(new("onboarding.opening.empty", "OpeningBalance", selectedId,
                        "The retained opening reconciliation contains no checks."));
                foreach (var result in openingResults.Where(result =>
                             !result.Category.Contains("Position", StringComparison.OrdinalIgnoreCase)))
                {
                    observations.Add(new("Balance", selectedId, account.BaseCurrency, $"opening:{result.CheckLabel}",
                        result.ExpectedAmount, result.ActualAmount, [openingSnapshot, membershipSnapshot], [openingSnapshot],
                        result.ActualAmount is null || result.ExpectedAmount is null ? result.Reason : null));
                }
            }

            var comparison = providerComparisons is null ? null
                : await providerComparisons.GetLatestAsync(account.AccountId, access, ct).ConfigureAwait(false);
            var retainedProvider = comparison is null ? null
                : await CaptureProviderInputsAsync(account.AccountId, comparison, ct).ConfigureAwait(false);
            var exactDate = comparison?.Summary.InternalAsOfDate == request.AsOfDate
                && retainedProvider is not null && retainedProvider.AsOfDate == request.AsOfDate;
            if (comparison is null || comparison.Summary.AccountId != account.AccountId || !exactDate)
            {
                missing.Add(new("onboarding.provider.exact_source_missing", "ProviderLedger", selectedId,
                    "A retained provider-ledger comparison with both source dates equal to the requested date is required."));
                AddMissingObservations(selectedId, account.BaseCurrency, [accountSnapshot, membershipSnapshot],
                    "The exact dated provider comparison is unavailable.");
            }
            else
            {
                var providerSnapshot = AddSnapshot("ProviderLedger", comparison.Summary.ReconciliationRunId.ToString("D"),
                    comparison, comparison.EvidenceLinks);
                var rawSnapshot = AddSnapshot("ProviderSource", selectedId, retainedProvider!, comparison.EvidenceLinks);
                var shadow = comparison.ShadowBookComparison;
                if (shadow is null || shadow.AccountId != account.AccountId
                    || !string.Equals(shadow.Currency, account.BaseCurrency, StringComparison.OrdinalIgnoreCase))
                {
                    missing.Add(new("onboarding.shadow.missing", "LedgerNav", selectedId,
                        "The retained comparison has no same-account, same-currency ledger/NAV comparison."));
                    AddMissingObservations(selectedId, account.BaseCurrency, [providerSnapshot, accountSnapshot, membershipSnapshot],
                        "The retained ledger/NAV comparison is unavailable.");
                }
                else
                {
                    var internalSource = balances.Where(balance => balance.RecordedAt <= comparison.Summary.CreatedAt)
                        .OrderByDescending(balance => balance.RecordedAt).ThenBy(balance => balance.SnapshotId).FirstOrDefault();
                    var raw = JsonSerializer.Deserialize<BrokerageSourceSnapshot>(retainedProvider!.RawPayloadJson, JsonOptions)!;
                    var completeSource = raw.Portfolio!.IsComplete;
                    var currentRun = openingRuns.Where(run => run.AccountId == account.AccountId
                            && run.AsOfDate == request.AsOfDate && run.CompletedAt is not null)
                        .OrderByDescending(run => run.CompletedAt).ThenBy(run => run.ReconciliationRunId).FirstOrDefault();
                    var currentResults = currentRun is null ? []
                        : await accountSource.GetReconciliationResultsAsync(currentRun.ReconciliationRunId, ct).ConfigureAwait(false);
                    if (currentRun is not null)
                        AddSnapshot("AccountReconciliation", currentRun.ReconciliationRunId.ToString("D"),
                            new { Run = currentRun, Results = currentResults }, []);
                    var custodianPopulationComplete = currentResults.Any(result =>
                        result.ReconciliationRunId == currentRun!.ReconciliationRunId && result.Category == "Positions"
                        && result.IsMatch && result.ExpectedAmount == positions.Count && result.ActualAmount == positions.Count);
                    var provedEmptyPositions = completeSource && raw.Portfolio.Positions.Count == 0 && positions.Count == 0
                        && custodianPopulationComplete;
                    var selectedLines = shadow.Lines.Where(line => line.Dimension is "account-cash" or "total-equity"
                        || line.Dimension.StartsWith("position-quantity:", StringComparison.Ordinal)
                        || line.Dimension.StartsWith("position-market-value:", StringComparison.Ordinal)).ToArray();
                    // An explicit aggregate market-value comparison proves a zero-position account;
                    // an absent position list alone cannot establish position coverage.
                    if (provedEmptyPositions && !selectedLines.Any(line => line.Dimension.StartsWith("position-", StringComparison.Ordinal)))
                        selectedLines = [.. selectedLines, .. shadow.Lines.Where(line => line.Dimension == "positions-market-value"
                            && line.InternalAmount == 0m && line.ProviderAmount == 0m)];
                    // A disappeared instrument resolves only after both complete source populations
                    // prove its absence. Missing population evidence never produces a zero.
                    if (completeSource && custodianPopulationComplete)
                    {
                        foreach (var prior in workspace.CurrentDifferences.Where(difference => difference.AccountId == selectedId
                                     && difference.Kind == "Position" && difference.InstrumentId is not null))
                        {
                            var dimension = prior.InstrumentId!;
                            if (selectedLines.Any(line => line.Dimension == dimension)
                                || !TryPositionAmounts(dimension, positions, raw.Portfolio.Positions, out var internalAmount, out var externalAmount)
                                || internalAmount is not null || externalAmount is not null)
                                continue;
                            selectedLines = [.. selectedLines, new(dimension, "Verified absent position", "custodian-statement", "provider-source",
                                0m, 0m, 0m, ProviderLedgerReconciliationCheckStatusDto.Matched,
                                "Both complete retained source populations confirm that the previously observed instrument is absent.")];
                        }
                    }
                    foreach (var line in selectedLines)
                    {
                        var kind = line.Dimension == "account-cash" ? "Balance"
                            : line.Dimension == "total-equity" ? "Nav" : "Position";
                        var incomplete = line.InternalAmount is null || line.ProviderAmount is null;
                        var reason = incomplete ? line.Reason : null;
                        if (!completeSource)
                            reason = "The retained provider source does not prove complete account and position coverage.";
                        else if (internalSource is null || !string.Equals(internalSource.Currency, shadow.Currency, StringComparison.OrdinalIgnoreCase))
                            reason = "The exact internal balance source used by this comparison is unavailable.";
                        else if (kind == "Nav" && (internalSource.SecuritiesMarketValue is null
                                 || internalSource.AccruedInterest is null || internalSource.PendingSettlement is null))
                            reason = "NAV requires explicit securities, accrual, and settlement components; absent components are not zero.";
                        else if (kind == "Balance" && (line.InternalAmount != internalSource.CashBalance
                                 || line.ProviderAmount != raw.Portfolio.Balance.Cash))
                            reason = "The retained balance comparison does not match its exact source amounts.";
                        else if (kind == "Nav" && (line.InternalAmount != internalSource.CashBalance
                                 + internalSource.SecuritiesMarketValue + internalSource.AccruedInterest + internalSource.PendingSettlement
                                 || line.ProviderAmount != raw.Portfolio.Balance.Equity))
                            reason = "The retained NAV comparison does not match its exact source amounts.";
                        else if (kind == "Position" && (positions.Any(position => !string.Equals(position.Currency, shadow.Currency, StringComparison.OrdinalIgnoreCase))
                                 || raw.Portfolio.Positions.Any(position => !string.Equals(position.Currency ?? raw.Portfolio.Balance.Currency,
                                     shadow.Currency, StringComparison.OrdinalIgnoreCase))))
                            reason = "Position currencies differ from the comparison currency; governed conversion evidence is required.";
                        else if (kind == "Position" && TryPositionAmounts(line.Dimension, positions, raw.Portfolio.Positions,
                                     out var expectedInternalPosition, out var expectedExternalPosition)
                                 && (line.InternalAmount != (expectedInternalPosition ?? (custodianPopulationComplete ? 0m : null))
                                     || line.ProviderAmount != (expectedExternalPosition ?? (completeSource ? 0m : null))))
                            reason = "The retained position comparison does not match its exact custodian and provider source amounts.";
                        observations.Add(new(kind, selectedId, shadow.Currency,
                            kind == "Position" ? line.Dimension : null,
                            line.InternalAmount, line.ProviderAmount,
                            [providerSnapshot, rawSnapshot, accountSnapshot, membershipSnapshot], comparison.EvidenceLinks,
                            reason));
                    }
                    foreach (var kind in workspace.Criteria.RequiredKinds.Where(kind =>
                                 !observations.Any(row => row.AccountId == selectedId && row.Kind == kind
                                     && (row.InstrumentId is null || !row.InstrumentId.StartsWith("opening:", StringComparison.Ordinal)))))
                    {
                        missing.Add(new("onboarding.comparison.kind_missing", kind, selectedId,
                            $"The retained comparison has no {kind} check."));
                        if (kind != "Position")
                            observations.Add(new(kind, selectedId, account.BaseCurrency, null, null, null,
                                [providerSnapshot], comparison.EvidenceLinks, $"The retained comparison has no {kind} check."));
                    }
                }
            }

            if (workspace.Criteria.RequireCloseReadiness)
            {
                var readiness = closeReadiness is null ? null
                    : await closeReadiness.GetAsync(account.AccountId, access, ct).ConfigureAwait(false);
                var readyForDate = readiness is not null && readiness.LatestLedgerAsOfDate == request.AsOfDate
                    && readiness.LatestReconciliationRunId == comparison?.Summary.ReconciliationRunId;
                completeCloseEvidence &= readyForDate;
                var ready = readyForDate && readiness!.IsReadyToClose;
                var refs = readiness is null ? Array.Empty<string>() : [AddSnapshot("CloseReadiness", selectedId, readiness,
                    readiness.Components.Where(component => component.EvidenceLink is not null)
                        .Select(component => component.EvidenceLink!).ToArray())];
                contributions.Add(new($"account:{selectedId}", workspace.OwnerId, ready ? "Ready" : "Blocked",
                    readiness?.EvaluatedAtUtc, refs));
                if (!ready)
                    closeBlockers.Add(new("onboarding.close.not_ready", $"account:{selectedId}", "CloseReadiness", 1,
                        "Critical", workspace.OwnerId, readyForDate
                            ? string.Join("; ", readiness!.Blockers.Select(blocker => blocker.Message))
                            : "Close readiness does not refer to the exact account comparison and requested date.", refs));
            }
        }

        // Existing Evidence Vault services prove access and retained content. Copy document/manifest
        // metadata into the run so later review edits cannot change this historical capture.
        foreach (var reference in snapshots.SelectMany(snapshot => snapshot.EvidenceIds).Distinct(StringComparer.Ordinal).ToArray())
        {
            if (!EvidenceVaultReference.TryParseCanonical(reference, out var vaultId))
                continue;
            var identity = evidence is null ? null
                : await evidence.TryGetVaultIdentityAsync(vaultId, scope.TenantId, scope.CompanyId, ct).ConfigureAwait(false);
            if (identity is null || !await evidence!.VerifyRetainedContentAsync(vaultId, scope.TenantId, scope.CompanyId, ct).ConfigureAwait(false))
                missing.Add(new("onboarding.evidence.unavailable", "Evidence", null,
                    $"Retained evidence {vaultId} is unavailable or fails content verification in this tenant/company scope."));
            else
                AddSnapshot("Evidence", vaultId, identity, [reference]);
        }

        // Reject membership/ownership drift during a multi-source capture.
        var finalAccounts = await ValidateScopeAsync(scope, ct).ConfigureAwait(false);
        if (JsonSerializer.Serialize(selectedAccounts, JsonOptions) != JsonSerializer.Serialize(finalAccounts, JsonOptions))
            throw new OnboardingConcurrencyException("Account membership changed during onboarding capture. Capture again.");

        CloseReadinessProjectionDto? close = workspace.Criteria.RequireCloseReadiness
            ? new(new(scope.FundProfileId, scope.LedgerBookId, null, scope.EntityId, request.AsOfDate.ToString("yyyy-MM-dd")),
                capturedAt, closeBlockers.Count == 0 ? "Ready" : "Blocked",
                completeCloseEvidence && contributions.Count == selectedAccounts.Count,
                closeBlockers.Count == 0, contributions, closeBlockers)
            : null;
        return new(snapshots, observations, missing, close);

        string AddSnapshot(string kind, string sourceId, object payload, IReadOnlyList<string> evidenceIds, DateOnly? asOfDate = null)
        {
            var json = JsonSerializer.Serialize(payload, JsonOptions);
            var hash = Sha256Digest.ComputeUtf8(json);
            var id = $"onboarding:{kind}:{hash}";
            if (snapshots.All(snapshot => snapshot.SnapshotId != id))
                snapshots.Add(new(id, kind, sourceId, hash, hash, capturedAt, asOfDate ?? request.AsOfDate,
                    request.MappingVersion, json, evidenceIds.ToArray()));
            return id;
        }

        void AddMissingObservations(string accountId, string currency, IReadOnlyList<string> snapshotIds, string reason)
        {
            foreach (var kind in workspace.Criteria.RequiredKinds)
            {
                if (kind == "Position")
                    missing.Add(new("onboarding.position.source_missing", kind, accountId, reason));
                else
                    observations.Add(new(kind, accountId, currency, null, null, null, snapshotIds, [], reason));
            }
        }
    }

    private async Task<IReadOnlyList<KeyValuePair<string, AccountSummaryDto>>> ValidateScopeAsync(
        OnboardingScopeDto scope, CancellationToken ct)
    {
        if (accounts is null || books is null || tenancy is null)
            throw new OnboardingValidationException("Account, ledger-book, and tenant ownership services are required to capture onboarding sources.");
        if (!Guid.TryParse(scope.EntityId, out var entityId) || entityId == Guid.Empty)
            throw new OnboardingValidationException("A canonical entity ID is required for onboarding source capture.");
        var owner = await tenancy.ResolveAsync(scope.FundProfileId, ct).ConfigureAwait(false);
        if (owner is null || !owner.IsHeldBy(scope.TenantId)
            || !string.Equals(owner.CompanyId, scope.CompanyId, StringComparison.OrdinalIgnoreCase))
            throw new OnboardingValidationException("The selected fund is not owned by this tenant and company.");
        var book = await books.GetBookAsync(scope.LedgerBookId, ct).ConfigureAwait(false);
        if (book is null || !string.Equals(book.FundProfileId, scope.FundProfileId, StringComparison.OrdinalIgnoreCase))
            throw new OnboardingValidationException("The selected ledger book does not belong to the onboarding fund.");
        var selected = new List<KeyValuePair<string, AccountSummaryDto>>();
        foreach (var id in scope.AccountIds)
        {
            if (!Guid.TryParse(id, out var accountId) || accountId == Guid.Empty)
                throw new OnboardingValidationException("Onboarding account population must use canonical financial account IDs.");
            var account = await accounts.GetAccountAsync(accountId, ct).ConfigureAwait(false);
            var member = account is not null && book.FundStructureNodeId != Guid.Empty && (book.FundStructureNodeKind switch
            {
                FundStructureNodeKindDto.Account => book.FundStructureNodeId == account.AccountId,
                FundStructureNodeKindDto.Entity => book.FundStructureNodeId == account.EntityId,
                FundStructureNodeKindDto.Fund => book.FundStructureNodeId == account.FundId,
                FundStructureNodeKindDto.Sleeve => book.FundStructureNodeId == account.SleeveId,
                FundStructureNodeKindDto.Vehicle => book.FundStructureNodeId == account.VehicleId,
                _ => false
            });
            if (account is null || account.EntityId != entityId || !account.IsActive || !member)
                throw new OnboardingValidationException("Every selected account must belong to the chosen entity, fund, and ledger book.");
            selected.Add(new(id, account));
        }
        return selected;
    }

    private static bool TryPositionAmounts(string dimension, IReadOnlyList<CustodianPositionLineDto> internalPositions,
        IReadOnlyList<BrokeragePositionSnapshotDto> externalPositions, out decimal? internalAmount, out decimal? externalAmount)
    {
        internalAmount = null;
        externalAmount = null;
        var quantity = dimension.StartsWith("position-quantity:", StringComparison.Ordinal);
        if (!quantity && !dimension.StartsWith("position-market-value:", StringComparison.Ordinal))
            return false;
        var key = dimension[(dimension.IndexOf(':') + 1)..].Trim().ToUpperInvariant();
        var internalRows = internalPositions.Where(position => position.Identifier.Trim().ToUpperInvariant() == key).ToArray();
        var externalRows = externalPositions.Where(position => position.Symbol.Trim().ToUpperInvariant() == key).ToArray();
        if (internalRows.Length > 0)
            internalAmount = quantity ? internalRows.Sum(position => position.IsShort ? -Math.Abs(position.Quantity) : position.Quantity)
                : internalRows.Sum(position => position.MarketValue);
        if (externalRows.Length > 0)
            externalAmount = quantity ? externalRows.Sum(position => position.Quantity) : externalRows.Sum(position => position.MarketValue);
        return true;
    }

    private async Task<RetainedProviderInputs?> CaptureProviderInputsAsync(Guid accountId,
        ProviderLedgerReconciliationDetailDto comparison, CancellationToken ct)
    {
        if (brokerageSnapshots is null || brokerageOptions is null)
            return null;
        var projection = await brokerageSnapshots.GetActivityAsync(accountId, ct).ConfigureAwait(false);
        if (projection is null || projection.FundAccountId != accountId
            || projection.SyncedAt != comparison.Summary.ProviderSyncedAt
            || !string.Equals(projection.Link.ProviderId, comparison.Summary.ProviderId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(projection.Link.ExternalAccountId, comparison.Summary.ExternalAccountId, StringComparison.Ordinal))
            return null;

        // Only a path supplied by the retained same-account projection under the configured
        // source root can be read; client input never selects a filesystem path.
        var root = Path.GetFullPath(brokerageOptions.RootDirectory).TrimEnd(Path.DirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(projection.RawSnapshotPath);
        if (!path.StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new OnboardingValidationException("The retained provider source is outside the configured evidence root.");
        if (!File.Exists(path))
            return null;
        var sourceFile = new FileInfo(path);
        if (sourceFile.LinkTarget is not null || sourceFile.Length > 32 * 1024 * 1024)
            throw new OnboardingValidationException("The retained provider source must be a bounded regular evidence file.");
        var rawJson = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        var raw = JsonSerializer.Deserialize<BrokerageSourceSnapshot>(rawJson, JsonOptions);
        if (raw?.Portfolio is null || raw.CapturedAt != projection.SyncedAt
            || !string.Equals(raw.ProviderId, projection.Link.ProviderId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(raw.ExternalAccountId, projection.Link.ExternalAccountId, StringComparison.Ordinal)
            || !string.Equals(raw.Portfolio.Account.AccountId, projection.Link.ExternalAccountId, StringComparison.Ordinal)
            || raw.Portfolio.AccountSnapshot is { } accountSnapshot
                && (!string.Equals(accountSnapshot.AccountId, projection.Link.ExternalAccountId, StringComparison.Ordinal)
                    || !string.Equals(accountSnapshot.ProviderId, projection.Link.ProviderId, StringComparison.OrdinalIgnoreCase)))
            throw new OnboardingValidationException("The retained provider source identity does not match the scoped comparison.");
        if (projection.Balance is null || projection.Balance.Cash != raw.Portfolio.Balance.Cash
            || projection.Balance.Equity != raw.Portfolio.Balance.Equity
            || !string.Equals(projection.Balance.Currency, raw.Portfolio.Balance.Currency, StringComparison.OrdinalIgnoreCase))
            throw new OnboardingValidationException("The retained provider projection balance does not match its source snapshot.");
        var sourcePositions = raw.Portfolio.Positions.Select(position => new
        { position.Symbol, position.Quantity, position.MarketValue, position.PositionId, position.Currency })
            .OrderBy(position => position.Symbol, StringComparer.Ordinal).ThenBy(position => position.PositionId, StringComparer.Ordinal)
            .ThenBy(position => position.Quantity).ThenBy(position => position.MarketValue).ToArray();
        var projectedPositions = projection.Positions.Select(position => new
        { position.Symbol, position.Quantity, position.MarketValue, position.PositionId, position.Currency })
            .OrderBy(position => position.Symbol, StringComparer.Ordinal).ThenBy(position => position.PositionId, StringComparer.Ordinal)
            .ThenBy(position => position.Quantity).ThenBy(position => position.MarketValue).ToArray();
        if (JsonSerializer.Serialize(sourcePositions, JsonOptions) != JsonSerializer.Serialize(projectedPositions, JsonOptions))
            throw new OnboardingValidationException("The retained provider projection positions do not match their source snapshot.");
        // Canonical account evidence has an economic as-of independent of ingestion time.
        // Legacy sources without that field can only prove their original retrieval date.
        var asOf = raw.Portfolio.AccountSnapshot?.AsOf ?? raw.Portfolio.RetrievedAt;
        return new(projection, rawJson, DateOnly.FromDateTime(asOf.UtcDateTime));
    }

    private sealed record BrokerageSourceSnapshot(DateTimeOffset CapturedAt, string ProviderId,
        string ExternalAccountId, BrokeragePortfolioSnapshotDto? Portfolio);

    private sealed record RetainedProviderInputs(FundAccountBrokerageSyncActivityDto Projection,
        string RawPayloadJson, DateOnly AsOfDate);
}
