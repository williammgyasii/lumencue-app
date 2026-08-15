using System.Collections.ObjectModel;
using ChurchProjection.UI.ViewModels;
using Xunit;

namespace ChurchProjection.Parsing.Tests;

public class OutputRowTests
{
    [Theory]
    [InlineData(OutputKind.Display, true)]
    [InlineData(OutputKind.Windowed, true)]
    [InlineData(OutputKind.ProPresenter, false)]
    [InlineData(OutputKind.Ndi, false)]
    public void CanRename_OnlyPhysicalAndWindowedOutputs(OutputKind kind, bool expected)
    {
        var row = new OutputRow("k", kind, "Name", null, new ObservableCollection<string>());
        Assert.Equal(expected, row.CanRename);
    }
}
