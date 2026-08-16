# cue4

A console front end for [CUE4Parse](../README.md). It exposes asset search, JSON
dumping, mesh/texture/animation export and raw extraction as machine-readable
commands, so an agent or a script can drive the library without writing C#.

Building this project produces `cue4.exe`.

## Three things that are easy to get wrong

- **Every invocation re-mounts the archives.** There is no daemon and no
  persisted index. `cue4 dump a b c` is one mount; three separate `cue4 dump`
  calls are three. Batch your paths into a single invocation.
- **`--aes auto` and `--mappings auto` read a local cache** written by
  `cue4 update`. The only other network I/O is CUE4Parse fetching a missing
  compression backend (Oodle/zlib-ng) on first use; that too lands in the cache
  directory rather than the working directory.
- **`unpack` writes every payload file of a package.** One asset path yields
  `.uasset` *and* `.uexp` (plus `.ubulk`/`.uptnl` when present). A `.uasset` on
  its own is unopenable, because the exports live in the `.uexp`.

## Output contract

- **stdout carries structured data only.** Everything else — logging, warnings,
  progress — goes to stderr via Serilog. Redirecting stdout gives you clean JSON.
- A command producing one result writes **one JSON object**.
- A command producing many results writes **NDJSON**: one compact JSON object
  per line.
- Errors are a single object: `{"error":{"code":"...","message":"...","details":{...}}}`.
- Per-asset failures inside NDJSON carry the same stable `code`, so a caller can
  branch on `MAPPINGS_REQUIRED` without matching on message text.

## Verbs

### `info` — mount state

```bash
cue4 info --paks "D:/Games/Fortnite/Paks" --game 5.6 --aes auto
```

```json
{
  "game": "GAME_UE5_6",
  "paksDir": "D:/Games/Fortnite/Paks",
  "mappings": "auto",
  "mountedVfs": 42,
  "unloadedVfs": 0,
  "fileCount": 918273,
  "missingAesGuids": []
}
```

A non-empty `missingAesGuids` means archives are still encrypted and their
contents are invisible to every other verb.

### `list` — enumerate assets

Cheap discovery: it reads the mounted index and never deserializes anything.
`list` is **exempt from the safety limit** — this is how you find out what a glob
would match before running a bulk operation.

```bash
cue4 list --glob "**/Characters/**/*.uasset" --count
cue4 list --regex "CID_\d+" --limit 20
```

```json
{"path":"FortniteGame/Content/Athena/Items/CID_001.uasset","size":2260,"extension":"uasset","encrypted":false}
```

With `--count` you get `{"count":N,"totalMatched":N,"truncated":false}` instead.

### `dump` — deserialize exports to JSON

```bash
cue4 dump "FortniteGame/Content/.../CID_001.uasset" --indent
cue4 dump --glob "**/DataTables/*.uasset" --class DataTable
cue4 dump --glob "**/*.uasset" --export MyExportName -o dump.ndjson
```

A single explicit path with no `--glob` writes one JSON object. Anything else
writes NDJSON of `{"path":...,"status":"ok"|"error","code":...,"data":...}`,
regardless of `-o`. In NDJSON mode a per-asset failure is reported as data and
does not fail the process.

`--class` filters exports **after** loading the package — determining a class
requires deserializing it, which is why this lives on `dump` and not on `list`.

### `unpack` — raw bytes, no deserialization

```bash
cue4 unpack --glob "**/Audio/**" -o ./out
cue4 unpack "Game/Content/A.uasset" -o ./out --flat
```

Preserves the archive's directory structure under `-o` unless `--flat`.

`--flat` **fails** with `OUTPUT_COLLISION` when two different assets share a leaf
name, rather than silently overwriting one with the other. A package's own
payload files (`.uasset`/`.uexp`/`.ubulk`) differ by extension and never collide.

### `export` — converted meshes, textures, animations, materials

```bash
cue4 export --glob "**/Meshes/*.uasset" -o ./out \
  --mesh-format gltf2 --texture-format png --texture-platform desktop
```

