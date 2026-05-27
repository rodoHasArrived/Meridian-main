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
    public void WorkstationStateFactories_ShouldCreateExpectedStateKinds()
    {
        WorkstationStateModel.Ready("Ready", "Usable").Kind.Should().Be(WorkstationStateKind.Ready);
        WorkstationStateModel.Loading("Loading", "Waiting").Kind.Should().Be(WorkstationStateKind.Loading);
        WorkstationStateModel.Empty("Empty", "Nothing found").Kind.Should().Be(WorkstationStateKind.Empty);
        WorkstationStateModel.Error("Failed", "Retry").Kind.Should().Be(WorkstationStateKind.Error);
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
