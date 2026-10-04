using System.Text;
using System.Text.Json;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;

namespace Meridian.Strategies.Services;

public sealed class SecurityMasterAccountingEventSourceAdapter : ISecurityMasterAccountingEventSourceAdapter
{
    private readonly ISecurityMasterQueryService? _securityMasterQueryService;
    private readonly IAssetOperationsQueryService? _assetOperationsQueryService;

    public SecurityMasterAccountingEventSourceAdapter(
        ISecurityMasterQueryService? securityMasterQueryService = null,
        IAssetOperationsQueryService? assetOperationsQueryService = null)
    {
        _securityMasterQueryService = securityMasterQueryService;
        _assetOperationsQueryService = assetOperationsQueryService;
    }

    public async Task<SecurityMasterAccountingEventRequest?> BuildRequestAsync(
        StrategyRunDetail detail,
        ReconciliationRunRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(detail);
        ArgumentNullException.ThrowIfNull(request);

        if (_securityMasterQueryService is null)
        {
            return null;
        }

        var positions = CollectPositions(detail);
        if (positions.Count == 0)
        {
            return null;
        }

        var (periodStart, periodEnd) = ResolvePeriod(detail);
        var definitions = new Dictionary<Guid, SecurityEconomicDefinitionRecord>();
        foreach (var securityId in positions.Select(static position => position.SecurityId!.Value).Distinct())
        {
            ct.ThrowIfCancellationRequested();
            var definition = await _securityMasterQueryService
                .GetEconomicDefinitionByIdAsync(securityId, ct)
                .ConfigureAwait(false);
            if (definition is not null)
            {
                definitions[securityId] = definition;
            }
        }

        // Positions whose definition lookup MISSED are deliberately retained: the request still
        // carries them (with no matching security) so the event service records a High-severity
        // SECURITY_ACCOUNTING_RULE_MISSING completeness issue for each. Returning null here — or
        // filtering them out below — would silently suppress expected events AND the break for a
        // held security whose Security Master definition is unavailable.
        var securities = definitions.Values
            .Select(ToAccountingSecurity)
            .ToArray();

        var factorSchedule = BuildFactorSchedule(definitions.Values, periodStart, periodEnd);
        if (factorSchedule.Count > 0)
        {
            positions = await ResolveDurablePositionsAsync(positions, factorSchedule, ct)
                .ConfigureAwait(false);
        }

        var resolvedPositions = positions
            .Where(static position => position.SecurityId is not null)
            .GroupBy(
                static position => $"{position.SecurityId!.Value:N}|{position.AccountId}|{position.Symbol}|{position.PositionId:N}",
                StringComparer.OrdinalIgnoreCase)
            .Select(static group =>
            {
                var first = group.First();
                return new SecurityMasterAccountingPosition(
                    first.Symbol,
                     first.SecurityId,
                     first.AccountId,
                     group.Sum(static position => position.ParAmount),
                     PositionId: first.PositionId,
                     PositionVersion: first.PositionVersion)
                {
                    // The durable position's original face is identity-scoped (every row in the
                    // group shares the position id), so it carries through unsummed.
                    OriginalFaceAmount = first.OriginalFaceAmount,
                    // ParAmount sums absolute magnitudes, so a group mixing long and short rows
                    // would otherwise read as one large long exposure — any short row fails the
                    // whole group closed.
                    IsShort = group.Any(static position => position.IsShort)
                };
            })
            .Where(static position => position.ParAmount != 0m)
            .ToArray();

        if (resolvedPositions.Length == 0)
        {
            return null;
        }

        return new SecurityMasterAccountingEventRequest(
            request.RunId,
            periodStart,
            periodEnd,
            securities,
            resolvedPositions,
            FactorSchedule: factorSchedule.Count > 0 ? factorSchedule : null,
            AmountTolerance: request.AmountTolerance);
    }

