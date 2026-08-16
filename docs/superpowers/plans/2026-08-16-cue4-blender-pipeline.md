# cue4 → Blender Pipeline Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `cue4` the only tool in the pipeline — a `.glb` it exports opens in Blender with textures, alpha and correct normals, described by a written output contract that a headless bpy script can consume without human intervention.

**Architecture:** Material binding happens inside `CUE4Parse-Conversion/Writers/Gltf/`, not as a CLI post-process. A new pure `GltfMaterialBinder` turns a `CMaterialParams2` into a SharpGLTF `MaterialBuilder` whose images are **external URIs** (never embedded), computed from the same texture classification `MaterialExporter` already uses, so the referenced files and the written files are the same set by construction. A new `MeshExportContext` record carries `SaveDirectory` and `ExportOptions` into the writers. A new `TextureFileNamer` is the single predictor of texture file names. The CLI grows a deterministic manifest; the library stays ignorant of it.

**Tech Stack:** .NET 10, SharpGLTF.Core / SharpGLTF.Toolkit 1.0.6, SkiaSharp 2.88.9, System.CommandLine 2.0.11, Newtonsoft.Json, Serilog, xunit.v3, glTF-Validator (pinned standalone binary), Blender (pinned tarball, headless).

**Spec:** [docs/superpowers/specs/2026-08-16-cue4-blender-pipeline-design.md](../specs/2026-08-16-cue4-blender-pipeline-design.md) — **read revision 3's header note first**; the design review changed the substrate, the scope and the target.

**Branch:** `worktree-feat-cue4-cli`, in the worktree at `.claude/worktrees/feat-cue4-cli`. All paths below are relative to that worktree root.

**Target for this round: Phase 0 + Phase 1.** Together they deliver the one thing that matters — a `.glb` that opens in Blender with textures — and after revision 3 they are fully verifiable on the fixtures that exist. Phases 2 and 3 are conveniences; decide on them after using Phase 1 output for real work. Task 17 has been rewritten from a behaviour change into a characterization test (spec §6.2).

---

## Global Constraints

- Target framework is `net10.0`, inherited from `Directory.Build.props`. Never add `<TargetFramework>` to a csproj.
- NuGet versions are centrally managed. `PackageReference` entries carry **no** `Version` attribute; add a `PackageVersion` to `Directory.Packages.props` instead.
- Nullable reference types and implicit usings are enabled solution-wide. `AllowUnsafeBlocks` is on for `CUE4Parse` and `CUE4Parse-Conversion`.
- All CLI JSON uses **Newtonsoft.Json**, never `System.Text.Json`.
- **stdout carries structured data only.** All logging goes to stderr through `CUE4ParseLog.Log` / Serilog.
- Exit codes: `0` success, `1` unclassified, `2` usage, `3` config, `4` mount, `5` AES key, `6` mappings, `7` not found, `8` partial export failure.
- Class names must be globally unique across the assembly graph (`ObjectTypeRegistry`). This does not apply to exporters/formats, which are not `IPropertyHolder`.
- Game-specific serialization quirks stay inline as `if (Ar.Game == GAME_X)`. Do not introduce new abstractions for them.
- **Fork first.** `origin` is `FabianFG/CUE4Parse` directly. This plan modifies ~14 files in the conversion layer that upstream touches ~66 times a year, with signature-breaking changes. Before Task 1: fork to your own remote and rebase `worktree-feat-cue4-cli` onto it. Spec §4.6 sorts the changes into a PR-upstream bucket and a keep-local bucket; tag each commit's body with which bucket it belongs to, so the PR series can be assembled later without archaeology.
- **Still do not edit `.github/workflows/tests.yml`** — CI for the CLI goes into a new `cli-tests.yml`. That file is upstream-owned and touching it buys nothing.
- **The fixture set is read-only.** There is no UE 5.8 and no `CUE4ParseFixtures` project on this machine (spec §9.1). No task may assume a fixture can be added. Where the fixtures fall short, tests synthesize objects instead — that is why `GltfMaterialBinder.Bind` takes a `CMaterialParams2` (Task 5).
- Set `CUE4PARSE_SKIP_NATIVE=true` when iterating on managed code to skip the CMake native step.
- Run a single test:
  `dotnet run --project CUE4Parse.Cli.Tests/CUE4Parse.Cli.Tests.csproj -- --filter-method "*TestName*"`
  `dotnet run --project CUE4Parse.Tests/CUE4Parse.Tests.csproj -- --filter-method "*TestName*"`
- Integration tests must export into a **fresh temp directory per run** (`Directory.CreateTempSubdirectory()`), never a shared path. A leftover file from a previous run turns "every URI resolves to a real file" green for the wrong reason.
- Fixtures are the redistributable `CUE4Parse.Tests/Fixtures/UE5_8/` set. Never add Stellar Blade / `Lemi21_Mods` game assets to the repo.

### Verified SharpGLTF 1.0.6 facts

Confirmed by compiling and running a probe against the real package on 2026-08-16. Do not deviate; do not "fix" these from memory.

- `VertexGeometryDelta` has exactly one usable constructor: **`VertexGeometryDelta(Vector3 p, Vector3 n, Vector3 t)`** with fields in that order — `PositionDelta`, `NormalDelta`, `TangentDelta`. The spec's §6.1 slot analysis is therefore correct: `TangentZDelta` (which *is* the normal in UE) belongs in the **second** argument.
- `WriteSettings` has `ImageWriting` (`ResourceWriteMode.Default | SatelliteFile | EmbeddedAsBase64 | BufferView`) and `ImageWriteCallback` of delegate type `ImageWriterCallback`:
  `string ImageWriterCallback(WriteContext context, string assetName, MemoryImage image)`.
- `ModelRoot.WriteGLB(WriteSettings settings)` returns `ArraySegment<byte>`.
- `ImageBuilder.AlternateWriteFileName` is propagated to the written `Image` and arrives as the `assetName` argument of `ImageWriteCallback`.
- Setting `ImageWriting = ResourceWriteMode.SatelliteFile` plus an `ImageWriteCallback` that returns a string emits `"images":[{"name":…,"uri":"<that string>"}]` with **no** image bytes in the GLB buffer and **no** satellite file written. Verified output for a 3-triangle model:
  `"images":[{"name":"../C/T_A.png","uri":"../C/T_A.png"}], "textures":[{"source":0}]`, `"buffers":[{"byteLength":380}]` (geometry only).
- **`SceneBuilder.ToGltf2()` deduplicates images by content, not by name.** Two `ImageBuilder`s with identical bytes collapse into one `LogicalImage` even when their `Name` and `AlternateWriteFileName` differ. A per-texture placeholder image must therefore have **content unique to its URI** — see `GltfMaterialBinder.CreateImage`.
- Two materials referencing the same URI correctly share one `images`/`textures` entry (verified: 3 materials, 2 distinct URIs → 2 images, third material points back at index 0).
- **`ImageBuilder.AreEqualByContent(a, b)` returns `false` even for byte-identical images.** Compare `a.Content` and `b.Content` with `MemoryImage.AreEqual` (or `==`) instead — that is what actually tracks the deduplication behaviour.
- `MaterialBuilder.WithMetallicRoughnessShader()` sets `ShaderStyle == "PBRMetallicRoughness"`. `GetChannel(KnownChannel.X)` returns `null` for a channel that was never set, and `GetChannel(...).Texture.PrimaryImage` reaches the `ImageBuilder`.
- `meshes[].name` comes from the `MeshBuilder<...>(name)` constructor; `scenes[].name` from `SceneBuilder(name)`.
- `SceneBuilder.AddRigidMesh` overloads: `(IMeshBuilder, NodeBuilder)`, `(IMeshBuilder, AffineTransform)`, `(IMeshBuilder, NodeBuilder, AffineTransform)`. Use the `NodeBuilder` overload to name the node.
- `MaterialBuilder` API used here: `new MaterialBuilder(string name)`, `.WithMetallicRoughnessShader()`, `.WithBaseColor(ImageBuilder, Vector4?)`, `.WithBaseColor(Vector4)`, `.WithMetallicRoughness(ImageBuilder, float?, float?)`, `.WithNormal(ImageBuilder, float)`, `.WithEmissive(ImageBuilder, Vector3?, float)`, `.WithEmissive(Vector3, float)`, `.WithAlpha(AlphaMode, float)`, `.WithDoubleSide(bool)`.
- `AlphaMode` is **ambiguous** between `SharpGLTF.Materials.AlphaMode` and `SharpGLTF.Schema2.AlphaMode` when both namespaces are imported. Always write `SharpGLTF.Materials.AlphaMode.MASK` fully qualified in `Writers/Gltf/`.

### Verified CUE4Parse facts

- `ExporterBase.SavePath` = `<packagePath>` when the package leaf equals the object name, else `<packagePath>/<objectName>`; `SaveDirectory` is `SavePath` minus its last segment. Both are pure string math — no disk access, no ordering dependency.
- `ExporterBase.Resolve(UObject obj, string fromDirectory, string extension)` already produces the relative reference (`"./x.png"` or `"../a/b/x.png"`) used by `UsdMaterialFormat`.
- `ExportSession.Add(ExporterBase)` deduplicates on `ObjectPath`, first-wins, case-insensitive.
- `FTexturePlatformData.PixelFormat` is a **`string`** (e.g. `"PF_BC6H"`), not an `EPixelFormat`. Parse with `Enum.TryParse<EPixelFormat>`.
- `UTexture` exposes `CompressionSettings`, `IsNormalMap`, `GetFirstMipIndex()`, `PlatformData`, `DecodeMip(int, ETexturePlatform)`.
- `CMaterialParams2.TryGetTexture2d(out UTexture texture, params string[] names)`; the classification arrays are `Diffuse[0]`, `Normals[0]`, `SpecularMasks[0]`, `Emissive[0]` plus the `Fallback*` constants; colour arrays are `DiffuseColors[0]`, `EmissiveColors[0]` read via `TryGetLinearColor`.
- `CMaterialParams2` carries `BlendMode`, but **not** `TwoSided` and **not** `OpacityMaskClipValue`. `UMaterial` has both; `UMaterialInstance.BasePropertyOverrides` has `OpacityMaskClipValue` (0 when unset) and `Parent`.
- All six `IMeshExportFormat.Build*` call sites live in `MeshExporter<T>` subclasses: `SkeletalMeshExporter`, `StaticMeshExporter`, `SkeletonExporter`, `SplineMeshExporter`, `LandscapeMeshExporter`, `LandscapeMeshExporter2`.

---

## File Structure

**New files**

| File | Responsibility |
|---|---|
| `CUE4Parse-Conversion/Formats/Meshes/MeshExportContext.cs` | Record carrying `ObjectName`, `ObjectPath`, `SaveDirectory`, `Options`, `MaterialPaths` into mesh writers. |
| `CUE4Parse-Conversion/Exporters/TextureFileNamer.cs` | Single predictor of a texture's `(extension, suffix)` without decoding. |
| `CUE4Parse-Conversion/Writers/Gltf/GltfMaterialBinder.cs` | Pure `UMaterialInterface` → `MaterialBuilder`, external image URIs, alpha/side. |
| `CUE4Parse-Conversion/Exporters/OrmTextureExporter.cs` | Repacks a `SpecularMasks` texture into glTF ORM channel order under its own `ObjectPath`. |
| `CUE4Parse-Conversion/Exporters/SoundExporter.cs` | Wraps `SoundDecoder.Decode`, writes raw audio bytes. |
| `CUE4Parse.Cli/Output/ExportManifest.cs` | `ExportResult[]` → deterministic manifest JSON with sha256. |
| `CUE4Parse.Cli.Tests/*Tests.cs` | New test files, one per subject. |
| `CUE4Parse.Tests/BCDecoderTests.cs` | Exhaustive block-compression decode tests. |
| `.github/workflows/cli-tests.yml` | CI for `CUE4Parse.Cli.Tests` + glTF-Validator. |
| `tools/gltf-validator.version` | The pinned glTF-Validator release tag, one line. |
| `tools/parity/` | `PixDiff.ps1`, `glbcmp.py`, `GlbInfo.ps1`, `BinDiff.ps1`. |
| `docs/cue4-output-contract.md` | The output contract (spec §8). |
| `docs/cue4-guide.md` | The user guide, moved in from `C:\tools\cue4_guide.txt`. |

**Modified files**

`CUE4Parse-Conversion/ExportSession.cs` · `ExportResult.cs` · `Exporters/ExporterBase.cs` · `Exporters/TextureExporter.cs` · `Exporters/MaterialExporter.cs` · `Exporters/MeshExporter.cs` and its six subclasses · `Formats/Meshes/IMeshExportFormat.cs` + `ActorXMeshFormat` `GltfMeshFormat` `UEFormatMeshFormat` `UsdMeshFormat` · `Writers/Gltf/Gltf.cs` · `Writers/UEFormat/UEModel.cs` · `Textures/BC/BCDecoder.cs` · `Textures/TextureEncoder.cs` · `Options/ExportOptions.cs` · `CUE4Parse.Cli/Program.cs` · `Commands/ExportCommand.cs` · `Commands/InfoCommand.cs` · `Services/ExportOptionsMapper.cs` · `Services/ProviderFactory.cs` · `Services/CommandContext.cs` · `CUE4Parse.Cli.Tests/CUE4Parse.Cli.Tests.csproj` · `CLAUDE.md`.

---

# Phase 0 — Make verification possible

Nothing below Phase 0 can be trusted until CI runs the CLI tests on Linux and the path separator bug is gone.

> **Ordering note:** the spec lists the path fix before CI. This plan runs CI first, because the path fix's failure mode is *Linux-only* and CI is the only place it is observable. Task 2's test is green on Windows both before and after the fix — that is stated in the task and is not a defect in the test.

## Task 1: CI for the CLI test project, with a pinned glTF-Validator

**Files:**
- Create: `.github/workflows/cli-tests.yml`
- Create: `tools/gltf-validator.version`
- Modify: `CUE4Parse.Cli.Tests/CUE4Parse.Cli.Tests.csproj`
- Test: `CUE4Parse.Cli.Tests/GltfValidatorTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: environment variable `GLTF_VALIDATOR` pointing at the validator binary when present; `GltfValidator.TryLocate(out string path)` and `GltfValidator.Validate(string glbPath)` returning `GltfValidatorReport(int Errors, int Warnings, string Raw)`, used by Task 9.

- [ ] **Step 1: Add the Linux SkiaSharp native asset to the CLI test project**

`CUE4Parse.Cli.Tests` encodes PNGs through SkiaSharp once export tests exist. `CUE4Parse.Tests` already carries this reference; the CLI test project does not, and without it every texture export test throws `DllNotFoundException: libSkiaSharp` on the Linux runner.

In `CUE4Parse.Cli.Tests/CUE4Parse.Cli.Tests.csproj`, inside the existing `PackageReference` `ItemGroup`, add:

```xml
    <PackageReference Include="SkiaSharp.NativeAssets.Linux.NoDependencies">
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
```

- [ ] **Step 2: Pin the validator version**

Fetch the newest release tag and record it. Run:

```bash
curl -s https://api.github.com/repos/KhronosGroup/glTF-Validator/releases/latest | grep '"tag_name"'
```

Write the tag (without a leading `v` if the tag has none) into `tools/gltf-validator.version` as a single line with a trailing newline. If the API is unreachable, use `2.0.0-dev.3.10`.

```
2.0.0-dev.3.10
```

Do **not** use the `gltf-validator` npm package: it exposes a JavaScript API rather than a CLI, so "one npm step" is really npm plus a hand-written JS driver.

- [ ] **Step 2b: Pin the Blender version**

Same shape, same reason. Spec §9.2 makes Blender a verification dependency because goal
condition #1 — "opens in Blender showing textures" — is otherwise checked by nothing, and
glTF-Validator structurally cannot check it: it does not resolve URIs the way Blender's
importer does, does not decode PNG payloads, and has no concept of a shader node.

Write a single line into `tools/blender.version`, e.g.:

```
4.2.5
```

Pick an LTS. The download URL used in Step 5 is
`https://download.blender.org/release/Blender<MAJOR.MINOR>/blender-<VERSION>-linux-x64.tar.xz`,
so the version string must match a real release directory. Pinning is not optional here for
the same reason it was not for the validator: "imports cleanly into Blender" only means
something against a Blender that does not move underneath the claim.

- [ ] **Step 3: Write the validator wrapper and its failing test**

Create `CUE4Parse.Cli.Tests/GltfValidatorTests.cs`:

```csharp
using System.Diagnostics;
using Newtonsoft.Json.Linq;

namespace CUE4Parse.Cli.Tests;

public readonly record struct GltfValidatorReport(int Errors, int Warnings, string Raw);

/// <summary>
/// Locates the pinned glTF-Validator binary. CI sets GLTF_VALIDATOR; developers
/// who have not installed it get skipped tests rather than false green ones.
/// </summary>
public static class GltfValidator
{
    public static bool TryLocate(out string path)
    {
        path = Environment.GetEnvironmentVariable("GLTF_VALIDATOR") ?? string.Empty;
        return path.Length > 0 && File.Exists(path);
    }

    public static GltfValidatorReport Validate(string glbPath)
    {
        Assert.True(TryLocate(out var exe), "GLTF_VALIDATOR is not set to an existing file.");

        using var process = Process.Start(new ProcessStartInfo(exe, ["-o", "-r", "-a", glbPath])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;

        // The validator writes its report to stdout with -o; -r keeps it quiet about
        // resources it cannot resolve relative to the working directory.
        var raw = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();

        var issues = JObject.Parse(raw)["issues"];
        return new GltfValidatorReport(
            issues?["numErrors"]?.Value<int>() ?? -1,
            issues?["numWarnings"]?.Value<int>() ?? -1,
            raw);
    }
}

public class GltfValidatorTests
{
    [Fact]
    public void ValidatorIsReachableAndReportsZeroErrorsOnAMinimalGlb()
    {
        if (!GltfValidator.TryLocate(out _))
        {
            Assert.Skip("GLTF_VALIDATOR not set; install the pinned validator to run this test.");
        }

        // 12-byte header + empty JSON chunk is not valid glTF; the smallest thing we
        // can assert without depending on the exporter is that the tool runs and
        // produces a parseable report for a real file written by SharpGLTF.
        var dir = Directory.CreateTempSubdirectory();
        var glb = Path.Combine(dir.FullName, "minimal.glb");
        File.WriteAllBytes(glb, MinimalGlb.Bytes());

        var report = GltfValidator.Validate(glb);

        Assert.Equal(0, report.Errors);
    }
}
```

Create `CUE4Parse.Cli.Tests/MinimalGlb.cs`:

```csharp
using System.Numerics;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;

namespace CUE4Parse.Cli.Tests;

/// <summary>A one-triangle GLB, used to prove the validator harness itself works.</summary>
public static class MinimalGlb
{
    public static byte[] Bytes()
    {
        var mesh = new MeshBuilder<VertexPosition, VertexEmpty, VertexEmpty>("Minimal");
        var prim = mesh.UsePrimitive(new MaterialBuilder("Mat").WithMetallicRoughnessShader());
        prim.AddTriangle(
            new VertexBuilder<VertexPosition, VertexEmpty, VertexEmpty>(new VertexPosition(0, 0, 0)),
            new VertexBuilder<VertexPosition, VertexEmpty, VertexEmpty>(new VertexPosition(1, 0, 0)),
            new VertexBuilder<VertexPosition, VertexEmpty, VertexEmpty>(new VertexPosition(0, 0, 1)));

        var scene = new SceneBuilder("Minimal");
        scene.AddRigidMesh(mesh, Matrix4x4.Identity);
        return scene.ToGltf2().WriteGLB().ToArray();
    }
}
```

- [ ] **Step 4: Run the test to see it skip locally**

Run: `dotnet run --project CUE4Parse.Cli.Tests/CUE4Parse.Cli.Tests.csproj -- --filter-method "*ValidatorIsReachable*"`
Expected: the test is reported as skipped with "GLTF_VALIDATOR not set".

- [ ] **Step 5: Write the CI workflow**

Create `.github/workflows/cli-tests.yml`:

```yaml
name: CLI Tests

on:
  pull_request:
  push:
    branches: [worktree-feat-cue4-cli]

permissions:
  contents: read

concurrency:
  group: cli-tests-${{ github.event.pull_request.number || github.ref }}
  cancel-in-progress: true

jobs:
  cli-tests-linux:
    name: cli-tests-linux
    runs-on: ubuntu-latest
    timeout-minutes: 30

    steps:
      - name: Checkout
        uses: actions/checkout@v7
        with:
          submodules: recursive

      - name: Setup .NET
        uses: actions/setup-dotnet@v6
        with:
          dotnet-version: 10.0.x

      - name: Install pinned glTF-Validator
        run: |
          set -euo pipefail
          VERSION="$(cat tools/gltf-validator.version)"
          URL="https://github.com/KhronosGroup/glTF-Validator/releases/download/${VERSION}/gltf_validator-${VERSION}-linux64.tar.xz"
          curl -fsSL "$URL" -o /tmp/gltf_validator.tar.xz
          mkdir -p /tmp/gltf-validator
          tar -xJf /tmp/gltf_validator.tar.xz -C /tmp/gltf-validator
          echo "GLTF_VALIDATOR=/tmp/gltf-validator/gltf_validator" >> "$GITHUB_ENV"
          /tmp/gltf-validator/gltf_validator --version

      - name: Install pinned Blender
        run: |
          set -euo pipefail
          VERSION="$(cat tools/blender.version)"
          SERIES="${VERSION%.*}"
          URL="https://download.blender.org/release/Blender${SERIES}/blender-${VERSION}-linux-x64.tar.xz"
          curl -fsSL "$URL" -o /tmp/blender.tar.xz
          mkdir -p /tmp/blender
          tar -xJf /tmp/blender.tar.xz -C /tmp/blender --strip-components=1
          echo "BLENDER=/tmp/blender/blender" >> "$GITHUB_ENV"
          /tmp/blender/blender --version

      - name: Run CLI tests
        run: dotnet test CUE4Parse.Cli.Tests/CUE4Parse.Cli.Tests.csproj --configuration Release --logger "console;verbosity=normal"
```

