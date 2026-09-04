using System.Buffers.Binary;

namespace ChurchProjection.Core.Services;

/// <summary>
/// Reads duration and whether a sound track exists from an MP4/MOV file, without a decoder.
/// Lets the media bin show "0:55 · mute" vs "11:04" so a short silent recording is not mistaken
/// for a longer one with the same Screen Recording name.
/// </summary>
public readonly record struct MediaClipInfo(long DurationMs, bool HasAudio)
{
    public string DurationLabel => PlaybackClock.From(0, DurationMs, 0).Duration;

    public string TileBadge => HasAudio || DurationMs <= 0
        ? DurationLabel
        : $"{DurationLabel} · mute";

    public static MediaClipInfo? TryRead(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        try
        {
            using var stream = File.OpenRead(path);
            return TryRead(stream);
        }
        catch (IOException)
        {
            return null;
        }
    }

    public static MediaClipInfo? TryRead(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        long durationMs = 0;
        var hasAudio = false;
        Walk(stream, 0, stream.Length, ref durationMs, ref hasAudio);
        return durationMs > 0 || hasAudio ? new MediaClipInfo(durationMs, hasAudio) : null;
    }

    private static void Walk(Stream stream, long start, long end, ref long durationMs, ref bool hasAudio)
    {
        var header = new byte[16];
        var cursor = start;
        while (cursor + 8 <= end)
        {
            if (!ReadAt(stream, cursor, header.AsSpan(0, 8)))
                return;

            var size = BinaryPrimitives.ReadUInt32BigEndian(header);
            var type = System.Text.Encoding.ASCII.GetString(header, 4, 4);
            var hdr = 8L;
            long boxSize = size;
            if (size == 1)
            {
                if (!ReadAt(stream, cursor, header))
                    return;
                boxSize = (long)BinaryPrimitives.ReadUInt64BigEndian(header.AsSpan(8, 8));
                hdr = 16;
            }
            else if (size == 0)
            {
                boxSize = end - cursor;
            }

            if (boxSize < hdr || cursor + boxSize > end)
                return;

            var payload = cursor + hdr;
            var boxEnd = cursor + boxSize;

            if (type is "moov" or "trak" or "mdia" or "minf")
            {
                Walk(stream, payload, boxEnd, ref durationMs, ref hasAudio);
            }
            else if (type == "mvhd" && durationMs <= 0)
            {
                durationMs = ReadMvhdDurationMs(stream, payload, boxEnd);
            }
            else if (type == "hdlr" && !hasAudio)
            {
                var hdlr = new byte[12];
                if (ReadAt(stream, payload, hdlr) &&
                    hdlr[8] == (byte)'s' && hdlr[9] == (byte)'o' && hdlr[10] == (byte)'u' && hdlr[11] == (byte)'n')
                    hasAudio = true;
            }

            cursor = boxEnd;
        }
    }

    private static long ReadMvhdDurationMs(Stream stream, long payload, long boxEnd)
    {
        var buf = new byte[32];
        var need = (int)Math.Min(buf.Length, boxEnd - payload);
        if (need < 20 || !ReadAt(stream, payload, buf.AsSpan(0, need)))
            return 0;

        var version = buf[0];
        uint timescale;
        ulong duration;
        if (version == 0)
        {
            timescale = BinaryPrimitives.ReadUInt32BigEndian(buf.AsSpan(12, 4));
            duration = BinaryPrimitives.ReadUInt32BigEndian(buf.AsSpan(16, 4));
        }
        else
        {
            if (need < 32) return 0;
            timescale = BinaryPrimitives.ReadUInt32BigEndian(buf.AsSpan(20, 4));
            duration = BinaryPrimitives.ReadUInt64BigEndian(buf.AsSpan(24, 8));
        }

        if (timescale == 0 || duration == 0) return 0;
        return (long)Math.Round(duration * 1000.0 / timescale);
    }

    private static bool ReadAt(Stream stream, long offset, Span<byte> dest)
    {
        stream.Seek(offset, SeekOrigin.Begin);
        return stream.Read(dest) == dest.Length;
    }
}
