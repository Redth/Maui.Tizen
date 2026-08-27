using System.Buffers.Binary;
using System.IO.Compression;

namespace Maui.Tizen.TestUtils;

/// <summary>
/// A decoded 8-bit RGBA raster.
/// </summary>
/// <remarks>
/// <para>
/// Screenshot comparison is implemented over a self-contained PNG codec rather than SkiaSharp or
/// ImageSharp on purpose:
/// </para>
/// <list type="bullet">
///   <item>No native assets, so the comparison behaves identically on a hosted Linux runner, a
///   developer Mac and the self-hosted Tizen lane.</item>
///   <item>Byte-for-byte determinism. Managed imaging libraries are free to change resampling or
///   colour handling across versions, which silently invalidates every checked-in baseline.</item>
/// </list>
/// <para>
/// Supported subset: non-interlaced, 8-bit, colour types 0 (grayscale), 2 (RGB), 4 (grayscale+alpha)
/// and 6 (RGBA). Anything else throws with an explicit message; device-side capture is expected to
/// produce plain 8-bit PNGs.
/// </para>
/// </remarks>
public sealed class PngImage
{
    static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public PngImage(int width, int height, byte[] pixels)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), $"Invalid image size {width}x{height}.");

        var expected = width * height * 4;
        if (pixels.Length != expected)
        {
            throw new ArgumentException(
                $"Expected {expected} bytes for a {width}x{height} RGBA image but received {pixels.Length}.",
                nameof(pixels));
        }

        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Row-major RGBA bytes, 4 per pixel.</summary>
    public byte[] Pixels { get; }

    public static PngImage Load(string path) => Decode(File.ReadAllBytes(path));

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllBytes(path, Encode());
    }

    /// <summary>Returns the RGBA tuple at (<paramref name="x"/>, <paramref name="y"/>).</summary>
    public (byte R, byte G, byte B, byte A) GetPixel(int x, int y)
    {
        var offset = ((y * Width) + x) * 4;
        return (Pixels[offset], Pixels[offset + 1], Pixels[offset + 2], Pixels[offset + 3]);
    }

    public void SetPixel(int x, int y, byte r, byte g, byte b, byte a = 255)
    {
        var offset = ((y * Width) + x) * 4;
        Pixels[offset] = r;
        Pixels[offset + 1] = g;
        Pixels[offset + 2] = b;
        Pixels[offset + 3] = a;
    }

    public static PngImage Decode(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (data.Length < Signature.Length || !data.AsSpan(0, Signature.Length).SequenceEqual(Signature))
            throw new InvalidDataException("Not a PNG file: signature mismatch.");

        var position = Signature.Length;
        int width = 0, height = 0, bitDepth = 0, colorType = 0, interlace = 0;
        var idat = new MemoryStream();
        var sawHeader = false;

        while (position + 8 <= data.Length)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(position, 4));
            var type = System.Text.Encoding.ASCII.GetString(data, position + 4, 4);
            var dataStart = position + 8;

            if (length < 0 || dataStart + length + 4 > data.Length)
                throw new InvalidDataException($"Malformed PNG: chunk '{type}' declares {length} bytes but the file is truncated.");

            switch (type)
            {
                case "IHDR":
                    width = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(dataStart, 4));
                    height = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(dataStart + 4, 4));
                    bitDepth = data[dataStart + 8];
                    colorType = data[dataStart + 9];
                    interlace = data[dataStart + 12];
                    sawHeader = true;
                    break;

                case "IDAT":
                    idat.Write(data, dataStart, length);
                    break;

                case "IEND":
                    position = data.Length;
                    break;
            }

            position = dataStart + length + 4;
        }

        if (!sawHeader)
            throw new InvalidDataException("Malformed PNG: no IHDR chunk.");

        if (bitDepth != 8)
            throw new NotSupportedException($"Only 8-bit PNGs are supported for baseline comparison; this image is {bitDepth}-bit.");

        if (interlace != 0)
            throw new NotSupportedException("Interlaced PNGs are not supported for baseline comparison.");

        var channels = colorType switch
        {
            0 => 1,
            2 => 3,
            4 => 2,
            6 => 4,
            _ => throw new NotSupportedException(
                $"PNG colour type {colorType} is not supported. Palette images (3) must be expanded before comparison."),
        };

        idat.Position = 0;
        using var zlib = new ZLibStream(idat, CompressionMode.Decompress);
        using var raw = new MemoryStream();
        zlib.CopyTo(raw);

        var scanlines = Unfilter(raw.ToArray(), width, height, channels);
        return new PngImage(width, height, ToRgba(scanlines, width, height, channels, colorType));
    }

    public byte[] Encode()
    {
        using var output = new MemoryStream();
        output.Write(Signature);

        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr[..4], Width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.Slice(4, 4), Height);
        ihdr[8] = 8;  // bit depth
        ihdr[9] = 6;  // colour type: RGBA
        ihdr[10] = 0; // compression
        ihdr[11] = 0; // filter
        ihdr[12] = 0; // interlace
        WriteChunk(output, "IHDR", ihdr.ToArray());

        // Filter type 0 for every scanline keeps encoding deterministic and trivially verifiable.
        var stride = Width * 4;
        var filtered = new byte[(stride + 1) * Height];
        for (var y = 0; y < Height; y++)
        {
            filtered[y * (stride + 1)] = 0;
            Buffer.BlockCopy(Pixels, y * stride, filtered, (y * (stride + 1)) + 1, stride);
        }

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            zlib.Write(filtered, 0, filtered.Length);

        WriteChunk(output, "IDAT", compressed.ToArray());
        WriteChunk(output, "IEND", []);

        return output.ToArray();
    }

    static void WriteChunk(Stream output, string type, byte[] data)
    {
        Span<byte> header = stackalloc byte[8];
        BinaryPrimitives.WriteInt32BigEndian(header[..4], data.Length);
        System.Text.Encoding.ASCII.GetBytes(type).CopyTo(header[4..]);
        output.Write(header);
        output.Write(data);

        var crc = Crc32.Compute(header[4..].ToArray(), data);
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        output.Write(crcBytes);
    }

    static byte[] Unfilter(byte[] raw, int width, int height, int channels)
    {
        var stride = width * channels;
        var result = new byte[stride * height];
        var previous = new byte[stride];

        var position = 0;
        for (var y = 0; y < height; y++)
        {
            if (position >= raw.Length)
                throw new InvalidDataException($"Malformed PNG: expected {height} scanlines, decompressed data ended at row {y}.");

            var filter = raw[position++];
            var current = new byte[stride];
            Buffer.BlockCopy(raw, position, current, 0, Math.Min(stride, raw.Length - position));
            position += stride;

            for (var x = 0; x < stride; x++)
            {
                var a = x >= channels ? current[x - channels] : (byte)0;
                var b = previous[x];
                var c = x >= channels ? previous[x - channels] : (byte)0;

                current[x] = filter switch
                {
                    0 => current[x],
                    1 => (byte)(current[x] + a),
                    2 => (byte)(current[x] + b),
                    3 => (byte)(current[x] + ((a + b) / 2)),
                    4 => (byte)(current[x] + Paeth(a, b, c)),
                    _ => throw new InvalidDataException($"Malformed PNG: unknown scanline filter {filter} on row {y}."),
                };
            }

            Buffer.BlockCopy(current, 0, result, y * stride, stride);
            previous = current;
        }

        return result;
    }

    static byte Paeth(byte a, byte b, byte c)
    {
        var p = a + b - c;
        var pa = Math.Abs(p - a);
        var pb = Math.Abs(p - b);
        var pc = Math.Abs(p - c);

        if (pa <= pb && pa <= pc)
            return a;

        return pb <= pc ? b : c;
    }

    static byte[] ToRgba(byte[] scanlines, int width, int height, int channels, int colorType)
    {
        var rgba = new byte[width * height * 4];

        for (var i = 0; i < width * height; i++)
        {
            var source = i * channels;
            var destination = i * 4;

            switch (colorType)
            {
                case 0: // grayscale
                    rgba[destination] = rgba[destination + 1] = rgba[destination + 2] = scanlines[source];
                    rgba[destination + 3] = 255;
                    break;

                case 2: // RGB
                    rgba[destination] = scanlines[source];
                    rgba[destination + 1] = scanlines[source + 1];
                    rgba[destination + 2] = scanlines[source + 2];
                    rgba[destination + 3] = 255;
                    break;

                case 4: // grayscale + alpha
                    rgba[destination] = rgba[destination + 1] = rgba[destination + 2] = scanlines[source];
                    rgba[destination + 3] = scanlines[source + 1];
                    break;

                default: // RGBA
                    Buffer.BlockCopy(scanlines, source, rgba, destination, 4);
                    break;
            }
        }

        return rgba;
    }
}

/// <summary>CRC-32 (IEEE 802.3) as required by the PNG chunk format.</summary>
static class Crc32
{
    static readonly uint[] Table = BuildTable();

    internal static uint Compute(params byte[][] segments)
    {
        var crc = 0xFFFFFFFFu;

        foreach (var segment in segments)
        {
            foreach (var b in segment)
                crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        return crc ^ 0xFFFFFFFFu;
    }

    static uint[] BuildTable()
    {
        var table = new uint[256];

        for (var n = 0u; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;

            table[n] = c;
        }

        return table;
    }
}
