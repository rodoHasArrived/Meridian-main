using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.Calendars;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Storage.SecurityMaster;

namespace Meridian.Instruments.FixedIncome;

/// <summary>
/// Builds reviewed migration previews and a full-inventory cutover gate. Staging is returned as an
/// ordinary governed workbench edit; this service never approves, publishes or writes journals.
/// </summary>
public sealed class BondMigrationService : IBondMigrationService
{
    public const string ModelVersion = "bond-migration-v1";
    private readonly ISecurityMasterStore _store;
    private readonly ISecurityMasterCashFlowService _cashFlows;
    private readonly FileBondCalculationPolicy _policy;
    private readonly IBusinessDayCalendarSource? _calendars;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _cutoverGate = new(1, 1);

    public BondMigrationService(ISecurityMasterStore store, ISecurityMasterCashFlowService cashFlows,
        FileBondCalculationPolicy policy, IBusinessDayCalendarSource? calendars = null, TimeProvider? timeProvider = null)
    {
        _store = store;
        _cashFlows = cashFlows;
        _policy = policy;
        _calendars = calendars;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<BondMigrationPreviewDto> PreviewAsync(BondMigrationPreviewRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.SecurityId == Guid.Empty || request.ExpectedVersion <= 0 || request.Context is null
            || request.Context.AsOf == default || request.ScheduleConventions.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Migration preview requires identity, retained version, an explicit calculation date and a conventions object.");
        var conventions = SecurityVersionedTerms.ReadScheduleConventions(
            JsonSerializer.SerializeToElement(new { scheduleConventions = request.ScheduleConventions }), SecurityVersionedTerms.ScheduleConventionsKey);
        if (conventions.CouponRateUnit is not ("Percent" or "Fraction") || conventions.CouponCashBasis is not ("Level" or "DayCount"))
            throw new ArgumentException("Migration requires explicitly reviewed Percent or Fraction couponRateUnit and Level or DayCount couponCashBasis.");
        var retained = await _store.GetProjectionAsync(request.SecurityId, ct).ConfigureAwait(false)
            ?? throw new ArgumentException("The bond has no retained Security Master projection.");
        if (!SecurityAssetClassCatalog.GetOrDefault(retained.AssetClass).CarriesBondCouponStructure || retained.Status != SecurityStatusDto.Active)
            throw new ArgumentException("Migration preview requires an active Bond.");
        if (retained.Version != request.ExpectedVersion)
            throw new InvalidOperationException("The bond changed since the requested version; rebuild the migration preview.");
        var original = ToDetail(retained);
        var fields = JsonNode.Parse(retained.AssetSpecificTerms.GetRawText()) as JsonObject
            ?? throw new ArgumentException("Bond terms must be an object.");
        fields[SecurityVersionedTerms.ScheduleConventionsKey] = JsonNode.Parse(request.ScheduleConventions.GetRawText());
        var storedVersion = retained.AssetSpecificTerms.TryGetProperty("assetClassTermsVersion", out var value)
            && value.TryGetInt32(out var version) ? version : 1;
        fields["assetClassTermsVersion"] = Math.Max(2, storedVersion);
        var candidate = original with { AssetSpecificTerms = JsonSerializer.SerializeToElement(fields) };
        var resolution = ScheduleSpecFactory.Resolve(candidate, StructuredCashFlowTermsResolver.Resolve(candidate), _calendars);
        var gaps = new List<string>();
        var context = request.Context with { SecurityVersion = retained.Version };
        var before = await _cashFlows.GetProjectionAsync(original, StructuredCashFlowScenario.Base, context, ct).ConfigureAwait(false);
        StructuredCashFlowProjectionDto? after = null;
        if (resolution.Status != DeclaredScheduleStatus.Declared)
            gaps.Add(resolution.Refusal?.Message ?? "Candidate schedule conventions could not be resolved.");
        else
            after = await _cashFlows.GetProjectionAsync(candidate, StructuredCashFlowScenario.Base, context, ct).ConfigureAwait(false);
        if (before is null || before.BlockedReason is not null)
            gaps.Add($"Previous projection unavailable: {before?.BlockedReason ?? "no assigned source"}.");
        if (after is null || after.BlockedReason is not null)
            gaps.Add($"Candidate projection unavailable: {after?.BlockedReason ?? "no assigned source"}.");
        gaps.Add("Valuation, settlement accrued interest and book-specific accounting effects require their own reviewed calculations; cash differences alone do not authorize a journal or restatement.");
        var differences = Compare(before, after);
        var preview = new BondMigrationPreviewDto(request, OpenLotAmortization.SecurityHash(retained), string.Empty,
            before, after, differences, gaps, after is { BlockedReason: null });
        return preview with { PreviewSha256 = PreviewHash(preview) };
    }

    public async Task<UpdateSecurityFieldRequest> PrepareEditAsync(BondMigrationPreviewDto preview, string actor,
        string justification, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        ArgumentException.ThrowIfNullOrWhiteSpace(justification);
        if (!preview.CanStage || !Sha256Digest.FixedEquals(preview.PreviewSha256, PreviewHash(preview with { PreviewSha256 = string.Empty })))
            throw new ArgumentException("The migration preview is blocked or its retained content hash does not match.");
        var fresh = await PreviewAsync(preview.Request, ct).ConfigureAwait(false);
        if (!fresh.CanStage || !Sha256Digest.FixedEquals(fresh.PreviewSha256, preview.PreviewSha256))
            throw new InvalidOperationException("Migration inputs changed after review; rebuild and review the preview.");
        return new UpdateSecurityFieldRequest(preview.Request.SecurityId, preview.Request.ExpectedVersion,
            "assetSpecificTerms.scheduleConventions", preview.Request.ScheduleConventions.GetRawText(),
            preview.Request.Context.AsOf, actor.Trim(), justification.Trim(),
            SourceRecordId: $"bond-migration:{preview.PreviewSha256}", CorrelationId: preview.PreviewSha256);
    }

    public async Task<BondMigrationInventoryDto> GetInventoryAsync(CancellationToken ct = default)
    {
        // One full store read avoids offset-pagination omissions. A later writer can still change
        // inventory; the live policy refuses any later undeclared bond instead of reviving legacy math.
        var retained = await _store.LoadActiveAsync(ct).ConfigureAwait(false);
        var rows = new List<BondMigrationInventoryRowDto>();
        foreach (var bond in retained.Where(row => SecurityAssetClassCatalog.GetOrDefault(row.AssetClass).CarriesBondCouponStructure)
                     .OrderBy(row => row.SecurityId))
        {
            ct.ThrowIfCancellationRequested();
            var detail = ToDetail(bond);
            var declared = ScheduleSpecFactory.Resolve(detail, StructuredCashFlowTermsResolver.Resolve(detail), _calendars);
            var reason = FileBondCalculationPolicy.DeclarationBlocker(declared);
            rows.Add(new(bond.SecurityId, bond.Version, OpenLotAmortization.SecurityHash(bond), bond.DisplayName,
                reason is null, reason));
        }
        var hash = Sha256Digest.Compute(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(rows.ToArray(),
            BondMigrationJsonContext.Default.BondMigrationInventoryRowDtoArray)));
        var receipt = await _policy.ReadAsync(ct).ConfigureAwait(false);
        return new(hash, rows, receipt is not null, receipt is null && rows.Any(row => !row.HasDeclaredConventions),
            receipt is null ? null : FileBondCalculationPolicy.ReceiptHash(receipt));
    }

