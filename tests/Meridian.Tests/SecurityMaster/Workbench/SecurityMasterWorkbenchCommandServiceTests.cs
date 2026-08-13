using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Meridian.Tests.SecurityMaster.Workbench;

public sealed class SecurityMasterWorkbenchCommandServiceTests
{
    private static readonly Guid SecurityId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    // ---- UpdateSecurityField ------------------------------------------------------------------

    [Fact]
    public async Task UpdateSecurityField_AssetTermTypeMismatch_ThrowsArgument()
    {
        var harness = new Harness(currentVersion: 3);
        harness.SetPassportAssetClass("Option");

        var request = new UpdateSecurityFieldRequest(
            SecurityId: SecurityId,
            ExpectedVersion: 3,
            FieldPath: "assetSpecificTerms.strike",
            NewValue: "not-a-number",
            EffectiveFrom: DateTimeOffset.UtcNow,
            Actor: "ops.analyst",
            Justification: "Correct the strike.");

        var ex = await harness.Service.Invoking(s => s.UpdateSecurityFieldAsync(request))
            .Should().ThrowAsync<ArgumentException>();
        ex.Which.Message.Should().Contain("Decimal");
        harness.Overrides.Verify(
            o => o.PatchAsync(It.IsAny<Guid>(), It.IsAny<OperatorOverridesPatchRequest>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<long?>()),
            Times.Never,
            "a type-invalid asset-term edit must be rejected before any overlay write");
    }

    [Fact]
    public async Task UpdateSecurityField_UndeclaredAssetTerm_ThrowsArgumentListingDeclaredFields()
    {
        var harness = new Harness(currentVersion: 3);
        harness.SetPassportAssetClass("Bond");

        var request = new UpdateSecurityFieldRequest(
            SecurityId: SecurityId,
            ExpectedVersion: 3,
            FieldPath: "assetSpecificTerms.strike",
            NewValue: "100",
            EffectiveFrom: DateTimeOffset.UtcNow,
            Actor: "ops.analyst",
            Justification: "Typo: bonds have no strike.");

        var ex = await harness.Service.Invoking(s => s.UpdateSecurityFieldAsync(request))
            .Should().ThrowAsync<ArgumentException>();
        ex.Which.Message.Should().Contain("not a declared asset-specific term").And.Contain("maturity");
    }

    [Fact]
    public async Task UpdateSecurityField_ValidTypedAssetTerm_Stages()
    {
        var harness = new Harness(currentVersion: 3);
        harness.SetPassportAssetClass("Bond");
        // Window terms (maturity/issueDate/par) validate against the record's effective principal
        // schedule, so the retained terms must resolve for the edit to stage.
        harness.SetProjectionAssetTerms("Bond", new { issueDate = "2026-01-01", maturityDate = "2030-01-01", par = 100m });

        var request = new UpdateSecurityFieldRequest(
            SecurityId: SecurityId,
            ExpectedVersion: 3,
            FieldPath: "assetSpecificTerms.maturity",
            NewValue: "2031-06-15",
            EffectiveFrom: DateTimeOffset.UtcNow,
            Actor: "ops.analyst",
            Justification: "Vendor corrected the maturity.");

        var result = await harness.Service.UpdateSecurityFieldAsync(request);

        result.State.Should().Be(SecurityMasterRevisionStateDto.Draft);
    }

    [Fact]
    public async Task UpdateSecurityField_ProfileFieldsPathOnCustomAsset_Stages()
    {
        var harness = new Harness(currentVersion: 3);
        harness.SetPassportAssetClass("CustomAsset");
        harness.SetProjectionProfileEnvelope("structured-credit-io-po", profileVersion: 1);

        var request = new UpdateSecurityFieldRequest(
            SecurityId: SecurityId,
            ExpectedVersion: 3,
            FieldPath: "assetSpecificTerms.profileFields.tranche",
            NewValue: "A2",
            EffectiveFrom: DateTimeOffset.UtcNow,
            Actor: "ops.analyst",
            Justification: "Profile-governed field correction.");

        var result = await harness.Service.UpdateSecurityFieldAsync(request);

        result.State.Should().Be(SecurityMasterRevisionStateDto.Draft);
    }

    [Fact]
    public async Task UpdateSecurityField_ProfileFieldEdit_ValidatesAgainstThePinnedProfile()
    {
        // The static schema cannot type dynamic profile-governed fields; the pinned profile can.
        // structured-credit-io-po declares currentFactor as a Decimal bounded [0, 1].
        var harness = new Harness(currentVersion: 3);
        harness.SetPassportAssetClass("CustomAsset");
        harness.SetProjectionProfileEnvelope("structured-credit-io-po", profileVersion: 1);

        UpdateSecurityFieldRequest EditWith(string value) => new(
            SecurityId: SecurityId,
            ExpectedVersion: 3,
            FieldPath: "assetSpecificTerms.profileFields.currentFactor",
            NewValue: value,
            EffectiveFrom: DateTimeOffset.UtcNow,
            Actor: "ops.analyst",
            Justification: "Factor correction.");

        await harness.Service.Invoking(s => s.UpdateSecurityFieldAsync(EditWith("garbage")))
            .Should().ThrowAsync<ArgumentException>("a non-numeric value must not satisfy the profile's Decimal type")
            .WithMessage("*declared type Decimal*");

        await harness.Service.Invoking(s => s.UpdateSecurityFieldAsync(EditWith("1.5")))
            .Should().ThrowAsync<ArgumentException>("the profile bounds currentFactor to [0, 1]")
            .WithMessage("*allowed range*");

        var accepted = await harness.Service.UpdateSecurityFieldAsync(EditWith("0.5"));
        accepted.State.Should().Be(SecurityMasterRevisionStateDto.Draft,
            "a value satisfying the pinned profile's type and range must stage normally");
    }

    [Fact]
    public async Task UpdateSecurityField_ProfileFieldClearWithCasingVariant_RemovesTheCanonicalKey()
    {
        // A clear must remove the CANONICAL override key: clearing profileFields.CurrentFactor and
        // removing a casing-variant key would leave the asserted currentFactor override and its
        // provenance active while the draft claims it was cleared.
        var harness = new Harness(currentVersion: 3);
        harness.SetPassportAssetClass("CustomAsset");
        harness.SetProjectionProfileEnvelope("structured-credit-io-po", profileVersion: 1);

        var request = new UpdateSecurityFieldRequest(
            SecurityId: SecurityId,
            ExpectedVersion: 3,
            FieldPath: "assetSpecificTerms.profileFields.CurrentFactor",
            NewValue: null,
            EffectiveFrom: DateTimeOffset.UtcNow,
            Actor: "ops.analyst",
            Justification: "Withdraw the factor override.");

        await harness.Service.UpdateSecurityFieldAsync(request);

        harness.Overrides.Verify(
            o => o.PatchAsync(
                SecurityId,
                It.Is<OperatorOverridesPatchRequest>(p =>
                    p.RemoveKeys != null
                    && p.RemoveKeys.Contains("assetSpecificTerms.profileFields.currentFactor")),
                "ops.analyst",
                It.IsAny<CancellationToken>(),
                It.IsAny<long?>()),
            Times.Once,
            "the clear must remove the pinned definition's canonical key, not the casing variant");
    }

    [Fact]
    public async Task UpdateSecurityField_UnresolvedPinnedProfile_FailsClosed()
    {
        // profileFields values are governed by the pinned profile; when the projection carries no
        // envelope (lag, legacy drift, catalog mismatch) a value edit must be rejected, not staged
        // unvalidated with a draft revision and provenance row. Clears fail closed too: without
        // the pinned definition the path cannot be canonicalized, so clearing a casing variant
        // would remove the wrong key while the stored override and its provenance stay active.
        var harness = new Harness(currentVersion: 3);
        harness.SetPassportAssetClass("CustomAsset");
        // No SetProjectionProfileEnvelope: the pinned profile cannot resolve.

        var request = new UpdateSecurityFieldRequest(
            SecurityId: SecurityId,
            ExpectedVersion: 3,
            FieldPath: "assetSpecificTerms.profileFields.currentFactor",
            NewValue: "garbage",
            EffectiveFrom: DateTimeOffset.UtcNow,
            Actor: "ops.analyst",
            Justification: "Factor correction.");

        await harness.Service.Invoking(s => s.UpdateSecurityFieldAsync(request))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*pinned asset profile*");

        var clear = request with { NewValue = null, Justification = "Withdraw the factor override." };
        await harness.Service.Invoking(s => s.UpdateSecurityFieldAsync(clear))
            .Should().ThrowAsync<InvalidOperationException>(
                "an uncanonicalized clear could remove the wrong casing's key and leave the asserted value active")
            .WithMessage("*pinned asset profile*");
    }

    [Fact]
    public async Task UpdateSecurityField_SubpathBeneathDeclaredScalarProfileField_IsRejected()
    {
        // Profile field types describe scalar values; a deeper path under a declared field would
        // bypass its type/range validation and stage an undeclared override.
        var harness = new Harness(currentVersion: 3);
        harness.SetPassportAssetClass("CustomAsset");
        harness.SetProjectionProfileEnvelope("structured-credit-io-po", profileVersion: 1);

        var request = new UpdateSecurityFieldRequest(
            SecurityId: SecurityId,
            ExpectedVersion: 3,
            FieldPath: "assetSpecificTerms.profileFields.currentFactor.unit",
            NewValue: "percent",
            EffectiveFrom: DateTimeOffset.UtcNow,
            Actor: "ops.analyst",
            Justification: "Factor unit annotation.");

        await harness.Service.Invoking(s => s.UpdateSecurityFieldAsync(request))
            .Should().ThrowAsync<ArgumentException>()
            .WithMessage("*no structured children*");
    }

    [Fact]
    public async Task UpdateSecurityField_ProfileFieldsReplacement_EnforcesDateOrderRules()
    {
        // The pinned profile's complete rule set includes cross-field date ordering: individually
        // valid dates in reverse order violate the profile just as a mistyped value does.
        var datedProfile = new SecurityAssetProfileDefinitionDto(
            "dated-profile",
            1,
            "Dated Profile",
            "PrivateFunds",
            null,
            SecurityAssetProfileStatusDto.Approved,
            [
                new SecurityAssetProfileFieldDefinitionDto(
                    "startDate", "Start date", SecurityAssetProfileFieldTypeDto.Date, false, [], null, null, null, false, false),
                new SecurityAssetProfileFieldDefinitionDto(
                    "endDate", "End date", SecurityAssetProfileFieldTypeDto.Date, false, [], null, null, null, false, false)
            ],
            [],
            ["Active"],
            [],
            [new SecurityAssetProfileDateOrderRuleDto("startDate", "endDate", "PF_DATE_ORDER", "startDate must be on or before endDate.")],
            new DateOnly(2026, 1, 1),
            null,
            "governance.lead",
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            "test profile");
        var harness = new Harness(
            currentVersion: 3,
            assetProfileCatalog: new Meridian.ReferenceData.SecurityMaster.StaticSecurityAssetProfileCatalog([datedProfile]));
        harness.SetPassportAssetClass("CustomAsset");
        harness.SetProjectionProfileEnvelope("dated-profile", profileVersion: 1);

        var request = new UpdateSecurityFieldRequest(
            SecurityId: SecurityId,
            ExpectedVersion: 3,
            FieldPath: "assetSpecificTerms.profileFields",
            NewValue: """{"startDate":"2026-06-01","endDate":"2026-01-01"}""",
            EffectiveFrom: DateTimeOffset.UtcNow,
            Actor: "ops.analyst",
            Justification: "Date correction.");

        await harness.Service.Invoking(s => s.UpdateSecurityFieldAsync(request))
            .Should().ThrowAsync<ArgumentException>()
            .WithMessage("*PF_DATE_ORDER*");
    }

    [Fact]
    public async Task UpdateSecurityField_ScalarDateEdit_EnforcesDateOrderAgainstRetainedDates()
    {
        // A scalar date edit participates in the pinned profile's cross-field ordering exactly as
        // a whole-object replacement does: moving startDate after the RETAINED endDate must not
        // stage a draft and provenance row behind a contract the object replacement rejects.
        var datedProfile = new SecurityAssetProfileDefinitionDto(
            "dated-profile",
            1,
            "Dated Profile",
            "PrivateFunds",
            null,
            SecurityAssetProfileStatusDto.Approved,
            [
                new SecurityAssetProfileFieldDefinitionDto(
                    "startDate", "Start date", SecurityAssetProfileFieldTypeDto.Date, false, [], null, null, null, false, false),
                new SecurityAssetProfileFieldDefinitionDto(
                    "endDate", "End date", SecurityAssetProfileFieldTypeDto.Date, false, [], null, null, null, false, false)
            ],
            [],
            ["Active"],
            [],
            [new SecurityAssetProfileDateOrderRuleDto("startDate", "endDate", "PF_DATE_ORDER", "startDate must be on or before endDate.")],
            new DateOnly(2026, 1, 1),
            null,
            "governance.lead",
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            "test profile");
        var harness = new Harness(
            currentVersion: 3,
            assetProfileCatalog: new Meridian.ReferenceData.SecurityMaster.StaticSecurityAssetProfileCatalog([datedProfile]));
        harness.SetPassportAssetClass("CustomAsset");
        harness.SetProjectionProfileEnvelope(
            "dated-profile", profileVersion: 1,
            profileFields: new { startDate = "2026-01-01", endDate = "2026-03-01" });

        UpdateSecurityFieldRequest EditWith(string fieldPath, string value) => new(
            SecurityId: SecurityId,
            ExpectedVersion: 3,
            FieldPath: fieldPath,
            NewValue: value,
            EffectiveFrom: DateTimeOffset.UtcNow,
            Actor: "ops.analyst",
            Justification: "Date correction.");

        await harness.Service.Invoking(s => s.UpdateSecurityFieldAsync(
                EditWith("assetSpecificTerms.profileFields.startDate", "2026-06-01")))
            .Should().ThrowAsync<ArgumentException>("startDate would land after the retained endDate")
            .WithMessage("*PF_DATE_ORDER*");

        var staged = await harness.Service.UpdateSecurityFieldAsync(
            EditWith("assetSpecificTerms.profileFields.endDate", "2026-06-01"));
        staged.State.Should().Be(SecurityMasterRevisionStateDto.Draft,
            "moving endDate after the retained startDate satisfies the ordering");
    }

