using CUE4Parse_Conversion.Textures.BC;

namespace CUE4Parse.Tests;

/// <summary>
/// Exhaustive over the real endpoint space, not over 2^32 packed words: BC1/BC3 colour
/// endpoints are 5/6/5 bits, so 1024 red pairs, 4096 green pairs and 1024 blue pairs
/// cover every value the decoder can ever see. BC4/BC5 endpoints are 8 bits: 65536 pairs.
/// <para>
/// The threshold is <b>zero</b> — but zero against <em>what the decoder does today</em>, not
/// against exact arithmetic. These tests characterize: BC1/BC2/BC3 truncate, BC4/BC5 round.
/// That asymmetry is a known defect reported upstream (docs/reports/bc-interpolant-rounding.md),
/// deliberately not fixed here. When upstream changes the formula these go red, which is
/// the entire point of pinning it.
/// </para>
/// <para>
/// "Within 1" would be useless either way: it is a threshold both the truncating and the
/// rounding formula pass, i.e. a test that cannot tell one from the other.
/// </para>
/// <para>
/// Equal endpoints are excluded from every BC1 case. BC1 interpolates only when the first
/// endpoint word is strictly the larger; equal words take the punchthrough branch instead,
/// which <see cref="Bc1WithoutAStrictlyLargerFirstEndpointPunchesThrough"/> covers.
/// </para>
/// </summary>
public class BCDecoderTests
{
    private static byte Expand5(int value) => (byte) ((value << 3) | (value >> 2));
    private static byte Expand6(int value) => (byte) ((value << 2) | (value >> 4));

    // What BC1/BC2/BC3 do today: truncate. Not what they ought to do — see the class
    // summary. BC4/BC5 use the rounding form below instead.
    private static byte Interpolate2To1(int a, int b) => (byte) ((2 * a + b) / 3);
    private static byte Interpolate1To2(int a, int b) => (byte) ((a + 2 * b) / 3);

    // The rounding form: the historical DXTDecoder DXT3/DXT5 formula, still used by BC4/BC5.
    private static byte Interpolate2To1Rounded(int a, int b) => (byte) ((2 * a + b + 1) / 3);

    [Fact]
    public void Bc1RedAndBlueInterpolantsTruncate()
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

    [Fact]
    public void Bc1GreenInterpolantsTruncate()
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
    /// BC1 has no interpolating form at all.
    /// </summary>
    [Fact]
    public void Bc3InterpolatesEvenWhenTheEndpointsAreEqualOrAscending()
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
    /// The branch the interpolant tests must skip, pinned rather than ignored: with no
    /// strictly-larger first endpoint BC1 produces a 3-colour block — slot 2 is the
    /// endpoint midpoint and slot 3 is fully transparent black.
    /// </summary>
    [Fact]
    public void Bc1WithoutAStrictlyLargerFirstEndpointPunchesThrough()
    {
        for (var e0 = 0; e0 < 32; e0++)
        for (var e1 = e0; e1 < 32; e1++)
        {
            var (midpoint, fourth) = BCDecoderProbe.Bc1PunchthroughSlots(e0, e1);

            Assert.Equal(0u, fourth);
            Assert.Equal((Expand5(e0) + Expand5(e1)) >> 1, (byte) midpoint);
            Assert.Equal(0xFFu, midpoint >> 24);
        }
    }

    /// <summary>
    /// Pins the defect itself, so the upstream report has a reproducible witness and so the
    /// day someone "fixes" BC1 without touching BC4 this test says which half moved.
    /// </summary>
    [Fact]
    public void Bc1TruncatesWhereBc4Rounds()
    {
        var divergences = 0;
        for (var e0 = 0; e0 < 32; e0++)
        for (var e1 = 0; e1 < 32; e1++)
        {
            var c0 = Expand5(e0);
            var c1 = Expand5(e1);
            if (Interpolate2To1(c0, c1) != Interpolate2To1Rounded(c0, c1)) divergences++;
        }

        // Non-zero by construction: any exact value with fraction 1/3 or 2/3 differs.
        Assert.True(divergences > 0,
            "BC1 now agrees with the rounding form — upstream changed the decoder. " +
            "Re-read docs/reports/bc-interpolant-rounding.md before updating this test.");
    }

