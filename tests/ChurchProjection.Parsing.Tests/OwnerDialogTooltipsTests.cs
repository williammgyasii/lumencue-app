using ChurchProjection.Core.Services;
using Xunit;

namespace ChurchProjection.Parsing.Tests;

public class OwnerDialogTooltipsTests
{
    [Fact]
    public void Hides_the_trigger_tip_while_a_dialog_is_open()
    {
        Assert.Null(OwnerDialogTooltips.TipWhileOpen(dialogOpen: true, saved: "Settings"));
    }

    [Fact]
    public void Restores_the_same_tip_after_the_dialog_closes()
    {
        Assert.Equal("Settings", OwnerDialogTooltips.TipWhileOpen(dialogOpen: false, saved: "Settings"));
    }
}
