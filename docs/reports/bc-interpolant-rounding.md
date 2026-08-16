# BC1–BC5 interpolant rounding: one regression and one incorrect reciprocal

> **Status:** **both defects are fixed in this fork.** Not yet filed upstream —
> this remains ready-to-send issue text for `FabianFG/CUE4Parse`, and the patch
> below is what we actually applied. Record the issue URL here once filed.
>
> **This means our decoder output deliberately differs from upstream's.** That
> is the intended trade: this fork is used locally, where being arithmetically
> correct matters more than matching upstream byte-for-byte.
> `CUE4Parse.Tests/BCDecoderTests.cs` now asserts exact round-half-up against
> the exact rational — so it goes red if an upstream merge reintroduces either
> defect, which is the real reason to keep this document.

Commit [`ea938ba8`](https://github.com/FabianFG/CUE4Parse/commit/ea938ba8) —
"Optimize BC1–BC5 decoders for 3–5× speedup", 2026-08-04 — replaced `DXTDecoder`
with `BCDecoder` for DXT1/DXT3/DXT5 and rewrote `BCDecoder`'s alpha path to use
reciprocal multiplies. It changed decoded pixel values in two ways that look
unintended. Both are covered exhaustively by `CUE4Parse.Tests/BCDecoderTests.cs`,
so either one reappearing becomes a red build rather than a render surprise
months later.

The applied patch is in "What we changed" at the end.

---

## 1. BC2/BC3 silently changed from rounding to truncating

**Before.** `DXTDecoder` used two different formulas on purpose:

| Format | Colour interpolants |
|---|---|
| DXT1 | `(2*c0 + c1) / 3` — truncating |
| DXT3, DXT5 | `(2*c0 + c1 + 1) / 3` — rounding |

**After.** `BCDecoder.ReadColorsBC1` and `ReadColorsBC3` share one expression,
`((2*c0 + c1) * 683) >> 11`, which truncates. DXT1's behaviour is preserved;
**DXT3/DXT5 lost their `+1`**. Every BC2/BC3 texture decoded since that commit
differs from the pre-commit output, in a commit whose stated purpose was speed.

Mean error is 0 for the rounding form and −1/3 LSB for the truncating one.

### The trap, if you fix it

The obvious patch — add `+1` before dividing by three — is a **no-op on green**,
and we measured it: of the 4096 possible green endpoint pairs, `+1` changes
**zero** of them.

Green is kept pre-shifted 8 bits (`g0 = g & 0xFF00`), which is why it divides
with `>>19` rather than `>>11`. `2*g0 + g1` is therefore always a multiple of
256. Adding 1 contributes 683 to a quantity that `>>19` quantizes at 524288, so
it essentially never crosses a boundary. Green needs **`+256`**, which changes
1365 of the 4096 pairs and equals `(2*g + g' + 1) / 3` on the expanded 8-bit
values exactly.

A `+1`-everywhere patch would round R and B, leave G truncating, and introduce a
*new* channel inconsistency worse than today's uniform truncation. The correct
form:

```csharp
Unsafe.Add(ref dst, 2) = ((2 * r0 + r1 + 1) * 683) >> 11
                       | ((((2 * g0 + g1 + 256) * 683) >> 19) << 8)
                       | ((((2 * b0 + b1 + 1) * 683) >> 11) << 16)
                       | 0xFF000000;
```

The identity `(x * 683) >> 11 == floor(x / 3)` holds on `[0, 765]`. `+1` pushes
the maximum to 766, and `766 * 683 >> 11 == 255 == floor(766 / 3)`, so the range
survives. Verified exhaustively over all 1024 red/blue endpoint pairs.

---

## 2. BC4/BC5's 6-value alpha divides by five incorrectly

This one is not a preserved-versus-changed judgement call. It is arithmetic that
does not compute what it says it computes.

The same commit replaced exact integer division with reciprocal multiplies and
added rounding terms:

```csharp
// 8-value mode (c0 > c1) — correct
var temp0 = 6 * c0 + c1 + 3;
cl |= (ulong)((temp0 * 9365) >> 16) << 16;   // (x * 9365) >> 16 == x / 7

// 6-value mode (c0 <= c1) — INCORRECT
var temp0 = 4 * c0 + c1 + 2;
cl |= (ulong)((temp0 * 1636) >> 13) << 16;   // (x * 1636) >> 13 != x / 5
```

`1636 * 5 == 8180`, which is less than `8192`. The expression therefore
undershoots `floor(x / 5)` by one for **374 of the 1278** values it can produce
— every multiple of five among them. The smallest witness is `c0 = 0, c1 = 3`,
whose first interpolant decodes as `0` where the division gives `1`.

Measured over the whole 6-value endpoint space:

| | |
|---|---|
| Endpoint pairs with `c0 <= c1` | 32896 |
| ...with at least one wrong interpolant | **26214 (79.7 %)** |
| Interpolant slots wrong | **38007 of 131584 (28.9 %)** |
| Direction and magnitude | always low, never by more than 1 |

The correct multiplier is **1639** (`1639 * 5 == 8195 >= 8192`), which is exact
over the entire range — verified exhaustively. The sibling `/7` path's
`(x * 9365) >> 16` is already exact over its own `[0, 1788]` range, so this is
not a deliberate speed-for-accuracy trade: the two paths were meant to be the
same kind of expression, and one of them is wrong.

The net effect on output is partly masked by the rounding term that was added in
the same commit, which is probably why it went unnoticed. Comparing decoded
values before and after `ea938ba8`:

| Alpha mode | Slots that moved | |
|---|---|---|
| 8-value (`/7`) | 84258 of 195840 (43 %) rose by 1 | the intended rounding change |
| 6-value (`/5`) | 14421 of 131584 (11 %) rose by 1 | rounding added, then mostly cancelled by the undershoot |

BC5 is the normal-map format, so this affects the reconstructed Z channel of
normal maps as well as BC4 masks.

---

## Reproducing

`CUE4Parse.Tests/BCDecoderTests.cs` covers the real endpoint space exhaustively
rather than sampling: BC1/BC3 endpoints are 5/6/5 bits (1024 red pairs, 4096
green pairs, 1024 blue pairs) and BC4/BC5 endpoints are 8 bits (65536 pairs).
Every count quoted above is asserted there.

Every assertion compares against **exact round-half-up of the exact rational**,
threshold zero. "Within 1" would be useless: it is a threshold that the
truncating form, the rounding form and the off-by-one form all pass.

- `Bc1RedAndBlueInterpolantsRoundToNearest`, `Bc1GreenInterpolantsRoundToNearest`,
  `Bc3ColourInterpolantsMatchBc1Exactly`,
  `Bc2And3InterpolateEvenWhenTheEndpointsAreEqualOrAscending` — §1.
- `Bc1AndBc4UseTheSameRoundingPolicy` — the asymmetry itself, now absent.
- `Bc1PunchthroughMidpointRoundsToNearest` — the 3-colour branch.
- `Bc4And5EightValueAlphaInterpolantsAreExactlyRounded` — the `/7` path, exact
  before and after; this is what makes §2 a defect rather than a design choice.
- `Bc4And5SixValueAlphaInterpolantsAreExactlyRounded` — §2.

The tests reach the two private colour readers through `BCDecoderProbe` in
`BCDecoder.cs`. Note that BC1 only runs its interpolating branch when the first
endpoint word is strictly the larger; equal or ascending endpoints take the
3-colour punchthrough branch, which is covered separately.

---

## What we changed

Three edits in `CUE4Parse-Conversion/Textures/BC/BCDecoder.cs`:

1. **`DecodeBCColors`, 6-value branch:** `1636` → `1639`. One token. Fixes §2.
2. **`ReadColorsBC1`, interpolating branch:** `+1` on the red and blue
   numerators, **`+256`** on green. Fixes §1 without falling into the green
   trap.
3. **`ReadColorsBC1`, punchthrough branch, and `ReadColorsBC3`:** the same
   rounding, for consistency. Rounding the interpolants while leaving the
   midpoint truncating would create a fresh inconsistency inside one function —
   exactly the failure mode described above.

`ReadColorsBC3` is shared by BC2 and BC3, so both are covered by edit 3.

No behaviour outside these expressions changed. The whole test suite is green,
including the fixture texture-decode tests that compare decoded bitmaps against
deterministic ImageMagick references — the change is within their tolerances and
moves output toward those references, not away.

## Why this is not a PR yet

Changing this alters the bytes of every BC1–BC5 texture for every CUE4Parse
consumer, and §1 is partly a question about intent — whether DXT3/DXT5's
rounding was meant to be dropped — that only the author of `ea938ba8` can
answer. §2 is a plain mistake and the patch is one token; we would send it
immediately on request.
