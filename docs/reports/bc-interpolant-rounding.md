# BC1–BC5 interpolant rounding: one regression and one incorrect reciprocal

> **Status:** not yet filed upstream. Intended as issue text for
> `FabianFG/CUE4Parse`. Record the issue URL here once filed, so
> `CUE4Parse.Tests/BCDecoderTests.cs` can point its failure messages at a real
> discussion.

Commit [`ea938ba8`](https://github.com/FabianFG/CUE4Parse/commit/ea938ba8) —
"Optimize BC1–BC5 decoders for 3–5× speedup", 2026-08-04 — replaced `DXTDecoder`
with `BCDecoder` for DXT1/DXT3/DXT5 and rewrote `BCDecoder`'s alpha path to use
reciprocal multiplies. It changed decoded pixel values in two ways that look
unintended. Both are pinned by `CUE4Parse.Tests/BCDecoderTests.cs`, so whatever
is decided here becomes visible as a red build rather than as a render surprise
months later.

Nothing in this report has been changed in the decoder. See "Why we are not
sending a PR" at the end.

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

- `Bc1RedAndBlueInterpolantsTruncate`, `Bc1GreenInterpolantsTruncate`,
  `Bc3ColourInterpolantsMatchBc1Exactly` — pin §1's current behaviour.
- `Bc1TruncatesWhereBc4Rounds` — pins the asymmetry itself.
- `Bc4And5EightValueAlphaInterpolantsAreExactlyRounded` — pins that the `/7`
  path *is* exact, which is what makes §2 a defect rather than a design choice.
- `Bc4And5SixValueInterpolantsUndershootExactDivisionByFive` — pins §2, with the
  three counts above as the witness.

The tests reach the two private colour readers through `BCDecoderProbe` in
`BCDecoder.cs`. Note that BC1 only runs its interpolating branch when the first
endpoint word is strictly the larger; equal or ascending endpoints take the
3-colour punchthrough branch, which is pinned separately.

---

## Why we are not sending a PR

Fixing either issue changes the bytes of every BC1/BC2/BC3/BC4/BC5 texture for
every CUE4Parse consumer. That is upstream's call, not a downstream fork's, and
§1 in particular is a question about intent — whether DXT3/DXT5's rounding was
meant to be dropped — that only the author of `ea938ba8` can answer. §2 looks
like a plain mistake and we would send a patch for it on request; the one-word
change is `1636` → `1639`.