    private async Task<List<SecurityMasterAccountingPosition>> ResolveDurablePositionsAsync(
        IReadOnlyList<SecurityMasterAccountingPosition> positions,
        IReadOnlyList<SecurityFactorObservation> factorSchedule,
        CancellationToken ct)
    {
        if (_assetOperationsQueryService is null)
        {
            return positions.ToList();
        }

        // Durable ownership resolves as of each security's FACTOR OBSERVATION DATES, not one
        // period-wide date: an observation belongs to whichever position held the security on the
        // observation date, and resolving at the period's end could attach an early-month paydown
        // to a successor position opened after the observation. When the period's observations do
        // not all resolve to the SAME durable position (ownership changed between observations),
        // the run position stays unresolved rather than mislabeling any observation - the single
        // aggregated run position cannot faithfully represent a split-ownership period.
        var observationDates = factorSchedule
            .GroupBy(static factor => factor.SecurityId)
            .ToDictionary(
                static group => group.Key,
                static group => group.Select(static factor => factor.AsOfDate).Distinct().ToArray());

        var details = new Dictionary<Guid, AssetOperationsDetailDto?>();
        foreach (var securityId in positions
                     .Where(static position => position.SecurityId.HasValue)
                     .Select(static position => position.SecurityId!.Value)
                     .Where(observationDates.ContainsKey)
                     .Distinct())
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                details[securityId] = await _assetOperationsQueryService
                    .GetOperationsAsync(securityId, ct)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                details[securityId] = null;
            }
        }

        return positions.Select(position =>
        {
            if (position.SecurityId is not Guid securityId ||
                !details.TryGetValue(securityId, out var detail) ||
                !observationDates.TryGetValue(securityId, out var securityObservationDates))
            {
                return position;
            }

            if (detail is null)
            {
                // The security HAS in-period factor observations but the Asset Operations read
                // failed (or returned nothing), so ownership could not be verified at all. A
                // supplied identity (e.g. a ledger dimension PositionId) must not pass through as
                // if resolved — factor generation would post against an unverified, possibly stale
                // owner for the whole outage. Clearing the identity makes the paydown generator
                // fail closed with FACTOR_PAYDOWN_POSITION_REQUIRED instead.
                return position with { PositionId = null, PositionVersion = 1 };
            }

            BookPositionDto? resolved = null;
            foreach (var asOfDate in securityObservationDates)
            {
                var candidates = detail.BookPositions
                    .Where(candidate => candidate.SecurityId == securityId)
                    .Where(candidate => position.PositionId is Guid suppliedPositionId
                        ? candidate.PositionId == suppliedPositionId
                        : string.Equals(candidate.PrimaryAccountId?.Trim(), position.AccountId.Trim(), StringComparison.OrdinalIgnoreCase))
                    // Activity is judged AS OF the observation date by the effective window, not by
                    // the position's CURRENT lifecycle status: Asset Operations returns closed
                    // positions too, and a position that owned the security on the observation date
                    // but was closed afterwards still carries the historical paydown — requiring
                    // today's Active status would clear its identity and fail month-end
                    // reconciliation closed on FACTOR_PAYDOWN_POSITION_REQUIRED. A position closed
                    // BEFORE the observation date is already excluded by its EffectiveTo bound.
                    .Where(candidate => candidate.EffectiveFrom <= asOfDate &&
                        (candidate.EffectiveTo is null || candidate.EffectiveTo >= asOfDate))
                    .ToArray();

                if (candidates.Length != 1 ||
                    (resolved is not null && resolved.PositionId != candidates[0].PositionId))
                {
                    // Resolution RAN and could not confirm one active owner for every observation
                    // date — the supplied identity (e.g. a ledger dimension PositionId inactive on
                    // an observation date, or ownership split across observations) must not pass
                    // through, or every paydown and posting candidate would carry the exact stale
                    // position this resolver exists to prevent. The position returns explicitly
                    // UNRESOLVED; the paydown generator then fails closed with its
                    // FACTOR_PAYDOWN_POSITION_REQUIRED issue instead of posting against the wrong
                    // owner, and the operator resolves the ownership question.
                    return position with { PositionId = null, PositionVersion = 1 };
                }

                resolved = candidates[0];
            }

            if (resolved is null)
            {
                return position;
            }

            var durable = resolved;
            // Factor paydowns are computed against ORIGINAL face (factors are relative to it):
            // the run position's quantity may already be the factor-adjusted current face, so
            // when the durable book position retains its original/par face, that value — not the
            // run quantity — is the held face the paydown math must use. It rides a SEPARATE
            // paydown basis: ParAmount keeps the CURRENT outstanding balance (the run quantity),
            // which is what coupon accruals bill — replacing it with original face would
            // overstate expected interest and journal previews by the already-paid-down portion.
            // The generator falls back to ParAmount when the durable state records no face.
            return position with
            {
                PositionId = durable.PositionId,
                PositionVersion = durable.Version,
                OriginalFaceAmount = durable.CurrentEconomicState?.OriginalFaceAmount
                    ?? durable.CurrentEconomicState?.ParAmount
            };
        }).ToList();
    }

    private static List<SecurityMasterAccountingPosition> CollectPositions(StrategyRunDetail detail)
    {
        var positions = new List<SecurityMasterAccountingPosition>();
        if (detail.Portfolio is not null)
        {
            foreach (var position in detail.Portfolio.Positions)
            {
                if (position.Security is null || string.IsNullOrWhiteSpace(position.Symbol) || position.Quantity == 0)
                {
                    continue;
                }

                positions.Add(new SecurityMasterAccountingPosition(
                    position.Symbol.Trim().ToUpperInvariant(),
                    position.Security.SecurityId,
                    ResolveAccountId(position.AccountScopeId, detail.Portfolio.AccountScopeId),
                    Math.Abs((decimal)position.Quantity))
                {
                    // ParAmount is an absolute magnitude, so the direction must travel separately:
                    // dropping it would present a short as a long holding and generate long-side
                    // income and receivable events for a liability.
                    IsShort = position.IsShort || position.Quantity < 0
                });
            }
        }

        if (positions.Count > 0)
        {
            return positions;
        }

        if (detail.Ledger is null)
        {
            return positions;
        }

        foreach (var line in detail.Ledger.TrialBalance)
        {
            if (line.Security is null || string.IsNullOrWhiteSpace(line.Symbol) || line.Balance == 0m)
            {
                continue;
            }

            positions.Add(new SecurityMasterAccountingPosition(
                line.Symbol.Trim().ToUpperInvariant(),
                line.Security.SecurityId,
                ResolveAccountId(line.AccountScopeId ?? line.FinancialAccountId, detail.Ledger.AccountScopeId),
                Math.Abs(line.Balance),
                PositionId: line.Dimensions?.PositionId)
            {
                IsShort = line.Balance < 0m
            });
        }

        return positions;
    }

    private static SecurityMasterAccountingSecurity ToAccountingSecurity(SecurityEconomicDefinitionRecord definition)
    {
        var economicTerms = definition.EconomicTerms;
        var coupon = GetObject(economicTerms, "coupon");
        var accrual = GetObject(economicTerms, "accrual");
        var maturity = GetObject(economicTerms, "maturity");
        var payment = GetObject(economicTerms, "payment");
        var structuredProduct = GetObject(economicTerms, "structuredProduct");
        var redemption = GetObject(economicTerms, "redemption");

        var couponType = ReadString(coupon, "couponType");
        var currentFactor = ReadDecimal(structuredProduct, "factor");
        var originalFace = ReadDecimal(structuredProduct, "notionalBalance");
        decimal? currentFace = originalFace is decimal face && currentFactor is decimal factor
            ? decimal.Round(face * factor, 6, MidpointRounding.AwayFromZero)
            : null;

        return new SecurityMasterAccountingSecurity(
            definition.SecurityId,
            ResolveSymbol(definition),
            ResolveAccountingAssetClass(definition),
            definition.Currency,
            new SecurityFixedIncomeTerms(
                CouponRate: ReadDecimal(coupon, "couponRate") ?? ReadDecimal(structuredProduct, "weightedAvgCoupon"),
                CouponType: NormalizeCouponType(couponType),
                DayCountConvention: ReadString(coupon, "dayCount") ?? ReadString(accrual, "dayCount"),
                PaymentFrequencyPerYear: ResolvePaymentFrequency(
                    ReadString(coupon, "paymentFrequency") ?? ReadString(payment, "paymentFrequency")),
                IssueDate: ReadDate(maturity, "issueDate"),
                DatedDate: ReadDate(maturity, "effectiveDate"),
                MaturityDate: ReadDate(maturity, "maturityDate"),
                AccrualStartDate: ReadDate(accrual, "accrualStartDate") ?? ReadDate(maturity, "effectiveDate"),
                CurrentFactor: currentFactor,
                OriginalFace: originalFace,
                CurrentFace: currentFace,
                // A retained TYPED factor schedule marks the security as factor-driven even when
                // no scalar factor or amortizing flag is present: a canonical StructuredCredit may
                // carry its whole factor history in factorScheduleEntries, and a later month with
                // no in-period observation must still gate on missing/stale factor coverage
                // instead of silently skipping the amortizing security.
                RequiresFactorSchedule: currentFactor is < 1m
                    || ReadBool(redemption, "isAmortizing") == true
                    || HasTypedFactorSchedule(definition)),
            ToAccountingRule(definition));
    }

    private static SecurityAccountingRule? ToAccountingRule(SecurityEconomicDefinitionRecord definition)
    {
        var classification = ReadString(definition.CommonTerms, "accountingClassification") ??
            ReadString(definition.LegacyAssetSpecificTerms, "accountingClassification") ??
            ReadString(definition.Classification, "accountingClassification");
        if (string.IsNullOrWhiteSpace(classification))
        {
            return null;
        }

        return new SecurityAccountingRule(classification.Trim(), "GAAP");
    }

    private static IReadOnlyList<SecurityFactorObservation> BuildFactorSchedule(
        IEnumerable<SecurityEconomicDefinitionRecord> definitions,
        DateOnly periodStart,
        DateOnly periodEnd)
    {
        var entries = new List<SecurityFactorObservation>();
        foreach (var definition in definitions)
        {
            var coveredDates = new HashSet<DateOnly>();
            var definitionStartCount = entries.Count;
            SecurityFactorObservation? latestPrePeriod = null;
            foreach (var schedule in EnumerateFactorScheduleArrays(definition))
            {
                foreach (var item in schedule.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var asOfDate = ReadDate(item, "asOfDate") ??
                        ReadDate(item, "factorDate") ??
                        ReadDate(item, "date");
                    var priorFactor = ReadDecimal(item, "priorFactor") ??
                        ReadDecimal(item, "previousFactor");
                    var currentFactor = ReadDecimal(item, "currentFactor") ??
                        ReadDecimal(item, "factor");
                    // Period end is EXCLUSIVE (ResolvePeriod supplies the next month's first day),
                    // so a month-boundary row surfaces in exactly one reconciliation.
                    if (asOfDate is null ||
                        asOfDate >= periodEnd ||
                        priorFactor is null ||
                        currentFactor is null)
                    {
                        continue;
                    }

                    var entry = new SecurityFactorObservation(
                        definition.SecurityId,
                        asOfDate.Value,
                        priorFactor.Value,
                        currentFactor.Value,
                        ReadString(item, "source") ?? ResolveProvenanceSourceSystem(definition) ?? "security-master",
                        ReadString(item, "evidenceLink") ?? ReadString(item, "evidenceId") ?? ReadString(item, "evidenceRoute"),
                        ReadString(item, "sourceContentHash") ??
                        ReadString(item, "contentHash") ??
                        ReadString(item, "sourceHash") ??
                        ReadString(definition.Provenance, "sourceContentHash") ??
                        HashFactorRow(item));

                    if (asOfDate < periodStart)
                    {
                        // Pre-period rows never enter the request as coverage — but the LATEST one
                        // is retained below when the security has no in-period observation at all,
                        // so the coverage classifier can report FACTOR_STALE instead of the
                        // indistinguishable FACTOR_SCHEDULE_MISSING.
                        if (latestPrePeriod is null || entry.AsOfDate > latestPrePeriod.AsOfDate)
                        {
                            latestPrePeriod = entry;
                        }

                        continue;
                    }

                    if (!coveredDates.Add(asOfDate.Value))
                    {
                        continue;
                    }

                    entries.Add(entry);
                }
            }

            // Typed factorScheduleEntries rows ({asOfDate, factor}) written by the canonical F#
            // StructuredCredit serializer carry no per-row priorFactor: the prior is the ORDERED
            // preceding entry's factor, derived over the whole array so an in-period row whose
            // predecessor falls outside the period still pairs correctly. The first observation
            // pairs against the original-face baseline of 1.0. Dates already asserted by an
            // explicit legacy row are skipped — the explicit prior is authoritative.
            // The typed rows carry no per-row evidence link; the canonical StructuredCredit record
            // retains its trustee-report pointer as the free-text factorSchedule field, which is
            // the retained evidence the paydown projector requires. Rows with no resolvable
            // evidence still surface — the projector fails them closed with its evidence-required
            // issue rather than this adapter fabricating a link. The GOVERNED nested pointer wins
            // over an outer pass-through copy, matching the nested-first precedence of the typed
            // schedule itself and the shared term-source walk — an ungoverned outer value must not
            // supply the trustee-report lineage the expected event records.
            var typedRowEvidence = definition.LegacyAssetSpecificTerms is JsonElement legacyTermsForEvidence
                ? ReadString(GetObject(legacyTermsForEvidence, "profileFields"), "factorSchedule")
                    ?? ReadString(legacyTermsForEvidence, "factorSchedule")
                : null;
            foreach (var schedule in EnumerateTypedFactorScheduleArrays(definition))
            {
                var typedRows = new List<(DateOnly AsOfDate, decimal Factor, JsonElement Item)>();
                foreach (var item in schedule.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var asOfDate = ReadDate(item, "asOfDate") ??
                        ReadDate(item, "factorDate") ??
                        ReadDate(item, "date");
                    var factor = ReadDecimal(item, "factor") ??
                        ReadDecimal(item, "currentFactor");
                    if (asOfDate is not null && factor is not null)
                    {
                        typedRows.Add((asOfDate.Value, factor.Value, item));
                    }
                }

                var ordered = typedRows.OrderBy(static row => row.AsOfDate).ToArray();
                for (var i = 0; i < ordered.Length; i++)
                {
                    var row = ordered[i];
                    // The FIRST observation's prior is the original-face baseline of 1.0: factors
                    // are relative to original face, so a schedule opening below one records a
                    // real first paydown (canonical validation does not require an explicit 1.00
                    // baseline row). EVERY in-period observation emits — including unchanged ones
                    // and an explicit 1.00 opening — because each is the period's factor COVERAGE
                    // evidence; dropping one whose factor equals its prior would raise a false
                    // missing-coverage issue when it is the period's only observation. The
                    // paydown generator skips no-principal-moved rows instead of projecting a
                    // zero-change candidate.
                    var priorFactor = i == 0 ? 1.00m : ordered[i - 1].Factor;

                    // The period end is EXCLUSIVE: ResolvePeriod supplies the first day of the
                    // NEXT month, so an inclusive comparison would surface a month-boundary
                    // observation in two adjacent reconciliations as duplicate paydown evidence.
                    if (row.AsOfDate >= periodEnd)
                    {
                        continue;
                    }

                    // Typed rows carry no per-row source; the canonical F# provenance serializes
                    // the asserting provider under sourceSystem, so that vendor identity — not the
                    // generic security-master fallback — is the factor-source lineage the expected
                    // event must record.
                    var typedEntry = new SecurityFactorObservation(
                        definition.SecurityId,
                        row.AsOfDate,
                        priorFactor,
                        row.Factor,
                        ReadString(row.Item, "source")
                            ?? ResolveProvenanceSourceSystem(definition)
                            ?? "security-master",
                        ReadString(row.Item, "evidenceLink") ?? typedRowEvidence,
                        ReadString(definition.Provenance, "sourceContentHash") ?? HashFactorRow(row.Item));

                    if (row.AsOfDate < periodStart)
                    {
                        if (latestPrePeriod is null || typedEntry.AsOfDate > latestPrePeriod.AsOfDate)
                        {
                            latestPrePeriod = typedEntry;
                        }

                        continue;
                    }

                    if (!coveredDates.Add(row.AsOfDate))
                    {
                        continue;
                    }

                    entries.Add(typedEntry);
                }
            }

            // A factor-driven security whose observations ALL predate the period still needs its
            // latest retained row in the request: the coverage classifier can distinguish
            // FACTOR_STALE (evidence needs refreshing) from FACTOR_SCHEDULE_MISSING (no evidence
            // at all) only when it sees that row. The paydown generator filters to in-period rows
            // itself, so the retained observation never projects a paydown — and securities WITH
            // in-period coverage never carry it, keeping paydown evidence period-scoped.
            if (entries.Count == definitionStartCount && latestPrePeriod is not null)
            {
                entries.Add(latestPrePeriod);
            }
        }

        return entries
            .OrderBy(static entry => entry.SecurityId)
            .ThenBy(static entry => entry.AsOfDate)
            .ToArray();
    }

    /// <summary>
    /// True when the record retains a nonempty typed factor schedule (governed profileFields or
    /// envelope root) — the presence signal that makes the security factor-driven for coverage
    /// gating regardless of scalar terms.
    /// </summary>
    private static bool HasTypedFactorSchedule(SecurityEconomicDefinitionRecord definition)
    {
        foreach (var schedule in EnumerateTypedFactorScheduleArrays(definition))
        {
            foreach (var item in schedule.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// The record-level asserting provider: the canonical <c>sourceSystem</c> when present, then
    /// the legacy free-text <c>source</c> key; null when neither names a provider, so callers can
    /// apply their own terminal fallback.
    /// </summary>
    private static string? ResolveProvenanceSourceSystem(SecurityEconomicDefinitionRecord definition)
    {
        var sourceSystem = SecurityMasterProvenanceReader.Read(definition.Provenance).SourceSystem;
        if (!string.Equals(sourceSystem, SecurityMasterProvenanceReader.UnknownSource, StringComparison.OrdinalIgnoreCase))
        {
            return sourceSystem;
        }

        return ReadString(definition.Provenance, "source");
    }

    private static string HashFactorRow(JsonElement item)
        => $"sha256:{Sha256Digest.ComputeUtf8(item.GetRawText())}";

    private static IEnumerable<JsonElement> EnumerateFactorScheduleArrays(SecurityEconomicDefinitionRecord definition)
    {
        if (TryGetArray(definition.EconomicTerms, "factorSchedule", out var rootSchedule))
        {
            yield return rootSchedule;
        }

        var structuredProduct = GetObject(definition.EconomicTerms, "structuredProduct");
        if (TryGetArray(structuredProduct, "factorSchedule", out var structuredSchedule))
        {
            yield return structuredSchedule;
        }
    }

    /// <summary>
    /// The typed {asOfDate, factor} arrays the canonical F# StructuredCredit serializer persists:
    /// at the asset-specific-terms root for first-class records, and beneath the profile envelope's
    /// profileFields for profile-backed ones. Unlike the legacy arrays above, their rows carry no
    /// per-row prior factor.
    /// </summary>
    private static IEnumerable<JsonElement> EnumerateTypedFactorScheduleArrays(SecurityEconomicDefinitionRecord definition)
    {
        // Governed profileFields rows OWN the typed schedule when they exist, mirroring the term
        // resolver's precedence: profileFields values are schema- and profile-validated on write,
        // while an extra outer array on an envelope is ungoverned pass-through. The outer array is
        // not merely deprioritized but EXCLUDED — each enumerated array derives its own priors
        // (first observation against the 1.0 baseline), so an outer row on a date the governed
        // schedule does not cover would synthesize a paydown from a prior the governed history
        // contradicts, and coveredDates only suppresses exact date matches.
        var profileFields = definition.LegacyAssetSpecificTerms is JsonElement legacyTerms
            ? GetObject(legacyTerms, "profileFields")
            : null;
        if (TryGetArray(profileFields, "factorScheduleEntries", out var nestedEntries))
        {
            yield return nestedEntries;
            yield break;
        }

        if (TryGetArray(definition.LegacyAssetSpecificTerms, "factorScheduleEntries", out var rootEntries))
        {
            yield return rootEntries;
        }
    }

    private static (DateOnly PeriodStart, DateOnly PeriodEnd) ResolvePeriod(StrategyRunDetail detail)
    {
        var asOf = detail.Portfolio?.AsOf ?? detail.Ledger?.AsOf ?? detail.Summary.CompletedAt ?? detail.Summary.StartedAt;
        var date = DateOnly.FromDateTime(asOf.UtcDateTime);
        var start = new DateOnly(date.Year, date.Month, 1);
        var end = start.AddMonths(1);
        return (start, end);
    }

    private static string ResolveSymbol(SecurityEconomicDefinitionRecord definition)
    {
        var symbol = definition.Identifiers.FirstOrDefault(static identifier =>
            identifier.Kind == SecurityIdentifierKind.Ticker)?.Value;
        return string.IsNullOrWhiteSpace(symbol)
            ? definition.DisplayName.Trim().ToUpperInvariant()
            : symbol.Trim().ToUpperInvariant();
    }

    /// <summary>
    /// Resolves the accounting-slice instrument class from the classification the record DECLARES,
    /// via <see cref="SecurityAssetClassCatalog.ResolveAccountingInstrumentClass"/>. The declared
    /// names are offered most-specific first — the legacy (canonical) asset class, then the type
    /// name, then the sub-type — with the coarse taxonomy asset class last.
    /// <para>
    /// <see cref="SecurityEconomicDefinitionRecord.AssetFamily"/> is deliberately NOT consulted. It
    /// is a reporting rollup label, not an instrument identity: <c>CashSweep</c> and
    /// <c>StructuredCredit</c> shared the <c>StructuredCash</c> family, so matching on it classified
    /// every cash-sweep vehicle as an asset-backed security, admitted it to the fixed-income slice,
    /// and turned an Info-severity "not in this slice" note into a High-severity
    /// SECURITY_ACCOUNTING_RULE_MISSING break. The families are split now; the classification still
    /// reads only from what the record says it IS.
    /// </para>
    /// <para>
    /// A record naming no covered class falls back to its sub-type (then its taxonomy asset class),
    /// which the event service's own gate rejects as SM_UNSUPPORTED_ACCOUNTING_INSTRUMENT at Info —
    /// the correct outcome for an instrument this first accounting slice does not cover.
    /// </para>
    /// </summary>
    private static string ResolveAccountingAssetClass(SecurityEconomicDefinitionRecord definition)
        => SecurityAssetClassCatalog.ResolveAccountingInstrumentClass(
               definition.LegacyAssetClass,
               definition.TypeName,
               definition.SubType,
               definition.AssetClass)
           ?? definition.SubType
           ?? definition.AssetClass;

    private static string? NormalizeCouponType(string? couponType)
    {
        if (string.IsNullOrWhiteSpace(couponType))
        {
            return null;
        }

        return couponType.Trim() switch
        {
            "FixedRate" => "Fixed",
            "FloatingRate" => "Floating",
            var value => value
        };
    }

    private static int? ResolvePaymentFrequency(string? frequency)
    {
        if (string.IsNullOrWhiteSpace(frequency))
        {
            return null;
        }

        return frequency.Trim() switch
        {
            "Annual" => 1,
            "SemiAnnual" => 2,
            "Semi-Annual" => 2,
            "Quarterly" => 4,
            "Monthly" => 12,
            "Weekly" => 52,
            "Daily" => 365,
            var value when int.TryParse(value, out var parsed) => parsed,
            _ => null
        };
    }

    private static string ResolveAccountId(string? primary, string? fallback)
    {
        if (!string.IsNullOrWhiteSpace(primary))
        {
            return primary.Trim();
        }

        if (!string.IsNullOrWhiteSpace(fallback))
        {
            return fallback.Trim();
        }

        return "unscoped-account";
    }

    private static JsonElement? GetObject(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out var value) ||
            value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return value;
    }

    private static bool TryGetArray(JsonElement? element, string propertyName, out JsonElement value)
    {
        if (element is { ValueKind: JsonValueKind.Object } objectElement &&
            objectElement.TryGetProperty(propertyName, out value) &&
            value.ValueKind == JsonValueKind.Array)
        {
            return true;
        }

        value = default;
        return false;
    }

    private static string? ReadString(JsonElement? element, string propertyName)
    {
        if (element is not { ValueKind: JsonValueKind.Object } objectElement ||
            !objectElement.TryGetProperty(propertyName, out var value) ||
            value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return value.GetString();
    }

    private static decimal? ReadDecimal(JsonElement? element, string propertyName)
    {
        if (element is not { ValueKind: JsonValueKind.Object } objectElement ||
            !objectElement.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetDecimal(out var parsed) => parsed,
            JsonValueKind.String when decimal.TryParse(value.GetString(), out var parsed) => parsed,
            _ => null
        };
    }

    private static bool? ReadBool(JsonElement? element, string propertyName)
    {
        if (element is not { ValueKind: JsonValueKind.Object } objectElement ||
            !objectElement.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static DateOnly? ReadDate(JsonElement? element, string propertyName)
    {
        var raw = ReadString(element, propertyName);
        return DateOnly.TryParse(raw, out var parsed) ? parsed : null;
    }
}
