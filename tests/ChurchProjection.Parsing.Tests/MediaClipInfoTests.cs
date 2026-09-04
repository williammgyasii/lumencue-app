using System.Buffers.Binary;
using System.Text;
using ChurchProjection.Core.Services;
using Xunit;

namespace ChurchProjection.Parsing.Tests;

public class MediaClipInfoTests
{
    [Fact]
    public void Reads_duration_and_silent_video()
    {
        var bytes = BuildMovie(timescale: 600, duration: 33164, audio: false);

        var info = MediaClipInfo.TryRead(new MemoryStream(bytes));

        Assert.NotNull(info);
        Assert.Equal(55_273, info.Value.DurationMs);
        Assert.False(info.Value.HasAudio);
        Assert.Equal("0:55 · mute", info.Value.TileBadge);
    }

    [Fact]
    public void Reads_eleven_minute_clip_with_audio()
    {
        var bytes = BuildMovie(timescale: 48000, duration: 31892960, audio: true);

        var info = MediaClipInfo.TryRead(new MemoryStream(bytes));

        Assert.NotNull(info);
        Assert.Equal(664_437, info.Value.DurationMs);
        Assert.True(info.Value.HasAudio);
        Assert.Equal("11:04", info.Value.TileBadge);
    }

    private static byte[] BuildMovie(uint timescale, uint duration, bool audio)
    {
        var mvhd = Box("mvhd", Mvhd(timescale, duration));
        var hdlr = Box("hdlr", Hdlr(audio ? "soun" : "vide"));
        var mdia = Box("mdia", hdlr);
        var trak = Box("trak", mdia);
        var moov = Box("moov", Concat(mvhd, trak));
        return moov;
    }

    private static byte[] Mvhd(uint timescale, uint duration)
    {
        var body = new byte[20];
        BinaryPrimitives.WriteUInt32BigEndian(body.AsSpan(12), timescale);
        BinaryPrimitives.WriteUInt32BigEndian(body.AsSpan(16), duration);
        return body;
    }

    private static byte[] Hdlr(string subtype)
    {
        var body = new byte[12];
        Encoding.ASCII.GetBytes(subtype).CopyTo(body, 8);
        return body;
    }

    private static byte[] Box(string type, byte[] payload)
    {
        var box = new byte[8 + payload.Length];
        BinaryPrimitives.WriteUInt32BigEndian(box, (uint)box.Length);
        Encoding.ASCII.GetBytes(type).CopyTo(box, 4);
        payload.CopyTo(box, 8);
        return box;
    }

    private static byte[] Concat(params byte[][] parts)
    {
        var size = parts.Sum(p => p.Length);
        var dest = new byte[size];
        var o = 0;
        foreach (var p in parts)
        {
            p.CopyTo(dest, o);
            o += p.Length;
        }
        return dest;
    }
}
