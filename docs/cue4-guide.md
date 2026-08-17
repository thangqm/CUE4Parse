# cue4 — agent guide

`cue4` reads Unreal Engine game archives (`.pak`, `.utoc`/`.ucas`) and gives you back
JSON or extracted files. It is built for programmatic use: **stdout is always JSON,
never prose.**

You do not need to write any C# to use it. Everything below is a shell command.

> **This guide describes how to drive the tool. The *shape of what it writes* — file
> names, directory layout, glTF specifics, and the honest limits of what CI checks — is
> a separate, versioned promise: [docs/cue4-output-contract.md](cue4-output-contract.md).
> Read that one before writing a script that consumes the output.**

---

## 1. Before anything else

**Locate the executable.** It is a single self-contained file; no .NET install needed.
Build one with [CUE4Parse.Cli/publish.ps1](../CUE4Parse.Cli/publish.ps1) and put it
somewhere on `PATH`. This guide writes `cue4` for that binary.

```
C:\tools\cue4.exe          # wherever you put it
```

`publish.ps1` deliberately avoids trimming and NativeAOT — both break the reflection
`ObjectTypeRegistry` does at static-init time, and the failure mode is "no exporter for
this type" on assets that used to work rather than a build error.

**Every command needs to know two things** — where the archives are, and which
engine version built them:

```bash
cue4 <command> --paks "<directory containing .pak/.utoc>" --game 5.6
```

`--game` takes either an `EGame` name (`GAME_UE5_6`) or shorthand (`5.6`).
If you don't know the version, guess from the game's engine and check `info` — a
wrong version usually shows up as a mount failure or garbage output.

Set these once in a config file instead (see §7) and drop the flags entirely.

---

## 2. Four rules that will save you

**1. Redirect stderr. Parse stdout.**
Logs, warnings and progress go to stderr; only JSON goes to stdout.

```bash
cue4 list --paks ... --game 5.6 2>/dev/null    # clean JSON
```

**2. Batch your paths into ONE invocation.**
There is no daemon and no cached index — every run re-mounts every archive, which
is the slow part. `cue4 dump a b c` mounts once. Three separate calls mount three
times. Never loop a `cue4` call over a list of assets.

**3. Run `info` first when something looks empty.**
If `missingAesGuids` is non-empty, those archives are still encrypted and their
contents are invisible to every other command. You will get zero results and no
error. Supply the key with `--aes` before blaming the pattern.

**4. Explore with `list` before you `dump`/`export`/`unpack`.**
`list` only reads the index — it never deserializes, so it is fast and has no
safety limit. Use it to find out what a pattern matches before doing real work.

---

## 3. The normal workflow

```bash
# Step 1 — confirm the archives mounted and nothing is still encrypted
cue4 info --paks "D:/Games/Fortnite/FortniteGame/Content/Paks" --game 5.6 --aes auto
# -> {"game":"GAME_UE5_6","mountedVfs":42,"fileCount":918273,"missingAesGuids":[]}

# Step 2 — find what you want, and how much of it there is
cue4 list --glob "**/Characters/**/*.uasset" --count
# -> {"count":184,"totalMatched":184,"truncated":false}

# Step 3 — do the work
cue4 dump --glob "**/Characters/**/*.uasset" -o characters.ndjson
```

---

## 4. Commands

### `info` — check the mount

```bash
cue4 info --paks ... --game 5.6
```

Returns one object. Read `missingAesGuids` and `fileCount` first.

```json
{
  "game": "GAME_UE5_6", "paksDir": "...", "mappings": "...",
  "mountedVfs": 42, "unloadedVfs": 0, "fileCount": 918273,
  "missingAesGuids": [],
  "native": { "library": true, "acl": true, "oodle": "native" }
}
```

`unloadedVfs` counts archives that were found but **not** mounted. Encrypted archives
whose GUIDs are still in `missingAesGuids` land here, but so do containers that are never
mounted as such — on an IoStore game `global.utoc` shows up here — so a non-zero count is
not by itself a problem. Name them before drawing a conclusion:

