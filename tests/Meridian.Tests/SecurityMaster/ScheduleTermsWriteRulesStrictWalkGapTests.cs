using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// The strict-walk gaps in the declared-schedule write rules: canonical and unrepeated calendar ids,
/// member names inside unset containers (R30), blank values, a periodic exerciseFrequency, and a member
/// type the walk cannot check failing closed. Like the rule gaps, each judges only an introduced or
/// changed container.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ScheduleTermsWriteRulesStrictWalkGapTests
{
    private const string Prefix = "Invalid declared schedule terms: ";
    private const string Grammar = "an uppercase ASCII letter followed by 1 to 15 uppercase ASCII letters or digits";

    [Theory]
    [InlineData("""["usny"]""", "Property 'scheduleConventions.paymentCalendars[0]' must be a canonical calendar id (" + Grammar + "), but was 'usny'.")]
    [InlineData("""["USNY"," GBLO"]""", "Property 'scheduleConventions.paymentCalendars[1]' must be a canonical calendar id (" + Grammar + "), but was ' GBLO'.")]
    [InlineData("""["USNY+GBLO"]""", "Property 'scheduleConventions.paymentCalendars[0]' must be a canonical calendar id (" + Grammar + "), but was 'USNY+GBLO'.")]
    [InlineData("""["USNY","GBLO","USNY"]""", "Property 'scheduleConventions.paymentCalendars[2]' names the calendar 'USNY' again; name each calendar once.")]
    public void DeclaredCalendars_MustBeCanonicalAndNamedOnce(string calendars, string text)
    {
        var payload = Bond(Block(calendars));

        var introduced = () => ScheduleTermsWriteRules.Validate("Bond", payload);
        var retained = () => ScheduleTermsWriteRules.Validate("Bond", payload, payload);

        introduced.Should().Throw<InvalidOperationException>().Which.Message.Should().Be(Prefix + text);
        retained.Should().NotThrow("a stored block is judged by the rules it was stored under");
    }

    [Fact]
    public void CanonicalCalendars_IncludingWeekends_AreAccepted()
    {
        var action = () => ScheduleTermsWriteRules.Validate("Bond", Bond(Block("""["USNY","GBLO","WEEKENDS"]""")));

        action.Should().NotThrow();
    }

    [Theory]
    [InlineData("""{"scheduleConventions":{"settlementLagDays":null}}""", "scheduleConventions.settlementLagDays")]
    [InlineData("""{"scheduleConventions":{"BusinessDayConvention":null}}""", "scheduleConventions.BusinessDayConvention")]
    [InlineData("""{"callSchedule":[{"callDate":null,"foo":null}]}""", "callSchedule.foo")]
    [InlineData("""{"putSchedule":[{"putDate":"2027-01-01","exerciseStyle":"European"},{"PutDate":null}]}""", "putSchedule.PutDate")]
    public void R30_MemberNamesInUnsetContainersAndEntries_AreChecked(string containers, string member)
    {
        var payload = Bond(JsonSerializer.Deserialize<JsonElement>(containers));

        var introduced = () => ScheduleTermsWriteRules.Validate("Bond", payload);
        var retained = () => ScheduleTermsWriteRules.Validate("Bond", payload, payload);

        introduced.Should().Throw<InvalidOperationException>().Which.Message.Should()
            .Be(Prefix + $"Unknown, case-variant or repeated member '{member}'.");
        retained.Should().NotThrow();
    }

    [Theory]
    [InlineData("""{"scheduleConventions":{"BusinessDayConvention":null}}""", "{}", "scheduleConventions.BusinessDayConvention")]
    [InlineData("""{"scheduleConventions":{"settlementLagDays":null}}""", """{"scheduleConventions":{"accrualAdjusted":null}}""", "scheduleConventions.settlementLagDays")]
    [InlineData("""{"putSchedule":[{"putDate":"2027-01-01","exerciseStyle":"European"},{"calDate":null}]}""",
        """{"putSchedule":[{"putDate":"2027-01-01","exerciseStyle":"European"}]}""", "putSchedule.calDate")]
    public void R30_AnUnsetContainerAddedOnAnAmendment_IsCheckedEvenThoughUnsetIsNoChange(string containers, string stored, string member)
    {
        var payload = Bond(JsonSerializer.Deserialize<JsonElement>(containers));
        var retained = Bond(JsonSerializer.Deserialize<JsonElement>(stored));

        var action = () => ScheduleTermsWriteRules.Validate("Bond", payload, retained);

        action.Should().Throw<InvalidOperationException>().Which.Message.Should()
            .Be(Prefix + $"Unknown, case-variant or repeated member '{member}'.");
    }

    [Fact]
    public void R30_ANameTheStoredContainerAlreadyHolds_IsKept()
    {
        var retained = Bond(JsonSerializer.Deserialize<JsonElement>("""{"callSchedule":[{"callDate":null,"foo":null}]}"""));
        var payload = Bond(JsonSerializer.Deserialize<JsonElement>("""{"callSchedule":[{"foo":null}],"scheduleConventions":{"accrualAdjusted":null}}"""));

        var action = () => ScheduleTermsWriteRules.Validate("Bond", payload, retained);

        action.Should().NotThrow("a stored row stays amendable; only names it does not already hold are refused");
    }

    [Theory]
    [InlineData("""{"scheduleConventions":[]}""")]
    [InlineData("""{"callSchedule":{"x":null}}""")]
    [InlineData("""{"callSchedule":[{"callDate":null,"exerciseStyle":null}]}""")]
    public void R30_AnUnsetValueOfTheWrongKindOrWithDeclaredNames_IsAccepted(string containers)
    {
        var action = () => ScheduleTermsWriteRules.Validate("Bond", Bond(JsonSerializer.Deserialize<JsonElement>(containers)));

        action.Should().NotThrow();
    }

    [Theory]
    [InlineData("""{"scheduleConventions":{"businessDayConvention":"  ","accrualAdjusted":false,"paymentCalendars":["USNY"]}}""", "scheduleConventions.businessDayConvention")]
    [InlineData("""{"putSchedule":[{"putDate":"2027-01-01","exerciseStyle":""}]}""", "putSchedule.exerciseStyle")]
    public void BlankValues_AreRefusedAsBlank(string containers, string member)
    {
        var action = () => ScheduleTermsWriteRules.Validate("Bond", Bond(JsonSerializer.Deserialize<JsonElement>(containers)));

        action.Should().Throw<InvalidOperationException>().Which.Message.Should()
            .Be(Prefix + $"Property '{member}' must not be blank; omit it or send null to leave it unset.");
    }

    [Fact]
    public void ExerciseFrequency_MustBePeriodic()
    {
        var payload = Bond(JsonSerializer.Deserialize<JsonElement>(
            """{"putSchedule":[{"putDate":"2027-01-01","exerciseStyle":"European","exerciseFrequency":"AtMaturity"}]}"""));

        var introduced = () => ScheduleTermsWriteRules.Validate("Bond", payload);
        var retained = () => ScheduleTermsWriteRules.Validate("Bond", payload, payload);

        introduced.Should().Throw<InvalidOperationException>().Which.Message.Should().Be(Prefix +
            "Value 'AtMaturity' is not a periodic exercise frequency for 'putSchedule[0].exerciseFrequency' on asset class 'Bond': " +
            "exercise dates recur a whole number of months apart. Periodic values: Monthly, BiMonthly, Quarterly, SemiAnnual, Annual.");
        retained.Should().NotThrow();
    }

    [Theory]
    [InlineData(SecurityAssetTermFieldType.String, true)]
    [InlineData(SecurityAssetTermFieldType.Boolean, true)]
    [InlineData(SecurityAssetTermFieldType.Integer, true)]
    [InlineData(SecurityAssetTermFieldType.Decimal, true)]
    [InlineData(SecurityAssetTermFieldType.Date, true)]
    [InlineData(SecurityAssetTermFieldType.Array, true)]
    [InlineData(SecurityAssetTermFieldType.Guid, false)]
    [InlineData(SecurityAssetTermFieldType.Object, false)]
    public void AMemberTypeTheWalkCannotCheck_FailsClosed(SecurityAssetTermFieldType type, bool checkedType)
    {
        ScheduleTermsWriteRules.IsCheckedMemberType(type).Should().Be(checkedType);
    }

    [Fact]
    public void EveryDeclaredContainerMember_HasACheckedType()
    {
        var members = SecurityAssetClassCatalog.All
            .SelectMany(descriptor => new[] { SecurityVersionedTerms.ScheduleConventionsKey, SecurityVersionedTerms.CallScheduleKey, SecurityVersionedTerms.PutScheduleKey }
                .SelectMany(key => SecurityAssetTermsSchema.ElementFields(descriptor.AssetClass, key)))
            .ToArray();

        members.Should().NotBeEmpty();
        members.Should().OnlyContain(field => ScheduleTermsWriteRules.IsCheckedMemberType(field.Type),
            "a stored row must never meet the fail-closed refusal, which exists for a later schema member");
    }

    private static JsonElement Block(string calendars)
        => JsonSerializer.Deserialize<JsonElement>(
            """{"scheduleConventions":{"businessDayConvention":"Following","accrualAdjusted":false,"paymentCalendars":""" + calendars + "}}");

    private static JsonElement Bond(JsonElement containers)
    {
        var terms = new Dictionary<string, object?>
        {
            ["issueDate"] = "2026-01-01",
            ["maturity"] = "2030-01-01",
            ["paymentFrequency"] = "SemiAnnual",
            ["couponRate"] = 4.25m,
            ["dayCount"] = "30/360",
        };
        foreach (var container in containers.EnumerateObject())
            terms[container.Name] = container.Value;
        return JsonSerializer.SerializeToElement(terms);
    }
}
