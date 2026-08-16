using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace CUE4Parse_Conversion.Textures.BC;

public static partial class BCDecoder
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte GetZNormal(byte x, byte y)
    {
        const float scale = 2.0f / 255.0f;
        var xf = x * scale - 1;
        var yf = y * scale - 1;
        var zval = 1 - xf * xf - yf * yf;
        var zval_ = MathF.Sqrt(zval > 0 ? zval : 0);
        zval = zval_ < 1.0f ? zval_ : 1.0f;
        return (byte) ((zval * 127) + 128);
    }

    // Test-only windows onto the two private colour-block readers. See BCDecoderProbe.
    internal static void ReadColorsBC1Internal(uint data, Span<uint> op) => ReadColorsBC1(data, op);
    internal static void ReadColorsBC3Internal(uint data, Span<uint> op) => ReadColorsBC3(data, op);

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private static void ReadColorsBC1(uint data, Span<uint> op)
    {
        // this is the absolute best i got after a lot of testing
        ref uint dst = ref MemoryMarshal.GetReference(op);
        var c0 = data & 0xFFFF;
        uint rb = c0 & 0xF81F;
        rb = rb << 3 | rb >> 2;
        uint r0 = rb >> 11;
        uint b0 = rb & 0xFF;
        var g = data & 0x07E007E0;
        g = g << 5 | g >> 1;
        uint g0 = g & 0xFF00;
        dst = r0 | g0 | (b0 << 16) | 0xFF000000;

        var c1 = data >> 16;
        rb = c1 & 0xF81F;
        rb = (rb << 3) | (rb >> 2);
        uint r1 = rb >> 11;
        uint b1 = rb & 0xFF;
        uint g1 = (g >> 16) & 0xFF00;
        Unsafe.Add(ref dst, 1) = r1 | g1 | (b1 << 16) | 0xFF000000;

        if (c0 > c1)
        {
            // (x * 683) >> 11 = x/3 for all values in range [0,765]
            Unsafe.Add(ref dst, 2) = ((2 * r0 + r1) * 683) >> 11 | (((2 * g0 + g1) * 683) >> 19) << 8 | (((2 * b0 + b1) * 683) >> 11) << 16 | 0xFF000000;
            Unsafe.Add(ref dst, 3) = ((r0 + 2 * r1) * 683) >> 11 | (((g0 + 2 * g1) * 683) >> 19) << 8 | (((b0 + 2 * b1) * 683) >> 11) << 16 | 0xFF000000;
        }
        else
        {
            var b2 = (b0 + b1) >> 1;
            var g2 = (g0 + g1) >> 9;
            var r2 = (r0 + r1) >> 1;
            Unsafe.Add(ref dst, 2) = r2 | g2 << 8 | b2 << 16 | 0xFF000000;
            Unsafe.Add(ref dst, 3) = 0;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private static void ReadColorsBC3(uint data, Span<uint> op)
    {
        ref uint dst = ref MemoryMarshal.GetReference(op);
        var g = data & 0x07E007E0;
        uint rb = data & 0xF81F;
        uint rb1 = (data >> 16) & 0xF81F;
        g = g << 5 | g >> 1;
        rb = rb << 3 | rb >> 2;
        rb1 = rb1 << 3 | rb1 >> 2;
        uint r0 = rb >> 11;
        uint b0 = rb & 0xFF;
        uint g0 = g & 0xFF00;
        dst = r0 | g0 | (b0 << 16);

        uint r1 = rb1 >> 11;
        uint b1 = rb1 & 0xFF;
        uint g1 = (g >> 16) & 0xFF00;
        Unsafe.Add(ref dst, 1) = r1 | g1 | (b1 << 16);

        Unsafe.Add(ref dst, 2) = ((2 * r0 + r1) * 683) >> 11 | (((2 * g0 + g1) * 683) >> 19) << 8 | (((2 * b0 + b1) * 683) >> 11) << 16;
        Unsafe.Add(ref dst, 3) = ((r0 + 2 * r1) * 683) >> 11 | (((g0 + 2 * g1) * 683) >> 19) << 8 | (((b0 + 2 * b1) * 683) >> 11) << 16;
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static ulong DecodeBCColors(ulong data)
    {
        var cl = data & 0xFFFF;
        var c0 = (uint) (data & 0xFF);
        var c1 = (uint) (cl >> 8);

        if (c0 > c1)
        {
            // (x * 9365) >> 16 = x/7 in [0,1788]
            var diff = c0 - c1;
            var temp0 = 6 * c0 + c1 + 3;
            cl |= (ulong) ((temp0 * 9365) >> 16) << 16;
            temp0 -= diff;
            cl |= (ulong) ((temp0 * 9365) >> 16) << 24;
            temp0 -= diff;
            cl |= (ulong) ((temp0 * 9365) >> 16) << 32;
            temp0 -= diff;
            cl |= (ulong) ((temp0 * 9365) >> 16) << 40;
            temp0 -= diff;
            cl |= (ulong) ((temp0 * 9365) >> 16) << 48;
            temp0 -= diff;
            cl |= (ulong) ((temp0 * 9365) >> 16) << 56;
        }
        else
        {
            // (x * 1636) >> 13 = x/5 in [0,1277]
            var diff = c1 - c0;
            var temp0 = 4 * c0 + c1 + 2;
            cl |= (ulong) ((temp0 * 1636) >> 13) << 16;
            temp0 += diff;
            cl |= (ulong) ((temp0 * 1636) >> 13) << 24;
            temp0 += diff;
            cl |= (ulong) ((temp0 * 1636) >> 13) << 32;
            temp0 += diff;
            cl |= (ulong) ((temp0 * 1636) >> 13) << 40;
            cl |= 0xFF00000000000000;
        }
        return cl;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static void DecodeBCBlock(ulong data, Span<byte> block)
    {
        var cl = DecodeBCColors(data);
        var bits = (uint) (data >> 16);
        block[0] = (byte) (cl >> (int) (((bits >> 0) & 7) << 3));
        block[1] = (byte) (cl >> (int) (((bits >> 3) & 7) << 3));
        block[2] = (byte) (cl >> (int) (((bits >> 6) & 7) << 3));
        block[3] = (byte) (cl >> (int) (((bits >> 9) & 7) << 3));
        block[4] = (byte) (cl >> (int) (((bits >> 12) & 7) << 3));
        block[5] = (byte) (cl >> (int) (((bits >> 15) & 7) << 3));
        block[6] = (byte) (cl >> (int) (((bits >> 18) & 7) << 3));
        block[7] = (byte) (cl >> (int) (((bits >> 21) & 7) << 3));
        bits = (uint) (data >> 40);
        block[8] = (byte) (cl >> (int) (((bits >> 0) & 7) << 3));
        block[9] = (byte) (cl >> (int) (((bits >> 3) & 7) << 3));
        block[10] = (byte) (cl >> (int) (((bits >> 6) & 7) << 3));
        block[11] = (byte) (cl >> (int) (((bits >> 9) & 7) << 3));
        block[12] = (byte) (cl >> (int) (((bits >> 12) & 7) << 3));
        block[13] = (byte) (cl >> (int) (((bits >> 15) & 7) << 3));
        block[14] = (byte) (cl >> (int) (((bits >> 18) & 7) << 3));
        block[15] = (byte) (cl >> (int) (((bits >> 21) & 7) << 3));
    }
}

/// <summary>
/// Test-only window onto the two private colour-block readers. The exhaustive
/// endpoint tests are the only thing standing between this file and a silent
/// half-LSB shift across every DXT texture the tool has ever written, so they
/// need to see the interpolants directly rather than through a whole image decode.
/// </summary>
public static class BCDecoderProbe
{
    private delegate void ColorReader(uint data, Span<uint> op);

    /// <summary>
    /// The two interpolated colours of a BC1 block whose endpoints carry
    /// <paramref name="e0"/> and <paramref name="e1"/> in both the red and blue
    /// 5-bit fields, as (2:1, 1:2) in the caller's endpoint order.
    /// </summary>
    public static (byte Two, byte One) Bc1RedInterpolants(int e0, int e1)
        => Ordered(BCDecoder.ReadColorsBC1Internal, RedBlue(e0), RedBlue(e1), 0);

    /// <summary>Same block, read out of the blue byte instead of the red one.</summary>
    public static (byte Two, byte One) Bc1BlueInterpolants(int e0, int e1)
        => Ordered(BCDecoder.ReadColorsBC1Internal, RedBlue(e0), RedBlue(e1), 2);

    public static (byte Two, byte One) Bc1GreenInterpolants(int e0, int e1)
        => Ordered(BCDecoder.ReadColorsBC1Internal, Green(e0), Green(e1), 1);

    /// <summary>
    /// BC3 has no punchthrough branch — it always interpolates — so it needs no
    /// endpoint reordering and is defined for equal endpoints too.
    /// </summary>
    public static (byte Two, byte One) Bc3RedInterpolants(int e0, int e1)
        => Channel(Read(BCDecoder.ReadColorsBC3Internal, Pack(RedBlue(e0), RedBlue(e1))), 0);

    /// <summary>
    /// The other BC1 branch, reached when the first endpoint word is not the larger:
    /// slot 2 is a midpoint and slot 3 is transparent black rather than a second
    /// interpolant. Returned as (slot2, slot3) in the raw BGRA words.
    /// </summary>
    public static (uint Midpoint, uint Fourth) Bc1PunchthroughSlots(int e0, int e1)
    {
        var colors = Read(BCDecoder.ReadColorsBC1Internal, Pack(RedBlue(e0), RedBlue(e1)));
        return (colors[2], colors[3]);
    }

    // 5:6:5, so red occupies bits 11-15, green bits 5-10 and blue bits 0-4. Driving
    // red and blue from the same value covers both 5-bit channels in one pass; green
    // has to be driven separately because it is 6 bits wide and, unlike the other two,
    // is kept pre-shifted by 8 inside the decoder.
    private static int RedBlue(int e) => (e << 11) | e;
    private static int Green(int e) => e << 5;

    /// <summary>
    /// BC1 only runs the interpolating branch when the first endpoint word is strictly
    /// the larger, so the pair is fed in descending order and the result swapped back.
    /// That is exact rather than approximate: with (2a+b)/3 and (a+2b)/3, exchanging
    /// a and b exchanges the two interpolants. Equal endpoints have no interpolating
    /// form at all — they take the punchthrough branch — so callers must exclude them.
    /// </summary>
    private static (byte Two, byte One) Ordered(ColorReader reader, int c0, int c1, int shiftIndex)
    {
        if (c0 == c1)
            throw new ArgumentException("BC1 does not interpolate equal endpoints; see Bc1PunchthroughSlots.");

        var swap = c1 > c0;
        if (swap) (c0, c1) = (c1, c0);

        var (two, one) = Channel(Read(reader, Pack(c0, c1)), shiftIndex);
        return swap ? (one, two) : (two, one);
    }

    private static uint Pack(int c0, int c1) => (uint) c0 | ((uint) c1 << 16);

    private static uint[] Read(ColorReader reader, uint data)
    {
        var colors = new uint[4];
        reader(data, colors);
        return colors;
    }

    private static (byte, byte) Channel(uint[] colors, int shiftIndex)
    {
        var shift = shiftIndex * 8;
        return ((byte) (colors[2] >> shift), (byte) (colors[3] >> shift));
    }
}