`--verbose` adds `mountedArchives` and `unloadedArchives`, each an ordered array of
archive names. Counts cannot settle a disagreement with another tool about how many
archives a game has; names can.

See §10 for the `native` block.

### `list` — find assets (cheap, never deserializes)

```bash
cue4 list --glob "**/Meshes/*.uasset"      # NDJSON: one object per asset
cue4 list --regex "CID_\d+" --count        # just the number
```

Emits `{"path":...,"size":...,"extension":...,"encrypted":...}` per line, or with
`--count` a single `{"count":N,"totalMatched":N,"truncated":false}`.

### `dump` — asset contents as JSON

```bash
cue4 dump "Game/Content/Foo/Bar.uasset" --indent      # one asset -> JSON array of exports
cue4 dump --glob "**/DataTables/*.uasset"             # many      -> NDJSON
cue4 dump --glob "**/*.uasset" --class DataTable -o out.ndjson
```

| Flag | Meaning |
|---|---|
| `--indent` | Pretty-print (single-asset output only) |
| `--class <name>` | Keep only exports of this class |
| `--export <name>` | Serialize only the named export |
| `-o <file>` | Write to a file instead of stdout |

**Output shape depends on the input — check this before parsing.**

- **One** explicit path, no `--glob` → a **JSON array** of that asset's exports:
  `[{"Type":...,"Name":...,"Properties":{...}}, ...]`. No wrapper, no `path` field.
- **Anything else** (multiple paths, or any `--glob`/`--regex`/`--ext`) → **NDJSON**,
  one line per asset: `{"path":...,"status":"ok"|"error","code":...,"data":...}`.
  The exports are under `data`. This holds even with `-o`.

In NDJSON mode a per-asset failure is reported *in the data* and does **not** fail
the process, so check each line's `status` rather than relying on the exit code.

If you want one predictable shape for both cases, always pass a filter (e.g.
`--glob` matching the single asset) so you always get NDJSON.

`--class` filters *after* loading, because determining a class requires
deserializing. That is why it exists on `dump` and not on `list`.

### `unpack` — raw bytes, no deserialization

```bash
cue4 unpack --glob "**/Audio/**" -o ./out
cue4 unpack "Game/Content/A.uasset" -o ./out --flat
```

`-o` is required. Directory structure is preserved unless `--flat`.

**One asset path yields multiple files.** A cooked package is `.uasset` **plus**
`.uexp` (and sometimes `.ubulk`/`.uptnl`). The exports live in the `.uexp` — a
`.uasset` on its own is unopenable. `unpack` writes all of them; keep them together.

`--flat` **fails** with `OUTPUT_COLLISION` rather than silently overwriting when two
assets share a leaf name.

### `export` — converted meshes, textures, animations, materials

```bash
cue4 export --glob "**/Meshes/*.uasset" -o ./out --mesh-format gltf2
```

`-o` is required.

| Flag | Values | Default |
|---|---|---|
| `--mesh-format` | `actorx` `gltf2` `ueformat` `usd` | `ueformat` |
| `--texture-format` | `png` `jpeg` `tga` `webp` | `png` |
| `--texture-platform` | `desktop` `xbox-ps4` `switch` `ps5` | `desktop` |
| `--mesh-quality` | `highest` `lowest` `all` | `highest` |
| `--nanite` | `nanite-only` `no-nanite` `nanite-first` `nanite-last` | `no-nanite` |
| `--socket-format` | `socket` `bone` `none` | `bone` |
| `--material-depth` | `top-layer-only` `all-layers-no-ref` `all-layers` | `top-layer-only` |
| `--texture-quality` | 1–100 | `100` |
| `--compression-format` | `none` `gzip` `zstd` | `none` |
| `--no-materials` `--all-mips` `--no-morph-targets` `--no-hdr` `--flip-normal-y` | flags | off |
| `--manifest <file.json>` | path | off |
| `--parallel` | integer ≥ 1 | processor count |

- **Console/Switch textures decode to garbage without the matching
  `--texture-platform`.** If textures look scrambled, that's the cause.
