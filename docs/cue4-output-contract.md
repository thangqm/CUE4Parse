# cue4 output contract

What `cue4 export` writes, where, and under what name. This file is a **promise**:
changing the shape of the output means changing this document first.

It is written so that a headless `bpy` script can consume an export directory without
reading any CUE4Parse source. If a question about the output cannot be answered from
here, that is a gap in this document, not in the reader.

---

## 1. Directory layout

```
<out>/<package path>/<ObjectName>.<ext>
```

The package path is the UE package path with its leading `/` stripped, so
`/Game/Fixtures/Meshes/SM_Fixture` becomes `<out>/Game/Fixtures/Meshes/`.

When the package path's last segment already equals the object's name — the normal case
for a top-level asset — **no extra level is nested**. `SM_Fixture` in package
`.../Meshes/SM_Fixture` is written as `.../Meshes/SM_Fixture.glb`, not
`.../Meshes/SM_Fixture/SM_Fixture.glb`.

Subobjects fold their outer chain into folders: an object at
`/Game/Maps/Level.PersistentLevel:MyActor.MyComp` is written under
`Game/Maps/Level/MyActor/`.

Paths use the platform separator on disk. Every path *inside* an output file (glTF URIs,
manifest entries) uses `/`.

## 2. Mesh file-name suffixes

| Case | Suffix | Example |
|---|---|---|
| The first exported LOD | none | `SM_Fixture.glb` |
| A later LOD | `_LOD{SourceLodIndex}` | `SM_Fixture_LOD2.glb` |
| A Nanite LOD (when not first) | `_Nanite` | `SM_Fixture_Nanite.glb` |

`SourceLodIndex` is the LOD's index **in the source asset**, not its position in the
export. With `--mesh-quality highest`, `SM_Fixture.glb` may therefore contain source
LOD 3 — the file name does not say which, and the manifest does not report it either
(see §9).

Animations follow the same shape one level down: an anim set writes one file per
sequence, the first unsuffixed and later ones `_SEQ{i}`.

The extension follows `--mesh-format`:

| `--mesh-format` | Mesh | Animation |
|---|---|---|
| `gltf2` | `.glb` | **unsupported — see §6** |
| `ueformat` | `.uemodel` | `.ueanim` |
| `actorx` | `.psk` for a skeletal LOD of ≤ 65536 vertices, `.pskx` for anything above that and for every static mesh | `.psa` |
| `usd` | `.usda` | `.usda` |

## 3. Texture file names

The extension follows `--texture-format`, with three exceptions that stack. Note that
the flag value and the extension differ in one case: `--texture-format jpeg` writes
`.jpg`. The four are `png` → `.png`, `jpeg` → `.jpg`, `tga` → `.tga`, `webp` → `.webp`.

- **HDR sources** are written as `.hdr` regardless of `--texture-format`, but only when
  the cooked pixel format survives decoding as an HDR format. Block-compressed HDR
  formats (`PF_BC6H`, the ASTC HDR family) are decoded to 8-bit RGBA and therefore come
  out in the requested raster format, not `.hdr`.
- **`--mesh-format gltf2` forces HDR off entirely**, because glTF 2.0 has no Radiance HDR
  extension. `--no-hdr` is consequently a no-op there.
- **`--mesh-format usd` forces PNG**, regardless of `--texture-format`. Pre-existing
  behaviour, previously undocumented.

Name suffixes:

- `--all-mips` appends `_MIP{n}`, and **in that mode no unsuffixed file exists**.
- A `UTexture2DArray` appends `_LAYER{i}`.

`--mesh-format gltf2` rejects `--texture-format tga` and `--texture-format webp` with
exit code 2: glTF 2.0 core carries only PNG and JPEG.

## 4. glTF specifics

- **Textures are referenced, never embedded.** Every `images[].uri` is a relative,
  percent-encoded URI with `/` separators, resolved against the `.glb`'s own directory.
  No image bytes are in the GLB buffer and no satellite image file is written by the glTF
  writer — the files are written by the texture exporter in the same run.
- Every `images[].uri` in an export resolves to a file that the same run wrote.
- `metallicRoughnessTexture` points at a repacked sibling named `<name>_ORM.png`, always
  PNG. Its channels are **R = 255** (neutral), **G = the source's blue** (roughness),
  **B = the source's green** (metallic). UE packs G = metallic, B = roughness; glTF fixes
  the opposite, and glTF 2.0 has no channel swizzle.
- **`occlusionTexture` is never written.** The red channel of a UE SpecularMasks/SRM pack
  is specular, not ambient occlusion; emitting it would be an invention.
- `alphaMode` comes from the material's `BlendMode`: `BLEND_Opaque` → `OPAQUE`,
  `BLEND_Masked` → `MASK`, everything else → `BLEND`. For `MASK`, `alphaCutoff` is the
  nearest explicit `OpacityMaskClipValue` override, else the root `UMaterial`'s value,
  else UE's default of `0.333`.
- `doubleSided` is the root `UMaterial`'s `TwoSided`.
- Cube maps are never bound to a material channel: a cube map's panorama has no UV
  mapping matching the mesh.
