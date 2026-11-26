namespace Meridian.Contracts.SecurityMaster;

/// <summary>The economic role a term plays in a calculated cash-flow projection.</summary>
public enum SecurityCashFlowTermRole
{
    Maturity,
    Issue,
    PrincipalFace,
    CurrentFactor,
    CouponRate,
    PaymentFrequency,
    DayCount,
    InflationIndex,
    InflationBaseIndexValue,
    InflationIndexRatio,
}

/// <summary>
/// One of a class's declared term keys and the cash-flow role it plays
/// (<see cref="SecurityAssetClassDescriptor.CashFlowTermRoles"/>).
/// </summary>
/// <param name="Role">The role the key plays.</param>
/// <param name="Key">The class's schema key (its declared aliases are read with it).</param>
/// <param name="ReadAfterSharedSpellings">
/// Whether the key is read after the shared spellings rather than before them: a key that only
/// sometimes carries the role (StructuredCredit's <c>couponOrIndex</c>, which is a rate only when it
/// is a number) yields to a shared spelling present in the same document.
/// </param>
public sealed record SecurityCashFlowTermBinding(
    SecurityCashFlowTermRole Role,
    string Key,
    bool ReadAfterSharedSpellings = false);

/// <summary>
/// The spellings a calculated cash-flow projection reads for each term role, per class (review D9).
/// A class declares which of its schema keys play which role on its catalog descriptor; this merges
/// those keys with the shared spellings every class reads.
/// <para>
/// The shared spellings are the resolver's vendor vocabulary: they are read for every class because
/// governed profile fields, common terms and documents written before a class's schema existed carry
/// them. They are not a substitute for a class's declaration. A class key that a shared spelling
/// names keeps its shared priority; any other declared key is read before the shared spellings
/// (or after them, when the binding says so). <c>SecurityCashFlowTermRoleTests</c> holds every
/// calculated class to the schema: each declared key is a schema key, each schema key a shared
/// spelling names is declared with that role, and each calculated class declares a maturity and a
/// coupon (or that it pays none).
/// </para>
/// </summary>
public static class SecurityCashFlowTermRoles
{
    /// <summary>The shared spellings for each role, in priority order: the first key that resolves wins.</summary>
    public static IReadOnlyDictionary<SecurityCashFlowTermRole, IReadOnlyList<string>> SharedSpellings { get; } =
        new Dictionary<SecurityCashFlowTermRole, IReadOnlyList<string>>
        {
            [SecurityCashFlowTermRole.Maturity] = ["maturityDate", "maturity", "legalFinalMaturity"],
            [SecurityCashFlowTermRole.Issue] = ["issueDate", "originationDate", "effectiveDate"],
            [SecurityCashFlowTermRole.PrincipalFace] = ["par", "originalFace", "notional", "principal", "principalAmount"],
            [SecurityCashFlowTermRole.CurrentFactor] = ["currentFactor", "factor"],
            [SecurityCashFlowTermRole.CouponRate] = ["fixedCouponRate", "couponRate", "coupon", "annualRate"],
            [SecurityCashFlowTermRole.PaymentFrequency] = ["paymentFrequency", "paymentFrequencyPerYear", "couponFrequency"],
            [SecurityCashFlowTermRole.DayCount] = ["dayCountConvention", "dayCount", "dayCountBasis"],
            // Inflation-linked indexation the Bond codec writes for couponType = "InflationLinked".
            [SecurityCashFlowTermRole.InflationIndex] = ["inflationIndex"],
            [SecurityCashFlowTermRole.InflationBaseIndexValue] = ["inflationBaseIndexValue", "baseIndexValue"],
            [SecurityCashFlowTermRole.InflationIndexRatio] = ["inflationIndexRatio", "indexRatio"],
        };

    /// <summary>
    /// The spellings read for <paramref name="role"/> on a record of <paramref name="assetClass"/>:
    /// the class's declared keys (with their schema aliases) not already shared, then the shared
    /// spellings, then the declared keys bound to read after them.
    /// </summary>
    public static string[] Spellings(string? assetClass, SecurityCashFlowTermRole role)
    {
        var shared = SharedSpellings[role];
        var descriptor = SecurityAssetClassCatalog.GetOrDefault(assetClass);
        List<string> before = [];
        List<string> after = [];
        foreach (var binding in descriptor.CashFlowTermRoles)
        {
            if (binding.Role != role)
            {
                continue;
            }

            var target = binding.ReadAfterSharedSpellings ? after : before;
            foreach (var spelling in DeclaredSpellings(descriptor.AssetClass, binding.Key))
            {
                if (!shared.Contains(spelling, StringComparer.OrdinalIgnoreCase)
                    && !target.Contains(spelling, StringComparer.OrdinalIgnoreCase))
                {
                    target.Add(spelling);
                }
            }
        }

        return [.. before, .. shared, .. after];
    }

    private static IEnumerable<string> DeclaredSpellings(string assetClass, string key)
        => SecurityAssetTermsSchema.Field(assetClass, key) is { } field ? [field.Key, .. field.Aliases] : [key];
}
