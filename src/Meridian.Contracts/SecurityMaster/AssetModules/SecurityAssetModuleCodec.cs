namespace Meridian.Contracts.SecurityMaster;

/// <summary>Binding constructors shared by the class-owned generated-codec declarations.</summary>
internal static class SecurityAssetModuleCodec
{
    internal static SecurityTermBinding B(string path, string key, SecurityTermRead read, string? via = null)
        => new(path, key, read, WriteVia: via);

    /// <summary>A member the decode fixes or derives; nothing is written for it.</summary>
    internal static SecurityTermBinding Fixed(string path, string decodeExpression)
        => new(path, null, null, decodeExpression);

    /// <summary>A second member read from a key another row writes.</summary>
    internal static SecurityTermBinding Also(string path, string key, SecurityTermRead read)
        => new(path, key, read, DecodeOnly: true);

    /// <summary>A whole module built by a helper; the keys it reads are written by <see cref="WrittenFrom"/> rows.</summary>
    internal static SecurityTermBinding Module(string field, string decodeExpression)
        => new(field, null, null, decodeExpression);

    /// <summary>A key written from a record property and read by a <see cref="Module"/> row of the same field.</summary>
    internal static SecurityTermBinding WrittenFrom(string path, string key, string property)
        => new(path, key, null, WriteVia: property, DecodeOnly: false, WriteOnly: true);

    /// <summary>A member read through its own conversion and written from a record property.</summary>
    internal static SecurityTermBinding Converted(string path, string key, string decodeExpression, string property)
        => new(path, key, null, decodeExpression, property);

    /// <summary>A member read through its own conversion and written as a converted value.</summary>
    internal static SecurityTermBinding Computed(string path, string key, string decodeExpression, string writeExpression)
        => new(path, key, null, decodeExpression, WriteExpression: writeExpression);

    /// <summary>
    /// A key computed from a member another row decodes: one of several keys a single member (a
    /// coupon structure) is written as. The decode row of <paramref name="path"/> reads it back.
    /// </summary>
    internal static SecurityTermBinding WrittenAs(string path, string key, string writeExpression)
        => new(path, key, null, WriteOnly: true, WriteExpression: writeExpression);

    /// <summary>A list member read by its own decode and written entry by entry.</summary>
    internal static SecurityTermBinding Listed(string path, string key, string decodeExpression, string property,
        params (string Key, string Member)[] items)
        => new(path, key, null, decodeExpression, property,
            Items: items.Select(static item => new SecurityListItemBinding(item.Key, item.Member)).ToArray());

    internal static SecurityClassCodecBinding Class(string assetClass, params SecurityTermBinding[] members)
        => new(assetClass, false, members);

    internal static SecurityClassCodecBinding ScheduledClass(string assetClass, params SecurityTermBinding[] members)
        => new(assetClass, false, members, VersionedSchedules: true);

    internal static SecurityClassCodecBinding ResolvedClass(string assetClass, params SecurityTermBinding[] members)
        => new(assetClass, true, members);

    internal static readonly (string, string)[] CovenantItems =
        [("covenantType", "CovenantType"), ("threshold", "Threshold"), ("notes", "Notes")];

    internal static readonly (string, string)[] PrincipalPaymentItems =
        [("paymentDate", "PaymentDate"), ("amount", "Amount")];

}