- A channel that cannot be classified is left **blank**. A blank channel is a material a
  human can fix in Blender; an invented channel is data that is simply wrong.
- `--no-materials` produces bare material slots and no `images` array at all.
- Morph-target deltas are written under `NORMAL` (not `TANGENT`) and are **deliberately
  not unit vectors** — see §9.
- Units are metres: positions are scaled by 0.01 from UE's centimetres.

## 5. Naming

`meshes[].name`, the root node name and `scenes[].name` all equal **the `.glb` file name
without its extension**, with no exceptions for any `--mesh-quality` × `--nanite`
combination. A script that imports many `.glb` into one scene can therefore derive the
datablock name from the file it just opened.

The skeletal armature root node is `<that name>.ao`.

Material slots are named after the UE material slot. A mesh section whose
`MaterialInterface` is null yields a slot named `None`; a section with no material slot
at all yields `MaterialSlot_{i}`, numbered by section index.

## 6. Asset types, sidecars and per-format support

Which classes produce files:

| Class | Output |
|---|---|
| `USkeletalMesh`, `UStaticMesh`, `UGeometryCollection`, `USplineMeshComponent` | one mesh file per exported LOD (§2) |
| `UTexture`, `UTexture2DArray` | one image per mip and layer (§3) |
| `UMaterialInterface` | `<Name>.json` (below), plus `<Name>.usda` under `--mesh-format usd` |
| `UAnimationAsset` | one animation file per sequence (§2) |
| `UPoseAsset` | `<Name>.uepose` — **`ueformat` only** |
| `USkeleton` | one skeleton-shaped mesh file — **not `gltf2`** |
| `UDNAAsset` | `<Name>.dna` |
| `UWorld`, `ALandscapeProxy`, `ULandscapeComponent` | mesh plus heightmap/weightmap PNGs — **outside this contract**, unverified |
| `USoundWave`, `USoundNodeWave`, `UAkMediaAssetData` | §7 |

Anything else — data tables, blueprints, data assets, material parameter collections,
`USoundCue` — is reported `{"status":"skipped"}` and leaves the exit code alone.

**Not every class supports every `--mesh-format`.** An unsupported combination is
reported `status: "error"` — **not** `skipped` — and the run exits **8**:

| Class | Supported formats |
|---|---|
| `UAnimationAsset` (`UAnimSequence`, `UAnimMontage`, `UBlendSpace`, `UAnimComposite`, …) | `ueformat`, `actorx`, `usd` — **not `gltf2`** |
| `USkeleton` | `ueformat`, `actorx`, `usd` — **not `gltf2`** |
| `UPoseAsset` | `ueformat` only |
| meshes, textures, materials, sound | all formats |

This writer emits no glTF animation channels, and a skeleton alone has no glTF
representation. So one animation caught by the same glob as your meshes fails the whole
run: export in two passes, `--mesh-format gltf2` for the meshes and `ueformat` for
everything skeletal.

