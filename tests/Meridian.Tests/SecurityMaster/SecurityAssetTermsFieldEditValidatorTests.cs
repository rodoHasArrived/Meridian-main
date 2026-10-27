using FluentAssertions;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Tests.SecurityMaster;

[Trait("Category", "Unit")]
public sealed class SecurityAssetTermsFieldEditValidatorTests
{
    [Theory]
    [InlineData("Identity.Isin")]
    [InlineData("EconomicDefinition.Coupon")]
    [InlineData("operatorNote")]
    public void PathsOutsideAssetTermsNamespace_AreAlwaysValid(string fieldPath)
    {
        SecurityAssetTermsFieldEditValidator.TargetsAssetSpecificTerms(fieldPath).Should().BeFalse();
        SecurityAssetTermsFieldEditValidator.TryValidate("Bond", fieldPath, "anything", out _, out var error).Should().BeTrue();
        error.Should().BeNull();
    }

    [Theory]
    [InlineData("assetSpecificTerms")]
    [InlineData(" assetSpecificTerms ")]
    [InlineData("ASSETSPECIFICTERMS")]
    public void ExactAssetTermsRoot_IsReservedAndRejected(string fieldPath)
    {
        // The bare root names no schema field, but it must still classify as RESERVED: treating it
        // as a free annotation would let a root-level asset-terms value stage past the schema,
        // pinned-profile validation, and the overrides endpoint's reserved-namespace rejection.
        SecurityAssetTermsFieldEditValidator.TargetsAssetSpecificTerms(fieldPath).Should().BeTrue();
        SecurityAssetTermsFieldEditValidator.TryValidate("Bond", fieldPath, "{\"maturity\":\"2031-06-15\"}", out _, out var error)
            .Should().BeFalse();
        error.Should().Contain("reserved");
    }

    [Theory]
    [InlineData("Bond", "assetSpecificTerms.maturity", "2031-06-15")]
    [InlineData("Bond", "assetSpecificTerms.couponRate", "4.25")]
    [InlineData("Bond", "assetSpecificTerms.isCallable", "true")]
    [InlineData("Bond", "assetSpecificTerms.issuerName", "ACME Corp")]
    [InlineData("Option", "assetSpecificTerms.underlyingId", "6f9619ff-8b86-d011-b42d-00cf4fc964ff")]
    [InlineData("Future", "assetSpecificTerms.rollWindowDays", "5")]
    [InlineData("Swap", "assetSpecificTerms.legs", "[{\"legType\":\"Fixed\"}]")]
    public void DeclaredFieldWithTypeConformingValue_IsValid(string assetClass, string fieldPath, string value)
    {
        SecurityAssetTermsFieldEditValidator.TryValidate(assetClass, fieldPath, value, out _, out var error)
            .Should().BeTrue(error);
    }

    [Theory]
    [InlineData("Bond", "assetSpecificTerms.maturity", "not-a-date")]
    [InlineData("Bond", "assetSpecificTerms.couponRate", "four-and-a-quarter")]
    [InlineData("Bond", "assetSpecificTerms.isCallable", "yes")]
    [InlineData("Option", "assetSpecificTerms.underlyingId", "not-a-guid")]
    [InlineData("Future", "assetSpecificTerms.rollWindowDays", "5.5")]
    [InlineData("Swap", "assetSpecificTerms.legs", "{\"legType\":\"Fixed\"}")]
    public void DeclaredFieldWithTypeViolatingValue_IsRejected(string assetClass, string fieldPath, string value)
    {
        SecurityAssetTermsFieldEditValidator.TryValidate(assetClass, fieldPath, value, out _, out var error)
            .Should().BeFalse();
        error.Should().Contain("does not parse as the declared type");
    }

    [Fact]
    public void UndeclaredField_IsRejectedWithDeclaredFieldList()
    {
        SecurityAssetTermsFieldEditValidator.TryValidate("Bond", "assetSpecificTerms.strike", "100", out _, out var error)
            .Should().BeFalse();
        error.Should().Contain("'strike' is not a declared asset-specific term")
            .And.Contain("Bond")
            .And.Contain("maturity");
    }

