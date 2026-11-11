using System.Reflection;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.FSharp.Domain;
using Meridian.FSharp.SecurityMasterInterop;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;
using Microsoft.FSharp.Reflection;
using Xunit;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Pins <c>SecurityKindPersistence</c> — the per-class list of the term-module members a flat row
/// carries — against the flat codec itself. A per-class record holds whole shared term modules
/// (ADR-027 stage 3), so its type no longer limits it to the members its class persists; the
/// list is what refuses the rest. Rather than restate that list here, the suite derives the
/// truth from the real writer (<see cref="SecurityMasterSnapshotWrapper"/>) and reader
/// (<see cref="SecurityMasterMapping.ToRecord"/>): for every asset class it walks every member of
/// every module the class's record embeds, gives it a new value on the fully populated twin,
/// round-trips the record, and requires the list to refuse the member exactly when the round
/// trip did not carry the value. A member added to a module, or a flat key added to or removed
/// from a class, therefore fails here until the list is brought into step.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SecurityKindPersistenceTests
{
    private const string NotPersisted = "term_member_not_persisted";
    private const string ValueNotPersisted = "term_member_value_not_persisted";

    public static TheoryData<string> DeclaredAssetClasses() => SecurityAssetTermsSchemaRoundTripTests.DeclaredAssetClasses();

    /// <summary>
    /// Members whose new value the round trip does not return and which a rule of their own
    /// refuses, so the persistence list rightly says nothing about them.
    /// </summary>
    private static readonly IReadOnlyDictionary<(string AssetClass, string Member), string> RefusedByTheirOwnRule =
        new Dictionary<(string, string), string>
        {
            // The option row stores the style; the decode reads only the three governed spellings.
            [("Option", "Exercise.ExerciseStyle")] = "option_exercise_style_invalid",
        };

    /// <summary>
    /// Members the round trip returns under another case of the same union with the same label:
    /// the flat row stores the label, and the decode reads every label back as the open case.
    /// The value is carried, so the list must not refuse it.
    /// </summary>
    private static readonly IReadOnlySet<(string AssetClass, string Member)> CarriedAsTheSameLabel =
        new HashSet<(string, string)>
        {
            ("CashSweep", "SweepTerms.SweepFrequency"),
            ("StructuredCredit", "CouponTerms.CouponType"),
        };

    /// <summary>
    /// What the walk cannot give a new value, per class. The declared-schedule containers and the
    /// rate-unit declaration are read before they are written (ADR-024), so the decode sets them
    /// from a stored row and the list must not refuse them; they are the only members left out,
    /// and the set is asserted so a new gap cannot open unnoticed.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string[]> NotWalked = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["Bond"] = ["DeclaredSchedule.Conventions", "DeclaredSchedule.Exercises"],
        ["CertificateOfDeposit"] = ["DeclaredSchedule.Conventions", "DeclaredSchedule.Exercises", "RateConvention"],
        ["StructuredCredit"] = ["DeclaredSchedule.Conventions", "DeclaredSchedule.Exercises", "RateConvention"],
        ["Deposit"] = ["RateConvention"],
        ["CommercialPaper"] = ["RateConvention"],
        ["TreasuryBill"] = ["RateConvention"],
        ["Repo"] = ["RateConvention"],
        ["CashSweep"] = ["RateConvention"],
        ["Swap"] = ["RateConvention"],
        ["DirectLoan"] = ["RateConvention"],
        ["Swaption"] = ["RateConvention"],
        ["CapFloor"] = ["RateConvention"],
        ["StructuredNote"] = ["RateConvention"],
    };

    [Theory]
    [MemberData(nameof(DeclaredAssetClasses))]
    public void ARecordTheFlatDecodeBuilds_HoldsNothingTheListRefuses(string assetClass)
    {
        var kind = DecodedTwin(assetClass).Kind;

        SecurityKindPersistence.unpersistedMembers(kind).ToArray().Should().BeEmpty(
            "the flat decode sets only members the class's row carries");
        SecurityKindPersistence.derivedMemberConflicts(kind).ToArray().Should().BeEmpty(
            "the flat decode gives a derived member the value the class derives");
        SecurityMasterCommandFacade.ValidateKindInvariants(kind).Select(static e => e.Code)
            .Should().NotContain([NotPersisted, ValueNotPersisted]);
    }

    [Theory]
    [MemberData(nameof(DeclaredAssetClasses))]
    public void EveryEmbeddedModuleMember_IsRefusedExactlyWhenTheFlatRoundTripDoesNotCarryIt(string assetClass)
    {
        var record = DecodedTwin(assetClass);
        var (unionCase, terms) = Decompose(record.Kind);
        var disagreements = new List<string>();
        var notWalked = new List<string>();
        var walked = 0;

        // CustomAsset's terms are the profile-envelope module itself: there is no embedded module
        // to hold a member the class lacks.
        var termFields = assetClass == "CustomAsset" ? [] : FSharpType.GetRecordFields(terms.GetType(), null);
        var termValues = assetClass == "CustomAsset" ? [] : FSharpValue.GetRecordFields(terms, null);
        for (var f = 0; f < termFields.Length; f++)
        {
            var fieldType = termFields[f].PropertyType;
            var isOptional = IsOption(fieldType);
            var moduleType = isOptional ? fieldType.GetGenericArguments()[0] : fieldType;
            FSharpType.IsRecord(moduleType, null).Should().BeTrue(
                $"{assetClass}.{termFields[f].Name} is expected to be a term module");
            if (isOptional && termValues[f] is null)
            {
                notWalked.Add(termFields[f].Name);
                continue;
            }

            var module = isOptional ? fieldType.GetProperty("Value")!.GetValue(termValues[f])! : termValues[f];
            var members = FSharpType.GetRecordFields(moduleType, null);
            var memberValues = FSharpValue.GetRecordFields(module, null);
            for (var m = 0; m < members.Length; m++)
            {
                var path = $"{termFields[f].Name}.{members[m].Name}";
                var sample = NewValueFor(members[m].PropertyType, memberValues[m]);
                if (sample is null)
                {
                    notWalked.Add(path);
                    continue;
                }

                walked++;
                var changedMembers = (object[])memberValues.Clone();
                changedMembers[m] = sample.Value;
                var changedModule = FSharpValue.MakeRecord(moduleType, changedMembers, null);
                var changedTerms = (object[])termValues.Clone();
                changedTerms[f] = isOptional ? fieldType.GetMethod("Some")!.Invoke(null, [changedModule])! : changedModule;
                var changedKind = (SecurityKind)FSharpValue.MakeUnion(
                    unionCase, [FSharpValue.MakeRecord(terms.GetType(), changedTerms, null)], null);

                var refused = SecurityKindPersistence.unpersistedMembers(changedKind).Contains(path)
                    || SecurityKindPersistence.derivedMemberConflicts(changedKind).Contains(path);
                var returned = RoundTrip(record, changedKind, f, isOptional, fieldType, m);
                var carried = Equals(returned, sample.Value);

                if (RefusedByTheirOwnRule.TryGetValue((assetClass, path), out var ownCode))
                {
                    carried.Should().BeFalse($"{assetClass} {path} is listed as refused by its own rule because the round trip drops it");
                    refused.Should().BeFalse($"{assetClass} {path} is persisted; its own rule refuses the value");
                    SecurityMasterCommandFacade.ValidateKindInvariants(changedKind).Select(static e => e.Code)
                        .Should().Contain(ownCode);
                    continue;
                }

                if (CarriedAsTheSameLabel.Contains((assetClass, path)))
                {
                    carried.Should().BeFalse($"{assetClass} {path} is listed as returned under another case");
                    Label(returned).Should().Be(Label(sample.Value), $"{assetClass} {path} must come back with the label it was given");
                    refused.Should().BeFalse($"{assetClass} {path} is carried, under its label");
                    continue;
                }

                if (refused == carried)
                {
                    disagreements.Add(carried
                        ? $"{path}: the round trip carries the value, but the list refuses it"
                        : $"{path}: the round trip returns {Show(returned)} for {Show(sample.Value)}, but the list accepts it");
                }
            }
        }

        disagreements.Should().BeEmpty(
            $"SecurityKindPersistence must refuse a {assetClass} member exactly when the flat row does not carry its value");
        notWalked.Should().BeEquivalentTo(
            NotWalked.TryGetValue(assetClass, out var expected) ? expected : [],
            $"every other embedded member of {assetClass} must be given a new value and round-tripped");
        if (assetClass != "CustomAsset")
        {
            walked.Should().BeGreaterThan(0);
        }
    }

    [Fact]
    public void AWarrantWithAnExerciseStyleAndAPremium_IsRefused_NamingTheMembers()
    {
        var warrant = (SecurityKind.Warrant)DecodedTwin("Warrant").Kind;
        var exercise = warrant.Item.Exercise;
        var kind = SecurityKind.NewWarrant(new WarrantTerms(
            warrant.Item.Dates,
            new ExerciseTerms(
                exercise.OptionRight,
                exercise.StrikePrice,
                exercise.StrikeRate,
                FSharpOption<string>.Some("American"),
                exercise.UnderlyingSecurityId,
                FSharpOption<decimal>.Some(1.5m)),
            warrant.Item.TradingParameters));

        var errors = SecurityMasterCommandFacade.ValidateKindInvariants(kind).ToArray();

        errors.Should().ContainSingle().Which.Code.Should().Be(NotPersisted);
        errors[0].Message.Should().Be(
            "Warrant does not persist Exercise.ExerciseStyle, Exercise.PremiumAmount; a value there would be dropped by the next write of the record.");
    }

    [Fact]
    public void ATreasuryBillNamingAnotherIssuer_IsRefused_BecauseTheRowDerivesTheIssuer()
    {
        var bill = (SecurityKind.TreasuryBill)DecodedTwin("TreasuryBill").Kind;
        var issuer = bill.Item.IssuerTerms;
        var kind = SecurityKind.NewTreasuryBill(new TreasuryBillTerms(
            bill.Item.Dates,
            bill.Item.Auction,
            new IssuerRef(
                FSharpOption<string>.Some("Bundesrepublik Deutschland"),
                issuer.InstitutionName,
                issuer.IssuerProgram,
                issuer.LeiCode,
                issuer.UltimateParentName,
                issuer.IssuerSector,
                issuer.IssuerCountry),
            bill.Item.DiscountTerms,
            bill.Item.RateConvention));

        var errors = SecurityMasterCommandFacade.ValidateKindInvariants(kind).ToArray();

        errors.Should().ContainSingle().Which.Code.Should().Be(ValueNotPersisted);
        errors[0].Message.Should().Be(
            "TreasuryBill derives IssuerTerms.IssuerName from its other terms; a different value would be replaced by the next read of the record.");
    }

    [Fact]
    public void ADerivedMemberLeftAbsent_IsAccepted_BecauseTheDecodeFillsIt()
    {
        var bill = (SecurityKind.TreasuryBill)DecodedTwin("TreasuryBill").Kind;
        var issuer = bill.Item.IssuerTerms;
        var kind = SecurityKind.NewTreasuryBill(new TreasuryBillTerms(
            bill.Item.Dates,
            bill.Item.Auction,
            new IssuerRef(null, null, issuer.IssuerProgram, null, null, null, null),
            bill.Item.DiscountTerms,
            bill.Item.RateConvention));

        SecurityMasterCommandFacade.ValidateKindInvariants(kind).Should().BeEmpty();
    }

    private static SecurityMasterRecord DecodedTwin(string assetClass)
        => SecurityMasterMapping.ToRecord(SecurityAssetTermsSchemaRoundTripTests.LegacyProjection(
            assetClass,
            SecurityAssetTermsSchemaRoundTripTests.SerializeThroughDomain(
                assetClass, SecurityAssetTermsSchemaRoundTripTests.FullPayloads[assetClass])));

    /// <summary>The member's value after the record is written to a flat row and read back.</summary>
    private static object? RoundTrip(
        SecurityMasterRecord record, SecurityKind kind, int field, bool isOptional, Type fieldType, int member)
    {
        var changed = new SecurityMasterRecord(
            record.SecurityId, record.Status, record.Common, record.Identifiers, kind, record.Version,
            record.EffectiveFrom, record.EffectiveTo, record.Provenance);
        var back = SecurityMasterMapping.ToRecord(SecurityMasterMapping.ToProjection(new SecurityMasterSnapshotWrapper(changed)));
        var module = FSharpValue.GetRecordFields(Decompose(back.Kind).Terms, null)[field];
        if (isOptional)
        {
            if (module is null)
            {
                return null;
            }

            module = fieldType.GetProperty("Value")!.GetValue(module)!;
        }

        return FSharpValue.GetRecordFields(module, null)[member];
    }

    private static (UnionCaseInfo Case, object Terms) Decompose(SecurityKind kind)
    {
        var fields = FSharpValue.GetUnionFields(kind, typeof(SecurityKind), null);
        return (fields.Item1, fields.Item2[0]);
    }

    private static bool IsOption(Type type)
        => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(FSharpOption<>);

    private static bool IsList(Type type)
        => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(FSharpList<>);

    /// <summary>A value of the member's type that differs from the one it holds, when one can be built.</summary>
    private static FSharpOption<object>? NewValueFor(Type memberType, object? current)
    {
        foreach (var variant in new[] { 0, 1 })
        {
            var candidate = Build(memberType, variant, member: true);
            if (candidate is not null && !Equals(candidate, current))
            {
                return FSharpOption<object>.Some(candidate);
            }
        }

        return null;
    }

    private static object? Build(Type type, int variant, bool member = false)
    {
        if (type == typeof(string))
            return variant == 0 ? "ZZSAMPLE" : "YYSAMPLE";
        // Fractions, so a sampled factor or rate stays inside any range the decode enforces.
        if (type == typeof(decimal))
            return variant == 0 ? 0.25m : 0.5m;
        if (type == typeof(int))
            return variant == 0 ? 7 : 8;
        if (type == typeof(bool))
            return variant == 0;
        if (type == typeof(DateOnly))
            return variant == 0 ? new DateOnly(2031, 3, 4) : new DateOnly(2032, 5, 6);
        if (type == typeof(Guid))
            return Guid.Parse(variant == 0 ? "11111111-2222-3333-4444-555555555555" : "21111111-2222-3333-4444-555555555555");
        if (type.IsEnum)
        {
            var values = Enum.GetValues(type).Cast<object>().ToArray();
            return values[^(1 + (variant % values.Length))];
        }

        if (IsOption(type))
        {
            // Only the member itself is given a value; options nested in a sampled entry stay None.
            if (!member)
                return null;
            var inner = Build(type.GetGenericArguments()[0], variant);
            return inner is null ? null : type.GetMethod("Some")!.Invoke(null, [inner]);
        }

        if (IsList(type))
        {
            var empty = type.GetProperty("Empty")!.GetValue(null)!;
            if (!member)
                return empty;
            var element = Build(type.GetGenericArguments()[0], variant);
            return element is null ? null : type.GetMethod("Cons")!.Invoke(null, [element, empty]);
        }

        if (FSharpType.IsRecord(type, null))
        {
            var fields = FSharpType.GetRecordFields(type, null);
            var values = new object?[fields.Length];
            for (var i = 0; i < fields.Length; i++)
            {
                values[i] = IsOption(fields[i].PropertyType) ? null : Build(fields[i].PropertyType, variant);
                if (values[i] is null && !IsOption(fields[i].PropertyType))
                    return null;
            }

            return FSharpValue.MakeRecord(type, values!, null);
        }

        if (FSharpType.IsUnion(type, null))
        {
            var cases = FSharpType.GetUnionCases(type, null);
            var nullary = cases.Where(static c => c.GetFields().Length == 0).ToArray();
            if (nullary.Length > 0)
                return FSharpValue.MakeUnion(nullary[variant % nullary.Length], [], null);
            var arguments = cases[0].GetFields()
                .Select(field => IsOption(field.PropertyType) ? null : Build(field.PropertyType, variant))
                .ToArray();
            return FSharpValue.MakeUnion(cases[0], arguments!, null);
        }

        return null;
    }

    /// <summary>The label a union value serializes as: the case name, or the text an open case carries.</summary>
    private static string Label(object? value)
    {
        if (value is null)
            return "<none>";
        var type = value.GetType();
        if (IsOption(type))
            return Label(type.GetProperty("Value")!.GetValue(value));
        if (!FSharpType.IsUnion(type, null))
            return value.ToString()!;
        var fields = FSharpValue.GetUnionFields(value, type, null);
        return fields.Item2.Length == 1 && fields.Item2[0] is string text ? text : fields.Item1.Name;
    }

    private static string Show(object? value)
    {
        var text = (value?.ToString() ?? "nothing").ReplaceLineEndings(" ");
        return text.Length > 80 ? text[..80] + "…" : text;
    }
}
