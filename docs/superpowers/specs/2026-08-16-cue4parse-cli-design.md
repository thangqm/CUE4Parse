# CUE4Parse CLI — Design

**Date:** 2026-08-16
**Status:** Approved, pending implementation plan

## Purpose

FModel has no command-line interface, so its capabilities cannot be driven by
scripts or AI agents. This spec describes `cue4`, a console application that
exposes FModel's non-visual capabilities — asset search, property dumping,
mesh/texture/animation export, and raw extraction — as composable commands with
machine-readable output.

The target consumer is an AI agent shelling out to the tool and parsing its
stdout. Every design decision below favours predictable, parseable behaviour
over human ergonomics where the two conflict.

## Scope

In scope:

- Search and list assets in the mounted virtual file system
- Deserialize exports to JSON
- Export meshes, animations, textures, and materials via `ExportSession`
- Raw byte extraction of packages
- Fortnite-oriented auto-fetch of AES keys and `.usmap` mappings
- Diagnostics reporting mount state and missing keys

Out of scope:

- The 3D viewport (Snooper), texture and audio preview, and the asset tree UI.
  These are FModel-only and have no equivalent in CUE4Parse.
- Batch mode, a persistent daemon, and an MCP server. The plain per-invocation
  CLI was chosen deliberately; these remain possible later but are not built.
- A persisted VFS index cache. Usage is targeted lookups, not whole-game sweeps.

## Architecture

A new project, `CUE4Parse.Cli`, sits alongside `CUE4Parse.Example` and project-
references `CUE4Parse` and `CUE4Parse-Conversion`. Building against local source
rather than the NuGet package means new game support arrives with every pull.

```
CUE4Parse.Cli/
  Program.cs                 root command wiring only
  Commands/
    ListCommand.cs
    DumpCommand.cs
    ExportCommand.cs
    UnpackCommand.cs
    UpdateCommand.cs
    InfoCommand.cs
  Services/
    CliConfig.cs             profile model and resolution
    ProviderFactory.cs       the single provider bootstrap path
    FortniteApiClient.cs     AES key and mappings fetch, disk-cached
    JsonOutput.cs            stdout envelope and error codes
    GameParser.cs            EGame string parsing
```

Each command is a plain class exposing `Execute(options)` where `options` is an
already-parsed record. Argument parsing lives only in `Program.cs`. This keeps
commands unit-testable without constructing a parser, and means the parsing
library can be swapped without touching command logic.

`System.CommandLine` 2.0.11 (the current GA release; 3.0 is preview) provides
parsing, validation, and generated help.

### Divergence from upstream

`origin` points at `FabianFG/CUE4Parse` directly rather than a fork. This work
adds new directories plus two lines in `CUE4Parse.slnx` (the two new projects)
and one in `Directory.Packages.props` (`System.CommandLine`). Those two files are
the only conflict surface on `git pull --rebase`, and upstream touches them
infrequently. Everything else, `publish.ps1` included, lives under
`CUE4Parse.Cli/`.

## Command surface

```
cue4 [global options] <verb> [arguments]
```

| Verb | Purpose |
|---|---|
| `list` | Enumerate assets in the mounted VFS |
| `dump` | Deserialize exports to JSON |
| `export` | Convert meshes/anims/textures/materials to files |
| `unpack` | Raw byte extraction of packages |
| `update` | Refresh AES keys and mappings from fortnite-api.com |
| `info` | Report mount state, game version, missing AES GUIDs |

### Global options

`--profile/-p`, `--config`, `--paks`, `--game`, `--mappings`, `--aes`,
`--verbose/-v`.

Command-line flags override profile values so an agent can run a one-off against
a different game or key without editing configuration.

### Per-verb arguments

`list`, `dump`, `unpack` and `export` share one filter set, written `[filters]`
below: `[--glob <pat>]... [--regex <re>] [--ext <ext>] [--limit <n>]`. Giving
`list` filters the other three lacked would leave a caller unable to narrow a
bulk operation the same way they discovered it.

