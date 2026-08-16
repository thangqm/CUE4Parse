# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

CUE4Parse is a C# library that reads Unreal Engine 4/5/6 archives (`.pak`, `.utoc`/`.ucas`) and packages (`.uasset`/`.umap`), deserializes `UObject` exports, and (via the Conversion project) exports meshes/animations/textures/materials to glTF, UEFormat, ActorX (psk/psa), USD, PNG/DDS, etc. Primary consumer is [FModel](https://github.com/4sval/FModel).

## Build & test

Targets `net10.0` (set in `Directory.Build.props`, which also holds the shared package `<Version>`). NuGet versions are managed centrally in `Directory.Packages.props` — `PackageReference` entries in csproj files carry **no** `Version` attribute; add new versions there.

```bash
git submodule update --init --recursive   # required for ACL (animation decompression)
dotnet restore
dotnet build -c Release
dotnet test CUE4Parse.Tests/CUE4Parse.Tests.csproj -c Release
```

The test project uses xunit.v3 with `UseMicrosoftTestingPlatformRunner`, so it builds as an executable. To run a single test:

```bash
dotnet run --project CUE4Parse.Tests/CUE4Parse.Tests.csproj -- --filter-method "*DuplicateClasses*"
```

CI (`.github/workflows/tests.yml`) runs the full test project on Linux for every PR. `nuget_push.yml` packs and publishes monthly using the `Directory.Build.props` version plus a `.YYYYMM` suffix.

### Native library

`CUE4Parse.csproj` has a `Build-Natives` target that shells out to CMake in `CUE4Parse-Natives/` before/after every build and copies `CUE4Parse-Natives.{dll,so,dylib}` to the output. It builds ACL (animation decompression) and optionally Oodle. **Failure is non-fatal** — the build prints a warning and continues, so a missing CMake or an uninitialized `ACL/external/acl` submodule silently degrades ACL-compressed animation support rather than breaking the build.

Set `CUE4PARSE_SKIP_NATIVE=true` to skip the CMake step entirely (useful when iterating on managed code only). `dotnet clean` deletes `CUE4Parse-Natives/builddir` and `bin/`.

