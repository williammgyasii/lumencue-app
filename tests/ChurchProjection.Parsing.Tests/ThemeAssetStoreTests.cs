using ChurchProjection.Infrastructure.Services;
using Xunit;

namespace ChurchProjection.Parsing.Tests;

// The asset store makes an imported theme self-contained: it copies the picked image into the app's
// own folder so the theme survives the operator moving/deleting the original file.
public class ThemeAssetStoreTests
{
    [Fact]
    public void Save_copies_the_image_into_the_store_and_leaves_the_original_untouched()
    {
        var temp = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var src = Path.Combine(temp, "design.png");
            var bytes = new byte[] { 1, 2, 3, 4 };
            File.WriteAllBytes(src, bytes);

            var store = new ThemeAssetStore(Path.Combine(temp, "data"));
            var stored = store.Save(src);

            Assert.True(File.Exists(stored));            // a copy now lives in the store
            Assert.True(File.Exists(src));               // original is left where it was
            Assert.NotEqual(src, stored);
            Assert.Equal(".png", Path.GetExtension(stored));
            Assert.Equal(bytes, File.ReadAllBytes(stored)); // it's a faithful copy
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    [Fact]
    public void Save_generates_a_unique_name_per_import_so_copies_never_collide()
    {
        var temp = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var src = Path.Combine(temp, "design.png");
            File.WriteAllBytes(src, new byte[] { 9 });

            var store = new ThemeAssetStore(Path.Combine(temp, "data"));
            var first = store.Save(src);
            var second = store.Save(src);

            Assert.NotEqual(first, second);
            Assert.True(File.Exists(first));
            Assert.True(File.Exists(second));
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }
}