    [Fact]
    public void SchemaVersion_IsNeverOperatorEditable()
    {
        SecurityAssetTermsFieldEditValidator.TryValidate("Bond", "assetSpecificTerms.schemaVersion", "3", out _, out var error)
            .Should().BeFalse();
        error.Should().Contain("not operator-editable");
    }

    [Fact]
    public void LegacyAlias_IsAcceptedAsTheDeclaredField()
    {
        // "dayCountConvention" is a declared legacy alias of Bond "dayCount".
        SecurityAssetTermsFieldEditValidator.TryValidate("Bond", "assetSpecificTerms.dayCountConvention", "30/360", out _, out var error)
            .Should().BeTrue(error);
    }

    [Fact]
    public void NestedPathUnderScalarField_IsRejected()
    {
        SecurityAssetTermsFieldEditValidator.TryValidate("Option", "assetSpecificTerms.strike.value", "100", out _, out var error)
            .Should().BeFalse();
        error.Should().Contain("has no nested path");
    }

    [Fact]
    public void NestedPathUnderObjectField_IsAccepted()
    {
        SecurityAssetTermsFieldEditValidator.TryValidate(
                "Equity", "assetSpecificTerms.preferredTerms.dividendRate", "6.25", out _, out var error)
            .Should().BeTrue(error);
    }

    [Fact]
    public void ProfileFieldsPath_OnProfileBackedClass_IsAccepted()
    {
        SecurityAssetTermsFieldEditValidator.TryValidate(
                "CustomAsset", "assetSpecificTerms.profileFields.tranche", "A2", out _, out var error)
            .Should().BeTrue(error);
    }

    [Theory]
    [InlineData("CustomAsset")]
    [InlineData("OtherSecurity")]
    public void ProfileFieldsRoot_OnProfileBackedClass_RequiresJsonObject(string assetClass)
    {
        SecurityAssetTermsFieldEditValidator.TryValidate(
                assetClass, "assetSpecificTerms.profileFields", "{\"tranche\":\"A2\"}", out _, out var validError)
            .Should().BeTrue(validError);

        SecurityAssetTermsFieldEditValidator.TryValidate(
                assetClass, "assetSpecificTerms.profileFields", "not-json", out _, out var textError)
            .Should().BeFalse();
        textError.Should().Contain("declared type Object");

        SecurityAssetTermsFieldEditValidator.TryValidate(
                assetClass, "assetSpecificTerms.profileFields", "[]", out _, out var arrayError)
            .Should().BeFalse();
        arrayError.Should().Contain("declared type Object");
    }

    [Fact]
    public void ProfileFieldsPath_OnNonProfileBackedClass_IsRejected()
    {
        SecurityAssetTermsFieldEditValidator.TryValidate(
                "Equity", "assetSpecificTerms.profileFields.custom", "x", out _, out var error)
            .Should().BeFalse();
        error.Should().Contain("not a declared asset-specific term");
    }

    [Fact]
    public void BlankValue_ClearsTheOverlayAndIsAccepted()
    {
        SecurityAssetTermsFieldEditValidator.TryValidate("Bond", "assetSpecificTerms.couponRate", "  ", out _, out var error)
            .Should().BeTrue(error);
        SecurityAssetTermsFieldEditValidator.TryValidate("Bond", "assetSpecificTerms.couponRate", null, out _, out error)
            .Should().BeTrue(error);
    }

    [Fact]
    public void BarePrefixWithoutKey_IsRejected()
    {
        SecurityAssetTermsFieldEditValidator.TryValidate("Bond", "assetSpecificTerms.", "x", out _, out var error)
            .Should().BeFalse();
        error.Should().Contain("must name a term field");
    }