Compression backends that are *not* statically linked are initialized at runtime by the consumer: `OodleHelper.Initialize()` (falls back to downloading `oo2core` if the native lib isn't present) and `ZlibHelper.Initialize()`.

## Architecture

### Read path

```
IFileProvider  →  VFS readers  →  GameFile  →  IPackage  →  UObject exports
```

- **`CUE4Parse/FileProvider/`** — `AbstractFileProvider` is the base; `AbstractVfsFileProvider` adds pak/iostore mounting. Concrete providers: `DefaultFileProvider` (directory on disk), `StreamedFileProvider`, `ApkFileProvider`, `EmscriptenDataFileProvider`. `FileProviderDictionary` is the path→`GameFile` index; `VirtualPaths` maps plugin/game roots.
- **`CUE4Parse/UE4/Pak/`** and **`CUE4Parse/UE4/IO/`** — `PakFileReader` and `IoStoreReader`, both under `UE4/VirtualFileSystem/` abstractions (`AbstractAesVfsReader` handles AES-encrypted indices; keys are submitted per-GUID via `provider.SubmitKey`).
- **`CUE4Parse/UE4/Assets/`** — `Package` (legacy `.uasset`) and `IoPackage` (iostore, unversioned properties) both derive from `AbstractUePackage`. `AbstractUePackage.ConstructObject` walks the `UStruct` super chain until it finds a class registered in code, falling back to `UObject`.
- **`CUE4Parse/UE4/Assets/Exports/`** — the `UObject` type hierarchy, grouped by engine module (Animation, Material, SkeletalMesh, Sound, Texture, Landscape, Niagara, …). **`CUE4Parse/UE4/Objects/`** holds non-`UObject` structs (`FVector`, `FGuid`, mesh/physics/rendering structs).
- **`CUE4Parse/UE4/Readers/`** — `FArchive` and friends. `FAssetArchive` (under `UE4/Assets/Readers/`) is what `Deserialize` receives; it exposes `Ar.Game`, `Ar.Ver`, `Ar.Owner`.

### Object type registration

`ObjectTypeRegistry` reflects over the assembly at static-init time and registers every non-abstract `IPropertyHolder` implementor, stripping a leading `U`/`A` from the type name (`UTexture2D` → `Texture2D`). Consequences:

- **Class names must be globally unique across the whole assembly graph** — `DuplicateClassesTest` fails the build otherwise. This is the main constraint when adding game-specific types; prefix them (`UFortItemDefinition`, `GFPSkeletalMesh`).
- Mark a type `[SkipObjectRegistration]` to keep it out of the registry.
- `RegisterEngine(assembly)` / `RegisterClass(name, type)` let downstream apps add their own types.

### Versioning — the central mechanism

`VersionContainer` (in `UE4/Versions/`) carries `EGame`, `FPackageFileVersion`, licensee version, custom versions, texture platform, plus two derived dictionaries:

- `Options` — named booleans like `"SkeletalMesh.UseNewCookedFormat"`, computed from the game in `InitOptions()` and overridable by the caller. Prefer adding an option here over scattering game checks when the same behavior differs across many games.
- `MapStructTypes` — key/value struct types for `TMap` properties that can't be inferred.

`EGame` encodes engine version in the high bytes (`0x0400NN00` style) with per-game values slotted after each engine base, so `Ar.Game >= GAME_UE5_1` range checks work. `GameUtils.GetVersion()` maps an `EGame` to the package file version. `CUE4Parse/Globals.cs` has `global using static CUE4Parse.UE4.Versions.EGame;`, so `GAME_*` constants are usable unqualified everywhere, as is `Log` (from `CUE4ParseLog`).

Serialization quirks are expressed inline as `if (Ar.Game == GAME_X) Ar.Position += 4;` or `if (Ar.Ver >= EUnrealEngineObjectUE4Version.FOO)`. This is the established idiom — follow it rather than introducing new abstractions.

### Game-specific support

`CUE4Parse/GameTypes/<GAME>/` mirrors the main tree (`Assets/Exports/`, `Objects/`, `Encryption/Aes/`, `UE4/Pak/`) and holds anything specific to one title: custom export classes, custom AES/decryptors, custom pak readers, name-hash maps. There are ~100 such folders; look at `GameTypes/PUBG/` or `GameTypes/FN/` for the shape. Adding a game usually means: an `EGame` entry, optionally `Options`/`MapStructTypes` tweaks in `VersionContainer`, and a `GameTypes/` folder for anything that can't be expressed as a version check.

### Mappings

`CUE4Parse/MappingsProvider/` parses `.usmap`/`.jmap` type mappings, required to deserialize unversioned properties (UE4.25+ / iostore packages). Wired via `provider.MappingsContainer`.

### Conversion layer

`CUE4Parse-Conversion` (root namespace `CUE4Parse_Conversion`, note the underscore) depends on `CUE4Parse`:

- `ExportSession` is the entry point — a queue-based, parallel session that dispatches a `UObject` to the right `ExporterBase` subclass (`MeshExporter`, `AnimationExporter`, `TextureExporter`, `MaterialExporter`, `WorldExporter`, …) and returns `ExportResult`. An exporter must be added to a session before use. Note that the README still documents the removed `new Exporter(...)` / `TryWriteToDir` API — it is out of date.
- `Dto/` holds the intermediate representations meshes/anims are converted into; `Legacy/` holds `[Obsolete]` converters (`MeshConverter`, `AnimConverter`, `PoseAssetConverter`) kept for compatibility — construct the DTOs directly instead.
- `Formats/` picks the serialization strategy per output format (`IMeshExportFormat` → ActorX/Gltf/UEFormat/USD); `Writers/` holds the actual format writers; `Textures/` holds block-compression decoders and platform deswizzlers.
- `ExportOptions` (in `Options/`) selects mesh/texture/socket formats and quality.

### CLI

`CUE4Parse.Cli` builds `cue4.exe`, the automation front end: `info`, `list`, `dump`, `unpack`, `export`, `update`. All structured output goes to stdout as JSON or NDJSON; all logging goes to stderr. `Program.cs` does argument parsing only — each verb is a plain class taking a parsed options record, so commands are testable without a parser, and `Services/ProviderFactory.cs` is the single provider bootstrap path. `CUE4Parse.Cli/publish.ps1` produces the standalone binary; it deliberately avoids trimming and NativeAOT, which break `ObjectTypeRegistry`'s static-init reflection. See [CUE4Parse.Cli/README.md](CUE4Parse.Cli/README.md).

## Conventions

- Nullable and implicit usings are enabled solution-wide; `AllowUnsafeBlocks` is on for `CUE4Parse` and `CUE4Parse-Conversion`.
- Logging goes through `CUE4ParseLog.Log` (Serilog, `SourceContext` forced to `"CUE4Parse"` so consumers can filter the whole library with one override — see `SourceContextTest`).
- Optional compile-time defines are documented in [CUE4Parse/Defines.md](CUE4Parse/Defines.md): `NAME_HASHES`, `NO_FNAME_VALIDATION`, `NO_STRING_NULL_TERMINATION_VALIDATION`, `READ_SHADER_MAPS`, `USE_LZ4_NATIVE_LIB`.
- Runtime toggles live in `CUE4Parse/Globals.cs` (`LogVfsMounts`, `FatalObjectSerializationErrors`, `WarnMissingImportPackage`).
- Test fixtures are real game assets under `CUE4Parse.Tests/Fixtures/<engine version>/`, copied to output on build.