```
cue4 list    [filters] [--count]
cue4 dump    <path>... [filters] [--export <name>] [--class <name>]
             [-o <file>] [--indent] [--force]
cue4 export  <path>... [filters] -o <dir> [export options] [--parallel <n>] [--force]
cue4 unpack  <path>... [filters] -o <dir> [--flat] [--force]
cue4 update  [--aes-keys] [--usmap]
cue4 info
```

`dump` writes to stdout when `-o` is omitted, which is the expected mode for an
agent. With multiple paths or `--glob` it emits NDJSON, one object per asset,
regardless of `-o`.

`--export <name>` selects a single named export, corresponding to
`LoadPackageObject(path + "." + name)`. Without it, all exports are returned.

`--class <name>` filters exports by class. It belongs to `dump`, not `list`:
knowing an asset's class requires deserializing the package, which contradicts
`list` being the cheap discovery verb.

`info` takes no verb-specific flags. All output is JSON, so a `--json` flag would
be a no-op.

`update`'s flags are named after what they fetch — `--aes-keys` and `--usmap` —
rather than after the recursive global `--mappings` they must not collide with.

`unpack` preserves the asset's directory structure under `-o` unless `--flat` is
given. It writes **every payload file of a package** via `SavePackage`, so one
asset path yields `.uasset` + `.uexp` (+ `.ubulk`/`.uptnl` when present); a
`.uasset` alone is unopenable because the exports live in the `.uexp`. `--flat`
fails with `OUTPUT_COLLISION` when two *different* assets share a leaf name
rather than silently overwriting; a package's own payload files differ by
extension and never collide.

### Export options

These map one-to-one onto `ExportOptions` and its enums. Values below are taken
from the enum definitions in `CUE4Parse-Conversion/Options/`.

| Flag | Values | Default |
|---|---|---|
| `--mesh-format` | `actorx`, `gltf2`, `ueformat`, `usd` | `ueformat` |
| `--texture-format` | `png`, `jpeg`, `tga`, `webp` | `png` |
| `--texture-platform` | `desktop`, `xbox-ps4`, `switch`, `ps5` | `desktop` |
| `--mesh-quality` | `highest`, `lowest`, `all` | `highest` |
| `--nanite` | `nanite-only`, `no-nanite`, `nanite-first`, `nanite-last` | `no-nanite` |
| `--socket-format` | `socket`, `bone`, `none` | `bone` |
| `--material-depth` | `top-layer-only`, `all-layers-no-ref`, `all-layers` | `top-layer-only` |
| `--texture-quality` | 1–100 | 100 |
| `--no-materials` | flag | materials exported |
| `--all-mips` | flag | off |

`ETextureFormat` offers no DDS output. DDS data is reachable only through
`unpack`.

`--texture-platform` is exposed because console and Switch textures are swizzled
and decode incorrectly without it. `ExportOptions` also takes
`exportHdrTexturesAsHdr`, `exportMorphTargets` and `compressionFormat`; these
keep their defaults and are not surfaced unless a caller asks. Note that
`ExportOptions` rewrites `TextureFormat` to `Png` whenever `--mesh-format usd` is
selected, regardless of `--texture-format`.

### info

`AbstractVfsFileProvider` exposes `RequiredKeys`, `UnloadedVfs`, and
`MountedVfs`. `info` surfaces these so a caller can learn which AES GUIDs are
outstanding rather than retrying blindly. The same `RequiredKeys` check runs
inside the other verbs on a failed lookup, which is what lets them distinguish
"this asset does not exist" (exit 7) from "the archive holding it is still
encrypted" (exit 5).

`info` is also the quickest sanity check that mounting worked at all: a
`fileCount` of 0 against a populated paks directory means nothing mounted.

## Configuration

`cue4.json`, resolved in order: `--config`, then `./cue4.json`, then
`%APPDATA%\cue4\cue4.json`.

