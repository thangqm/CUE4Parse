# `tools/parity/`

Comparison scripts for answering "did the output change, and *how*?" — between two
`cue4` builds, or between `cue4` and another tool's export.

These are investigation tools, not tests. The automated equivalents live in
`CUE4Parse.Cli.Tests` and `CUE4Parse.Tests`; reach for these when a test has gone red
and you need to know what actually moved.

**Read [`docs/cue4-output-contract.md`](../../docs/cue4-output-contract.md) §8 first.**
Texture bytes are explicitly *not* stable across CUE4Parse versions, so a byte
difference between two versions is expected, not a bug. Comparing a committed
`--manifest` baseline is the supported way to detect change; these scripts are for
explaining a difference you already found.

---

## `PixDiff.ps1 -A <a.png> -B <b.png> -Name <label>`

**Answers:** *is this a decode failure or a rounding change?*

Prints a **signed** per-channel histogram of `A − B` (B/G/R/A). The distinction it
exists to draw: a rounding change shows as `+1` (or `−1`) on ~100 % of differing
pixels and **never** the other sign; a real decode failure is signed both ways with a
long tail. An unsigned "N pixels differ" count cannot tell those apart.

```powershell
./PixDiff.ps1 -A ./cue4out/T_Body_N.png -B ./other/T_Body_N.png -Name 'Body normal (BC5)'
```

> Windows-only: uses `System.Drawing`.

## `glbcmp.py <a.glb> <b.glb>`

**Answers:** *which vertex attribute diverged, and by how much?*

Per-primitive comparison of `POSITION`, `NORMAL`, `TANGENT`, `TEXCOORD_0`, `COLOR_0`,
`JOINTS_0`, `WEIGHTS_0` and `INDICES`, reporting the differing count and max absolute
delta for each. **`JOINTS_0` is remapped by joint *name* before comparing**, so a
different `nodes` ordering — which is not committed output (contract §8) — does not
masquerade as a skinning difference.

Also prints whether the joint name *sets* and the joint *order* match; `skins[].joints`
order is committed, so a mismatch there is a real finding.

```bash
python tools/parity/glbcmp.py ./cue4out/Mesh.glb ./other/Mesh.glb
```

Standard library only, no dependencies. Compares `meshes[0]` and `skins[0]`.

## `GlbInfo.ps1 -Path <file.glb> [-JsonOut <out.json>]`

**Answers:** *what is actually in this container?*

Splits the GLB into its JSON and BIN chunks, prints each chunk's length against the
file's real length (a mismatch means a truncated or mis-written container), and dumps
the glTF JSON to stdout. With `-JsonOut`, writes it pretty-printed for diffing.

```powershell
./GlbInfo.ps1 -Path ./cue4out/Mesh.glb -JsonOut ./mesh.gltf.json
```

## `BinDiff.ps1 -A <a.glb> -B <b.glb> [-MaxRanges 15]`

**Answers:** *where in the buffer does it diverge?*

Lists the contiguous differing byte runs in the two BIN chunks. One tight run usually
means a single accessor changed — cross-reference the offset against `bufferViews` in
`GlbInfo.ps1`'s output to name it, then use `glbcmp.py` to interpret the values. Many
scattered runs mean the geometry itself is different.

---

## `baseline-UE5_8.json`

A committed `--manifest` of a glTF export of the UE5.8 mesh fixtures. Because the
manifest is sorted and hashed, a change to output shows up as a readable diff on this
file rather than as a vague suspicion. Verified byte-identical across repeat runs.

Regenerate and diff:

```bash
KEY="0x$(printf 'CUE4ParseFixtureAESKey0123456789' | xxd -p -c 64 | tr 'a-f' 'A-F')"

dotnet run --project CUE4Parse.Cli/CUE4Parse.Cli.csproj -c Release -- export \
  --glob "**/Fixtures/Meshes/*.uasset" \
  --paks CUE4Parse.Tests/Fixtures/UE5_8/IoStore/Tagged \
  --game 5.8 \
  --mappings CUE4Parse.Tests/Fixtures/UE5_8/Mappings/CUE4ParseFixtures-Oodle.usmap \
  --aes "$KEY" \
  --mesh-format gltf2 -o /tmp/baseline \
  --manifest tools/parity/baseline-UE5_8.json

git diff tools/parity/baseline-UE5_8.json
```

Three things about that command are load-bearing:

- **`IoStore/Tagged`, not `LegacyPak`.** The legacy paks hold five assets and no mesh
  at all, so the export would silently produce nothing.
- **Point at `Tagged` itself, not a compression leaf under it.** IoStore packages need
  `global.utoc`, which sits beside the compression folders rather than inside them.
- **The AES key is required** even though the meshes are unencrypted: an encrypted
  sibling container under `Tagged` would otherwise leave the mount incomplete and the
  command exits 5.

The manifest records sha256es of **texture** bytes too, so this baseline will
legitimately change when the BC decoder changes upstream. That is the point: it makes
such a change visible and dated instead of silent. The `tool` field carries the
assembly version, so a version bump also shows up here.

The manifest records sha256es of **texture** bytes too, so this baseline will
legitimately change when the BC decoder changes upstream. That is the point: it makes
such a change visible and dated instead of silent.
