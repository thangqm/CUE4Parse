using CUE4Parse_Conversion.Textures.BC;

namespace CUE4Parse.Tests;

/// <summary>
/// Exhaustive over the real endpoint space, not over 2^32 packed words: BC1/BC3 colour
/// endpoints are 5/6/5 bits, so 1024 red pairs, 4096 green pairs and 1024 blue pairs
/// cover every value the decoder can ever see. BC4/BC5 endpoints are 8 bits: 65536 pairs.
/// <para>
/// The threshold is <b>zero</b>, against exact arithmetic. Every interpolated value the
/// decoder produces must equal round-half-up of the exact rational — no truncation, no
/// off-by-one, in any channel of any format.
/// </para>
/// <para>
/// This is deliberately stricter than "within 1", which would be useless: it is a
/// threshold that a truncating decoder, a rounding decoder and an off-by-one decoder all
/// pass, i.e. a test that cannot tell correct from incorrect.
/// </para>
/// <para>
/// These tests used to <em>characterize</em> two upstream defects rather than assert
/// correctness — see docs/reports/bc-interpolant-rounding.md, which is still the record
/// of what was wrong and why. Both are now fixed locally, so the tests assert the exact
/// arithmetic instead. They will go red if a future upstream merge reintroduces either.
/// </para>
/// <para>
/// Equal endpoints are excluded from every BC1 interpolant case. BC1 interpolates only
/// when the first endpoint word is strictly the larger; equal or ascending words take
/// the 3-colour punchthrough branch, covered by
/// <see cref="Bc1PunchthroughMidpointRoundsToNearest"/>.
/// </para>
/// </summary>
public class BCDecoderTests
{
    private static byte Expand5(int value) => (byte) ((value << 3) | (value >> 2));
    private static byte Expand6(int value) => (byte) ((value << 2) | (value >> 4));

    // Round-half-up of the exact rational. For division by three, floor((N + 1) / 3) is
    // exactly round-half-up: N%3==1 rounds down, N%3==2 rounds up, N%3==0 is exact.
    private static byte Interpolate2To1(int a, int b) => (byte) ((2 * a + b + 1) / 3);
    private static byte Interpolate1To2(int a, int b) => (byte) ((a + 2 * b + 1) / 3);
    private static byte Midpoint(int a, int b) => (byte) ((a + b + 1) >> 1);

    [Fact]
    public void Bc1RedAndBlueInterpolantsRoundToNearest()
    {
        for (var e0 = 0; e0 < 32; e0++)
        for (var e1 = 0; e1 < 32; e1++)
        {
            if (e0 == e1) continue;

            var c0 = Expand5(e0);
            var c1 = Expand5(e1);

            var (red2, red1) = BCDecoderProbe.Bc1RedInterpolants(e0, e1);
            Assert.Equal(Interpolate2To1(c0, c1), red2);
            Assert.Equal(Interpolate1To2(c0, c1), red1);

            // Red and blue are both 5-bit and share one expansion, so blue must land
            // on exactly the same bytes. It is driven from the same endpoint pair.
            Assert.Equal((red2, red1), BCDecoderProbe.Bc1BlueInterpolants(e0, e1));
        }
    }

    /// <summary>
    /// Green is the channel a naive fix silently misses. It is kept pre-shifted by 8
    /// inside the decoder, so its rounding term must be <c>+256</c>, not <c>+1</c> —
    /// <c>+1</c> changes zero of the 4096 pairs. This test is what distinguishes the
    /// two, so it must never be relaxed to a tolerance.
    /// </summary>
    [Fact]
    public void Bc1GreenInterpolantsRoundToNearest()
    {
        for (var e0 = 0; e0 < 64; e0++)
        for (var e1 = 0; e1 < 64; e1++)
        {
            if (e0 == e1) continue;

            var c0 = Expand6(e0);
            var c1 = Expand6(e1);

            var (two, one) = BCDecoderProbe.Bc1GreenInterpolants(e0, e1);

            Assert.Equal(Interpolate2To1(c0, c1), two);
            Assert.Equal(Interpolate1To2(c0, c1), one);
        }
    }

    [Fact]
    public void Bc3ColourInterpolantsMatchBc1Exactly()
    {
        for (var e0 = 0; e0 < 32; e0++)
        for (var e1 = 0; e1 < 32; e1++)
        {
            if (e0 == e1) continue;

            Assert.Equal(BCDecoderProbe.Bc1RedInterpolants(e0, e1), BCDecoderProbe.Bc3RedInterpolants(e0, e1));
        }
    }

    /// <summary>
    /// BC3 carries its alpha in a separate block, so its colour block always interpolates
    /// and has no punchthrough branch to fall into — including for equal endpoints, where
    /// BC1 has no interpolating form at all. BC2 shares this reader.
    /// </summary>
    [Fact]
    public void Bc2And3InterpolateEvenWhenTheEndpointsAreEqualOrAscending()
    {
        for (var e = 0; e < 32; e++)
        {
            var c = Expand5(e);
            Assert.Equal(((byte) c, (byte) c), BCDecoderProbe.Bc3RedInterpolants(e, e));
        }

        for (var e0 = 0; e0 < 32; e0++)
        for (var e1 = 0; e1 < 32; e1++)
        {
            var c0 = Expand5(e0);
            var c1 = Expand5(e1);

            var (two, one) = BCDecoderProbe.Bc3RedInterpolants(e0, e1);
            Assert.Equal(Interpolate2To1(c0, c1), two);
            Assert.Equal(Interpolate1To2(c0, c1), one);
        }
    }