    [Theory]
    // An accepted legacy alias must normalize to the declared field key so both spellings share one
    // override key, revision lineage, and provenance row.
    [InlineData("Bond", "assetSpecificTerms.dayCountConvention", "30/360", "assetSpecificTerms.dayCount")]
    // Casing variants of the prefix and key collapse to the canonical spelling.
    [InlineData("Bond", "ASSETSPECIFICTERMS.CouponRate", "4.25", "assetSpecificTerms.couponRate")]
    // Nested paths keep their remainder while the declared root key is normalized.
    [InlineData("Equity", "assetSpecificTerms.PreferredTerms.dividendRate", "6.25", "assetSpecificTerms.preferredTerms.dividendRate")]
    // profileFields paths normalize the envelope key on profile-backed classes.
    [InlineData("CustomAsset", "assetSpecificTerms.ProfileFields.tranche", "A2", "assetSpecificTerms.profileFields.tranche")]
    public void AcceptedAssetTermPaths_NormalizeToTheDeclaredFieldKey(
        string assetClass, string fieldPath, string value, string expectedCanonicalPath)
    {
        SecurityAssetTermsFieldEditValidator.TryValidate(assetClass, fieldPath, value, out var canonicalPath, out var error)
            .Should().BeTrue(error);
        canonicalPath.Should().Be(expectedCanonicalPath);
    }

    [Fact]
    public void PathsOutsideAssetTermsNamespace_PassThroughUnchanged()
    {
        SecurityAssetTermsFieldEditValidator.TryValidate("Bond", "operatorNote", "context", out var canonicalPath, out _)
            .Should().BeTrue();
        canonicalPath.Should().Be("operatorNote");
    }

    [Theory]
    [InlineData("Bond", "assetSpecificTerms.principalSchedule",
        "[{\"paymentDate\":\"2028-06-15\",\"amount\":-500}]",
        "*greater than zero*")]
    [InlineData("Bond", "assetSpecificTerms.principalSchedule",
        "[{\"paymentDate\":\"not-a-date\",\"amount\":500}]",
        "*parseable 'paymentDate'*")]
    [InlineData("StructuredCredit", "assetSpecificTerms.factorScheduleEntries",
        "[{\"asOfDate\":\"2026-01-01\",\"factor\":1.5}]",
        "*between zero and one*")]
    [InlineData("StructuredCredit", "assetSpecificTerms.factorScheduleEntries",
        "[{\"asOfDate\":\"2026-01-01\",\"factor\":0.8},{\"asOfDate\":\"2026-01-01\",\"factor\":0.7}]",
        "*unique*")]
    [InlineData("StructuredCredit", "assetSpecificTerms.factorScheduleEntries",
        "[{\"asOfDate\":\"2026-01-01\",\"factor\":0.7},{\"asOfDate\":\"2026-02-01\",\"factor\":0.9}]",
        "*non-increasing*")]
    public void ScheduleReplacements_ViolatingDomainInvariants_AreRejected(
        string assetClass, string fieldPath, string value, string expectedError)
    {
        // A syntactically valid array is not enough: rows that the canonical F# command would
        // reject (negative instalments, factors above one, duplicate or rising factor dates) must
        // not stage an overlay with a draft revision and provenance row.
        SecurityAssetTermsFieldEditValidator.TryValidate(assetClass, fieldPath, value, out _, out var error)
            .Should().BeFalse();
        error.Should().Match(expectedError);
    }

    [Theory]
    [InlineData("Bond", "assetSpecificTerms.principalSchedule",
        "[{\"paymentDate\":\"2028-06-15\",\"amount\":500},{\"paymentDate\":\"2029-06-15\",\"amount\":250}]")]
    [InlineData("StructuredCredit", "assetSpecificTerms.factorScheduleEntries",
        "[{\"asOfDate\":\"2026-01-01\",\"factor\":0.9},{\"asOfDate\":\"2026-02-01\",\"factor\":0.8}]")]
    public void ScheduleReplacements_SatisfyingDomainInvariants_AreAccepted(
        string assetClass, string fieldPath, string value)
    {
        SecurityAssetTermsFieldEditValidator.TryValidate(assetClass, fieldPath, value, out _, out var error)
            .Should().BeTrue(error);
    }

