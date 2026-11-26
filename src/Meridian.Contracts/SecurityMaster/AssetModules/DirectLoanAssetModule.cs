using Meridian.Contracts.Domain.Enums;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleSchema;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleRates;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleCodec;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleEconomics;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>Compile-time contract contributions for DirectLoan.</summary>
internal static class DirectLoanAssetModule
{
    private const string AssetClass = SecurityAssetClassNames.DirectLoan;

    internal static SecurityAssetClassModule Create()
        => new(
            Descriptor: new(
                AssetClass: AssetClass,
                AssetPackIds: ["private-loan-credit", "mortgage-facility-intercompany"],
                InstrumentTypes: [InstrumentType.DirectLoan],
                LotUnit: SecurityLotUnitKind.FaceValue,
                CashFlowBehavior: SecurityCashFlowBehavior.ContractualSchedule,
                ProjectionKind: SecurityTermsProjectionKind.Declarative,
                SupportsBasicCreateWorkflow: false,
                PreferredIdentifierKinds:
                [
                    SecurityIdentifierKind.InternalCode,
                    SecurityIdentifierKind.LoanX,
                    SecurityIdentifierKind.Clip,
                    SecurityIdentifierKind.PermId,
                    SecurityIdentifierKind.Lei,
                    SecurityIdentifierKind.ProviderSymbol
                ],
                AssetOperationsCapabilities: AssetOperationsCapabilitySet.DirectLending,
                AmortizesTowardPar: true,
                Aliases: ["Loan", "AmortizingLoan", "BankLoan"],
                TermsUpgrades: [SecurityAssetClassTermsUpgrade.Identity(fromVersion: 1, rationale: "Version 2 adds the optional calculation-only cashFlowContract companion without changing earlier terms (ADR-024/025, 2026-10-29)."),
                    SecurityAssetClassTermsUpgrade.Identity(fromVersion: 2, rationale: "Version 3 adds the optional governed scalar rateUnit declaration without changing earlier terms.")
                ],
                TermsVersion: 3)
            {
                CashFlowTermRoles =
                [
                    new(SecurityCashFlowTermRole.Maturity, "maturity"),
                    new(SecurityCashFlowTermRole.CouponRate, "currentCouponRate")
                ],
                StampFloor = 1,
                AccountingOutsideSlice = true
            },
            Fields: [
                OptOneOf("rateUnit", "Fraction", "Percent", "BasisPoints").WithVerbatimUndeclaredValue().Since(3),
                Req("borrower", SecurityAssetTermFieldType.String),
                Opt("maturity", SecurityAssetTermFieldType.Date),
                Opt("referenceIndex", SecurityAssetTermFieldType.String).LinksUnderlying(SecurityUnderlyingRole.Index),
                Opt("spreadBps", SecurityAssetTermFieldType.Decimal),
                Opt("currentCouponRate", SecurityAssetTermFieldType.Decimal),
                Opt("resetFrequency", SecurityAssetTermFieldType.String).WithConvention(SecurityTermConventionKind.PaymentFrequency),
                Opt("pricingSource", SecurityAssetTermFieldType.String).AsOperational(),
                Req("covenants", SecurityAssetTermFieldType.Array),
                Req("principalSchedule", SecurityAssetTermFieldType.Array),
                Opt("cashFlowContract", SecurityAssetTermFieldType.Object).Since(2).ReadOnly()
            ],
            SchemaOrder: 15,
            ElementShapes:
            [
                new("principalSchedule", 4, [
                    Req("paymentDate", SecurityAssetTermFieldType.Date),
                    Req("amount", SecurityAssetTermFieldType.Decimal)
                ]),
                new("covenants", 6, [
                    Req("covenantType", SecurityAssetTermFieldType.String),
                    Req("threshold", SecurityAssetTermFieldType.String),
                    Opt("notes", SecurityAssetTermFieldType.String)
                ])
            ])
        {
            RateFields =
            [
                // Direct loans and structured credit.
                Bps(AssetClass, "spreadBps", "Loan spread over the reference index", allowsNegative: true),
                Coupon(AssetClass, "currentCouponRate", "Current all-in loan coupon")
            ],
            CouponRateFieldKey = "currentCouponRate",
            CodecBinding = new(AssetClass, false,
            [
                B("Dates.MaturityDate", "maturity", SecurityTermRead.OptionalDate),
                B("IssuerTerms.IssuerName", "borrower", SecurityTermRead.RequiredString, via: "Borrower"),
                Listed("LoanCovenants.Entries", "covenants", "ToCovenants(GetRequiredArray(json, \"covenants\").EnumerateArray())",
                    "Covenants", CovenantItems),
                B("FloatingRate.ReferenceIndex", "referenceIndex", SecurityTermRead.OptionalString),
                B("FloatingRate.SpreadBps", "spreadBps", SecurityTermRead.OptionalDecimal),
                B("FloatingRate.ResetFrequency", "resetFrequency", SecurityTermRead.OptionalString),
                B("CouponTerms.CouponRate", "currentCouponRate", SecurityTermRead.OptionalDecimal),
                Listed("Principal.Payments", "principalSchedule", "PrincipalPayments(economicSchedules)", "PrincipalSchedule",
                    PrincipalPaymentItems),
                B("ReferenceDataSource.PricingSource", "pricingSource", SecurityTermRead.OptionalString),
            ], Inputs: SecurityCodecInputs.EconomicSchedules),
            CodecOrder = 14,
            EconomicFlattenings = [
                Map("borrower", ("issuer", "issuerName")),
                Map("maturity", MaturityDate),
                Map("currentCouponRate", ("coupon", "couponRate")),
                Map("referenceIndex", ("floatingRate", "referenceIndex")),
                Map("spreadBps", ("floatingRate", "spreadBps")),
                Map("resetFrequency", ("floatingRate", "resetFrequency")),
                Map("pricingSource", ("referenceDataSource", "pricingSource"))
            ],
            EconomicFlatteningOrder = 17
        };
}