    /// <summary>
    /// The other BC1 branch, reached when the first endpoint word is not strictly the
    /// larger: slot 2 is the endpoint midpoint and slot 3 is fully transparent black.
    /// The midpoint rounds to nearest too — rounding the interpolants while leaving this
    /// truncating would introduce a fresh inconsistency inside the very same function.
    /// </summary>
    [Fact]
    public void Bc1PunchthroughMidpointRoundsToNearest()
    {
        for (var e0 = 0; e0 < 32; e0++)
        for (var e1 = e0; e1 < 32; e1++)
        {
            var (midpoint, fourth) = BCDecoderProbe.Bc1PunchthroughSlots(e0, e1);

            Assert.Equal(0u, fourth);
            Assert.Equal(Midpoint(Expand5(e0), Expand5(e1)), (byte) midpoint);
            Assert.Equal(0xFFu, midpoint >> 24);
        }
    }

    /// <summary>
    /// The formerly-asymmetric pair, now pinned as consistent: BC1's colour interpolants
    /// and BC4/BC5's alpha interpolants both round to nearest. Before the fix BC1
    /// truncated while BC4/BC5 rounded, which is the asymmetry the upstream report
    /// describes.
    /// </summary>
    [Fact]
    public void Bc1AndBc4UseTheSameRoundingPolicy()
    {
        for (var e0 = 0; e0 < 32; e0++)
        for (var e1 = 0; e1 < 32; e1++)
        {
            if (e0 == e1) continue;

            var c0 = Expand5(e0);
            var c1 = Expand5(e1);

            // BC1's 2:1 interpolant against the exact round-half-up form that
            // DecodeBCColors' +3 / +2 terms implement for BC4/BC5.
            var (two, _) = BCDecoderProbe.Bc1RedInterpolants(e0, e1);
            Assert.Equal((byte) ((2 * c0 + c1 + 1) / 3), two);
        }
    }

    /// <summary>
    /// The 8-value alpha mode (<c>c0 &gt; c1</c>): <c>(x * 9365) &gt;&gt; 16 == x / 7</c>
    /// holds for every <c>x</c> in <c>[0, 1788]</c>, the whole range the expression can
    /// produce, so this branch was always exact.
    /// </summary>
    [Fact]
    public void Bc4And5EightValueAlphaInterpolantsAreExactlyRounded()
    {
        for (var c0 = 0; c0 < 256; c0++)
        for (var c1 = 0; c1 < 256; c1++)
        {
            if (c0 <= c1) continue;

            var decoded = BCDecoder.DecodeBCColors((ulong) c0 | ((ulong) c1 << 8));

            Assert.Equal((byte) c0, (byte) decoded);
            Assert.Equal((byte) c1, (byte) (decoded >> 8));

            for (var i = 1; i <= 6; i++)
            {
                var expected = (byte) (((7 - i) * c0 + i * c1 + 3) / 7);
                Assert.Equal(expected, (byte) (decoded >> (8 * (i + 1))));
            }
        }
    }

    /// <summary>
    /// The 6-value alpha mode (<c>c0 &lt;= c1</c>), where slots 6 and 7 are the constants
    /// 0 and 255.
    /// <para>
    /// This is the branch that carried the reciprocal-multiply bug: it divided by five as
    /// <c>(x * 1636) &gt;&gt; 13</c>, and <c>1636 * 5 == 8180 &lt; 8192</c>, so it
    /// undershot <c>floor(x / 5)</c> by one on 374 of the 1278 reachable values. The
    /// multiplier is now <b>1639</b> (<c>1639 * 5 == 8195 &gt;= 8192</c>), exact over the
    /// entire range. The smallest witness of the old bug was <c>c0 = 0, c1 = 3</c>, whose
    /// first interpolant decoded as 0 where exact division gives 1 — so that pair is
    /// worth keeping in mind if this ever goes red again.
    /// </para>
    /// </summary>
    [Fact]
    public void Bc4And5SixValueAlphaInterpolantsAreExactlyRounded()
    {
        for (var c0 = 0; c0 < 256; c0++)
        for (var c1 = 0; c1 < 256; c1++)
        {
            if (c0 > c1) continue;

            var decoded = BCDecoder.DecodeBCColors((ulong) c0 | ((ulong) c1 << 8));

            Assert.Equal((byte) c0, (byte) decoded);
            Assert.Equal((byte) c1, (byte) (decoded >> 8));

            for (var i = 1; i <= 4; i++)
            {
                var expected = (byte) (((5 - i) * c0 + i * c1 + 2) / 5);
                Assert.Equal(expected, (byte) (decoded >> (8 * (i + 1))));
            }

            Assert.Equal(0, (byte) (decoded >> 48));
            Assert.Equal(255, (byte) (decoded >> 56));
        }
    }
}