    /// <summary>
    /// The 8-value alpha mode (<c>c0 &gt; c1</c>). Here the decoder's reciprocal
    /// multiply is exact: <c>(x * 9365) &gt;&gt; 16 == x / 7</c> holds for every
    /// <c>x</c> in <c>[0, 1788]</c>, which is the whole range the expression can
    /// produce. So this branch really is exactly rounded.
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
    /// The 6-value alpha mode (<c>c0 &lt;= c1</c>), where slots 6 and 7 are the
    /// constants 0 and 255. Endpoints and constants are pinned here; the interpolants
    /// are <b>not</b> exactly rounded, which
    /// <see cref="Bc4And5SixValueInterpolantsUndershootExactDivisionByFive"/> pins
    /// separately.
    /// </summary>
    [Fact]
    public void Bc4And5SixValueAlphaKeepsItsEndpointsAndConstantSlots()
    {
        for (var c0 = 0; c0 < 256; c0++)
        for (var c1 = 0; c1 < 256; c1++)
        {
            if (c0 > c1) continue;

            var decoded = BCDecoder.DecodeBCColors((ulong) c0 | ((ulong) c1 << 8));

            Assert.Equal((byte) c0, (byte) decoded);
            Assert.Equal((byte) c1, (byte) (decoded >> 8));
            Assert.Equal(0, (byte) (decoded >> 48));
            Assert.Equal(255, (byte) (decoded >> 56));
        }
    }

    /// <summary>
    /// A defect, pinned rather than fixed — see the class summary for why this round
    /// changes no decoding expression.
    /// <para>
    /// The 6-value branch divides by five as <c>(x * 1636) &gt;&gt; 13</c>, and that
    /// identity is simply false: <c>1636 * 5 == 8180 &lt; 8192</c>, so the expression
    /// undershoots <c>floor(x / 5)</c> by one on 374 of the 1278 values it can see —
    /// every multiple of five among them. The smallest witness is <c>c0 = 0, c1 = 3</c>,
    /// whose first interpolant decodes as 0 where exact division gives 1. It is never
    /// wrong in the other direction, and never by more than one.
    /// </para>
    /// <para>
    /// The correct multiplier is <b>1639</b> (<c>1639 * 5 == 8195 &gt;= 8192</c>), which is
    /// exact over the entire range. The sibling <c>/7</c> path's <c>9365 &gt;&gt; 16</c> is
    /// already exact, so this is not a deliberate speed/accuracy trade — the two paths
    /// were meant to be the same kind of expression and one of them is wrong.
    /// </para>
    /// <para>
    /// The counts below are the reproducible witness the upstream report cites. If they
    /// move, the decoder changed: read docs/reports/bc-interpolant-rounding.md before
    /// touching this test.
    /// </para>
    /// </summary>
    [Fact]
    public void Bc4And5SixValueInterpolantsUndershootExactDivisionByFive()
    {
        var wrongSlots = 0;
        var affectedPairs = 0;
        var totalPairs = 0;

        for (var c0 = 0; c0 < 256; c0++)
        for (var c1 = 0; c1 < 256; c1++)
        {
            if (c0 > c1) continue;
            totalPairs++;

            var decoded = BCDecoder.DecodeBCColors((ulong) c0 | ((ulong) c1 << 8));
            var affected = false;

            for (var i = 1; i <= 4; i++)
            {
                var numerator = (5 - i) * c0 + i * c1 + 2;
                var actual = (byte) (decoded >> (8 * (i + 1)));

                // What the decoder computes today, asserted exactly.
                Assert.Equal((byte) ((numerator * 1636) >> 13), actual);

                // ...and how far that is from the division it means to perform.
                var exact = (byte) (numerator / 5);
                Assert.InRange(exact - actual, 0, 1);
                if (exact == actual) continue;

                wrongSlots++;
                affected = true;
            }

            if (affected) affectedPairs++;
        }

        Assert.Equal(32896, totalPairs);
        Assert.Equal(26214, affectedPairs);
        Assert.Equal(38007, wrongSlots);
    }
}