**Material JSON sidecar.** Every exported material writes `<Name>.json`, whatever
`--mesh-format` is (abridged — `Properties` carries the material's own UE properties):

```json
{
  "Textures": { "PM_Diffuse": "/Game/Fixtures/Textures/T_BC3.T_BC3" },
  "Parameters": {
    "BlendMode": 0, "ShadingModel": 0,
    "Colors": { "PrimaryColor": { "R": 0.1, "G": 0.2, "B": 0.8, "A": 1.0, "Hex": "597CE8" } },
    "Scalars": { "FixtureRoughness": 0.375 },
    "Switches": {}, "Properties": { },
    "HasTopDiffuse": false, "HasTopNormals": false,
    "HasTopSpecularMasks": false, "HasTopEmissive": false,
    "IsTranslucent": false, "IsNull": false
  }
}
```

`Textures` maps each classified parameter name to the texture's UE object path — the same
classification the glTF binder uses, so it is the way to see why a channel came out blank.
The paths are UE object paths, **not** file paths; the written file for one of them
follows §1 and §3.

Export is transitive: exporting a mesh exports its materials, and exporting a material
exports the textures it references. A `--glob` matching only meshes still produces
material and texture files.

## 7. Audio

A sound asset is written as one file whose extension is the decoder's reported format,
lowercased. The extension names the **container**, not a transcode: cue4 extracts, it
does not decode. CUE4Parse bundles no codec.

| Extension | Header | Playable as-is |
|---|---|---|
| `.wav` | `RIFF….WAVE` | yes |
| `.ogg` | `OggS` | yes |
| `.opus` | `UEOPUS` | no — UE container, not an Ogg Opus stream |
| `.binka` | `ABEU` | no — UE's Bink Audio container |
| `.rada` | `ADAR` | no — UE's RAD Audio container |
| `.adpcm` | `RIFF` | depends on the reader; ADPCM-coded WAV |
| `.wem` | `RIFF` | no — Wwise; needs vgmstream |

The unplayable ones carry the same bytes FModel extracts; FModel can play them only
because it bundles vgmstream. Turning them into audio is a downstream step.

`USoundWave`, `USoundNodeWave` and `UAkMediaAssetData` are exported. `USoundCue` is
**not** — it is a node graph with no audio data of its own, and is reported as
`skipped`.

## 8. Exit codes

| Code | Meaning |
|---|---|
| 0 | success |
| 1 | unclassified error |
| 2 | usage error |
| 3 | configuration error |
| 4 | mount failure |
| 5 | AES key missing or wrong |
| 6 | mappings missing |
| 7 | asset not found |
| 8 | at least one item failed to export |

Exit 8 means an item **failed**. An object type with no exporter is reported as
`skipped` and leaves the exit code at 0.

Exit 5 takes precedence over 7 when an archive is present but unmounted for want of a
key: in that case cue4 genuinely cannot tell whether the asset exists.

## 9. Non-commitments

These are explicitly **not** promised, and a consumer that relies on them will break:

- **Texture bytes are not stable across CUE4Parse versions.** Block-compression decoding
  has changed before and may change again. Note also that **this build's BC decoder
  deliberately differs from upstream `FabianFG/CUE4Parse`**: two arithmetic defects in
  `ea938ba8` are fixed here (BC1/BC2/BC3 round to nearest rather than truncating, and
  BC4/BC5's divide-by-five uses the correct reciprocal). Decoded BC output is therefore
  correct to ±0 against exact round-half-up, but will not match an upstream build
  byte-for-byte. See [reports/bc-interpolant-rounding.md](reports/bc-interpolant-rounding.md).
- **NDJSON line order is undefined.** The export session is parallel. Use `--manifest`
  for an ordered, diffable view.
- **glTF `nodes` order is not committed.** `skins[].joints` order *is*.
- **The manifest does not report which source LOD a file contains** (§2).
- **Morph deltas are not unit vectors.** A delta is a difference, not a direction;
  a typical delta normal has length ~0.02 and the renderer normalises
  `base + sum(weight × delta)` after summation.
- **`--all-mips` removes the unsuffixed file.** Do not assume it is still there.

## 10. Verification gaps

Honest limits of what CI checks.

- **Meshes with materials are not covered by the redistributable fixture set.**
  `SM_Fixture`'s two material slots both have a null `MaterialInterface`, and no fixture
  mesh has one assigned, so no fixture export can produce a glTF `images` array, a
  resolvable texture URI or an `_ORM` sibling. Those assertions run only when
  `CUE4_REAL_PAKS` and `CUE4_REAL_MESH` point at a real game install; they are skipped
  otherwise. They have been verified by hand against a retail UE4.27 title.
- **The fixture material exercises only the base-colour path.** `M_Fixture` has one
  texture parameter and no normal map, no SpecularMasks source, no emissive and no masked
  material. The other channels, every `EBlendMode` and the ORM swizzle are covered by
  fabricated `CMaterialParams2` sets built from real fixture textures.
- **Wwise `.wem` export is not covered by CI.** The redistributable UE5_8 fixture set
  contains no `UAkMediaAssetData`. The code path is the same one the covered formats use
  (`SoundDecoder.Decode` → raw bytes → `ExportFile`), and the sibling formats it shares
  that path with — BinkAudio, RAD Audio, Opus, Vorbis, PCM/ADPCM and the streamed
  chunk-concatenation branch — are all covered by header assertions. But the specific
  claim "a Wwise asset exports to a `.wem` starting with `RIFF` whose size matches the
  source chunks" has only been checked by hand against a real Wwise title. Re-check it by
  hand when touching `SoundExporter` or `SoundDecoder`.
- **ACL-compressed animation decode is not covered by CI.** `cue4 info` reports whether
  the ACL feature is compiled in, and that reporting *is* tested
  (`AclReportingMatchesTheNativeLibraryState` asserts `acl` against
  `CUE4ParseNatives.IsFeatureAvailable`, and that `acl` can never be true without a
  loaded library). Decoding an actual ACL-compressed animation is not tested: ACL
  compression requires a UE plugin, the fixture set contains no ACL-compressed
  animation, and game assets are not redistributable. So the plumbing is proven honest
  and decompression is not proven to work at all. Verify by hand against a real ACL
  title when touching animation decompression.
- **`UGeometryCollection` export is not covered by CI.** Merged from upstream, it is
  dispatched by `ExportSession` through `GeometryCollectionExporter` and written by the
  same `IMeshExportFormat` path static meshes use, so §1, §2 and §5 apply to it
  unchanged. But the redistributable UE5_8 fixture set contains no geometry collection,
  so no test opens one. `ParseCollectionRenderData` also throws when
  `RenderData.CustomData` is not the `MR`-shaped list it expects, which means the only
  asset layout it is known to handle is the one upstream had on hand.
- **The native library is built by a deliberately non-fatal CMake step.** A machine
  without CMake, or with an uninitialised `ACL/external/acl` submodule, silently gets
  `library: false` / `acl: false` and degraded animation support rather than a failed
  build. `cue4 info` is the way to tell which you have — check it before concluding an
  animation decoded wrongly.