    [Fact]
    public async Task UpdateSecurityField_ScalarDateEdit_ValidatesAgainstStagedOverrides()
    {
        // The counterpart date must come from the EFFECTIVE overlay: with canonical endDate in
        // December but a STAGED override moving it to February, an edit of startDate to March must
        // be judged against February — validating against the superseded canonical value would let
        // two individually plausible edits stage a start-after-end overlay.
        var datedProfile = new SecurityAssetProfileDefinitionDto(
            "dated-profile",
            1,
            "Dated Profile",
            "PrivateFunds",
            null,
            SecurityAssetProfileStatusDto.Approved,
            [
                new SecurityAssetProfileFieldDefinitionDto(
                    "startDate", "Start date", SecurityAssetProfileFieldTypeDto.Date, false, [], null, null, null, false, false),
                new SecurityAssetProfileFieldDefinitionDto(
                    "endDate", "End date", SecurityAssetProfileFieldTypeDto.Date, false, [], null, null, null, false, false)
            ],
            [],
            ["Active"],
            [],
            [new SecurityAssetProfileDateOrderRuleDto("startDate", "endDate", "PF_DATE_ORDER", "startDate must be on or before endDate.")],
            new DateOnly(2026, 1, 1),
            null,
            "governance.lead",
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            "test profile");
        var harness = new Harness(
            currentVersion: 3,
            assetProfileCatalog: new Meridian.ReferenceData.SecurityMaster.StaticSecurityAssetProfileCatalog([datedProfile]));
        harness.SetPassportAssetClass("CustomAsset");
        harness.SetProjectionProfileEnvelope(
            "dated-profile", profileVersion: 1,
            profileFields: new { startDate = "2026-01-01", endDate = "2026-12-01" });
        harness.Overrides
            .Setup(o => o.GetAsync(SecurityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperatorOverridesDto(
                SecurityId,
                new Dictionary<string, string>
                {
                    ["assetSpecificTerms.profileFields.endDate"] = "2026-02-01"
                },
                "ops.analyst",
                DateTimeOffset.UtcNow.AddMinutes(-5)));

        var request = new UpdateSecurityFieldRequest(
            SecurityId: SecurityId,
            ExpectedVersion: 3,
            FieldPath: "assetSpecificTerms.profileFields.startDate",
            NewValue: "2026-03-01",
            EffectiveFrom: DateTimeOffset.UtcNow,
            Actor: "ops.analyst",
            Justification: "Date correction.");

        await harness.Service.Invoking(s => s.UpdateSecurityFieldAsync(request))
            .Should().ThrowAsync<ArgumentException>(
                "the staged February endDate override is the effective counterpart, not canonical December")
            .WithMessage("*PF_DATE_ORDER*");
    }

    [Fact]
    public async Task UpdateSecurityField_StagedOverlayUnreadable_RejectsTheEditInsteadOfCanonicalFallback()
    {
        // FAIL-CLOSED: when the staged overlay cannot be LOADED, validating against canonical
        // values only could contradict an already staged counterpart (e.g. a February endDate
        // hidden by the read failure while a March startDate is accepted), leaving the stored
        // overlay violating the pinned profile. The edit must be rejected, not silently validated
        // against a partial view.
        var datedProfile = new SecurityAssetProfileDefinitionDto(
            "dated-profile",
            1,
            "Dated Profile",
            "PrivateFunds",
            null,
            SecurityAssetProfileStatusDto.Approved,
            [
                new SecurityAssetProfileFieldDefinitionDto(
                    "startDate", "Start date", SecurityAssetProfileFieldTypeDto.Date, false, [], null, null, null, false, false),
                new SecurityAssetProfileFieldDefinitionDto(
                    "endDate", "End date", SecurityAssetProfileFieldTypeDto.Date, false, [], null, null, null, false, false)
            ],
            [],
            ["Active"],
            [],
            [new SecurityAssetProfileDateOrderRuleDto("startDate", "endDate", "PF_DATE_ORDER", "startDate must be on or before endDate.")],
            new DateOnly(2026, 1, 1),
            null,
            "governance.lead",
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            "test profile");
        var harness = new Harness(
            currentVersion: 3,
            assetProfileCatalog: new Meridian.ReferenceData.SecurityMaster.StaticSecurityAssetProfileCatalog([datedProfile]));
        harness.SetPassportAssetClass("CustomAsset");
        harness.SetProjectionProfileEnvelope(
            "dated-profile", profileVersion: 1,
            profileFields: new { startDate = "2026-01-01", endDate = "2026-12-01" });
        harness.Overrides
            .Setup(o => o.GetAsync(SecurityId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("overlay store down"));

        var request = new UpdateSecurityFieldRequest(
            SecurityId: SecurityId,
            ExpectedVersion: 3,
            FieldPath: "assetSpecificTerms.profileFields.startDate",
            NewValue: "2026-03-01",
            EffectiveFrom: DateTimeOffset.UtcNow,
            Actor: "ops.analyst",
            Justification: "Date correction.");

        await harness.Service.Invoking(s => s.UpdateSecurityFieldAsync(request))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*could not be loaded*");
        harness.Overrides.Verify(
            o => o.PatchAsync(
                It.IsAny<Guid>(),
                It.IsAny<OperatorOverridesPatchRequest>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<long?>()),
            Times.Never);
    }

    [Fact]
    public async Task UpdateSecurityField_ScalarDateEdit_ValidatesAgainstStagedWholeObjectReplacement()
    {
        // A staged WHOLE-OBJECT profileFields replacement is the effective overlay too: after
        // staging a replacement whose endDate is February, a scalar startDate=March edit must be
        // judged against February — falling through to the canonical December end date would let
        // the overlay serialize into start-after-end order.
        var datedProfile = new SecurityAssetProfileDefinitionDto(
            "dated-profile",
            1,
            "Dated Profile",
            "PrivateFunds",
            null,
            SecurityAssetProfileStatusDto.Approved,
            [
                new SecurityAssetProfileFieldDefinitionDto(
                    "startDate", "Start date", SecurityAssetProfileFieldTypeDto.Date, false, [], null, null, null, false, false),
                new SecurityAssetProfileFieldDefinitionDto(
                    "endDate", "End date", SecurityAssetProfileFieldTypeDto.Date, false, [], null, null, null, false, false)
            ],
            [],
            ["Active"],
            [],
            [new SecurityAssetProfileDateOrderRuleDto("startDate", "endDate", "PF_DATE_ORDER", "startDate must be on or before endDate.")],
            new DateOnly(2026, 1, 1),
            null,
            "governance.lead",
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            "test profile");
        var harness = new Harness(
            currentVersion: 3,
            assetProfileCatalog: new Meridian.ReferenceData.SecurityMaster.StaticSecurityAssetProfileCatalog([datedProfile]));
        harness.SetPassportAssetClass("CustomAsset");
        harness.SetProjectionProfileEnvelope(
            "dated-profile", profileVersion: 1,
            profileFields: new { startDate = "2026-01-01", endDate = "2026-12-01" });
        harness.Overrides
            .Setup(o => o.GetAsync(SecurityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperatorOverridesDto(
                SecurityId,
                new Dictionary<string, string>
                {
                    ["assetSpecificTerms.profileFields"] = """{"startDate":"2026-01-01","endDate":"2026-02-01"}"""
                },
                "ops.analyst",
                DateTimeOffset.UtcNow.AddMinutes(-5)));

        var request = new UpdateSecurityFieldRequest(
            SecurityId: SecurityId,
            ExpectedVersion: 3,
            FieldPath: "assetSpecificTerms.profileFields.startDate",
            NewValue: "2026-03-01",
            EffectiveFrom: DateTimeOffset.UtcNow,
            Actor: "ops.analyst",
            Justification: "Date correction.");

        await harness.Service.Invoking(s => s.UpdateSecurityFieldAsync(request))
            .Should().ThrowAsync<ArgumentException>(
                "the staged whole-object replacement's February endDate is the effective counterpart")
            .WithMessage("*PF_DATE_ORDER*");
    }

    [Fact]
    public async Task UpdateSecurityField_ProfileFieldsReplacement_ResolvesDeclaredKeysCaseInsensitively()
    {
        // A whole-object replacement must match declared keys the way scalar edits and downstream
        // readers do — case-insensitively. An exact-case loop would let "Maturity":"not-a-date"
        // bypass the declared optional maturity field's Date validation and stage an invalid
        // value; two casings of the same declared key are ambiguous and rejected outright.
        var harness = new Harness(currentVersion: 3);
        harness.SetPassportAssetClass("CustomAsset");
        harness.SetProjectionProfileEnvelope("structured-credit-io-po", profileVersion: 1);

        UpdateSecurityFieldRequest ReplaceWith(string json) => new(
            SecurityId: SecurityId,
            ExpectedVersion: 3,
            FieldPath: "assetSpecificTerms.profileFields",
            NewValue: json,
            EffectiveFrom: DateTimeOffset.UtcNow,
            Actor: "ops.analyst",
            Justification: "Profile fields replacement.");

        const string requiredFields =
            "\"tranche\":\"A-1\",\"poolId\":\"POOL-1\",\"currentFactor\":0.5,\"originalFace\":1000000," +
            "\"couponOrIndex\":\"SOFR+250\",\"factorSchedule\":\"trustee\",\"collateralType\":\"CLO\"";

        await harness.Service.Invoking(s => s.UpdateSecurityFieldAsync(ReplaceWith(
                "{" + requiredFields + ",\"Maturity\":\"not-a-date\"}")))
            .Should().ThrowAsync<ArgumentException>("a miscased declared key must still hit its Date validation")
            .WithMessage("*Date*");

        await harness.Service.Invoking(s => s.UpdateSecurityFieldAsync(ReplaceWith(
                "{" + requiredFields + ",\"maturity\":\"2031-06-15\",\"Maturity\":\"2032-06-15\"}")))
            .Should().ThrowAsync<ArgumentException>("two casings of one declared key are ambiguous")
            .WithMessage("*multiple casings*");

        var staged = await harness.Service.UpdateSecurityFieldAsync(ReplaceWith(
            "{" + requiredFields + ",\"Maturity\":\"2031-06-15\"}"));
        staged.State.Should().Be(SecurityMasterRevisionStateDto.Draft,
            "a miscased but VALID optional value passes the declared field's rules");
    }

    [Fact]
    public async Task UpdateSecurityField_ProfileFieldsReplacement_ValidatesDateOrderAgainstStagedScalarOverrides()
    {
        // A staged PER-FIELD override outranks a whole-object replacement in the effective
        // overlay: with endDate staged to February, replacing the object with an internally valid
        // March→December pair still reads back as March-after-February after approval. The
        // replacement must be judged against the effective overlay, not its own pair alone.
        var datedProfile = new SecurityAssetProfileDefinitionDto(
            "dated-profile",
            1,
            "Dated Profile",
            "PrivateFunds",
            null,
            SecurityAssetProfileStatusDto.Approved,
            [
                new SecurityAssetProfileFieldDefinitionDto(
                    "startDate", "Start date", SecurityAssetProfileFieldTypeDto.Date, false, [], null, null, null, false, false),
                new SecurityAssetProfileFieldDefinitionDto(
                    "endDate", "End date", SecurityAssetProfileFieldTypeDto.Date, false, [], null, null, null, false, false)
            ],
            [],
            ["Active"],
            [],
            [new SecurityAssetProfileDateOrderRuleDto("startDate", "endDate", "PF_DATE_ORDER", "startDate must be on or before endDate.")],
            new DateOnly(2026, 1, 1),
            null,
            "governance.lead",
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            "test profile");
        var harness = new Harness(
            currentVersion: 3,
            assetProfileCatalog: new Meridian.ReferenceData.SecurityMaster.StaticSecurityAssetProfileCatalog([datedProfile]));
        harness.SetPassportAssetClass("CustomAsset");
        harness.SetProjectionProfileEnvelope(
            "dated-profile", profileVersion: 1,
            profileFields: new { startDate = "2026-01-01", endDate = "2026-12-01" });
        harness.Overrides
            .Setup(o => o.GetAsync(SecurityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperatorOverridesDto(
                SecurityId,
                new Dictionary<string, string>
                {
                    ["assetSpecificTerms.profileFields.endDate"] = "2026-02-01"
                },
                "ops.analyst",
                DateTimeOffset.UtcNow.AddMinutes(-5)));

        var request = new UpdateSecurityFieldRequest(
            SecurityId: SecurityId,
            ExpectedVersion: 3,
            FieldPath: "assetSpecificTerms.profileFields",
            NewValue: """{"startDate":"2026-03-01","endDate":"2026-12-01"}""",
            EffectiveFrom: DateTimeOffset.UtcNow,
            Actor: "ops.analyst",
            Justification: "Date correction.");

        await harness.Service.Invoking(s => s.UpdateSecurityFieldAsync(request))
            .Should().ThrowAsync<ArgumentException>(
                "the staged February endDate override outranks the replacement's December value in the effective overlay")
            .WithMessage("*PF_DATE_ORDER*");
    }

    [Fact]
    public async Task UpdateSecurityField_ProfileFieldEditOnResolvedClass_EnforcesResolvedKindInvariants()
    {
        // The seeded private-fund-interest profile permits commitment = 0 at the profile level,
        // but a record RESOLVED to PrivateFundInterest is bound by the canonical kind invariant
        // requiring a strictly positive commitment. A profile-valid edit that violates the
        // resolved class's invariants must not stage an overlay the canonical amend seam rejects.
        var harness = new Harness(currentVersion: 3);
        harness.SetPassportAssetClass("PrivateFundInterest");
        harness.SetProjectionProfileEnvelope(
            "private-fund-interest", profileVersion: 1,
            profileFields: new
            {
                gpSponsor = "Apex GP",
                strategy = "Buyout",
                vintage = 2024,
                commitment = 1_000_000m,
                fundedAmount = 250_000m,
                unfundedAmount = 750_000m,
                navDate = "2026-06-30"
            },
            assetClass: "PrivateFundInterest");

        UpdateSecurityFieldRequest EditWith(string value) => new(
            SecurityId: SecurityId,
            ExpectedVersion: 3,
            FieldPath: "assetSpecificTerms.profileFields.commitment",
            NewValue: value,
            EffectiveFrom: DateTimeOffset.UtcNow,
            Actor: "ops.analyst",
            Justification: "Commitment correction.");

        await harness.Service.Invoking(s => s.UpdateSecurityFieldAsync(EditWith("0")))
            .Should().ThrowAsync<ArgumentException>(
                "the profile permits zero but the resolved PrivateFundInterest kind requires a positive commitment")
            .WithMessage("*private_fund_commitment_invalid*");

        var staged = await harness.Service.UpdateSecurityFieldAsync(EditWith("500000"));
        staged.State.Should().Be(SecurityMasterRevisionStateDto.Draft,
            "a positive commitment satisfies both the profile and the resolved kind invariants");
    }

    [Fact]
    public async Task UpdateSecurityField_ProfileFieldsReplacementOnResolvedClass_EnforcesResolvedKindInvariants()
    {
        // A whole-object replacement is bound by the resolved class's invariants too: a
        // replacement that satisfies every declared profile field but zeroes the commitment must
        // be rejected before staging, not at the later canonical amend.
        var harness = new Harness(currentVersion: 3);
        harness.SetPassportAssetClass("PrivateFundInterest");
        harness.SetProjectionProfileEnvelope(
            "private-fund-interest", profileVersion: 1,
            profileFields: new
            {
                gpSponsor = "Apex GP",
                strategy = "Buyout",
                vintage = 2024,
                commitment = 1_000_000m,
                fundedAmount = 250_000m,
                unfundedAmount = 750_000m,
                navDate = "2026-06-30"
            },
            assetClass: "PrivateFundInterest");

        var request = new UpdateSecurityFieldRequest(
            SecurityId: SecurityId,
            ExpectedVersion: 3,
            FieldPath: "assetSpecificTerms.profileFields",
            NewValue:
                """{"gpSponsor":"Apex GP","strategy":"Buyout","vintage":2024,"commitment":0,"fundedAmount":0,"unfundedAmount":0,"navDate":"2026-06-30"}""",
            EffectiveFrom: DateTimeOffset.UtcNow,
            Actor: "ops.analyst",
            Justification: "Profile fields replacement.");

        await harness.Service.Invoking(s => s.UpdateSecurityFieldAsync(request))
            .Should().ThrowAsync<ArgumentException>()
            .WithMessage("*private_fund_commitment_invalid*");
    }

    [Fact]
    public async Task UpdateSecurityField_PrincipalScheduleReplacement_ValidatesAgainstRetainedBondTerms()
    {
        // Row-local shape validation alone lets a positive instalment after maturity, or
        // instalments totaling more than par, stage cleanly and only fail at the later canonical
        // amend. The replacement must be validated against the record's RETAINED issue/maturity
        // window and principal face before staging.
        var harness = new Harness(currentVersion: 3);
        harness.SetPassportAssetClass("Bond");
        harness.SetProjectionAssetTerms("Bond", new
        {
            issueDate = "2026-01-01",
            maturityDate = "2030-01-01",
            par = 100m
        });

        UpdateSecurityFieldRequest ReplaceWith(string json) => new(
            SecurityId: SecurityId,
            ExpectedVersion: 3,
            FieldPath: "assetSpecificTerms.principalSchedule",
            NewValue: json,
            EffectiveFrom: DateTimeOffset.UtcNow,
            Actor: "ops.analyst",
            Justification: "Schedule correction.");

        await harness.Service.Invoking(s => s.UpdateSecurityFieldAsync(ReplaceWith(
                """[{"paymentDate":"2031-06-15","amount":10}]""")))
            .Should().ThrowAsync<ArgumentException>("an instalment after the retained maturity cannot stage")
            .WithMessage("*retained maturity date*");

        await harness.Service.Invoking(s => s.UpdateSecurityFieldAsync(ReplaceWith(
                """[{"paymentDate":"2025-06-15","amount":10}]""")))
            .Should().ThrowAsync<ArgumentException>("an instalment before the retained issue date cannot stage")
            .WithMessage("*retained issue date*");

        await harness.Service.Invoking(s => s.UpdateSecurityFieldAsync(ReplaceWith(
                """[{"paymentDate":"2027-06-15","amount":60},{"paymentDate":"2028-06-15","amount":50}]""")))
            .Should().ThrowAsync<ArgumentException>("instalments summing past the retained principal face cannot stage")
            .WithMessage("*principal face*");

        var staged = await harness.Service.UpdateSecurityFieldAsync(ReplaceWith(
            """[{"paymentDate":"2027-06-15","amount":60},{"paymentDate":"2028-06-15","amount":40}]"""));
        staged.State.Should().Be(SecurityMasterRevisionStateDto.Draft,
            "a schedule inside the retained window and within par stages normally");
    }

    [Fact]
    public async Task UpdateSecurityField_PrincipalScheduleReplacement_FailsClosedWithoutRetainedTerms()
    {
        // When the projection cannot be loaded the replacement cannot be validated against the
        // retained window and par — the reserved namespace only accepts validated writes.
        var harness = new Harness(currentVersion: 3);
        harness.SetPassportAssetClass("Bond");
        // No SetProjectionAssetTerms: the projection store resolves nothing.

        var request = new UpdateSecurityFieldRequest(
            SecurityId: SecurityId,
            ExpectedVersion: 3,
            FieldPath: "assetSpecificTerms.principalSchedule",
            NewValue: """[{"paymentDate":"2027-06-15","amount":10}]""",
            EffectiveFrom: DateTimeOffset.UtcNow,
            Actor: "ops.analyst",
            Justification: "Schedule correction.");

        await harness.Service.Invoking(s => s.UpdateSecurityFieldAsync(request))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*retained terms*");
    }

    [Fact]
    public async Task UpdateSecurityField_PrincipalScheduleReplacement_ValidatesAgainstStagedTermOverrides()
    {
        // The window and par bind from the EFFECTIVE overlay: with par staged down to 50, a
        // replacement totaling 80 violates the Bond invariant even though canonical par is 100 —
        // validating against the superseded canonical value would let the overlay overpay par.
        var harness = new Harness(currentVersion: 3);
        harness.SetPassportAssetClass("Bond");
        harness.SetProjectionAssetTerms("Bond", new
        {
            issueDate = "2026-01-01",
            maturityDate = "2030-01-01",
            par = 100m
        });
        harness.Overrides
            .Setup(o => o.GetAsync(SecurityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperatorOverridesDto(
                SecurityId,
                new Dictionary<string, string> { ["assetSpecificTerms.par"] = "50" },
                "ops.analyst",
                DateTimeOffset.UtcNow.AddMinutes(-5)));

        var request = new UpdateSecurityFieldRequest(
            SecurityId: SecurityId,
            ExpectedVersion: 3,
            FieldPath: "assetSpecificTerms.principalSchedule",
            NewValue: """[{"paymentDate":"2027-06-15","amount":50},{"paymentDate":"2028-06-15","amount":30}]""",
            EffectiveFrom: DateTimeOffset.UtcNow,
            Actor: "ops.analyst",
            Justification: "Schedule correction.");

        await harness.Service.Invoking(s => s.UpdateSecurityFieldAsync(request))
            .Should().ThrowAsync<ArgumentException>(
                "the staged par override of 50 is the effective principal face, not the canonical 100")
            .WithMessage("*principal face*");
    }

    [Fact]
    public async Task UpdateSecurityField_FirstClassTermEdit_RunsResolvedKindInvariants()
    {
        // "2" is a perfectly typed decimal, but the canonical StructuredCredit contract bounds
        // CurrentFactor to [0, 1] - the edit the equivalent canonical amendment rejects must not
        // stage, submit, and approve through the workbench route either.
        var harness = new Harness(currentVersion: 3);
        harness.SetPassportAssetClass("StructuredCredit");
        harness.SetProjectionAssetTerms("StructuredCredit", new
        {
            tranche = "A-1",
            collateralType = "CLO",
            originalFace = 100m,
            couponOrIndex = "SOFR+250",
            currentFactor = 0.9m
        });

        var request = new UpdateSecurityFieldRequest(
            SecurityId: SecurityId,
            ExpectedVersion: 3,
            FieldPath: "assetSpecificTerms.currentFactor",
            NewValue: "2",
            EffectiveFrom: DateTimeOffset.UtcNow,
            Actor: "ops.analyst",
            Justification: "Factor correction.");

        await harness.Service.Invoking(s => s.UpdateSecurityFieldAsync(request))
            .Should().ThrowAsync<ArgumentException>()
            .WithMessage("*domain invariants*");
    }

    [Fact]
    public async Task UpdateSecurityField_KindInvariantProjectionReadFails_RejectsTheEdit()
    {
        // A transient projection-store failure must not skip the resolved-kind invariant check:
        // the passport already resolved a first-class class, so a type-correct but
        // invariant-violating value would stage unvalidated for the whole outage.
        var harness = new Harness(currentVersion: 3);
        harness.SetPassportAssetClass("StructuredCredit");
        harness.ProjectionStore
            .Setup(p => p.GetProjectionAsync(SecurityId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("projection store down"));

        var request = new UpdateSecurityFieldRequest(
            SecurityId: SecurityId,
            ExpectedVersion: 3,
            FieldPath: "assetSpecificTerms.currentFactor",
            NewValue: "0.5",
            EffectiveFrom: DateTimeOffset.UtcNow,
            Actor: "ops.analyst",
            Justification: "Factor correction.");

        await harness.Service.Invoking(s => s.UpdateSecurityFieldAsync(request))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*domain invariants*");
        harness.Overrides.Verify(
            o => o.PatchAsync(
                It.IsAny<Guid>(),
                It.IsAny<OperatorOverridesPatchRequest>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<long?>()),
            Times.Never);
    }

    [Fact]
    public async Task UpdateSecurityField_ClearingBoundTermOverride_RevalidatesEffectiveSchedule()
    {
        // Clearing a bound term is an edit to the effective overlay too: with par staged UP to 100
        // and a staged schedule totaling 80, clearing the par override reverts the effective par
        // to the canonical 50 — the staged schedule then exceeds it, so the clear must be refused
        // instead of leaving an approvable overlay that violates the principal cap.
        var harness = new Harness(currentVersion: 3);
        harness.SetPassportAssetClass("Bond");
        harness.SetProjectionAssetTerms("Bond", new
        {
            issueDate = "2026-01-01",
            maturityDate = "2030-01-01",
            par = 50m
        });
        harness.Overrides
            .Setup(o => o.GetAsync(SecurityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperatorOverridesDto(
                SecurityId,
                new Dictionary<string, string>
                {
                    ["assetSpecificTerms.par"] = "100",
                    ["assetSpecificTerms.principalSchedule"] =
                        """[{"paymentDate":"2027-06-15","amount":50},{"paymentDate":"2028-06-15","amount":30}]"""
                },
                "ops.analyst",
                DateTimeOffset.UtcNow.AddMinutes(-5)));

        var request = new UpdateSecurityFieldRequest(
            SecurityId: SecurityId,
            ExpectedVersion: 3,
            FieldPath: "assetSpecificTerms.par",
            NewValue: null,
            EffectiveFrom: DateTimeOffset.UtcNow,
            Actor: "ops.analyst",
            Justification: "Remove par override.");

        await harness.Service.Invoking(s => s.UpdateSecurityFieldAsync(request))
            .Should().ThrowAsync<ArgumentException>(
                "clearing the par override reverts the effective par to the canonical 50, below the staged schedule total of 80")
            .WithMessage("*principal face*");
    }

    [Fact]
    public async Task UpdateSecurityField_ConcurrentEditsOnSameSecurity_SerializeTheValidatePatchWindow()
    {
        // Two concurrent edits to the same security must not both validate against the same
        // pre-edit overlay and then both patch: the validate→patch window is serialized per
        // security, so the store never observes overlapping windows.
        var harness = new Harness(currentVersion: 3);
        harness.SetPassportAssetClass("Bond");
        harness.SetProjectionAssetTerms("Bond", new
        {
            issueDate = "2026-01-01",
            maturityDate = "2030-01-01",
            par = 100m
        });

        // Every store call (overlay reads and the patch) tracks its own in-flight window: with
        // the per-security gate, the two edits' store calls can never overlap, so the maximum
        // observed concurrency across ALL calls must be one.
        var concurrentCalls = 0;
        var maxConcurrentCalls = 0;
        async Task<T> Tracked<T>(Func<T> result)
        {
            var now = Interlocked.Increment(ref concurrentCalls);
            InterlockedMax(ref maxConcurrentCalls, now);
            try
            {
                await Task.Delay(30);
                return result();
            }
            finally
            {
                Interlocked.Decrement(ref concurrentCalls);
            }
        }

        harness.Overrides
            .Setup(o => o.GetAsync(SecurityId, It.IsAny<CancellationToken>()))
            .Returns(() => Tracked(static () => (OperatorOverridesDto?)null));
        harness.Overrides
            .Setup(o => o.PatchAsync(
                It.IsAny<Guid>(), It.IsAny<OperatorOverridesPatchRequest>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<long?>()))
            .Returns((Guid id, OperatorOverridesPatchRequest _, string actor, CancellationToken _, long? _) =>
                Tracked(() => new OperatorOverridesDto(id, new Dictionary<string, string>(), actor, DateTimeOffset.UtcNow)));

        UpdateSecurityFieldRequest EditWith(string value) => new(
            SecurityId: SecurityId,
            ExpectedVersion: 3,
            FieldPath: "assetSpecificTerms.par",
            NewValue: value,
            EffectiveFrom: DateTimeOffset.UtcNow,
            Actor: "ops.analyst",
            Justification: "Par correction.");

        await Task.WhenAll(
            harness.Service.UpdateSecurityFieldAsync(EditWith("60")),
            harness.Service.UpdateSecurityFieldAsync(EditWith("70")));

        maxConcurrentCalls.Should().Be(1,
            "the second edit's overlay reads must not begin until the first edit's patch completed");
    }

    private static void InterlockedMax(ref int target, int candidate)
    {
        int snapshot;
        while (candidate > (snapshot = Volatile.Read(ref target)))
        {
            if (Interlocked.CompareExchange(ref target, candidate, snapshot) == snapshot)
            {
                return;
            }
        }
    }

    [Fact]
    public async Task UpdateSecurityField_WindowTermEdit_ValidatesAgainstEffectivePrincipalSchedule()
    {
        // The reciprocal direction: with a contractual schedule retained on the record, moving
        // maturity before a scheduled instalment (or par below the scheduled total) stages an
        // overlay whose schedule violates the Bond window/par — the same inconsistency the
        // schedule replacement route rejects, reachable in the other staging order.
        var harness = new Harness(currentVersion: 3);
        harness.SetPassportAssetClass("Bond");
        harness.SetProjectionAssetTerms("Bond", new
        {
            issueDate = "2026-01-01",
            maturityDate = "2030-01-01",
            par = 100m,
            principalSchedule = new object[]
            {
                new { paymentDate = "2027-06-15", amount = 60m },
                new { paymentDate = "2028-06-15", amount = 40m }
            }
        });

        UpdateSecurityFieldRequest EditWith(string fieldPath, string value) => new(
            SecurityId: SecurityId,
            ExpectedVersion: 3,
            FieldPath: fieldPath,
            NewValue: value,
            EffectiveFrom: DateTimeOffset.UtcNow,
            Actor: "ops.analyst",
            Justification: "Term correction.");

        await harness.Service.Invoking(s => s.UpdateSecurityFieldAsync(
                EditWith("assetSpecificTerms.maturity", "2028-01-01")))
            .Should().ThrowAsync<ArgumentException>("the retained 2028-06-15 instalment would fall after the proposed maturity")
            .WithMessage("*retained maturity date*");

        await harness.Service.Invoking(s => s.UpdateSecurityFieldAsync(
                EditWith("assetSpecificTerms.par", "80")))
            .Should().ThrowAsync<ArgumentException>("the retained schedule totals 100, above the proposed par of 80")
            .WithMessage("*principal face*");

        var staged = await harness.Service.UpdateSecurityFieldAsync(
            EditWith("assetSpecificTerms.maturity", "2031-06-15"));
        staged.State.Should().Be(SecurityMasterRevisionStateDto.Draft,
            "extending maturity keeps every retained instalment inside the window");
    }

    [Fact]
    public async Task UpdateSecurityField_UndeclaredProfileFieldOnResolvedClass_EnforcesResolvedKindInvariants()
    {
        // The seeded structured-credit-io-po profile does not declare factorScheduleEntries, but
        // the resolved StructuredCredit kind reads exactly that key and enforces factors within
        // [0, 1], unique dates, and non-increasing order. An undeclared key must not be an
        // unrestricted side door around the invariants the canonical command enforces.
        var harness = new Harness(currentVersion: 3);
        harness.SetPassportAssetClass("StructuredCredit");
        harness.SetProjectionProfileEnvelope(
            "structured-credit-io-po", profileVersion: 1,
            profileFields: new
            {
                tranche = "A-1",
                poolId = "POOL-1",
                currentFactor = 0.5m,
                originalFace = 1_000_000m,
                couponOrIndex = "SOFR+250",
                factorSchedule = "trustee",
                collateralType = "CLO"
            },
            assetClass: "StructuredCredit");

        UpdateSecurityFieldRequest EditWith(string value) => new(
            SecurityId: SecurityId,
            ExpectedVersion: 3,
            FieldPath: "assetSpecificTerms.profileFields.factorScheduleEntries",
            NewValue: value,
            EffectiveFrom: DateTimeOffset.UtcNow,
            Actor: "ops.analyst",
            Justification: "Factor schedule correction.");

        await harness.Service.Invoking(s => s.UpdateSecurityFieldAsync(EditWith(
                """[{"asOfDate":"2026-01-01","factor":0.5},{"asOfDate":"2026-02-01","factor":0.8}]""")))
            .Should().ThrowAsync<ArgumentException>("a rising factor schedule violates the StructuredCredit invariants")
            .WithMessage("*structured_credit_factor_schedule_not_monotonic*");

        await harness.Service.Invoking(s => s.UpdateSecurityFieldAsync(EditWith("garbage")))
            .Should().ThrowAsync<ArgumentException>("a non-array value cannot reconstruct into the resolved kind and fails closed")
            .WithMessage("*only accepts validated writes*");

        var staged = await harness.Service.UpdateSecurityFieldAsync(EditWith(
            """[{"asOfDate":"2026-01-01","factor":0.8},{"asOfDate":"2026-02-01","factor":0.5}]"""));
        staged.State.Should().Be(SecurityMasterRevisionStateDto.Draft,
            "a non-increasing in-range schedule satisfies the resolved kind's invariants");
    }

    [Fact]
    public async Task UpdateSecurityField_NestedEditBeneathUndeclaredResolvedKindField_IsRejected()
    {
        // A nested value edit beneath an undeclared structured field the RESOLVED kind owns
        // (profileFields.factorScheduleEntries.0.factor = 2 on StructuredCredit) cannot be
        // validated against the kind's schedule-wide invariants, while the equivalent whole-array
        // replacement runs them — the nested route must not stay an unvalidated side door.
        var harness = new Harness(currentVersion: 3);
        harness.SetPassportAssetClass("StructuredCredit");
        harness.SetProjectionProfileEnvelope(
            "structured-credit-io-po", profileVersion: 1,
            profileFields: new
            {
                tranche = "A-1",
                poolId = "POOL-1",
                currentFactor = 0.5m,
                originalFace = 1_000_000m,
                couponOrIndex = "SOFR+250",
                factorSchedule = "trustee",
                collateralType = "CLO"
            },
            assetClass: "StructuredCredit");

        var valueEdit = new UpdateSecurityFieldRequest(
            SecurityId: SecurityId,
            ExpectedVersion: 3,
            FieldPath: "assetSpecificTerms.profileFields.factorScheduleEntries.0.factor",
            NewValue: "2",
            EffectiveFrom: DateTimeOffset.UtcNow,
            Actor: "ops.analyst",
            Justification: "Factor row correction.");

        await harness.Service.Invoking(s => s.UpdateSecurityFieldAsync(valueEdit))
            .Should().ThrowAsync<ArgumentException>()
            .WithMessage("*replace the whole*");

        var clear = valueEdit with { NewValue = null, Justification = "Remove junk subpath override." };
        var cleared = await harness.Service.UpdateSecurityFieldAsync(clear);
        cleared.State.Should().Be(SecurityMasterRevisionStateDto.Draft,
            "a clear removes overlay junk rather than asserting an unvalidatable value");
    }

    [Fact]
    public async Task UpdateSecurityField_ProfileFieldCasingVariant_PersistsUnderThePinnedProfileKey()
    {
        // The pinned-profile lookup is case-insensitive, so the persisted path must be rebuilt from
        // the profile definition's key — otherwise CurrentFactor and currentFactor fork the same
        // field into separate overrides, revisions, and provenance rows.
        var harness = new Harness(currentVersion: 3);
        harness.SetPassportAssetClass("CustomAsset");
        harness.SetProjectionProfileEnvelope("structured-credit-io-po", profileVersion: 1);

        var request = new UpdateSecurityFieldRequest(
            SecurityId: SecurityId,
            ExpectedVersion: 3,
            FieldPath: "assetSpecificTerms.profileFields.CurrentFactor",
            NewValue: "0.5",
            EffectiveFrom: DateTimeOffset.UtcNow,
            Actor: "ops.analyst",
            Justification: "Factor correction.");

        await harness.Service.UpdateSecurityFieldAsync(request);

        harness.Overrides.Verify(
            o => o.PatchAsync(
                SecurityId,
                It.Is<OperatorOverridesPatchRequest>(p =>
                    p.SetValues != null
                    && p.SetValues.ContainsKey("assetSpecificTerms.profileFields.currentFactor")),
                "ops.analyst",
                It.IsAny<CancellationToken>(),
                It.IsAny<long?>()),
            Times.Once,
            "the override must persist under the pinned profile definition's key spelling");
    }

    [Fact]
    public async Task UpdateSecurityField_UnresolvableAssetClass_FailsClosed()
    {
        // The assetSpecificTerms namespace only accepts validated writes; a read-model outage must
        // not become the window in which unvalidated values slip through. Clears fail closed too:
        // without the schema the path cannot be canonicalized, so clearing an alias spelling would
        // remove the wrong key while the canonical override stays active.
        var harness = new Harness(currentVersion: 3);
        // No SetPassportAssetClass: the passport read model resolves nothing.

        var valueEdit = new UpdateSecurityFieldRequest(
            SecurityId: SecurityId,
            ExpectedVersion: 3,
            FieldPath: "assetSpecificTerms.couponRate",
            NewValue: "4.25",
            EffectiveFrom: DateTimeOffset.UtcNow,
            Actor: "ops.analyst",
            Justification: "Coupon correction.");

        await harness.Service.Invoking(s => s.UpdateSecurityFieldAsync(valueEdit))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*could not be resolved*");

        var clear = valueEdit with { NewValue = null, Justification = "Withdraw the coupon override." };
        await harness.Service.Invoking(s => s.UpdateSecurityFieldAsync(clear))
            .Should().ThrowAsync<InvalidOperationException>(
                "an uncanonicalized clear could remove the wrong overlay key and leave the asserted value active")
            .WithMessage("*could not be resolved*");
    }

    [Fact]
    public async Task UpdateSecurityField_ProfileFieldsReplacement_EnforcesRequiredProfileFields()
    {
        // A whole-object replacement REPLACES profileFields, so it must satisfy the pinned
        // profile's complete rules: "{}" would strip every required field yet stage cleanly, and a
        // blank required text is as invalid as a missing one.
        var harness = new Harness(currentVersion: 3);
        harness.SetPassportAssetClass("CustomAsset");
        harness.SetProjectionProfileEnvelope("structured-credit-io-po", profileVersion: 1);

        UpdateSecurityFieldRequest ReplaceWith(string json) => new(
            SecurityId: SecurityId,
            ExpectedVersion: 3,
            FieldPath: "assetSpecificTerms.profileFields",
            NewValue: json,
            EffectiveFrom: DateTimeOffset.UtcNow,
            Actor: "ops.analyst",
            Justification: "Profile fields replacement.");

        await harness.Service.Invoking(s => s.UpdateSecurityFieldAsync(ReplaceWith("{}")))
            .Should().ThrowAsync<ArgumentException>("an empty replacement strips every required profile field")
            .WithMessage("*omits required profile field*");

        await harness.Service.Invoking(s => s.UpdateSecurityFieldAsync(ReplaceWith(
                """{"tranche":"  ","poolId":"POOL-1","currentFactor":0.5,"originalFace":1000000,"couponOrIndex":"SOFR+250","factorSchedule":"trustee","collateralType":"CLO"}""")))
            .Should().ThrowAsync<ArgumentException>("a blank required text field strips the value while passing the kind check")
            .WithMessage("*tranche*");
    }

    [Fact]
    public async Task UpdateSecurityField_BlankValue_RemovesTheOverlayKeyInsteadOfStoringEmpty()
    {
        // A blank edit is a CLEAR: persisting an empty-string override would bypass type
        // validation and read as an asserted value downstream.
        var harness = new Harness(currentVersion: 3);
        harness.SetPassportAssetClass("Bond");

        var request = new UpdateSecurityFieldRequest(
            SecurityId: SecurityId,
            ExpectedVersion: 3,
            FieldPath: "assetSpecificTerms.couponRate",
            NewValue: "   ",
            EffectiveFrom: DateTimeOffset.UtcNow,
            Actor: "ops.analyst",
            Justification: "Withdraw the staged coupon override.");

        var result = await harness.Service.UpdateSecurityFieldAsync(request);

        result.State.Should().Be(SecurityMasterRevisionStateDto.Draft);
        harness.Overrides.Verify(
            o => o.PatchAsync(
                SecurityId,
                It.Is<OperatorOverridesPatchRequest>(p =>
                    (p.SetValues == null || p.SetValues.Count == 0)
                    && p.RemoveKeys != null
                    && p.RemoveKeys.Contains("assetSpecificTerms.couponRate")),
                "ops.analyst",
                It.IsAny<CancellationToken>(),
                It.IsAny<long?>()),
            Times.Once,
            "a blank edit must remove the overlay key, not upsert an empty value");
    }

    [Fact]
    public async Task UpdateSecurityField_BlankValue_RemovesOperatorProvenanceInsteadOfUpserting()
    {
        // A clear withdraws the asserted operator value, so lineage must not keep (or newly record)
        // an OperatorFieldEdit attribution claiming an asserted value that no longer exists.
        var harness = new Harness(currentVersion: 3);
        harness.SetPassportAssetClass("Bond");
        var overrideRecordedAt = new DateTimeOffset(2026, 3, 14, 23, 59, 0, TimeSpan.Zero);
        harness.Overrides
            .Setup(o => o.PatchAsync(
                It.IsAny<Guid>(),
                It.IsAny<OperatorOverridesPatchRequest>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<long?>()))
            .ReturnsAsync(new OperatorOverridesDto(
                SecurityId,
                new Dictionary<string, string>(),
                "ops.analyst",
                overrideRecordedAt));

        var request = new UpdateSecurityFieldRequest(
            SecurityId: SecurityId,
            ExpectedVersion: 3,
            FieldPath: "assetSpecificTerms.couponRate",
            NewValue: null,
            EffectiveFrom: DateTimeOffset.UtcNow,
            Actor: "ops.analyst",
            Justification: "Withdraw the staged coupon override.");

        await harness.Service.UpdateSecurityFieldAsync(request);

        harness.FieldProvenance.Verify(
            p => p.RemoveAsync(
                SecurityId,
                "assetSpecificTerms.couponRate",
                SecurityFieldProvenanceOrigins.OperatorFieldEdit,
                overrideRecordedAt,
                It.IsAny<CancellationToken>()),
            Times.Once,
            "a clear must remove the operator attribution row, ordered by the overlay write time");
        harness.FieldProvenance.Verify(
            p => p.UpsertAsync(It.IsAny<SecurityFieldProvenanceRecord>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "a clear must not record an OperatorFieldEdit attribution for a value that was withdrawn");
    }

    [Fact]
    public async Task UpdateSecurityField_AliasPath_PersistsUnderTheCanonicalFieldKey()
    {
        // "dayCountConvention" is a declared legacy alias of Bond "dayCount". Persisting the raw
        // alias would fork the same term into separate override keys, revisions, and provenance
        // rows, so the write path must normalize to the schema field key.
        var harness = new Harness(currentVersion: 3);
        harness.SetPassportAssetClass("Bond");

        var request = new UpdateSecurityFieldRequest(
            SecurityId: SecurityId,
            ExpectedVersion: 3,
            FieldPath: "assetSpecificTerms.dayCountConvention",
            NewValue: "30/360",
            EffectiveFrom: DateTimeOffset.UtcNow,
            Actor: "ops.analyst",
            Justification: "Day-count correction.");

        var result = await harness.Service.UpdateSecurityFieldAsync(request);

        result.ChangeEntry.ChangedFields.Should().Equal(
            new[] { "assetSpecificTerms.dayCount" },
            "the audit trail must carry the canonical spelling, not the caller's alias");
        harness.Overrides.Verify(
            o => o.PatchAsync(
                SecurityId,
                It.Is<OperatorOverridesPatchRequest>(p =>
                    p.SetValues != null && p.SetValues.ContainsKey("assetSpecificTerms.dayCount")),
                "ops.analyst",
                It.IsAny<CancellationToken>(),
                It.IsAny<long?>()),
            Times.Once,
            "the override must be stored under the canonical field key");
        harness.FieldProvenance.Verify(
            p => p.UpsertAsync(
                It.Is<SecurityFieldProvenanceRecord>(r => r.FieldPath == "assetSpecificTerms.dayCount"),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "field lineage must be keyed by the canonical field path");
    }

    [Fact]
    public async Task UpdateSecurityField_RecordsOperatorFieldProvenance()
    {
        var harness = new Harness(currentVersion: 3);
        var overrideRecordedAt = new DateTimeOffset(2026, 3, 14, 23, 59, 0, TimeSpan.Zero);
        harness.Overrides
            .Setup(o => o.PatchAsync(
                It.IsAny<Guid>(),
                It.IsAny<OperatorOverridesPatchRequest>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<long?>()))
            .ReturnsAsync(new OperatorOverridesDto(
                SecurityId,
                new Dictionary<string, string>(),
                "ops.analyst",
                overrideRecordedAt));

        var request = new UpdateSecurityFieldRequest(
            SecurityId: SecurityId,
            ExpectedVersion: 3,
            FieldPath: "EconomicDefinition.Coupon",
            NewValue: "4.25",
            EffectiveFrom: new DateTimeOffset(2026, 3, 15, 0, 0, 0, TimeSpan.Zero),
            Actor: "ops.analyst",
            Justification: "Coupon correction.");

        var result = await harness.Service.UpdateSecurityFieldAsync(request);

        harness.FieldProvenance.Verify(
            p => p.UpsertAsync(
                It.Is<SecurityFieldProvenanceRecord>(r =>
                    r.SecurityId == SecurityId
                    && r.FieldPath == "EconomicDefinition.Coupon"
                    && r.Origin == SecurityFieldProvenanceOrigins.OperatorFieldEdit
                    && r.UpdatedBy == "ops.analyst"
                    && r.OriginReference == result.RevisionId.ToString("D")
                    && r.RecordedAt == overrideRecordedAt),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "an operator field edit must record its field-level attribution referenced to the draft revision");
    }

    [Fact]
    public async Task UpdateSecurityField_ProvenanceWriteFailure_DoesNotFailTheEdit()
    {
        var harness = new Harness(currentVersion: 3);
        harness.FieldProvenance
            .Setup(p => p.UpsertAsync(It.IsAny<SecurityFieldProvenanceRecord>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("provenance store down"));

        var request = new UpdateSecurityFieldRequest(
            SecurityId: SecurityId,
            ExpectedVersion: 3,
            FieldPath: "EconomicDefinition.Coupon",
            NewValue: "4.25",
            EffectiveFrom: DateTimeOffset.UtcNow,
            Actor: "ops.analyst",
            Justification: "Coupon correction.");

        var result = await harness.Service.UpdateSecurityFieldAsync(request);

        result.State.Should().Be(SecurityMasterRevisionStateDto.Draft,
            "field lineage is best-effort; the staged override and draft revision remain authoritative");
    }

    [Fact]
    public async Task UpdateSecurityField_OperatorOriginNoJustification_Throws()
    {
        var harness = new Harness(currentVersion: 3);

        var request = new UpdateSecurityFieldRequest(
            SecurityId: SecurityId,
            ExpectedVersion: 3,
            FieldPath: "Identity.Isin",
            NewValue: "US0378331005",
            EffectiveFrom: DateTimeOffset.UtcNow,
            Actor: "ops.analyst",
            Justification: "   ");

        await harness.Service.Invoking(s => s.UpdateSecurityFieldAsync(request))
            .Should().ThrowAsync<ArgumentException>();
        harness.EventStore.Appends.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateSecurityField_StaleExpectedVersion_ThrowsConcurrency()
    {
        var harness = new Harness(currentVersion: 9);

        var request = new UpdateSecurityFieldRequest(
            SecurityId: SecurityId,
            ExpectedVersion: 8, // stale
            FieldPath: "Identity.Cusip",
            NewValue: "037833100",
            EffectiveFrom: DateTimeOffset.UtcNow,
            Actor: "ops.analyst",
            Justification: "Backfill CUSIP.");

        var ex = await harness.Service.Invoking(s => s.UpdateSecurityFieldAsync(request))
            .Should().ThrowAsync<SecurityMasterConcurrencyException>();
        ex.Which.CurrentVersion.Should().Be(9);
        ex.Which.ExpectedVersion.Should().Be(8);
        harness.EventStore.Appends.Should().BeEmpty("a stale edit is rejected before any append");
    }

    [Fact]
    public async Task UpdateSecurityField_UnknownSecurity_Throws()
    {
        // currentVersion 0 == no events == the security was never created.
        var harness = new Harness(currentVersion: 0);

        var request = new UpdateSecurityFieldRequest(
            SecurityId: SecurityId,
            ExpectedVersion: 0,
            FieldPath: "Identity.Isin",
            NewValue: "US0378331005",
            EffectiveFrom: DateTimeOffset.UtcNow,
            Actor: "ops.analyst",
            Justification: "Backfill ISIN.");

        await harness.Service.Invoking(s => s.UpdateSecurityFieldAsync(request))
            .Should().ThrowAsync<InvalidOperationException>();
        harness.Overrides.Verify(
            o => o.PatchAsync(It.IsAny<Guid>(), It.IsAny<OperatorOverridesPatchRequest>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<long?>()),
            Times.Never);
    }

    [Fact]
    public async Task UpdateSecurityField_HappyPath_StagesOverrideAnnotation_WithoutEconomicStreamAppend()
    {
        var effectiveFrom = new DateTimeOffset(2026, 03, 31, 0, 0, 0, TimeSpan.Zero);
        var harness = new Harness(currentVersion: 7);

        var request = new UpdateSecurityFieldRequest(
            SecurityId: SecurityId,
            ExpectedVersion: 7,
            FieldPath: "EconomicDefinition.Coupon",
            NewValue: "4.250",
            EffectiveFrom: effectiveFrom,
            Actor: "ops.analyst",
            Justification: "Corrected coupon per agent term sheet.");

        var result = await harness.Service.UpdateSecurityFieldAsync(request);

        result.State.Should().Be(SecurityMasterRevisionStateDto.Draft);
        result.NewVersion.Should().Be(7, "an overlay annotation does not advance the canonical security version");
        result.ChangeEntry.EffectiveAtUtc.Should().Be(effectiveFrom);
        result.ChangeEntry.ChangedFields.Should().Contain("EconomicDefinition.Coupon");

        // A durable Draft revision is opened with a real, server-issued id (not a transient client
        // value), carrying the field-edit metadata so a later publish can scope downstream impact.
        var stored = await harness.Revisions.GetAsync(result.RevisionId);
        stored.Should().NotBeNull();
        stored!.State.Should().Be(SecurityMasterRevisionStateDto.Draft);
        stored.SecurityId.Should().Be(SecurityId);
        stored.FieldPath.Should().Be("EconomicDefinition.Coupon");
        stored.FieldEffectiveFrom.Should().Be(effectiveFrom);
        stored.FieldJustification.Should().Be("Corrected coupon per agent term sheet.");

        // The edit is staged purely as an override read-model annotation. It must NOT be appended to
        // the economic event stream — that stream is replayed verbatim to rebuild the passport, so a
        // partial field-edit payload would corrupt the economic definition on the next reload.
        harness.EventStore.Appends.Should().BeEmpty("overlay edits never enter the economic replay stream");
        harness.Overrides.Verify(
            o => o.PatchAsync(
                SecurityId,
                It.Is<OperatorOverridesPatchRequest>(p => p.SetValues != null && p.SetValues.ContainsKey("EconomicDefinition.Coupon")),
                "ops.analyst",
                It.IsAny<CancellationToken>(),
                7),
            Times.Once);
    }

    [Fact]
    public async Task UpdateSecurityField_CanonicalVersionAdvancesDuringOverlayPatch_ThrowsConcurrency()
    {
        var harness = new Harness(currentVersion: 7);
        harness.Overrides
            .Setup(o => o.PatchAsync(
                SecurityId,
                It.IsAny<OperatorOverridesPatchRequest>(),
                "ops.analyst",
                It.IsAny<CancellationToken>(),
                7))
            .ThrowsAsync(new OperatorOverrideCanonicalVersionConflictException(SecurityId, 7, 8));

        var request = new UpdateSecurityFieldRequest(
            SecurityId,
            ExpectedVersion: 7,
            FieldPath: "EconomicDefinition.Coupon",
            NewValue: "4.250",
            EffectiveFrom: DateTimeOffset.UtcNow,
            Actor: "ops.analyst",
            Justification: "Corrected coupon per agent term sheet.");

        var exception = await harness.Service.Invoking(s => s.UpdateSecurityFieldAsync(request))
            .Should().ThrowAsync<SecurityMasterConcurrencyException>();

        exception.Which.ExpectedVersion.Should().Be(7);
        exception.Which.CurrentVersion.Should().Be(8);
        harness.EventStore.Appends.Should().BeEmpty("the retry-safe overlay remains outside the economic stream");
    }

    // ---- ResolveSourceConflict (validation guards) --------------------------------------------

    [Fact]
    public async Task ResolveSourceConflict_NoReason_Throws()
    {
        var harness = new Harness(currentVersion: 2);

        var request = new ResolveSourceConflictRequest(
            SecurityId: SecurityId,
            ConflictId: Guid.NewGuid(),
            ExpectedVersion: 2,
            ChosenWinnerSource: "Edgar",
            Actor: "ops.analyst",
            Reason: "  ");

        await harness.Service.Invoking(s => s.ResolveSourceConflictAsync(request))
            .Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ResolveSourceConflict_StaleExpectedVersion_ThrowsConcurrency()
    {
        var harness = new Harness(currentVersion: 5);

        var request = new ResolveSourceConflictRequest(
            SecurityId: SecurityId,
            ConflictId: Guid.NewGuid(),
            ExpectedVersion: 4, // stale
            ChosenWinnerSource: "Edgar",
            Actor: "ops.analyst",
            Reason: "Prefer Edgar.");

        await harness.Service.Invoking(s => s.ResolveSourceConflictAsync(request))
            .Should().ThrowAsync<SecurityMasterConcurrencyException>();
    }

    // ---- ResolveSourceConflict (winner-candidate validation) ----------------------------------

    [Theory]
    [InlineData("Edgar")]        // the current winning source
    [InlineData("edgar")]        // case-insensitive
    [InlineData("  Edgar  ")]    // whitespace-tolerant
    [InlineData("Polygon")]      // the challenger source
    public void EnsureChosenWinnerIsCandidate_ValidCandidate_DoesNotThrow(string chosen)
    {
        var assessment = BuildAssessment(currentSource: "Edgar", challengerSource: "Polygon");

        var act = () => SecurityMasterWorkbenchCommandService.EnsureChosenWinnerIsCandidate(assessment, chosen);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("Bloomberg")]    // never in conflict
    [InlineData("Edgr")]         // typo
    [InlineData("")]             // empty
    public void EnsureChosenWinnerIsCandidate_NonCandidate_Throws(string chosen)
    {
        var assessment = BuildAssessment(currentSource: "Edgar", challengerSource: "Polygon");

        var act = () => SecurityMasterWorkbenchCommandService.EnsureChosenWinnerIsCandidate(assessment, chosen);

        act.Should().Throw<ArgumentException>("an arbitrary or mistyped source must not be allowed to close the conflict");
    }

    private static SecurityMasterConflictAssessmentDto BuildAssessment(string currentSource, string challengerSource)
        => new(
            Conflict: new SecurityMasterConflict(
                ConflictId: Guid.NewGuid(),
                SecurityId: SecurityId,
                ConflictKind: "IdentifierAmbiguity",
                FieldPath: "Identifiers.Cusip",
                ProviderA: currentSource,
                ValueA: "value-a",
                ProviderB: challengerSource,
                ValueB: "value-b",
                DetectedAt: DateTimeOffset.UnixEpoch,
                Status: "Open"),
            CurrentWinningValue: "value-a",
            ChallengerValue: "value-b",
            CurrentWinningSource: currentSource,
            ChallengerSource: challengerSource,
            Recommendation: SecurityMasterConflictRecommendationKind.PreserveWinner,
            RecommendedResolution: "Resolve",
            RecommendedWinner: currentSource,
            ImpactSeverity: SecurityMasterImpactSeverity.Low,
            ImpactSummary: "summary",
            ImpactDetail: "detail",
            IsBulkEligible: false);

    // ---- Submit / Approve through the gate ----------------------------------------------------

    [Fact]
    public async Task Submit_WithoutWorkflow_ReturnsSubmittedState()
    {
        var harness = new Harness(currentVersion: 2);
        var revisionId = await harness.SeedRevisionAsync(SecurityMasterRevisionStateDto.Draft);

        var request = new SubmitSecurityMasterRevisionRequest(
            SecurityId: SecurityId,
            RevisionId: revisionId,
            Actor: "ops.analyst",
            Note: "Ready for review.");

        var result = await harness.Service.SubmitForApprovalAsync(request);

        result.State.Should().Be(SecurityMasterRevisionStateDto.Submitted);
        result.RevisionId.Should().Be(revisionId);
        (await harness.Revisions.GetAsync(revisionId))!.State.Should().Be(SecurityMasterRevisionStateDto.Submitted);
        harness.Workflow.Verify(
            w => w.SubmitForApprovalAsync(It.IsAny<Guid>(), It.IsAny<OperationsSubmitApprovalRequestDto>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Submit_WithWorkflow_RoutesThroughApprovalGate()
    {
        var workflowId = Guid.NewGuid();
        var harness = new Harness(currentVersion: 2);
        var revisionId = await harness.SeedRevisionAsync(SecurityMasterRevisionStateDto.Draft);
        harness.Workflow
            .Setup(w => w.SubmitForApprovalAsync(workflowId, It.IsAny<OperationsSubmitApprovalRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SuccessTransition(newVersion: 3));

        var request = new SubmitSecurityMasterRevisionRequest(
            SecurityId: SecurityId,
            RevisionId: revisionId,
            Actor: "ops.analyst",
            Note: "Submit through gate.",
            FundProfileId: null,
            WorkflowId: workflowId,
            ExpectedWorkflowVersion: 2,
            Reviewer: "ops.reviewer",
            ReportPackId: "rp-1");

        var result = await harness.Service.SubmitForApprovalAsync(request);

        result.State.Should().Be(SecurityMasterRevisionStateDto.Submitted);
        var stored = await harness.Revisions.GetAsync(revisionId);
        stored!.State.Should().Be(SecurityMasterRevisionStateDto.Submitted);
        stored.WorkflowId.Should().Be(workflowId, "the submitting workflow is bound so approval is restricted to this lane");
        harness.Workflow.Verify(
            w => w.SubmitForApprovalAsync(workflowId, It.IsAny<OperationsSubmitApprovalRequestDto>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Submit_WithWorkflow_BlankReviewer_ThrowsBeforeGate()
    {
        var workflowId = Guid.NewGuid();
        var harness = new Harness(currentVersion: 2);
        var revisionId = await harness.SeedRevisionAsync(SecurityMasterRevisionStateDto.Draft);

        var request = new SubmitSecurityMasterRevisionRequest(
            SecurityId: SecurityId,
            RevisionId: revisionId,
            Actor: "ops.analyst",
            Note: "Submit.",
            FundProfileId: null,
            WorkflowId: workflowId,
            ExpectedWorkflowVersion: 2,
            Reviewer: "   ", // blank — would otherwise default to the submitter
            ReportPackId: "rp-1");

        await harness.Service.Invoking(s => s.SubmitForApprovalAsync(request))
            .Should().ThrowAsync<ArgumentException>();
        harness.Workflow.Verify(
            w => w.SubmitForApprovalAsync(It.IsAny<Guid>(), It.IsAny<OperationsSubmitApprovalRequestDto>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Submit_WithWorkflow_ReviewerEqualsSubmitter_ThrowsBeforeGate()
    {
        var workflowId = Guid.NewGuid();
        var harness = new Harness(currentVersion: 2);
        var revisionId = await harness.SeedRevisionAsync(SecurityMasterRevisionStateDto.Draft);

        var request = new SubmitSecurityMasterRevisionRequest(
            SecurityId: SecurityId,
            RevisionId: revisionId,
            Actor: "ops.analyst",
            Note: "Submit.",
            FundProfileId: null,
            WorkflowId: workflowId,
            ExpectedWorkflowVersion: 2,
            Reviewer: "OPS.ANALYST", // same actor, different case — self-approval attempt
            ReportPackId: "rp-1");

        await harness.Service.Invoking(s => s.SubmitForApprovalAsync(request))
            .Should().ThrowAsync<ArgumentException>();
        harness.Workflow.Verify(
            w => w.SubmitForApprovalAsync(It.IsAny<Guid>(), It.IsAny<OperationsSubmitApprovalRequestDto>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Submit_UnknownRevision_Throws_BeforeTouchingGate()
    {
        var workflowId = Guid.NewGuid();
        var harness = new Harness(currentVersion: 2);

        var request = new SubmitSecurityMasterRevisionRequest(
            SecurityId: SecurityId,
            RevisionId: Guid.NewGuid(), // never created
            Actor: "ops.analyst",
            Note: "Submit through gate.",
            FundProfileId: null,
            WorkflowId: workflowId,
            ExpectedWorkflowVersion: 2,
            Reviewer: "ops.reviewer",
            ReportPackId: "rp-1");

        await harness.Service.Invoking(s => s.SubmitForApprovalAsync(request))
            .Should().ThrowAsync<SecurityMasterRevisionStateException>();

        // The approval gate must not be mutated for a stale/mistyped revision id (no orphaned lane).
        harness.Workflow.Verify(
            w => w.SubmitForApprovalAsync(It.IsAny<Guid>(), It.IsAny<OperationsSubmitApprovalRequestDto>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Approve_UnknownRevision_Throws_BeforeTouchingGate()
    {
        var workflowId = Guid.NewGuid();
        var harness = new Harness(currentVersion: 4);

        var request = new ApproveSecurityMasterRevisionRequest(
            SecurityId: SecurityId,
            RevisionId: Guid.NewGuid(), // never created
            WorkflowId: workflowId,
            ExpectedWorkflowVersion: 4,
            Actor: "ops.reviewer",
            Reviewer: "ops.reviewer",
            Rationale: "Approved.",
            ReportPackId: "rp-1");

        await harness.Service.Invoking(s => s.ApproveRevisionAsync(request))
            .Should().ThrowAsync<SecurityMasterRevisionStateException>();
        harness.Workflow.Verify(
            w => w.ApproveWorkflowAsync(It.IsAny<Guid>(), It.IsAny<OperationsApprovalDecisionRequestDto>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Approve_WorkflowMismatch_Throws_BeforeTouchingGate()
    {
        var submitWorkflowId = Guid.NewGuid();
        var unrelatedWorkflowId = Guid.NewGuid();
        var harness = new Harness(currentVersion: 4);
        var revisionId = await harness.SeedRevisionAsync(
            SecurityMasterRevisionStateDto.Submitted, workflowId: submitWorkflowId);

        // Approve via an unrelated, already-approvable workflow lane.
        var request = new ApproveSecurityMasterRevisionRequest(
            SecurityId: SecurityId,
            RevisionId: revisionId,
            WorkflowId: unrelatedWorkflowId,
            ExpectedWorkflowVersion: 4,
            Actor: "ops.reviewer",
            Reviewer: "ops.reviewer",
            Rationale: "Approved.",
            ReportPackId: "rp-1");

        await harness.Service.Invoking(s => s.ApproveRevisionAsync(request))
            .Should().ThrowAsync<SecurityMasterRevisionStateException>();
        harness.Workflow.Verify(
            w => w.ApproveWorkflowAsync(It.IsAny<Guid>(), It.IsAny<OperationsApprovalDecisionRequestDto>(), It.IsAny<CancellationToken>()),
            Times.Never);
        (await harness.Revisions.GetAsync(revisionId))!.State.Should().Be(SecurityMasterRevisionStateDto.Submitted);
    }

    [Fact]
    public async Task Approve_RoutesThroughApprovalGate_AndReturnsApproved()
    {
        var workflowId = Guid.NewGuid();
        var harness = new Harness(currentVersion: 4);
        var revisionId = await harness.SeedRevisionAsync(
            SecurityMasterRevisionStateDto.Submitted, workflowId: workflowId);
        harness.Workflow
            .Setup(w => w.ApproveWorkflowAsync(workflowId, It.IsAny<OperationsApprovalDecisionRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SuccessTransition(newVersion: 5));

        var request = new ApproveSecurityMasterRevisionRequest(
            SecurityId: SecurityId,
            RevisionId: revisionId,
            WorkflowId: workflowId,
            ExpectedWorkflowVersion: 4,
            Actor: "ops.reviewer",
            Reviewer: "ops.reviewer",
            Rationale: "Approved.",
            ReportPackId: "rp-1");

        var result = await harness.Service.ApproveRevisionAsync(request);

        result.State.Should().Be(SecurityMasterRevisionStateDto.Approved);
        (await harness.Revisions.GetAsync(revisionId))!.State.Should().Be(SecurityMasterRevisionStateDto.Approved);
        harness.Workflow.Verify(
            w => w.ApproveWorkflowAsync(workflowId, It.IsAny<OperationsApprovalDecisionRequestDto>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Approve_PendingOperatorOverride_RecordsOverrideApprovalWithTheGateDecision()
    {
        // The browser workflow exposes ONE approval step: approving the revision must also record
        // the underlying override's approval decision, or the published edit stays Pending and
        // SM_OVERRIDE_APPROVAL_REQUIRED keeps blocking governed runs despite the completed gate.
        var workflowId = Guid.NewGuid();
        var harness = new Harness(currentVersion: 4);
        var revisionId = await harness.SeedRevisionAsync(
            SecurityMasterRevisionStateDto.Submitted, workflowId: workflowId);
        harness.Workflow
            .Setup(w => w.ApproveWorkflowAsync(workflowId, It.IsAny<OperationsApprovalDecisionRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SuccessTransition(newVersion: 5));
        harness.Overrides
            .Setup(o => o.GetAsync(SecurityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperatorOverridesDto(
                SecurityId,
                new Dictionary<string, string> { ["EconomicDefinition.Coupon"] = "4.5" },
                "ops.analyst",
                DateTimeOffset.UtcNow)
            {
                ApprovalStatus = SecurityOverrideApprovalStatusDto.Pending
            });

        await harness.Service.ApproveRevisionAsync(new ApproveSecurityMasterRevisionRequest(
            SecurityId, revisionId, workflowId, 4, "ops.actor", "ops.reviewer", "Approved.", "rp-1"));

        harness.Overrides.Verify(
            o => o.RecordApprovalDecisionAsync(
                SecurityId,
                It.Is<OperatorOverrideDecision>(decision =>
                    decision.Decision == SecurityOverrideApprovalStatusDto.Approved
                    && decision.Reviewer == "ops.reviewer"),
                It.IsAny<CancellationToken>()),
            Times.Once);
        (await harness.Revisions.GetAsync(revisionId))!.State.Should().Be(SecurityMasterRevisionStateDto.Approved);
    }

    [Fact]
    public async Task Approve_WhenGateBlocks_Throws()
    {
        var workflowId = Guid.NewGuid();
        var harness = new Harness(currentVersion: 4);
        var revisionId = await harness.SeedRevisionAsync(
            SecurityMasterRevisionStateDto.Submitted, workflowId: workflowId);
        harness.Workflow
            .Setup(w => w.ApproveWorkflowAsync(workflowId, It.IsAny<OperationsApprovalDecisionRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperationsTransitionResultDto(false, "BLOCKED", "Independent reviewer required.", null, [], []));

        var request = new ApproveSecurityMasterRevisionRequest(
            SecurityId, revisionId, workflowId, 4, "ops.analyst", "ops.analyst", "Approve.", "rp-1");

        await harness.Service.Invoking(s => s.ApproveRevisionAsync(request))
            .Should().ThrowAsync<InvalidOperationException>();

        // The gate rejected the approval, so the revision must remain Submitted (not advanced).
        (await harness.Revisions.GetAsync(revisionId))!.State.Should().Be(SecurityMasterRevisionStateDto.Submitted);
    }

    // ---- Publish ------------------------------------------------------------------------------

    [Fact]
    public async Task Publish_ApprovedRevision_FansOutHandlersInOrder_AndTransitionsToPublished()
    {
        var log = new List<int>();
        var ufl = new RecordingHandler(order: 10, invocationLog: log);
        var coverage = new RecordingHandler(order: 20, invocationLog: log);
        var harness = new Harness(currentVersion: 4, handlers: [coverage, ufl]); // intentionally out of order
        var revisionId = await harness.SeedRevisionAsync(SecurityMasterRevisionStateDto.Approved);

        var request = new PublishSecurityMasterRevisionRequest(
            SecurityId: SecurityId,
            RevisionId: revisionId,
            Actor: "ops.analyst",
            ApproverActor: "ops.reviewer");

        var result = await harness.Service.PublishRevisionAsync(request);

        ufl.Received.Should().ContainSingle();
        coverage.Received.Should().ContainSingle();
        log.Should().Equal(new[] { 10, 20 });
        result.RestatementRequired.Should().BeFalse();
        result.RestatementCandidates.Should().BeEmpty();
        result.InvalidatedProjections.Should().HaveCount(2);
        (await harness.Revisions.GetAsync(revisionId))!.State.Should().Be(SecurityMasterRevisionStateDto.Published);
    }

    [Fact]
    public async Task Publish_RestatementResolutionFails_KeepsRevisionApprovedForRetry()
    {
        // The closed-period restatement decision is a REQUIRED publish side effect: resolving it
        // after the Published transition would make a transient period-lock outage unretryable
        // (the Approved-state precondition rejects the retry), permanently skipping the decision.
        var harness = new Harness(currentVersion: 4, handlers: [new RecordingHandler(order: 10)]);
        var revisionId = await harness.SeedRevisionAsync(SecurityMasterRevisionStateDto.Approved);
        harness.Restatement
            .Setup(r => r.ResolveAsync(It.IsAny<SecurityMasterRevisionPublishedEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("period-lock index unavailable"));

        var request = new PublishSecurityMasterRevisionRequest(
            SecurityId: SecurityId,
            RevisionId: revisionId,
            Actor: "ops.analyst",
            ApproverActor: "ops.reviewer");

        await harness.Service.Invoking(s => s.PublishRevisionAsync(request))
            .Should().ThrowAsync<InvalidOperationException>();
        (await harness.Revisions.GetAsync(revisionId))!.State.Should().Be(SecurityMasterRevisionStateDto.Approved,
            "a failed restatement resolution must leave the publish retryable");

        // The resolver recovers and the SAME publish retries to completion (handlers are idempotent).
        harness.Restatement
            .Setup(r => r.ResolveAsync(It.IsAny<SecurityMasterRevisionPublishedEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SecurityMasterRestatementDecision(RestatementRequired: false, Candidates: []));
        await harness.Service.PublishRevisionAsync(request);
        (await harness.Revisions.GetAsync(revisionId))!.State.Should().Be(SecurityMasterRevisionStateDto.Published);
    }

    [Fact]
    public async Task Publish_FieldEditRevision_EmitsEventWithStoredEffectiveDateAndChangedField()
    {
        var effectiveFrom = new DateTimeOffset(2026, 03, 31, 0, 0, 0, TimeSpan.Zero);
        var handler = new RecordingHandler(order: 10);
        var harness = new Harness(currentVersion: 4, handlers: [handler]);
        var revisionId = await harness.SeedFieldEditRevisionAsync(
            "EconomicDefinition.Coupon", effectiveFrom, "Corrected coupon.");

        var request = new PublishSecurityMasterRevisionRequest(
            SecurityId: SecurityId,
            RevisionId: revisionId,
            Actor: "ops.analyst",
            ApproverActor: "ops.reviewer");

        await harness.Service.PublishRevisionAsync(request);

        var evt = handler.Received.Should().ContainSingle().Subject;
        evt.EffectiveFrom.Should().Be(effectiveFrom, "the published event must carry the edit's effective date, not publish time");
        evt.ChangedFields.Should().Equal("EconomicDefinition.Coupon");
    }

    [Fact]
    public async Task Publish_ResolvesAffectedLedgerBooks_AndFlowsThemIntoPublishedEvent()
    {
        var bookA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var bookB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var handler = new RecordingHandler(order: 10);
        var harness = new Harness(currentVersion: 4, handlers: [handler]);
        var revisionId = await harness.SeedRevisionAsync(SecurityMasterRevisionStateDto.Approved);
        harness.AffectedBooks
            .Setup(r => r.ResolveAsync(It.IsAny<SecurityMasterDownstreamImpactDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<Guid>)[bookA, bookB]);

        var request = new PublishSecurityMasterRevisionRequest(
            SecurityId: SecurityId,
            RevisionId: revisionId,
            Actor: "ops.analyst",
            ApproverActor: "ops.reviewer");

        await harness.Service.PublishRevisionAsync(request);

        // The resolved books must reach the published event so the period-aware resolver and the
        // side-effect handlers route by each book's accounting-period lock status.
        var evt = handler.Received.Should().ContainSingle().Subject;
        evt.AffectedLedgerBookIds.Should().Equal(bookA, bookB);
    }

    [Fact]
    public async Task Publish_ClosedPeriodExposure_FlowsRestatementDecisionIntoResult()
    {
        var harness = new Harness(currentVersion: 4);
        var revisionId = await harness.SeedRevisionAsync(SecurityMasterRevisionStateDto.Approved);
        var candidate = new RestatementCandidateDto(
            ReportId: Guid.NewGuid(),
            PriorVersionReportId: Guid.NewGuid(),
            PeriodLabel: "2026-P03",
            Summary: "Restate Q1 NAV pack for corrected coupon.",
            ChangedLines: []);
        harness.Restatement
            .Setup(r => r.ResolveAsync(It.IsAny<SecurityMasterRevisionPublishedEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SecurityMasterRestatementDecision(RestatementRequired: true, Candidates: [candidate]));

        var request = new PublishSecurityMasterRevisionRequest(
            SecurityId: SecurityId,
            RevisionId: revisionId,
            Actor: "ops.analyst",
            ApproverActor: "ops.reviewer");

        var result = await harness.Service.PublishRevisionAsync(request);

        result.RestatementRequired.Should().BeTrue();
        result.RestatementCandidates.Should().ContainSingle().Which.Should().Be(candidate);
        (await harness.Revisions.GetAsync(revisionId))!.State.Should().Be(SecurityMasterRevisionStateDto.Published);
    }

    [Fact]
    public async Task Publish_RevisionNotApproved_Throws_AndDoesNotFanOut()
    {
        var handler = new RecordingHandler(order: 10);
        var harness = new Harness(currentVersion: 4, handlers: [handler]);
        // Submitted, not yet Approved — publish must refuse.
        var revisionId = await harness.SeedRevisionAsync(SecurityMasterRevisionStateDto.Submitted);

        var request = new PublishSecurityMasterRevisionRequest(
            SecurityId: SecurityId,
            RevisionId: revisionId,
            Actor: "ops.analyst",
            ApproverActor: "ops.reviewer");

        await harness.Service.Invoking(s => s.PublishRevisionAsync(request))
            .Should().ThrowAsync<SecurityMasterRevisionStateException>();
        handler.Received.Should().BeEmpty("an unapproved revision must never trigger publish handlers");
        (await harness.Revisions.GetAsync(revisionId))!.State.Should().Be(SecurityMasterRevisionStateDto.Submitted);
    }

    [Fact]
    public async Task Publish_UnknownRevision_Throws()
    {
        var harness = new Harness(currentVersion: 4);

        var request = new PublishSecurityMasterRevisionRequest(
            SecurityId: SecurityId,
            RevisionId: Guid.NewGuid(), // never created
            Actor: "ops.analyst",
            ApproverActor: "ops.reviewer");

        await harness.Service.Invoking(s => s.PublishRevisionAsync(request))
            .Should().ThrowAsync<SecurityMasterRevisionStateException>();
    }

    [Fact]
    public async Task Publish_HandlerThrows_SurfacesFailure_AndLeavesRevisionApproved()
    {
        var throwing = new RecordingHandler(order: 10, onHandle: () => throw new InvalidOperationException("transient"));
        var harness = new Harness(currentVersion: 6, handlers: [throwing]);
        var revisionId = await harness.SeedRevisionAsync(SecurityMasterRevisionStateDto.Approved);

        var request = new PublishSecurityMasterRevisionRequest(
            SecurityId: SecurityId,
            RevisionId: revisionId,
            Actor: "ops.analyst",
            ApproverActor: "ops.reviewer");

        // A failed required side effect surfaces to the caller instead of a silently-successful publish.
        var ex = await harness.Service.Invoking(s => s.PublishRevisionAsync(request))
            .Should().ThrowAsync<SecurityMasterPublishFailedException>();
        ex.Which.FailedHandlers.Should().ContainSingle();

        // The revision stays Approved so the idempotent fan-out can be retried.
        (await harness.Revisions.GetAsync(revisionId))!.State.Should().Be(SecurityMasterRevisionStateDto.Approved);
    }

    [Fact]
    public async Task Publish_ScopesImpactToDraftFundScope()
    {
        var harness = new Harness(currentVersion: 5);
        // The fund scope is captured on the draft at edit time; publish reuses it and accepts no
        // caller-supplied override, so impact resolution targets the fund the operator edited under.
        var revisionId = await harness.SeedFieldEditRevisionWithFundAsync("fund-from-edit");

        await harness.Service.PublishRevisionAsync(new PublishSecurityMasterRevisionRequest(
            SecurityId, revisionId, "ops.analyst", "approver.independent"));

        harness.QueryService.Verify(
            q => q.GetTrustSnapshotAsync(SecurityId, "fund-from-edit", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Publish_BlankDraftScope_ResolvesToUnscopedNull()
    {
        var harness = new Harness(currentVersion: 5);
        // No fund captured on the draft revision → publish resolves an unscoped (null) impact.
        var revisionId = await harness.SeedFieldEditRevisionAsync(
            "EconomicDefinition.Coupon", new DateTimeOffset(2026, 3, 15, 0, 0, 0, TimeSpan.Zero), "Corrected coupon.");

        await harness.Service.PublishRevisionAsync(new PublishSecurityMasterRevisionRequest(
            SecurityId, revisionId, "ops.analyst", "approver.independent"));

        harness.QueryService.Verify(
            q => q.GetTrustSnapshotAsync(SecurityId, null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ---- helpers ------------------------------------------------------------------------------

    private static OperationsTransitionResultDto SuccessTransition(long newVersion)
        => new(true, null, null, null, [], [], newVersion);

    private sealed class Harness
    {
        public FakeEventStore EventStore { get; }
        public Mock<IOperatorOverridesStore> Overrides { get; } = new(MockBehavior.Loose);
        public Mock<ISecurityMasterConflictAuthorityPolicy> Policy { get; } = new(MockBehavior.Loose);
        public Mock<ISecurityMasterConflictService> ConflictService { get; } = new(MockBehavior.Loose);
        public Mock<ISecurityMasterWorkbenchQueryService> QueryService { get; } = new(MockBehavior.Loose);
        public Mock<IOperationsContinuityWorkflowService> Workflow { get; } = new(MockBehavior.Loose);
        public Mock<IPeriodAwareRestatementResolver> Restatement { get; } = new(MockBehavior.Loose);
        public Mock<IAffectedLedgerBookResolver> AffectedBooks { get; } = new(MockBehavior.Loose);
        public Mock<ISecurityFieldProvenanceStore> FieldProvenance { get; } = new(MockBehavior.Loose);
        public Mock<ISecurityMasterStore> ProjectionStore { get; } = new(MockBehavior.Loose);
        public ISecurityMasterRevisionStore Revisions { get; } = new InMemorySecurityMasterRevisionStore();
        public SecurityMasterWorkbenchCommandService Service { get; }

        public Harness(
            long currentVersion,
            IEnumerable<ISecurityMasterRevisionPublishedHandler>? handlers = null,
            Meridian.ReferenceData.SecurityMaster.ISecurityAssetProfileCatalog? assetProfileCatalog = null)
        {
            _assetProfileCatalog = assetProfileCatalog
                ?? Meridian.ReferenceData.SecurityMaster.StaticSecurityAssetProfileCatalog.CreateDefault();
            EventStore = new FakeEventStore(currentVersion);

            Overrides
                .Setup(o => o.PatchAsync(It.IsAny<Guid>(), It.IsAny<OperatorOverridesPatchRequest>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<long?>()))
                .ReturnsAsync((Guid id, OperatorOverridesPatchRequest _, string actor, CancellationToken _, long? _) =>
                    new OperatorOverridesDto(id, new Dictionary<string, string>(), actor, DateTimeOffset.UtcNow));

            QueryService
                .Setup(q => q.GetTrustSnapshotAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((SecurityMasterTrustSnapshotDto?)null);

            // Default: no closed-period exposure. Individual tests override to assert restatement flow-through.
            Restatement
                .Setup(r => r.ResolveAsync(It.IsAny<SecurityMasterRevisionPublishedEvent>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SecurityMasterRestatementDecision(RestatementRequired: false, Candidates: []));

            // Default: no affected ledger books resolved. Individual tests override to assert the feed flows.
            AffectedBooks
                .Setup(r => r.ResolveAsync(It.IsAny<SecurityMasterDownstreamImpactDto>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((IReadOnlyList<Guid>)[]);

            Service = new SecurityMasterWorkbenchCommandService(
                EventStore,
                Overrides.Object,
                Policy.Object,
                ConflictService.Object,
                QueryService.Object,
                Workflow.Object,
                Revisions,
                Restatement.Object,
                AffectedBooks.Object,
                handlers ?? Array.Empty<ISecurityMasterRevisionPublishedHandler>(),
                NullLogger<SecurityMasterWorkbenchCommandService>.Instance,
                FieldProvenance.Object,
                ProjectionStore.Object,
                _assetProfileCatalog);
        }

        private readonly Meridian.ReferenceData.SecurityMaster.ISecurityAssetProfileCatalog _assetProfileCatalog;

        /// <summary>
        /// Makes the projection store resolve the security with a pinned profile envelope so
        /// profileFields edits are validated against that profile's declared field types.
        /// </summary>
        public void SetProjectionProfileEnvelope(
            string customProfileId, int profileVersion, object? profileFields = null, string assetClass = "CustomAsset")
        {
            SetProjectionAssetTerms(assetClass, new
            {
                customProfileId,
                profileVersion,
                profileFields = profileFields ?? new { }
            });
        }

        /// <summary>
        /// Makes the projection store resolve the security with the given asset class and
        /// asset-specific terms so contextual retained-term validation (schedule windows, resolved
        /// kind invariants) can bind against them. The common terms and provenance carry the
        /// fields the canonical record mapping requires so kind reconstruction succeeds.
        /// </summary>
        public void SetProjectionAssetTerms(string assetClass, object assetSpecificTerms)
        {
            var projection = new SecurityProjectionRecord(
                SecurityId: SecurityId,
                AssetClass: assetClass,
                Status: SecurityStatusDto.Active,
                DisplayName: "Profile-backed asset",
                Currency: "USD",
                PrimaryIdentifierKind: "InternalCode",
                PrimaryIdentifierValue: "CUST-1",
                CommonTerms: JsonSerializer.SerializeToElement(new { displayName = "Profile-backed asset", currency = "USD" }),
                AssetSpecificTerms: JsonSerializer.SerializeToElement(assetSpecificTerms),
                Provenance: JsonSerializer.SerializeToElement(new
                {
                    sourceSystem = "test",
                    asOf = "2026-01-01T00:00:00+00:00",
                    updatedBy = "test"
                }),
                Version: 3,
                EffectiveFrom: DateTimeOffset.UtcNow.AddDays(-30),
                EffectiveTo: null,
                Identifiers:
                [
                    new SecurityIdentifierDto(SecurityIdentifierKind.InternalCode, "CUST-1", true, DateTimeOffset.UtcNow.AddDays(-30))
                ],
                Aliases: Array.Empty<SecurityAliasDto>());
            ProjectionStore
                .Setup(s => s.GetProjectionAsync(SecurityId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(projection);
        }

        /// <summary>
        /// Makes the passport read model resolve the security to <paramref name="assetClass"/> so
        /// assetSpecificTerms edits are schema-validated against that class. Only the economic
        /// definition drill-in is read by the validation path; the rest of the passport is inert.
        /// </summary>
        public void SetPassportAssetClass(string assetClass)
        {
            var passport = new InstrumentPassportDto(
                SecurityId,
                Identity: null!,
                EconomicDefinition: new SecurityMasterEconomicDefinitionDrillInDto(
                    SecurityId, assetClass, "USD", 3, DateTimeOffset.UtcNow, null,
                    null, null, null, null, null, null, null, null, null),
                IdentifierSummary: null!,
                ProviderMappings: [],
                LifecycleEvents: [],
                CorporateActions: [],
                Pricing: null!,
                Usage: null!,
                TrustPosture: null!,
                RetrievedAtUtc: DateTimeOffset.UtcNow);

            QueryService
                .Setup(q => q.GetInstrumentPassportAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(passport);
        }

        /// <summary>
        /// Seeds a revision advanced to <paramref name="state"/> and returns its id. When
        /// <paramref name="workflowId"/> is supplied it is bound on the Draft→Submitted transition so
        /// approval-binding checks can be exercised.
        /// </summary>
        public async Task<Guid> SeedRevisionAsync(
            SecurityMasterRevisionStateDto state, string actor = "ops.analyst", Guid? workflowId = null)
        {
            var draft = await Revisions.CreateDraftAsync(SecurityId, actor);
            var id = draft.RevisionId;
            if (state == SecurityMasterRevisionStateDto.Draft)
            {
                return id;
            }

            await Revisions.TransitionAsync(
                id, SecurityMasterRevisionStateDto.Draft, SecurityMasterRevisionStateDto.Submitted, actor,
                workflowIdForSubmit: workflowId);
            if (state == SecurityMasterRevisionStateDto.Submitted)
            {
                return id;
            }

            await Revisions.TransitionAsync(id, SecurityMasterRevisionStateDto.Submitted, SecurityMasterRevisionStateDto.Approved, actor);
            if (state == SecurityMasterRevisionStateDto.Approved)
            {
                return id;
            }

            await Revisions.TransitionAsync(id, SecurityMasterRevisionStateDto.Approved, SecurityMasterRevisionStateDto.Published, actor);
            return id;
        }

        /// <summary>Seeds an Approved revision carrying field-edit metadata (path + effective date).</summary>
        public async Task<Guid> SeedFieldEditRevisionAsync(
            string fieldPath, DateTimeOffset effectiveFrom, string justification, string actor = "ops.analyst")
        {
            var draft = await Revisions.CreateDraftAsync(SecurityId, actor, fieldPath, effectiveFrom, justification);
            var id = draft.RevisionId;
            await Revisions.TransitionAsync(id, SecurityMasterRevisionStateDto.Draft, SecurityMasterRevisionStateDto.Submitted, actor);
            await Revisions.TransitionAsync(id, SecurityMasterRevisionStateDto.Submitted, SecurityMasterRevisionStateDto.Approved, actor);
            return id;
        }

        /// <summary>Seeds an Approved field-edit revision carrying a fund-profile scope from the edit.</summary>
        public async Task<Guid> SeedFieldEditRevisionWithFundAsync(string fundProfileId, string actor = "ops.analyst")
        {
            var effectiveFrom = new DateTimeOffset(2026, 3, 15, 0, 0, 0, TimeSpan.Zero);
            var draft = await Revisions.CreateDraftAsync(
                SecurityId, actor, "EconomicDefinition.Coupon", effectiveFrom, "Corrected coupon.", fundProfileId);
            var id = draft.RevisionId;
            await Revisions.TransitionAsync(id, SecurityMasterRevisionStateDto.Draft, SecurityMasterRevisionStateDto.Submitted, actor);
            await Revisions.TransitionAsync(id, SecurityMasterRevisionStateDto.Submitted, SecurityMasterRevisionStateDto.Approved, actor);
            return id;
        }
    }

    /// <summary>Version-aware fake: LoadAsync reports the configured stream version; AppendAsync is recorded.</summary>
    private sealed class FakeEventStore : ISecurityMasterEventStore
    {
        private readonly long _version;

        public FakeEventStore(long version) => _version = version;

        public List<(Guid SecurityId, long ExpectedVersion, IReadOnlyList<SecurityMasterEventEnvelope> Events)> Appends { get; } = new();

        public Task<IReadOnlyList<SecurityMasterEventEnvelope>> LoadAsync(Guid securityId, CancellationToken ct = default)
        {
            if (_version <= 0)
            {
                return Task.FromResult<IReadOnlyList<SecurityMasterEventEnvelope>>([]);
            }

            var envelope = new SecurityMasterEventEnvelope(
                GlobalSequence: _version,
                SecurityId: securityId,
                StreamVersion: _version,
                EventType: "seed",
                EventTimestamp: DateTimeOffset.UnixEpoch,
                Actor: "seed",
                CorrelationId: null,
                CausationId: null,
                Payload: default,
                Metadata: default);
            return Task.FromResult<IReadOnlyList<SecurityMasterEventEnvelope>>([envelope]);
        }

        public Task AppendAsync(Guid securityId, long expectedVersion, IReadOnlyList<SecurityMasterEventEnvelope> events, CancellationToken ct = default)
        {
            Appends.Add((securityId, expectedVersion, events));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<SecurityMasterEventEnvelope>> LoadSinceSequenceAsync(long sequenceExclusive, int take, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<SecurityMasterEventEnvelope>>([]);

        public Task<long> GetLatestSequenceAsync(CancellationToken ct = default) => Task.FromResult(_version);

        public Task AppendCorporateActionAsync(CorporateActionDto action, CancellationToken ct = default) => Task.CompletedTask;

        public Task<IReadOnlyList<CorporateActionDto>> LoadCorporateActionsAsync(Guid securityId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<CorporateActionDto>>([]);
    }

    private sealed class RecordingHandler : ISecurityMasterRevisionPublishedHandler
    {
        private readonly Action? _onHandle;
        private readonly List<int>? _invocationLog;

        public RecordingHandler(int order, Action? onHandle = null, List<int>? invocationLog = null)
        {
            Order = order;
            _onHandle = onHandle;
            _invocationLog = invocationLog;
        }

        public int Order { get; }

        public List<SecurityMasterRevisionPublishedEvent> Received { get; } = new();

        public Task HandleAsync(SecurityMasterRevisionPublishedEvent evt, CancellationToken ct = default)
        {
            _invocationLog?.Add(Order);
            Received.Add(evt);
            _onHandle?.Invoke();
            return Task.CompletedTask;
        }
    }
}
