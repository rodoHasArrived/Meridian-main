using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>What a read of a row's declared schedule conventions found.</summary>
public enum SecurityDeclaredConventionsStatus
{
    /// <summary>
    /// The row declares nothing this node decodes: no populated block, a class without the container,
    /// a container the row's stamp or profile envelope does not decode, or a payload no reader takes.
    /// </summary>
    None,

    /// <summary>The row decodes at least one non-empty block.</summary>
    Declared,

    /// <summary>The row is stamped at an asset-class terms version this node does not read.</summary>
    Unreadable
}

/// <summary>
/// The result of <see cref="SecurityVersionedTerms.ReadDeclaredScheduleConventions"/> or
/// <see cref="SecurityVersionedTerms.ReadDeclaredLegScheduleConventions"/>.
/// </summary>
/// <param name="Status">Whether the row declares conventions, declares none, or cannot be read.</param>
/// <param name="Block">The root block when <paramref name="Status"/> is Declared on a root read; otherwise <see cref="ScheduleConventionTerms.Empty"/>.</param>
/// <param name="UnreadableReason">The read guard's reason when <paramref name="Status"/> is Unreadable.</param>
public sealed record SecurityDeclaredConventionsRead(
    SecurityDeclaredConventionsStatus Status,
    ScheduleConventionTerms Block,
    string? UnreadableReason)
{
    /// <summary>
    /// On an element read (<see cref="SecurityVersionedTerms.ReadDeclaredLegScheduleConventions"/>):
    /// the non-empty blocks of the selected container's object elements, in element order. Empty on a
    /// root read.
    /// </summary>
    public IReadOnlyList<ScheduleConventionTerms> ElementBlocks { get; init; } = [];
}

/// <summary>
/// Which versioned containers a payload decodes (ADR-024 decision 3). A versioned container
/// (<see cref="SecurityVersionedTerms.IsVersionedContainer"/>) is decoded only from a payload that is
/// not profile-governed and only when its version is at or below <paramref name="Through"/>; every other
/// field is decoded as before. Built by <see cref="SecurityVersionedTerms.Decoding"/>.
/// </summary>
/// <param name="ProfileGoverned">Whether the payload is a profile envelope (<see cref="SecurityTermsEnvelope.IsProfileGovernedPayload"/>).</param>
/// <param name="Through">The highest asset-class terms version whose versioned containers the payload decodes.</param>
public readonly record struct SecurityVersionedTermDecoding(bool ProfileGoverned, int Through)
{
    /// <summary>Whether <paramref name="field"/> is decoded from the payload this decoding describes.</summary>
    public bool Decodes(SecurityAssetTermField field)
    {
        ArgumentNullException.ThrowIfNull(field);
        return !SecurityVersionedTerms.IsVersionedContainer(field)
               || (!ProfileGoverned && field.SinceTermsVersion <= Through);
    }
}

/// <summary>
/// The versioned class-term containers of the flat terms family (ADR-024, readable before writable):
/// the <c>scheduleConventions</c> block and the <c>callSchedule</c> and <c>putSchedule</c> arrays, at
/// the payload root and inside array elements (a swap leg's block). It holds their keys, the one
/// predicate deciding which of them a payload decodes, and the one reader of the block, so the mapping,
/// the schema validator and the cash-flow engine cannot disagree about what a row declares.
/// </summary>
public static class SecurityVersionedTerms
{
    /// <summary>The schedule-conventions block (ADR-025).</summary>
    public const string ScheduleConventionsKey = "scheduleConventions";

    /// <summary>The declared call schedule.</summary>
    public const string CallScheduleKey = "callSchedule";

    /// <summary>The declared put schedule.</summary>
    public const string PutScheduleKey = "putSchedule";

    /// <summary>
    /// The version of the mapping from a declared <c>scheduleConventions</c> block to a schedule
    /// specification (ADR-025). Schedule evidence and the startup capability line report it, so a node's
    /// logs and its evidence name one mapping. This is the only literal: the schedule factory's
    /// <c>DeclaredMappingVersion</c> must be declared as this constant, never as its own string, and its
    /// tests must assert that the two are equal. Any change to how a declared block maps to a schedule
    /// ships under a new value.
    /// </summary>
    public const string DeclaredScheduleMappingVersion = "security-master-declared-schedule-v1";

    private static readonly SecurityDeclaredConventionsRead NotDeclared =
        new(SecurityDeclaredConventionsStatus.None, ScheduleConventionTerms.Empty, null);

    /// <summary>
    /// Whether <paramref name="field"/> is a versioned container: an object or array term introduced
    /// above asset-class terms version 1. Its members are frozen with it, and it is never walked,
    /// decoded or validated in a profile envelope (ADR-024 decision 3). Containers that predate
    /// per-class versioning (swap <c>legs</c>, a bond's <c>principalSchedule</c>) and later scalar terms
    /// are not.
    /// </summary>
    public static bool IsVersionedContainer(SecurityAssetTermField field)
    {
        ArgumentNullException.ThrowIfNull(field);
        return field.SinceTermsVersion > SecurityTermsEnvelope.DefaultAssetClassTermsVersion
               && field.Type is SecurityAssetTermFieldType.Object or SecurityAssetTermFieldType.Array;
    }