    [Fact]
    public void EveryDeclaredAssetClass_AcceptsItsOwnDeclaredKeys()
    {
        // The profile governance envelope is declared for round-trip purposes but reserved from
        // per-field operator edits: repinning identity or touching approval evidence bypasses the
        // complete-envelope amendment route.
        string[] reservedGovernanceKeys = ["customProfileId", "profileVersion", "profileApproval"];
        foreach (var assetClass in SecurityAssetTermsSchema.AssetClasses)
        {
            // Only what the node writes: a term it reads ahead of writing (ADR-024) is refused, which
            // the readable-only edit tests below pin.
            foreach (var field in SecurityAssetTermsSchema.WritableFields(assetClass))
            {
                if (reservedGovernanceKeys.Contains(field.Key, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                SecurityAssetTermsFieldEditValidator
                    .TryValidate(assetClass, $"assetSpecificTerms.{field.Key}", null, out _, out var error)
                    .Should().BeTrue($"'{assetClass}.{field.Key}' is declared, but was rejected: {error}");
            }
        }
    }

    [Theory]
    [InlineData("assetSpecificTerms.customProfileId", "structured-credit-io-po")]
    [InlineData("assetSpecificTerms.profileVersion", "2")]
    [InlineData("assetSpecificTerms.profileApproval", "{\"approvedBy\":\"rogue\"}")]
    public void GovernanceEnvelopeKeys_AreNotEditableFieldByField(string fieldPath, string value)
    {
        // A scalar repin of customProfileId/profileVersion would change the record's profile
        // identity while keeping the old profileFields and approval metadata, bypassing the
        // complete-envelope catalog validation and reclassification of the canonical amend seam.
        SecurityAssetTermsFieldEditValidator.TryValidate("CustomAsset", fieldPath, value, out _, out var error)
            .Should().BeFalse();
        error.Should().Match("*governed profile envelope*");
    }

    [Theory]
    [InlineData("Bond", "assetSpecificTerms.principalSchedule.0.amount", "-10")]
    [InlineData("StructuredCredit", "assetSpecificTerms.factorScheduleEntries.0.factor", "2")]
    public void NestedScheduleValueEdits_AreRejected(string assetClass, string fieldPath, string value)
    {
        // A row fragment cannot be validated against schedule-wide invariants; asserting values
        // requires replacing the whole array (which runs them). Clears of such subpaths still
        // pass — they remove junk overlay keys rather than asserting a value.
        SecurityAssetTermsFieldEditValidator.TryValidate(assetClass, fieldPath, value, out _, out var error)
            .Should().BeFalse();
        error.Should().Match("*replace the whole array*");

        SecurityAssetTermsFieldEditValidator.TryValidate(assetClass, fieldPath, null, out _, out _)
            .Should().BeTrue("clears remove junk overlay keys rather than asserting a value");
    }

    // ── Readable before writable (ADR-024) ─────────────────────────────────────────────────────

    // The reason every write surface gives for a term this node reads but does not yet write
    // (SecurityTermsEnvelope.DescribeNotYetWritable), spelled out so a reworded refusal fails here.
    private static string NotYetWritable(string assetClass, string path, int since, int writes)
        => $"'{path}' is an asset-class terms version {since} term of '{assetClass}'. This node reads it but writes " +
           $"assetClassTermsVersion {writes}, so a create, amend, import or field edit cannot set it yet. " +
           "Leave it absent, null or empty (ADR-024).";

    [Theory]
    [InlineData("Bond", 2, 1, null)]
    [InlineData("Bond", 2, 1, "{}")]
    [InlineData("Bond", 2, 1, """{"stubPolicy":"Short","paymentCalendars":["USNY"]}""")]
    [InlineData("CertificateOfDeposit", 2, 1, null)]
    [InlineData("CertificateOfDeposit", 2, 1, "{}")]
    [InlineData("CertificateOfDeposit", 2, 1, """{"businessDayConvention":"ModifiedFollowing"}""")]
    [InlineData("StructuredCredit", 3, 2, null)]
    [InlineData("StructuredCredit", 3, 2, "{}")]
    [InlineData("StructuredCredit", 3, 2, """{"rollConvention":"15","endOfMonthRule":true}""")]
    public void ReadableOnlyScheduleConventions_IsRefused_ForASetAndForAClear(
        string assetClass, int since, int writes, string? value)
    {
        // The merge maps an approved edit in Write mode, which refuses the block, so staging even a
        // clear would put a draft and a provenance row behind an edit that can never publish.
        SecurityAssetTermsFieldEditValidator.TryValidate(
                assetClass, "assetSpecificTerms.scheduleConventions", value, out var canonicalPath, out var error)
            .Should().BeFalse();
        error.Should().Be(NotYetWritable(assetClass, "scheduleConventions", since, writes));
        canonicalPath.Should().Be("assetSpecificTerms.scheduleConventions", "a refused edit passes its path through unchanged");
    }

    [Theory]
    [InlineData("Bond", 2, 1, "assetSpecificTerms.scheduleConventions.stubPolicy", "Short")]
    [InlineData("Bond", 2, 1, "assetSpecificTerms.scheduleConventions.paymentCalendars", null)]
    [InlineData("Bond", 2, 1, "assetSpecificTerms.ScheduleConventions.rollConvention", "EndOfMonth")]
    [InlineData("CertificateOfDeposit", 2, 1, "assetSpecificTerms.scheduleConventions.businessDayConvention", "Following")]
    [InlineData("StructuredCredit", 3, 2, "assetSpecificTerms.scheduleConventions.stubPolicy", "Long")]
    [InlineData("StructuredCredit", 3, 2, "assetSpecificTerms.scheduleConventions.paymentLagBusinessDays", null)]
    public void NestedMemberOfReadableOnlyScheduleConventions_IsRefused(
        string assetClass, int since, int writes, string fieldPath, string? value)
    {
        // The refusal names the declared block, whatever member or key casing the edit addressed.
        SecurityAssetTermsFieldEditValidator.TryValidate(assetClass, fieldPath, value, out _, out var error)
            .Should().BeFalse();
        error.Should().Be(NotYetWritable(assetClass, "scheduleConventions", since, writes));
    }

    [Theory]
    [InlineData("assetSpecificTerms.legs",
        """[{"legId":"fixed","legType":"Fixed","scheduleConventions":{"stubPolicy":"Short"}}]""",
        "legs[0].scheduleConventions")]
    [InlineData("assetSpecificTerms.legs",
        """[{"legId":"fixed","legType":"Fixed","scheduleConventions":null},{"legId":"float","legType":"Floating","scheduleConventions":{"paymentCalendars":["USNY"]}}]""",
        "legs[1].scheduleConventions")]
    [InlineData("assetSpecificTerms.legs",
        """[{"legId":"fixed","legType":"Fixed","scheduleConventions":{"accrualAdjusted":false}}]""",
        "legs[0].scheduleConventions")]
    [InlineData("assetSpecificTerms.legs",
        """[{"legId":"fixed","legType":"Fixed","ScheduleConventions":{"rollConvention":"Imm"}}]""",
        "legs[0].ScheduleConventions")]
    [InlineData("assetSpecificTerms.swapLegs",
        """[{"legId":"fixed","legType":"Fixed","scheduleConventions":{"direction":"Backward"}}]""",
        "legs[0].scheduleConventions")]
    public void WholeSwapLegsReplacement_CarryingALegBlock_IsRefused(string fieldPath, string value, string blockPath)
    {
        // legs itself is writable; the block inside one of its elements is a version 2 term. false is
        // populated, so {accrualAdjusted:false} is refused like any other value.
        SecurityAssetTermsFieldEditValidator.TryValidate("Swap", fieldPath, value, out _, out var error)
            .Should().BeFalse();
        error.Should().Be(NotYetWritable("Swap", blockPath, 2, 1));
    }

    [Theory]
    [InlineData("""[{"legId":"fixed","legType":"Fixed","scheduleConventions":null}]""")]
    [InlineData("""[{"legId":"fixed","legType":"Fixed","scheduleConventions":{}}]""")]
    [InlineData("""[{"legId":"fixed","legType":"Fixed","scheduleConventions":{"paymentCalendars":[],"stubPolicy":null}}]""")]
    public void WholeSwapLegsReplacement_WithAnUnsetLegBlock_IsAccepted(string value)
    {
        // Null, {} and an all-empty object mean unset, as an absent block does, which this node writes.
        SecurityAssetTermsFieldEditValidator.TryValidate("Swap", "assetSpecificTerms.legs", value, out var canonicalPath, out var error)
            .Should().BeTrue(error);
        canonicalPath.Should().Be("assetSpecificTerms.legs");
    }

    [Theory]
    [InlineData("assetSpecificTerms.legs.0.scheduleConventions", null, "legs.0.scheduleConventions")]
    [InlineData("assetSpecificTerms.legs.0.scheduleConventions", "{\"stubPolicy\":\"Short\"}", "legs.0.scheduleConventions")]
    [InlineData("assetSpecificTerms.legs.1.scheduleConventions.businessDayConvention", "ModifiedFollowing", "legs.1.scheduleConventions.businessDayConvention")]
    [InlineData("assetSpecificTerms.legs.scheduleConventions", "{}", "legs.scheduleConventions")]
    [InlineData("assetSpecificTerms.swapLegs.0.ScheduleConventions", null, "legs.0.ScheduleConventions")]
    public void NestedSwapLegScheduleConventions_IsRefused(string fieldPath, string? value, string blockPath)
    {
        SecurityAssetTermsFieldEditValidator.TryValidate("Swap", fieldPath, value, out _, out var error)
            .Should().BeFalse();
        error.Should().Be(NotYetWritable("Swap", blockPath, 2, 1));
    }

    [Theory]
    [InlineData("assetSpecificTerms.legs.0.notionalSchedule", null)]
    [InlineData("assetSpecificTerms.legs.0", """{"legId":"fixed","legType":"Fixed","scheduleConventions":null}""")]
    [InlineData("assetSpecificTerms.legs.1", """{"legId":"float","legType":"Floating","scheduleConventions":{}}""")]
    public void NestedSwapLegEdit_WithoutAPopulatedLegBlock_IsStillAccepted(string fieldPath, string? value)
    {
        // Only the readable-only element field is refused; the leg's other nested paths keep their
        // dynamic pass-through.
        SecurityAssetTermsFieldEditValidator.TryValidate("Swap", fieldPath, value, out var canonicalPath, out var error)
            .Should().BeTrue(error);
        canonicalPath.Should().Be(fieldPath);
    }

    [Theory]
    [InlineData("assetSpecificTerms.legs.0",
        """{"legId":"fixed","legType":"Fixed","scheduleConventions":{"stubPolicy":"Short"}}""",
        "legs.0.scheduleConventions")]
    [InlineData("assetSpecificTerms.swapLegs.1",
        """{"legId":"float","legType":"Floating","scheduleConventions":{"paymentCalendars":["USNY"]}}""",
        "legs.1.scheduleConventions")]
    [InlineData("assetSpecificTerms.legs.0",
        """{"legId":"fixed","legType":"Fixed","scheduleConventions":{"accrualAdjusted":false}}""",
        "legs.0.scheduleConventions")]
    public void NestedSwapLegElementReplacement_CarryingAPopulatedLegBlock_IsRefused(
        string fieldPath, string value, string blockPath)
    {
        // Replacing one leg sets the leg's scheduleConventions as surely as legs.0.scheduleConventions
        // does, so it is refused like that path and like a whole-legs value carrying the same block.
        SecurityAssetTermsFieldEditValidator.TryValidate("Swap", fieldPath, value, out _, out var error)
            .Should().BeFalse("a leg element carrying a populated block sets a term this node only reads");
        error.Should().Be(NotYetWritable("Swap", blockPath, 2, 1));
    }

    [Theory]
    [InlineData("Bond", "callSchedule", 3, 1, """[{"callDate":"2030-06-15","callPrice":101}]""")]
    [InlineData("Bond", "callSchedule", 3, 1, "[]")]
    [InlineData("Bond", "callSchedule", 3, 1, null)]
    [InlineData("Bond", "putSchedule", 3, 1, """[{"putDate":"2029-06-15"}]""")]
    [InlineData("Bond", "putSchedule", 3, 1, "[]")]
    [InlineData("Bond", "putSchedule", 3, 1, null)]
    [InlineData("CertificateOfDeposit", "callSchedule", 3, 1, """[{"callDate":"2026-09-30","partialRedemption":"WholeOnly"}]""")]
    [InlineData("CertificateOfDeposit", "callSchedule", 3, 1, "[]")]
    [InlineData("CertificateOfDeposit", "callSchedule", 3, 1, null)]
    [InlineData("CertificateOfDeposit", "putSchedule", 3, 1, """[{"putDate":"2026-09-30","putPrice":100}]""")]
    [InlineData("CertificateOfDeposit", "putSchedule", 3, 1, "[]")]
    [InlineData("CertificateOfDeposit", "putSchedule", 3, 1, null)]
    [InlineData("StructuredCredit", "callSchedule", 4, 2, """[{"callDate":"2030-06-15","exerciseStyle":"Bermudan"}]""")]
    [InlineData("StructuredCredit", "callSchedule", 4, 2, "[]")]
    [InlineData("StructuredCredit", "callSchedule", 4, 2, null)]
    [InlineData("StructuredCredit", "putSchedule", 4, 2, """[{"putDate":"2029-06-15"}]""")]
    [InlineData("StructuredCredit", "putSchedule", 4, 2, "[]")]
    [InlineData("StructuredCredit", "putSchedule", 4, 2, null)]
    public void ReadableOnlyCallAndPutSchedules_AreRefused_ForASetAnEmptyArrayAndAClear(
        string assetClass, string key, int since, int writes, string? value)
    {
        // The arrays are terms of the version above the block's (3, or 4 for StructuredCredit). An
        // empty array and a clear mean unset, but the merge would still map the edit in Write mode, so
        // they are refused like an entry array.
        SecurityAssetTermsFieldEditValidator.TryValidate(
                assetClass, $"assetSpecificTerms.{key}", value, out var canonicalPath, out var error)
            .Should().BeFalse();
        error.Should().Be(NotYetWritable(assetClass, key, since, writes));
        canonicalPath.Should().Be($"assetSpecificTerms.{key}", "a refused edit passes its path through unchanged");
    }

    [Theory]
    [InlineData("Bond", "assetSpecificTerms.callSchedule.0.callDate", "2030-06-15", "callSchedule", 3, 1)]
    [InlineData("Bond", "assetSpecificTerms.callSchedule.0.callDate", null, "callSchedule", 3, 1)]
    [InlineData("Bond", "assetSpecificTerms.putSchedule.0", """{"putDate":"2029-06-15"}""", "putSchedule", 3, 1)]
    [InlineData("Bond", "assetSpecificTerms.CallSchedule", """[{"callDate":"2030-06-15"}]""", "callSchedule", 3, 1)]
    [InlineData("CertificateOfDeposit", "assetSpecificTerms.callSchedule.0.callDate", "2026-09-30", "callSchedule", 3, 1)]
    [InlineData("CertificateOfDeposit", "assetSpecificTerms.putSchedule.0", null, "putSchedule", 3, 1)]
    [InlineData("CertificateOfDeposit", "assetSpecificTerms.CallSchedule", "[]", "callSchedule", 3, 1)]
    [InlineData("StructuredCredit", "assetSpecificTerms.callSchedule.0.callDate", "2030-06-15", "callSchedule", 4, 2)]
    [InlineData("StructuredCredit", "assetSpecificTerms.putSchedule.0", """{"putDate":"2029-06-15"}""", "putSchedule", 4, 2)]
    [InlineData("StructuredCredit", "assetSpecificTerms.CallSchedule", null, "callSchedule", 4, 2)]
    public void NestedOrCaseVariantEditOfAReadableOnlySchedule_IsRefused_NamingTheDeclaredArray(
        string assetClass, string fieldPath, string? value, string key, int since, int writes)
    {
        // Every member of a readable-only container is refused: an element, a member of an element
        // and a case variant of the key all name the declared array in the reason.
        SecurityAssetTermsFieldEditValidator.TryValidate(assetClass, fieldPath, value, out var canonicalPath, out var error)
            .Should().BeFalse();
        error.Should().Be(NotYetWritable(assetClass, key, since, writes));
        canonicalPath.Should().Be(fieldPath, "a refused edit passes its path through unchanged");
    }

    [Theory]
    [InlineData("assetSpecificTerms.profileFields.scheduleConventions.stubPolicy", "Short",
        "assetSpecificTerms.profileFields.scheduleConventions.stubPolicy")]
    [InlineData("assetSpecificTerms.profileFields.scheduleConventions", "{\"stubPolicy\":\"Short\"}",
        "assetSpecificTerms.profileFields.scheduleConventions")]
    [InlineData("assetSpecificTerms.profileFields",
        """{"tranche":"A-1","scheduleConventions":{"stubPolicy":"Short","rollConvention":"15"}}""",
        "assetSpecificTerms.profileFields")]
    // The version 4 call and put schedules are no different inside a profile envelope.
    [InlineData("assetSpecificTerms.profileFields.callSchedule", """[{"callDate":"2030-06-15","callPrice":101}]""",
        "assetSpecificTerms.profileFields.callSchedule")]
    [InlineData("assetSpecificTerms.profileFields.putSchedule.0.putDate", "2029-06-15",
        "assetSpecificTerms.profileFields.putSchedule.0.putDate")]
    [InlineData("assetSpecificTerms.profileFields",
        """{"tranche":"A-1","callSchedule":[{"callDate":"2030-06-15"}],"putSchedule":[{"putDate":"2029-06-15"}]}""",
        "assetSpecificTerms.profileFields")]
    public void StructuredCreditProfileFieldsScheduleConventions_IsStillAccepted(
        string fieldPath, string value, string expectedCanonicalPath)
    {
        // A profile envelope's fields belong to the approved profile, not to the class's terms
        // version: versioned class terms are never walked inside profileFields, so these edits stage
        // exactly as they did before the class read versions 3 and 4.
        SecurityAssetTermsFieldEditValidator.TryValidate("StructuredCredit", fieldPath, value, out var canonicalPath, out var error)
            .Should().BeTrue(error);
        error.Should().BeNull();
        canonicalPath.Should().Be(expectedCanonicalPath);
    }

    [Theory]
    [InlineData("Bond")]
    [InlineData("CertificateOfDeposit")]
    [InlineData("StructuredCredit")]
    public void UndeclaredFieldRefusal_ListsOnlyTheWritableFields(string assetClass)
    {
        SecurityAssetTermsFieldEditValidator.TryValidate(assetClass, "assetSpecificTerms.notATerm", "x", out _, out var error)
            .Should().BeFalse();
        error.Should().Be(
            $"'notATerm' is not a declared asset-specific term for asset class '{assetClass}'. " +
            $"Declared term fields: {string.Join(", ", SecurityAssetTermsSchema.WritableFields(assetClass).Select(static field => field.Key))}.");
        error.Should().NotContain("scheduleConventions")
            .And.NotContain("callSchedule")
            .And.NotContain("putSchedule");
    }
}