- [ ] **Step 6: Verify the workflow parses and the validator URL is real**

Run locally, substituting the pinned version:

```bash
VERSION="$(cat tools/gltf-validator.version)"
curl -fsSLI "https://github.com/KhronosGroup/glTF-Validator/releases/download/${VERSION}/gltf_validator-${VERSION}-linux64.tar.xz" | head -1
```

Expected: `HTTP/2 200` (after redirects). If it 404s, the asset naming differs for that release — list the release assets with
`curl -s https://api.github.com/repos/KhronosGroup/glTF-Validator/releases/tags/${VERSION} | grep browser_download_url`
and correct the URL in the workflow.

- [ ] **Step 7: Commit**

```bash
git add .github/workflows/cli-tests.yml tools/gltf-validator.version CUE4Parse.Cli.Tests/CUE4Parse.Cli.Tests.csproj CUE4Parse.Cli.Tests/GltfValidatorTests.cs CUE4Parse.Cli.Tests/MinimalGlb.cs
git commit -m "ci: run CLI tests on Linux with a pinned glTF-Validator"
```

---

## Task 2: Use the platform path separator when resolving output paths

**Files:**
- Modify: `CUE4Parse-Conversion/ExportSession.cs:159-165`
- Test: `CUE4Parse.Cli.Tests/ExportPathTests.cs`

**Interfaces:**
- Consumes: `FixtureSupport.Profile()`, `FixtureSupport.Context()`.
- Produces: nothing new; `ExportResult.DiskFilePaths` now carries native separators.

- [ ] **Step 1: Write the failing test**

Create `CUE4Parse.Cli.Tests/ExportPathTests.cs`:

```csharp
using CUE4Parse.Cli.Commands;
using CUE4Parse.Cli.Output;

namespace CUE4Parse.Cli.Tests;

public class ExportPathTests
{
    /// <summary>
    /// ExportSession.ResolveOutputPath used to end with Replace('/', '\\') unconditionally.
    /// On Linux '\' is a legal file name character, so the whole tree collapsed into a
    /// single file named "\tmp\out\..." in the working directory while the real
    /// directories were created empty next to it. Nothing threw.
    /// NOTE: this test is green on Windows both before and after the fix — the bug is
    /// Linux-only. Trust the CI run, not a local Windows pass.
    /// </summary>
    [Fact]
    public async Task ExportWritesFilesUsingThePlatformSeparatorAndTheyExistOnDisk()
    {
        var output = Directory.CreateTempSubdirectory();
        var (context, _) = FixtureSupport.Context();

        var code = await ExportCommand.ExecuteAsync(context, new ExportCommandOptions(
            Paths: ["CUE4ParseFixtures/Content/Fixtures/Materials/M_Fixture.uasset"],
            Criteria: new MatchCriteria(null, null, null, null),
            Output: output,
            Flags: ExportFlagDefaults.Gltf2(),
            Parallel: 1,
            Force: false), CancellationToken.None);

        Assert.Equal((int)ExitCode.Success, code);

        var written = Directory.GetFiles(output.FullName, "*", SearchOption.AllDirectories);
        Assert.NotEmpty(written);

        var foreign = Path.DirectorySeparatorChar == '/' ? '\\' : '/';
        Assert.All(written, path => Assert.DoesNotContain(foreign, Path.GetFileName(path)));

        // Nothing may have leaked into the process working directory.
        Assert.Empty(Directory.GetFiles(Directory.GetCurrentDirectory(), "*tmp*"));
    }
}
```

Create `CUE4Parse.Cli.Tests/ExportFlagDefaults.cs` — every later export test reuses it, so it is defined once here:

```csharp
using CUE4Parse.Cli.Commands;

namespace CUE4Parse.Cli.Tests;

/// <summary>The flag record every export test starts from, so a new flag added to
/// ExportFlags breaks one file instead of a dozen.</summary>
public static class ExportFlagDefaults
{
    public static ExportFlags Gltf2() => new(
        MeshFormat: "gltf2",
        TextureFormat: "png",
        TexturePlatform: "desktop",
        MeshQuality: "highest",
        Nanite: "no-nanite",
        SocketFormat: "bone",
        MaterialDepth: "top-layer-only",
        TextureQuality: 100,
        NoMaterials: false,
        AllMips: false);
}
```

- [ ] **Step 2: Run the test**

Run: `dotnet run --project CUE4Parse.Cli.Tests/CUE4Parse.Cli.Tests.csproj -- --filter-method "*ExportWritesFilesUsingThePlatformSeparator*"`
Expected on Windows: PASS (the bug is invisible here). Expected on Linux/CI before the fix: FAIL — `Assert.NotEmpty(written)` fails because everything landed in the working directory.

- [ ] **Step 3: Fix the separator**

In `CUE4Parse-Conversion/ExportSession.cs`, replace the body of `ResolveOutputPath`:

```csharp
    internal string ResolveOutputPath(string savePath, string ext, string? nameSuffix = null)
    {
        var fullPath = Path.Combine(BaseDirectory.FullName, savePath) + nameSuffix + '.' + ext.ToLower();
        var dir = Path.GetDirectoryName(fullPath) ?? throw new InvalidOperationException($"Cannot determine directory for path: {fullPath}");
        Directory.CreateDirectory(dir);

        // savePath always uses '/', BaseDirectory uses the platform separator, so
        // Path.Combine yields a mixed string on Windows. Normalise to the platform
        // separator rather than hard-coding '\\': on Linux '\' is a legal file name
        // character, and the hard-coded version silently produced one giant file name.
        return fullPath.Replace('/', Path.DirectorySeparatorChar);
    }
```

- [ ] **Step 4: Run the test again**

Run: `dotnet run --project CUE4Parse.Cli.Tests/CUE4Parse.Cli.Tests.csproj -- --filter-method "*ExportWritesFilesUsingThePlatformSeparator*"`
Expected: PASS. Push the branch and confirm the `cli-tests-linux` job is green — that is the real verification.

- [ ] **Step 5: Commit**

```bash
git add CUE4Parse-Conversion/ExportSession.cs CUE4Parse.Cli.Tests/ExportPathTests.cs CUE4Parse.Cli.Tests/ExportFlagDefaults.cs
git commit -m "fix: resolve export paths with the platform separator instead of a hard-coded backslash"
```

---

# Phase 1 — A `.glb` that opens in Blender with textures

## Task 3: `MeshExportContext`

**Files:**
- Create: `CUE4Parse-Conversion/Formats/Meshes/MeshExportContext.cs`
- Modify: `CUE4Parse-Conversion/Formats/Meshes/IMeshExportFormat.cs`, `ActorXMeshFormat.cs`, `GltfMeshFormat.cs`, `UEFormatMeshFormat.cs`, `UsdMeshFormat.cs`
- Modify: `CUE4Parse-Conversion/Exporters/MeshExporter.cs`, `SkeletalMeshExporter.cs`, `StaticMeshExporter.cs`, `SkeletonExporter.cs`, `SplineMeshExporter.cs`, `LandscapeMeshExporter.cs`
- Test: `CUE4Parse.Cli.Tests/MeshExportContextTests.cs`

**Interfaces:**
- Consumes: `ExporterBase.SaveDirectory`, `ExporterBase.ObjectName`, `ExporterBase.ObjectPath`, `ExportSession.Options`.
- Produces:
  - `public readonly record struct MeshExportContext(string ObjectName, string ObjectPath, string SaveDirectory, ExportOptions Options, IReadOnlyDictionary<string, string>? MaterialPaths = null)`
  - `IMeshExportFormat.BuildSkeletalMesh(in MeshExportContext context, SkeletalMeshDto dto)`
  - `IMeshExportFormat.BuildStaticMesh(in MeshExportContext context, StaticMeshDto dto)`
  - `IMeshExportFormat.BuildSkeleton(in MeshExportContext context, SkeletonDto dto)`
  - `protected MeshExportContext MeshExporter<T>.CreateContext(IReadOnlyDictionary<string, string>? materialPaths = null)`

- [ ] **Step 1: Write the failing test**

Create `CUE4Parse.Cli.Tests/MeshExportContextTests.cs`:

```csharp
using CUE4Parse_Conversion.Formats.Meshes;
using CUE4Parse_Conversion.Options;

namespace CUE4Parse.Cli.Tests;

public class MeshExportContextTests
{
    [Fact]
    public void ContextCarriesSaveDirectoryAndOptionsTogether()
    {
        var options = new ExportOptions(meshFormat: EMeshFormat.Gltf2);
        var context = new MeshExportContext(
            "MESH_X",
            "Game/Chars/MESH_X.MESH_X",
            "Game/Chars",
            options);

        Assert.Equal("MESH_X", context.ObjectName);
        Assert.Equal("Game/Chars", context.SaveDirectory);
        Assert.Same(options, context.Options);
        Assert.Null(context.MaterialPaths);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet run --project CUE4Parse.Cli.Tests/CUE4Parse.Cli.Tests.csproj -- --filter-method "*ContextCarriesSaveDirectory*"`
Expected: build error — `The type or namespace name 'MeshExportContext' could not be found`.

- [ ] **Step 3: Add the record**

Create `CUE4Parse-Conversion/Formats/Meshes/MeshExportContext.cs`:

```csharp
using System.Collections.Generic;
using CUE4Parse_Conversion.Options;

namespace CUE4Parse_Conversion.Formats.Meshes;

/// <summary>
/// Everything a mesh writer needs about the export in progress.
/// <para>
/// <see cref="SaveDirectory"/> is the exporter's own directory. It cannot be derived
/// from <see cref="ObjectPath"/> without duplicating <c>ExporterBase</c>'s path rules,
/// and two sources of truth for output paths is exactly the failure the glTF material
/// URIs must not have.
/// </para>
/// </summary>
public readonly record struct MeshExportContext(
    string ObjectName,
    string ObjectPath,
    string SaveDirectory,
    ExportOptions Options,
    IReadOnlyDictionary<string, string>? MaterialPaths = null);
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet run --project CUE4Parse.Cli.Tests/CUE4Parse.Cli.Tests.csproj -- --filter-method "*ContextCarriesSaveDirectory*"`
Expected: PASS.

- [ ] **Step 5: Change the interface**

Replace the body of `CUE4Parse-Conversion/Formats/Meshes/IMeshExportFormat.cs`:

```csharp
using System.Collections.Generic;
using CUE4Parse_Conversion.Dto;

namespace CUE4Parse_Conversion.Formats.Meshes;

public interface IMeshExportFormat : IExportFormat
{
    public IReadOnlyList<ExportFile> BuildSkeletalMesh(in MeshExportContext context, SkeletalMeshDto dto);

    public IReadOnlyList<ExportFile> BuildStaticMesh(in MeshExportContext context, StaticMeshDto dto);

    public IReadOnlyList<ExportFile> BuildSkeleton(in MeshExportContext context, SkeletonDto dto);
}
```

- [ ] **Step 6: Update the four implementors mechanically**

In each of `ActorXMeshFormat.cs`, `GltfMeshFormat.cs`, `UEFormatMeshFormat.cs`, `UsdMeshFormat.cs`, change every method signature from
`(string objectName, string objectPath, ExportOptions options, XDto dto, IReadOnlyDictionary<string, string>? materialPaths = null)`
to `(in MeshExportContext context, XDto dto)` and, at the top of each body, add the locals the existing code already uses:

```csharp
        var objectName = context.ObjectName;
        var objectPath = context.ObjectPath;
        var options = context.Options;
        var materialPaths = context.MaterialPaths;
```

Delete any of those four locals the method does not reference, so no unused-variable warnings appear. Do not change any other logic in this step.

- [ ] **Step 7: Add the context factory and update the six call sites**

In `CUE4Parse-Conversion/Exporters/MeshExporter.cs`, add inside `MeshExporter<T>`:

```csharp
    protected MeshExportContext CreateContext(IReadOnlyDictionary<string, string>? materialPaths = null)
        => new(ObjectName, ObjectPath, SaveDirectory, Session.Options, materialPaths);
```

Then replace each call site:

| File | Old | New |
|---|---|---|
| `SkeletalMeshExporter.cs:37` | `format.BuildSkeletalMesh(ObjectName, ObjectPath, Session.Options, dto, materialPaths)` | `format.BuildSkeletalMesh(CreateContext(materialPaths), dto)` |
| `StaticMeshExporter.cs:20` | `format.BuildStaticMesh(ObjectName, ObjectPath, Session.Options, dto, materialPaths)` | `format.BuildStaticMesh(CreateContext(materialPaths), dto)` |
| `SkeletonExporter.cs:13` | `format.BuildSkeleton(ObjectName, ObjectPath, Session.Options, dto)` | `format.BuildSkeleton(CreateContext(), dto)` |
| `SplineMeshExporter.cs:20` | `format.BuildStaticMesh(ObjectName, ObjectPath, Session.Options, dto, materialPaths)` | `format.BuildStaticMesh(CreateContext(materialPaths), dto)` |
| `LandscapeMeshExporter.cs:48` | `format.BuildStaticMesh(ObjectName, ObjectPath, Session.Options, dto, materialPaths)` | `format.BuildStaticMesh(CreateContext(materialPaths), dto)` |
| `LandscapeMeshExporter.cs:63` | `format.BuildStaticMesh(ObjectName, ObjectPath, Session.Options, dto, materialPaths)` | `format.BuildStaticMesh(CreateContext(materialPaths), dto)` |

- [ ] **Step 8: Build the solution**

Run: `dotnet build -c Release`
Expected: build succeeds with no errors. If `CUE4Parse.Example` references `IMeshExportFormat`, fix it the same way.

- [ ] **Step 9: Run the full CLI test project**

Run: `dotnet test CUE4Parse.Cli.Tests/CUE4Parse.Cli.Tests.csproj -c Release`
Expected: all tests pass — this refactor changes no behaviour.

- [ ] **Step 10: Commit**

```bash
git add CUE4Parse-Conversion CUE4Parse.Cli.Tests/MeshExportContextTests.cs
git commit -m "refactor: pass a MeshExportContext to mesh writers instead of four loose parameters"
```

---

## Task 4: `TextureFileNamer` and the glTF texture-format constraints

**Files:**
- Create: `CUE4Parse-Conversion/Exporters/TextureFileNamer.cs`
- Modify: `CUE4Parse-Conversion/Exporters/TextureExporter.cs`
- Modify: `CUE4Parse-Conversion/Options/ExportOptions.cs`
- Modify: `CUE4Parse.Cli/Services/ExportOptionsMapper.cs`
- Test: `CUE4Parse.Cli.Tests/TextureFileNamerTests.cs`

**Interfaces:**
- Consumes: `UTexture.PlatformData.PixelFormat` (string), `UTexture.GetFirstMipIndex()`, `ExportOptions`.
- Produces:
  - `public static class TextureFileNamer` with
    `public static string Extension(UTexture texture, ExportOptions options)`,
    `public static string? Suffix(UTexture texture, ExportOptions options, int layer = 0)`,
    `public static (string Extension, string? Suffix) Name(UTexture texture, ExportOptions options, int layer = 0)`.
  - `ExportOptions.ExportHdrTexturesAsHdr` is forced `false` when `MeshFormat == EMeshFormat.Gltf2`.

- [ ] **Step 1: Write the failing tests**

Create `CUE4Parse.Cli.Tests/TextureFileNamerTests.cs`:

```csharp
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse_Conversion.Exporters;
using CUE4Parse_Conversion.Options;
using CUE4Parse_Conversion.Textures;

namespace CUE4Parse.Cli.Tests;

public class TextureFileNamerTests
{
    private static UTexture LoadFixtureTexture(string name) =>
        FixtureAssets.LoadExport<UTexture>($"CUE4ParseFixtures/Content/Fixtures/Textures/{name}.uasset", name);

    [Theory]
    [InlineData(ETextureFormat.Png, "png")]
    [InlineData(ETextureFormat.Jpeg, "jpg")]
    [InlineData(ETextureFormat.Tga, "tga")]
    [InlineData(ETextureFormat.Webp, "webp")]
    public void ExtensionFollowsTheRequestedFormatForNonHdrTextures(ETextureFormat format, string expected)
    {
        var texture = LoadFixtureTexture("T_BC3");
        var options = new ExportOptions(meshFormat: EMeshFormat.UEFormat, textureFormat: format);

        Assert.Equal(expected, TextureFileNamer.Extension(texture, options));
    }

    [Fact]
    public void AllMipsAddsAMipSuffixAndTheDefaultBranchAddsNone()
    {
        var texture = LoadFixtureTexture("T_BC3");

        var single = new ExportOptions(meshFormat: EMeshFormat.UEFormat);
        Assert.Null(TextureFileNamer.Suffix(texture, single));

        var all = new ExportOptions(meshFormat: EMeshFormat.UEFormat, exportAllTextureMips: true);
        Assert.Equal($"_MIP{texture.GetFirstMipIndex()}", TextureFileNamer.Suffix(texture, all));
    }

    [Fact]
    public void Gltf2ForcesHdrTexturesDownToTheRequestedRasterFormat()
    {
        var options = new ExportOptions(meshFormat: EMeshFormat.Gltf2, exportHdrTexturesAsHdr: true);
        Assert.False(options.ExportHdrTexturesAsHdr);
    }

    /// <summary>
    /// The namer predicts what TextureExporter will actually write. If the two ever
    /// disagree, every glTF image URI points at a file that does not exist.
    /// </summary>
    [Theory]
    [InlineData(ETextureFormat.Png)]
    [InlineData(ETextureFormat.Jpeg)]
    [InlineData(ETextureFormat.Tga)]
    [InlineData(ETextureFormat.Webp)]
    public void NamerAgreesWithTheEncoderOnEveryFixtureTexture(ETextureFormat format)
    {
        foreach (var hdr in new[] { true, false })
        foreach (var name in FixtureAssets.TextureNames)
        {
            var texture = LoadFixtureTexture(name);
            var options = new ExportOptions(meshFormat: EMeshFormat.UEFormat, textureFormat: format, exportHdrTexturesAsHdr: hdr);

            var decoded = texture.DecodeMip(texture.GetFirstMipIndex(), options.TexturePlatform);
            if (decoded is null) continue;

            decoded.Encode(options, out var actual);
            Assert.Equal(actual, TextureFileNamer.Extension(texture, options));
        }
    }
}
```

Create `CUE4Parse.Cli.Tests/FixtureAssets.cs`:

```csharp
using CUE4Parse.Cli.Services;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports;

namespace CUE4Parse.Cli.Tests;

/// <summary>Loads a single export out of the fixture archives, mounting once per process.</summary>
public static class FixtureAssets
{
    private static readonly Lazy<IFileProvider> Shared = new(() => ProviderFactory.Create(FixtureSupport.Profile()));

    /// <summary>Texture assets present in the UE5_8 fixture set. Discover the real list
    /// once with `cue4 list --glob "**/Textures/*"` and keep this array in step with it.</summary>
    public static readonly string[] TextureNames = ["T_BC3"];

    public static T LoadExport<T>(string path, string exportName) where T : UObject
    {
        var package = Shared.Value.LoadPackage(path);
        return (T)package.GetExports().First(export =>
            export.Name.Equals(exportName, StringComparison.OrdinalIgnoreCase));
    }
}
```

- [ ] **Step 2: Discover the real fixture texture names before running**

Run:

```bash
dotnet run --project CUE4Parse.Cli/CUE4Parse.Cli.csproj -- list \
  --paks CUE4Parse.Tests/Fixtures/UE5_8/LegacyPak/Unversioned/Oodle \
  --game 5.8 \
  --mappings CUE4Parse.Tests/Fixtures/UE5_8/Mappings/CUE4ParseFixtures-Oodle.usmap \
  --glob "**/Textures/**"
```

Expected: NDJSON listing the fixture textures. Replace `TextureNames` and the `LoadFixtureTexture` path prefix in the tests above with what this prints. If the textures live somewhere other than `Fixtures/Textures/`, correct the path template in `LoadFixtureTexture` too.

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet run --project CUE4Parse.Cli.Tests/CUE4Parse.Cli.Tests.csproj -- --filter-method "*TextureFileNamer*"`
Expected: build error — `TextureFileNamer` does not exist.

- [ ] **Step 4: Write the namer**

Create `CUE4Parse-Conversion/Exporters/TextureFileNamer.cs`:

```csharp
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse_Conversion.Options;

namespace CUE4Parse_Conversion.Exporters;

/// <summary>
/// Predicts the file name <see cref="TextureExporter"/> will write, without decoding
/// the texture.
/// <para>
/// Three rules stack: HDR sources ignore <see cref="ExportOptions.TextureFormat"/> and
/// come out as <c>.hdr</c>; <c>--all-mips</c> appends <c>_MIP{n}</c> — and in that mode
/// the unsuffixed file does not exist at all; <see cref="UTexture2DArray"/> appends
/// <c>_LAYER{i}</c>. A caller that hard-codes ".png" is wrong for three separate reasons.
/// </para>
/// <para>
/// The encoder remains authoritative for the bytes it writes; this type only predicts.
/// <c>NamerAgreesWithTheEncoderOnEveryFixtureTexture</c> is what keeps the prediction
/// honest — if it ever fails, fix this file, do not relax the test.
/// </para>
/// </summary>
public static class TextureFileNamer
{
    public static (string Extension, string? Suffix) Name(UTexture texture, ExportOptions options, int layer = 0)
        => (Extension(texture, options), Suffix(texture, options, layer));