```json
{
  "defaultProfile": "fn",
  "profiles": {
    "fn": {
      "paksDir": "D:/Games/Fortnite/FortniteGame/Content/Paks",
      "game": "GAME_UE5_6",
      "mappings": "auto",
      "aes": {
        "main": "0x3A9B...",
        "dynamic": { "<guid>": "0x..." }
      }
    }
  }
}
```

`game` accepts either the `EGame` enum name (`GAME_UE5_6`) or engine-version
shorthand (`5.6`). `mappings` accepts a file path or the literal `auto`, which
uses the cached auto-fetched `.usmap`.

## Provider bootstrap

`ProviderFactory` is the only place a provider is constructed, so no command can
get the sequence subtly wrong:

1. `OodleHelper.Initialize()` — uses the built native library when present,
   otherwise downloads `oo2core`
2. `ZlibHelper.Initialize()`
3. `new DefaultFileProvider(paksDir, SearchOption.AllDirectories, versions, StringComparer.OrdinalIgnoreCase)`
4. `provider.MappingsContainer = new FileUsmapTypeMappingsProvider(path)`
5. `provider.Initialize()`
6. `provider.SubmitKeys(...)` for the main key and any dynamic keys
7. **`provider.Mount()`**
8. `provider.PostMount()`

Step 3 uses the `StringComparer` overload. The `bool isCaseInsensitive`
constructors are marked `[Obsolete]`.

**Step 7 is not optional and the order is not negotiable.** `Initialize()` only
scans the directory and registers VFS readers into the unloaded set; it mounts
nothing. `SubmitKeys` mounts only those readers whose `EncryptionKeyGuid`
matches a submitted key. A game with no encryption — or any profile without an
AES key — therefore mounts **zero** archives without `Mount()`, and every verb
returns an empty result set successfully. Mappings move ahead of `Initialize()`
to match every call site in `CUE4Parse.Tests`.

`ThrowIfKeysMissing(provider)` is called by the commands **only after a lookup
has already failed**, so "asset not found" stays distinguishable from "the
archive holding it is still encrypted". Calling it unconditionally after mount
would be wrong: a game can legitimately ship optional encrypted chunks the
caller does not need.

## Output contract

This is the interface an agent couples to, so it is specified precisely.

- **stdout carries structured data only.** A single JSON object for
  single-result verbs; newline-delimited JSON for multi-result verbs.
- **stderr carries everything else** — logging, warnings, progress — through the
  Serilog console sink.
- **Failures also emit JSON on stdout**, so a parser never encounters a bare
  string:

  Structured payloads are nested under `error.details`, so `code` and `message`
  are always the only two fixed keys a parser has to know:

  ```json
  {"error":{"code":"AES_KEY_MISSING","message":"...","details":{"missingGuids":["..."]}}}
  ```

### Exit codes

| Code | Meaning |
|---|---|
| 0 | Success |
| 1 | Unclassified error |
| 2 | Usage error |
| 3 | Configuration or profile error |
| 4 | Provider initialization or mount failure |
| 5 | AES key missing or wrong |
| 6 | Mappings missing or invalid |
| 7 | Asset not found |
| 8 | Export completed with per-item failures |

AES, mappings, and not-found get distinct codes because those are the three
failures a caller can recover from differently: supply a key, supply mappings,
or correct the path.

**Code 8 means an item genuinely failed.** `ExportSession.Add` throws
`NotSupportedException` for every object type without an exporter — DataAssets,
DataTables, StringTables, Blueprints and so on. Those are reported as
`{"status":"skipped"}` and leave the exit code at 0; only
`ExportResult.Success == false` (or a package that fails to load) produces 8.
Counting skips as failures would make almost every realistic glob return 8 and
drain the code of meaning.

## Fortnite auto-fetch

