using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.FSharp.Domain;
using Meridian.FSharp.SecurityMasterInterop;
using Xunit;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// ADR-027 stage 2 (S2-B1): pins the equivalence between the codec-side kind validation
/// (<c>validateKindTerms</c>, reached through
/// <see cref="SecurityMasterCommandFacade.ValidateKindInvariants"/>) and the new validation
/// surface over the economic definition's modules
/// (<see cref="SecurityMasterCommandFacade.ValidateDefinitionInvariants"/>). For every declared
/// class, the clean twin passes both; for every error code the ledger
/// (<see cref="SecurityMasterCommandFacade.DefinitionExpressibleCodes"/>) declares
/// definition-expressible, a mutation of the twin trips <b>both</b> validators with that code,
/// and the definition validator never reports a code the kind validator does not — so S2-B2 can
/// switch the command path onto the definition surface without changing a single verdict on the
/// expressible set. Codes absent from the ledger are codec-only: their subject data has no
/// module home yet, and they stay with the kind validation at the codec boundary.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SecurityDefinitionValidationEquivalenceTests
{
    public static TheoryData<string> DeclaredAssetClasses() => SecurityAssetTermsSchemaRoundTripTests.DeclaredAssetClasses();

    [Theory]
    [MemberData(nameof(DeclaredAssetClasses))]
    public void CleanTwin_PassesBothValidators(string assetClass)
    {
        var (kind, modules) = KindAndModules(assetClass, FlatTwin(assetClass));

        SecurityMasterCommandFacade.ValidateKindInvariants(kind).Should().BeEmpty(
            $"the {assetClass} twin is a valid record");
        SecurityMasterCommandFacade.ValidateDefinitionInvariants(assetClass, modules).Should().BeEmpty(
            $"the definition surface must agree with the kind surface on the valid {assetClass} twin");
    }

    // ── The mutation table: one row per definition-expressible code ─────────────────────────────

    public static TheoryData<string, string, string, string> ExpressibleCodeMutations()
    {
        var data = new TheoryData<string, string, string, string>();
        foreach (var (assetClass, code, path, value) in Mutations)
        {
            data.Add(assetClass, code, path, value);
        }

        return data;
    }

    private static readonly (string AssetClass, string Code, string Path, string Value)[] Mutations =
    [
        // The Bond twin carries a floating coupon, whose flat couponRate feeds the floating
        // structure; the rule reads the fixed-coupon rate, so the mutation pins the type too.
        ("Bond", "bond_coupon_invalid", "*", """{"couponType":"Fixed","couponRate":-1}"""),
        ("CertificateOfDeposit", "cd_issuer_required", "issuerName", "\" \""),
        ("CertificateOfDeposit", "cd_coupon_invalid", "couponRate", "-1"),
        ("CertificateOfDeposit", "cd_callable_date_invalid", "callableDate", "\"2999-01-01\""),
        ("Deposit", "deposit_type_required", "depositType", "\" \""),
        ("Deposit", "deposit_institution_required", "institutionName", "\" \""),
        ("Deposit", "deposit_interest_rate_invalid", "interestRate", "-1"),
        ("MoneyMarketFund", "mmf_wam_invalid", "weightedAverageMaturityDays", "-1"),
        ("CommercialPaper", "commercial_paper_issuer_required", "issuerName", "\" \""),
        ("CommercialPaper", "commercial_paper_discount_rate_invalid", "discountRate", "-1"),
        ("TreasuryBill", "treasury_bill_auction_date_invalid", "auctionDate", "\"2999-01-01\""),
        ("TreasuryBill", "treasury_bill_discount_rate_invalid", "discountRate", "-1"),
        ("Repo", "repo_counterparty_required", "counterparty", "\" \""),
        ("Repo", "repo_date_range_invalid", "startDate", "\"2999-01-01\""),
        ("Repo", "repo_rate_invalid", "repoRate", "-1"),
        ("Repo", "repo_haircut_invalid", "haircut", "-1"),
        ("CashSweep", "cash_sweep_program_required", "programName", "\" \""),
        ("CashSweep", "cash_sweep_vehicle_required", "sweepVehicleType", "\" \""),
        ("CashSweep", "cash_sweep_yield_rate_invalid", "yieldRate", "-1"),
        ("StructuredCredit", "structured_credit_tranche_required", "tranche", "\" \""),
        ("StructuredCredit", "structured_credit_collateral_required", "collateralType", "\" \""),
        ("StructuredCredit", "structured_credit_original_face_invalid", "originalFace", "-1"),
        ("StructuredCredit", "structured_credit_current_factor_invalid", "currentFactor", "1.5"),
        ("StructuredCredit", "structured_credit_coupon_index_required", "couponOrIndex", "\" \""),
        // structured_credit_factor_schedule_invalid, _duplicate_date and _not_monotonic cannot be
        // exercised through the JSON route: the codec gate
        // (SecurityEconomicScheduleValidation.ThrowIfInvalid in ToSecurityKind) refuses those
        // shapes before either validator runs, so both validators carry them as defense in depth.
        // SecurityDefinitionValidationTests (Meridian.FSharp.Tests) mutates the records directly
        // and pins both surfaces for those codes; CodesCoveredByFSharpTests keeps the ledger
        // coverage honest here.
        ("StructuredCredit", "structured_credit_factor_after_maturity", "factorScheduleEntries",
            """[{"asOfDate":"2999-01-01","factor":0.9}]"""),
        ("StructuredCredit", "structured_credit_weighted_average_coupon_invalid", "weightedAverageCoupon", "-1"),
        ("StructuredCredit", "structured_credit_weighted_average_maturity_invalid", "weightedAverageMaturityMonths", "-1"),
        ("StructuredCredit", "structured_credit_weighted_average_loan_age_invalid", "weightedAverageLoanAgeMonths", "-1"),
        ("Commodity", "commodity_contract_size_invalid", "contractSize", "-1"),
        ("Cfd", "cfd_leverage_invalid", "leverage", "-1"),
        // Zero regression (Codex review on #3439): the module projection divides by leverage, so
        // zero must carry as a zero margin and be refused, not throw before validation runs.
        ("Cfd", "cfd_leverage_invalid", "leverage", "0"),
        ("Warrant", "warrant_multiplier_invalid", "multiplier", "-1"),
        ("CapFloor", "cap_floor_reference_index_required", "referenceIndex", "\" \""),
        ("CapFloor", "cap_floor_strike_order_invalid", "floorStrike", "99999"),
        ("CapFloor", "cap_floor_date_range_invalid", "effectiveDate", "\"2999-01-01\""),
        ("Swap", "swap_date_range_invalid", "effectiveDate", "\"2999-01-01\""),
        ("CreditDefaultSwap", "cds_date_range_invalid", "effectiveDate", "\"2999-01-01\""),
        ("CreditDefaultSwap", "cds_fixed_coupon_invalid", "fixedCouponBps", "-100"),
        ("NonDeliverableForward", "ndf_fixing_date_invalid", "fixingDate", "\"2999-01-01\""),
        ("FxForward", "fx_forward_trade_date_invalid", "tradeDate", "\"2999-01-01\""),
        ("StructuredNote", "structured_note_issuer_required", "issuerName", "\" \""),
        ("StructuredNote", "structured_note_issue_date_invalid", "issueDate", "\"2999-01-01\""),
        ("StructuredNote", "structured_note_coupon_invalid", "couponRate", "-1"),
        ("CommitmentGuarantee", "commitment_guarantee_counterparty_required", "counterparty", "\" \""),
        ("CommitmentGuarantee", "commitment_guarantee_date_range_invalid", "effectiveDate", "\"2999-01-01\""),
        ("DirectLoan", "direct_loan_borrower_required", "borrower", "\" \""),
        ("PrivateFundInterest", "private_fund_gp_required", "gpSponsor", "\" \""),
        ("PrivateFundInterest", "private_fund_strategy_required", "strategy", "\" \""),
        ("PrivateCompanyEquity", "private_company_issuer_required", "issuer", "\" \""),
        ("PrivateCompanyEquity", "private_company_share_class_required", "shareClass", "\" \""),
        ("PrivateCompanyEquity", "private_company_round_required", "round", "\" \""),
        ("RealEstateHolding", "real_estate_property_type_required", "propertyType", "\" \""),
    ];

    [Theory]
    [MemberData(nameof(ExpressibleCodeMutations))]
    public void MutatedTwin_TripsBothValidators_WithTheSameCode(string assetClass, string code, string path, string value)
    {
        var twin = JsonNode.Parse(FlatTwin(assetClass).GetRawText())!.AsObject();
        if (path == "*")
        {
            foreach (var edit in JsonNode.Parse(value)!.AsObject())
            {
                twin[edit.Key] = edit.Value?.DeepClone();
            }
        }
        else
        {
            twin[path] = JsonNode.Parse(value);
        }
        var (kind, modules) = KindAndModules(assetClass, JsonDocument.Parse(twin.ToJsonString()).RootElement.Clone());

        var kindCodes = SecurityMasterCommandFacade.ValidateKindInvariants(kind).Select(static e => e.Code).ToArray();
        var definitionCodes = SecurityMasterCommandFacade.ValidateDefinitionInvariants(assetClass, modules).Select(static e => e.Code).ToArray();

        kindCodes.Should().Contain(code, $"the mutation must trip the kind validator ({assetClass}.{path} = {value})");
        definitionCodes.Should().Contain(code, "the definition surface must report the same verdict on an expressible code");
        definitionCodes.Should().BeSubsetOf(kindCodes,
            "the definition surface may never invent an error the kind surface does not raise");
        definitionCodes.Should().BeSubsetOf(SecurityMasterCommandFacade.DefinitionExpressibleCodes[assetClass],
            "every reported definition code must be in the class's expressible ledger");
    }

    /// <summary>
    /// Codes whose invalid shapes the codec gate refuses before any validator runs on the JSON
    /// route; their two-surface proof lives in the F# suite (SecurityDefinitionValidationTests),
    /// which mutates the records directly.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string[]> CodesCoveredByFSharpTests =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["StructuredCredit"] =
            [
                "structured_credit_factor_schedule_invalid",
                "structured_credit_factor_schedule_duplicate_date",
                "structured_credit_factor_schedule_not_monotonic"
            ]
        };

    /// <summary>
    /// Parity guard for the asset-class literal ratchet
    /// (<c>SecurityAssetClassLiteralRatchetTests</c>): the class-gated
    /// <c>validateDefinitionTerms</c> dispatch in <c>SecurityMasterCommands.fs</c> is a declared
    /// dispatch site, and its ledger may only name declared catalog classes. Together with the
    /// clean-twin theory (every declared class runs both validators) and
    /// <see cref="MutationTable_CoversEveryLedgeredCode"/> (the proof covers exactly the ledger),
    /// this holds the F# dispatch to <see cref="SecurityAssetClassCatalog"/>.
    /// </summary>
    [Fact]
    public void LedgeredClasses_AreDeclaredCatalogClasses()
    {
        SecurityMasterCommandFacade.DefinitionExpressibleCodes.Keys.Should().BeSubsetOf(
            SecurityAssetClassCatalog.AssetClasses,
            "the definition-expressible ledger (and the validateDefinitionTerms dispatch it mirrors) may only name declared catalog classes");
    }

    [Fact]
    public void MutationTable_CoversEveryLedgeredCode()
    {
        var covered = Mutations
            .GroupBy(static row => row.AssetClass, static row => row.Code)
            .ToDictionary(static group => group.Key, static group => group.Distinct().ToHashSet(StringComparer.Ordinal));

        foreach (var (assetClass, codes) in SecurityMasterCommandFacade.DefinitionExpressibleCodes)
        {
            covered.Should().ContainKey(assetClass, "every ledgered class needs mutation coverage");
            var fsharpCovered = CodesCoveredByFSharpTests.TryGetValue(assetClass, out var elsewhere) ? elsewhere : [];
            covered[assetClass].Concat(fsharpCovered).Should().BeEquivalentTo(codes,
                $"the mutation table (plus the F#-covered codes) must cover exactly the ledgered codes for {assetClass}, so the ledger and the proof stay in step");
        }

        covered.Keys.Should().BeEquivalentTo(
            SecurityMasterCommandFacade.DefinitionExpressibleCodes.Keys,
            "a mutation row for an unledgered class means the ledger is stale");
    }

    private static JsonElement FlatTwin(string assetClass)
        => SecurityAssetTermsSchemaRoundTripTests.SerializeThroughDomain(
            assetClass,
            SecurityAssetTermsSchemaRoundTripTests.FullPayloads[assetClass]);

    private static (SecurityKind Kind, SecurityTermModules Modules) KindAndModules(string assetClass, JsonElement flatTerms)
    {
        var record = SecurityMasterMapping.ToRecord(
            SecurityAssetTermsSchemaRoundTripTests.LegacyProjection(assetClass, flatTerms));
        var definition = SecurityMasterLegacyUpgrade.toEconomicDefinition(record);
        return (record.Kind, definition.Terms);
    }
}
