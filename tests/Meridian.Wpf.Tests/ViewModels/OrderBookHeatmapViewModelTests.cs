using Meridian.Wpf.Models;
using Meridian.Wpf.ViewModels;
using Meridian.Wpf.Views;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class OrderBookHeatmapViewModelTests
{
    [Fact]
    public void RenderVersion_IncrementsWhenSnapshotOrControlHeightChanges()
    {
        var viewModel = new OrderBookHeatmapViewModel();
        long initialVersion = viewModel.RenderVersion;

        viewModel.SetControlHeight(240);

        viewModel.RenderVersion.Should().Be(initialVersion + 1);

        viewModel.UpdateFromSnapshot(
            new[] { new OrderBookDisplayLevel { RawPrice = 100.00m, RawSize = 10 } },
            new[] { new OrderBookDisplayLevel { RawPrice = 100.05m, RawSize = 12 } });

        viewModel.RenderVersion.Should().Be(initialVersion + 2);
    }

    [Fact]
    public void TryObserveRenderVersion_ReturnsFalseForRepeatedTicksWithoutSnapshotChanges()
    {
        var viewModel = new OrderBookHeatmapViewModel();
        long observedVersion = -1;

        OrderBookHeatmapControl.TryObserveRenderVersion(viewModel, ref observedVersion).Should().BeTrue();

        OrderBookHeatmapControl.TryObserveRenderVersion(viewModel, ref observedVersion).Should().BeFalse(
            "a second timer tick with the same render version must skip RenderFrame and bitmap copy work");

        viewModel.UpdateFromSnapshot(
            new[] { new OrderBookDisplayLevel { RawPrice = 100.00m, RawSize = 10 } },
            new[] { new OrderBookDisplayLevel { RawPrice = 100.05m, RawSize = 12 } });

        OrderBookHeatmapControl.TryObserveRenderVersion(viewModel, ref observedVersion).Should().BeTrue();
        OrderBookHeatmapControl.TryObserveRenderVersion(viewModel, ref observedVersion).Should().BeFalse();
    }
}