| Option | Values | Default |
|---|---|---|
| `--mesh-format` | `actorx`, `gltf2`, `ueformat`, `usd` | `ueformat` |
| `--texture-format` | `png`, `jpeg`, `tga`, `webp` | `png` |
| `--texture-platform` | `desktop`, `xbox-ps4`, `switch`, `ps5` | `desktop` |
| `--mesh-quality` | `highest`, `lowest`, `all` | `highest` |
| `--nanite` | `nanite-only`, `no-nanite`, `nanite-first`, `nanite-last` | `no-nanite` |
| `--socket-format` | `socket`, `bone`, `none` | `bone` |
| `--material-depth` | `top-layer-only`, `all-layers-no-ref`, `all-layers` | `top-layer-only` |
| `--texture-quality` | 1–100 | `100` |
| `--no-materials` | flag | off |
| `--all-mips` | flag | off |
| `--parallel` | integer ≥ 1 | processor count |

Console and Switch textures decode incorrectly without the matching
`--texture-platform`. Note that `--mesh-format usd` forces PNG textures
regardless of `--texture-format`; that is a library constraint, not a CLI one.

Object types with no exporter (DataTables, DataAssets, Blueprints, …) are
reported as `{"status":"skipped"}` and **do not** affect the exit code. Only a
genuine export failure produces exit 8.

### `update` — refresh the Fortnite cache

```bash
cue4 update              # both
cue4 update --aes-keys   # AES keys only
cue4 update --usmap      # mappings only
```

Fetches from `fortnite-api.com` into `%LOCALAPPDATA%\cue4\cache\`
(`aes.json`, `mappings.usmap`). Those files are what `--aes auto` and
`--mappings auto` consume. `--aes auto` supplies the main key **and** every
dynamic key — Fortnite ships encrypted chunks under non-zero GUIDs that the main
key alone will not mount.

## Filtering

`--glob`, `--regex`, `--ext` and `--limit` are accepted by `list`, `dump`,
`unpack` and `export` alike.

- `--glob` is repeatable; an asset matches if **any** glob matches. `*` stays
  within a path segment, `**` crosses separators, `?` is one non-separator
  character. Matching is case-insensitive, as UE asset paths are throughout
  CUE4Parse.
- `--regex` is applied to the whole asset path, case-insensitively.
- `--ext` takes an extension with or without the leading dot.
- `--limit N` takes the first N matches.

## The 1000-asset safety limit

`dump`, `export` and `unpack` refuse to operate on more than **1000** matched
assets and exit 2 with `LIMIT_EXCEEDED`. The guard is measured against the
**true** match count, never a truncated one, so `--limit 100` against 50 000
matches cannot slip past it.

Override with `--force`, or pass `--limit` — stating how many you want counts as
consent. `list` is exempt.

## Configuration

Resolution order: `--config <path>`, then `./cue4.json`, then
`%APPDATA%\cue4\cue4.json`. Command-line flags always beat profile values.

```json
{
  "defaultProfile": "fn",
  "profiles": {
    "fn": {
      "paksDir": "D:/Games/Fortnite/FortniteGame/Content/Paks",
      "game": "GAME_UE5_6",
      "mappings": "auto",
      "aes": {
        "main": "auto",
        "dynamic": {
          "11111111-2222-2222-3333-333344444444": "0x<64 hex chars>"
        }
      }
    }
  }
}
```

`game` accepts an `EGame` name (`GAME_UE5_6`) or engine-version shorthand
(`5.6`). AES keys are 64 hex characters, with or without a `0x` prefix. GUIDs are
32 hex characters, with or without dashes. Profile-level `dynamic` keys are
applied last, so they win over anything from the cache.

Select a profile with `-p/--profile`; override any single value with `--paks`,
`--game`, `--mappings` or `--aes`.

## Exit codes

| Code | Meaning |
|---|---|
| 0 | success |
| 1 | unclassified error |
| 2 | usage error |
| 3 | configuration error |
| 4 | mount failure |
| 5 | AES key missing or malformed |
| 6 | mappings missing or required |
| 7 | asset not found |
| 8 | partial export failure |

5 and 7 are deliberately separate: "the archive holding it is still encrypted"
and "no such asset" have different remedies, so a failed lookup reports the
outstanding key GUIDs rather than a bare not-found.

## Building and publishing

```bash
dotnet build CUE4Parse.Cli/CUE4Parse.Cli.csproj -c Release
./CUE4Parse.Cli/publish.ps1                 # -> ./artifacts/cue4.exe
./CUE4Parse.Cli/publish.ps1 -Runtime linux-x64
```

`publish.ps1` produces a self-contained single file. Trimming and NativeAOT are
deliberately not used: `ObjectTypeRegistry` reflects over the assembly at static
init, and trimming breaks `UObject` construction.