- `--mesh-format usd` forces PNG textures regardless of `--texture-format`.
- `--texture-format jpeg` writes files ending `.jpg`, not `.jpeg`.
- Assets with no exporter (DataTables, Blueprints, DataAssets…) report
  `{"status":"skipped"}` and **do not** fail the run. Only a real export failure
  gives exit 8. Do not treat `skipped` as an error.
- **Export is transitive.** A glob matching only meshes still writes their materials
  (`<Name>.json`) and every texture those materials reference.

**`--mesh-format gltf2` cannot export animations or skeletons.** This is the one that
bites a character-export glob:

```bash
cue4 export --glob "**/Characters/**/*.uasset" -o ./out --mesh-format gltf2
# -> {"path":".../AS_Walk.AS_Walk","status":"error",
#     "message":"Animation export does not support format Gltf2. ..."}
# -> exit 8
```

`UAnimSequence`, `UAnimMontage`, `UBlendSpace`, `UAnimComposite` and `USkeleton` all
report `status:"error"` — **not** `skipped` — so one animation in the glob turns a
perfectly good mesh export into exit 8. (`UPoseAsset` is narrower still: `ueformat` is
the only format that exports it at all.) There is no "meshes only" filter, so split the
work by path and run it twice:

```bash
cue4 export --glob "**/Characters/**/Meshes/*.uasset" -o ./out --mesh-format gltf2
cue4 export --glob "**/Characters/**/Animations/*.uasset" -o ./out --mesh-format ueformat
```

Use `list --glob` first to find the folder split for the game you are on. Blender reads
`.glb` natively; `.ueanim`, `.uepose` and `.psa` need the corresponding importer addon.

**Nanite meshes warn on stderr.** With the default `--nanite no-nanite`, a mesh that
carries Nanite geometry logs `Mesh has Nanite data that is being skipped (N non-Nanite
LOD(s) exported)`. On a UE5 title whose meshes are Nanite-only that is the difference
between "the exporter is broken" and "pass `--nanite nanite-only`". It goes to stderr,
so do not discard stderr entirely while you are still figuring out a game.

**The flags added since the first release of this guide:**

