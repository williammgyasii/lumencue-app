using ChurchProjection.UI.ViewModels.Operator;
using Xunit;

namespace ChurchProjection.Parsing.Tests;

public class MediaFolderOptionTests
{
    [Fact]
    public void Built_in_folders_cannot_be_deleted()
    {
        Assert.False(new MediaFolderOption(null, "All media", IsAll: true).CanDelete);
        Assert.False(new MediaFolderOption(null, "Uncategorized").CanDelete);
    }

    [Fact]
    public void User_folders_can_be_deleted()
    {
        Assert.True(new MediaFolderOption("streets", "Streets").CanDelete);
    }
}