    /// <summary>
    /// The decoding of a payload of <paramref name="assetClass"/>. A read decodes the versioned
    /// containers at or below the payload's own stamp; a write decodes those at or below the class's
    /// write version. A profile envelope decodes none in either mode.
    /// </summary>
    public static SecurityVersionedTermDecoding Decoding(string assetClass, JsonElement payload, bool writeMode)
        => new(
            SecurityTermsEnvelope.IsProfileGovernedPayload(assetClass, payload),
            writeMode
                ? SecurityAssetClassCatalog.GetTermsVersion(assetClass)
                : SecurityTermsEnvelope.ResolveAssetClassTermsVersion(payload));

    /// <summary>
    /// Reads the <c>scheduleConventions</c> block under <paramref name="key"/> (exact case) of
    /// <paramref name="container"/>, tolerantly and verbatim, without any version gate: callers decide
    /// whether the container is decoded (<see cref="SecurityVersionedTermDecoding.Decodes"/>). A key
    /// whose value is not an object, and a container that is not an object, read as
    /// <see cref="ScheduleConventionTerms.Empty"/>. Members match exactly by name; strings are read only
    /// from string tokens and keep blanks; booleans only from <c>true</c> or <c>false</c>; the lag only
    /// from a number that fits an <see cref="int"/>; dates through the current culture's
    /// <see cref="DateOnly.TryParse(string?, out DateOnly)"/>; calendars from the string items of an
    /// array, dropping whitespace-only ids without trimming the rest.
    /// </summary>
    public static ScheduleConventionTerms ReadScheduleConventions(JsonElement container, string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (container.ValueKind != JsonValueKind.Object
            || !container.TryGetProperty(key, out var block)
            || block.ValueKind != JsonValueKind.Object)
        {
            return ScheduleConventionTerms.Empty;
        }

        return new ScheduleConventionTerms(
            ReadOptionalString(block, "businessDayConvention"),
            ReadOptionalBoolean(block, "accrualAdjusted"),
            ReadCalendars(block, "paymentCalendars"),
            ReadOptionalInt32(block, "paymentLagBusinessDays"),
            ReadOptionalString(block, "rollConvention"),
            ReadOptionalBoolean(block, "endOfMonthRule"),
            ReadOptionalDate(block, "firstRegularDate"),
            ReadOptionalDate(block, "lastRegularDate"),
            ReadOptionalString(block, "direction"),
            ReadOptionalString(block, "stubPolicy"),
            ReadOptionalString(block, "couponRateUnit"),
            ReadOptionalString(block, "couponCashBasis"));
    }

    /// <summary>
    /// The root <c>scheduleConventions</c> block a stored row of <paramref name="assetClass"/> declares,
    /// decided exactly as the Read-mode mapping decodes it. <see cref="SecurityDeclaredConventionsStatus.None"/>
    /// when the terms are not an object, the class declares no root block, the payload declares a
    /// format other than flat or custom profile (economic terms are read through their own family), the
    /// row does not decode the block, or the block is empty. <see cref="SecurityDeclaredConventionsStatus.Unreadable"/>
    /// when the row's stamp is one this node does not read. There is no flat-format check: a row of a
    /// class without profile-backed terms decodes its root block by its own stamp even when it carries
    /// a <c>customProfileId</c> and <c>profileFields</c>.
    /// </summary>
    public static SecurityDeclaredConventionsRead ReadDeclaredScheduleConventions(string assetClass, JsonElement storedTerms)
    {
        ArgumentNullException.ThrowIfNull(assetClass);
        if (storedTerms.ValueKind != JsonValueKind.Object
            || SecurityAssetTermsSchema.Field(assetClass, ScheduleConventionsKey) is not { } field)
        {
            return NotDeclared;
        }

        if (TryRefuseRead(assetClass, storedTerms, out var refused))
        {
            return refused;
        }

        if (!Decoding(assetClass, storedTerms, writeMode: false).Decodes(field))
        {
            return NotDeclared;
        }

        var block = ReadScheduleConventions(SecurityAssetClassTermsUpgrader.UpgradeForRead(assetClass, storedTerms), field.Key);
        return block.IsEmpty
            ? NotDeclared
            : new SecurityDeclaredConventionsRead(SecurityDeclaredConventionsStatus.Declared, block, null);
    }