    public static string Extension(UTexture texture, ExportOptions options)
    {
        if (options.ExportHdrTexturesAsHdr && IsHdrSource(texture))
            return "hdr";

        return options.TextureFormat switch
        {
            ETextureFormat.Png => "png",
            ETextureFormat.Jpeg => "jpg",
            ETextureFormat.Webp => "webp",
            ETextureFormat.Tga => "tga",
            _ => throw new NotSupportedException("Unsupported texture format: " + options.TextureFormat),
        };
    }

    public static string? Suffix(UTexture texture, ExportOptions options, int layer = 0)
    {
        var mip = options.ExportAllTextureMips ? $"_MIP{texture.GetFirstMipIndex()}" : null;
        return texture is UTexture2DArray ? $"{mip}_LAYER{layer}" : mip;
    }

    // FTexturePlatformData.PixelFormat is the cooked format's *name*, e.g. "PF_BC6H".
    private static bool IsHdrSource(UTexture texture)
        => Enum.TryParse<EPixelFormat>(texture.PlatformData.PixelFormat, out var format)
           && PixelFormatUtils.IsHDR(format);
}
```

- [ ] **Step 5: Force HDR off for glTF and reject formats glTF cannot carry**

In `CUE4Parse-Conversion/Options/ExportOptions.cs`, change the `ExportHdrTexturesAsHdr` field initialiser:

```csharp
    // glTF 2.0 core accepts only image/png and image/jpeg. Radiance HDR has no
    // extension at all, so an .hdr URI would produce a file no glTF reader can load.
    // Unlike --texture-format, the user did not ask for this, and the conflict only
    // surfaces per texture — so downgrade rather than fail the whole command.
    public readonly bool ExportHdrTexturesAsHdr = meshFormat != EMeshFormat.Gltf2 && exportHdrTexturesAsHdr;
```

In `CUE4Parse.Cli/Services/ExportOptionsMapper.cs`, add the usage check at the top of `Map`:

```csharp
    public static ExportOptions Map(ExportFlags flags)
    {
        var meshFormat = Pick(flags.MeshFormat, "--mesh-format", MeshFormats);
        var textureFormat = Pick(flags.TextureFormat, "--texture-format", TextureFormats);

        // glTF 2.0 core carries only PNG and JPEG. TGA and WebP have no core support
        // (WebP needs EXT_texture_webp, TGA nothing at all). The user typed the flag,
        // so quietly handing them something else would be dishonest.
        if (meshFormat == EMeshFormat.Gltf2 && textureFormat is ETextureFormat.Tga or ETextureFormat.Webp)
        {
            throw new CliException(
                ExitCode.Usage, "BAD_OPTION",
                $"--texture-format {flags.TextureFormat} cannot be used with --mesh-format gltf2. " +
                "glTF 2.0 accepts only png and jpeg.");
        }

        return new ExportOptions(
            meshFormat: meshFormat,
            naniteMeshFormat: Pick(flags.Nanite, "--nanite", NaniteFormats),
            meshQuality: Pick(flags.MeshQuality, "--mesh-quality", MeshQualities),
            textureFormat: textureFormat,
            texturePlatform: Pick(flags.TexturePlatform, "--texture-platform", TexturePlatforms),
            textureQuality: flags.TextureQuality,
            exportAllTextureMips: flags.AllMips,
            materialDepth: Pick(flags.MaterialDepth, "--material-depth", MaterialDepths),
            exportMaterials: !flags.NoMaterials,
            socketFormat: Pick(flags.SocketFormat, "--socket-format", SocketFormats));
    }
```

- [ ] **Step 6: Make `TextureExporter` use the namer for suffixes**

In `CUE4Parse-Conversion/Exporters/TextureExporter.cs`, replace the tail of `AddMip`:

```csharp
            for (var i = 0; i < decoded.Length; i++)
            {
                if (decoded[i] is not { } slice) continue;

                var data = slice.Encode(Session.Options, out var ext);
                // The suffix comes from the shared namer so the glTF binder and this
                // writer cannot drift apart. `index` is passed explicitly because
                // all-mips walks every mip, not just the first.
                var suffix = all
                    ? (texture is UTexture2DArray ? $"_MIP{index}_LAYER{i}" : $"_MIP{index}")
                    : TextureFileNamer.Suffix(texture, Session.Options, i);
                files.Add(new ExportFile(ext, data, suffix));
            }
```

- [ ] **Step 7: Add a usage-error test**

Append to `CUE4Parse.Cli.Tests/ExportOptionsMapperTests.cs`:

```csharp
    [Theory]
    [InlineData("tga")]
    [InlineData("webp")]
    public void Gltf2RejectsTextureFormatsGltfCannotCarry(string textureFormat)
    {
        var flags = ExportFlagDefaults.Gltf2() with { TextureFormat = textureFormat };

        var ex = Assert.Throws<CliException>(() => ExportOptionsMapper.Map(flags));

        Assert.Equal(ExitCode.Usage, ex.ExitCode);
        Assert.Equal("BAD_OPTION", ex.ErrorCode);
    }
```

Add `using CUE4Parse.Cli.Output;` to that file if it is not already imported.

- [ ] **Step 8: Run the tests**

Run: `dotnet run --project CUE4Parse.Cli.Tests/CUE4Parse.Cli.Tests.csproj -- --filter-method "*TextureFileNamer*"`
Run: `dotnet run --project CUE4Parse.Cli.Tests/CUE4Parse.Cli.Tests.csproj -- --filter-method "*Gltf2RejectsTextureFormats*"`
Expected: PASS for both.

- [ ] **Step 9: Commit**

```bash
git add CUE4Parse-Conversion CUE4Parse.Cli CUE4Parse.Cli.Tests
git commit -m "feat: single source of truth for texture file names, and glTF texture-format limits"
```

---

## Task 5: `GltfMaterialBinder`

**Files:**
- Create: `CUE4Parse-Conversion/Writers/Gltf/GltfMaterialBinder.cs`
- Modify: `CUE4Parse-Conversion/Exporters/ExporterBase.cs` (add a `nameSuffix`-aware `Resolve` overload)
- Test: `CUE4Parse.Cli.Tests/GltfMaterialBinderTests.cs`

**Interfaces:**
- Consumes: `CMaterialParams2`, `ExporterBase.Resolve`, `TextureFileNamer`, `OrmTextureExporter.OrmSuffix` (a `const string` — Task 7 adds the exporter; declare the constant here in Task 7's file and reference it, or temporarily inline `"_ORM"` and switch to the constant in Task 7. Prefer creating the constant now inside `GltfMaterialBinder` as `public const string OrmSuffix = "_ORM";` and having Task 7's exporter consume it — one owner, no forward reference).
- Produces:
  - `public static MaterialBuilder GltfMaterialBinder.Bind(UMaterialInterface material, CMaterialParams2 parameters, string slotName, ExportOptions options, string meshSaveDirectory)`
  - `public const string GltfMaterialBinder.OrmSuffix = "_ORM"`

> **The caller supplies `parameters`; the binder does not call `GetParams` itself.**
> This is the change that makes the binder testable at all. The fixture material
> `M_Fixture` has one texture parameter whose name matches no classification table and no
> regex fallback, and there is no normal map, no SpecularMasks source, no emissive and no
> masked material anywhere in the fixture set — nor any way to add one (spec §9.1). A
> binder that resolved its own parameters could therefore only ever be tested on a single
> accidental base-color path. Taking `CMaterialParams2` lets the tests below fabricate
> every channel and every `EBlendMode` with no asset at all.
>
> `material` is still needed alongside it: `CMaterialParams2` carries `BlendMode` but
> **not** `TwoSided` and **not** `OpacityMaskClipValue`, which §5.3 requires.
>
> Spec §4.4's guarantee gets stronger, not weaker — binder and `MaterialExporter` no longer
> merely make matching `GetParams` calls, they share one object.
  - `internal static ImageBuilder GltfMaterialBinder.CreateImage(string uri)`
  - `internal static string ExporterBase.Resolve(UObject obj, string fromDirectory, string extension, string? nameSuffix)`

- [ ] **Step 1: Write the failing tests**

Create `CUE4Parse.Cli.Tests/GltfMaterialBinderTests.cs`:

```csharp
using System.Numerics;
using CUE4Parse.UE4.Assets.Exports.Material;
using CUE4Parse_Conversion.Options;
using CUE4Parse_Conversion.Writers.Gltf;
using SharpGLTF.Materials;
using SharpGLTF.Memory;

namespace CUE4Parse.Cli.Tests;

public class GltfMaterialBinderTests
{
    private static UMaterialInterface Fixture(string name) =>
        FixtureAssets.LoadExport<UMaterialInterface>(
            $"CUE4ParseFixtures/Content/Fixtures/Materials/{name}.uasset", name);

    /// <summary>
    /// Binds a real fixture material. Only useful for the base-color path: the fixture set
    /// has no normal map, no SpecularMasks source, no emissive and no masked material, and
    /// cannot be extended (spec §9.1). Everything else uses <see cref="BindSynthetic"/>.
    /// </summary>
    private static MaterialBuilder Bind(string name, string meshSaveDirectory = "CUE4ParseFixtures/Content/Fixtures/Meshes")
    {
        var material = Fixture(name);
        var options = new ExportOptions(meshFormat: EMeshFormat.Gltf2);
        var parameters = new CMaterialParams2();
        material.GetParams(parameters, options.MaterialDepth);
        return GltfMaterialBinder.Bind(material, parameters, "Primary", options, meshSaveDirectory);
    }

    /// <summary>
    /// Binds a fabricated parameter set. This is how every channel, every <c>EBlendMode</c>
    /// and the ORM swizzle get covered without an asset that does not exist.
    /// </summary>
    private static MaterialBuilder BindSynthetic(
        Action<CMaterialParams2> configure,
        UMaterialInterface? material = null,
        string meshSaveDirectory = "CUE4ParseFixtures/Content/Fixtures/Meshes")
    {
        var options = new ExportOptions(meshFormat: EMeshFormat.Gltf2);
        var parameters = new CMaterialParams2();
        configure(parameters);
        return GltfMaterialBinder.Bind(material ?? Fixture("M_Fixture"), parameters, "Primary", options, meshSaveDirectory);
    }

    [Fact]
    public void BoundMaterialKeepsTheSlotNameAndUsesTheMetallicRoughnessShader()
    {
        var material = Bind("M_Fixture");

        Assert.Equal("Primary", material.Name);
        Assert.Equal("PBRMetallicRoughness", material.ShaderStyle);
    }

    [Fact]
    public void BaseColorPointsAtAnExternalRelativeUriNotAnEmbeddedImage()
    {
        var material = Bind("M_Fixture");

        var image = material.GetChannel(KnownChannel.BaseColor)?.Texture?.PrimaryImage;
        Assert.NotNull(image);
        Assert.StartsWith("../", image!.AlternateWriteFileName);
        Assert.EndsWith(".png", image.AlternateWriteFileName);
    }

    [Fact]
    public void MetallicRoughnessPointsAtTheRepackedOrmSibling()
    {
        var material = Bind("M_Fixture");

        var image = material.GetChannel(KnownChannel.MetallicRoughness)?.Texture?.PrimaryImage;
        Assert.NotNull(image);
        Assert.EndsWith("_ORM.png", image!.AlternateWriteFileName);
    }

    [Fact]
    public void OcclusionIsNeverWrittenBecauseTheSpecularRedChannelIsNotAmbientOcclusion()
    {
        var material = Bind("M_Fixture");

        Assert.Null(material.GetChannel(KnownChannel.Occlusion)?.Texture?.PrimaryImage);
    }

    [Fact]
    public void UrisArePercentEncodedAndAlwaysUseForwardSlashes()
    {
        Assert.Equal("../Common/L21%20Body%20Color.png",
            GltfMaterialBinder.EncodeUri("../Common/L21 Body Color.png"));
        Assert.Equal("./T_Foo.png", GltfMaterialBinder.EncodeUri("./T_Foo.png"));
    }

    [Theory]
    [InlineData(EBlendMode.BLEND_Opaque, "OPAQUE")]
    [InlineData(EBlendMode.BLEND_Masked, "MASK")]
    [InlineData(EBlendMode.BLEND_Translucent, "BLEND")]
    [InlineData(EBlendMode.BLEND_Additive, "BLEND")]
    [InlineData(EBlendMode.BLEND_Modulate, "BLEND")]
    public void BlendModeMapsToTheGltfAlphaMode(EBlendMode blendMode, string expected)
        => Assert.Equal(expected, GltfMaterialBinder.ToAlphaMode(blendMode).ToString());

