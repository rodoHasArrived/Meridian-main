using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// The schedule-terms rule gaps (E4, E7, E9a/b, E10d, C1a, C5c and the R24 European exemption from C2),
/// and the retained-declaration scope: a gap rule judges only a container the write introduces or changes.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ScheduleTermsWriteRulesGapTests
{
    private const string Prefix = "Invalid declared schedule terms: ";

    public static TheoryData<string, string, string> GapCases => new()
    {
        {
            "E4",
            """{"callSchedule":[{"callDate":"2027-01-01","exerciseStyle":"European","exerciseFrequency":"Quarterly"}]}""",
            "Declared callSchedule[0] exerciseFrequency 'Quarterly' applies only to a Bermudan entry, and its exerciseStyle is 'European'."
        },
        {
            "E7",
            """{"putSchedule":[{"putDate":"2027-01-01","putEndDate":"2027-06-01","exerciseStyle":"European"}]}""",
            "Declared putSchedule[0] is European, which exercises on its start date only, so it cannot declare a putEndDate."
        },
        {
            "E9a-no-price-type",
            """{"callSchedule":[{"callDate":"2027-01-01","exerciseStyle":"European","makeWholeSpreadBps":25}]}""",
            "Declared callSchedule[0] makeWholeSpreadBps applies only to a priceType 'MakeWhole' entry; it declares no priceType, which means Fixed."
        },
        {
            "E9a-fixed",
            """{"callSchedule":[{"callDate":"2027-01-01","exerciseStyle":"European","priceType":"Fixed","makeWholeSpreadBps":25}]}""",
            "Declared callSchedule[0] makeWholeSpreadBps applies only to a priceType 'MakeWhole' entry; its priceType is 'Fixed'."
        },
        {
            "E9b",
            """{"callSchedule":[{"callDate":"2027-01-01","exerciseStyle":"European","priceType":"MakeWhole","makeWholeSpreadBps":-0.5}]}""",
            "Declared callSchedule[0] makeWholeSpreadBps -0.5 must be zero or more."
        },
        {
            "E10d",
            """{"putSchedule":[{"putDate":"2027-01-01","exerciseStyle":"European","noticeDayType":"Calendar"}]}""",
            "Declared putSchedule[0] noticeDayType 'Calendar' has no effect without minimumNoticeDays or maximumNoticeDays."
        },
        {
            "C1a",
            """{"putSchedule":[{"putDate":"2027-01-01","exerciseStyle":"European"},{"putDate":"2023-06-01","exerciseStyle":"European"}]}""",
            "Declared putSchedule[1] putDate 2023-06-01 is before the issueDate 2024-01-01; an exercise window cannot open before issue."
        },
    };

    [Theory]
    [MemberData(nameof(GapCases))]
    public void IntroducedDeclarations_AreRefusedWithTheGapText(string rule, string schedules, string text)
    {
        var payload = BondWith(schedules);

        var action = () => ScheduleTermsWriteRules.Validate("Bond", payload);

        action.Should().Throw<InvalidOperationException>(rule).Which.Message.Should().Be(Prefix + text);
    }

    [Theory]
    [MemberData(nameof(GapCases))]
    public void RetainedDeclarations_KeepTheRulesTheyWereStoredUnder(string rule, string schedules, string text)
    {
        _ = text;
        var payload = BondWith(schedules);

        var unchanged = () => ScheduleTermsWriteRules.Validate("Bond", payload, payload);
        var unrelatedScalarChanged = () => ScheduleTermsWriteRules.Validate("Bond", payload, With(payload, "couponRate", 5.5));

        unchanged.Should().NotThrow(rule);
        unrelatedScalarChanged.Should().NotThrow(rule);
    }

    [Theory]
    [MemberData(nameof(GapCases))]
    public void ChangingARetainedContainer_JudgesItInFull(string rule, string schedules, string text)
    {
        var payload = BondWith(schedules);
        var retained = BondWith("""{"callSchedule":[],"putSchedule":[]}""");

        var action = () => ScheduleTermsWriteRules.Validate("Bond", payload, retained);

        action.Should().Throw<InvalidOperationException>(rule).Which.Message.Should().Be(Prefix + text);
    }

    [Fact]
    public void ChangingTheIssueDate_JudgesRetainedWindowsAgainstIt()
    {
        var retained = BondWith("""{"putSchedule":[{"putDate":"2027-01-01","exerciseStyle":"European"}]}""");
        var payload = With(retained, "issueDate", "2027-06-01");

        var action = () => ScheduleTermsWriteRules.Validate("Bond", payload, retained);

        action.Should().Throw<InvalidOperationException>().Which.Message.Should().Be(Prefix +
            "Declared putSchedule[0] putDate 2027-01-01 is before the issueDate 2027-06-01; an exercise window cannot open before issue.");
    }

    [Fact]
    public void TheGapRules_AcceptTheirAdmissibleNeighbours()
    {
        var payload = BondWith("""
            {"callSchedule":[{"callDate":"2024-01-01","exerciseStyle":"European","priceType":"MakeWhole","makeWholeSpreadBps":0},
                             {"callDate":"2027-01-01","callEndDate":"2028-01-01","exerciseStyle":"Bermudan","exerciseFrequency":"Quarterly"}],
             "putSchedule":[{"putDate":"2027-01-01","exerciseStyle":"European","minimumNoticeDays":5,"noticeDayType":"Calendar"}]}
            """, callDate: "2024-01-01");

        var action = () => ScheduleTermsWriteRules.Validate("Bond", payload);

        action.Should().NotThrow("a window may open on the issue date, and a zero spread on a make-whole call is admissible");
    }

    [Fact]
    public void C5c_ACallOpeningBeforeTheNonCallPeriodEnd_IsRefused()
    {
        var payload = StructuredCredit("2027-01-01", nonCallPeriodEnd: "2027-06-15");

        var action = () => ScheduleTermsWriteRules.Validate("StructuredCredit", payload);
        var retained = () => ScheduleTermsWriteRules.Validate("StructuredCredit", payload, payload);
        var periodMoved = () => ScheduleTermsWriteRules.Validate("StructuredCredit", payload,
            StructuredCredit("2027-01-01", nonCallPeriodEnd: "2026-06-15"));

        action.Should().Throw<InvalidOperationException>().Which.Message.Should().Be(Prefix +
            "Declared callSchedule[0] callDate 2027-01-01 is before the nonCallPeriodEnd 2027-06-15; no call may open before the non-call period ends.");
        retained.Should().NotThrow();
        periodMoved.Should().Throw<InvalidOperationException>().WithMessage("*nonCallPeriodEnd*");
    }

    [Fact]
    public void C5c_NamesTheEarliestEntryByItsStoredIndex()
    {
        var terms = System.Text.Json.Nodes.JsonNode.Parse(StructuredCredit("2028-01-01", nonCallPeriodEnd: "2027-06-15").GetRawText())!.AsObject();
        terms["callSchedule"]!.AsArray().Add(new JsonObject { ["callDate"] = "2027-03-01", ["exerciseStyle"] = "European" });

        var action = () => ScheduleTermsWriteRules.Validate("StructuredCredit", JsonSerializer.SerializeToElement(terms));

        action.Should().Throw<InvalidOperationException>().Which.Message.Should().Be(Prefix +
            "Declared callSchedule[1] callDate 2027-03-01 is before the nonCallPeriodEnd 2027-06-15; no call may open before the non-call period ends.");
    }

    [Fact]
    public void C5c_ACallOpeningAtTheNonCallPeriodEnd_IsAccepted()
    {
        var action = () => ScheduleTermsWriteRules.Validate("StructuredCredit", StructuredCredit("2027-06-15", nonCallPeriodEnd: "2027-06-15"));

        action.Should().NotThrow();
    }

    [Fact]
    public void R24_AEuropeanLastEntryWithoutAMaturity_NeedsNoEnd()
    {
        var european = () => ScheduleTermsWriteRules.Validate("StructuredCredit", StructuredCredit("2027-06-15", nonCallPeriodEnd: null, maturity: null));
        var american = () => ScheduleTermsWriteRules.Validate("StructuredCredit",
            StructuredCredit("2027-06-15", nonCallPeriodEnd: null, maturity: null, style: "Bermudan", frequency: "Quarterly"));

        european.Should().NotThrow("a European entry exercises on its start date only");
        american.Should().Throw<InvalidOperationException>().WithMessage("*final exercise window needs an end date or maturity*");
    }

    [Fact]
    public void ReadBackAnalytics_ApplyOnlyTheRulesARowWasStoredUnder()
    {
        var payload = BondWith("""{"putSchedule":[{"putDate":"2027-01-01","putEndDate":"2027-06-01","exerciseStyle":"European"}]}""");

        var readBack = () => ScheduleTermsWriteRules.ValidateDeclarations("Bond", payload, 3, retained: payload);
        var diagnostic = () => ScheduleTermsWriteRules.ValidateDeclarations("Bond", payload, 3);

        readBack.Should().NotThrow();
        diagnostic.Should().Throw<InvalidOperationException>().WithMessage("*European*");
    }

    private static JsonElement BondWith(string schedules, string? callDate = "2027-01-01")
    {
        var terms = new JsonObject
        {
            ["issueDate"] = "2024-01-01",
            ["maturity"] = "2030-01-01",
            ["paymentFrequency"] = "SemiAnnual",
            ["couponRate"] = 4.25,
            ["isCallable"] = true,
            ["callDate"] = callDate,
        };
        foreach (var (key, value) in JsonNode.Parse(schedules)!.AsObject())
            terms[key] = value?.DeepClone();
        if (terms["callSchedule"] is not JsonArray { Count: > 0 })
        {
            terms["isCallable"] = false;
            terms["callDate"] = null;
        }
        return JsonSerializer.SerializeToElement(terms);
    }

    private static JsonElement StructuredCredit(string callDate, string? nonCallPeriodEnd, string? maturity = "2031-06-15",
        string style = "European", string? frequency = null)
    {
        var entry = new JsonObject { ["callDate"] = callDate, ["exerciseStyle"] = style };
        if (frequency is not null)
            entry["exerciseFrequency"] = frequency;
        var terms = new JsonObject
        {
            ["collateralType"] = "CLO",
            ["structuredCreditKind"] = "Clo",
            ["callSchedule"] = new JsonArray(entry),
        };
        if (maturity is not null)
            terms["maturity"] = maturity;
        if (nonCallPeriodEnd is not null)
            terms["nonCallPeriodEnd"] = nonCallPeriodEnd;
        return JsonSerializer.SerializeToElement(terms);
    }

    private static JsonElement With(JsonElement payload, string key, JsonNode? value)
    {
        var terms = JsonNode.Parse(payload.GetRawText())!.AsObject();
        terms[key] = value;
        return JsonSerializer.SerializeToElement(terms);
    }
}
