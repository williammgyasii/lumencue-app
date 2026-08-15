namespace ChurchProjection.UI.Services;

/// <summary>
/// Avalonia/Skia on Mac often stores RenderTargetBitmap pixels as RGBA.
/// NDI's VideoFrame FourCC is BGRA (straight, opaque). This converts one to the other.
/// </summary>
public static class NdiPixelConvert
{
    public static void ToStraightOpaqueBgra(byte[] pixels, int width, int height, int stride, bool sourceIsRgba)
    {
        for (var y = 0; y < height; y++)
        {
            var row = y * stride;
            for (var x = 0; x < width; x++)
            {
                var i = row + x * 4;
                byte c0 = pixels[i];
                byte c1 = pixels[i + 1];
                byte c2 = pixels[i + 2];
                byte a = pixels[i + 3];

                byte r, g, b;
                if (sourceIsRgba)
                {
                    r = c0;
                    g = c1;
                    b = c2;
                }
                else
                {
                    b = c0;
                    g = c1;
                    r = c2;
                }

                if (a == 0)
                {
                    r = g = b = 0;
                }
                else if (a < 255)
                {
                    r = (byte)Math.Min(255, r * 255 / a);
                    g = (byte)Math.Min(255, g * 255 / a);
                    b = (byte)Math.Min(255, b * 255 / a);
                }

                pixels[i] = b;
                pixels[i + 1] = g;
                pixels[i + 2] = r;
                pixels[i + 3] = 255;
            }
        }
    }
}
