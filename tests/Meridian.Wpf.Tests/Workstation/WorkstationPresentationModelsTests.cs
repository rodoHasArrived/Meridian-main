using System.Collections.ObjectModel;
using System.Linq;
using Meridian.Wpf.Models;
using Meridian.Wpf.Workstation.Models;

namespace Meridian.Wpf.Tests.Workstation;

public sealed class WorkstationPresentationModelsTests
{
    [Fact]
    public void FromWorkspaceCommand_ShouldPreserveCommandIdentityAndDisabledReason()
    {
        var command = new WorkspaceCommandItem
        {
            Id = "Refresh",
            Label = "Refresh",
            Description = "Refresh provider health.",
            Glyph = "\uE72C",
            Tone = WorkspaceTone.Primary,
            IsEnabled = false,
            ShortcutHint = "Ctrl+R"
        };

        var mapped = WorkstationCommandMapper.FromWorkspaceCommand(command);

        mapped.Id.Should().Be("Refresh");
        mapped.Label.Should().Be("Refresh");
        mapped.Description.Should().Be("Refresh provider health.");
        mapped.Glyph.Should().Be("\uE72C");
        mapped.Tone.Should().Be(WorkspaceTone.Primary);
        mapped.IsEnabled.Should().BeFalse();
        mapped.DisabledReason.Should().Be("Refresh provider health.");
        mapped.ShortcutHint.Should().Be("Ctrl+R");
    }

    [Fact]
    public void FromWorkspaceCommand_WhenDisabledReasonIsSet_ShouldPreferItOverDescription()
    {
        var command = new WorkspaceCommandItem
        {
            Id = "PostJournal",
            Label = "Post journal",
            Description = "Post the staged journal entries.",
            DisabledReason = "Close the prior period before posting.",
            IsEnabled = false
        };

        var mapped = WorkstationCommandMapper.FromWorkspaceCommand(command);

        mapped.DisabledReason.Should().Be("Close the prior period before posting.");
    }

    [Fact]
    public void FromWorkspaceCommand_WhenCommandIsEnabled_ShouldNotPublishADisabledReason()
    {
        var command = new WorkspaceCommandItem
        {
            Id = "PostJournal",
            Label = "Post journal",
            Description = "Post the staged journal entries.",
            DisabledReason = "Close the prior period before posting.",
            IsEnabled = true
        };

        var mapped = WorkstationCommandMapper.FromWorkspaceCommand(command);

        mapped.DisabledReason.Should().BeEmpty();
    }

    [Fact]
    public void AutomationId_ShouldPreferTheDeclaredCommandId()
    {
        var command = new WorkspaceCommandItem { Id = "PostJournal", Label = "Post journal" };

        command.AutomationId.Should().Be("PostJournal");
        WorkstationCommandMapper.FromWorkspaceCommand(command).AutomationId.Should().Be("PostJournal");
    }

    [Fact]
    public void AutomationId_WhenCommandIdIsMissing_ShouldNormalizeTheLabel()
    {
        var command = new WorkspaceCommandItem { Label = "Post journal (staged)" };

        command.AutomationId.Should().Be("WorkspaceCommandPostjournalstaged");
        WorkstationCommandMapper.FromWorkspaceCommand(command).AutomationId
            .Should().Be("WorkstationCommandPostjournalstaged");
    }

    [Fact]
    public void AutomationId_WhenIdAndLabelAreMissing_ShouldStayStableRatherThanEmpty()
    {
        new WorkspaceCommandItem().AutomationId.Should().Be("WorkspaceCommandAction");
    }

    [Fact]
    public void AutomationId_ShouldNotChangeWhenDisplayCopyChanges()
    {
        var before = new WorkspaceCommandItem { Id = "PostJournal", Label = "Post journal" };
        var after = new WorkspaceCommandItem { Id = "PostJournal", Label = "Post journal entries" };

        after.AutomationId.Should().Be(before.AutomationId);
    }

    [Fact]
    public void WorkstationStateFactories_ShouldCreateExpectedStateKinds()
    {
        WorkstationStateModel.Ready("Ready", "Usable").Kind.Should().Be(WorkstationStateKind.Ready);
        WorkstationStateModel.Loading("Loading", "Waiting").Kind.Should().Be(WorkstationStateKind.Loading);
        WorkstationStateModel.Empty("Empty", "Nothing found").Kind.Should().Be(WorkstationStateKind.Empty);
        WorkstationStateModel.Error("Failed", "Retry").Kind.Should().Be(WorkstationStateKind.Error);
    }

    [Fact]
    public void WorkstationStateModel_ShouldExposeAffordanceAndSignifierCollections()
    {
        var posture = new WorkstationActionPostureModel(
            "Resolve",
            "Capture the operator decision.",
            "/api/workstation/reconciliation/break-queue/br-1",
            "Fund operations lead",
            WorkstationReadinessTone.Blocked,
            WorkspaceTone.Danger);

        var state = WorkstationStateModel.Blocked(
            "Open reconciliation break",
            "The close lane is blocked by an unresolved break.",
            posture,
            [new WorkstationEvidenceLinkModel("Break evidence", "/api/workstation/reconciliation/break-queue/br-1", "shared endpoint")],
            [new WorkstationRecoveryActionModel("Start review", "Assign ownership and move the break into review.")],
            new WorkstationSignoffRequirementModel("Fund operations lead", "pending-signoff"));

        state.ReadinessTone.Should().Be(WorkstationReadinessTone.Blocked);
        state.ActionPosture.Should().Be(posture);
        state.VisibleEvidenceLinks.Should().ContainSingle().Which.Label.Should().Be("Break evidence");
        state.VisibleRecoveryActions.Should().ContainSingle().Which.Label.Should().Be("Start review");
        state.SignoffRequirement!.Role.Should().Be("Fund operations lead");
    }

    [Fact]
    public void WorkstationCommandModel_ShouldCarryPostureEvidenceAndTargetText()
    {
        var posture = new WorkstationActionPostureModel(
            "Review handoff",
            "Review the linked evidence before distributing the report pack.",
            "FundReportPack",
            "Accounting operator",
            WorkstationReadinessTone.EvidenceLinked,
            WorkspaceTone.Info);

        var command = new WorkstationCommandModel(
            "ReportPackReview",
            "Review",
            "Review the report pack.",
            "\uE8A5",
            WorkspaceTone.Primary,
            Posture: posture,
            EvidenceSourceText: "Generated preview",
            TargetText: "FundReportPack",
            GroupLabel: "Reporting");

        command.Posture.Should().Be(posture);
        command.EvidenceSourceText.Should().Be("Generated preview");
        command.TargetText.Should().Be("FundReportPack");
        command.GroupLabel.Should().Be("Reporting");
    }

    [Fact]
    public void WorkstationTableModel_ShouldExposeRowsThroughGenericAndBaseContracts()
    {
        var rows = new ObservableCollection<RowFixture>
        {
            new("Alpaca", "Healthy")
        };

        var table = new WorkstationTableModel<RowFixture>(
            rows,
            [new("Provider", nameof(RowFixture.Name), 120)]);

        table.Rows.Should().ContainSingle().Which.Name.Should().Be("Alpaca");
        ((WorkstationTableModel)table).Rows.Cast<object>().Should().ContainSingle();
    }

    private sealed record RowFixture(string Name, string Status);
}
