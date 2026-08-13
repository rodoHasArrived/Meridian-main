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
        harness.SetProjectionAssetTerms("Bond", new { issueDate = "2026-01-01", maturity = "2030-01-01", par = 100m });

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
    public async Task UpdateSecurityField_ClearingDateOverride_RevalidatesThePostClearOverlay()
    {
        // A CLEAR is an edit to the effective overlay too: with canonical endDate in March, a
        // staged endDate override in December, and a staged November startDate (valid against the
        // staged December end), clearing the endDate override reveals the canonical March end —
        // the retained November start would then violate the profile's date ordering, so the
        // clear must be refused instead of leaving an approvable start-after-end overlay.
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
        harness.Overrides
            .Setup(o => o.GetAsync(SecurityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperatorOverridesDto(
                SecurityId,
                new Dictionary<string, string>
                {
                    ["assetSpecificTerms.profileFields.endDate"] = "2026-12-01",
                    ["assetSpecificTerms.profileFields.startDate"] = "2026-11-01"
                },
                "ops.analyst",
                DateTimeOffset.UtcNow.AddMinutes(-5)));

        var request = new UpdateSecurityFieldRequest(
            SecurityId: SecurityId,
            ExpectedVersion: 3,
            FieldPath: "assetSpecificTerms.profileFields.endDate",
            NewValue: null,
            EffectiveFrom: DateTimeOffset.UtcNow,
            Actor: "ops.analyst",
            Justification: "Remove endDate override.");

        await harness.Service.Invoking(s => s.UpdateSecurityFieldAsync(request))
            .Should().ThrowAsync<ArgumentException>(
                "clearing the December endDate override reveals the canonical March end, behind the staged November start")
            .WithMessage("*PF_DATE_ORDER*");
    }

    [Fact]
    public async Task UpdateSecurityField_ClearingProfileFieldsReplacement_RevalidatesThePostClearOverlay()
    {
        // Clearing the WHOLE-OBJECT replacement is an edit to the effective overlay too: with a
        // canonical January–March window, a staged November–December replacement, and a staged
        // scalar November startDate (valid against the replacement's December end), clearing the
        // replacement reveals the canonical March end beneath the retained November start — the
        // clear must be refused instead of leaving an approvable start-after-end overlay.
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
        harness.Overrides
            .Setup(o => o.GetAsync(SecurityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperatorOverridesDto(
                SecurityId,
                new Dictionary<string, string>
                {
                    ["assetSpecificTerms.profileFields"] = """{"startDate":"2026-11-01","endDate":"2026-12-01"}""",
                    ["assetSpecificTerms.profileFields.startDate"] = "2026-11-01"
                },
                "ops.analyst",
                DateTimeOffset.UtcNow.AddMinutes(-5)));

        var request = new UpdateSecurityFieldRequest(
            SecurityId: SecurityId,
            ExpectedVersion: 3,
            FieldPath: "assetSpecificTerms.profileFields",
            NewValue: null,
            EffectiveFrom: DateTimeOffset.UtcNow,
            Actor: "ops.analyst",
            Justification: "Remove profileFields replacement.");

        await harness.Service.Invoking(s => s.UpdateSecurityFieldAsync(request))
            .Should().ThrowAsync<ArgumentException>(
                "clearing the November–December replacement reveals the canonical March end behind the staged November start")
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
            maturity = "2030-01-01",
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
            maturity = "2030-01-01",
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
    public async Task UpdateSecurityField_LegacyShapedFirstClassRecord_FailsClosedOnReconstruction()
    {
        // A record whose retained terms cannot round-trip the strict kind mapping (Bond maturity
        // stored only under the maturityDate alias) cannot have its invariants verified - legacy
        // shape is not permission to skip validation, so the edit is rejected until the record is
        // migrated to the canonical shape.
        var harness = new Harness(currentVersion: 3);
        harness.SetPassportAssetClass("Bond");
        harness.SetProjectionAssetTerms("Bond", new
        {
            issueDate = "2026-01-01",
            maturityDate = "2030-01-01",
            par = 100m
        });

        var request = new UpdateSecurityFieldRequest(
            SecurityId: SecurityId,
            ExpectedVersion: 3,
            FieldPath: "assetSpecificTerms.par",
            NewValue: "80",
            EffectiveFrom: DateTimeOffset.UtcNow,
            Actor: "ops.analyst",
            Justification: "Par correction.");

        await harness.Service.Invoking(s => s.UpdateSecurityFieldAsync(request))
            .Should().ThrowAsync<ArgumentException>()
            .WithMessage("*could not be reconstructed*");
    }

    [Fact]
    public async Task UpdateSecurityField_DraftCreationFails_CompensatesTheStagedOverride()
    {
        // If the draft revision cannot be created after the patch commits, no approval workflow
        // can ever govern the staged value - the overlay must revert so governed runs are not
        // blocked behind SM_OVERRIDE_APPROVAL_REQUIRED by an ungoverned Pending value.
        var harness = new Harness(currentVersion: 3, revisions: new ThrowingRevisionStore());
        harness.SetPassportAssetClass("Bond");
        harness.SetProjectionAssetTerms("Bond", new
        {
            issueDate = "2026-01-01",
            maturity = "2030-01-01",
            par = 100m
        });

        var request = new UpdateSecurityFieldRequest(
            SecurityId: SecurityId,
            ExpectedVersion: 3,
            FieldPath: "assetSpecificTerms.par",
            NewValue: "80",
            EffectiveFrom: DateTimeOffset.UtcNow,
            Actor: "ops.analyst",
            Justification: "Par correction.");

        await harness.Service.Invoking(s => s.UpdateSecurityFieldAsync(request))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*staged override was reverted*");

        // The original staging patch plus the compensating revert (removing the key, since no
        // prior override existed for the field).
        harness.Overrides.Verify(
            o => o.PatchAsync(
                It.IsAny<Guid>(),
                It.Is<OperatorOverridesPatchRequest>(patch =>
                    patch.RemoveKeys != null && patch.RemoveKeys.Contains("assetSpecificTerms.par")),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<long?>()),
            Times.Once);
    }

    [Fact]
    public async Task UpdateSecurityField_DraftCreationCanceled_StillCompensatesTheStagedOverride()
    {
        // Cancellation after the committed patch is a post-patch failure like any other: the
        // canceled request token must not leave a Pending override with no governing revision, so
        // the compensating revert runs on a non-canceled token before the cancellation propagates.
        var harness = new Harness(
            currentVersion: 3,
            revisions: new ThrowingRevisionStore(new OperationCanceledException("request aborted")));
        harness.SetPassportAssetClass("Bond");
        harness.SetProjectionAssetTerms("Bond", new
        {
            issueDate = "2026-01-01",
            maturity = "2030-01-01",
            par = 100m
        });

        var request = new UpdateSecurityFieldRequest(
            SecurityId: SecurityId,
            ExpectedVersion: 3,
            FieldPath: "assetSpecificTerms.par",
            NewValue: "80",
            EffectiveFrom: DateTimeOffset.UtcNow,
            Actor: "ops.analyst",
            Justification: "Par correction.");

        await harness.Service.Invoking(s => s.UpdateSecurityFieldAsync(request))
            .Should().ThrowAsync<OperationCanceledException>();

        harness.Overrides.Verify(
            o => o.PatchAsync(
                It.IsAny<Guid>(),
                It.Is<OperatorOverridesPatchRequest>(patch =>
                    patch.RemoveKeys != null && patch.RemoveKeys.Contains("assetSpecificTerms.par")),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<long?>()),
            Times.Once);
    }

    private sealed class ThrowingRevisionStore(Exception? toThrow = null) : ISecurityMasterRevisionStore
    {
        private Exception Failure => toThrow ?? new InvalidOperationException("revision store down");

        public Task<SecurityMasterRevisionRecord> CreateDraftAsync(Guid securityId, string actor, CancellationToken ct = default)
            => throw Failure;

        public Task<SecurityMasterRevisionRecord> CreateDraftAsync(
            Guid securityId, string actor, string fieldPath, DateTimeOffset fieldEffectiveFrom,
            string fieldJustification, string? fundProfileId = null,
            SecurityMasterRevisionFieldValue? fieldValue = null, CancellationToken ct = default)
            => throw Failure;

        public Task<SecurityMasterRevisionRecord?> GetAsync(Guid revisionId, CancellationToken ct = default)
            => Task.FromResult<SecurityMasterRevisionRecord?>(null);

        public Task<IReadOnlyList<SecurityMasterRevisionRecord>> ListBySecurityAsync(Guid securityId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<SecurityMasterRevisionRecord>>([]);

        public Task<SecurityMasterRevisionRecord> TransitionAsync(
            Guid revisionId, SecurityMasterRevisionStateDto expected, SecurityMasterRevisionStateDto next,
            string actor, Guid? workflowIdForSubmit = null, CancellationToken ct = default)
            => throw new InvalidOperationException("revision store down");
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
            maturity = "2030-01-01",
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
    public async Task UpdateSecurityField_ClearingRequiredTermOverride_ValidatesTheRevealedCanonicalValue()
    {
        // Clearing a staged maturity override removes only the OVERLAY value: the post-clear read
        // reveals the canonical maturity, so the reconstruction must validate that state. Deleting
        // the canonical term from the reconstruction envelope would model a Bond with no maturity,
        // fail the strict kind mapping, and wrongly refuse a legitimate clear.
        var harness = new Harness(currentVersion: 3);
        harness.SetPassportAssetClass("Bond");
        harness.SetProjectionAssetTerms("Bond", new
        {
            issueDate = "2026-01-01",
            maturity = "2030-01-01",
            par = 100m
        });
        harness.Overrides
            .Setup(o => o.GetAsync(SecurityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperatorOverridesDto(
                SecurityId,
                new Dictionary<string, string> { ["assetSpecificTerms.maturity"] = "2031-01-01" },
                "ops.analyst",
                DateTimeOffset.UtcNow.AddMinutes(-5)));

        var request = new UpdateSecurityFieldRequest(
            SecurityId: SecurityId,
            ExpectedVersion: 3,
            FieldPath: "assetSpecificTerms.maturity",
            NewValue: null,
            EffectiveFrom: DateTimeOffset.UtcNow,
            Actor: "ops.analyst",
            Justification: "Remove maturity override; the canonical maturity is correct.");

        var result = await harness.Service.UpdateSecurityFieldAsync(request);

        result.Should().NotBeNull();
        harness.Overrides.Verify(
            o => o.PatchAsync(
                It.IsAny<Guid>(),
                It.Is<OperatorOverridesPatchRequest>(patch =>
                    patch.RemoveKeys != null && patch.RemoveKeys.Contains("assetSpecificTerms.maturity")),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<long?>()),
            Times.Once);
    }

    [Fact]
    public async Task UpdateSecurityField_DraftCreationFails_RestoresThePriorApprovalDecision()
    {
        // The compensating patch resets the surviving overlay values to Pending; when the prior
        // overlay was already Approved, that would strand reviewed values behind
        // SM_OVERRIDE_APPROVAL_REQUIRED with no revision left to approve them — so the prior
        // decision is re-recorded with its original reviewer.
        var harness = new Harness(currentVersion: 3, revisions: new ThrowingRevisionStore());
        harness.SetPassportAssetClass("Bond");
        harness.SetProjectionAssetTerms("Bond", new
        {
            issueDate = "2026-01-01",
            maturity = "2030-01-01",
            par = 100m
        });
        harness.Overrides
            .Setup(o => o.GetAsync(SecurityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperatorOverridesDto(
                SecurityId,
                new Dictionary<string, string> { ["assetSpecificTerms.couponRate"] = "4.5" },
                "ops.analyst",
                DateTimeOffset.UtcNow.AddDays(-1))
            {
                ApprovalStatus = SecurityOverrideApprovalStatusDto.Approved,
                ReviewedBy = "risk.reviewer"
            });

        var request = new UpdateSecurityFieldRequest(
            SecurityId: SecurityId,
            ExpectedVersion: 3,
            FieldPath: "assetSpecificTerms.par",
            NewValue: "80",
            EffectiveFrom: DateTimeOffset.UtcNow,
            Actor: "ops.analyst",
            Justification: "Par correction.");

        await harness.Service.Invoking(s => s.UpdateSecurityFieldAsync(request))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*staged override was reverted*");

        harness.Overrides.Verify(
            o => o.RecordApprovalDecisionAsync(
                SecurityId,
                It.Is<OperatorOverrideDecision>(decision =>
                    decision.Decision == SecurityOverrideApprovalStatusDto.Approved
                    && decision.Reviewer == "risk.reviewer"),
                It.IsAny<CancellationToken>()),
            Times.Once);
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
            maturity = "2030-01-01",
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
            maturity = "2030-01-01",
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
                It.IsAny<long?>(),
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
    public async Task UpdateSecurityField_ProvenanceCanceled_StillReturnsTheDraft()
    {
        // By the time the provenance step runs, the patch is committed and the draft revision is
        // durably created — the edit HAS succeeded. A request token canceled during the
        // best-effort lineage write must not surface as a failed edit: the caller would retry or
        // compensate an edit whose staged override and governing draft already exist.
        var harness = new Harness(currentVersion: 3);
        harness.FieldProvenance
            .Setup(p => p.UpsertAsync(It.IsAny<SecurityFieldProvenanceRecord>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException("request aborted"));

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
            "cancellation during the best-effort lineage write must not hide the already-committed draft from the caller");
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
    public async Task Submit_WithoutWorkflow_IsRejectedAndTheRevisionStaysDraft()
    {
        // The only approval command requires the revision's BOUND workflow to match the approving
        // one, so a workflow-less submission could never be approved — it would strand permanently
        // in Submitted. The service boundary rejects it up front (matching the HTTP endpoint) and
        // the revision stays Draft for a corrected resubmission.
        var harness = new Harness(currentVersion: 2);
        var revisionId = await harness.SeedRevisionAsync(SecurityMasterRevisionStateDto.Draft);

        var request = new SubmitSecurityMasterRevisionRequest(
            SecurityId: SecurityId,
            RevisionId: revisionId,
            Actor: "ops.analyst",
            Note: "Ready for review.");

        await harness.Service.Invoking(s => s.SubmitForApprovalAsync(request))
            .Should().ThrowAsync<ArgumentException>()
            .WithMessage("*requires an approval workflow*");

        (await harness.Revisions.GetAsync(revisionId))!.State.Should().Be(SecurityMasterRevisionStateDto.Draft);
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
    public async Task Submit_WorkflowAlreadySubmitted_ReconcilesTheStrandedBinding()
    {
        // The gate submission is irreversible: if a prior attempt submitted the workflow but
        // failed before the revision transition committed, the workflow is Submitted while the
        // revision is still an unbound Draft — and the gate rejects every retry because the
        // workflow is no longer draft-state, so without reconciliation the revision could never
        // bind or submit while the orphaned workflow stayed independently approvable. A retry
        // against the already-submitted workflow completes the stranded revision-side transition
        // and binding instead of failing forever.
        var workflowId = Guid.NewGuid();
        var harness = new Harness(currentVersion: 2);
        var revisionId = await harness.SeedRevisionAsync(SecurityMasterRevisionStateDto.Draft);
        harness.Workflow
            .Setup(w => w.SubmitForApprovalAsync(workflowId, It.IsAny<OperationsSubmitApprovalRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperationsTransitionResultDto(
                false, "APPROVAL_ALREADY_SUBMITTED", "Workflow has already been submitted for approval.", null, [], []));
        harness.Workflow
            .Setup(w => w.GetAsync(workflowId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildWorkflowDto(
                workflowId, OperationsApprovalStateDto.Submitted, reviewer: "ops.reviewer",
                submissionRationale: $"Retry after a crashed submission. [security-master-revision:{revisionId:D}]"));

        var request = new SubmitSecurityMasterRevisionRequest(
            SecurityId: SecurityId,
            RevisionId: revisionId,
            Actor: "ops.analyst",
            Note: "Retry after a crashed submission.",
            FundProfileId: null,
            WorkflowId: workflowId,
            ExpectedWorkflowVersion: 2,
            Reviewer: "ops.reviewer",
            ReportPackId: "rp-1");

        var result = await harness.Service.SubmitForApprovalAsync(request);

        result.State.Should().Be(SecurityMasterRevisionStateDto.Submitted);
        var stored = await harness.Revisions.GetAsync(revisionId);
        stored!.State.Should().Be(SecurityMasterRevisionStateDto.Submitted,
            "the already-submitted workflow's recorded submission is reconciled onto the stranded revision");
        stored.WorkflowId.Should().Be(workflowId,
            "the reconciled submission still binds the workflow so approval stays restricted to this lane");
    }

    [Fact]
    public async Task Submit_WorkflowSubmittedForAnotherRevision_IsRejected()
    {
        // The reconciliation is scoped to the EXACT interrupted submission: the workflow's
        // recorded submission evidence carries a revision-identity marker, and a workflow
        // submitted for a DIFFERENT revision (even another security's) must not be claimable as
        // this draft's interrupted submission — binding it would let one gate approval decide
        // multiple unrelated edits.
        var workflowId = Guid.NewGuid();
        var harness = new Harness(currentVersion: 2);
        var revisionId = await harness.SeedRevisionAsync(SecurityMasterRevisionStateDto.Draft);
        harness.Workflow
            .Setup(w => w.SubmitForApprovalAsync(workflowId, It.IsAny<OperationsSubmitApprovalRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperationsTransitionResultDto(
                false, "APPROVAL_ALREADY_SUBMITTED", "Workflow has already been submitted for approval.", null, [], []));
        harness.Workflow
            .Setup(w => w.GetAsync(workflowId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildWorkflowDto(
                workflowId, OperationsApprovalStateDto.Submitted, reviewer: "ops.reviewer",
                submissionRationale: $"Another edit. [security-master-revision:{Guid.NewGuid():D}]"));

        var request = new SubmitSecurityMasterRevisionRequest(
            SecurityId: SecurityId,
            RevisionId: revisionId,
            Actor: "ops.analyst",
            Note: "Attempt to claim someone else's submitted workflow.",
            FundProfileId: null,
            WorkflowId: workflowId,
            ExpectedWorkflowVersion: 2,
            Reviewer: "ops.reviewer",
            ReportPackId: "rp-1");

        await harness.Service.Invoking(s => s.SubmitForApprovalAsync(request))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*blocked*");
        (await harness.Revisions.GetAsync(revisionId))!.State.Should().Be(SecurityMasterRevisionStateDto.Draft,
            "a workflow submitted for a different revision must not be claimed by this draft");
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
    public async Task Approve_OverrideDecisionFails_ApprovalStillSucceedsAndPublishConverges()
    {
        // A post-gate override-decision failure must not strand the flow: the gate approval is
        // irreversible, so the revision advances to Approved and PUBLISH - fail-closed and
        // retryable before its own transition - records the decision.
        var workflowId = Guid.NewGuid();
        var harness = new Harness(currentVersion: 4, handlers: [new RecordingHandler(order: 10)]);
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
        harness.Overrides
            .Setup(o => o.RecordApprovalDecisionAsync(SecurityId, It.IsAny<OperatorOverrideDecision>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("overlay store down"));

        // Approval succeeds despite the decision failure.
        await harness.Service.ApproveRevisionAsync(new ApproveSecurityMasterRevisionRequest(
            SecurityId, revisionId, workflowId, 4, "ops.actor", "ops.reviewer", "Approved.", "rp-1"));
        (await harness.Revisions.GetAsync(revisionId))!.State.Should().Be(SecurityMasterRevisionStateDto.Approved);

        // The overlay store recovers; publish converges the decision before transitioning.
        harness.Overrides
            .Setup(o => o.RecordApprovalDecisionAsync(SecurityId, It.IsAny<OperatorOverrideDecision>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperatorOverridesDto(SecurityId, new Dictionary<string, string>(), "ops.reviewer", DateTimeOffset.UtcNow));
        await harness.Service.PublishRevisionAsync(new PublishSecurityMasterRevisionRequest(
            SecurityId, revisionId, "ops.analyst", "ops.reviewer"));

        harness.Overrides.Verify(
            o => o.RecordApprovalDecisionAsync(
                SecurityId,
                It.Is<OperatorOverrideDecision>(decision => decision.Decision == SecurityOverrideApprovalStatusDto.Approved),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));
        (await harness.Revisions.GetAsync(revisionId))!.State.Should().Be(SecurityMasterRevisionStateDto.Published);
    }

    [Fact]
    public async Task Approve_OtherRevisionsStillStaged_DefersTheOverrideDecision()
    {
        // The override decision is SECURITY-level: with another revision still staged for the
        // same security, recording it would co-approve values no reviewer has seen, so the
        // overlay deliberately stays Pending.
        var workflowId = Guid.NewGuid();
        var harness = new Harness(currentVersion: 4);
        var revisionId = await harness.SeedRevisionAsync(
            SecurityMasterRevisionStateDto.Submitted, workflowId: workflowId);
        await harness.SeedRevisionAsync(SecurityMasterRevisionStateDto.Draft);
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
            o => o.RecordApprovalDecisionAsync(It.IsAny<Guid>(), It.IsAny<OperatorOverrideDecision>(), It.IsAny<CancellationToken>()),
            Times.Never);
        (await harness.Revisions.GetAsync(revisionId))!.State.Should().Be(SecurityMasterRevisionStateDto.Approved);
    }

    [Fact]
    public async Task Discard_DraftRevision_TransitionsToRejectedAndWithdrawsTheOverride()
    {
        // Discard is the terminal path for an abandoned draft: the revision transitions to
        // Rejected and its staged override value is withdrawn, so it stops deferring the
        // security-level override decision for every later revision.
        var harness = new Harness(currentVersion: 3);
        var draft = await harness.Revisions.CreateDraftAsync(
            SecurityId, "ops.analyst", "assetSpecificTerms.par", DateTimeOffset.UtcNow, "Par correction.");

        var result = await harness.Service.DiscardRevisionAsync(new DiscardSecurityMasterRevisionRequest(
            SecurityId, draft.RevisionId, "ops.analyst", "Abandoned draft."));

        result.State.Should().Be(SecurityMasterRevisionStateDto.Rejected);
        (await harness.Revisions.GetAsync(draft.RevisionId))!.State.Should().Be(SecurityMasterRevisionStateDto.Rejected);
        harness.Overrides.Verify(
            o => o.PatchAsync(
                SecurityId,
                It.Is<OperatorOverridesPatchRequest>(patch =>
                    patch.RemoveKeys != null && patch.RemoveKeys.Contains("assetSpecificTerms.par")),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<long?>()),
            Times.Once);
    }

    [Fact]
    public async Task Discard_OtherStagedRevisionGovernsTheSameField_LeavesTheOverrideInPlace()
    {
        // When another staged revision governs the same field path, the overlay key carries THAT
        // revision's value — withdrawing it would destroy a value still under review.
        var harness = new Harness(currentVersion: 3);
        var abandoned = await harness.Revisions.CreateDraftAsync(
            SecurityId, "ops.analyst", "assetSpecificTerms.par", DateTimeOffset.UtcNow.AddMinutes(-10), "First edit.");
        await Task.Delay(10);
        await harness.Revisions.CreateDraftAsync(
            SecurityId, "ops.analyst", "assetSpecificTerms.par", DateTimeOffset.UtcNow, "Second edit.");

        await harness.Service.DiscardRevisionAsync(new DiscardSecurityMasterRevisionRequest(
            SecurityId, abandoned.RevisionId, "ops.analyst"));

        (await harness.Revisions.GetAsync(abandoned.RevisionId))!.State.Should().Be(SecurityMasterRevisionStateDto.Rejected);
        harness.Overrides.Verify(
            o => o.PatchAsync(
                It.IsAny<Guid>(), It.IsAny<OperatorOverridesPatchRequest>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>(), It.IsAny<long?>()),
            Times.Never);
    }

    [Fact]
    public async Task Discard_LaterApprovedSiblingOwnsThePath_LeavesTheOverrideInPlace()
    {
        // An APPROVED later sibling owns the overlay key just as firmly as a staged one: its value
        // won review and is waiting only for publish to record the security-level decision.
        // Treating "Approved" as not-staged would let discarding a superseded draft withdraw a
        // reviewed, approved value — un-approving it without any rejection ever being recorded.
        var harness = new Harness(currentVersion: 3);
        var abandoned = await harness.Revisions.CreateDraftAsync(
            SecurityId, "ops.analyst", "assetSpecificTerms.par", DateTimeOffset.UtcNow.AddMinutes(-10), "First edit.");
        await Task.Delay(10);
        var approved = await harness.Revisions.CreateDraftAsync(
            SecurityId, "ops.analyst", "assetSpecificTerms.par", DateTimeOffset.UtcNow, "Winning edit.");
        await harness.Revisions.TransitionAsync(
            approved.RevisionId, SecurityMasterRevisionStateDto.Draft, SecurityMasterRevisionStateDto.Submitted, "ops.analyst");
        await harness.Revisions.TransitionAsync(
            approved.RevisionId, SecurityMasterRevisionStateDto.Submitted, SecurityMasterRevisionStateDto.Approved, "ops.reviewer");

        await harness.Service.DiscardRevisionAsync(new DiscardSecurityMasterRevisionRequest(
            SecurityId, abandoned.RevisionId, "ops.analyst"));

        (await harness.Revisions.GetAsync(abandoned.RevisionId))!.State.Should().Be(SecurityMasterRevisionStateDto.Rejected);
        harness.Overrides.Verify(
            o => o.PatchAsync(
                It.IsAny<Guid>(), It.IsAny<OperatorOverridesPatchRequest>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>(), It.IsAny<long?>()),
            Times.Never,
            "the approved sibling's reviewed value still governs the field path");
    }

    [Fact]
    public async Task Discard_StagedSibling_UnblocksTheDeferredOverrideDecision()
    {
        // The scenario the terminal path exists for: an approved revision's override decision was
        // deferred by a staged sibling. Discarding the sibling ends the deferral — the next
        // approval (or publish retry) records the security-level decision.
        var workflowId = Guid.NewGuid();
        var harness = new Harness(currentVersion: 4);
        var revisionId = await harness.SeedRevisionAsync(
            SecurityMasterRevisionStateDto.Submitted, workflowId: workflowId);
        var sibling = await harness.Revisions.CreateDraftAsync(
            SecurityId, "ops.analyst", "assetSpecificTerms.couponRate", DateTimeOffset.UtcNow, "Abandoned edit.");
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

        await harness.Service.DiscardRevisionAsync(new DiscardSecurityMasterRevisionRequest(
            SecurityId, sibling.RevisionId, "ops.analyst", "Abandoned."));
        await harness.Service.ApproveRevisionAsync(new ApproveSecurityMasterRevisionRequest(
            SecurityId, revisionId, workflowId, 4, "ops.actor", "ops.reviewer", "Approved.", "rp-1"));

        harness.Overrides.Verify(
            o => o.RecordApprovalDecisionAsync(
                SecurityId,
                It.Is<OperatorOverrideDecision>(decision =>
                    decision.Decision == SecurityOverrideApprovalStatusDto.Approved),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Discard_PublishedRevision_IsRejected()
    {
        // Only staged (Draft/Submitted) revisions can be discarded: a decided revision's value is
        // already governed, and rejecting it out of band would fork the recorded lifecycle.
        var harness = new Harness(currentVersion: 3);
        var revisionId = await harness.SeedRevisionAsync(SecurityMasterRevisionStateDto.Published);

        await harness.Service.Invoking(s => s.DiscardRevisionAsync(new DiscardSecurityMasterRevisionRequest(
                SecurityId, revisionId, "ops.analyst")))
            .Should().ThrowAsync<SecurityMasterRevisionStateException>()
            .WithMessage("*only Draft or Submitted revisions can be discarded*");
    }

    [Fact]
    public async Task Discard_LatestRevisionForTheField_WithdrawsTheValueInsteadOfLeavingItApprovable()
    {
        // Overlay ownership follows staging ORDER: revision A stages a par value, revision B later
        // replaces it. Discarding B must REMOVE the overlay key — leaving it in place would let
        // A's approval approve B's discarded value. A's superseded value is unrecoverable, so A
        // cannot stay approvable either: it is terminalized to Rejected with the withdrawal —
        // otherwise A could later approve and publish through a NotPending override decision with
        // its approved value present nowhere.
        var harness = new Harness(currentVersion: 3);
        var superseded = await harness.Revisions.CreateDraftAsync(
            SecurityId, "ops.analyst", "assetSpecificTerms.par", DateTimeOffset.UtcNow.AddMinutes(-10), "First edit (100).");
        await Task.Delay(10);
        var latest = await harness.Revisions.CreateDraftAsync(
            SecurityId, "ops.analyst", "assetSpecificTerms.par", DateTimeOffset.UtcNow, "Second edit (200).");

        await harness.Service.DiscardRevisionAsync(new DiscardSecurityMasterRevisionRequest(
            SecurityId, latest.RevisionId, "ops.analyst", "Second edit abandoned."));

        harness.Overrides.Verify(
            o => o.PatchAsync(
                SecurityId,
                It.Is<OperatorOverridesPatchRequest>(patch =>
                    patch.RemoveKeys != null && patch.RemoveKeys.Contains("assetSpecificTerms.par")),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<long?>()),
            Times.Once);
        (await harness.Revisions.GetAsync(superseded.RevisionId))!.State.Should().Be(
            SecurityMasterRevisionStateDto.Rejected,
            "the superseded draft's unrecoverable value must not stay approvable once the key is withdrawn");
    }

    [Fact]
    public async Task Discard_OlderSubmittedSiblingSharesThePath_RefusesTheDiscard()
    {
        // An older SUBMITTED same-path sibling is under active review — silently invalidating its
        // (unrecoverable) staged value is not the discarding actor's call, so the discard fails
        // closed before any transition: the sibling must be decided or discarded first.
        var harness = new Harness(currentVersion: 3);
        var submitted = await harness.Revisions.CreateDraftAsync(
            SecurityId, "ops.analyst", "assetSpecificTerms.par", DateTimeOffset.UtcNow.AddMinutes(-10), "First edit (100).");
        await harness.Revisions.TransitionAsync(
            submitted.RevisionId, SecurityMasterRevisionStateDto.Draft, SecurityMasterRevisionStateDto.Submitted, "ops.analyst");
        await Task.Delay(10);
        var latest = await harness.Revisions.CreateDraftAsync(
            SecurityId, "ops.analyst", "assetSpecificTerms.par", DateTimeOffset.UtcNow, "Second edit (200).");

        await harness.Service.Invoking(s => s.DiscardRevisionAsync(new DiscardSecurityMasterRevisionRequest(
                SecurityId, latest.RevisionId, "ops.analyst", "Second edit abandoned.")))
            .Should().ThrowAsync<SecurityMasterRevisionStateException>()
            .WithMessage("*already Submitted, Approved, or Published*");

        (await harness.Revisions.GetAsync(latest.RevisionId))!.State.Should().Be(SecurityMasterRevisionStateDto.Draft,
            "the refused discard must leave the revision untouched");
        harness.Overrides.Verify(
            o => o.PatchAsync(
                It.IsAny<Guid>(), It.IsAny<OperatorOverridesPatchRequest>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>(), It.IsAny<long?>()),
            Times.Never);
    }

    [Fact]
    public async Task Discard_LatestDraft_RestoresTheApprovedPredecessorsRecordedValue()
    {
        // The A-Approved / B-Draft deadlock: A's override decision is deferred while B is staged,
        // and B could not be discarded while A (older, Approved) shared the path — approving B was
        // the only escape, after which A would publish against B's value instead of the value A's
        // reviewer approved. With the staged value durably recorded on each revision, discarding B
        // RESTORES A's exact reviewed value to the overlay: A stays Approved, publishes its own
        // economics, and no sibling is invalidated.
        var harness = new Harness(currentVersion: 3);
        var approved = await harness.Revisions.CreateDraftAsync(
            SecurityId, "ops.analyst", "assetSpecificTerms.par", DateTimeOffset.UtcNow.AddMinutes(-10), "Reviewed edit (100).",
            fieldValue: new SecurityMasterRevisionFieldValue("100"));
        await harness.Revisions.TransitionAsync(
            approved.RevisionId, SecurityMasterRevisionStateDto.Draft, SecurityMasterRevisionStateDto.Submitted, "ops.analyst");
        await harness.Revisions.TransitionAsync(
            approved.RevisionId, SecurityMasterRevisionStateDto.Submitted, SecurityMasterRevisionStateDto.Approved, "ops.reviewer");
        await Task.Delay(10);
        var latest = await harness.Revisions.CreateDraftAsync(
            SecurityId, "ops.analyst", "assetSpecificTerms.par", DateTimeOffset.UtcNow, "Abandoned edit (200).",
            fieldValue: new SecurityMasterRevisionFieldValue("200"));

        await harness.Service.DiscardRevisionAsync(new DiscardSecurityMasterRevisionRequest(
            SecurityId, latest.RevisionId, "ops.analyst", "Second edit abandoned."));

        (await harness.Revisions.GetAsync(latest.RevisionId))!.State.Should().Be(SecurityMasterRevisionStateDto.Rejected);
        (await harness.Revisions.GetAsync(approved.RevisionId))!.State.Should().Be(SecurityMasterRevisionStateDto.Approved,
            "the approved predecessor keeps its decision — its exact value is restored, not invalidated");
        harness.Overrides.Verify(
            o => o.PatchAsync(
                SecurityId,
                It.Is<OperatorOverridesPatchRequest>(patch =>
                    patch.SetValues != null
                    && patch.SetValues.ContainsKey("assetSpecificTerms.par")
                    && patch.SetValues["assetSpecificTerms.par"] == "100"),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<long?>()),
            Times.Once,
            "the discard must restore the approved predecessor's exact recorded value");
    }

    [Fact]
    public async Task Discard_PredecessorRecordedAClear_RemovesTheKey()
    {
        // The latest remaining sibling's RECORDED intent can be a clear: restoring it means
        // removing the key, not resurrecting an older value.
        var harness = new Harness(currentVersion: 3);
        await harness.Revisions.CreateDraftAsync(
            SecurityId, "ops.analyst", "assetSpecificTerms.par", DateTimeOffset.UtcNow.AddMinutes(-10), "Clear par.",
            fieldValue: new SecurityMasterRevisionFieldValue(null));
        await Task.Delay(10);
        var latest = await harness.Revisions.CreateDraftAsync(
            SecurityId, "ops.analyst", "assetSpecificTerms.par", DateTimeOffset.UtcNow, "Assert par (200).",
            fieldValue: new SecurityMasterRevisionFieldValue("200"));

        await harness.Service.DiscardRevisionAsync(new DiscardSecurityMasterRevisionRequest(
            SecurityId, latest.RevisionId, "ops.analyst"));

        harness.Overrides.Verify(
            o => o.PatchAsync(
                SecurityId,
                It.Is<OperatorOverridesPatchRequest>(patch =>
                    patch.SetValues == null
                    && patch.RemoveKeys != null && patch.RemoveKeys.Contains("assetSpecificTerms.par")),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<long?>()),
            Times.Once,
            "the predecessor's recorded clear restores by removing the key");
    }

    [Fact]
    public async Task Discard_RediscardedAfterSiblingPublished_LeavesThePublishedValueInPlace()
    {
        // A re-discard retried after a later same-path sibling PUBLISHED (the original discard's
        // response was lost with its withdrawal incomplete) must find the published sibling
        // owning the key: withdrawing it would silently erase the value the sibling already
        // published through the full governed lifecycle.
        var harness = new Harness(currentVersion: 3);
        var rejected = await harness.Revisions.CreateDraftAsync(
            SecurityId, "ops.analyst", "assetSpecificTerms.par", DateTimeOffset.UtcNow.AddMinutes(-10), "Abandoned edit (100).",
            fieldValue: new SecurityMasterRevisionFieldValue("100"));
        await harness.Revisions.TransitionAsync(
            rejected.RevisionId, SecurityMasterRevisionStateDto.Draft, SecurityMasterRevisionStateDto.Rejected, "ops.analyst");
        await Task.Delay(10);
        var published = await harness.Revisions.CreateDraftAsync(
            SecurityId, "ops.analyst", "assetSpecificTerms.par", DateTimeOffset.UtcNow, "Winning edit (200).",
            fieldValue: new SecurityMasterRevisionFieldValue("200"));
        await harness.Revisions.TransitionAsync(
            published.RevisionId, SecurityMasterRevisionStateDto.Draft, SecurityMasterRevisionStateDto.Submitted, "ops.analyst");
        await harness.Revisions.TransitionAsync(
            published.RevisionId, SecurityMasterRevisionStateDto.Submitted, SecurityMasterRevisionStateDto.Approved, "ops.reviewer");
        await harness.Revisions.TransitionAsync(
            published.RevisionId, SecurityMasterRevisionStateDto.Approved, SecurityMasterRevisionStateDto.Published, "ops.analyst");

        await harness.Service.DiscardRevisionAsync(new DiscardSecurityMasterRevisionRequest(
            SecurityId, rejected.RevisionId, "ops.analyst", "Reconcile the incomplete discard."));

        harness.Overrides.Verify(
            o => o.PatchAsync(
                It.IsAny<Guid>(), It.IsAny<OperatorOverridesPatchRequest>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>(), It.IsAny<long?>()),
            Times.Never,
            "the published sibling owns the overlay key; the re-discard must not touch it");
    }

    [Fact]
    public async Task Discard_RediscardWithDecidedSurvivors_RestoresThePriorApprovalDecision()
    {
        // Re-discarding a stale key out of an overlay whose REMAINING values were already decided
        // (a later different-path revision approved and published while this revision sat
        // Rejected with its withdrawal incomplete) must not leave those published values re-gated
        // behind SM_OVERRIDE_APPROVAL_REQUIRED: the withdrawal patch resets the overlay to
        // Pending, so the prior decision is restored for the surviving dictionary — mirroring the
        // draft-creation compensation path.
        var harness = new Harness(currentVersion: 3);
        var rejected = await harness.Revisions.CreateDraftAsync(
            SecurityId, "ops.analyst", "assetSpecificTerms.par", DateTimeOffset.UtcNow.AddMinutes(-10), "Abandoned edit.",
            fieldValue: new SecurityMasterRevisionFieldValue("100"));
        await harness.Revisions.TransitionAsync(
            rejected.RevisionId, SecurityMasterRevisionStateDto.Draft, SecurityMasterRevisionStateDto.Rejected, "ops.analyst");
        harness.Overrides
            .Setup(o => o.GetAsync(SecurityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperatorOverridesDto(
                SecurityId,
                new Dictionary<string, string>
                {
                    ["assetSpecificTerms.par"] = "100",
                    ["assetSpecificTerms.couponRate"] = "4.5"
                },
                "ops.analyst",
                DateTimeOffset.UtcNow.AddMinutes(-5))
            {
                ApprovalStatus = SecurityOverrideApprovalStatusDto.Approved,
                ReviewedBy = "risk.reviewer"
            });
        harness.Overrides
            .Setup(o => o.PatchAsync(
                It.IsAny<Guid>(), It.IsAny<OperatorOverridesPatchRequest>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>(), It.IsAny<long?>()))
            .ReturnsAsync(new OperatorOverridesDto(
                SecurityId,
                new Dictionary<string, string> { ["assetSpecificTerms.couponRate"] = "4.5" },
                "ops.analyst",
                DateTimeOffset.UtcNow)
            {
                ApprovalStatus = SecurityOverrideApprovalStatusDto.Pending
            });

        await harness.Service.DiscardRevisionAsync(new DiscardSecurityMasterRevisionRequest(
            SecurityId, rejected.RevisionId, "ops.analyst", "Reconcile the incomplete discard."));

        harness.Overrides.Verify(
            o => o.RecordApprovalDecisionAsync(
                SecurityId,
                It.Is<OperatorOverrideDecision>(decision =>
                    decision.Decision == SecurityOverrideApprovalStatusDto.Approved
                    && decision.Reviewer == "risk.reviewer"),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "the surviving decided overlay must not be re-gated Pending by the stale key's withdrawal");
    }

    [Fact]
    public async Task Discard_RejectedRevision_ReconcilesAnIncompleteWithdrawal()
    {
        // A prior discard's transition committed but its withdrawal failed or was canceled:
        // re-discarding the Rejected revision skips the transition and completes the withdrawal
        // instead of stranding the Pending overlay value behind an unretryable state precondition.
        var harness = new Harness(currentVersion: 3);
        var draft = await harness.Revisions.CreateDraftAsync(
            SecurityId, "ops.analyst", "assetSpecificTerms.par", DateTimeOffset.UtcNow, "Par correction.");
        await harness.Revisions.TransitionAsync(
            draft.RevisionId, SecurityMasterRevisionStateDto.Draft, SecurityMasterRevisionStateDto.Rejected, "ops.analyst");

        var result = await harness.Service.DiscardRevisionAsync(new DiscardSecurityMasterRevisionRequest(
            SecurityId, draft.RevisionId, "ops.analyst", "Reconcile incomplete discard."));

        result.State.Should().Be(SecurityMasterRevisionStateDto.Rejected);
        harness.Overrides.Verify(
            o => o.PatchAsync(
                SecurityId,
                It.Is<OperatorOverridesPatchRequest>(patch =>
                    patch.RemoveKeys != null && patch.RemoveKeys.Contains("assetSpecificTerms.par")),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<long?>()),
            Times.Once);
    }

    [Fact]
    public async Task Discard_SubmittedRevision_RetiresTheBoundWorkflow()
    {
        // A discarded submission must not leave its bound workflow approvable: the generic
        // operations-continuity approval endpoint does not consult the revision, so an orphaned
        // Submitted workflow could later record approval evidence for the abandoned change. The
        // rejection is recorded under the ASSIGNED reviewer.
        var workflowId = Guid.NewGuid();
        var harness = new Harness(currentVersion: 3);
        var draft = await harness.Revisions.CreateDraftAsync(
            SecurityId, "ops.analyst", "assetSpecificTerms.par", DateTimeOffset.UtcNow, "Par correction.");
        await harness.Revisions.TransitionAsync(
            draft.RevisionId, SecurityMasterRevisionStateDto.Draft, SecurityMasterRevisionStateDto.Submitted,
            "ops.analyst", workflowIdForSubmit: workflowId);
        harness.Workflow
            .Setup(w => w.GetAsync(workflowId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildWorkflowDto(workflowId, OperationsApprovalStateDto.Submitted, reviewer: "ops.reviewer"));
        harness.Workflow
            .Setup(w => w.RejectWorkflowAsync(workflowId, It.IsAny<OperationsRejectWorkflowRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SuccessTransition(newVersion: 6));

        await harness.Service.DiscardRevisionAsync(new DiscardSecurityMasterRevisionRequest(
            SecurityId, draft.RevisionId, "ops.reviewer", "Submission abandoned."));

        harness.Workflow.Verify(
            w => w.RejectWorkflowAsync(
                workflowId,
                It.Is<OperationsRejectWorkflowRequestDto>(reject =>
                    reject.Actor == "ops.reviewer"
                    && reject.Reviewer == "ops.reviewer"
                    && reject.ReasonCode == "SecurityMasterRevisionDiscarded"),
                It.IsAny<CancellationToken>()),
            Times.Once);
        (await harness.Revisions.GetAsync(draft.RevisionId))!.State.Should().Be(SecurityMasterRevisionStateDto.Rejected);
    }

    [Fact]
    public async Task Discard_ActorIsNotTheAssignedReviewer_RefusesTheDiscard()
    {
        // Reviewer evidence must name the person who actually decided: when the bound workflow has
        // an assigned reviewer, another operator discarding the submission would record a
        // rejection attributed to a reviewer who never made the decision — so only the assigned
        // reviewer may discard it.
        var workflowId = Guid.NewGuid();
        var harness = new Harness(currentVersion: 3);
        var draft = await harness.Revisions.CreateDraftAsync(
            SecurityId, "ops.analyst", "assetSpecificTerms.par", DateTimeOffset.UtcNow, "Par correction.");
        await harness.Revisions.TransitionAsync(
            draft.RevisionId, SecurityMasterRevisionStateDto.Draft, SecurityMasterRevisionStateDto.Submitted,
            "ops.analyst", workflowIdForSubmit: workflowId);
        harness.Workflow
            .Setup(w => w.GetAsync(workflowId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildWorkflowDto(workflowId, OperationsApprovalStateDto.Submitted, reviewer: "ops.reviewer"));

        await harness.Service.Invoking(s => s.DiscardRevisionAsync(new DiscardSecurityMasterRevisionRequest(
                SecurityId, draft.RevisionId, "ops.analyst", "Trying to discard someone else's review.")))
            .Should().ThrowAsync<SecurityMasterRevisionStateException>()
            .WithMessage("*only the assigned reviewer may discard*");

        (await harness.Revisions.GetAsync(draft.RevisionId))!.State.Should().Be(SecurityMasterRevisionStateDto.Submitted);
        harness.Workflow.Verify(
            w => w.RejectWorkflowAsync(It.IsAny<Guid>(), It.IsAny<OperationsRejectWorkflowRequestDto>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task UpdateSecurityField_ReplacementOmitsOptionalDate_IsNotValidatedAgainstTheSupersededCanonical()
    {
        // A successfully parsed whole-object replacement REPLACES the object, including ABSENCE:
        // when it legitimately omits the optional endDate, a later scalar startDate edit must not
        // be rejected against the canonical endDate the replacement already removed.
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
            profileFields: new { startDate = "2026-01-01", endDate = "2026-02-01" });
        harness.Overrides
            .Setup(o => o.GetAsync(SecurityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperatorOverridesDto(
                SecurityId,
                new Dictionary<string, string>
                {
                    ["assetSpecificTerms.profileFields"] = """{"startDate":"2026-01-01"}"""
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
            Justification: "Move the start after the replacement removed the end date.");

        var result = await harness.Service.UpdateSecurityFieldAsync(request);

        result.Should().NotBeNull(
            "the staged replacement removed the optional endDate, so no date-order counterpart remains to violate");
    }

    [Fact]
    public async Task Discard_BoundWorkflowAlreadyApproved_RefusesTheDiscard()
    {
        // An already-Approved bound workflow is the stranded half-approval: discarding the
        // revision would orphan recorded approval evidence, so the discard is refused and the
        // approve-side reconciliation is the remedy.
        var workflowId = Guid.NewGuid();
        var harness = new Harness(currentVersion: 3);
        var draft = await harness.Revisions.CreateDraftAsync(
            SecurityId, "ops.analyst", "assetSpecificTerms.par", DateTimeOffset.UtcNow, "Par correction.");
        await harness.Revisions.TransitionAsync(
            draft.RevisionId, SecurityMasterRevisionStateDto.Draft, SecurityMasterRevisionStateDto.Submitted,
            "ops.analyst", workflowIdForSubmit: workflowId);
        harness.Workflow
            .Setup(w => w.GetAsync(workflowId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildWorkflowDto(workflowId, OperationsApprovalStateDto.Approved, reviewer: "ops.reviewer"));

        await harness.Service.Invoking(s => s.DiscardRevisionAsync(new DiscardSecurityMasterRevisionRequest(
                SecurityId, draft.RevisionId, "ops.analyst")))
            .Should().ThrowAsync<SecurityMasterRevisionStateException>()
            .WithMessage("*already Approved*");

        (await harness.Revisions.GetAsync(draft.RevisionId))!.State.Should().Be(SecurityMasterRevisionStateDto.Submitted);
        harness.Workflow.Verify(
            w => w.RejectWorkflowAsync(It.IsAny<Guid>(), It.IsAny<OperationsRejectWorkflowRequestDto>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static OperationsContinuityWorkflowDto BuildWorkflowDto(
        Guid workflowId, OperationsApprovalStateDto approvalState, string? reviewer = null,
        string? submissionRationale = null)
        => new(
            workflowId,
            Guid.NewGuid(),
            "2026-06",
            SecurityMasterSnapshotId: null,
            BrokerSource: "custodian",
            CreatedAtUtc: DateTimeOffset.UtcNow.AddDays(-1),
            UpdatedAtUtc: DateTimeOffset.UtcNow,
            Version: 5,
            OperationsWorkflowStatusDto.ApprovalPending,
            OperationsBrokerIntakeStateDto.Complete,
            OperationsSecurityMasterStateDto.Complete,
            OperationsLedgerPostingStateDto.Complete,
            OperationsReconciliationStateDto.Complete,
            approvalState,
            Gates: [],
            Timeline: [],
            BreakCases: [],
            LedgerPreview: null,
            Approvals:
            [
                new OperationsApprovalDto(
                    "approval-1",
                    approvalState,
                    "ops.analyst",
                    reviewer,
                    submissionRationale ?? "Submitted for review.",
                    DateTimeOffset.UtcNow.AddHours(-1),
                    null,
                    [])
            ],
            ReportPackReadiness: new OperationsReportPackReadinessDto(true, "rp-1", null, []),
            CloseChecklist: [],
            EvidenceLinks: [],
            Blockers: [],
            NextActions: []);

    [Fact]
    public async Task Approve_WorkflowAlreadyApproved_ReconcilesTheStrandedRevision()
    {
        // The gate approval is irreversible: if a prior attempt approved the workflow but crashed
        // before the revision transition, the revision is Submitted while the workflow is Approved
        // — and the gate rejects every retry (it only accepts Submitted/ReviewerAssigned
        // workflows), so without reconciliation the revision could never reach Approved or
        // Published. A retry against an already-Approved bound workflow completes the stranded
        // revision-side transition instead of failing forever.
        var workflowId = Guid.NewGuid();
        var harness = new Harness(currentVersion: 4);
        var revisionId = await harness.SeedRevisionAsync(
            SecurityMasterRevisionStateDto.Submitted, workflowId: workflowId);
        harness.Workflow
            .Setup(w => w.ApproveWorkflowAsync(workflowId, It.IsAny<OperationsApprovalDecisionRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperationsTransitionResultDto(
                false, "APPROVAL_SUBMISSION_REQUIRED", "Workflow must be submitted for approval before it can be approved.", null, [], []));
        harness.Workflow
            .Setup(w => w.GetAsync(workflowId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperationsContinuityWorkflowDto(
                workflowId,
                Guid.NewGuid(),
                "2026-06",
                SecurityMasterSnapshotId: null,
                BrokerSource: "custodian",
                CreatedAtUtc: DateTimeOffset.UtcNow.AddDays(-1),
                UpdatedAtUtc: DateTimeOffset.UtcNow,
                Version: 5,
                OperationsWorkflowStatusDto.ApprovalPending,
                OperationsBrokerIntakeStateDto.Complete,
                OperationsSecurityMasterStateDto.Complete,
                OperationsLedgerPostingStateDto.Complete,
                OperationsReconciliationStateDto.Complete,
                OperationsApprovalStateDto.Approved,
                Gates: [],
                Timeline: [],
                BreakCases: [],
                LedgerPreview: null,
                Approvals: [],
                ReportPackReadiness: new OperationsReportPackReadinessDto(true, "rp-1", null, []),
                CloseChecklist: [],
                EvidenceLinks: [],
                Blockers: [],
                NextActions: []));

        var result = await harness.Service.ApproveRevisionAsync(new ApproveSecurityMasterRevisionRequest(
            SecurityId, revisionId, workflowId, 4, "ops.actor", "ops.reviewer", "Approved.", "rp-1"));

        result.State.Should().Be(SecurityMasterRevisionStateDto.Approved);
        (await harness.Revisions.GetAsync(revisionId))!.State.Should().Be(SecurityMasterRevisionStateDto.Approved,
            "the already-approved workflow's decision is reconciled onto the stranded revision");
    }

    [Fact]
    public async Task Approve_ReconciledDecision_RecordsTheWorkflowReviewer()
    {
        // A reconciling retry did not decide anything — the workflow's retained approval did. The
        // retrying caller can be a DIFFERENT ModifySecurityMaster user (the endpoint server-binds
        // the request reviewer to the current actor), so the override decision recorded during
        // reconciliation must name the workflow's retained reviewer, not the retrying caller;
        // publish cannot repair the attribution later because the overlay is already Approved.
        var workflowId = Guid.NewGuid();
        var harness = new Harness(currentVersion: 4);
        var revisionId = await harness.SeedRevisionAsync(
            SecurityMasterRevisionStateDto.Submitted, workflowId: workflowId);
        harness.Workflow
            .Setup(w => w.ApproveWorkflowAsync(workflowId, It.IsAny<OperationsApprovalDecisionRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperationsTransitionResultDto(
                false, "APPROVAL_SUBMISSION_REQUIRED", "Workflow must be submitted for approval before it can be approved.", null, [], []));
        harness.Workflow
            .Setup(w => w.GetAsync(workflowId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildWorkflowDto(workflowId, OperationsApprovalStateDto.Approved, reviewer: "gate.reviewer"));
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
            SecurityId, revisionId, workflowId, 4, "retrying.caller", "retrying.caller", "Retry.", "rp-1"));

        harness.Overrides.Verify(
            o => o.RecordApprovalDecisionAsync(
                SecurityId,
                It.Is<OperatorOverrideDecision>(decision =>
                    decision.Decision == SecurityOverrideApprovalStatusDto.Approved
                    && decision.Reviewer == "gate.reviewer"),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "the reconciled override decision must name the workflow's retained reviewer, not the retrying caller");
    }

    [Fact]
    public async Task Approve_UngovernedOverlayKey_DefersTheDecision()
    {
        // The generic operator-overrides PATCH endpoint can add free-form keys to the same
        // security-level Values dictionary without creating a revision. The security-level
        // decision approves the ENTIRE dictionary, so approving a field revision while such a key
        // exists would silently approve a value no reviewer's workflow ever saw — the decision
        // defers until the ungoverned key is withdrawn or re-staged through the governed route.
        var workflowId = Guid.NewGuid();
        var harness = new Harness(currentVersion: 4);
        var revision = await harness.Revisions.CreateDraftAsync(
            SecurityId, "ops.analyst", "assetSpecificTerms.par", DateTimeOffset.UtcNow, "Par correction.",
            fieldValue: new SecurityMasterRevisionFieldValue("80"));
        await harness.Revisions.TransitionAsync(
            revision.RevisionId, SecurityMasterRevisionStateDto.Draft, SecurityMasterRevisionStateDto.Submitted, "ops.analyst",
            workflowIdForSubmit: workflowId);
        harness.Workflow
            .Setup(w => w.ApproveWorkflowAsync(workflowId, It.IsAny<OperationsApprovalDecisionRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SuccessTransition(newVersion: 5));
        harness.Overrides
            .Setup(o => o.GetAsync(SecurityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperatorOverridesDto(
                SecurityId,
                new Dictionary<string, string>
                {
                    ["assetSpecificTerms.par"] = "80",
                    ["annotations.freeform"] = "patched outside any revision"
                },
                "ops.analyst",
                DateTimeOffset.UtcNow)
            {
                ApprovalStatus = SecurityOverrideApprovalStatusDto.Pending
            });

        await harness.Service.ApproveRevisionAsync(new ApproveSecurityMasterRevisionRequest(
            SecurityId, revision.RevisionId, workflowId, 4, "ops.actor", "ops.reviewer", "Approved.", "rp-1"));

        harness.Overrides.Verify(
            o => o.RecordApprovalDecisionAsync(It.IsAny<Guid>(), It.IsAny<OperatorOverrideDecision>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "an overlay key with no governing revision must defer the security-level decision");
        (await harness.Revisions.GetAsync(revision.RevisionId))!.State.Should().Be(SecurityMasterRevisionStateDto.Approved,
            "the revision-side approval itself still completes; only the overlay decision defers");
    }

    [Fact]
    public async Task Approve_WhileFieldEditIsMidFlight_WaitsForTheGateAndDefersTheDecision()
    {
        // The approval's staged-revision check and override decision run under the SAME
        // per-security gate the field-edit route holds across its patch + draft creation. An
        // approval arriving while an edit sits between its committed patch and its draft creation
        // must wait for the gate and then observe the edit's draft — deferring the security-level
        // decision instead of co-approving the freshly Pending, unreviewed value.
        var workflowId = Guid.NewGuid();
        var revisions = new GatedRevisionStore();
        var harness = new Harness(currentVersion: 3, revisions: revisions);
        harness.SetPassportAssetClass("Bond");
        harness.SetProjectionAssetTerms("Bond", new
        {
            issueDate = "2026-01-01",
            maturity = "2030-01-01",
            par = 100m
        });
        var revisionId = await harness.SeedRevisionAsync(
            SecurityMasterRevisionStateDto.Submitted, workflowId: workflowId);
        harness.Workflow
            .Setup(w => w.ApproveWorkflowAsync(workflowId, It.IsAny<OperationsApprovalDecisionRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SuccessTransition(newVersion: 4));

        // The overlay turns Pending the moment the edit's patch commits — exactly the state an
        // ungated approval would co-approve.
        var patched = false;
        harness.Overrides
            .Setup(o => o.GetAsync(SecurityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => patched
                ? new OperatorOverridesDto(
                    SecurityId,
                    new Dictionary<string, string> { ["assetSpecificTerms.par"] = "80" },
                    "ops.analyst",
                    DateTimeOffset.UtcNow)
                {
                    ApprovalStatus = SecurityOverrideApprovalStatusDto.Pending
                }
                : null);
        harness.Overrides
            .Setup(o => o.PatchAsync(
                It.IsAny<Guid>(), It.IsAny<OperatorOverridesPatchRequest>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<long?>()))
            .ReturnsAsync((Guid id, OperatorOverridesPatchRequest _, string actor, CancellationToken _, long? _) =>
            {
                patched = true;
                return new OperatorOverridesDto(id, new Dictionary<string, string>(), actor, DateTimeOffset.UtcNow);
            });

        revisions.GateFieldEditDrafts = true;
        var editTask = harness.Service.UpdateSecurityFieldAsync(new UpdateSecurityFieldRequest(
            SecurityId: SecurityId,
            ExpectedVersion: 3,
            FieldPath: "assetSpecificTerms.par",
            NewValue: "80",
            EffectiveFrom: DateTimeOffset.UtcNow,
            Actor: "ops.analyst",
            Justification: "Par correction."));
        await revisions.DraftEntered.Task;

        // The edit has committed its patch and is parked inside draft creation: start the approval
        // and give it a head start — without the shared gate it would record the decision now.
        var approveTask = harness.Service.ApproveRevisionAsync(new ApproveSecurityMasterRevisionRequest(
            SecurityId, revisionId, workflowId, 3, "ops.actor", "ops.reviewer", "Approved.", "rp-1"));
        await Task.Delay(100);
        revisions.ReleaseDrafts.TrySetResult();

        await Task.WhenAll(editTask, approveTask);

        harness.Overrides.Verify(
            o => o.RecordApprovalDecisionAsync(It.IsAny<Guid>(), It.IsAny<OperatorOverrideDecision>(), It.IsAny<CancellationToken>()),
            Times.Never);
        (await harness.Revisions.GetAsync(revisionId))!.State.Should().Be(SecurityMasterRevisionStateDto.Approved);
    }

    [Fact]
    public async Task Submit_WhileFieldEditIsMidFlight_WaitsForTheGate()
    {
        // The ENTIRE submission — preflight, workflow mutation, revision transition — runs under
        // the same per-security gate the field-edit and discard routes hold. Without it, a discard
        // could reject the revision and withdraw its overlay while the submission awaits the
        // external workflow gate, committing a Submitted workflow the discard never retired.
        var workflowId = Guid.NewGuid();
        var revisions = new GatedRevisionStore();
        var harness = new Harness(currentVersion: 2, revisions: revisions);
        harness.SetPassportAssetClass("Bond");
        harness.SetProjectionAssetTerms("Bond", new
        {
            issueDate = "2026-01-01",
            maturity = "2030-01-01",
            par = 100m
        });
        var draft = await harness.Revisions.CreateDraftAsync(SecurityId, "ops.analyst");
        harness.Workflow
            .Setup(w => w.SubmitForApprovalAsync(workflowId, It.IsAny<OperationsSubmitApprovalRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SuccessTransition(newVersion: 3));

        revisions.GateFieldEditDrafts = true;
        var editTask = harness.Service.UpdateSecurityFieldAsync(new UpdateSecurityFieldRequest(
            SecurityId: SecurityId,
            ExpectedVersion: 2,
            FieldPath: "assetSpecificTerms.par",
            NewValue: "80",
            EffectiveFrom: DateTimeOffset.UtcNow,
            Actor: "ops.analyst",
            Justification: "Par correction."));
        await revisions.DraftEntered.Task;

        var submitTask = harness.Service.SubmitForApprovalAsync(new SubmitSecurityMasterRevisionRequest(
            SecurityId: SecurityId,
            RevisionId: draft.RevisionId,
            Actor: "ops.analyst",
            Note: "Submit while an edit is mid-flight.",
            FundProfileId: null,
            WorkflowId: workflowId,
            ExpectedWorkflowVersion: 2,
            Reviewer: "ops.reviewer",
            ReportPackId: "rp-1"));
        await Task.Delay(100);
        submitTask.IsCompleted.Should().BeFalse(
            "the submission must wait for the per-security gate the in-flight edit holds");

        revisions.ReleaseDrafts.TrySetResult();
        await Task.WhenAll(editTask, submitTask);

        (await harness.Revisions.GetAsync(draft.RevisionId))!.State.Should().Be(SecurityMasterRevisionStateDto.Submitted);
    }

    [Fact]
    public async Task Discard_LastDeferringDraft_ConvergesThePublishedOwnersDecision()
    {
        // A published owner's restored value can sit Pending because an UNRELATED draft defers
        // the security-level decision. Discarding that last deferring draft leaves no approval or
        // publish operation that could ever converge the overlay (published revisions never
        // re-enter the lifecycle), so the discard itself converges it — recording Approved with
        // the reviewer retained by the published revision's bound workflow.
        var workflowId = Guid.NewGuid();
        var harness = new Harness(currentVersion: 3);
        var published = await harness.Revisions.CreateDraftAsync(
            SecurityId, "ops.analyst", "assetSpecificTerms.par", DateTimeOffset.UtcNow.AddMinutes(-20), "Winning edit (100).",
            fieldValue: new SecurityMasterRevisionFieldValue("100"));
        await harness.Revisions.TransitionAsync(
            published.RevisionId, SecurityMasterRevisionStateDto.Draft, SecurityMasterRevisionStateDto.Submitted, "ops.analyst",
            workflowIdForSubmit: workflowId);
        await harness.Revisions.TransitionAsync(
            published.RevisionId, SecurityMasterRevisionStateDto.Submitted, SecurityMasterRevisionStateDto.Approved, "ops.reviewer");
        await harness.Revisions.TransitionAsync(
            published.RevisionId, SecurityMasterRevisionStateDto.Approved, SecurityMasterRevisionStateDto.Published, "ops.analyst");
        await Task.Delay(10);
        var deferring = await harness.Revisions.CreateDraftAsync(
            SecurityId, "ops.analyst", "assetSpecificTerms.couponRate", DateTimeOffset.UtcNow, "Abandoned edit.",
            fieldValue: new SecurityMasterRevisionFieldValue("5"));
        harness.Workflow
            .Setup(w => w.GetAsync(workflowId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildWorkflowDto(workflowId, OperationsApprovalStateDto.Approved, reviewer: "gate.reviewer"));
        harness.Overrides
            .Setup(o => o.GetAsync(SecurityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperatorOverridesDto(
                SecurityId,
                new Dictionary<string, string> { ["assetSpecificTerms.par"] = "100" },
                "ops.analyst",
                DateTimeOffset.UtcNow)
            {
                ApprovalStatus = SecurityOverrideApprovalStatusDto.Pending
            });

        await harness.Service.DiscardRevisionAsync(new DiscardSecurityMasterRevisionRequest(
            SecurityId, deferring.RevisionId, "ops.analyst", "Abandoned."));

        harness.Overrides.Verify(
            o => o.RecordApprovalDecisionAsync(
                SecurityId,
                It.Is<OperatorOverrideDecision>(decision =>
                    decision.Decision == SecurityOverrideApprovalStatusDto.Approved
                    && decision.Reviewer == "gate.reviewer"),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "removing the last deferring draft must converge the published owner's decision");
    }

    [Fact]
    public async Task Discard_LatestDraft_ConvergesThePublishedRestoreOwnersDecision()
    {
        // The published restore owner's decision re-converges INSIDE the discard's gate-held
        // region, and the decision seam acquires the SAME non-reentrant per-security gate — the
        // discard must route through the seam's under-gate core, because re-entering the gated
        // wrapper would deadlock the discard right after it restored the published value.
        var workflowId = Guid.NewGuid();
        var harness = new Harness(currentVersion: 3);
        var published = await harness.Revisions.CreateDraftAsync(
            SecurityId, "ops.analyst", "assetSpecificTerms.par", DateTimeOffset.UtcNow.AddMinutes(-20), "Winning edit (100).",
            fieldValue: new SecurityMasterRevisionFieldValue("100"));
        await harness.Revisions.TransitionAsync(
            published.RevisionId, SecurityMasterRevisionStateDto.Draft, SecurityMasterRevisionStateDto.Submitted, "ops.analyst",
            workflowIdForSubmit: workflowId);
        await harness.Revisions.TransitionAsync(
            published.RevisionId, SecurityMasterRevisionStateDto.Submitted, SecurityMasterRevisionStateDto.Approved, "ops.reviewer");
        await harness.Revisions.TransitionAsync(
            published.RevisionId, SecurityMasterRevisionStateDto.Approved, SecurityMasterRevisionStateDto.Published, "ops.analyst");
        await Task.Delay(10);
        var latest = await harness.Revisions.CreateDraftAsync(
            SecurityId, "ops.analyst", "assetSpecificTerms.par", DateTimeOffset.UtcNow, "Abandoned edit (200).",
            fieldValue: new SecurityMasterRevisionFieldValue("200"));
        harness.Workflow
            .Setup(w => w.GetAsync(workflowId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildWorkflowDto(workflowId, OperationsApprovalStateDto.Approved, reviewer: "gate.reviewer"));
        // The overlay tracks the restoration patch: at decision time it must carry the published
        // owner's restored "100" (the value its reviewer approved), not the discarded draft's.
        var overlayPar = "200";
        var decided = false;
        harness.Overrides
            .Setup(o => o.PatchAsync(
                SecurityId, It.IsAny<OperatorOverridesPatchRequest>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<long?>()))
            .ReturnsAsync((Guid id, OperatorOverridesPatchRequest patch, string actor, CancellationToken _, long? _) =>
            {
                if (patch.SetValues is not null && patch.SetValues.TryGetValue("assetSpecificTerms.par", out var restoredValue))
                {
                    overlayPar = restoredValue;
                }

                return new OperatorOverridesDto(id, new Dictionary<string, string>(), actor, DateTimeOffset.UtcNow);
            });
        harness.Overrides
            .Setup(o => o.GetAsync(SecurityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new OperatorOverridesDto(
                SecurityId,
                new Dictionary<string, string> { ["assetSpecificTerms.par"] = overlayPar },
                "ops.analyst",
                DateTimeOffset.UtcNow)
            {
                ApprovalStatus = decided
                    ? SecurityOverrideApprovalStatusDto.Approved
                    : SecurityOverrideApprovalStatusDto.Pending
            });
        harness.Overrides
            .Setup(o => o.RecordApprovalDecisionAsync(SecurityId, It.IsAny<OperatorOverrideDecision>(), It.IsAny<CancellationToken>()))
            .Callback(() => decided = true)
            .ReturnsAsync((OperatorOverridesDto)null!);

        await harness.Service.DiscardRevisionAsync(new DiscardSecurityMasterRevisionRequest(
                SecurityId, latest.RevisionId, "ops.analyst", "Second edit abandoned."))
            .WaitAsync(TimeSpan.FromSeconds(10));

        harness.Overrides.Verify(
            o => o.RecordApprovalDecisionAsync(
                SecurityId,
                It.Is<OperatorOverrideDecision>(decision =>
                    decision.Decision == SecurityOverrideApprovalStatusDto.Approved
                    && decision.Reviewer == "gate.reviewer"),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "restoring a published owner's value must re-record its decision without re-acquiring the held gate");
    }

    [Fact]
    public async Task Discard_LastDeferringDraft_UngovernedKey_LeavesTheOverlayPending()
    {
        // Convergence records Approved for the ENTIRE surviving overlay, so it enforces the same
        // per-key governance rule as the decision seam: a free-form key patched in through the
        // generic overrides route has no revision evidence, and converging past it would approve
        // a value no published reviewer ever saw. The overlay stays Pending instead.
        var workflowId = Guid.NewGuid();
        var harness = new Harness(currentVersion: 3);
        var published = await harness.Revisions.CreateDraftAsync(
            SecurityId, "ops.analyst", "assetSpecificTerms.par", DateTimeOffset.UtcNow.AddMinutes(-20), "Winning edit (100).",
            fieldValue: new SecurityMasterRevisionFieldValue("100"));
        await harness.Revisions.TransitionAsync(
            published.RevisionId, SecurityMasterRevisionStateDto.Draft, SecurityMasterRevisionStateDto.Submitted, "ops.analyst",
            workflowIdForSubmit: workflowId);
        await harness.Revisions.TransitionAsync(
            published.RevisionId, SecurityMasterRevisionStateDto.Submitted, SecurityMasterRevisionStateDto.Approved, "ops.reviewer");
        await harness.Revisions.TransitionAsync(
            published.RevisionId, SecurityMasterRevisionStateDto.Approved, SecurityMasterRevisionStateDto.Published, "ops.analyst");
        await Task.Delay(10);
        var deferring = await harness.Revisions.CreateDraftAsync(
            SecurityId, "ops.analyst", "assetSpecificTerms.couponRate", DateTimeOffset.UtcNow, "Abandoned edit.",
            fieldValue: new SecurityMasterRevisionFieldValue("5"));
        harness.Workflow
            .Setup(w => w.GetAsync(workflowId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildWorkflowDto(workflowId, OperationsApprovalStateDto.Approved, reviewer: "gate.reviewer"));
        harness.Overrides
            .Setup(o => o.GetAsync(SecurityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperatorOverridesDto(
                SecurityId,
                new Dictionary<string, string>
                {
                    ["assetSpecificTerms.par"] = "100",
                    ["annotations.freeform"] = "patched outside any revision"
                },
                "ops.analyst",
                DateTimeOffset.UtcNow)
            {
                ApprovalStatus = SecurityOverrideApprovalStatusDto.Pending
            });

        await harness.Service.DiscardRevisionAsync(new DiscardSecurityMasterRevisionRequest(
            SecurityId, deferring.RevisionId, "ops.analyst", "Abandoned."));

        harness.Overrides.Verify(
            o => o.RecordApprovalDecisionAsync(It.IsAny<Guid>(), It.IsAny<OperatorOverrideDecision>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "an overlay key with no governing revision must keep the convergence deferred");
    }

    [Fact]
    public async Task Approve_HistoricalWholeRecordRevision_DoesNotWaiveTheUngovernedKeyScan()
    {
        // Only the decision's OWN revision being whole-record exempts the ungoverned-key scan:
        // that reviewer reviewed the record and its overlay as one unit. A HISTORICAL Published
        // whole-record revision reviewed the overlay as it was THEN — its reviewer never saw keys
        // patched in afterwards, so one old row must not waive the scan for every later
        // field-level decision.
        var workflowId = Guid.NewGuid();
        var harness = new Harness(currentVersion: 4);
        var legacy = await harness.Revisions.CreateDraftAsync(SecurityId, "ops.analyst");
        await harness.Revisions.TransitionAsync(
            legacy.RevisionId, SecurityMasterRevisionStateDto.Draft, SecurityMasterRevisionStateDto.Submitted, "ops.analyst");
        await harness.Revisions.TransitionAsync(
            legacy.RevisionId, SecurityMasterRevisionStateDto.Submitted, SecurityMasterRevisionStateDto.Approved, "ops.reviewer");
        await harness.Revisions.TransitionAsync(
            legacy.RevisionId, SecurityMasterRevisionStateDto.Approved, SecurityMasterRevisionStateDto.Published, "ops.analyst");
        await Task.Delay(10);
        var revision = await harness.Revisions.CreateDraftAsync(
            SecurityId, "ops.analyst", "assetSpecificTerms.par", DateTimeOffset.UtcNow, "Par correction.",
            fieldValue: new SecurityMasterRevisionFieldValue("80"));
        await harness.Revisions.TransitionAsync(
            revision.RevisionId, SecurityMasterRevisionStateDto.Draft, SecurityMasterRevisionStateDto.Submitted, "ops.analyst",
            workflowIdForSubmit: workflowId);
        harness.Workflow
            .Setup(w => w.ApproveWorkflowAsync(workflowId, It.IsAny<OperationsApprovalDecisionRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SuccessTransition(newVersion: 5));
        harness.Overrides
            .Setup(o => o.GetAsync(SecurityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperatorOverridesDto(
                SecurityId,
                new Dictionary<string, string>
                {
                    ["assetSpecificTerms.par"] = "80",
                    ["annotations.freeform"] = "patched outside any revision"
                },
                "ops.analyst",
                DateTimeOffset.UtcNow)
            {
                ApprovalStatus = SecurityOverrideApprovalStatusDto.Pending
            });

        await harness.Service.ApproveRevisionAsync(new ApproveSecurityMasterRevisionRequest(
            SecurityId, revision.RevisionId, workflowId, 4, "ops.actor", "ops.reviewer", "Approved.", "rp-1"));

        harness.Overrides.Verify(
            o => o.RecordApprovalDecisionAsync(It.IsAny<Guid>(), It.IsAny<OperatorOverrideDecision>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "a historical whole-record revision must not waive the ungoverned-key scan for a field-level decision");
    }

    [Fact]
    public async Task Approve_DecisionConvergenceCanceled_StillReportsTheApprovedRevision()
    {
        // The gate approval and the Submitted→Approved transition are irreversible by the time
        // the override decision converges, so everything past the transition runs on
        // CancellationToken.None with a catch-all: a canceled convergence must not surface the
        // committed approval as a canceled request — publish converges the decision later,
        // fail-closed.
        var workflowId = Guid.NewGuid();
        var harness = new Harness(currentVersion: 4);
        var revisionId = await harness.SeedRevisionAsync(
            SecurityMasterRevisionStateDto.Submitted, workflowId: workflowId);
        harness.Workflow
            .Setup(w => w.ApproveWorkflowAsync(workflowId, It.IsAny<OperationsApprovalDecisionRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SuccessTransition(newVersion: 5));
        harness.Overrides
            .Setup(o => o.GetAsync(SecurityId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var result = await harness.Service.ApproveRevisionAsync(new ApproveSecurityMasterRevisionRequest(
            SecurityId, revisionId, workflowId, 4, "ops.actor", "ops.reviewer", "Approved.", "rp-1"));

        result.State.Should().Be(SecurityMasterRevisionStateDto.Approved,
            "the durable approval must be reported even when the best-effort decision convergence is canceled");
        (await harness.Revisions.GetAsync(revisionId))!.State.Should().Be(SecurityMasterRevisionStateDto.Approved);
    }

    [Fact]
    public async Task PatchOperatorOverrides_WhileFieldEditIsMidFlight_WaitsForTheGateThenRefuses()
    {
        // The generic overrides patch seam holds the same per-security gate as the field-edit,
        // approve, submit, and discard routes: a free-form key must not land between an in-flight
        // edit's committed patch and its draft creation. And once the gate is released, the seam
        // observes the edit's freshly staged Draft and refuses the patch outright — the overlay is
        // mid-review, and a free-form mutation would change what the draft's reviewer decides
        // over.
        var revisions = new GatedRevisionStore();
        var harness = new Harness(currentVersion: 2, revisions: revisions);
        harness.SetPassportAssetClass("Bond");
        harness.SetProjectionAssetTerms("Bond", new
        {
            issueDate = "2026-01-01",
            maturity = "2030-01-01",
            par = 100m
        });

        revisions.GateFieldEditDrafts = true;
        var editTask = harness.Service.UpdateSecurityFieldAsync(new UpdateSecurityFieldRequest(
            SecurityId: SecurityId,
            ExpectedVersion: 2,
            FieldPath: "assetSpecificTerms.par",
            NewValue: "80",
            EffectiveFrom: DateTimeOffset.UtcNow,
            Actor: "ops.analyst",
            Justification: "Par correction."));
        await revisions.DraftEntered.Task;

        var patchTask = harness.Service.PatchOperatorOverridesAsync(
            SecurityId,
            new OperatorOverridesPatchRequest(
                SetValues: new Dictionary<string, string> { ["annotations.freeform"] = "raw patch" },
                RemoveKeys: null),
            "ops.analyst");
        await Task.Delay(100);
        patchTask.IsCompleted.Should().BeFalse(
            "the raw patch must wait for the per-security gate the in-flight edit holds");

        revisions.ReleaseDrafts.TrySetResult();
        await editTask;
        await FluentActions.Awaiting(() => patchTask)
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*governed revision workflow*");
    }

    [Fact]
    public async Task PatchOperatorOverrides_NothingStaged_AppliesThePatch()
    {
        // With no staged revisions, the generic route remains the free annotation surface — the
        // seam only adds gate serialization, not a new gate on legacy usage.
        var harness = new Harness(currentVersion: 2);

        await harness.Service.PatchOperatorOverridesAsync(
            SecurityId,
            new OperatorOverridesPatchRequest(
                SetValues: new Dictionary<string, string> { ["annotations.freeform"] = "raw patch" },
                RemoveKeys: null),
            "ops.analyst");

        harness.Overrides.Verify(
            o => o.PatchAsync(
                SecurityId, It.IsAny<OperatorOverridesPatchRequest>(), "ops.analyst",
                It.IsAny<CancellationToken>(), It.IsAny<long?>()),
            Times.Once);
    }

    [Fact]
    public async Task Discard_GenericReplacementAfterStaging_LeavesTheReplacementInPlace()
    {
        // A generic-route patch replaced the key AFTER the revision staged its value (possible
        // while the revision sat Rejected awaiting a re-discard, when nothing blocks patches).
        // The discarded revision no longer owns the key's current value, so the discard must not
        // withdraw or restore over the newer replacement it never staged.
        var harness = new Harness(currentVersion: 3);
        var rejected = await harness.Revisions.CreateDraftAsync(
            SecurityId, "ops.analyst", "assetSpecificTerms.par", DateTimeOffset.UtcNow, "Abandoned edit (100).",
            fieldValue: new SecurityMasterRevisionFieldValue("100"));
        await harness.Revisions.TransitionAsync(
            rejected.RevisionId, SecurityMasterRevisionStateDto.Draft, SecurityMasterRevisionStateDto.Rejected, "ops.analyst");
        harness.Overrides
            .Setup(o => o.GetAsync(SecurityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperatorOverridesDto(
                SecurityId,
                new Dictionary<string, string> { ["assetSpecificTerms.par"] = "999" },
                "ops.analyst",
                DateTimeOffset.UtcNow)
            {
                ApprovalStatus = SecurityOverrideApprovalStatusDto.Pending
            });

        await harness.Service.DiscardRevisionAsync(new DiscardSecurityMasterRevisionRequest(
            SecurityId, rejected.RevisionId, "ops.analyst", "Reconcile the incomplete discard."));

        harness.Overrides.Verify(
            o => o.PatchAsync(
                It.IsAny<Guid>(), It.IsAny<OperatorOverridesPatchRequest>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>(), It.IsAny<long?>()),
            Times.Never,
            "the discard must not delete or overwrite the newer generic replacement it never staged");
    }

    [Fact]
    public async Task Approve_ReplacedGovernedKeyValue_DefersTheDecision()
    {
        // Governance binds to the reviewed VALUE, not mere path existence: after a revision staged
        // par=80, the generic overrides route can replace the key with 999 in place. The revision's
        // approval must not co-approve that replacement on the strength of the old review — the
        // decision defers until the value is withdrawn or re-staged.
        var workflowId = Guid.NewGuid();
        var harness = new Harness(currentVersion: 4);
        var revision = await harness.Revisions.CreateDraftAsync(
            SecurityId, "ops.analyst", "assetSpecificTerms.par", DateTimeOffset.UtcNow, "Par correction.",
            fieldValue: new SecurityMasterRevisionFieldValue("80"));
        await harness.Revisions.TransitionAsync(
            revision.RevisionId, SecurityMasterRevisionStateDto.Draft, SecurityMasterRevisionStateDto.Submitted, "ops.analyst",
            workflowIdForSubmit: workflowId);
        harness.Workflow
            .Setup(w => w.ApproveWorkflowAsync(workflowId, It.IsAny<OperationsApprovalDecisionRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SuccessTransition(newVersion: 5));
        harness.Overrides
            .Setup(o => o.GetAsync(SecurityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperatorOverridesDto(
                SecurityId,
                new Dictionary<string, string> { ["assetSpecificTerms.par"] = "999" },
                "ops.analyst",
                DateTimeOffset.UtcNow)
            {
                ApprovalStatus = SecurityOverrideApprovalStatusDto.Pending
            });

        await harness.Service.ApproveRevisionAsync(new ApproveSecurityMasterRevisionRequest(
            SecurityId, revision.RevisionId, workflowId, 4, "ops.actor", "ops.reviewer", "Approved.", "rp-1"));

        harness.Overrides.Verify(
            o => o.RecordApprovalDecisionAsync(It.IsAny<Guid>(), It.IsAny<OperatorOverrideDecision>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "an overlay value no revision staged must defer the security-level decision even when the path matches");
    }

    [Fact]
    public async Task RecordOperatorOverrideDecision_StagedRevisionPending_Refuses()
    {
        // The legacy decision route has none of the revision lifecycle's controls. While a staged
        // revision is pending, a direct decision would decide its staged value without the bound
        // workflow's review — the editor could approve their own Draft.
        var harness = new Harness(currentVersion: 3);
        await harness.Revisions.CreateDraftAsync(
            SecurityId, "ops.analyst", "assetSpecificTerms.par", DateTimeOffset.UtcNow, "Par correction.",
            fieldValue: new SecurityMasterRevisionFieldValue("80"));

        await harness.Service.Invoking(s => s.RecordOperatorOverrideDecisionAsync(
                SecurityId,
                new OperatorOverrideDecision(SecurityOverrideApprovalStatusDto.Approved, "ops.analyst", "Looks right.")))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*governed revision workflow*");

        harness.Overrides.Verify(
            o => o.RecordApprovalDecisionAsync(It.IsAny<Guid>(), It.IsAny<OperatorOverrideDecision>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RecordOperatorOverrideDecision_RevisionBackedOverlay_Refuses()
    {
        // Even with nothing staged, an overlay carrying revision-backed values gets its decision
        // from the approve/publish/discard seams — a direct decision would bypass the reviewer
        // evidence those seams record.
        var harness = new Harness(currentVersion: 3);
        var published = await harness.Revisions.CreateDraftAsync(
            SecurityId, "ops.analyst", "assetSpecificTerms.par", DateTimeOffset.UtcNow.AddMinutes(-10), "Winning edit (100).",
            fieldValue: new SecurityMasterRevisionFieldValue("100"));
        await harness.Revisions.TransitionAsync(
            published.RevisionId, SecurityMasterRevisionStateDto.Draft, SecurityMasterRevisionStateDto.Submitted, "ops.analyst");
        await harness.Revisions.TransitionAsync(
            published.RevisionId, SecurityMasterRevisionStateDto.Submitted, SecurityMasterRevisionStateDto.Approved, "ops.reviewer");
        await harness.Revisions.TransitionAsync(
            published.RevisionId, SecurityMasterRevisionStateDto.Approved, SecurityMasterRevisionStateDto.Published, "ops.analyst");
        harness.Overrides
            .Setup(o => o.GetAsync(SecurityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperatorOverridesDto(
                SecurityId,
                new Dictionary<string, string> { ["assetSpecificTerms.par"] = "100" },
                "ops.analyst",
                DateTimeOffset.UtcNow)
            {
                ApprovalStatus = SecurityOverrideApprovalStatusDto.Pending
            });

        await harness.Service.Invoking(s => s.RecordOperatorOverrideDecisionAsync(
                SecurityId,
                new OperatorOverrideDecision(SecurityOverrideApprovalStatusDto.Approved, "ops.reviewer", "Confirmed.")))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*governed revision workflow*");

        harness.Overrides.Verify(
            o => o.RecordApprovalDecisionAsync(It.IsAny<Guid>(), It.IsAny<OperatorOverrideDecision>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RecordOperatorOverrideDecision_LegacyOverlay_RecordsTheDecision()
    {
        // A pure legacy overlay — free-form values, no revisions anywhere — is exactly what the
        // legacy decision route exists for; the governed seam passes it through under the gate.
        var harness = new Harness(currentVersion: 3);
        harness.Overrides
            .Setup(o => o.GetAsync(SecurityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperatorOverridesDto(
                SecurityId,
                new Dictionary<string, string> { ["annotations.freeform"] = "legacy note" },
                "ops.analyst",
                DateTimeOffset.UtcNow)
            {
                ApprovalStatus = SecurityOverrideApprovalStatusDto.Pending
            });

        await harness.Service.RecordOperatorOverrideDecisionAsync(
            SecurityId,
            new OperatorOverrideDecision(SecurityOverrideApprovalStatusDto.Approved, "ops.reviewer", "Reviewed."));

        harness.Overrides.Verify(
            o => o.RecordApprovalDecisionAsync(
                SecurityId,
                It.Is<OperatorOverrideDecision>(decision =>
                    decision.Decision == SecurityOverrideApprovalStatusDto.Approved
                    && decision.Reviewer == "ops.reviewer"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task UpdateSecurityField_EditBeforeTheProfilesEffectiveWindow_IsRejected()
    {
        // The pinned profile's governance window gates overlay writes exactly as it gates
        // canonical create/amend (SM_CUSTOM_PROFILE_VERSION_NOT_EFFECTIVE): an edit effective on
        // a date the pinned version never governed must not stage. The read-side validator
        // deliberately accepts historical pins so records stay interpretable, which makes this
        // write-time gate the only line keeping new overlay economics from entering under a
        // schema that did not govern the edit's effective date.
        var harness = new Harness(currentVersion: 3);
        harness.SetPassportAssetClass("CustomAsset");
        harness.SetProjectionProfileEnvelope("structured-credit-io-po", profileVersion: 1);

        var request = new UpdateSecurityFieldRequest(
            SecurityId: SecurityId,
            ExpectedVersion: 3,
            FieldPath: "assetSpecificTerms.profileFields.currentFactor",
            NewValue: "0.5",
            EffectiveFrom: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            Actor: "ops.analyst",
            Justification: "Backdated factor correction.");

        await harness.Service.Invoking(s => s.UpdateSecurityFieldAsync(request))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*does not cover the edit's effective date*");
    }

    [Fact]
    public async Task Publish_FieldEditDuringDecisionToTransitionWindow_WaitsForThePublishGate()
    {
        // The publish's override decision and Approved→Published transition hold the per-security
        // field-edit gate as ONE step: with the gate released between them, a concurrent field
        // edit could reset the overlay to Pending after the decision recorded, and the revision
        // would be marked Published while SM_OVERRIDE_APPROVAL_REQUIRED still blocks its
        // economics — unretryably, since the Approved precondition rejects any republish.
        var revisions = new GatedRevisionStore { GatePublishTransitions = true };
        var harness = new Harness(currentVersion: 3, revisions: revisions);
        harness.SetPassportAssetClass("Bond");
        harness.SetProjectionAssetTerms("Bond", new
        {
            issueDate = "2026-01-01",
            maturity = "2030-01-01",
            par = 100m
        });
        var revisionId = await harness.SeedRevisionAsync(SecurityMasterRevisionStateDto.Approved);
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

        var publishTask = harness.Service.PublishRevisionAsync(new PublishSecurityMasterRevisionRequest(
            SecurityId: SecurityId,
            RevisionId: revisionId,
            Actor: "ops.analyst",
            ApproverActor: "ops.reviewer"));
        await revisions.PublishTransitionEntered.Task;

        // The decision has recorded and the transition is parked — exactly the window an ungated
        // edit would exploit to reset the overlay to Pending.
        var editTask = harness.Service.UpdateSecurityFieldAsync(new UpdateSecurityFieldRequest(
            SecurityId: SecurityId,
            ExpectedVersion: 3,
            FieldPath: "assetSpecificTerms.par",
            NewValue: "80",
            EffectiveFrom: DateTimeOffset.UtcNow,
            Actor: "ops.analyst",
            Justification: "Par correction."));
        await Task.Delay(100);
        editTask.IsCompleted.Should().BeFalse(
            "the edit must wait until the publish's decision+transition step releases the gate");

        revisions.ReleasePublishTransitions.TrySetResult();
        await Task.WhenAll(publishTask, editTask);

        (await harness.Revisions.GetAsync(revisionId))!.State.Should().Be(SecurityMasterRevisionStateDto.Published);
    }

    private sealed class GatedRevisionStore : ISecurityMasterRevisionStore
    {
        private readonly InMemorySecurityMasterRevisionStore _inner = new();

        public bool GateFieldEditDrafts { get; set; }
        public TaskCompletionSource DraftEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseDrafts { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool GatePublishTransitions { get; set; }
        public TaskCompletionSource PublishTransitionEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleasePublishTransitions { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<SecurityMasterRevisionRecord> CreateDraftAsync(Guid securityId, string actor, CancellationToken ct = default)
            => _inner.CreateDraftAsync(securityId, actor, ct);

        public async Task<SecurityMasterRevisionRecord> CreateDraftAsync(
            Guid securityId, string actor, string fieldPath, DateTimeOffset fieldEffectiveFrom,
            string fieldJustification, string? fundProfileId = null,
            SecurityMasterRevisionFieldValue? fieldValue = null, CancellationToken ct = default)
        {
            if (GateFieldEditDrafts)
            {
                DraftEntered.TrySetResult();
                await ReleaseDrafts.Task;
            }

            return await _inner.CreateDraftAsync(securityId, actor, fieldPath, fieldEffectiveFrom, fieldJustification, fundProfileId, fieldValue, ct);
        }

        public Task<SecurityMasterRevisionRecord?> GetAsync(Guid revisionId, CancellationToken ct = default)
            => _inner.GetAsync(revisionId, ct);

        public Task<IReadOnlyList<SecurityMasterRevisionRecord>> ListBySecurityAsync(Guid securityId, CancellationToken ct = default)
            => _inner.ListBySecurityAsync(securityId, ct);

        public async Task<SecurityMasterRevisionRecord> TransitionAsync(
            Guid revisionId, SecurityMasterRevisionStateDto expected, SecurityMasterRevisionStateDto next,
            string actor, Guid? workflowIdForSubmit = null, CancellationToken ct = default)
        {
            if (GatePublishTransitions && next == SecurityMasterRevisionStateDto.Published)
            {
                PublishTransitionEntered.TrySetResult();
                await ReleasePublishTransitions.Task;
            }

            return await _inner.TransitionAsync(revisionId, expected, next, actor, workflowIdForSubmit, ct);
        }
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
    public async Task Publish_OtherRevisionsStillStaged_FailsRetryablyInsteadOfPublishingThroughTheDeferral()
    {
        // A DEFERRED override decision must stop the publish: other revisions for the security are
        // still staged, so the overlay stays Pending — transitioning to Published anyway would
        // report a completed publish while SM_OVERRIDE_APPROVAL_REQUIRED still blocks the
        // economics. The revision stays Approved; once the other staged revision is decided, the
        // same publish retries to completion.
        var harness = new Harness(currentVersion: 4, handlers: [new RecordingHandler(order: 10)]);
        var revisionId = await harness.SeedRevisionAsync(SecurityMasterRevisionStateDto.Approved);
        var stagedDraftId = await harness.SeedRevisionAsync(SecurityMasterRevisionStateDto.Draft);
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

        var request = new PublishSecurityMasterRevisionRequest(
            SecurityId: SecurityId,
            RevisionId: revisionId,
            Actor: "ops.analyst",
            ApproverActor: "ops.reviewer");

        await harness.Service.Invoking(s => s.PublishRevisionAsync(request))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*still staged*");
        (await harness.Revisions.GetAsync(revisionId))!.State.Should().Be(SecurityMasterRevisionStateDto.Approved,
            "a deferred override decision must leave the publish retryable");
        harness.Overrides.Verify(
            o => o.RecordApprovalDecisionAsync(It.IsAny<Guid>(), It.IsAny<OperatorOverrideDecision>(), It.IsAny<CancellationToken>()),
            Times.Never);

        // The other staged revision is decided (approved through its own gate) and the SAME
        // publish retries to completion, recording the override decision on the way.
        await harness.Revisions.TransitionAsync(
            stagedDraftId, SecurityMasterRevisionStateDto.Draft, SecurityMasterRevisionStateDto.Submitted, "ops.analyst");
        await harness.Revisions.TransitionAsync(
            stagedDraftId, SecurityMasterRevisionStateDto.Submitted, SecurityMasterRevisionStateDto.Approved, "ops.reviewer");
        await harness.Service.PublishRevisionAsync(request);
        (await harness.Revisions.GetAsync(revisionId))!.State.Should().Be(SecurityMasterRevisionStateDto.Published);
        harness.Overrides.Verify(
            o => o.RecordApprovalDecisionAsync(
                SecurityId,
                It.Is<OperatorOverrideDecision>(decision => decision.Decision == SecurityOverrideApprovalStatusDto.Approved),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Publish_ConvergedDecision_RecordsTheWorkflowReviewer()
    {
        // The publish body's ApproverActor is caller-supplied text — trusting it would let a
        // publisher persist ANY name as the reviewer of governed approval evidence. When the
        // publish converges a deferred override decision, the recorded reviewer must come from
        // the bound approved workflow (who actually decided at the gate), not from the request.
        var workflowId = Guid.NewGuid();
        var harness = new Harness(currentVersion: 4, handlers: [new RecordingHandler(order: 10)]);
        var revisionId = await harness.SeedRevisionAsync(
            SecurityMasterRevisionStateDto.Approved, workflowId: workflowId);
        harness.Workflow
            .Setup(w => w.GetAsync(workflowId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildWorkflowDto(workflowId, OperationsApprovalStateDto.Approved, reviewer: "gate.reviewer"));
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

        var request = new PublishSecurityMasterRevisionRequest(
            SecurityId: SecurityId,
            RevisionId: revisionId,
            Actor: "ops.analyst",
            ApproverActor: "spoofed.reviewer");

        await harness.Service.PublishRevisionAsync(request);

        harness.Overrides.Verify(
            o => o.RecordApprovalDecisionAsync(
                SecurityId,
                It.Is<OperatorOverrideDecision>(decision =>
                    decision.Decision == SecurityOverrideApprovalStatusDto.Approved
                    && decision.Reviewer == "gate.reviewer"),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "the recorded reviewer must be the workflow's gate reviewer, never the caller-supplied ApproverActor");
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
            Meridian.ReferenceData.SecurityMaster.ISecurityAssetProfileCatalog? assetProfileCatalog = null,
            ISecurityMasterRevisionStore? revisions = null)
        {
            _assetProfileCatalog = assetProfileCatalog
                ?? Meridian.ReferenceData.SecurityMaster.StaticSecurityAssetProfileCatalog.CreateDefault();
            if (revisions is not null)
            {
                Revisions = revisions;
            }

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