`FortniteApiClient` retrieves current AES keys and the latest `.usmap` from
fortnite-api.com, caching both under `%LOCALAPPDATA%\cue4\cache\` keyed by build
version. Only `update` performs network I/O; every other verb reads the cache.

The client sits behind an interface so tests can mock it and so a change to the
upstream API is a single-class replacement. If the API becomes unavailable,
`update` fails while all other verbs continue working from cache.

**`--aes auto` and `--mappings auto` are what consume the cache.** Writing
`aes.json` without a reader would leave `cue4 update --aes-keys` with no
observable effect anywhere in the tool. `--mappings auto` resolves to the cached
`.usmap`, failing with `MAPPINGS_NOT_FOUND` and a "run `cue4 update`" hint when
absent. `--aes auto` (or `"aes": { "main": "auto" }`) resolves to the cached
`aes.json` and supplies the main key **and every dynamic key** — Fortnite ships
encrypted chunks under non-zero GUIDs that the main key alone will not mount.
Profile-level `dynamic` entries are applied last, so they override the cache.

## Error handling

Failures are classified at the boundary and mapped to the exit codes above.
Three cases warrant specific treatment:

- **Missing AES keys.** Detected via `RequiredKeys` after mount. Reported with
  the outstanding GUIDs rather than a generic decryption failure.
- **Missing mappings.** Unversioned properties silently deserialize wrong
  without a `.usmap`. When mappings are absent and the package requires them,
  fail with code 6 rather than emitting misleading JSON.
- **Partial export failure.** `ExportSession` processes items in parallel; some
  may fail. Successful items are still written, each item's status appears in
  the NDJSON stream, and the process exits 8 rather than 0 or 1.

## Safety limits

A broad `--glob` can match a large fraction of a 500k-asset game. `unpack`,
`export`, and `dump` therefore refuse to process more than 1000 matched assets
in one invocation unless `--force` is given. `list` is exempt, since enumerating
paths is cheap and is how a caller discovers what a glob would match.

The limit breach is reported as a structured error carrying the match count, and
exits 2. It is never a silent truncation — an agent must be able to distinguish
"these are all the results" from "these are the first 1000".

## Testing

A separate `CUE4Parse.Cli.Tests` project, keeping changes confined to new
directories rather than editing the upstream-maintained test project.

Unit tests, requiring no game data:

- Config resolution and profile precedence, including flag-over-profile override
- `EGame` parsing for both enum names and shorthand
- Glob and regex path matching
- JSON envelope shape for success and error results
- Exception-to-exit-code mapping
- `FortniteApiClient` behaviour against a mocked HTTP handler

Integration tests against the existing fixtures in
`CUE4Parse.Tests/Fixtures/`:

- `dump` a fixture asset and assert the JSON structure
- `list` against a fixture archive and assert the returned paths

## Packaging

Published as a self-contained single-file executable:

```
dotnet publish CUE4Parse.Cli -c Release -r win-x64 --self-contained \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

`IncludeNativeLibrariesForSelfExtract` is required so `CUE4Parse-Natives.dll`
travels with the executable.

This is wrapped by `CUE4Parse.Cli/publish.ps1`, which lives **inside** the
project directory rather than at the repository root — the divergence claim
above only holds if this work adds no third root-level file.

Trimming and NativeAOT are excluded. `ObjectTypeRegistry` reflects over the
assembly at static initialization to register every `IPropertyHolder`
implementor; trimming would remove types it needs and break object construction
at runtime rather than at build time.

## Build prerequisites

Established while validating this design:

- .NET 10 SDK. Visual Studio 2022 cannot target `net10.0`; use the standalone
  SDK with the CLI or VS Code, or Visual Studio 2026.
- CMake and an MSVC toolchain for `CUE4Parse-Natives`. Failure is non-fatal but
  silently disables ACL animation decompression.
- `git submodule update --init --recursive`. The nested `sjson-cpp` submodule
  under ACL does not initialize through recursion and must be cloned directly.

## Open risks

- **Third-party API dependency.** fortnite-api.com is outside our control.
  Contained behind an interface and a disk cache.
- **Upstream divergence.** Limited to `CUE4Parse.slnx` and
  `Directory.Packages.props`.
- **Broad glob performance.** Mitigated by the limit-and-force mechanism, not
  eliminated. If usage shifts toward whole-game sweeps, a persisted index cache
  and streaming output become necessary.
