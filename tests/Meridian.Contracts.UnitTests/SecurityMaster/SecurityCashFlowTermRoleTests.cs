using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Holds the cash-flow term reads to the terms schema (review D9). Before, the resolver kept a private
/// alias table that nothing tied to the schema, so a class whose coupon key was missing from it
/// (DirectLoan's <c>currentCouponRate</c>) projected no interest without any refusal. Each class now
/// declares which of its schema keys play which role, and these tests require the declarations to be
/// complete and the swap-leg reads to cover every spelling the schema declares.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SecurityCashFlowTermRoleTests
{
    private static readonly SecurityCashFlowBehavior[] Calculated =
    [
        SecurityCashFlowBehavior.ContractualSchedule,
        SecurityCashFlowBehavior.FactorAmortizing,
        SecurityCashFlowBehavior.FinancingInterest,
        SecurityCashFlowBehavior.SwapLegs,
    ];

    private static readonly SecurityCashFlowBehavior[] CouponBearing =
    [
        SecurityCashFlowBehavior.ContractualSchedule,
        SecurityCashFlowBehavior.FactorAmortizing,
        SecurityCashFlowBehavior.FinancingInterest,
    ];

    private static IEnumerable<SecurityAssetClassDescriptor> Descriptors
        => SecurityAssetClassCatalog.All.Where(static descriptor => descriptor.AssetClass != "Unknown");

    [Fact]
    public void EveryDeclaredRoleKey_IsASchemaKeyOfItsClass_AndOnlyCalculatedClassesDeclareRoles()
    {
        foreach (var descriptor in Descriptors)
        {
            if (!Calculated.Contains(descriptor.CashFlowBehavior))
            {
                descriptor.CashFlowTermRoles.Should().BeEmpty($"{descriptor.AssetClass} calculates no cash flows");
                descriptor.PaysNoContractualCoupon.Should().BeFalse($"{descriptor.AssetClass} calculates no cash flows");
                continue;
            }

            foreach (var binding in descriptor.CashFlowTermRoles)
            {
                SecurityAssetTermsSchema.Field(descriptor.AssetClass, binding.Key).Should().NotBeNull(
                    $"{descriptor.AssetClass} binds {binding.Role} to '{binding.Key}', which its schema must declare");
            }

            descriptor.CashFlowTermRoles.Select(static binding => (binding.Role, binding.Key)).Should().OnlyHaveUniqueItems();
        }
    }

    [Fact]
    public void EveryCalculatedClass_DeclaresItsMaturity_AndItsCouponOrThatItPaysNone()
    {
        foreach (var descriptor in Descriptors.Where(static descriptor => Calculated.Contains(descriptor.CashFlowBehavior)))
        {
            descriptor.CashFlowTermRoles.Should().Contain(static binding => binding.Role == SecurityCashFlowTermRole.Maturity,
                $"{descriptor.AssetClass} calculates cash flows to a maturity");
            if (!CouponBearing.Contains(descriptor.CashFlowBehavior))
            {
                continue;
            }

            var declaresCoupon = descriptor.CashFlowTermRoles.Any(static binding => binding.Role == SecurityCashFlowTermRole.CouponRate);
            (declaresCoupon ^ descriptor.PaysNoContractualCoupon).Should().BeTrue(
                $"{descriptor.AssetClass} must declare exactly one of: the key that carries its coupon, or that it pays none");
        }
    }

    [Fact]
    public void EverySchemaKeyASharedSpellingNames_IsDeclaredWithThatRole()
    {
        // The lock. A calculated class's schema key that the shared vocabulary reads for a role must
        // be declared with that role, so the class's declarations are its complete reading list and a
        // key the shared list does not name has nowhere to hide.
        foreach (var descriptor in Descriptors.Where(static descriptor => Calculated.Contains(descriptor.CashFlowBehavior)))
        {
            foreach (var field in SecurityAssetTermsSchema.Fields(descriptor.AssetClass))
            {
                foreach (var (role, shared) in SecurityCashFlowTermRoles.SharedSpellings)
                {
                    if (!field.Aliases.Prepend(field.Key).Any(spelling => shared.Contains(spelling, StringComparer.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    descriptor.CashFlowTermRoles.Should().Contain(new SecurityCashFlowTermBinding(role, field.Key),
                        $"{descriptor.AssetClass}.{field.Key} is read as the {role} by the shared spellings, so the class must declare it");
                }
            }
        }
    }

    [Fact]
    public void EveryDeclaredKey_AndItsAliases_AreRead()
    {
        foreach (var descriptor in Descriptors)
        {
            foreach (var binding in descriptor.CashFlowTermRoles)
            {
                var field = SecurityAssetTermsSchema.Field(descriptor.AssetClass, binding.Key)!;
                SecurityCashFlowTermRoles.Spellings(descriptor.AssetClass, binding.Role)
                    .Should().Contain(field.Aliases.Prepend(field.Key));
            }
        }
    }

    [Theory]
    [InlineData("Deposit", SecurityCashFlowTermRole.CouponRate, new[] { "interestRate", "fixedCouponRate", "couponRate", "coupon", "annualRate" })]
    [InlineData("Repo", SecurityCashFlowTermRole.CouponRate, new[] { "repoRate", "fixedCouponRate", "couponRate", "coupon", "annualRate" })]
    [InlineData("DirectLoan", SecurityCashFlowTermRole.CouponRate, new[] { "currentCouponRate", "fixedCouponRate", "couponRate", "coupon", "annualRate" })]
    [InlineData("StructuredCredit", SecurityCashFlowTermRole.CouponRate, new[] { "fixedCouponRate", "couponRate", "coupon", "annualRate", "couponOrIndex" })]
    [InlineData("Bond", SecurityCashFlowTermRole.CouponRate, new[] { "fixedCouponRate", "couponRate", "coupon", "annualRate" })]
    [InlineData("CertificateOfDeposit", SecurityCashFlowTermRole.CouponRate, new[] { "fixedCouponRate", "couponRate", "coupon", "annualRate" })]
    [InlineData("Repo", SecurityCashFlowTermRole.Maturity, new[] { "endDate", "maturityDate", "maturity", "legalFinalMaturity" })]
    [InlineData("Repo", SecurityCashFlowTermRole.Issue, new[] { "startDate", "issueDate", "originationDate", "effectiveDate" })]
    [InlineData("Bond", SecurityCashFlowTermRole.Maturity, new[] { "maturityDate", "maturity", "legalFinalMaturity" })]
    [InlineData("Swap", SecurityCashFlowTermRole.Issue, new[] { "issueDate", "originationDate", "effectiveDate" })]
    [InlineData("Equity", SecurityCashFlowTermRole.CouponRate, new[] { "fixedCouponRate", "couponRate", "coupon", "annualRate" })]
    [InlineData("NotACatalogClass", SecurityCashFlowTermRole.Maturity, new[] { "maturityDate", "maturity", "legalFinalMaturity" })]
    public void Spellings_MatchTheReadOrderTheResolverUsedBeforeRolesWereDeclared(
        string assetClass, SecurityCashFlowTermRole role, string[] expected)
        => SecurityCashFlowTermRoles.Spellings(assetClass, role).Should().Equal(expected);

    [Theory]
    [InlineData("TreasuryBill")]
    [InlineData("CommercialPaper")]
    public void DiscountInstruments_ProjectNoCoupon_EvenWhenACouponSpellingIsPresent(string assetClass)
    {
        var terms = StructuredCashFlowTermsResolver.Resolve(Security(assetClass, new { maturity = "2027-01-01", couponRate = 5m }));

        terms.CouponRate.Should().Be(0m, "a discount yield is not a contractual coupon");
    }

    // ── Swap legs ────────────────────────────────────────────────────────────────────────────────

    /// <summary>Swap-leg fields the resolver deliberately does not read, with the reason.</summary>
    private static readonly IReadOnlyDictionary<string, string> UnreadLegFields = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["legs.currency"] = "the projection is single-currency; a leg's currency is not a calculation input",
        ["legs.scheduleConventions"] = "the declared leg block is read by the Security Master decode, not by this resolver",
    };

    [Fact]
    public void EverySwapLegRead_CoversEverySpellingTheSchemaDeclaresForItsPath()
    {
        foreach (var path in StructuredCashFlowTermsResolver.LegSpellingPaths)
        {
            var field = SwapField(path);
            field.Should().NotBeNull($"the resolver reads Swap.{path}, which the schema must declare");
            StructuredCashFlowTermsResolver.LegSpellings(path).Should().Contain(field!.Aliases.Prepend(field.Key),
                $"every spelling the schema declares for Swap.{path} must be read");
        }
    }

    [Fact]
    public void EverySwapLegFieldTheSchemaDeclares_IsReadOrListedAsUnread()
    {
        string[] containers = ["legs", "legs.notionalSchedule", "legs.resetSchedule"];
        var declared = containers
            .SelectMany(static container => SecurityAssetTermsSchema.ElementFields("Swap", container)
                .Select(field => $"{container}.{field.Key}"))
            .Prepend("legs");

        declared.Should().BeSubsetOf(StructuredCashFlowTermsResolver.LegSpellingPaths.Concat(UnreadLegFields.Keys));
        UnreadLegFields.Keys.Should().NotIntersectWith(StructuredCashFlowTermsResolver.LegSpellingPaths);
    }

    [Fact]
    public void ResetEntriesSpelledWithTheSchemasDeclaredAliases_AreRead()
    {
        // effectiveDate and indexRate are the schema's aliases for resetDate and fixingRate; the
        // resolver used to skip such entries, so their fixings were lost.
        var terms = StructuredCashFlowTermsResolver.Resolve(Security("Swap", new
        {
            effectiveDate = "2026-01-15",
            maturityDate = "2031-01-15",
            legs = new object[]
            {
                new
                {
                    legType = "Floating", currency = "USD", index = "SOFR", notional = 1_000_000m, exchangesPrincipal = false,
                    resetSchedule = new[] { new { effectiveDate = "2026-04-15", indexRate = 0.043m } }
                }
            }
        }));

        terms.Legs.Should().ContainSingle().Which.ResetSchedule.Should().ContainSingle()
            .Which.Should().Be(new StructuredCashFlowResetFixing(new DateOnly(2026, 4, 15), 0.043m));
    }

    private static SecurityAssetTermField? SwapField(string path)
    {
        var split = path.LastIndexOf('.');
        return split < 0
            ? SecurityAssetTermsSchema.Field("Swap", path)
            : SecurityAssetTermsSchema.ElementFields("Swap", path[..split]).FirstOrDefault(field => field.Key == path[(split + 1)..]);
    }

    private static SecurityDetailDto Security(string assetClass, object terms)
        => new(Guid.NewGuid(), assetClass, SecurityStatusDto.Active, $"{assetClass} under test", "USD",
            JsonSerializer.SerializeToElement(new { }), JsonSerializer.SerializeToElement(terms), [], [], 1,
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), null);
}