    [Fact]
    public void DistinctUrisProduceDistinctPlaceholderImagesSoSharpGltfDoesNotMergeThem()
    {
        // Compare MemoryImage content, not ImageBuilder.AreEqualByContent: the latter
        // returns false even for byte-identical images, while ToGltf2's deduplication
        // keys on the image bytes.
        var a = GltfMaterialBinder.CreateImage("../C/T_A.png");
        var b = GltfMaterialBinder.CreateImage("../C/T_B.png");

        Assert.False(MemoryImage.AreEqual(a.Content, b.Content));
        Assert.True(MemoryImage.AreEqual(a.Content, GltfMaterialBinder.CreateImage("../C/T_A.png").Content));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet run --project CUE4Parse.Cli.Tests/CUE4Parse.Cli.Tests.csproj -- --filter-method "*GltfMaterialBinder*"`
Expected: build error — `GltfMaterialBinder` does not exist.

- [ ] **Step 3: Add the suffix-aware `Resolve` overload**

In `CUE4Parse-Conversion/Exporters/ExporterBase.cs`, replace the existing internal `Resolve` with:

```csharp
    protected string Resolve(UObject obj, string extension) => Resolve(obj, SaveDirectory, extension);

    internal static string Resolve(UObject obj, string fromDirectory, string extension)
        => Resolve(obj, fromDirectory, extension, null);

    /// <summary>
    /// Relative reference to another object's output file. <paramref name="nameSuffix"/>
    /// addresses siblings written by an exporter with a suffixed name, e.g. the
    /// repacked <c>_ORM</c> texture.
    /// </summary>
    internal static string Resolve(UObject obj, string fromDirectory, string extension, string? nameSuffix)
    {
        var packagePath = BuildPackagePath(obj);

        // replicate SavePath
        if (!packagePath.SubstringAfterLast('/').Equals(obj.Name, StringComparison.OrdinalIgnoreCase))
            packagePath += '/' + obj.Name;

        return MakeRelativeRef(packagePath.TrimStart('/') + nameSuffix, fromDirectory, extension);
    }
```

- [ ] **Step 4: Write the binder**

Create `CUE4Parse-Conversion/Writers/Gltf/GltfMaterialBinder.cs`:

```csharp
using System;
using System.Linq;
using System.Numerics;
using System.Text;
using CUE4Parse.UE4.Assets.Exports.Material;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse_Conversion.Exporters;
using CUE4Parse_Conversion.Options;
using SharpGLTF.Materials;
using SharpGLTF.Memory;
using SkiaSharp;

namespace CUE4Parse_Conversion.Writers.Gltf;

/// <summary>
/// Turns a UE material into a glTF <see cref="MaterialBuilder"/> whose images are
/// external, relative URIs.
/// <para>
/// Pure by design: it takes no <c>ExportSession</c>, touches no disk, and never waits
/// for a texture to be written. It classifies textures with exactly the call
/// <c>MaterialExporter</c> makes — <c>material.GetParams(parameters, options.MaterialDepth)</c>
/// — so the set it references and the set the session writes are the same set by
/// construction, not by a cross-check that could rot.
/// </para>
/// <para>
/// Anything that cannot be classified is left blank. A blank channel is a material a
/// human can fix in Blender; an invented channel is data that is simply wrong.
/// </para>
/// </summary>
public static class GltfMaterialBinder
{
    /// <summary>Name suffix of the repacked ORM sibling written by <c>OrmTextureExporter</c>.</summary>
    public const string OrmSuffix = "_ORM";

    private static readonly string[] DiffuseNames = [.. CMaterialParams2.Diffuse[0], CMaterialParams2.FallbackDiffuse];
    private static readonly string[] NormalNames = [.. CMaterialParams2.Normals[0], CMaterialParams2.FallbackNormals];
    private static readonly string[] SpecularNames = [.. CMaterialParams2.SpecularMasks[0], CMaterialParams2.FallbackSpecularMasks];
    private static readonly string[] EmissiveNames = [.. CMaterialParams2.Emissive[0], CMaterialParams2.FallbackEmissive];

    /// <param name="parameters">
    /// Resolved by the caller, not here, so tests can fabricate parameter sets the fixture
    /// assets cannot provide (spec §5.1, §9.1). The caller must have resolved them at
    /// <c>options.MaterialDepth</c> — that is what keeps the referenced textures and the
    /// written textures the same set (spec §4.4).
    /// </param>
    public static MaterialBuilder Bind(
        UMaterialInterface material, CMaterialParams2 parameters,
        string slotName, ExportOptions options, string meshSaveDirectory)
    {
        var builder = new MaterialBuilder(slotName).WithMetallicRoughnessShader();

        BindBaseColor(builder, parameters, options, meshSaveDirectory);
        BindMetallicRoughness(builder, parameters, options, meshSaveDirectory);
        BindNormal(builder, parameters, options, meshSaveDirectory);
        BindEmissive(builder, parameters, options, meshSaveDirectory);
        BindAlphaAndSides(builder, material, parameters);

        // occlusionTexture is deliberately never written: the R channel of a UE
        // SpecularMasks/SRM pack is specular, not ambient occlusion.

        return builder;
    }

    private static void BindBaseColor(MaterialBuilder builder, CMaterialParams2 parameters, ExportOptions options, string from)
    {
        Vector4? tint = parameters.TryGetLinearColor(out var color, CMaterialParams2.DiffuseColors[0])
            ? new Vector4(color.R, color.G, color.B, color.A)
            : null;

        if (TryImage(parameters, DiffuseNames, options, from, null, out var image))
        {
            builder.WithBaseColor(image, tint);
        }
        else if (tint.HasValue)
        {
            builder.WithBaseColor(tint.Value);
        }
    }

    private static void BindMetallicRoughness(MaterialBuilder builder, CMaterialParams2 parameters, ExportOptions options, string from)
    {
        // glTF fixes G = roughness, B = metallic; UE's SRM pack is G = metallic,
        // B = roughness. glTF 2.0 has no channel swizzle, so the URI points at the
        // repacked sibling OrmTextureExporter writes, always as PNG.
        if (TryImage(parameters, SpecularNames, options, from, OrmSuffix, out var image, forcePng: true))
        {
            builder.WithMetallicRoughness(image, null, null);
        }
    }

    private static void BindNormal(MaterialBuilder builder, CMaterialParams2 parameters, ExportOptions options, string from)
    {
        if (TryImage(parameters, NormalNames, options, from, null, out var image))
        {
            builder.WithNormal(image, 1f);
        }
    }

    private static void BindEmissive(MaterialBuilder builder, CMaterialParams2 parameters, ExportOptions options, string from)
    {
        Vector3? factor = parameters.TryGetLinearColor(out var color, CMaterialParams2.EmissiveColors[0])
            ? new Vector3(color.R, color.G, color.B)
            : null;

        if (TryImage(parameters, EmissiveNames, options, from, null, out var image))
        {
            builder.WithEmissive(image, factor, 1f);
        }
        else if (factor.HasValue)
        {
            builder.WithEmissive(factor.Value, 1f);
        }
    }

    private static void BindAlphaAndSides(MaterialBuilder builder, UMaterialInterface material, CMaterialParams2 parameters)
    {
        var mode = ToAlphaMode(parameters.BlendMode);
        var root = RootMaterial(material);

        builder.WithAlpha(mode, mode == SharpGLTF.Materials.AlphaMode.MASK ? OpacityMaskClipValue(material, root) : 0.5f);
        builder.WithDoubleSide(root?.TwoSided == true);
    }

    public static SharpGLTF.Materials.AlphaMode ToAlphaMode(EBlendMode blendMode) => blendMode switch
    {
        EBlendMode.BLEND_Opaque => SharpGLTF.Materials.AlphaMode.OPAQUE,
        EBlendMode.BLEND_Masked => SharpGLTF.Materials.AlphaMode.MASK,
        _ => SharpGLTF.Materials.AlphaMode.BLEND,
    };

    /// <summary>
    /// The nearest explicit override wins; otherwise the root UMaterial's value;
    /// otherwise UE's own default of 0.333.
    /// </summary>
    private static float OpacityMaskClipValue(UMaterialInterface material, UMaterial? root)
    {
        for (var current = material; current is not null; current = (current as UMaterialInstance)?.Parent as UMaterialInterface)
        {
            if (current is UMaterialInstance { BasePropertyOverrides.OpacityMaskClipValue: > 0f } instance)
                return instance.BasePropertyOverrides!.OpacityMaskClipValue;

            if (current is UMaterial concrete)
                return concrete.OpacityMaskClipValue;
        }

        return root?.OpacityMaskClipValue ?? 0.333f;
    }

    /// <summary>Walks the Parent chain to the concrete UMaterial. Bounded so a cyclic
    /// or self-referencing chain cannot hang an export.</summary>
    private static UMaterial? RootMaterial(UMaterialInterface material)
    {
        var current = material;
        for (var depth = 0; current is not null && depth < 16; depth++)
        {
            if (current is UMaterial concrete) return concrete;
            current = (current as UMaterialInstance)?.Parent as UMaterialInterface;
        }

        return null;
    }

    private static bool TryImage(
        CMaterialParams2 parameters, string[] names, ExportOptions options,
        string from, string? nameSuffix, out ImageBuilder image, bool forcePng = false)
    {
        image = null!;
        if (!parameters.TryGetTexture2d(out var texture, names)) return false;

        // A cube map's panorama has no UV mapping that matches the mesh, so pointing a
        // channel at it would be an invention rather than a conversion.
        if (texture is UTextureCube)
        {
            Log.Debug("Skipping cube map {Name} for glTF material channel", texture.Name);
            return false;
        }

        var extension = forcePng ? "png" : TextureFileNamer.Extension(texture, options);
        var suffix = nameSuffix ?? TextureFileNamer.Suffix(texture, options);
        var uri = EncodeUri(ExporterBase.Resolve(texture, from, extension, suffix));

        image = CreateImage(uri);
        return true;
    }

    /// <summary>RFC 3986 encoding, per path segment, always with '/' separators.</summary>
    public static string EncodeUri(string relativePath) => string.Join('/', relativePath
        .Split('/')
        .Select(segment => segment is "." or ".." or "" ? segment : Uri.EscapeDataString(segment)));

    /// <summary>
    /// A placeholder image whose bytes exist only to give SharpGLTF a per-URI identity.
    /// <para>
    /// The bytes are never written: <c>WriteSettings.ImageWriteCallback</c> returns the
    /// URI and no image data reaches the GLB. But <c>SceneBuilder.ToGltf2()</c>
    /// deduplicates images <b>by content</b>, so a shared placeholder would merge every
    /// texture in the model into one entry with one wrong URI. Encoding the URI's own
    /// bytes into the pixels makes identity exact and deterministic: same URI, same
    /// image; different URI, different image.
    /// </para>
    /// </summary>
    internal static ImageBuilder CreateImage(string uri)
    {
        var bytes = Encoding.ASCII.GetBytes(uri);
        using var bitmap = new SKBitmap(Math.Max(bytes.Length, 1), 1, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        for (var i = 0; i < bytes.Length; i++)
        {
            bitmap.SetPixel(i, 0, new SKColor(bytes[i], 0, 0, 255));
        }

        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        var image = ImageBuilder.From(new MemoryImage(data.ToArray()), uri);
        image.AlternateWriteFileName = uri;
        return image;
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet run --project CUE4Parse.Cli.Tests/CUE4Parse.Cli.Tests.csproj -- --filter-method "*GltfMaterialBinder*"`
Expected: PASS. If `MetallicRoughnessPointsAtTheRepackedOrmSibling` or `BaseColorPointsAtAnExternalRelativeUri` fails because `M_Fixture` has no such texture, print what the fixture actually classifies with
`dotnet run --project CUE4Parse.Cli/CUE4Parse.Cli.csproj -- dump CUE4ParseFixtures/Content/Fixtures/Materials/M_Fixture.uasset --indent` (plus the fixture `--paks/--game/--mappings` flags from Task 4 Step 2) and adjust the asserted channel to one the fixture genuinely has. Do not weaken the assertion to "not null or null".

- [ ] **Step 6: Commit**

```bash
git add CUE4Parse-Conversion/Writers/Gltf/GltfMaterialBinder.cs CUE4Parse-Conversion/Exporters/ExporterBase.cs CUE4Parse.Cli.Tests/GltfMaterialBinderTests.cs
git commit -m "feat: bind UE material parameters to glTF materials with external texture URIs"
```

---

## Task 6: Wire the binder into the glTF writer, and name mesh datablocks after their file

**Files:**
- Modify: `CUE4Parse-Conversion/Writers/Gltf/Gltf.cs`
- Modify: `CUE4Parse-Conversion/Formats/Meshes/GltfMeshFormat.cs`
- Test: `CUE4Parse.Cli.Tests/GltfWriterTests.cs`

**Interfaces:**
- Consumes: `MeshExportContext`, `GltfMaterialBinder.Bind`, `MeshLodDto<T>._suffix`.
- Produces:
  - `Gltf(string name, MeshLodDto<MeshVertex> lod, in MeshExportContext context)`
  - `Gltf(string name, MeshLodDto<SkinnedMeshVertex> lod, in MeshExportContext context)`
  - `.glb` files whose `meshes[0].name`, root node name and `scenes[0].name` all equal the file name without its extension.

- [ ] **Step 1: Write the failing test**

Create `CUE4Parse.Cli.Tests/GltfWriterTests.cs`:

```csharp
using CUE4Parse.Cli.Commands;
using CUE4Parse.Cli.Output;
using Newtonsoft.Json.Linq;

namespace CUE4Parse.Cli.Tests;

public class GltfWriterTests
{
    /// <summary>Reads the JSON chunk out of a binary glTF container.</summary>
    public static JObject ReadGlbJson(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var length = BitConverter.ToInt32(bytes, 12);
        return JObject.Parse(System.Text.Encoding.UTF8.GetString(bytes, 20, length));
    }

    public static async Task<DirectoryInfo> ExportAsync(string assetPath, ExportFlags? flags = null)
    {
        var output = Directory.CreateTempSubdirectory();
        var (context, _) = FixtureSupport.Context();

        var code = await ExportCommand.ExecuteAsync(context, new ExportCommandOptions(
            Paths: [assetPath],
            Criteria: new MatchCriteria(null, null, null, null),
            Output: output,
            Flags: flags ?? ExportFlagDefaults.Gltf2(),
            Parallel: 1,
            Force: false), CancellationToken.None);

        Assert.Equal((int)ExitCode.Success, code);
        return output;
    }

    [Fact]
    public async Task ExportedGlbCarriesImagesAndTexturesRatherThanBareMaterials()
    {
        var output = await ExportAsync("CUE4ParseFixtures/Content/Fixtures/Meshes/SM_Fixture.uasset");
        var glb = Directory.GetFiles(output.FullName, "*.glb", SearchOption.AllDirectories).Single();

        var json = ReadGlbJson(glb);

        Assert.NotNull(json["images"]);
        Assert.NotEmpty((JArray)json["images"]!);
        Assert.NotNull(json["textures"]);
        Assert.NotEmpty((JArray)json["textures"]!);
    }

    [Fact]
    public async Task EveryImageUriResolvesToAFileThatThisRunActuallyWrote()
    {
        var output = await ExportAsync("CUE4ParseFixtures/Content/Fixtures/Meshes/SM_Fixture.uasset");
        var glb = Directory.GetFiles(output.FullName, "*.glb", SearchOption.AllDirectories).Single();

        var json = ReadGlbJson(glb);
        var glbDirectory = Path.GetDirectoryName(glb)!;

        foreach (var uri in ((JArray)json["images"]!).Select(image => image["uri"]!.Value<string>()!))
        {
            var decoded = Uri.UnescapeDataString(uri);
            var resolved = Path.GetFullPath(Path.Combine(glbDirectory, decoded.Replace('/', Path.DirectorySeparatorChar)));
            Assert.True(File.Exists(resolved), $"glTF image URI '{uri}' does not resolve to a file: {resolved}");
        }
    }

    [Fact]
    public async Task MeshDatablockAndRootNodeAreNamedAfterTheFile()
    {
        var output = await ExportAsync("CUE4ParseFixtures/Content/Fixtures/Meshes/SM_Fixture.uasset");
        var glb = Directory.GetFiles(output.FullName, "*.glb", SearchOption.AllDirectories).Single();

        var expected = Path.GetFileNameWithoutExtension(glb);
        var json = ReadGlbJson(glb);

        Assert.Equal(expected, json["meshes"]![0]!["name"]!.Value<string>());
        Assert.Contains(((JArray)json["nodes"]!).Select(node => node["name"]?.Value<string>()), name => name == expected);
    }

    [Fact]
    public async Task NoMaterialsFallsBackToBareMaterialSlots()
    {
        var flags = ExportFlagDefaults.Gltf2() with { NoMaterials = true };
        var output = await ExportAsync("CUE4ParseFixtures/Content/Fixtures/Meshes/SM_Fixture.uasset", flags);
        var glb = Directory.GetFiles(output.FullName, "*.glb", SearchOption.AllDirectories).Single();

        var json = ReadGlbJson(glb);

        Assert.Null(json["images"]);
        Assert.NotEmpty((JArray)json["materials"]!);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet run --project CUE4Parse.Cli.Tests/CUE4Parse.Cli.Tests.csproj -- --filter-method "*GltfWriterTests*"`
Expected: `ExportedGlbCarriesImagesAndTextures` fails with "images" null; `MeshDatablockAndRootNodeAreNamedAfterTheFile` fails with `Assert.Equal() Failure: Expected: SM_Fixture, Actual: LOD0`.

- [ ] **Step 3: Rewrite the glTF writer's material path, naming and save settings**

In `CUE4Parse-Conversion/Writers/Gltf/Gltf.cs`:

Add these usings:

```csharp
using CUE4Parse.UE4.Assets.Exports.Material;
using CUE4Parse_Conversion.Formats.Meshes;
using SharpGLTF.Memory;
```

Replace both constructors:

```csharp
    private readonly MeshExportContext _context;

    public Gltf(string name, MeshLodDto<MeshVertex> lod, in MeshExportContext context)
    {
        _context = context;

        var sceneBuilder = new SceneBuilder(name);
        // The datablock name is the file name without its extension, with no
        // exceptions: a bpy script importing many .glb into one scene needs a name it
        // can derive from the file it just opened. "LOD0" collides on the second import.
        var meshBuilder = new MeshBuilder<VERTEX, VertexColorXTextureX, VertexEmpty>(name);

        ExportMeshSections(meshBuilder, lod);
        sceneBuilder.AddRigidMesh(meshBuilder, new NodeBuilder(name));

        Model = sceneBuilder.ToGltf2();
    }

    public Gltf(string name, MeshLodDto<SkinnedMeshVertex> lod, in MeshExportContext context)
    {
        if (lod.Owner is not SkeletalMeshDto mesh)
            throw new ArgumentException("LOD owner must be a SkeletalMeshDto for skeletal meshes.", nameof(lod));

        _context = context;

        var sceneBuilder = new SceneBuilder(name);
        var armatureRoot = new NodeBuilder($"{name}.ao");
        var armature = CreateGltfSkeleton(mesh.Bones, armatureRoot);

        var meshBuilder = new MeshBuilder<VERTEX, VertexColorXTextureX, VertexJoints4>(name);
        ExportMeshSections(meshBuilder, lod);
        sceneBuilder.AddSkinnedMesh(meshBuilder, Matrix4x4.CreateTranslation(0, 0, 0), armature);

        if (context.Options.ExportMorphTargets && mesh.MorphTargets is { Length: > 0 } morphTargets)
        {
            // ... unchanged morph-target block, see Task 8 for its delta fix ...
        }

        Model = sceneBuilder.ToGltf2();
    }
```

Keep the morph-target block exactly as it is for now — Task 8 fixes it. Only the `exportMorphTargets` parameter becomes `context.Options.ExportMorphTargets`.

Replace the material construction inside `ExportMeshSections`:

```csharp
        for (var i = 0; i < lod.Sections.Length; i++)
        {
            var section = lod.Sections[i];
            var slot = lod.Owner.GetMaterial(section);
            var slotName = slot?.SlotName ?? $"MaterialSlot_{i}";

            // --no-materials disables the binder implicitly: with no materials exported
            // there is nothing on disk for a URI to point at.
            var mat = _context.Options.ExportMaterials && slot?.Material?.TryLoad<UMaterialInterface>(out var material) == true
                ? GltfMaterialBinder.Bind(material, ResolveParams(material), slotName, _context.Options, _context.SaveDirectory)
                : new MaterialBuilder(slotName).WithBaseColor(Vector4.One);
```

And add the resolver the binder now depends on. It is cached because a mesh with eight
sections sharing one material would otherwise walk the material graph eight times:

```csharp
    private readonly Dictionary<UMaterialInterface, CMaterialParams2> _paramCache = new();

    /// <summary>
    /// Resolves material parameters at the session's <c>MaterialDepth</c> — the same depth
    /// <c>MaterialExporter</c> uses, which is what makes the URIs this writer emits and the
    /// files that session writes the same set by construction (spec §4.4).
    /// </summary>
    private CMaterialParams2 ResolveParams(UMaterialInterface material)
    {
        if (_paramCache.TryGetValue(material, out var cached)) return cached;

        var parameters = new CMaterialParams2();
        material.GetParams(parameters, _context.Options.MaterialDepth);
        _paramCache[material] = parameters;
        return parameters;
    }
```

Replace `Save`:

```csharp
    public void Save(FArchiveWriter Ar)
    {
        // SatelliteFile plus a callback that just returns the URI writes the reference
        // and nothing else: no image bytes in the GLB, no satellite file on disk. The
        // texture files are written by TextureExporter, in this same session.
        var settings = new WriteSettings
        {
            ImageWriting = ResourceWriteMode.SatelliteFile,
            ImageWriteCallback = (_, assetName, _) => assetName,
            MergeBuffers = true,
        };

        Ar.Write(Model.WriteGLB(settings).ToArray());
    }
```

- [ ] **Step 4: Update `GltfMeshFormat` to the new context signature and pass the file-derived name**

Replace `CUE4Parse-Conversion/Formats/Meshes/GltfMeshFormat.cs`:

```csharp
using CUE4Parse_Conversion.Dto;
using CUE4Parse_Conversion.Writers.Gltf;
using CUE4Parse.UE4.Writers;

namespace CUE4Parse_Conversion.Formats.Meshes;

public sealed class GltfMeshFormat : IMeshExportFormat
{
    public string DisplayName => "glTF 2.0 (binary)";

    public IReadOnlyList<ExportFile> BuildSkeletalMesh(in MeshExportContext context, SkeletalMeshDto dto)
    {
        var results = new List<ExportFile>();

        foreach (var lod in dto.LODs)
        {
            using var ar = new FArchiveWriter();
            new Gltf(context.ObjectName + lod._suffix, lod, context).Save(ar);

            results.Add(new ExportFile("glb", ar.GetBuffer(), lod._suffix));
        }

        return results;
    }

    public IReadOnlyList<ExportFile> BuildStaticMesh(in MeshExportContext context, StaticMeshDto dto)
    {
        var results = new List<ExportFile>();

        foreach (var lod in dto.LODs)
        {
            using var ar = new FArchiveWriter();
            new Gltf(context.ObjectName + lod._suffix, lod, context).Save(ar);

            results.Add(new ExportFile("glb", ar.GetBuffer(), lod._suffix));
        }

        return results;
    }

    public IReadOnlyList<ExportFile> BuildSkeleton(in MeshExportContext context, SkeletonDto dto)
        => throw new NotSupportedException(
            "glTF does not support skeleton-only exports. Please export a skeletal mesh to get a glTF file containing the skeleton.");
}
```

`ExporterBase.ResolveOutputPath` builds the file name as `{ObjectName}{NameSuffix}.{Extension}`, so `context.ObjectName + lod._suffix` is exactly the file's stem — the naming rule holds with no exceptions for any `--mesh-quality` × `--nanite` combination.

- [ ] **Step 5: Run the tests**

Run: `dotnet run --project CUE4Parse.Cli.Tests/CUE4Parse.Cli.Tests.csproj -- --filter-method "*GltfWriterTests*"`
Expected: `MeshDatablockAndRootNodeAreNamedAfterTheFile`, `ExportedGlbCarriesImagesAndTextures` and `NoMaterialsFallsBackToBareMaterialSlots` PASS. `EveryImageUriResolvesToAFileThatThisRunActuallyWrote` **still fails** for the metallicRoughness URI — the `_ORM` file does not exist yet. Task 7 closes that.

- [ ] **Step 6: Commit**

```bash
git add CUE4Parse-Conversion/Writers/Gltf/Gltf.cs CUE4Parse-Conversion/Formats/Meshes/GltfMeshFormat.cs CUE4Parse.Cli.Tests/GltfWriterTests.cs
git commit -m "feat: write real materials and file-derived datablock names into exported glTF"
```

---

## Task 7: `OrmTextureExporter`

**Files:**
- Create: `CUE4Parse-Conversion/Exporters/OrmTextureExporter.cs`
- Modify: `CUE4Parse-Conversion/Exporters/ExporterBase.cs` (name-suffix constructor)
- Modify: `CUE4Parse-Conversion/Exporters/MaterialExporter.cs`
- Test: `CUE4Parse.Cli.Tests/OrmTextureExporterTests.cs`

**Interfaces:**
- Consumes: `GltfMaterialBinder.OrmSuffix`, `UTexture.DecodeMip`, `ExportSession.Add`.
- Produces:
  - `public sealed class OrmTextureExporter(UTexture texture) : ExporterBase(texture, nameSuffix: GltfMaterialBinder.OrmSuffix)`
  - `protected ExporterBase(UObject export, string? className = null, string? nameSuffix = null)`

- [ ] **Step 1: Write the failing tests**

Create `CUE4Parse.Cli.Tests/OrmTextureExporterTests.cs`:

```csharp
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse_Conversion.Exporters;
using CUE4Parse_Conversion.Writers.Gltf;
using SkiaSharp;

namespace CUE4Parse.Cli.Tests;

public class OrmTextureExporterTests
{
    [Fact]
    public void OrmExporterTakesADifferentObjectPathFromThePlainTextureExporter()
    {
        var texture = FixtureAssets.LoadExport<UTexture>(
            "CUE4ParseFixtures/Content/Fixtures/Textures/T_BC3.uasset", "T_BC3");

        var plain = new TextureExporter(texture);
        var orm = new OrmTextureExporter(texture);

        Assert.NotEqual(plain.ObjectPath, orm.ObjectPath);
        Assert.Equal(plain.ObjectName + GltfMaterialBinder.OrmSuffix, orm.ObjectName);

        // Same folder as the source texture — a sibling, not a nested subfolder.
        Assert.Equal(plain.SaveDirectory, orm.SaveDirectory);
        Assert.Equal(plain.SavePath + GltfMaterialBinder.OrmSuffix, orm.SavePath);
    }

    [Fact]
    public async Task ExportedOrmImageHasGreenFromTheSourceBlueAndBlueFromTheSourceGreen()
    {
        var output = await GltfWriterTests.ExportAsync("CUE4ParseFixtures/Content/Fixtures/Meshes/SM_Fixture.uasset");

        var orm = Directory
            .GetFiles(output.FullName, "*_ORM.png", SearchOption.AllDirectories)
            .Single();
        var source = Path.Combine(
            Path.GetDirectoryName(orm)!,
            Path.GetFileNameWithoutExtension(orm)[..^GltfMaterialBinder.OrmSuffix.Length] + ".png");

        Assert.True(File.Exists(source), $"Source texture for {orm} was not written: {source}");

        using var ormBitmap = SKBitmap.Decode(orm);
        using var sourceBitmap = SKBitmap.Decode(source);
        Assert.Equal(sourceBitmap.Width, ormBitmap.Width);

        for (var y = 0; y < ormBitmap.Height; y += Math.Max(1, ormBitmap.Height / 8))
        for (var x = 0; x < ormBitmap.Width; x += Math.Max(1, ormBitmap.Width / 8))
        {
            var src = sourceBitmap.GetPixel(x, y);
            var dst = ormBitmap.GetPixel(x, y);

            Assert.Equal(src.Blue, dst.Green);   // roughness
            Assert.Equal(src.Green, dst.Blue);   // metallic
            Assert.Equal(255, dst.Red);          // occlusion is not carried; keep it neutral
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet run --project CUE4Parse.Cli.Tests/CUE4Parse.Cli.Tests.csproj -- --filter-method "*OrmTextureExporter*"`
Expected: build error — `OrmTextureExporter` does not exist.

- [ ] **Step 3: Add the name-suffix constructor to `ExporterBase`**

In `CUE4Parse-Conversion/Exporters/ExporterBase.cs`, replace the private constructor and the `UObject` constructor:

```csharp
    private ExporterBase(string packagePath, string objectName, string className, string? nameSuffix)
    {
        PackagePath = packagePath;
        PackageDirectory = PackagePath.Contains('/') ? PackagePath.SubstringBeforeLast('/') : string.Empty;

        // The suffix is appended *after* the leaf/name collapse, so a suffixed exporter
        // writes a sibling file rather than a nested folder.
        var leaf = PackagePath.SubstringAfterLast('/');
        var basePath = (leaf.Equals(objectName, StringComparison.OrdinalIgnoreCase) ? PackagePath : PackagePath + '/' + objectName).TrimStart('/');
        SavePath = basePath + nameSuffix;
        SaveDirectory = SavePath.Contains('/') ? SavePath.SubstringBeforeLast('/') : string.Empty;

        ObjectName = objectName + nameSuffix;
        ObjectPath = PackagePath + '.' + ObjectName;
        ClassName = className;

        Log = Serilog.Log.ForContext(GetType())
            .ForContext(nameof(ObjectPath), ObjectPath)
            .ForContext(nameof(ClassName), ClassName)
            .ForContext("ExporterV2", true);
    }

    protected ExporterBase(UObject export, string? className = null, string? nameSuffix = null)
        : this(BuildPackagePath(export), export.Name, className ?? export.ExportType, nameSuffix)
    {

    }

    protected internal ExporterBase(GameFile file, string className)
        : this(file.PathWithoutExtension, file.NameWithoutExtension, className, null)
    {
        if (file.IsUePackagePayload)
            throw new ArgumentException("GameFile must not be a UE package payload file", nameof(file));
    }
```

- [ ] **Step 4: Write the ORM exporter**

Create `CUE4Parse-Conversion/Exporters/OrmTextureExporter.cs`:

```csharp
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse_Conversion.Textures;
using CUE4Parse_Conversion.Writers.Gltf;
using SkiaSharp;

namespace CUE4Parse_Conversion.Exporters;

/// <summary>
/// Repacks a UE SpecularMasks/SRM texture into glTF's metallicRoughness channel order.
/// <para>
/// UE packs G = metallic, B = roughness; glTF fixes G = roughness, B = metallic. The two
/// are exact transpositions and glTF 2.0 has no channel swizzle, so the only honest
/// option is a second image.
/// </para>
/// <para>
/// It lives behind its own <c>ObjectPath</c> rather than as a flag on
/// <see cref="TextureExporter"/> on purpose. <c>ExportSession.Add</c> deduplicates on
/// <c>ObjectPath</c> first-wins, so a flag would make the output depend on which
/// material happened to enqueue the shared texture first under
/// <c>Parallel.ForEachAsync</c>. Here every enqueue is equivalent, so the file is
/// written exactly once no matter how many materials point at it, and the result does
/// not depend on ordering.
/// </para>
/// </summary>
public sealed class OrmTextureExporter(UTexture texture)
    : ExporterBase(texture, nameSuffix: GltfMaterialBinder.OrmSuffix)
{
    protected override IReadOnlyList<ExportFile> BuildExportFiles(CancellationToken ct = default)
    {
        // Only the first mip: this image exists so a glTF material can point at it, and
        // a glTF material can point at exactly one image.
        var index = texture.GetFirstMipIndex();
        var decoded = texture.DecodeMip(index, Session.Options.TexturePlatform)
            ?? throw new Exception($"Failed to decode texture mip {index}");

        ct.ThrowIfCancellationRequested();

        using var source = decoded.ToSkBitmap();
        using var repacked = new SKBitmap(source.Width, source.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);

        for (var y = 0; y < source.Height; y++)
        for (var x = 0; x < source.Width; x++)
        {
            var pixel = source.GetPixel(x, y);
            repacked.SetPixel(x, y, new SKColor(255, pixel.Blue, pixel.Green, 255));
        }

        using var data = repacked.Encode(SKEncodedImageFormat.Png, Session.Options.TextureQuality);
        return [new ExportFile("png", data.ToArray())];
    }
}
```

- [ ] **Step 5: Enqueue it from `MaterialExporter`**

Replace `CUE4Parse-Conversion/Exporters/MaterialExporter.cs`:

```csharp
using CUE4Parse_Conversion.Formats.Materials;
using CUE4Parse_Conversion.Options;
using CUE4Parse_Conversion.Writers.Gltf;
using CUE4Parse.UE4.Assets.Exports.Material;

namespace CUE4Parse_Conversion.Exporters;

public sealed class MaterialExporter(UMaterialInterface material) : ExporterBase(material)
{
    private static readonly string[] SpecularNames =
        [.. CMaterialParams2.SpecularMasks[0], CMaterialParams2.FallbackSpecularMasks];

    protected override IReadOnlyList<ExportFile> BuildExportFiles(CancellationToken ct = default)
    {
        Log.Debug("Extracting material parameters (depth: {Depth})", Session.Options.MaterialDepth);

        var parameters = new CMaterialParams2();
        material.GetParams(parameters, Session.Options.MaterialDepth);

        var files = new List<ExportFile> { new JsonMaterialFormat().Build(ObjectName, parameters) };
        if (Session.Options.MeshFormat == EMeshFormat.USD)
        {
            files.Add(new UsdMaterialFormat().Build(ObjectName, parameters, SaveDirectory));
        }

        foreach (var texture in parameters.Textures.Values)
        {
            ct.ThrowIfCancellationRequested();
            Session.Add(texture);
        }

        // Enqueued here rather than in the binder: this is the place that already has
        // both the classification and the session, and it keeps the binder pure.
        if (Session.Options.MeshFormat == EMeshFormat.Gltf2 &&
            parameters.TryGetTexture2d(out var specular, SpecularNames))
        {
            Session.Add(new OrmTextureExporter(specular));
        }

        return files;
    }
}
```

- [ ] **Step 6: Run the tests**

Run: `dotnet run --project CUE4Parse.Cli.Tests/CUE4Parse.Cli.Tests.csproj -- --filter-method "*OrmTextureExporter*"`
Run: `dotnet run --project CUE4Parse.Cli.Tests/CUE4Parse.Cli.Tests.csproj -- --filter-method "*EveryImageUriResolvesToAFile*"`
Expected: PASS for all three, including the URI-resolution test that Task 6 left red.

- [ ] **Step 7: Commit**

```bash
git add CUE4Parse-Conversion CUE4Parse.Cli.Tests/OrmTextureExporterTests.cs
git commit -m "feat: write a repacked ORM sibling so glTF metallicRoughness has real data"
```

---

## Task 8: Unit-length normals, and morph deltas that are not normalized

**Files:**
- Modify: `CUE4Parse-Conversion/Writers/Gltf/Gltf.cs:76`, `:237-242`
- Modify: `CUE4Parse-Conversion/Writers/UEFormat/UEModel.cs:139-143`
- Test: `CUE4Parse.Cli.Tests/GltfGeometryTests.cs`

**Interfaces:**
- Consumes: `Gltf.SwapYZ`, `VertexGeometryDelta(Vector3 p, Vector3 n, Vector3 t)`.
- Produces: no new API. `MathUtils.InvSqrt` is deliberately **not** touched — it models UE's `FMath::InvSqrt` and is used well outside the write path.

- [ ] **Step 1: Write the failing tests**

Create `CUE4Parse.Cli.Tests/GltfGeometryTests.cs`:

```csharp
using System.Numerics;
using Newtonsoft.Json.Linq;

namespace CUE4Parse.Cli.Tests;

public class GltfGeometryTests
{
    /// <summary>Reads a VEC3 float accessor out of a GLB's single binary chunk.</summary>
    public static Vector3[] ReadVec3Accessor(string glbPath, int accessorIndex)
    {
        var bytes = File.ReadAllBytes(glbPath);
        var jsonLength = BitConverter.ToInt32(bytes, 12);
        var json = JObject.Parse(System.Text.Encoding.UTF8.GetString(bytes, 20, jsonLength));
        var binaryStart = 20 + jsonLength + 8; // json chunk, then BIN chunk header

        var accessor = json["accessors"]![accessorIndex]!;
        Assert.Equal("VEC3", accessor["type"]!.Value<string>());
        Assert.Equal(5126, accessor["componentType"]!.Value<int>()); // FLOAT

        var view = json["bufferViews"]![accessor["bufferView"]!.Value<int>()]!;
        var stride = view["byteStride"]?.Value<int>() ?? 12;
        var start = binaryStart + (view["byteOffset"]?.Value<int>() ?? 0) + (accessor["byteOffset"]?.Value<int>() ?? 0);
        var count = accessor["count"]!.Value<int>();

        var result = new Vector3[count];
        for (var i = 0; i < count; i++)
        {
            var at = start + i * stride;
            result[i] = new Vector3(
                BitConverter.ToSingle(bytes, at),
                BitConverter.ToSingle(bytes, at + 4),
                BitConverter.ToSingle(bytes, at + 8));
        }

        return result;
    }

    public static int AccessorIndex(string glbPath, string attribute)
    {
        var bytes = File.ReadAllBytes(glbPath);
        var jsonLength = BitConverter.ToInt32(bytes, 12);
        var json = JObject.Parse(System.Text.Encoding.UTF8.GetString(bytes, 20, jsonLength));
        return json["meshes"]![0]!["primitives"]![0]!["attributes"]![attribute]!.Value<int>();
    }

    [Fact]
    public async Task EveryNormalIsAUnitVector()
    {
        var output = await GltfWriterTests.ExportAsync("CUE4ParseFixtures/Content/Fixtures/Meshes/SM_Fixture.uasset");
        var glb = Directory.GetFiles(output.FullName, "*.glb", SearchOption.AllDirectories).Single();

        var normals = ReadVec3Accessor(glb, AccessorIndex(glb, "NORMAL"));

        Assert.NotEmpty(normals);
        Assert.All(normals, n => Assert.InRange(n.Length(), 1f - 1e-6f, 1f + 1e-6f));
    }

    [Fact]
    public async Task MorphTargetDeltasAreNotNormalizedAndSitInTheNormalSlot()
    {
        var output = await GltfWriterTests.ExportAsync("CUE4ParseFixtures/Content/Fixtures/Meshes/SK_Fixture.uasset");
        var glb = Directory.GetFiles(output.FullName, "*.glb", SearchOption.AllDirectories).Single();

        var bytes = File.ReadAllBytes(glb);
        var jsonLength = BitConverter.ToInt32(bytes, 12);
        var json = JObject.Parse(System.Text.Encoding.UTF8.GetString(bytes, 20, jsonLength));

        var targets = json["meshes"]![0]!["primitives"]![0]!["targets"] as JArray;
        Assert.NotNull(targets);
        Assert.NotEmpty(targets!);

        // A morph target that carries normals must expose NORMAL, not TANGENT, and its
        // deltas must not be unit vectors: the renderer normalises base + sum(deltas).
        var target = targets![0]!;
        Assert.NotNull(target["NORMAL"]);
        Assert.Null(target["TANGENT"]);

        var deltas = ReadVec3Accessor(glb, target["NORMAL"]!.Value<int>());
        Assert.Contains(deltas, d => d.Length() < 0.9f);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet run --project CUE4Parse.Cli.Tests/CUE4Parse.Cli.Tests.csproj -- --filter-method "*GltfGeometryTests*"`
Expected: `EveryNormalIsAUnitVector` fails with lengths off by roughly 0.175% (`FVector.Normalize` uses the Quake-style `MathUtils.InvSqrt`). `MorphTargetDeltasAreNotNormalized` fails on `Assert.Null(target["TANGENT"])` — deltas are currently written into the tangent slot.

- [ ] **Step 3: Fix the normalization and the morph delta**

In `CUE4Parse-Conversion/Writers/Gltf/Gltf.cs`, replace `SwapYZAndNormalize(FVector)`:

```csharp
    public static FVector SwapYZAndNormalize(FVector vec)
    {
        // System.Numerics, not FVector.Normalize: the latter goes through
        // MathUtils.InvSqrt, a fast inverse square root that models UE's FMath::InvSqrt
        // to about 0.175%. That is fine inside the engine's own maths and not fine for
        // a NORMAL accessor a validator checks for unit length. InvSqrt itself stays as
        // it is — it has callers well outside the write path.
        var normalized = Vector3.Normalize(new Vector3(vec.X, vec.Z, vec.Y));
        return new FVector(normalized.X, normalized.Y, normalized.Z);
    }
```

Replace line 76 (the `SetVertexDelta` call) with:

```csharp
                    // VertexGeometryDelta is (PositionDelta, NormalDelta, TangentDelta).
                    // In UE, TangentZ *is* the normal, so it belongs in the second slot.
                    // It is a difference, not a direction: a typical delta normal has
                    // length ~0.02, and normalizing it would inflate that fiftyfold.
                    // glTF requires no unit length here — the renderer normalises
                    // base + sum(weight * delta) after summation. UnitScale applies to
                    // positions only.
                    morphBuilder.SetVertexDelta(
                        morphBuilder.Vertices.ElementAt(index),
                        new VertexGeometryDelta(
                            SwapYZ(delta.PositionDelta * UnitScale),
                            SwapYZ(delta.TangentZDelta),
                            Vector3.Zero));
```

In `CUE4Parse-Conversion/Writers/UEFormat/UEModel.cs`, replace the TANGENTS attribute body:

```csharp
        attrs.AddAttribute("TANGENTS", attr => attr.WriteArray(lod.Vertices, (writer, vertex) =>
        {
            var tangent = (FVector) vertex.Tangent;
            // Exact, matching the NORMALS attribute directly above; FVector.Normalize
            // would reintroduce the fast-inverse-square-root error here alone.
            tangent /= MathF.Sqrt(tangent | tangent);
            tangent.Serialize(writer);
        }));
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet run --project CUE4Parse.Cli.Tests/CUE4Parse.Cli.Tests.csproj -- --filter-method "*GltfGeometryTests*"`
Expected: PASS. If `SK_Fixture` has no morph targets, the second test will fail at `Assert.NotEmpty(targets)` — in that case change it to a `[Fact]` that skips with an explicit message naming the missing fixture capability, and record the gap in the Task 19 documentation. Do not delete the assertion.

- [ ] **Step 5: Commit**

```bash
git add CUE4Parse-Conversion/Writers CUE4Parse.Cli.Tests/GltfGeometryTests.cs
git commit -m "fix: exact normal normalization in glTF/UEFormat and correct morph-target deltas"
```

---

## Task 9: Validate exported glTF against the pinned validator **and a headless Blender import**, in CI

**Files:**
- Test: `CUE4Parse.Cli.Tests/GltfValidationTests.cs`
- Create: `CUE4Parse.Cli.Tests/BlenderImportTests.cs`
- Create: `tools/blender/check_import.py`
- Modify: `.github/workflows/cli-tests.yml` (no change needed if Task 1's env vars are set; verify)

**Interfaces:**
- Consumes: `GltfValidator.Validate`, `GltfWriterTests.ExportAsync`, `$BLENDER`.
- Produces: nothing.

> **Why both.** They prove different things. The validator proves the file is spec-legal.
> Blender proves it is *usable* — that the importer resolves the relative URIs from its own
> base path, that the PNG bytes decode, and that the images land on shader nodes. Those are
> exactly the three ways to get a white mesh out of a file with zero validator errors, which
> is spec §1 goal condition #1 and was previously verified by nothing at all.
>
> On the current fixtures this gate only exercises the base-color path (spec §9.1). It is
> still worth having: it is the only check in the plan that runs the real consumer.

- [ ] **Step 1: Write the test**

Create `CUE4Parse.Cli.Tests/GltfValidationTests.cs`:

```csharp
namespace CUE4Parse.Cli.Tests;

public class GltfValidationTests
{
    [Theory]
    [InlineData("CUE4ParseFixtures/Content/Fixtures/Meshes/SM_Fixture.uasset")]
    [InlineData("CUE4ParseFixtures/Content/Fixtures/Meshes/SK_Fixture.uasset")]
    [InlineData("CUE4ParseFixtures/Content/Fixtures/Meshes/SM_Nanite.uasset")]
    public async Task ExportedGlbHasNoValidatorErrors(string assetPath)
    {
        if (!GltfValidator.TryLocate(out _))
        {
            Assert.Skip("GLTF_VALIDATOR not set; install the pinned validator to run this test.");
        }

        var output = await GltfWriterTests.ExportAsync(assetPath);

        foreach (var glb in Directory.GetFiles(output.FullName, "*.glb", SearchOption.AllDirectories))
        {
            var report = GltfValidator.Validate(glb);
            Assert.True(report.Errors == 0, $"{Path.GetFileName(glb)} has {report.Errors} validator errors:\n{report.Raw}");
        }
    }
}
```

- [ ] **Step 2: Install the validator locally and run**

Download the release named in `tools/gltf-validator.version` for your platform, then:

```bash
export GLTF_VALIDATOR=/path/to/gltf_validator
dotnet run --project CUE4Parse.Cli.Tests/CUE4Parse.Cli.Tests.csproj -- --filter-method "*ExportedGlbHasNoValidatorErrors*"
```

Expected: PASS. Any reported error is a real defect in the writer — fix the writer, never the assertion. The most likely first failures are `UNRESOLVED_REFERENCE` for image URIs (the validator resolves them relative to the `.glb`, which is exactly what Task 6's URI test also checks) and `ACCESSOR_INVALID_FLOAT` / non-unit `NORMAL` (Task 8).

- [ ] **Step 3: Write the Blender import check**

Create `tools/blender/check_import.py`. It runs under Blender's own Python, so it gets no
test framework — it fails by raising, and Blender's non-zero exit is the signal.

```python
"""Import every .glb under argv[0] and assert its textures actually resolve.

Run as: blender -b -P tools/blender/check_import.py -- <directory>
Fails loudly: any raise leaves Blender with a non-zero exit code, which is the assertion.
"""
import pathlib
import sys

import bpy

directory = pathlib.Path(sys.argv[sys.argv.index("--") + 1])
files = sorted(directory.rglob("*.glb"))
if not files:
    raise SystemExit(f"no .glb found under {directory}")

for path in files:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=str(path))

    if not bpy.data.objects:
        raise SystemExit(f"{path.name}: imported no objects")

    stem = path.stem
    if not any(mesh.name == stem for mesh in bpy.data.meshes):
        names = ", ".join(mesh.name for mesh in bpy.data.meshes)
        raise SystemExit(f"{path.name}: no mesh datablock named {stem!r}; got: {names}")

    for image in bpy.data.images:
        if image.name == "Render Result":
            continue
        # has_data is the real check: a resolvable path that fails to decode still
        # renders as a white mesh, which is the exact failure this gate exists for.
        if not image.has_data:
            raise SystemExit(f"{path.name}: image {image.name!r} ({image.filepath}) has no data")

    print(f"OK  {path.name}: {len(bpy.data.objects)} objects, {len(bpy.data.images)} images")
```

Create `CUE4Parse.Cli.Tests/BlenderImportTests.cs`, skipping when `$BLENDER` is unset so the
suite stays runnable on a machine without it:

```csharp
using System.Diagnostics;

namespace CUE4Parse.Cli.Tests;

public class BlenderImportTests
{
    [Theory]
    [InlineData("CUE4ParseFixtures/Content/Fixtures/Meshes/SM_Fixture.uasset")]
    [InlineData("CUE4ParseFixtures/Content/Fixtures/Meshes/SK_Fixture.uasset")]
    public async Task ExportedGlbImportsIntoBlenderWithResolvableTextures(string assetPath)
    {
        var blender = Environment.GetEnvironmentVariable("BLENDER");
        if (string.IsNullOrWhiteSpace(blender))
        {
            Assert.Skip("BLENDER not set; install the pinned Blender to run this test.");
        }

        var output = await GltfWriterTests.ExportAsync(assetPath);

        var psi = new ProcessStartInfo(blender!)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in new[] { "-b", "-P", "tools/blender/check_import.py", "--", output.FullName })
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi)!;
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        Assert.True(process.ExitCode == 0, $"Blender import failed:\n{stdout}\n{stderr}");
    }
}
```

- [ ] **Step 4: Run both locally**

```bash
export GLTF_VALIDATOR=/path/to/gltf_validator
export BLENDER=/path/to/blender
dotnet run --project CUE4Parse.Cli.Tests/CUE4Parse.Cli.Tests.csproj -- --filter-method "*ExportedGlb*"
```

Expected: PASS. A failure naming an image with no data means the URI resolved to a file
Blender could not decode — check `TextureFileNamer` (Task 4) before suspecting the binder.

- [ ] **Step 5: Confirm CI picks it up**

Push the branch; confirm the `cli-tests-linux` job reports these tests as run, not skipped. If they skip, `GLTF_VALIDATOR` or `BLENDER` was not exported to `$GITHUB_ENV` correctly in Task 1.

- [ ] **Step 6: Commit**

```bash
git add CUE4Parse.Cli.Tests/GltfValidationTests.cs CUE4Parse.Cli.Tests/BlenderImportTests.cs tools/blender/check_import.py
git commit -m "test: validate exported glTF against the pinned validator and a Blender import"
```

---

## Task 10: `docs/cue4-output-contract.md`

**Files:**
- Create: `docs/cue4-output-contract.md`
- Test: manual review against the acceptance criterion "describes enough to write a bpy script without reading source".

**Interfaces:**
- Consumes: everything Phase 1 established.
- Produces: the written contract Phase 2's manifest section extends.

- [ ] **Step 1: Write the contract**

Create `docs/cue4-output-contract.md` covering, in this order:

1. **Directory layout.** `<out>/<package path>/<ObjectName>.<ext>`; when the package path's leaf already equals `ObjectName`, no extra level is nested.
2. **File-name suffixes.** The first exported LOD carries no suffix; later LODs are `_LOD{SourceLodIndex}`; a Nanite LOD is `_Nanite`. With `--mesh-quality highest`, `MESH_X.glb` may contain source LOD 3 — the file name does not say which.
3. **Texture file names.** Extension follows `--texture-format`, **except** HDR sources become `.hdr` — and `--mesh-format gltf2` forces HDR off, making `--no-hdr` a no-op there. `--all-mips` appends `_MIP{n}` and in that mode **no unsuffixed file exists**. `UTexture2DArray` appends `_LAYER{i}`. `--mesh-format usd` forces PNG regardless of `--texture-format` (pre-existing behaviour, previously undocumented).
4. **glTF specifics.** Textures are referenced by relative, percent-encoded URI and are never embedded. `metallicRoughnessTexture` points at a repacked `<name>_ORM.png` (R = 255, G = source blue = roughness, B = source green = metallic); `occlusionTexture` is never written because the source red channel is specular, not AO. `alphaMode`/`alphaCutoff`/`doubleSided` come from the UE material.
5. **Naming.** `meshes[].name` = root node name = the `.glb` file name without its extension, with no exceptions.
6. **Audio.** `.wem` and `.binka` are raw bytes; they need vgmstream downstream to become playable. `.ogg` and `.wav` are final.
7. **Commitments** — copy spec §8.3 verbatim.
8. **Non-commitments** — copy spec §8.4 verbatim, including: texture bytes are not stable across CUE4Parse versions; NDJSON line order is undefined (use the manifest); glTF `nodes` order is not committed while `skins[].joints` is; the manifest does not report the source LOD of a file; morph deltas are deliberately not unit vectors.
9. **Exit codes** — the table from Global Constraints.

- [ ] **Step 2: Verify by writing the script the contract promises is possible**

Write `docs/examples/import_glb.py` (a short bpy script, not run in CI) that imports every `.glb` under a directory, renames objects from the mesh datablock name, and reports missing textures. Writing it is the check: any question it cannot answer from the contract is a gap in the contract, and the fix is to extend the contract.

- [ ] **Step 3: Commit**

```bash
git add docs/cue4-output-contract.md docs/examples/import_glb.py
git commit -m "docs: write the cue4 output contract"
```

---

# Phase 2 — A pipeline a bpy script can drive unattended

## Task 11: The missing export flags

**Files:**
- Modify: `CUE4Parse.Cli/Program.cs`, `CUE4Parse.Cli/Commands/ExportCommand.cs`, `CUE4Parse.Cli/Services/ExportOptionsMapper.cs`
- Test: `CUE4Parse.Cli.Tests/ExportOptionsMapperTests.cs`

**Interfaces:**
- Consumes: `ExportOptions` constructor parameters `compressionFormat`, `exportMorphTargets`, `exportHdrTexturesAsHdr`.
- Produces: `ExportFlags` gains `string CompressionFormat`, `bool NoMorphTargets`, `bool NoHdr`; `ExportOptionsMapper.CompressionFormats` dictionary.

- [ ] **Step 1: Write the failing tests**

Append to `CUE4Parse.Cli.Tests/ExportOptionsMapperTests.cs`:

```csharp
    [Fact]
    public void CompressionFormatDefaultsToNoneAndMapsByName()
    {
        Assert.Equal(EFileCompressionFormat.None, ExportOptionsMapper.Map(Ueformat()).CompressionFormat);
        Assert.Equal(EFileCompressionFormat.ZSTD,
            ExportOptionsMapper.Map(Ueformat() with { CompressionFormat = "zstd" }).CompressionFormat);
        Assert.Equal(EFileCompressionFormat.GZIP,
            ExportOptionsMapper.Map(Ueformat() with { CompressionFormat = "gzip" }).CompressionFormat);
    }

    [Fact]
    public void MorphTargetsAreExportedUnlessTheFlagOptsOut()
    {
        Assert.True(ExportOptionsMapper.Map(Ueformat()).ExportMorphTargets);
        Assert.False(ExportOptionsMapper.Map(Ueformat() with { NoMorphTargets = true }).ExportMorphTargets);
    }

    [Fact]
    public void HdrIsKeptUnlessTheFlagOptsOutAndIsAlwaysOffForGltf2()
    {
        Assert.True(ExportOptionsMapper.Map(Ueformat()).ExportHdrTexturesAsHdr);
        Assert.False(ExportOptionsMapper.Map(Ueformat() with { NoHdr = true }).ExportHdrTexturesAsHdr);
        Assert.False(ExportOptionsMapper.Map(ExportFlagDefaults.Gltf2()).ExportHdrTexturesAsHdr);
    }

    private static ExportFlags Ueformat() => ExportFlagDefaults.Gltf2() with { MeshFormat = "ueformat" };
```

Add `using CUE4Parse_Conversion.Writers.UEFormat.Enums;` for `EFileCompressionFormat`. Confirm the enum member spellings first:
`grep -n "enum EFileCompressionFormat" -A 6 CUE4Parse-Conversion/Writers/UEFormat/Enums/*.cs` — use whatever names it prints.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet run --project CUE4Parse.Cli.Tests/CUE4Parse.Cli.Tests.csproj -- --filter-method "*ExportOptionsMapperTests*"`
Expected: build error — `ExportFlags` has no `CompressionFormat`.

- [ ] **Step 3: Extend `ExportFlags`**

In `CUE4Parse.Cli/Commands/ExportCommand.cs`, add three members to the record:

```csharp
public sealed record ExportFlags(
    string MeshFormat,
    string TextureFormat,
    string TexturePlatform,
    string MeshQuality,
    string Nanite,
    string SocketFormat,
    string MaterialDepth,
    int TextureQuality,
    bool NoMaterials,
    bool AllMips,
    string CompressionFormat = "none",
    bool NoMorphTargets = false,
    bool NoHdr = false);
```

Defaults keep `ExportFlagDefaults.Gltf2()` and every existing test compiling unchanged.

- [ ] **Step 4: Extend the mapper**

In `CUE4Parse.Cli/Services/ExportOptionsMapper.cs`, add the vocabulary and wire the three options into the `new ExportOptions(...)` call added in Task 4:

```csharp
    public static readonly Dictionary<string, EFileCompressionFormat> CompressionFormats = new()
    {
        ["none"] = EFileCompressionFormat.None,
        ["gzip"] = EFileCompressionFormat.GZIP,
        ["zstd"] = EFileCompressionFormat.ZSTD,
    };
```

```csharp
            exportMorphTargets: !flags.NoMorphTargets,
            exportHdrTexturesAsHdr: !flags.NoHdr,
            compressionFormat: Pick(flags.CompressionFormat, "--compression-format", CompressionFormats));
```

`ExportOptions` already drops `compressionFormat` for anything other than UEFormat, so the CLI adds no second guard.

- [ ] **Step 5: Register the options in `Program.cs`**

Add next to the existing export options:

```csharp
var compressionFormatOpt = new Option<string>("--compression-format")
    { Description = "UEFormat file compression", DefaultValueFactory = _ => "none" };
var noMorphTargetsOpt = new Option<bool>("--no-morph-targets") { Description = "Skip morph targets" };
var noHdrOpt = new Option<bool>("--no-hdr")
    { Description = "Write HDR sources in the raster format instead of .hdr (no effect with --mesh-format gltf2)" };

compressionFormatOpt.AcceptOnlyFromAmong([.. ExportOptionsMapper.CompressionFormats.Keys]);
```

Add all three to the `foreach (var option in new Option[] { ... })` list, and to the `new ExportFlags(...)` construction:

```csharp
            CompressionFormat: pr.GetValue(compressionFormatOpt)!,
            NoMorphTargets: pr.GetValue(noMorphTargetsOpt),
            NoHdr: pr.GetValue(noHdrOpt)),
```

- [ ] **Step 6: Run the tests**

Run: `dotnet run --project CUE4Parse.Cli.Tests/CUE4Parse.Cli.Tests.csproj -- --filter-method "*ExportOptionsMapperTests*"`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add CUE4Parse.Cli CUE4Parse.Cli.Tests/ExportOptionsMapperTests.cs
git commit -m "feat: expose --compression-format, --no-morph-targets and --no-hdr"
```

---

## Task 12: `--flip-normal-y`

**Files:**
- Modify: `CUE4Parse-Conversion/Options/ExportOptions.cs`, `CUE4Parse-Conversion/Exporters/TextureExporter.cs`
- Modify: `CUE4Parse.Cli/Program.cs`, `Commands/ExportCommand.cs`, `Services/ExportOptionsMapper.cs`
- Test: `CUE4Parse.Cli.Tests/FlipNormalYTests.cs`

**Interfaces:**
- Consumes: `UTexture.CompressionSettings`, `CTexture`.
- Produces: `ExportOptions.FlipNormalY` (`bool`, default `false`); `ExportFlags.FlipNormalY`.

- [ ] **Step 1: Write the failing test**

Create `CUE4Parse.Cli.Tests/FlipNormalYTests.cs`:

```csharp
using SkiaSharp;

namespace CUE4Parse.Cli.Tests;

public class FlipNormalYTests
{
    /// <summary>
    /// Selection is by CompressionSettings == TC_Normalmap, a property of the texture
    /// itself, not by CMaterialParams2's name-based classification: a texture knows what
    /// it is, a name heuristic only guesses.
    /// </summary>
    [Fact]
    public async Task FlipNormalYInvertsTheGreenChannelOfNormalMapsOnly()
    {
        var asset = "CUE4ParseFixtures/Content/Fixtures/Meshes/SM_Fixture.uasset";

        var plain = await GltfWriterTests.ExportAsync(asset);
        var flipped = await GltfWriterTests.ExportAsync(
            asset, ExportFlagDefaults.Gltf2() with { FlipNormalY = true });

        var changed = 0;
        foreach (var before in Directory.GetFiles(plain.FullName, "*.png", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(plain.FullName, before);
            var after = Path.Combine(flipped.FullName, relative);
            Assert.True(File.Exists(after), after);

            using var a = SKBitmap.Decode(before);
            using var b = SKBitmap.Decode(after);

            var pa = a.GetPixel(a.Width / 2, a.Height / 2);
            var pb = b.GetPixel(b.Width / 2, b.Height / 2);

            if (pa.Green == pb.Green) continue;

            changed++;
            Assert.Equal(255 - pa.Green, pb.Green);
            Assert.Equal(pa.Red, pb.Red);
            Assert.Equal(pa.Blue, pb.Blue);
        }

        Assert.True(changed > 0, "No normal map changed; the fixture set has no TC_Normalmap texture.");
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet run --project CUE4Parse.Cli.Tests/CUE4Parse.Cli.Tests.csproj -- --filter-method "*FlipNormalY*"`
Expected: build error — `ExportFlags` has no `FlipNormalY`.

- [ ] **Step 3: Add the option**

In `CUE4Parse-Conversion/Options/ExportOptions.cs`, add the constructor parameter `bool flipNormalY = false` after `exportAllTextureMips`, and the field:

```csharp
    public readonly bool FlipNormalY = flipNormalY;
```

- [ ] **Step 4: Apply it when writing texture bytes**

glTF has no notion of "flip the green channel", so the inversion must happen in the PNG bytes. In `CUE4Parse-Conversion/Exporters/TextureExporter.cs`, inside `AddMip`, immediately before `slice.Encode(...)`:

```csharp
                if (Session.Options.FlipNormalY && texture.CompressionSettings == TextureCompressionSettings.TC_Normalmap)
                {
                    // Off by default: cue4 writes the bytes the game shipped. If a user
                    // opens the result in Blender and the lighting reads inside-out,
                    // they turn this on. Guessing for them is worse than asking.
                    InvertGreenChannel(slice);
                }
```

Add the helper at the bottom of the class:

```csharp
    /// <summary>Inverts G in place for an 8-bit RGBA-ish decoded texture.</summary>
    private static void InvertGreenChannel(CTexture texture)
    {
        if (!PixelFormatUtils.PixelFormats.TryGetValue(texture.PixelFormat, out var info) || info.NumComponents < 2)
        {
            return;
        }

        var stride = texture.Data.Length / (texture.Width * texture.Height);
        if (stride < 2) return;

        for (var i = 1; i < texture.Data.Length; i += stride)
        {
            texture.Data[i] = (byte)(255 - texture.Data[i]);
        }
    }
```

Add `using CUE4Parse.UE4.Assets.Exports.Texture;` if the file does not already import it. If `CTexture.Data` is not writable in place, decode already returns a fresh array per call — verify with `grep -n "byte\[\] Data" CUE4Parse-Conversion/Textures/CTexture.cs`; it is `public byte[] Data { get; }`, an array reference, so mutating its elements is fine.

- [ ] **Step 5: Wire the flag through the CLI**

Add `bool FlipNormalY = false` to `ExportFlags`; add `flipNormalY: flags.FlipNormalY` to the mapper's `new ExportOptions(...)`; register in `Program.cs`:

```csharp
var flipNormalYOpt = new Option<bool>("--flip-normal-y")
    { Description = "Invert the green channel of TC_Normalmap textures" };
```

and add it to the option list and to `new ExportFlags(... FlipNormalY: pr.GetValue(flipNormalYOpt))`.

- [ ] **Step 6: Run the test**

Run: `dotnet run --project CUE4Parse.Cli.Tests/CUE4Parse.Cli.Tests.csproj -- --filter-method "*FlipNormalY*"`
Expected: PASS. If it fails on `changed > 0`, the fixture set has no `TC_Normalmap` texture — confirm with
`dotnet run --project CUE4Parse.Cli/CUE4Parse.Cli.csproj -- dump <texture> --indent | grep CompressionSettings`, and if so convert that assertion into an `Assert.Skip` naming the missing fixture, keeping the per-pixel assertions.

- [ ] **Step 7: Commit**

```bash
git add CUE4Parse-Conversion CUE4Parse.Cli CUE4Parse.Cli.Tests/FlipNormalYTests.cs
git commit -m "feat: --flip-normal-y for normal maps whose green channel reads inverted"
```

---

## Task 13: Warn when Nanite data is being dropped

**Files:**
- Modify: `CUE4Parse-Conversion/Exporters/MeshExporter.cs`
- Test: `CUE4Parse.Cli.Tests/NaniteWarningTests.cs`

**Interfaces:**
- Consumes: `MeshDto<T>.LODs`, `ENaniteMeshFormat`.
- Produces: a `Log.Warning` on the exporter's logger; no API change.

- [ ] **Step 1: Write the failing test**

Create `CUE4Parse.Cli.Tests/NaniteWarningTests.cs`:

```csharp
using Serilog;
using Serilog.Events;

namespace CUE4Parse.Cli.Tests;

public class NaniteWarningTests
{
    private sealed class CapturingSink : Serilog.Core.ILogEventSink
    {
        public readonly List<string> Messages = [];
        public void Emit(LogEvent logEvent) => Messages.Add(logEvent.RenderMessage());
    }

    [Fact]
    public async Task ExportingANaniteOnlyMeshWithNoNaniteWarnsInsteadOfSilentlyProducingNothing()
    {
        var sink = new CapturingSink();
        var previous = Log.Logger;
        Log.Logger = new LoggerConfiguration().MinimumLevel.Debug().WriteTo.Sink(sink).CreateLogger();
        CUE4ParseLog.UseLogger(Log.Logger);

        try
        {
            await GltfWriterTests.ExportAsync("CUE4ParseFixtures/Content/Fixtures/Meshes/SM_Nanite.uasset");
        }
        finally
        {
            Log.Logger = previous;
            CUE4ParseLog.UseLogger(previous);
        }

        Assert.Contains(sink.Messages, message => message.Contains("Nanite", StringComparison.Ordinal));
    }
}
```

Add `using CUE4Parse;` for `CUE4ParseLog`.

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet run --project CUE4Parse.Cli.Tests/CUE4Parse.Cli.Tests.csproj -- --filter-method "*NaniteOnlyMesh*"`
Expected: FAIL — no message mentions Nanite.

- [ ] **Step 3: Add the warning**

In `CUE4Parse-Conversion/Exporters/MeshExporter.cs`, add a helper and call it from each subclass right after the DTO is built. Add to `MeshExporter<T>`:

```csharp
    /// <summary>
    /// The default stays --nanite no-nanite: changing it would be a blind bet on what
    /// the user wants. A warning is never wrong, and on a UE5 title whose meshes carry
    /// only Nanite LODs the current silence produces an empty mesh with no explanation.
    /// </summary>
    protected void WarnIfNaniteDataIsBeingDropped<TVertex>(MeshDto<TVertex> dto, bool hasNaniteData)
        where TVertex : struct, IMeshVertex
    {
        if (Session.Options.NaniteMeshFormat != ENaniteMeshFormat.NoNanite || !hasNaniteData) return;

        Log.Warning(
            "Mesh has Nanite data that is being skipped ({LodCount} non-Nanite LOD(s) exported). " +
            "Pass --nanite nanite-only or --nanite nanite-first to include it.",
            dto.LODs.Count);
    }
```

In `StaticMeshExporter.BuildFiles`, after the `LODs.Count == 0` check:

```csharp
        WarnIfNaniteDataIsBeingDropped(dto, originalMesh.NaniteResources is { PageStreamingStates.Length: > 0 });
```

In `SkeletalMeshExporter.BuildFiles`, the same, using that class's Nanite resource property. Confirm the exact member names first:
`grep -n "Nanite" CUE4Parse/UE4/Assets/Exports/StaticMesh/UStaticMesh.cs CUE4Parse/UE4/Assets/Exports/SkeletalMesh/USkeletalMesh.cs`
and use whatever they print. If a mesh type has no Nanite member, pass `false`.

- [ ] **Step 4: Run the test**

Run: `dotnet run --project CUE4Parse.Cli.Tests/CUE4Parse.Cli.Tests.csproj -- --filter-method "*NaniteOnlyMesh*"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add CUE4Parse-Conversion/Exporters CUE4Parse.Cli.Tests/NaniteWarningTests.cs
git commit -m "feat: warn when a mesh's Nanite data is skipped"
```

---

## Task 14: `ExportResult.ClassName` and the deterministic manifest

**Files:**
- Modify: `CUE4Parse-Conversion/ExportResult.cs`, `CUE4Parse-Conversion/Exporters/ExporterBase.cs`
- Create: `CUE4Parse.Cli/Output/ExportManifest.cs`
- Modify: `CUE4Parse.Cli/Commands/ExportCommand.cs`, `CUE4Parse.Cli/Program.cs`
- Test: `CUE4Parse.Cli.Tests/ExportManifestTests.cs`

**Interfaces:**
- Consumes: `ExportResult`, `ExportOptions`.
- Produces:
  - `ExportResult(bool Success, string ObjectPath, string ClassName, IReadOnlyList<string>? DiskFilePaths = null, Exception? Error = null)`
  - `ExportResult.Failure(string objectPath, string className, Exception ex)`
  - `ExportManifest.Build(IReadOnlyList<ExportResult> results, string outputRoot, ExportOptions options, string toolVersion) -> object`
  - `ExportManifest.Write(object manifest, FileInfo destination)`
  - `ExportCommandOptions.Manifest` (`FileInfo?`)

- [ ] **Step 1: Write the failing tests**

Create `CUE4Parse.Cli.Tests/ExportManifestTests.cs`:

```csharp
using CUE4Parse.Cli.Commands;
using CUE4Parse.Cli.Output;
using Newtonsoft.Json.Linq;

namespace CUE4Parse.Cli.Tests;

public class ExportManifestTests
{
    private static async Task<(DirectoryInfo Output, FileInfo Manifest)> ExportWithManifestAsync()
    {
        var output = Directory.CreateTempSubdirectory();
        var manifest = new FileInfo(Path.Combine(Directory.CreateTempSubdirectory().FullName, "manifest.json"));
        var (context, _) = FixtureSupport.Context();

        var code = await ExportCommand.ExecuteAsync(context, new ExportCommandOptions(
            Paths: ["CUE4ParseFixtures/Content/Fixtures/Meshes/SM_Fixture.uasset"],
            Criteria: new MatchCriteria(null, null, null, null),
            Output: output,
            Flags: ExportFlagDefaults.Gltf2(),
            Parallel: 4,
            Force: false,
            Manifest: manifest), CancellationToken.None);

        Assert.Equal((int)ExitCode.Success, code);
        return (output, manifest);
    }

    [Fact]
    public async Task ManifestRecordsClassRelativePathSizeAndHash()
    {
        var (output, manifestFile) = await ExportWithManifestAsync();
        var manifest = JObject.Parse(await File.ReadAllTextAsync(manifestFile.FullName));

        Assert.Equal(1, manifest["version"]!.Value<int>());
        var entries = (JArray)manifest["entries"]!;
        Assert.NotEmpty(entries);

        var mesh = entries.Single(e => e["class"]!.Value<string>() == "StaticMesh");
        var file = (JObject)mesh["files"]![0]!;

        var path = file["path"]!.Value<string>()!;
        Assert.DoesNotContain('\\', path);
        Assert.True(File.Exists(Path.Combine(output.FullName, path.Replace('/', Path.DirectorySeparatorChar))));
        Assert.True(file["bytes"]!.Value<long>() > 0);
        Assert.Equal(64, file["sha256"]!.Value<string>()!.Length);
    }

    [Fact]
    public async Task EntriesAreSortedByObjectPathSoTheManifestDiffs()
    {
        var (_, manifestFile) = await ExportWithManifestAsync();
        var entries = (JArray)JObject.Parse(await File.ReadAllTextAsync(manifestFile.FullName))["entries"]!;

        var paths = entries.Select(e => e["objectPath"]!.Value<string>()!).ToArray();
        Assert.Equal(paths.OrderBy(p => p, StringComparer.Ordinal), paths);
    }

    /// <summary>
    /// ExportSession runs in parallel, so NDJSON line order changes between runs and
    /// output cannot be diffed. The manifest is the ordered view — and it must be
    /// byte-identical across two runs with the same arguments.
    /// </summary>
    [Fact]
    public async Task TwoRunsWithTheSameArgumentsProduceByteIdenticalManifests()
    {
        var (_, first) = await ExportWithManifestAsync();
        var (_, second) = await ExportWithManifestAsync();

        Assert.Equal(
            await File.ReadAllBytesAsync(first.FullName),
            await File.ReadAllBytesAsync(second.FullName));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet run --project CUE4Parse.Cli.Tests/CUE4Parse.Cli.Tests.csproj -- --filter-method "*ExportManifestTests*"`
Expected: build error — `ExportCommandOptions` has no `Manifest`.

- [ ] **Step 3: Add `ClassName` to `ExportResult`**

Replace in `CUE4Parse-Conversion/ExportResult.cs`:

```csharp
public sealed record ExportResult(
    bool Success,
    string ObjectPath,
    string ClassName,
    IReadOnlyList<string>? DiskFilePaths = null,
    Exception? Error = null)
{
    public static ExportResult Failure(string objectPath, string className, Exception ex)
        => new(false, objectPath, className, null, ex);
}
```

In `CUE4Parse-Conversion/Exporters/ExporterBase.ExportAsync`, pass it through:

```csharp
            return new ExportResult(true, ObjectPath, ClassName, paths);
```
```csharp
            return ExportResult.Failure(ObjectPath, ClassName, ex);
```

The CLI cannot derive `class` itself: it only calls `session.Add` for root assets, while materials, textures, ORM images and DNA are enqueued by the library and never seen by the CLI — yet they appear in `results`. `ExporterBase.ClassName` already knew the answer; it was simply not reported.

- [ ] **Step 4: Write the manifest builder**

Create `CUE4Parse.Cli/Output/ExportManifest.cs`:

```csharp
using System.Security.Cryptography;
using CUE4Parse_Conversion;
using CUE4Parse_Conversion.Options;
using Newtonsoft.Json;

namespace CUE4Parse.Cli.Output;

/// <summary>
/// A sorted, hashed record of one export run.
/// <para>
/// Purely a CLI concern: the conversion layer reports <see cref="ExportResult"/> and
/// knows nothing about manifests. Ordering is by ordinal <c>objectPath</c> so two runs
/// with the same arguments produce byte-identical files and the output can be diffed —
/// the NDJSON stream cannot, because the session is parallel.
/// </para>
/// </summary>
public static class ExportManifest
{
    public static object Build(
        IReadOnlyList<ExportResult> results, string outputRoot, ExportOptions options, string toolVersion) => new
        {
            version = 1,
            tool = toolVersion,
            options = new
            {
                meshFormat = options.MeshFormat.ToString(),
                textureFormat = options.TextureFormat.ToString(),
                texturePlatform = options.TexturePlatform.ToString(),
                meshQuality = options.MeshQuality.ToString(),
                nanite = options.NaniteMeshFormat.ToString(),
                socketFormat = options.SocketFormat.ToString(),
                materialDepth = options.MaterialDepth.ToString(),
                textureQuality = options.TextureQuality,
                exportMaterials = options.ExportMaterials,
                allMips = options.ExportAllTextureMips,
                flipNormalY = options.FlipNormalY,
                compressionFormat = options.CompressionFormat.ToString(),
                exportMorphTargets = options.ExportMorphTargets,
                exportHdrTexturesAsHdr = options.ExportHdrTexturesAsHdr,
            },
            entries = results
                .OrderBy(result => result.ObjectPath, StringComparer.Ordinal)
                .Select(result => new
                {
                    objectPath = result.ObjectPath,
                    @class = result.ClassName,
                    status = result.Success ? "ok" : "error",
                    message = result.Error?.Message,
                    files = (result.DiskFilePaths ?? [])
                        .Select(path => Describe(path, outputRoot))
                        .OrderBy(file => file.path, StringComparer.Ordinal)
                        .ToArray(),
                })
                .ToArray(),
        };

    public static void Write(object manifest, FileInfo destination)
    {
        destination.Directory?.Create();

        // Indented and newline-terminated so a human can read a diff; the byte-identity
        // guarantee comes from the ordering above, not from the formatting.
        File.WriteAllText(destination.FullName,
            JsonConvert.SerializeObject(manifest, Formatting.Indented) + "\n");
    }

    private static FileRecord Describe(string absolutePath, string outputRoot)
    {
        var relative = Path.GetRelativePath(outputRoot, absolutePath).Replace(Path.DirectorySeparatorChar, '/');
        var bytes = File.ReadAllBytes(absolutePath);
        return new FileRecord(relative, bytes.LongLength, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
    }

    private sealed record FileRecord(string path, long bytes, string sha256);
}
```

- [ ] **Step 5: Wire `--manifest` into the export command**

In `CUE4Parse.Cli/Commands/ExportCommand.cs`, add `FileInfo? Manifest = null` as the last member of `ExportCommandOptions`, and after the results loop:

```csharp
        if (options.Manifest is { } manifestFile)
        {
            var version = typeof(ExportCommand).Assembly.GetName().Version?.ToString() ?? "0.0.0";
            ExportManifest.Write(
                ExportManifest.Build(results, options.Output.FullName, exportOptions, $"cue4 {version}"),
                manifestFile);
        }
```

In `CUE4Parse.Cli/Program.cs`:

```csharp
var manifestOpt = new Option<FileInfo?>("--manifest")
    { Description = "Write a sorted, hashed manifest of everything exported" };
```

Add it to the option list and to the options record: `Manifest: pr.GetValue(manifestOpt)`.

- [ ] **Step 6: Run the tests**

Run: `dotnet run --project CUE4Parse.Cli.Tests/CUE4Parse.Cli.Tests.csproj -- --filter-method "*ExportManifestTests*"`
Expected: PASS, including the byte-identity test. If it fails, the cause is almost always a `Dictionary` iteration or a `results` order leaking into output — find it by diffing the two files, not by relaxing the assertion.

- [ ] **Step 7: Commit**

```bash
git add CUE4Parse-Conversion/ExportResult.cs CUE4Parse-Conversion/Exporters/ExporterBase.cs CUE4Parse.Cli CUE4Parse.Cli.Tests/ExportManifestTests.cs
git commit -m "feat: --manifest writes a sorted, hashed record of an export run"
```

---

## Task 15: `info` reports native capabilities, even when mounting fails

**Files:**
- Modify: `CUE4Parse.Cli/Commands/InfoCommand.cs`, `CUE4Parse.Cli/Services/ProviderFactory.cs`
- Test: `CUE4Parse.Cli.Tests/InfoCommandTests.cs`

**Interfaces:**
- Consumes: `CUE4ParseNatives.IsInitialized`, `CUE4ParseNatives.IsFeatureAvailable`, `OodleHelper.Instance`, `GlobalOptions.Verbose`.
- Produces: `ProviderFactory.InitializeCompression()` becomes `public static`; `CommandContext.Verbose` (`bool`, default `false`); `info` output gains a `native` object and, under `--verbose`, `mountedArchives`/`unloadedArchives`.

- [ ] **Step 1: Write the failing tests**

Append to `CUE4Parse.Cli.Tests/InfoCommandTests.cs`:

```csharp
    [Fact]
    public void ExecuteReportsNativeCapabilities()
    {
        var (context, sw) = FixtureSupport.Context();

        var code = InfoCommand.Execute(context);

        Assert.Equal((int)ExitCode.Success, code);
        var native = JObject.Parse(sw.ToString())["native"];
        Assert.NotNull(native);
        Assert.NotNull(native!["library"]);
        Assert.NotNull(native["acl"]);
        Assert.Contains(native["oodle"]!.Value<string>(), new[] { "native", "downloaded", "unavailable" });
    }

    /// <summary>
    /// A diagnostic command that goes dark when there is something to diagnose is
    /// backwards. The native block does not depend on the provider, so a bad paks
    /// directory must not hide it.
    /// </summary>
    [Fact]
    public void ExecuteStillReportsNativeCapabilitiesWhenTheProviderCannotBeBuilt()
    {
        var profile = new ResolvedProfile("/definitely/not/a/paks/dir", EGame.GAME_UE5_6, null, null, new Dictionary<string, string>());
        var (context, sw) = FixtureSupport.Context(profile);

        var code = InfoCommand.Execute(context);

        Assert.Equal((int)ExitCode.Mount, code);
        var parsed = JObject.Parse(sw.ToString());
        Assert.NotNull(parsed["native"]);
        Assert.NotNull(parsed["error"]);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet run --project CUE4Parse.Cli.Tests/CUE4Parse.Cli.Tests.csproj -- --filter-method "*InfoCommandTests*"`
Expected: FAIL — `native` is null; the second test throws instead of returning 4.

- [ ] **Step 3: Make compression init callable on its own**

In `CUE4Parse.Cli/Services/ProviderFactory.cs`, change `private static void InitializeCompression()` to `public static void InitializeCompression()` and add:

```csharp
    /// <summary>
    /// Idempotent: OodleHelper returns early once an instance exists, so calling this
    /// from `info` before Create() costs nothing and lets the native block be reported
    /// even when mounting later fails.
    /// </summary>
```

- [ ] **Step 4: Rewrite `InfoCommand`**

Replace `CUE4Parse.Cli/Commands/InfoCommand.cs`:

```csharp
using CUE4Parse.Cli.Output;
using CUE4Parse.Cli.Services;
using CUE4Parse.Compression;
using CUE4Parse.Utils;

namespace CUE4Parse.Cli.Commands;

public static class InfoCommand
{
    public static int Execute(CommandContext context)
    {
        // Built before the provider: this is the one part of the report that does not
        // depend on a mountable paks directory, and it is exactly what a user needs
        // when the mount is what failed.
        ProviderFactory.InitializeCompression();
        var native = new
        {
            library = CUE4ParseNatives.IsInitialized,
            acl = CUE4ParseNatives.IsFeatureAvailable("ACL\0"u8),
            oodle = OodleState(),
        };

        try
        {
            using var provider = ProviderFactory.Create(context.Profile);

            context.Output.WriteResult(new
            {
                game = context.Profile.Game.ToString(),
                paksDir = context.Profile.PaksDir,
                mappings = context.Profile.Mappings,
                mountedVfs = provider.MountedVfs.Count,
                unloadedVfs = provider.UnloadedVfs.Count,
                fileCount = provider.Files.Count,
                missingAesGuids = provider.RequiredKeys.Select(g => g.ToString()).ToArray(),
                native,
            }, indent: true);

            return (int)ExitCode.Success;
        }
        catch (Exception ex)
        {
            // The one place the "errors print only an error object" rule is relaxed:
            // for `info`, the diagnosis *is* the product.
            var error = ErrorClassifier.Classify(ex);
            context.Output.WriteResult(new
            {
                native,
                error = new { code = error.Code, message = error.Message },
            }, indent: true);

            return (int)error.ExitCode;
        }
    }

    /// <summary>
    /// Three states, not a boolean. OodleHelper falls back to downloading oo2core when
    /// the native library was built without Oodle, so "no native Oodle" does not mean
    /// "no Oodle" — and the downloaded path needs network access, which is what breaks
    /// on an offline CI runner. A boolean carrying three meanings is exactly what makes
    /// an agent parsing this output reason wrongly with no way to tell.
    /// </summary>
    private static string OodleState()
    {
        if (CUE4ParseNatives.IsFeatureAvailable("Oodle\0"u8)) return "native";
        return OodleHelper.Instance is not null ? "downloaded" : "unavailable";
    }
}
```

Confirm the namespace of `CUE4ParseNatives` with `grep -rn "class CUE4ParseNatives" CUE4Parse/` and adjust the `using` if it differs.

- [ ] **Step 5: List the mounted archives under `--verbose`**

The archive-count discrepancy against FModel (212 vs 167 + 7 loose files, plus an unexplained `unloadedVfs: 1`) cannot be settled by staring at aggregate numbers. `--verbose` should name them, so the comparison is concrete. Note this is a convenience, not the method: per spec §2 the authoritative way to explain a difference against FModel is `git diff <the CUE4Parse version FModel 4.4.4 pins>..HEAD`, not black-box observation.

Extend `CommandContext` so `info` can see the verbose flag. `GlobalOptions.Verbose` is already parsed; add it to `CommandContext`:

```csharp
    public CommandContext(Lazy<ResolvedProfile> profile, JsonOutput output, bool verbose = false)
    {
        _profile = profile;
        Output = output;
        Verbose = verbose;
    }

    public bool Verbose { get; }
```

Keep the existing `(ResolvedProfile, JsonOutput)` overload delegating with `verbose: false` so every current caller and test compiles unchanged, and pass the flag in `ContextBuilder.Build`:

```csharp
    public static CommandContext Build(ParseResult parseResult) =>
        new(new Lazy<ResolvedProfile>(() => ResolveProfile(parseResult)),
            new JsonOutput(Console.Out),
            parseResult.GetValue(GlobalOptions.Verbose));
```

In `InfoCommand`, inside the successful branch, add the two archive arrays to the anonymous object:

```csharp
                mountedArchives = context.Verbose
                    ? provider.MountedVfs.Select(vfs => vfs.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray()
                    : null,
                unloadedArchives = context.Verbose
                    ? provider.UnloadedVfs.Select(vfs => vfs.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray()
                    : null,
```

`JsonOutput` ignores nulls, so the two keys simply do not appear without `--verbose`. Confirm the member that names an archive with
`grep -n "string Name" CUE4Parse/UE4/VirtualFileSystem/IAesVfsReader.cs CUE4Parse/UE4/VirtualFileSystem/AbstractVfsReader.cs`
and use whatever it reports.

Add the test:

```csharp
    [Fact]
    public void VerboseListsEveryMountedArchiveByName()
    {
        var sw = new StringWriter();
        var context = new CommandContext(new Lazy<ResolvedProfile>(FixtureSupport.Profile()), new JsonOutput(sw), verbose: true);

        InfoCommand.Execute(context);

        var parsed = JObject.Parse(sw.ToString());
        var archives = (JArray)parsed["mountedArchives"]!;
        Assert.Equal(parsed["mountedVfs"]!.Value<int>(), archives.Count);
        Assert.All(archives, name => Assert.False(string.IsNullOrWhiteSpace(name.Value<string>())));
    }
```

- [ ] **Step 6: Run the tests**

Run: `dotnet run --project CUE4Parse.Cli.Tests/CUE4Parse.Cli.Tests.csproj -- --filter-method "*InfoCommandTests*"`
Expected: PASS, including the pre-existing `ExecuteReportsGameVersionAndMountCountsAsJson`. If a non-existent paks directory does not raise a `Mount`-classified error, check what `ProviderFactory.Create` throws and make sure `ErrorClassifier` maps it to `ExitCode.Mount`; the contract for a bad paks directory is exit 4.

- [ ] **Step 7: Commit**

```bash
git add CUE4Parse.Cli CUE4Parse.Cli.Tests/InfoCommandTests.cs
git commit -m "feat: cue4 info reports native ACL/Oodle state, lists archives, survives a failed mount"
```

---

# Phase 3 — Stop going back to FModel to *extract*

> **Re-scoped after the design review.** The original heading claimed FModel would be
> replaced outright. It will not be, and §7.4 of the spec already conceded why: `.wem` and
> `.binka` come out as raw bytes that still need vgmstream, because CUE4Parse has no codec
> and FModel bundles one. What this phase actually delivers is that **extraction** no longer
> requires FModel.
>
> Two of the original acceptance criteria were also unreachable and have been rewritten
> (spec §12): there is no Wwise fixture and no ACL-compressed animation fixture, and neither
> can be created (spec §9.1). Audio is tested against BinkAudio, which the fixtures do cook;
> ACL is verified through `info` reporting only.
>
> This phase is **not** part of this round's target — see the plan header. Decide on it after
> Phase 1 output has been used for real work.

## Task 16: `SoundExporter`

**Files:**
- Create: `CUE4Parse-Conversion/Exporters/SoundExporter.cs`
- Modify: `CUE4Parse-Conversion/ExportSession.cs`
- Test: `CUE4Parse.Cli.Tests/SoundExporterTests.cs`

**Interfaces:**
- Consumes: `SoundDecoder.Decode(this UObject, bool, out string, out byte[]?)`.
- Produces: `public sealed class SoundExporter(UObject sound) : ExporterBase(sound)`; `ExportSession.Add` dispatches `USoundWave`, `USoundNodeWave` and `UAkMediaAssetData` to it.

- [ ] **Step 1: Write the failing tests**

Create `CUE4Parse.Cli.Tests/SoundExporterTests.cs`:

```csharp
namespace CUE4Parse.Cli.Tests;

public class SoundExporterTests
{
    /// <summary>
    /// Magic-byte assertions, not "the file is non-empty": the failures worth catching
    /// are picking the wrong chunk, a streaming-offset slip, and a mislabelled format —
    /// all of which produce a plausible-sized file with a wrong header.
    /// </summary>
    [Theory]
    [InlineData("SW_Format_BinkAudio", "binka", new byte[] { (byte)'B', (byte)'C', (byte)'F' })]
    [InlineData("SW_Format_PlatformSpecific", "ogg", new byte[] { (byte)'O', (byte)'g', (byte)'g', (byte)'S' })]
    public async Task ExportedAudioCarriesTheExpectedContainerMagic(string asset, string extension, byte[] magic)
    {
        var output = await GltfWriterTests.ExportAsync(
            $"CUE4ParseFixtures/Content/Fixtures/Audio/{asset}.uasset");

        var file = Directory
            .GetFiles(output.FullName, $"*.{extension}", SearchOption.AllDirectories)
            .Single();

        var head = new byte[magic.Length];
        await using (var stream = File.OpenRead(file)) _ = await stream.ReadAsync(head);

        Assert.Equal(magic, head);
    }

    [Fact]
    public async Task PcmSoundsComeOutAsRiffWave()
    {
        var output = await GltfWriterTests.ExportAsync(
            "CUE4ParseFixtures/Content/Fixtures/Audio/SW_Format_ADPCM.uasset");

        var file = Directory.GetFiles(output.FullName, "*.wav", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(output.FullName, "*.adpcm", SearchOption.AllDirectories))
            .Single();

        var head = await File.ReadAllBytesAsync(file);
        Assert.True(head.Length > 12);
        Assert.Equal("RIFF"u8.ToArray(), head[..4]);
    }

    [Fact]
    public async Task SoundCuesAreNotDispatchedToTheSoundExporter()
    {
        // A USoundCue is a node graph, not audio data. It keeps falling through to
        // `dump`, and export must report it as skipped rather than writing a bogus file.
        var output = await GltfWriterTests.ExportAsync(
            "CUE4ParseFixtures/Content/Fixtures/Audio/SC_Fixture.uasset");

        Assert.Empty(Directory.GetFiles(output.FullName, "*.wem", SearchOption.AllDirectories));
    }
}
```

Confirm the real BinkAudio magic before running: `xxd` the exported file, or read `CUE4Parse.Tests/Fixtures/UE5_8/Tests/FixtureAudioTests.cs`. If the first bytes are not `BCF`, use whatever the fixture actually starts with — the point of the test is that the header is *the right one*, not that it matches a guess.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet run --project CUE4Parse.Cli.Tests/CUE4Parse.Cli.Tests.csproj -- --filter-method "*SoundExporterTests*"`
Expected: FAIL — `ExportCommand` reports `status: skipped, reason: no exporter for this type` and nothing is written.

- [ ] **Step 3: Write the exporter**

Create `CUE4Parse-Conversion/Exporters/SoundExporter.cs`:

```csharp
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse_Conversion.Sounds;

namespace CUE4Parse_Conversion.Exporters;

/// <summary>
/// Writes a sound asset's audio bytes, with the extension the decoder reports.
/// <para>
/// cue4 extracts, it does not transcode. <c>SoundDecoder.Decompress</c> decompresses
/// nothing: it relabels PCM as <c>.wav</c> and rejects formats it does not recognise.
/// Wwise assets therefore come out as raw <c>.wem</c> and Bink as raw <c>.binka</c> —
/// the same bytes FModel extracts. FModel can play them because it bundles vgmstream;
/// CUE4Parse has no codec, so turning those into playable audio is a downstream step.
/// <c>OGG</c> is already a final format and plays as-is.
/// </para>
/// <para>
/// <c>shouldDecompress</c> is fixed at true. The false path only skips a header check
/// and a relabel, so a flag would promise far more than it delivers.
/// </para>
/// </summary>
public sealed class SoundExporter(UObject sound) : ExporterBase(sound)
{
    protected override IReadOnlyList<ExportFile> BuildExportFiles(CancellationToken ct = default)
    {
        sound.Decode(shouldDecompress: true, out var audioFormat, out var data);

        if (data is not { Length: > 0 })
        {
            throw new Exception($"Sound '{ObjectName}' produced no audio data (format: '{audioFormat}')");
        }

        Log.Debug("Extracted {Bytes} bytes of {Format} audio", data.Length, audioFormat);
        return [new ExportFile(audioFormat.ToLowerInvariant(), data)];
    }
}
```

- [ ] **Step 4: Dispatch sound assets in the session**

In `CUE4Parse-Conversion/ExportSession.cs`, add to the `Add(UObject)` switch, **above** the `_ => throw` arm:

```csharp
            USoundWave or USoundNodeWave or UAkMediaAssetData => Add(new SoundExporter(export)),
```

Add the usings `CUE4Parse.UE4.Assets.Exports.Sound;`, `CUE4Parse.UE4.Assets.Exports.Sound.Node;` and `CUE4Parse.UE4.Assets.Exports.Wwise;`. Do **not** add `USoundCue`.

- [ ] **Step 5: Run the tests**

Run: `dotnet run --project CUE4Parse.Cli.Tests/CUE4Parse.Cli.Tests.csproj -- --filter-method "*SoundExporterTests*"`
Expected: PASS.

- [ ] **Step 6: Record the Wwise gap**

The fixture set has no `UAkMediaAssetData` asset, so spec acceptance criterion 9 (a Wwise asset exporting to a `RIFF`-magic `.wem` of the right size) **cannot be verified in CI**. Add to `docs/cue4-output-contract.md` under a "Verification gaps" heading:

```markdown
### Verification gaps

- **Wwise `.wem` export is not covered by CI.** The redistributable UE5_8 fixture set
  contains no `UAkMediaAssetData`. The code path is the same one the covered formats
  use (`SoundDecoder.Decode` → raw bytes → `ExportFile`), but the assertion "a Wwise
  asset exports to a `.wem` starting with `RIFF` whose size matches the source chunks"
  has only been checked by hand against a real Wwise title. Re-check it by hand when
  touching `SoundExporter` or `SoundDecoder`.
```

- [ ] **Step 7: Commit**

```bash
git add CUE4Parse-Conversion CUE4Parse.Cli.Tests/SoundExporterTests.cs docs/cue4-output-contract.md
git commit -m "feat: export sound assets as raw audio bytes"
```

---

## Task 17: Characterize the BC interpolants exhaustively, and report the inconsistency upstream

**Files:**
- Test: `CUE4Parse.Tests/BCDecoderTests.cs`
- Create: `docs/reports/bc-interpolant-rounding.md` (the upstream issue text)
- Modify: `CUE4Parse-Conversion/Textures/BC/BCDecoder.cs` — **test accessors only** (Step 2's two `internal` wrappers). **No change to any decoding expression.**

**Interfaces:**
- Consumes: `BCDecoder.DecodeBCColors`.
- Produces: no API change. `ReadColorsBC1` and `ReadColorsBC3` are `private`, so the test drives them through the `internal`/`public` surface — see Step 2.

> **This task no longer changes behaviour.** Spec §6.2 was cut from this round. The finding
> is real — BC4/BC5 round via their `+3`/`+2` terms while BC1/BC2/BC3 truncate, and that
> asymmetry is an unintended consequence of upstream's `ea938ba8` — but fixing it here is
> wrong on three counts: it fixes no Blender problem, it changes the bytes of every texture
> for every CUE4Parse consumer, and the decision belongs to the upstream that made the
> commit. So the exhaustive test stays and **pins the current behaviour**; the fix goes
> upstream as an issue.
>
> A characterization test is not a weaker test. It is the thing that turns "upstream changed
> the decoder" from something you discover in a render months later into a red build.

- [ ] **Step 1: Write the failing tests**

Create `CUE4Parse.Tests/BCDecoderTests.cs`:

```csharp
using CUE4Parse_Conversion.Textures.BC;

namespace CUE4Parse.Tests;

/// <summary>
/// Exhaustive over the real endpoint space, not over 2^32 packed words: BC1/BC3 colour
/// endpoints are 5/6/5 bits, so 1024 red pairs, 4096 green pairs and 1024 blue pairs
/// cover every value the decoder can ever see. BC4/BC5 endpoints are 8 bits: 65536 pairs.
/// <para>
/// The threshold is **zero** — but zero against <em>what the decoder does today</em>, not
/// against exact arithmetic. These tests characterize: BC1/BC2/BC3 truncate, BC4/BC5 round.
/// That asymmetry is a known defect reported upstream (docs/reports/bc-interpolant-rounding.md),
/// deliberately not fixed here. When upstream changes the formula these go red, which is
/// the entire point of pinning it.
/// </para>
/// <para>
/// "Within 1" would be useless either way: it is a threshold both the truncating and the
/// rounding formula pass, i.e. a test that cannot tell one from the other.
/// </para>
/// </summary>
public class BCDecoderTests
{
    private static byte Expand5(int value) => (byte)((value << 3) | (value >> 2));
    private static byte Expand6(int value) => (byte)((value << 2) | (value >> 4));

    // What BC1/BC2/BC3 do today: truncate. Not what they ought to do — see the class
    // summary. BC4/BC5 use the rounding form below instead.
    private static byte Interpolate2To1(int a, int b) => (byte)((2 * a + b) / 3);
    private static byte Interpolate1To2(int a, int b) => (byte)((a + 2 * b) / 3);

    // The rounding form: the historical DXTDecoder DXT3/DXT5 formula, still used by BC4/BC5.
    private static byte Interpolate2To1Rounded(int a, int b) => (byte)((2 * a + b + 1) / 3);
    private static byte Interpolate1To2Rounded(int a, int b) => (byte)((a + 2 * b + 1) / 3);

    [Fact]
    public void Bc1RedAndBlueInterpolantsTruncate()
    {
        for (var e0 = 0; e0 < 32; e0++)
        for (var e1 = 0; e1 < 32; e1++)
        {
            var c0 = Expand5(e0);
            var c1 = Expand5(e1);

            var (two, one) = BCDecoderProbe.Bc1RedInterpolants(e0, e1);

            Assert.Equal(Interpolate2To1(c0, c1), two);
            Assert.Equal(Interpolate1To2(c0, c1), one);
        }
    }

    [Fact]
    public void Bc1GreenInterpolantsTruncate()
    {
        for (var e0 = 0; e0 < 64; e0++)
        for (var e1 = 0; e1 < 64; e1++)
        {
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
            Assert.Equal(BCDecoderProbe.Bc1RedInterpolants(e0, e1), BCDecoderProbe.Bc3RedInterpolants(e0, e1));
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

    [Fact]
    public void Bc4And5AlphaInterpolantsAreExactlyRounded()
    {
        for (var c0 = 0; c0 < 256; c0++)
        for (var c1 = 0; c1 < 256; c1++)
        {
            var decoded = BCDecoder.DecodeBCColors((ulong)c0 | ((ulong)c1 << 8));

            Assert.Equal((byte)c0, (byte)decoded);
            Assert.Equal((byte)c1, (byte)(decoded >> 8));

            if (c0 > c1)
            {
                for (var i = 1; i <= 6; i++)
                {
                    var expected = (byte)(((7 - i) * c0 + i * c1 + 3) / 7);
                    Assert.Equal(expected, (byte)(decoded >> (8 * (i + 1))));
                }
            }
            else
            {
                for (var i = 1; i <= 4; i++)
                {
                    var expected = (byte)(((5 - i) * c0 + i * c1 + 2) / 5);
                    Assert.Equal(expected, (byte)(decoded >> (8 * (i + 1))));
                }

                Assert.Equal(0, (byte)(decoded >> 48));
                Assert.Equal(255, (byte)(decoded >> 56));
            }
        }
    }
}
```

- [ ] **Step 2: Expose the two private readers to the test**

Add to the bottom of `CUE4Parse-Conversion/Textures/BC/BCDecoder.cs`, inside the same namespace:

```csharp
/// <summary>
/// Test-only window onto the two private colour-block readers. The exhaustive
/// endpoint tests are the only thing standing between this file and a silent
/// half-LSB shift across every DXT texture the tool has ever written, so they
/// need to see the interpolants directly rather than through a whole image decode.
/// </summary>
public static class BCDecoderProbe
{
    public static (byte Two, byte One) Bc1RedInterpolants(int e0, int e1) => Channel(Bc1(e0, e1), 0);
    public static (byte Two, byte One) Bc1GreenInterpolants(int e0, int e1) => Channel(Bc1Green(e0, e1), 1);
    public static (byte Two, byte One) Bc3RedInterpolants(int e0, int e1) => Channel(Bc3(e0, e1), 0);

    private static uint[] Bc1(int e0, int e1) => Read(BCDecoder.ReadColorsBC1Internal, Pack(e0 << 11, e1 << 11));
    private static uint[] Bc1Green(int e0, int e1) => Read(BCDecoder.ReadColorsBC1Internal, Pack(e0 << 5, e1 << 5));
    private static uint[] Bc3(int e0, int e1) => Read(BCDecoder.ReadColorsBC3Internal, Pack(e0 << 11, e1 << 11));

    // BC1 only interpolates when c0 > c1; force that by making c0 the larger word.
    private static uint Pack(int c0, int c1) => c0 > c1
        ? (uint)c0 | ((uint)c1 << 16)
        : (uint)c1 | ((uint)c0 << 16);

    private static uint[] Read(Action<uint, Span<uint>> reader, uint data)
    {
        var colors = new uint[4];
        reader(data, colors);
        return colors;
    }

    private static (byte, byte) Channel(uint[] colors, int shiftIndex)
    {
        var shift = shiftIndex * 8;
        return ((byte)(colors[2] >> shift), (byte)(colors[3] >> shift));
    }
}
```

and expose the readers next to their private definitions:

```csharp
    internal static void ReadColorsBC1Internal(uint data, Span<uint> op) => ReadColorsBC1(data, op);
    internal static void ReadColorsBC3Internal(uint data, Span<uint> op) => ReadColorsBC3(data, op);
```

Change `BCDecoderProbe`'s two `BCDecoder.ReadColors*Internal` references to work with `internal` visibility by keeping `BCDecoderProbe` inside `CUE4Parse-Conversion` (it is), and marking the class `public` as written. `CUE4Parse.Tests` already project-references `CUE4Parse-Conversion`.

**Note on `Pack`:** the packed word's low 16 bits are `c0` and the high 16 bits are `c1`, with the 5/6/5 layout `rrrrrggggggbbbbb`. `e0 << 11` puts a 5-bit value in the red field; `e0 << 5` puts a 6-bit value in the green field. Verify this against `ReadColorsBC1`'s masks (`0xF81F` for red+blue, `0x07E007E0` for green) before running — if the ordering is reversed, fix `Pack`, not the assertions.

- [ ] **Step 3: Run the tests to verify they pass**

Run: `dotnet test CUE4Parse.Tests/CUE4Parse.Tests.csproj -c Release`
Expected: all five BC tests PASS immediately. These characterize what the decoder already does, so a failure here means the decoder is not what this task believes it is — investigate before touching anything, and do **not** adjust the expectation to make it green.

- [ ] **Step 4: Write the upstream report**

Create `docs/reports/bc-interpolant-rounding.md`. This is issue text for `FabianFG/CUE4Parse`, not a design note — keep it short enough that a maintainer reads all of it:

1. **Observation.** BC4/BC5 round their interpolants (the `+3` and `+2` terms in `DecodeBCColors`); BC1/BC2/BC3 truncate (`(x * 683) >> 11`). Mean error is 0 for the former and −1/3 LSB for the latter.
2. **Provenance.** Commit `ea938ba8` ("Optimize BC1–BC5 decoders for 3–5× speedup", 04/08/2026) replaced `DXTDecoder` with `BCDecoder`. The old code used `(2*c0 + c1) / 3` for DXT1 but `(2*c0 + c1 + 1) / 3` for DXT3/DXT5. The new shared expression truncates for all three, so **BC3 silently changed from rounding to truncating** in a commit whose stated purpose was speed. Every DXT3/DXT5 texture decoded since then differs from the pre-commit output.
3. **The trap, if you fix it.** The obvious patch — add `+1` before dividing by three — is a **no-op on green**. Green is kept pre-shifted 8 bits (`g0 = g & 0xFF00`), which is why it divides with `>>19` rather than `>>11`, so `2*g0 + g1` is always a multiple of 256. Adding 1 contributes 683 to a quantity `>>19` quantizes at 524288; it will essentially never change the result. Green needs **`+256`**. A `+1`-everywhere patch rounds R and B, leaves G truncating, and introduces a *new* channel inconsistency worse than today's uniform truncation. The correct form:

   ```csharp
   Unsafe.Add(ref dst, 2) = ((2 * r0 + r1 + 1) * 683) >> 11
                          | ((((2 * g0 + g1 + 256) * 683) >> 19) << 8)
                          | ((((2 * b0 + b1 + 1) * 683) >> 11) << 16)
                          | 0xFF000000;
   ```

   The identity `(x * 683) >> 11 == floor(x/3)` holds on `[0, 765]`; `+1` pushes the max to 766 and `766 * 683 >> 11 = 255 = floor(766/3)`, so the range survives.
4. **Why we are not sending a PR.** Changing it alters the bytes of every BC1/BC2/BC3 texture for every consumer. That is upstream's call, not ours. `CUE4Parse.Tests/BCDecoderTests.cs` pins the current behaviour so the change is visible whenever it happens.

- [ ] **Step 5: File it**

Open the issue against `FabianFG/CUE4Parse` with the contents of that file. Record the issue URL in the report's header so the test's failure message can point at a real discussion.

- [ ] **Step 6: Commit**

```bash
git add CUE4Parse.Tests/BCDecoderTests.cs docs/reports/bc-interpolant-rounding.md
git commit -m "test: characterize BC1-BC5 interpolant rounding and report the asymmetry upstream"
```

---

## Task 18: ACL native build, verified through `info`

**Files:**
- Test: `CUE4Parse.Cli.Tests/NativeCapabilityTests.cs`
- Modify: `docs/cue4-output-contract.md` (verification gaps), `CLAUDE.md`

**Interfaces:**
- Consumes: `InfoCommand`'s `native` block from Task 15.
- Produces: nothing new.

> **Reporting only — there is no ACL animation to export.** ACL compression requires a UE
> plugin, and the fixture set contains no ACL-compressed animation (spec §9.1). Acceptance
> criterion 12 was reduced accordingly: it asserts that `info` tells the truth about the
> native library, not that an ACL animation round-trips. That is a real limit, not a
> formality — this task can prove the plumbing is honest and cannot prove decompression
> works. Record it in the contract's verification-gaps section rather than leaving a reader
> to assume otherwise.

- [ ] **Step 1: Build the natives with the ACL submodule present**

```bash
git submodule update --init --recursive
CUE4PARSE_SKIP_NATIVE= dotnet build -c Release
```

Expected: `CUE4Parse-Natives.{dll,so}` lands in the output directory. The CMake step is non-fatal by design, so read the build log: a warning here means ACL support silently degraded rather than the build failing.

- [ ] **Step 2: Confirm through the CLI**

```bash
dotnet run --project CUE4Parse.Cli/CUE4Parse.Cli.csproj -- info \
  --paks CUE4Parse.Tests/Fixtures/UE5_8/LegacyPak/Unversioned/Oodle \
  --game 5.8 \
  --mappings CUE4Parse.Tests/Fixtures/UE5_8/Mappings/CUE4ParseFixtures-Oodle.usmap
```

Expected: `"native": { "library": true, "acl": true, "oodle": "native" | "downloaded" }`.

- [ ] **Step 3: Write the test that locks the reporting in**

Create `CUE4Parse.Cli.Tests/NativeCapabilityTests.cs`:

```csharp
using CUE4Parse.Cli.Commands;
using Newtonsoft.Json.Linq;

namespace CUE4Parse.Cli.Tests;

public class NativeCapabilityTests
{
    /// <summary>
    /// Asserts the *reporting* is truthful, not that ACL is present: the native library
    /// is built by a non-fatal CMake step, so a runner without CMake legitimately gets
    /// acl:false. What must never happen is acl:true while the feature is absent.
    /// </summary>
    [Fact]
    public void AclReportingMatchesTheNativeLibraryState()
    {
        var (context, sw) = FixtureSupport.Context();
        InfoCommand.Execute(context);

        var native = JObject.Parse(sw.ToString())["native"]!;
        var acl = native["acl"]!.Value<bool>();
        var library = native["library"]!.Value<bool>();

        Assert.True(library || !acl, "acl cannot be true when no native library is loaded.");
        Assert.Equal(CUE4Parse.Utils.CUE4ParseNatives.IsFeatureAvailable("ACL\0"u8), acl);
    }
}
```

Adjust the `CUE4ParseNatives` namespace to whatever `grep -rn "class CUE4ParseNatives" CUE4Parse/` reports.

- [ ] **Step 4: Run the test**

Run: `dotnet run --project CUE4Parse.Cli.Tests/CUE4Parse.Cli.Tests.csproj -- --filter-method "*AclReporting*"`
Expected: PASS.

- [ ] **Step 5: Record the ACL fixture gap**

The redistributable fixture set has no ACL-compressed animation, and one cannot be added without redistributing a game asset. Append to the "Verification gaps" section of `docs/cue4-output-contract.md`:

```markdown
- **ACL-compressed animation decode is not covered by CI.** `cue4 info` reports whether
  the ACL feature is compiled in, and that reporting is tested; decoding an actual
  ACL-compressed animation is not, because the fixture set contains none and game assets
  are not redistributable. Verify by hand against a real ACL title when touching
  animation decompression.
```

- [ ] **Step 6: Commit**

```bash
git add CUE4Parse.Cli.Tests/NativeCapabilityTests.cs docs/cue4-output-contract.md
git commit -m "test: cue4 info reports ACL availability truthfully"
```

---

## Task 19: Documentation and the parity tooling

**Files:**
- Create: `docs/cue4-guide.md`, `tools/parity/PixDiff.ps1`, `tools/parity/glbcmp.py`, `tools/parity/GlbInfo.ps1`, `tools/parity/BinDiff.ps1`
- Modify: `docs/reports/2026-08-16-cue4-cli-vs-fmodel-parity.md`, `CLAUDE.md`
- Test: manual — the docs are the deliverable.

**Interfaces:**
- Consumes: everything above.
- Produces: nothing executable.

- [ ] **Step 1: Move the guide into the repo**

Copy `C:\tools\cue4_guide.txt` to `docs/cue4-guide.md`, convert it to Markdown, and:
- Delete the §10 claim that the tool is untried on retail builds — there is now evidence to the contrary.
- Document the flags added here: `--compression-format`, `--no-morph-targets`, `--no-hdr` (and that it is a no-op with `gltf2`), `--flip-normal-y`, `--manifest`.
- Link to `docs/cue4-output-contract.md` as the authority on output shape.

- [ ] **Step 2: Reframe the parity report**

In `docs/reports/2026-08-16-cue4-cli-vs-fmodel-parity.md`:
- **Keep item D1 unchanged** — it is correct. `ea938ba8` really did change BC3 from rounding to truncation, which is exactly the −1 difference the report measured.
- Rewrite the framing: FModel 4.4.4 is not an independent implementation to compare against, it is a pinned older CUE4Parse. Every measured difference is a delta between two versions of this repo.
- Replace any "which side is right" table with the method that follows from that: find the CUE4Parse version FModel 4.4.4 pins, then `git diff <that version>..HEAD` over the relevant paths. Do this for the unexplained rows — `COLOR_0`, and the 212 vs 167 + 7 loose-file archive count with `unloadedVfs: 1`.
- Record the pinned version once you have found it, so the next reader does not repeat the search.

- [ ] **Step 3: Move the parity scripts into the repo**

Copy the four scripts out of the scratchpad into `tools/parity/`: `PixDiff.ps1` (signed per-channel histogram), `glbcmp.py` (per-primitive vertex attribute comparison with joint remapping by name), `GlbInfo.ps1` (splits the JSON and BIN chunks), `BinDiff.ps1` (locates differing byte ranges). Add `tools/parity/README.md` with one paragraph per script: what it answers and the exact command line.

- [ ] **Step 4: Commit a manifest baseline**

```bash
dotnet run --project CUE4Parse.Cli/CUE4Parse.Cli.csproj -- export "CUE4ParseFixtures/Content/Fixtures/Meshes/**" \
  --paks CUE4Parse.Tests/Fixtures/UE5_8/LegacyPak/Unversioned/Oodle --game 5.8 \
  --mappings CUE4Parse.Tests/Fixtures/UE5_8/Mappings/CUE4ParseFixtures-Oodle.usmap \
  --mesh-format gltf2 -o /tmp/baseline --manifest tools/parity/baseline-UE5_8.json
```

Commit `tools/parity/baseline-UE5_8.json`. A future change that alters output shows up as a diff on this file rather than as a vague suspicion.

- [ ] **Step 5: Update `CLAUDE.md`**

Add to the Conversion layer section:
- `GltfMaterialBinder` binds materials for glTF; textures are **referenced**, never embedded; `OrmTextureExporter` writes the channel-swapped sibling glTF needs.
- `TextureFileNamer` is the single predictor of texture file names — never hard-code `.png`.
- `MeshExportContext` is what mesh writers receive; add a field there rather than a parameter to `IMeshExportFormat`.
- The output contract lives in [docs/cue4-output-contract.md](docs/cue4-output-contract.md) and is a promise; changing it means changing that file first.
- `tools/parity/` holds the comparison scripts and the committed manifest baseline.
- CI: `.github/workflows/cli-tests.yml` runs `CUE4Parse.Cli.Tests` plus the pinned glTF-Validator. `tests.yml` is upstream's and must not be edited.

- [ ] **Step 6: Run the whole suite one last time**

```bash
dotnet build -c Release
dotnet test CUE4Parse.Tests/CUE4Parse.Tests.csproj -c Release
dotnet test CUE4Parse.Cli.Tests/CUE4Parse.Cli.Tests.csproj -c Release
```

Expected: green, with the glTF-Validator tests running rather than skipped when `GLTF_VALIDATOR` is set.

- [ ] **Step 7: Commit**

```bash
git add docs tools CLAUDE.md
git commit -m "docs: output contract, guide, parity tooling and the reframed parity report"
```

---

## Acceptance criteria → task map

| Spec §12 criterion | Verified by |
|---|---|
| 1. `.glb` has `images`/`textures`, every URI resolves | Task 6 `ExportedGlbCarriesImagesAndTextures`, `EveryImageUriResolvesToAFileThatThisRunActuallyWrote` |
| 2. glTF-Validator reports no errors | Task 9 `ExportedGlbHasNoValidatorErrors` |
| 3. `\|NORMAL\| = 1` within 1e-6 | Task 8 `EveryNormalIsAUnitVector` |
| 4. `BLEND_Masked` → `alphaMode: MASK` with the material's `OpacityMaskClipValue` | Task 5 `BlendModeMapsToTheGltfAlphaMode` + `OpacityMaskClipValue` walk |
| 5. `metallicRoughnessTexture` is the repacked ORM, G = source B | Task 7 `ExportedOrmImageHasGreenFromTheSourceBlue…` |
| 6. Two runs produce byte-identical manifests | Task 14 `TwoRunsWithTheSameArgumentsProduceByteIdenticalManifests` |
| 7. Export works on Linux, no `\` in file names | Task 2 + the `cli-tests-linux` job (Task 1) |
| 8. `info` reports `acl`, and still prints `native` with exit 4 on a bad paks dir | Task 15 `ExecuteStillReportsNativeCapabilities…`, Task 18 `AclReportingMatchesTheNativeLibraryState`. **Partial:** decoding a real ACL animation is not covered — see Verification gaps. |
| 9. A Wwise asset exports as `RIFF`-magic `.wem` | **Not covered in CI** — no Wwise fixture exists. Adjacent formats are covered by Task 16; the gap is documented. |
| 10. Exhaustive BC tests, deviation = 0 | Task 17 |
| 11. Morph deltas unnormalized, in the normal slot | Task 8 `MorphTargetDeltasAreNotNormalizedAndSitInTheNormalSlot` |
| 12. The contract suffices to write a bpy script | Task 10, checked by writing `docs/examples/import_glb.py` |
