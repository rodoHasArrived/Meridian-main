using System.Text.Json;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Reconciliation;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Domain.Reconciliation;
using Meridian.FinancialOperations.Reconciliation;
using Meridian.Infrastructure.Reconciliation;
using Meridian.Storage.Ledger;
using Meridian.Strategies.Services;
using Meridian.Ui.Shared.Evidence;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Retains an exact external reconciliation frame for native dividend entitlement. API completion
/// badges and inferred symbols are insufficient: source bytes, sided membership, recovery hashes,
/// recorded security identity and the complete canonical lot population must agree.
/// </summary>
public sealed class CorporateActionCashDividendReconciliationSupport(
    IReconciliationApiService? reconciliation = null,
    ICanonicalStatementStore? canonicalStatements = null,
    IStatementRunMatchArtifactStore? matchArtifacts = null,
    IStatementRunRecoveryRepository? recovery = null,
    IInternalReconciliationPopulationProvider? populations = null,
    ILedgerSecurityEntitlementInventory? inventory = null,
    IEvidenceArtifactStore? evidenceVault = null,
    IReconciliationBreakQueueRepository? breakQueue = null,
    ISecurityMasterQueryService? securities = null) : ICorporateActionCashDividendReconciliationSupport
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<CashDividendReconciliationSupportDto> GetSupportAsync(CorporateActionCaseScopeDto scope,
        Guid securityId, DateOnly entitlementCutoff, IReadOnlyList<CashDividendReconciliationAccountDto> accounts,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(accounts);
        if (securityId == Guid.Empty || entitlementCutoff == default || string.IsNullOrWhiteSpace(scope.FundProfileId)
            || !Guid.TryParse(scope.LedgerBookId, out var bookId) || bookId == Guid.Empty
            || accounts.Count == 0 || accounts.Any(account => account.ExpectedLongQuantity <= 0m
                || !Guid.TryParse(account.FinancialAccountId, out var id) || id == Guid.Empty)
            || accounts.Select(account => account.FinancialAccountId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != accounts.Count)
            throw Refuse("Exact owner scope, canonical book/security and every positive account entitlement are required.");
        if (reconciliation is null || canonicalStatements is null || matchArtifacts is null || recovery is null
            || populations is null || inventory is null || evidenceVault is null || breakQueue is null || securities is null)
            throw Refuse("Native dividend reconciliation requires scoped statements, durable matcher/recovery, canonical lots, source bytes, queue and recorded security identity services.");
        var access = new ReconciliationBreakQueueScope(scope.TenantId, scope.CompanyId);
        var ordered = accounts.OrderBy(account => account.FinancialAccountId, StringComparer.Ordinal).ToArray();
        // Two complete reads detect source/checkpoint, identity, population, queue and lot drift.
        // Native posting holds the book's write barrier; queue facts are explicitly this retained
        // stable observation, not a claim that later casework cannot change.
        var first = await ReadFrameAsync(scope, access, bookId, securityId, entitlementCutoff, ordered, ct).ConfigureAwait(false);
        var second = await ReadFrameAsync(scope, access, bookId, securityId, entitlementCutoff, ordered, ct).ConfigureAwait(false);
        if (!string.Equals(first, second, StringComparison.Ordinal))
            throw Refuse("The reconciliation entitlement frame changed while it was verified; refresh and inspect it again.");
        return new(Sha256Digest.ComputeUtf8(second), second);
    }

    private async Task<string> ReadFrameAsync(CorporateActionCaseScopeDto scope, ReconciliationBreakQueueScope access,
        Guid bookId, Guid securityId, DateOnly cutoff, IReadOnlyList<CashDividendReconciliationAccountDto> accounts,
        CancellationToken ct)
    {
        var cutoffLots = await inventory!.ReadSecurityEntitlementInventoryAsync(scope.TenantId, scope.CompanyId,
            bookId, securityId, cutoff, ct).ConfigureAwait(false);
        ValidateInventory(cutoffLots, scope, bookId, securityId, cutoff);
        var held = HeldIdentity(cutoffLots);
        var expected = accounts.ToDictionary(account => Guid.Parse(account.FinancialAccountId), account => account.ExpectedLongQuantity);
        var actual = cutoffLots.HeldLots.GroupBy(lot => Guid.Parse(lot.Account.FinancialAccountId!))
            .ToDictionary(group => group.Key, group => group.Sum(lot => lot.OpenQuantity));
        if (actual.Count != expected.Count || expected.Any(pair => !actual.TryGetValue(pair.Key, out var quantity) || quantity != pair.Value))
            throw Refuse("The requested account quantities do not equal the complete canonical book entitlement.");

        var queue = (await breakQueue!.GetAllAsync(access, ct: ct).ConfigureAwait(false))
            .Where(item => item.LedgerBookId == bookId || item.FundAccountId is { } account
                && Guid.TryParse(account, out var id) && expected.ContainsKey(id)
                || item.LedgerBookId is null && string.Equals(item.FundProfileId, scope.FundProfileId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.BreakId, StringComparer.Ordinal).ToArray();
        if (queue.Any(item => !access.Owns(item) || IsBlocking(item)))
            throw Refuse("The complete book/account reconciliation queue contains an open above-tolerance or unmeasured break.");

        var runs = await reconciliation!.ListStatementRunsAsync(access, ct).ConfigureAwait(false);
        var authorized = new List<(StatementRunSummaryDto Run, StatementReconciliationRunAuthorization Authority)>();
        foreach (var run in runs.OrderBy(run => run.RunId, StringComparer.Ordinal))
        {
            var authority = await reconciliation!.GetStatementRunAuthorizationAsync(run.RunId, access, ct).ConfigureAwait(false);
            if (authority is not null && authority.LedgerBookId == bookId && authority.AsOfDate <= cutoff
                && expected.ContainsKey(authority.FundAccountId)
                && string.Equals(authority.FundProfileId, scope.FundProfileId, StringComparison.OrdinalIgnoreCase))
                authorized.Add((run, authority));
        }

        var support = new List<object>();
        var historicalLots = new Dictionary<DateOnly, LedgerSecurityEntitlementInventorySnapshot>();
        foreach (var account in accounts)
        {
            var accountId = Guid.Parse(account.FinancialAccountId);
            var accountAuthority = await reconciliation!.GetAuthorizedFundAccountAsync(accountId, access, ct).ConfigureAwait(false);
            if (accountAuthority is null || accountAuthority.FundAccountId != accountId
                || !string.Equals(accountAuthority.FundProfileId, scope.FundProfileId, StringComparison.OrdinalIgnoreCase))
                throw Refuse("Every canonical entitlement account requires current tenant/company and fund authorization.");
            var selected = authorized.Where(item => item.Authority.FundAccountId == accountId)
                .OrderByDescending(item => item.Authority.AsOfDate).ThenByDescending(item => item.Run.StartedAtUtc)
                .ThenBy(item => item.Run.RunId, StringComparer.Ordinal).FirstOrDefault();
            if (selected.Run is null)
                throw Refuse("Every entitlement account requires an authorized retained statement at or before the pre-ex cutoff.");
            var asOf = selected.Authority.AsOfDate;
            if (!historicalLots.TryGetValue(asOf, out var sourceLots))
            {
                sourceLots = await inventory!.ReadSecurityEntitlementInventoryAsync(scope.TenantId, scope.CompanyId,
                    bookId, securityId, asOf, ct).ConfigureAwait(false);
                ValidateInventory(sourceLots, scope, bookId, securityId, asOf);
                historicalLots.Add(asOf, sourceLots);
            }
            if (HeldIdentity(sourceLots) != held)
                throw Refuse("The exact canonical lot identities, accounts or held quantities changed between the reconciled statement and pre-ex cutoff.");
            support.Add(await ReadSourceAsync(scope, access, securityId, selected.Run, selected.Authority,
                account.ExpectedLongQuantity, ct).ConfigureAwait(false));
        }
        return JsonSerializer.Serialize(new
        {
            SchemaVersion = 1,
            Method = "retained-sided-statement-and-unchanged-canonical-entitlement-v1",
            Observation = "stable-double-read;queue-is-retained-as-observed;native-book-write-barrier-required-at-post",
            scope.TenantId,
            scope.CompanyId,
            scope.FundProfileId,
            LedgerBookId = bookId,
            SecurityId = securityId,
            EntitlementCutoff = cutoff,
            Accounts = accounts,
            CutoffInventoryHash = cutoffLots.SnapshotHash,
            HeldLotIdentity = JsonSerializer.Deserialize<JsonElement>(held),
            StatementInventories = historicalLots.OrderBy(pair => pair.Key)
                .Select(pair => new { AsOfDate = pair.Key, pair.Value.SnapshotHash }).ToArray(),
            Queue = queue,
            Sources = support
        }, Json);
    }

    private async Task<object> ReadSourceAsync(CorporateActionCaseScopeDto scope, ReconciliationBreakQueueScope access,
        Guid securityId, StatementRunSummaryDto run, StatementReconciliationRunAuthorization authority,
        decimal expectedQuantity, CancellationToken ct)
    {
        var canonical = await canonicalStatements!.GetImportAsync(run.ImportId, ct).ConfigureAwait(false)
            ?? throw Refuse("The retained canonical statement import is unavailable.");
        var artifact = await matchArtifacts!.GetAsync(run.RunId, ct).ConfigureAwait(false)
            ?? throw Refuse("The immutable sided matcher artifact is unavailable.");
        var checkpoint = await recovery!.GetAsync(run.RunId, ct).ConfigureAwait(false)
            ?? throw Refuse("The durable statement recovery checkpoint is unavailable.");
        var import = canonical.Import;
        if (run.RunId != authority.RunId || import.ImportId != run.ImportId || import.ImportId != run.RunId
            || import.FundAccountId != authority.FundAccountId.ToString("D")
            || import.StatementPeriodEnd != authority.AsOfDate || import.StatementDate != authority.AsOfDate
            || import.StatementPeriodStart != authority.PeriodStart || import.StatementPeriodEnd != authority.PeriodEnd
            || import.AccountingScope is not { } accounting || accounting.LedgerBookId != authority.LedgerBookId
            || accounting.AccountingPeriodId != authority.AccountingPeriodId || accounting.AsOfDate != authority.AsOfDate
            || !string.Equals(accounting.FundProfileId, scope.FundProfileId, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(import.ExternalAccountId) || !Sha256Digest.IsWellFormed(import.SourceFileHash)
            || canonical.Rows.Count != import.NormalizedRowCount
            || !StatementReconciliationIntakeAuthority.HasVerifiedCompletion(checkpoint, canonical, artifact)
            || !StatementReconciliationIntakeAuthority.CanCompareSourceRun(artifact)
            || !Sha256Digest.FixedEquals(artifact.SourceComparisonMappingFingerprint, import.ExecutedMappingFingerprint)
            || artifact.SourceComparisonPopulationKinds?.Contains("Position", StringComparer.OrdinalIgnoreCase) != true
            || artifact.MatchGroups is null || artifact.MatchGroups.Count != artifact.MatchCount
            || artifact.Breaks.Any(item => item.ToleranceBreached)
            || checkpoint.UpdatedAtUtc == default || checkpoint.UpdatedAtUtc.Offset != TimeSpan.Zero)
            throw Refuse("The exact authorized source, complete sided comparison and completed recovery hashes are not established.");

        var internalBook = await populations!.GetPopulationsAsync(new(import.FundAccountId, import.ExternalAccountId,
            import.StatementPeriodStart, import.StatementPeriodEnd, "USD")
        { AccountingScope = accounting }, ct).ConfigureAwait(false);
        var positions = internalBook.Positions;
        if (positions.Count == 0 || positions.Any(position => string.IsNullOrWhiteSpace(position.EvidenceReference))
            || positions.Select(position => position.EvidenceReference).Distinct(StringComparer.Ordinal).Count() != positions.Count)
            throw Refuse("An exact retained internal position population is required for sided identity verification.");
        var rows = canonical.Rows.Where(row => string.Equals(row.ActivityType, "position", StringComparison.OrdinalIgnoreCase))
            .OrderBy(row => row.SourceRowNumber).ToArray();
        if (rows.Length == 0 || rows.Any(row => row.ImportId != import.ImportId || row.SourceRowNumber <= 0
                || !Sha256Digest.IsWellFormed(row.RawChecksum) || row.TradeDate != authority.AsOfDate || row.Currency != "USD")
            || rows.Select(row => row.SourceRowNumber).Distinct().Count() != rows.Length)
            throw Refuse("The complete dated canonical position source rows and source-row hashes are required.");

        var resolved = new Dictionary<string, SecurityDetailDto>(StringComparer.Ordinal);
        foreach (var symbol in rows.Select(row => row.Symbol).Concat(positions.Select(position => position.SecurityId)).Distinct(StringComparer.Ordinal))
            resolved.Add(symbol, await ResolveIdentityAsync(symbol, checkpoint.UpdatedAtUtc, authority.AsOfDate, ct).ConfigureAwait(false));
        var targetRows = rows.Where(row => resolved[row.Symbol].SecurityId == securityId).ToArray();
        var targetPositions = positions.Where(position => resolved[position.SecurityId].SecurityId == securityId).ToArray();
        if (targetRows.Length == 0 || targetPositions.Length == 0 || targetRows.Any(row => row.Quantity <= 0m)
            || targetPositions.Any(position => position.Quantity <= 0m || position.AsOfDate != authority.AsOfDate
                || !string.Equals(position.Account, import.ExternalAccountId, StringComparison.Ordinal))
            || targetRows.Sum(row => row.Quantity) != expectedQuantity || targetPositions.Sum(position => position.Quantity) != expectedQuantity)
            throw Refuse("The exact security's external and internal long quantities do not equal the complete canonical account entitlement.");

        var rowByReference = rows.ToDictionary(row => $"{import.ImportId}:{row.SourceRowNumber}", StringComparer.Ordinal);
        var positionByReference = positions.ToDictionary(position => position.EvidenceReference, StringComparer.Ordinal);
        var groups = artifact.MatchGroups.Where(group => group.StatementEvidenceReferences.Any(reference =>
            targetRows.Any(row => reference == $"{import.ImportId}:{row.SourceRowNumber}"))).OrderBy(group => group.MatchGroupId, StringComparer.Ordinal).ToArray();
        if (groups.Length == 0 || groups.Any(group => !string.Equals(group.Kind, "Position", StringComparison.OrdinalIgnoreCase)
            || group.QuantityVariance != 0m || group.StatementEvidenceReferences.Count == 0 || group.InternalEvidenceReferences.Count == 0
            || group.StatementEvidenceReferences.Any(reference => !rowByReference.TryGetValue(reference, out var row)
                || resolved[row.Symbol].SecurityId != securityId)
            || group.InternalEvidenceReferences.Any(reference => !positionByReference.TryGetValue(reference, out var position)
                || resolved[position.SecurityId].SecurityId != securityId)
            || group.StatementEvidenceReferences.Sum(reference => rowByReference[reference].Quantity)
                != group.InternalEvidenceReferences.Sum(reference => positionByReference[reference].Quantity))
            || !SameReferences(groups.SelectMany(group => group.StatementEvidenceReferences), targetRows.Select(row => $"{import.ImportId}:{row.SourceRowNumber}"))
            || !SameReferences(groups.SelectMany(group => group.InternalEvidenceReferences), targetPositions.Select(position => position.EvidenceReference)))
            throw Refuse("The retained sided groups do not cover each exact security source/internal member once with equal quantities.");

        var vaults = await evidenceVault!.FindByLinkageAsync(new(null, run.RunId, null, null, null)
        { TenantId = scope.TenantId, Scope = scope.CompanyId }, ct).ConfigureAwait(false);
        var retained = new List<object>();
        foreach (var vault in vaults.OrderBy(vault => vault.VaultId, StringComparer.Ordinal))
        {
            if (vault.TenantId != scope.TenantId || vault.Scope != scope.CompanyId
                || !vault.Artifacts.Any(item => Sha256Digest.FixedEquals(item.ContentHashSha256, import.SourceFileHash)))
                continue;
            if (!await evidenceVault!.VerifyRetainedContentAsync(vault.VaultId, scope.TenantId, scope.CompanyId, ct).ConfigureAwait(false))
                throw Refuse("The source statement's retained Evidence Vault bytes failed verification.");
            retained.Add(new
            {
                vault.VaultId,
                vault.ManifestRoute,
                vault.ContentHashSha256,
                Artifacts = vault.Artifacts.Where(item => Sha256Digest.FixedEquals(item.ContentHashSha256, import.SourceFileHash))
                    .OrderBy(item => item.ArtifactId, StringComparer.Ordinal).ToArray()
            });
        }
        if (retained.Count == 0)
            throw Refuse("The source statement bytes are not retained in this owner scope with the exact source-file digest.");
        return new
        {
            Authority = authority,
            CanonicalImportHash = StatementDurabilityHashing.Hash(canonical),
            MatchArtifactHash = StatementDurabilityHashing.Hash(artifact),
            Recovery = checkpoint,
            import.SourceFileHash,
            Rows = targetRows,
            InternalPositions = targetPositions.OrderBy(position => position.EvidenceReference, StringComparer.Ordinal).ToArray(),
            MatchGroups = groups,
            RecordedIdentities = resolved.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new
                {
                    Input = pair.Key,
                    pair.Value.SecurityId,
                    pair.Value.Version,
                    RecordedDefinitionHash = Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(pair.Value, Json))
                }).ToArray(),
            SourceBytes = retained
        };
    }

    private async Task<SecurityDetailDto> ResolveIdentityAsync(string input, DateTimeOffset recordedAt, DateOnly effectiveDate, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input))
            throw Refuse("A source/internal position has no authoritative instrument identifier.");
        var effectiveAt = new DateTimeOffset(effectiveDate.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);
        var result = Guid.TryParse(input, out var id)
            ? await securities!.GetRecordedByIdAsOfAsync(id, recordedAt, ct).ConfigureAwait(false)
            : await securities!.GetRecordedByIdentifierAsOfAsync(SecurityIdentifierKind.Ticker, input, null,
                recordedAt, effectiveAt, ct).ConfigureAwait(false);
        return result is { SecurityId: var resolvedId, Version: > 0 } && resolvedId != Guid.Empty
            ? result : throw Refuse("Recorded Security Master identity is unavailable or ambiguous for a sided source/internal position.");
    }

    private static void ValidateInventory(LedgerSecurityEntitlementInventorySnapshot value, CorporateActionCaseScopeDto scope,
        Guid bookId, Guid securityId, DateOnly asOf)
    {
        if (value.TenantId != scope.TenantId || value.CompanyId != scope.CompanyId || value.Book.LedgerBookId != bookId
            || value.SecurityId != securityId || value.AsOfDate != asOf || value.Book.BaseCurrency != "USD"
            || !string.Equals(value.Book.FundProfileId, scope.FundProfileId, StringComparison.OrdinalIgnoreCase)
            || !Sha256Digest.FixedEquals(value.SnapshotHash, Sha256Digest.ComputeUtf8(value.SnapshotJson))
            || value.HeldLots.Count == 0 || value.HeldLots.Any(lot => lot.SecurityId != securityId || lot.LedgerBookId != bookId
                || lot.TaxLotRecordId == Guid.Empty || lot.BookPositionId == Guid.Empty || lot.OpenQuantity <= 0m
                || lot.Acquisition?.Direction != OpenLotDirection.Long || lot.Currency != "USD"
                || !Guid.TryParse(lot.Account.FinancialAccountId, out var account) || account == Guid.Empty)
            || value.HeldLots.Select(lot => lot.TaxLotRecordId).Distinct().Count() != value.HeldLots.Count)
            throw Refuse("The complete, owned and identified canonical long-lot inventory is unavailable.");
    }

    private static string HeldIdentity(LedgerSecurityEntitlementInventorySnapshot value)
        => JsonSerializer.Serialize(value.HeldLots.OrderBy(lot => lot.TaxLotRecordId)
            .Select(lot => new
            {
                lot.TaxLotRecordId,
                lot.BookPositionId,
                lot.SecurityId,
                lot.LedgerBookId,
                FinancialAccountId = Guid.Parse(lot.Account.FinancialAccountId!),
                lot.Account.Name,
                lot.Account.AccountType,
                lot.LotId,
                lot.OpenQuantity
            }).ToArray(), Json);

    private static bool SameReferences(IEnumerable<string> actual, IEnumerable<string> expected)
        => actual.Order(StringComparer.Ordinal).SequenceEqual(expected.Order(StringComparer.Ordinal), StringComparer.Ordinal);

    private static bool IsBlocking(ReconciliationBreakQueueItem item)
        => item.Status is not (ReconciliationBreakQueueStatus.Resolved or ReconciliationBreakQueueStatus.SignedOff)
            && (item.Measures is not { Count: > 0 } measures
                ? item.ToleranceBand is null || Math.Abs(item.Variance) > item.ToleranceBand
                : measures.Any(measure => measure.Variance is null || measure.Tolerance is null
                    || Math.Abs(measure.Variance.Value) > measure.Tolerance.Value));

    private static CorporateActionValidationException Refuse(string message) => new(message);
}