| Flag | What it does | When you want it |
|---|---|---|
| `--compression-format none\|gzip\|zstd` | Compression *inside* a `.uemodel`/`.ueanim` container. **Only affects `--mesh-format ueformat`**; glTF and ActorX ignore it. | Matching FModel, which writes ZSTD. Leaving this at `none` while FModel writes ZSTD is why the same mesh can come out several times larger and byte-different. |
| `--no-morph-targets` | Drop morph/blend-shape data. | Smaller files when the consumer does not animate shape keys. |
| `--no-hdr` | Write HDR sources (BC6H etc.) in the raster `--texture-format` instead of Radiance `.hdr`. **No-op with `--mesh-format gltf2`**, which already forces it: glTF 2.0 carries only PNG and JPEG, so an `.hdr` URI would be a file no glTF reader can open. | Pipelines that cannot read `.hdr`. |
| `--flip-normal-y` | Invert the green channel of `TC_Normalmap` textures only. | Consumers expecting DirectX-convention normal maps when the source is OpenGL-convention (or the reverse) — a normal map that lights "inside out" is the symptom. |
| `--manifest <file.json>` | Alongside the normal NDJSON, write a **sorted, hashed** record of every file written: path, object path, class, byte size, sha256. | CI and caching. Two runs with the same arguments produce byte-identical manifests, so "did this change?" becomes a `diff` instead of a directory crawl. This is the thing to compare across versions — **not** texture bytes (see the contract's non-commitments). |

`--mesh-format gltf2` rejects `--texture-format tga` and `webp` with exit 2
(`BAD_OPTION`) rather than silently substituting: glTF 2.0 core accepts only PNG and
JPEG.

### `update` — refresh the Fortnite cache (Fortnite only)

```bash
cue4 update                # AES keys + mappings
cue4 update --aes-keys     # keys only
cue4 update --usmap        # mappings only
```

Downloads from `fortnite-api.com` into `%LOCALAPPDATA%\cue4\cache\`. This is what
`--aes auto` and `--mappings auto` read. Run it once before using `auto`.

`--aes auto` supplies the main key **and** every dynamic key — Fortnite ships
encrypted chunks under non-zero GUIDs that the main key alone will not mount.

---

## 5. Filtering (works on `list`, `dump`, `unpack`, `export`)

| Flag | Behaviour |
|---|---|
| `--glob` | Repeatable; matches if **any** glob matches |
| `--regex` | Applied to the whole path, case-insensitive |
| `--ext` | Extension, with or without the leading dot |
| `--limit N` | Take the first N matches |

Glob syntax: `*` stays inside one path segment, `**` crosses separators, `?` is one
non-separator character. Matching is **case-insensitive** throughout.

```bash
--glob "**/Characters/**/*.uasset"    # anywhere under any Characters folder
--glob "Game/*/A.uasset"              # exactly one segment between
```

---

## 6. The 1000-asset safety limit

`dump`, `export` and `unpack` refuse to touch more than **1000** matched assets and
exit **2** with `LIMIT_EXCEEDED`. `list` is exempt.

The guard measures the **true** match count, not a truncated one, so `--limit 100`
against 50 000 matches cannot sneak past it.

To proceed deliberately, either narrow the pattern, or:

- `--limit N` — stating how many you want counts as consent
- `--force` — bypass entirely

**When you hit this, do not blindly add `--force`.** Run `list --count` first and
decide whether that many assets is really what you meant.

---

## 7. Config file (recommended — removes the repeated flags)

Put `cue4.json` in the working directory, or at `%APPDATA%\cue4\cue4.json`:

```json
{
  "defaultProfile": "fn",
  "profiles": {
    "fn": {
      "paksDir": "D:/Games/Fortnite/FortniteGame/Content/Paks",
      "game": "GAME_UE5_6",
      "mappings": "auto",
      "aes": { "main": "auto" }
    }
  }
}
```

Then simply:

```bash
cue4 list --glob "**/*.uasset" --count
```

Resolution order: `--config <path>` → `./cue4.json` → `%APPDATA%\cue4\cue4.json`.
Command-line flags always beat profile values. Pick a non-default profile with
`-p <name>`.

AES keys are 64 hex chars (`0x` prefix optional). GUIDs are 32 hex chars (dashes
optional). Add per-GUID dynamic keys under `aes.dynamic` if needed.

---

## 8. Exit codes

| Code | Meaning |
|---|---|
| 0 | success |
| 1 | unclassified error |
| 2 | usage error (includes `LIMIT_EXCEEDED`) |
| 3 | configuration error |
| 4 | mount failure |
| 5 | AES key missing or malformed |
| 6 | mappings missing or required |
| 7 | asset not found |
| 8 | at least one item failed to export |

5 and 7 are deliberately distinct: "the archive holding it is still encrypted" and
"no such asset" need different fixes. If a lookup fails while keys are outstanding,
you get 5 with the GUIDs — not a misleading not-found.

---

## 9. Errors and what to do about them

Every failure is one object: `{"error":{"code":"...","message":"...","details":{...}}}`.
**Branch on `code`, never on message text.** Per-asset failures inside NDJSON carry
the same codes.

| Code | Do this |
|---|---|
| `MISSING_PAKS_DIR` / `PAKS_DIR_NOT_FOUND` | Fix `--paks`; it must be the folder holding `.pak`/`.utoc` |
| `MISSING_GAME` / `UNKNOWN_GAME` | Pass `--game` as `GAME_UE5_6` or `5.6` |
| `UNKNOWN_PROFILE` | Check profile names in `cue4.json`; the message lists the known ones |
| `BAD_CONFIG` / `CONFIG_NOT_FOUND` | Fix the JSON or the `--config` path |
| `AES_KEY_MISSING` | Archives are encrypted. Supply `--aes`; `details` lists the outstanding GUIDs |
| `BAD_AES_KEY` / `BAD_AES_GUID` | Key = 64 hex chars, GUID = 32 hex chars |
| `MAPPINGS_REQUIRED` | The package uses unversioned properties. Pass `--mappings <file.usmap>`, or `cue4 update` then `--mappings auto` |
| `MAPPINGS_NOT_FOUND` / `NO_MAPPINGS_AVAILABLE` | The `.usmap` path is wrong, or the cache is empty — run `cue4 update --usmap` |
| `ASSET_NOT_FOUND` | Path is wrong. Use `list --glob` to find the real one; `details.missing` shows what failed |
| `LIMIT_EXCEEDED` | See §6 — check `list --count`, then narrow or consent |
| `OUTPUT_COLLISION` | Two assets share a leaf name under `--flat`. Drop `--flat` |
| `MOUNT_FAILED` | Usually the wrong `--game`, or corrupt/partial archives |
| `INTERNAL` | Unexpected. Re-run with `-v` for a stack trace on stderr |

---

## 10. Known limitations of this build

**Ask the build, don't guess.** `cue4 info` reports what this particular binary can
actually do, because the native library is compiled by a deliberately non-fatal CMake
step — a machine without CMake, or with an uninitialised ACL submodule, produces a
working `cue4` with degraded animation support rather than a failed build:

```bash
cue4 info --paks ... --game 5.6
# -> "native": { "library": true, "acl": true, "oodle": "native" | "downloaded" }
```

- `library: false` — no native library at all; ACL animations will fail.
- `acl: false` — the library exists but was built without ACL. Most animations in
  recent UE titles are ACL-compressed and will fail to export. Meshes, textures,
  materials and JSON dumps are unaffected.
- `oodle` is **three** states, not a boolean. `"native"` means compiled in;
  `"downloaded"` means `oo2core` was fetched to `%LOCALAPPDATA%\cue4\cache\` — that
  first run needs network access, later runs do not; `"unavailable"` means neither.
  zlib is always supported.

`info` still prints the `native` block when the mount itself fails (exit 4), so it
answers "is this binary capable?" independently of "did these archives load?".

**Sound assets extract but do not transcode.** `USoundWave`, `USoundNodeWave` and
Wwise `UAkMediaAssetData` export as raw container bytes — `.wav` and `.ogg` play
as-is, but `.binka`, `.rada`, `.opus` and `.wem` are UE/RAD/Wwise containers that need
vgmstream downstream. FModel plays them only because it bundles a codec; CUE4Parse
bundles none. `USoundCue` is a node graph, not audio, and is reported as `skipped`.
See the contract's audio table for the header magic of each.

**Verification status.** Exercised against the UE5.8 test archives (Oodle, Zlib,
Uncompressed, AES-encrypted, IoStore and legacy pak) *and* against a full retail
install — Stellar Blade, 212 archives, 234,340 files — with the exported `.glb`
importing into Blender 5.2 with every texture resolving. The earlier claim that this
was untried on a retail build is out of date; see
[docs/reports/2026-08-16-cue4-cli-vs-fmodel-parity.md](reports/2026-08-16-cue4-cli-vs-fmodel-parity.md).

What is **not** covered by CI, and why, is listed honestly in the output contract's
"Verification gaps" section. The short version: no Wwise asset and no ACL-compressed
animation exists in the redistributable fixture set, and neither can be added.

---

## 11. Copy-paste starting point

```bash
CUE4="C:/tools/cue4.exe"
PAKS="D:/Games/YourGame/Content/Paks"
GAME=5.6

# 1. Is it mounted and readable?
"$CUE4" info --paks "$PAKS" --game $GAME 2>/dev/null

# 2. What's in there?
"$CUE4" list --paks "$PAKS" --game $GAME --ext uasset --limit 25 2>/dev/null

# 3. Look inside one asset
"$CUE4" dump "<paste a path from step 2>" --paks "$PAKS" --game $GAME --indent 2>/dev/null

# 4. Extract something
"$CUE4" export --paks "$PAKS" --game $GAME --glob "**/Meshes/*.uasset" -o ./out 2>/dev/null
```