    /// <summary>
    /// The <c>scheduleConventions</c> blocks a stored row of <paramref name="assetClass"/> declares inside
    /// the elements of an array term (today only swap <c>legs</c>), decided exactly as the Read-mode
    /// mapping decodes them. The elements come from the container the mapping reads: the first of the
    /// term's key and aliases, in that order and matched case-insensitively, whose value is a non-empty
    /// array. A row the mapping cannot read, because one of those keys holds something other than an
    /// array, declares nothing. The format, stamp and decode rules are those of
    /// <see cref="ReadDeclaredScheduleConventions"/>; the result is Declared with
    /// <see cref="SecurityDeclaredConventionsRead.ElementBlocks"/> when any object element carries a
    /// non-empty block.
    /// </summary>
    public static SecurityDeclaredConventionsRead ReadDeclaredLegScheduleConventions(string assetClass, JsonElement storedTerms)
    {
        ArgumentNullException.ThrowIfNull(assetClass);
        if (storedTerms.ValueKind != JsonValueKind.Object)
        {
            return NotDeclared;
        }

        var containers = new List<(SecurityAssetTermField Container, SecurityAssetTermField Block)>();
        foreach (var field in SecurityAssetTermsSchema.Fields(assetClass))
        {
            if (field.Type == SecurityAssetTermFieldType.Array
                && SecurityAssetTermsSchema.ElementFields(assetClass, field.Key)
                    .FirstOrDefault(static element => element.Key == ScheduleConventionsKey && IsVersionedContainer(element)) is { } block)
            {
                containers.Add((field, block));
            }
        }

        if (containers.Count == 0)
        {
            return NotDeclared;
        }

        if (TryRefuseRead(assetClass, storedTerms, out var refused))
        {
            return refused;
        }

        var decoding = Decoding(assetClass, storedTerms, writeMode: false);
        var terms = SecurityAssetClassTermsUpgrader.UpgradeForRead(assetClass, storedTerms);
        var blocks = new List<ScheduleConventionTerms>();
        foreach (var (container, blockField) in containers)
        {
            if (!TrySelectElements(terms, container, out var elements))
            {
                return NotDeclared;
            }

            if (elements is not { } array || !decoding.Decodes(blockField))
            {
                continue;
            }

            foreach (var element in array.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var block = ReadScheduleConventions(element, blockField.Key);
                if (!block.IsEmpty)
                {
                    blocks.Add(block);
                }
            }
        }

        return blocks.Count == 0
            ? NotDeclared
            : new SecurityDeclaredConventionsRead(SecurityDeclaredConventionsStatus.Declared, ScheduleConventionTerms.Empty, null)
            {
                ElementBlocks = blocks
            };
    }

    // The read guard's two refusals: a declared non-asset format declares nothing here (economic terms
    // are projected through their own family), and a stamp this node does not read is Unreadable.
    private static bool TryRefuseRead(string assetClass, JsonElement storedTerms, [NotNullWhen(true)] out SecurityDeclaredConventionsRead? refused)
    {
        if (SecurityTermsEnvelope.TryGetFormatReadRefusal(assetClass, storedTerms, out _))
        {
            refused = NotDeclared;
            return true;
        }

        if (SecurityTermsEnvelope.TryGetVersionReadRefusal(assetClass, storedTerms, out var reason))
        {
            refused = new SecurityDeclaredConventionsRead(SecurityDeclaredConventionsStatus.Unreadable, ScheduleConventionTerms.Empty, reason);
            return true;
        }

        refused = null;
        return false;
    }

    // The elements the mapping reads for an array term with aliases (SecurityMasterMapping.GetSwapLegItems):
    // the first key or alias, in order and matched case-insensitively, whose value is a non-empty array;
    // null when none is. False when one of them, reached in that order, is not an array: the mapping
    // refuses the row.
    private static bool TrySelectElements(JsonElement terms, SecurityAssetTermField container, out JsonElement? elements)
    {
        elements = null;
        foreach (var key in new[] { container.Key }.Concat(container.Aliases))
        {
            if (!SecurityTermReader.TryGetProperty(terms, key, out var value))
            {
                continue;
            }

            if (value.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            if (value.GetArrayLength() > 0)
            {
                elements = value;
                return true;
            }
        }

        return true;
    }

    // The block's member getters, exact by name: the rules the mapping has always read the block with.

    internal static string? ReadOptionalString(JsonElement block, string member)
        => block.TryGetProperty(member, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    internal static bool? ReadOptionalBoolean(JsonElement block, string member)
        => block.TryGetProperty(member, out var value) && (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
            ? value.GetBoolean()
            : null;

    internal static int? ReadOptionalInt32(JsonElement block, string member)
        => block.TryGetProperty(member, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : null;

    // The current culture, deliberately: it is how every stored row has been read.
    internal static DateOnly? ReadOptionalDate(JsonElement block, string member)
        => block.TryGetProperty(member, out var value) && value.ValueKind == JsonValueKind.String && DateOnly.TryParse(value.GetString(), out var date)
            ? date
            : null;

    internal static IReadOnlyList<string> ReadCalendars(JsonElement block, string member)
    {
        if (!block.TryGetProperty(member, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var calendars = new List<string>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && item.GetString() is { } calendar && !string.IsNullOrWhiteSpace(calendar))
            {
                calendars.Add(calendar);
            }
        }

        return calendars;
    }
}