    public async Task<BondMigrationCutoverReceiptDto> CutoverAsync(BondMigrationCutoverRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Reviewer);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ExpectedInventorySha256);
        ArgumentNullException.ThrowIfNull(request.ExplicitBlocks);
        await _cutoverGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var inventory = await GetInventoryAsync(ct).ConfigureAwait(false);
            if (!Sha256Digest.FixedEquals(request.ExpectedInventorySha256, inventory.InventorySha256))
                throw new InvalidOperationException("Active bond inventory changed; rebuild and review the cutover inventory.");
            var blocks = request.ExplicitBlocks.OrderBy(entry => entry.SecurityId).ToArray();
            if (blocks.Select(entry => entry.SecurityId).Distinct().Count() != blocks.Length)
                throw new ArgumentException("Cutover contains duplicate security blocks.");
            foreach (var block in blocks)
            {
                var row = inventory.Bonds.SingleOrDefault(entry => entry.SecurityId == block.SecurityId);
                if (row is null || row.Version != block.Version || string.IsNullOrWhiteSpace(block.Reason)
                    || !Sha256Digest.FixedEquals(row.SecurityContentSha256, block.SecurityContentSha256))
                    throw new ArgumentException("Every explicit cutover block must name current retained bond identity, version, hash and a reviewed reason.");
            }
            if (inventory.Bonds.Any(row => !row.HasDeclaredConventions && blocks.All(block => block.SecurityId != row.SecurityId)))
                throw new InvalidOperationException("Every active bond must declare supported conventions or carry an explicit reviewed block before cutover.");
            var previous = await _policy.ReadAsync(ct).ConfigureAwait(false);
            if (previous is not null && SameDigest(previous.PreviousReceiptSha256, request.ExpectedPreviousReceiptSha256)
                && previous.InventorySha256 == inventory.InventorySha256 && previous.Reviewer == request.Reviewer.Trim()
                && previous.ExplicitBlocks.SequenceEqual(blocks))
                return previous;
            if (previous is null ? request.ExpectedPreviousReceiptSha256 is not null
                : request.ExpectedPreviousReceiptSha256 is null || !Sha256Digest.FixedEquals(
                    request.ExpectedPreviousReceiptSha256, FileBondCalculationPolicy.ReceiptHash(previous)))
                throw new InvalidOperationException("Bond cutover is immutable without a reviewed amendment naming its current receipt hash.");
            var receipt = new BondMigrationCutoverReceiptDto(inventory.InventorySha256, request.Reviewer.Trim(),
                _timeProvider.GetUtcNow(), blocks, ModelVersion, request.ExpectedPreviousReceiptSha256?.ToLowerInvariant());
            await _policy.WriteAsync(receipt, request.ExpectedPreviousReceiptSha256, ct).ConfigureAwait(false);
            return receipt;
        }
        finally { _cutoverGate.Release(); }
    }

    private static string PreviewHash(BondMigrationPreviewDto preview)
        => Sha256Digest.Compute(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(preview,
            BondMigrationJsonContext.Default.BondMigrationPreviewDto)));

    private static bool SameDigest(string? left, string? right)
        => left is null ? right is null : right is not null && Sha256Digest.FixedEquals(left, right);

    private static IReadOnlyList<BondMigrationCashDifference> Compare(StructuredCashFlowProjectionDto? before,
        StructuredCashFlowProjectionDto? after)
    {
        if (before is null || before.BlockedReason is not null || after is null || after.BlockedReason is not null)
            return [];
        var previous = before.Schedule.GroupBy(row => DateOnly.FromDateTime(row.PeriodDate.UtcDateTime))
            .ToDictionary(group => group.Key, group => (Principal: group.Sum(row => row.PrincipalAmount), Interest: group.Sum(row => row.InterestAmount)));
        var candidate = after.Schedule.GroupBy(row => DateOnly.FromDateTime(row.PeriodDate.UtcDateTime))
            .ToDictionary(group => group.Key, group => (Principal: group.Sum(row => row.PrincipalAmount), Interest: group.Sum(row => row.InterestAmount)));
        return previous.Keys.Union(candidate.Keys).OrderBy(date => date).Select(date =>
        {
            var left = previous.GetValueOrDefault(date);
            var right = candidate.GetValueOrDefault(date);
            return new BondMigrationCashDifference(date, left.Principal, right.Principal, left.Interest, right.Interest);
        }).Where(row => row.PreviousPrincipal != row.CandidatePrincipal || row.PreviousInterest != row.CandidateInterest).ToArray();
    }

    private static SecurityDetailDto ToDetail(SecurityProjectionRecord security)
        => new(security.SecurityId, security.AssetClass, security.Status, security.DisplayName, security.Currency,
            security.CommonTerms, security.AssetSpecificTerms, security.Identifiers, security.Aliases,
            security.Version, security.EffectiveFrom, security.EffectiveTo);
}
