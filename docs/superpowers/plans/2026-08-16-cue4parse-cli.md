# CUE4Parse CLI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build `cue4.exe`, a console application exposing CUE4Parse's asset search, JSON dumping, mesh/texture/animation export, and raw extraction as machine-readable commands an AI agent can drive.

**Architecture:** A new `CUE4Parse.Cli` project project-references `CUE4Parse` and `CUE4Parse-Conversion`. `Program.cs` performs argument parsing only; each verb is a plain class with an `Execute` method taking a parsed options record, making commands unit-testable without a parser. A single `ProviderFactory` owns provider construction so no command can get the bootstrap sequence wrong. All structured output goes to stdout; all logging goes to stderr.

**Tech Stack:** .NET 10, System.CommandLine 2.0.11, Newtonsoft.Json, Serilog, xunit.v3.

**Spec:** [docs/superpowers/specs/2026-08-16-cue4parse-cli-design.md](../specs/2026-08-16-cue4parse-cli-design.md)

## Global Constraints

- Target framework is `net10.0`, inherited from `Directory.Build.props`. Do not add a `<TargetFramework>` to any new csproj.
- NuGet versions are centrally managed. `PackageReference` entries carry **no** `Version` attribute; add a `PackageVersion` to `Directory.Packages.props` instead.
- Nullable reference types and implicit usings are enabled solution-wide.
- All JSON uses **Newtonsoft.Json**, never `System.Text.Json`. CUE4Parse ships Newtonsoft converters for `UObject`; mixing stacks would break export serialization.
- **stdout carries structured data only.** All logging, warnings, and progress go to stderr via Serilog.
- Never build JSON with string interpolation. Verified during design: a `System.CommandLine` validation message contains literal newlines and tabs, which produce invalid JSON when interpolated. Always serialize.
- Assembly name is `cue4`, so the produced executable is `cue4.exe`.
- Exit codes: `0` success, `1` unclassified, `2` usage, `3` config, `4` mount, `5` AES key, `6` mappings, `7` not found, `8` partial export failure.
- The safety limit is **1000** matched assets for `dump`, `export`, and `unpack`, measured against the **true** match count, never the truncated one. `--force` overrides, and an explicit `--limit` counts as consent. `list` is exempt.
- Exit code **8 means an item failed.** Object types with no exporter are reported as `skipped` and leave the exit code at 0.
- Set `CUE4PARSE_SKIP_NATIVE=true` when iterating on managed code to skip the CMake step. CMake is on PATH at `C:\Workspace\.tools\cmake-3.31.6-windows-x86_64\bin`.
- Run a single test with:
  `dotnet run --project CUE4Parse.Cli.Tests/CUE4Parse.Cli.Tests.csproj -- --filter-method "*TestName*"`

### Verified System.CommandLine 2.0.11 facts

These were confirmed by compiling and running against the real package. Do not deviate.

- Aliases are constructor parameters: `new Option<bool>("--verbose", "-v")`.
- Options use object-initializer properties: `Description`, `DefaultValueFactory = _ => value`, `AllowMultipleArgumentsPerToken`, `Recursive`.
- **Global options are NOT inherited by subcommands unless `Recursive = true` is set.** Without it, `cue4 list -v` fails with "Unrecognized command or argument '-v'".
- Enum-like validation: `option.AcceptOnlyFromAmong("a", "b")`. It produces a helpful error and renders choices in help text.
- Registration: `command.Options.Add(...)`, `command.Arguments.Add(...)`, `root.Subcommands.Add(...)`.
- Actions: `SetAction(pr => intResult)` for sync, `SetAction(async (pr, ct) => intResult)` for async.
- Values are read with `parseResult.GetValue(option)`.
- Parse errors default to exit code 1. To honour our exit code 2, inspect `parseResult.Errors` **before** invoking and return early.

### Verified CUE4Parse API facts

Confirmed by reading the source in this repository. Several contradict what a
reasonable person would assume from the README; do not deviate.

- **`Initialize()` does not mount anything.** It only scans the directory and
  registers VFS readers into the unloaded set (`DefaultFileProvider.Initialize`).
  `Mount()` mounts every *unencrypted* reader; `SubmitKeys` mounts only readers
  whose `EncryptionKeyGuid` matches a submitted key. **Both are needed.** With no
  AES key configured and no `Mount()` call, `provider.Files` is empty and every
  verb silently returns nothing. See `AbstractVfsFileProvider.Mount` and
  `CUE4Parse.Tests/Fixtures/UE5_8/Tests/FixturePakTests.cs`.
- **`FGuid.TryParse` does not exist.** The only string entry point is
  `FGuid(string hexString)`, which requires exactly 32 hex characters and throws
  `ArgumentOutOfRangeException`/`FormatException` on dashed GUIDs. Normalize
  (strip `-`, `{`, `}`), validate length 32, then construct inside a try/catch.
- **Missing mappings surface as `MappingException`** (namespace
  `CUE4Parse.UE4.Exceptions`), thrown from `AbstractUePackage.CanDeserialize`
  when a package has `PKG_UnversionedProperties` and `Mappings is null`. This is
  the hook for exit code 6 — there is no need to predict it up front.
- **`SaveAsset` returns one file's bytes.** A cooked UE package is `.uasset` +
  `.uexp` (+ `.ubulk`/`.uptnl`). Use `SavePackage(path)`, which returns
  `IReadOnlyDictionary<string, byte[]>` of every payload file, whenever
  `GameFile.IsUePackage` is true.
- `provider.Files` is a `FileProviderDictionary` (`IReadOnlyDictionary<string, GameFile>`).
  `Keys` is an `IEnumerable<string>` that concatenates every mounted index, so it
  **can yield duplicates** and enumerating it is O(total files). Prefer
  `Files.ContainsKey(path)` over building a `HashSet` of all keys, and `Distinct`
  before emitting.
- `ExportOptions` also takes `texturePlatform`, `exportHdrTexturesAsHdr`,
  `exportMorphTargets` and `compressionFormat`. Only `texturePlatform` is exposed
  by this CLI; the others keep their defaults.
- Enum spellings confirmed: `EMeshFormat.{ActorX,Gltf2,UEFormat,USD}`,
  `ETextureFormat.{Png,Jpeg,Tga,Webp}`, `EMeshQuality.{Highest,Lowest,All}`,
  `ENaniteMeshFormat.{NaniteOnly,NoNanite,NaniteFirst,NaniteLast}`,
  `ESocketFormat.{Socket,Bone,None}`, `EMaterialDepth.{TopLayerOnly,AllLayersNoRef,AllLayers}`,
  `ETexturePlatform.{DesktopMobile,XboxAndPlaystation4,NintendoSwitch,Playstation5}`.

### Verified fixture facts

- `Fixtures/UE5_8/Pak/Oodle` is a **minimal** pak containing only
  `AssetRegistry.bin`, `DefaultGame.ini` and two `.locres` files — **no
  `.uasset` at all**. Do not point integration tests at it.
- `Fixtures/UE5_8/LegacyPak/{Tagged,Unversioned}/{Oodle,Zlib,Uncompressed,OodleEncrypted}`
  hold the real cooked packages (`DA_AllProperties.uasset`, `Empty.umap`, the
  texture and mesh fixtures). `Unversioned` requires
  `Mappings/CUE4ParseFixtures-Oodle.usmap`. Only the `OodleEncrypted` variants
  need the AES key.
- The fixture AES key is the ASCII string `CUE4ParseFixtureAESKey0123456789`
  (`FixtureTestUtilities.CreateFixtureAesKey`).
- `Fixtures/UE5_8/IoStore/*` additionally needs `provider.RegisterVfs(global.utoc)`
  from the *parent* directory, which `ProviderFactory` does not do. Use the
  LegacyPak fixtures for CLI tests.

---

## File Structure

| File | Responsibility |
|---|---|
| `CUE4Parse.Cli/CUE4Parse.Cli.csproj` | Project definition, assembly name `cue4` |
| `CUE4Parse.Cli/Program.cs` | Argument parsing and command wiring only |
| `CUE4Parse.Cli/Output/ExitCode.cs` | Exit code enum |
| `CUE4Parse.Cli/Output/CliException.cs` | Exception carrying an exit code and error code |
| `CUE4Parse.Cli/Output/JsonOutput.cs` | stdout envelope: results, NDJSON, errors |
| `CUE4Parse.Cli/Services/GameParser.cs` | `EGame` string parsing |
| `CUE4Parse.Cli/Services/CliConfig.cs` | Profile model, file loading, precedence |
| `CUE4Parse.Cli/Services/AssetMatcher.cs` | Glob/regex/extension filtering, safety limit |
| `CUE4Parse.Cli/Services/TargetResolver.cs` | Explicit paths vs criteria, not-found vs still-encrypted |
| `CUE4Parse.Cli/Services/ProviderFactory.cs` | The single provider bootstrap path |
| `CUE4Parse.Cli/Services/ExportOptionsMapper.cs` | CLI flag strings to `ExportOptions` enums |
| `CUE4Parse.Cli/Services/IFortniteApiClient.cs` | Abstraction over fortnite-api.com |
| `CUE4Parse.Cli/Services/FortniteApiClient.cs` | HTTP implementation with disk cache |
| `CUE4Parse.Cli/Commands/InfoCommand.cs` | Mount state, missing AES GUIDs |
| `CUE4Parse.Cli/Commands/ListCommand.cs` | Asset enumeration |
| `CUE4Parse.Cli/Commands/DumpCommand.cs` | Export deserialization to JSON |
| `CUE4Parse.Cli/Commands/UnpackCommand.cs` | Raw byte extraction |
| `CUE4Parse.Cli/Commands/ExportCommand.cs` | ExportSession driving |
| `CUE4Parse.Cli/Commands/UpdateCommand.cs` | Key and mappings refresh |
| `CUE4Parse.Cli.Tests/FixtureSupport.cs` | Shared fixture paths and profile for integration tests |
| `CUE4Parse.Cli.Tests/*` | Mirrors the above |
| `CUE4Parse.Cli/publish.ps1` | Self-contained single-file publish |

> `publish.ps1` lives **inside** `CUE4Parse.Cli/`, not at the repository root.
> The spec's divergence claim — that only `CUE4Parse.slnx` and
> `Directory.Packages.props` conflict on `git pull --rebase` — is only true if
> this work adds no third root-level file.

---

## Task 1: Project scaffolding and the output contract

**Files:**
- Create: `CUE4Parse.Cli/CUE4Parse.Cli.csproj`
- Create: `CUE4Parse.Cli/Output/ExitCode.cs`
- Create: `CUE4Parse.Cli/Output/CliException.cs`
- Create: `CUE4Parse.Cli/Output/JsonOutput.cs`
- Create: `CUE4Parse.Cli/Program.cs`
- Create: `CUE4Parse.Cli.Tests/CUE4Parse.Cli.Tests.csproj`
- Create: `CUE4Parse.Cli.Tests/JsonOutputTests.cs`
- Modify: `Directory.Packages.props`
- Modify: `CUE4Parse.slnx`

**Interfaces:**
- Consumes: nothing.
- Produces: `ExitCode` enum; `CliException(ExitCode exitCode, string errorCode, string message, object? details = null)` with properties `ExitCode`, `ErrorCode`, `Details`; `JsonOutput(TextWriter writer)` with methods `void WriteResult(object value, bool indent = false)`, `void WriteLine(object value)`, `void WriteError(string code, string message, object? details = null)`.

> `CliException` names its payload property `Details`, **not** `Data`. `System.Exception` already defines `Data` and shadowing it causes a compiler warning and confusing behaviour.

- [ ] **Step 1: Add the package version**

In `Directory.Packages.props`, inside the existing `<ItemGroup>`, add:

```xml
<PackageVersion Include="System.CommandLine" Version="2.0.11" />
```

- [ ] **Step 2: Create the CLI project file**

`CUE4Parse.Cli/CUE4Parse.Cli.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <AssemblyName>cue4</AssemblyName>
    <RootNamespace>CUE4Parse.Cli</RootNamespace>
    <IsPackable>false</IsPackable>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <InvariantGlobalization>true</InvariantGlobalization>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\CUE4Parse-Conversion\CUE4Parse-Conversion.csproj" />
    <ProjectReference Include="..\CUE4Parse\CUE4Parse.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Newtonsoft.Json" />
    <PackageReference Include="Serilog" />
    <PackageReference Include="Serilog.Sinks.Console" />
    <PackageReference Include="System.CommandLine" />
  </ItemGroup>

</Project>
```

- [ ] **Step 3: Create the test project file**

`CUE4Parse.Cli.Tests/CUE4Parse.Cli.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
    <UseMicrosoftTestingPlatformRunner>true</UseMicrosoftTestingPlatformRunner>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit.v3" />
    <PackageReference Include="xunit.runner.visualstudio">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\CUE4Parse.Cli\CUE4Parse.Cli.csproj" />
  </ItemGroup>

  <!--
    Integration tests (Tasks 7-9) run against the real fixture archives. Link them
    into this project's own output rather than reaching into CUE4Parse.Tests/bin:
    that path hardcodes the configuration and breaks under Debug and on CI.
  -->
  <ItemGroup>
    <None Include="..\CUE4Parse.Tests\Fixtures\**\*"
          Link="Fixtures\%(RecursiveDir)%(Filename)%(Extension)"
          CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>

</Project>
```

- [ ] **Step 4: Register both projects in the solution**

In `CUE4Parse.slnx`, add two lines alongside the existing `<Project>` entries:

```xml
<Project Path="CUE4Parse.Cli/CUE4Parse.Cli.csproj" />
<Project Path="CUE4Parse.Cli.Tests/CUE4Parse.Cli.Tests.csproj" />
```

- [ ] **Step 5: Write the failing test**

`CUE4Parse.Cli.Tests/JsonOutputTests.cs`:

```csharp
using System.IO;
using CUE4Parse.Cli.Output;
using Newtonsoft.Json.Linq;

namespace CUE4Parse.Cli.Tests;

public class JsonOutputTests
{
    [Fact]
    public void WriteErrorProducesParseableEnvelopeWithCodeAndMessage()
    {
        var sw = new StringWriter();
        var output = new JsonOutput(sw);

        output.WriteError("AES_KEY_MISSING", "missing key");

        var parsed = JObject.Parse(sw.ToString());
        Assert.Equal("AES_KEY_MISSING", parsed["error"]?["code"]?.Value<string>());
        Assert.Equal("missing key", parsed["error"]?["message"]?.Value<string>());
    }

    [Fact]
    public void WriteErrorEscapesControlCharactersInsteadOfEmittingInvalidJson()
    {
        var sw = new StringWriter();
        var output = new JsonOutput(sw);

        output.WriteError("USAGE", "bad value. Must be one of:\n\t'a'\n\t'b'");

        // Must parse: raw newlines/tabs would make this invalid JSON.
        var parsed = JObject.Parse(sw.ToString());
        Assert.Contains("Must be one of", parsed["error"]?["message"]?.Value<string>());
    }

    [Fact]
    public void WriteLineEmitsOneCompactJsonObjectPerLine()
    {
        var sw = new StringWriter();
        var output = new JsonOutput(sw);

        output.WriteLine(new { path = "a" });
        output.WriteLine(new { path = "b" });

        var lines = sw.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.Equal("a", JObject.Parse(lines[0])["path"]?.Value<string>());
        Assert.Equal("b", JObject.Parse(lines[1])["path"]?.Value<string>());
    }

    [Fact]
    public void WriteErrorIncludesDetailsWhenSupplied()
    {
        var sw = new StringWriter();
        var output = new JsonOutput(sw);

        output.WriteError("AES_KEY_MISSING", "missing", new { missingGuids = new[] { "abc" } });

        var parsed = JObject.Parse(sw.ToString());
        Assert.Equal("abc", parsed["error"]?["details"]?["missingGuids"]?[0]?.Value<string>());
    }
}
```

- [ ] **Step 6: Run the test to verify it fails**

Run: `dotnet run --project CUE4Parse.Cli.Tests/CUE4Parse.Cli.Tests.csproj -- --filter-method "*JsonOutput*"`
Expected: build failure — `JsonOutput` and `CUE4Parse.Cli.Output` do not exist.

- [ ] **Step 7: Implement ExitCode**

`CUE4Parse.Cli/Output/ExitCode.cs`:

```csharp
namespace CUE4Parse.Cli.Output;

public enum ExitCode
{
    Success = 0,
    Error = 1,
    Usage = 2,
    Config = 3,
    Mount = 4,
    AesKey = 5,
    Mappings = 6,
    NotFound = 7,
    PartialExport = 8,
}
```

- [ ] **Step 8: Implement CliException**

`CUE4Parse.Cli/Output/CliException.cs`:

```csharp
namespace CUE4Parse.Cli.Output;

public sealed class CliException(ExitCode exitCode, string errorCode, string message, object? details = null)
    : Exception(message)
{
    public ExitCode ExitCode { get; } = exitCode;
    public string ErrorCode { get; } = errorCode;

    /// <summary>Structured payload emitted under <c>error.details</c>.</summary>
    /// <remarks>Named <c>Details</c> because <see cref="Exception.Data"/> already exists.</remarks>
    public object? Details { get; } = details;
}
```

- [ ] **Step 9: Implement JsonOutput**

`CUE4Parse.Cli/Output/JsonOutput.cs`:

```csharp
using Newtonsoft.Json;

namespace CUE4Parse.Cli.Output;

public sealed class JsonOutput(TextWriter writer)
{
    private static readonly JsonSerializerSettings Compact = new()
    {
        Formatting = Formatting.None,
        NullValueHandling = NullValueHandling.Ignore,
    };

    private static readonly JsonSerializerSettings Indented = new()
    {
        Formatting = Formatting.Indented,
        NullValueHandling = NullValueHandling.Ignore,
    };

    public void WriteResult(object value, bool indent = false)
    {
        writer.Write(JsonConvert.SerializeObject(value, indent ? Indented : Compact));
        writer.Write('\n');
        writer.Flush();
    }

    /// <summary>Emits one compact JSON object per line (NDJSON).</summary>
    public void WriteLine(object value)
    {
        writer.Write(JsonConvert.SerializeObject(value, Compact));
        writer.Write('\n');
        writer.Flush();
    }

    public void WriteError(string code, string message, object? details = null)
        => WriteResult(new { error = new { code, message, details } });
}
```

- [ ] **Step 10: Create a placeholder Program.cs so the project compiles**

`CUE4Parse.Cli/Program.cs`:

```csharp
// Wired up in Task 6.
return 0;
```

- [ ] **Step 11: Run the tests to verify they pass**

Run: `dotnet run --project CUE4Parse.Cli.Tests/CUE4Parse.Cli.Tests.csproj -- --filter-method "*JsonOutput*"`
Expected: PASS, 4 tests.

- [ ] **Step 12: Verify the whole solution still builds**

Run: `dotnet build CUE4Parse.slnx -c Release`
Expected: 0 errors.

- [ ] **Step 13: Commit**

```bash
git add Directory.Packages.props CUE4Parse.slnx CUE4Parse.Cli CUE4Parse.Cli.Tests
git commit -m "feat(cli): scaffold CUE4Parse.Cli project and JSON output contract"
```

---

## Task 2: EGame parsing

**Files:**
- Create: `CUE4Parse.Cli/Services/GameParser.cs`
- Create: `CUE4Parse.Cli.Tests/GameParserTests.cs`

**Interfaces:**
- Consumes: `ExitCode`, `CliException` from Task 1.
- Produces: `static class GameParser` with `static EGame Parse(string value)`.

`EGame` values are named `GAME_UE5_6`, `GAME_UE4_27`, and so on. `CUE4Parse/Globals.cs` declares `global using static CUE4Parse.UE4.Versions.EGame;`, so the constants are usable unqualified inside `CUE4Parse`, but **not** from this project — import `CUE4Parse.UE4.Versions` explicitly.

- [ ] **Step 1: Write the failing test**

`CUE4Parse.Cli.Tests/GameParserTests.cs`:

```csharp
using CUE4Parse.Cli.Output;
using CUE4Parse.Cli.Services;
using CUE4Parse.UE4.Versions;

namespace CUE4Parse.Cli.Tests;

public class GameParserTests
{
    [Theory]
    [InlineData("GAME_UE5_6")]
    [InlineData("game_ue5_6")]
    public void ParseAcceptsEnumNameCaseInsensitively(string value)
        => Assert.Equal(EGame.GAME_UE5_6, GameParser.Parse(value));

    [Theory]
    [InlineData("5.6", EGame.GAME_UE5_6)]
    [InlineData("4.27", EGame.GAME_UE4_27)]
    public void ParseAcceptsEngineVersionShorthand(string value, EGame expected)
        => Assert.Equal(expected, GameParser.Parse(value));

    [Theory]
    [InlineData("not-a-game")]
    [InlineData("5")]          // Enum.TryParse happily returns (EGame)5 — a nonsense version.
    [InlineData("67108864")]   // Same trap with a plausible-looking underlying value.
    [InlineData("GAME_UE5_6, GAME_UE4_27")] // Enum.TryParse accepts comma-separated lists.
    public void ParseThrowsConfigErrorForUnknownValue(string value)
    {
        var ex = Assert.Throws<CliException>(() => GameParser.Parse(value));
        Assert.Equal(ExitCode.Config, ex.ExitCode);
        Assert.Equal("UNKNOWN_GAME", ex.ErrorCode);
    }
}
```

> `Enum.TryParse<EGame>` accepts bare integers and comma-separated flag lists,
> both of which produce an `EGame` that is not a real game. Every parse result
> must be checked with `Enum.IsDefined`.

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet run --project CUE4Parse.Cli.Tests/CUE4Parse.Cli.Tests.csproj -- --filter-method "*GameParser*"`
Expected: build failure — `GameParser` does not exist.

- [ ] **Step 3: Implement GameParser**

`CUE4Parse.Cli/Services/GameParser.cs`:

```csharp
using CUE4Parse.Cli.Output;
using CUE4Parse.UE4.Versions;

namespace CUE4Parse.Cli.Services;

public static class GameParser
{
    /// <summary>Parses an <see cref="EGame"/> from an enum name ("GAME_UE5_6") or shorthand ("5.6").</summary>
    public static EGame Parse(string value)
    {
        var trimmed = value.Trim();

        // Enum.TryParse accepts bare integers ("5") and comma-separated lists, so
        // every candidate is validated against the declared members.
        if (Enum.TryParse<EGame>(trimmed, ignoreCase: true, out var direct) && Enum.IsDefined(direct))
            return direct;

        // Shorthand: "5.6" -> "GAME_UE5_6"
        var parts = trimmed.Split('.');
        if (parts.Length == 2 &&
            int.TryParse(parts[0], out var major) &&
            int.TryParse(parts[1], out var minor) &&
            Enum.TryParse<EGame>($"GAME_UE{major}_{minor}", ignoreCase: true, out var shorthand) &&
            Enum.IsDefined(shorthand))
        {
            return shorthand;
        }

        throw new CliException(
            ExitCode.Config,
            "UNKNOWN_GAME",
            $"Unknown game version '{value}'. Use an EGame name such as 'GAME_UE5_6' or shorthand such as '5.6'.");
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet run --project CUE4Parse.Cli.Tests/CUE4Parse.Cli.Tests.csproj -- --filter-method "*GameParser*"`
Expected: PASS, 8 tests.

- [ ] **Step 5: Commit**

```bash
git add CUE4Parse.Cli/Services/GameParser.cs CUE4Parse.Cli.Tests/GameParserTests.cs
git commit -m "feat(cli): parse EGame from enum name or engine-version shorthand"
```

---

## Task 3: Configuration and profile resolution

**Files:**
- Create: `CUE4Parse.Cli/Services/CliConfig.cs`
- Create: `CUE4Parse.Cli.Tests/CliConfigTests.cs`

**Interfaces:**
- Consumes: `GameParser.Parse` (Task 2), `CliException`/`ExitCode` (Task 1).
- Produces:
  - `sealed record ProfileConfig` with `string? PaksDir`, `string? Game`, `string? Mappings`, `AesConfig? Aes`
  - `sealed record AesConfig` with `string? Main`, `Dictionary<string, string>? Dynamic`
  - `sealed record CliConfig` with `string? DefaultProfile`, `Dictionary<string, ProfileConfig>? Profiles`
  - `sealed record ResolvedProfile(string PaksDir, EGame Game, string? Mappings, string? MainAesKey, IReadOnlyDictionary<string, string> DynamicKeys)`
  - `sealed record ProfileOverrides(string? PaksDir = null, string? Game = null, string? Mappings = null, string? Aes = null)`
  - `static class ConfigLoader` with:
    - `static CliConfig Load(string path)`
    - `static string? FindConfigFile(string? explicitPath, string workingDirectory, string appDataDirectory)`
    - `static ResolvedProfile Resolve(CliConfig config, string? profileName, ProfileOverrides overrides)`

Resolution order for the config file: explicit `--config`, then `./cue4.json`, then `%APPDATA%\cue4\cue4.json`. Command-line overrides beat profile values.

- [ ] **Step 1: Write the failing test**

`CUE4Parse.Cli.Tests/CliConfigTests.cs`:

```csharp
using CUE4Parse.Cli.Output;
using CUE4Parse.Cli.Services;
using CUE4Parse.UE4.Versions;

namespace CUE4Parse.Cli.Tests;

public class CliConfigTests
{
    private static CliConfig SampleConfig() => new()
    {
        DefaultProfile = "fn",
        Profiles = new Dictionary<string, ProfileConfig>
        {
            ["fn"] = new()
            {
                PaksDir = "D:/Games/Fortnite/Paks",
                Game = "GAME_UE5_6",
                Mappings = "auto",
                Aes = new AesConfig { Main = "0xAABB" },
            },
        },
    };

    [Fact]
    public void ResolveUsesDefaultProfileWhenNoNameGiven()
    {
        var resolved = ConfigLoader.Resolve(SampleConfig(), null, new ProfileOverrides());

        Assert.Equal("D:/Games/Fortnite/Paks", resolved.PaksDir);
        Assert.Equal(EGame.GAME_UE5_6, resolved.Game);
        Assert.Equal("0xAABB", resolved.MainAesKey);
    }

    [Fact]
    public void CommandLineOverridesBeatProfileValues()
    {
        var resolved = ConfigLoader.Resolve(
            SampleConfig(), "fn",
            new ProfileOverrides(PaksDir: "E:/Other", Game: "5.3", Aes: "0xCCDD"));

        Assert.Equal("E:/Other", resolved.PaksDir);
        Assert.Equal(EGame.GAME_UE5_3, resolved.Game);
        Assert.Equal("0xCCDD", resolved.MainAesKey);
    }

    [Fact]
    public void ResolveThrowsConfigErrorForUnknownProfileName()
    {
        var ex = Assert.Throws<CliException>(
            () => ConfigLoader.Resolve(SampleConfig(), "nope", new ProfileOverrides()));

        Assert.Equal(ExitCode.Config, ex.ExitCode);
        Assert.Equal("UNKNOWN_PROFILE", ex.ErrorCode);
    }

    [Fact]
    public void ResolveThrowsConfigErrorWhenPaksDirIsMissingEverywhere()
    {
        var config = new CliConfig
        {
            DefaultProfile = "empty",
            Profiles = new Dictionary<string, ProfileConfig> { ["empty"] = new() { Game = "5.6" } },
        };

        var ex = Assert.Throws<CliException>(() => ConfigLoader.Resolve(config, null, new ProfileOverrides()));
        Assert.Equal(ExitCode.Config, ex.ExitCode);
        Assert.Equal("MISSING_PAKS_DIR", ex.ErrorCode);
    }

    [Fact]
    public void ResolveWorksWithNoConfigFileWhenAllValuesComeFromOverrides()
    {
        var resolved = ConfigLoader.Resolve(
            new CliConfig(), null,
            new ProfileOverrides(PaksDir: "E:/Only", Game: "5.6"));

        Assert.Equal("E:/Only", resolved.PaksDir);
        Assert.Equal(EGame.GAME_UE5_6, resolved.Game);
    }

    [Fact]
    public void FindConfigFilePrefersWorkingDirectoryOverAppData()
    {
        var work = Directory.CreateTempSubdirectory().FullName;
        var appData = Directory.CreateTempSubdirectory().FullName;
        File.WriteAllText(Path.Combine(work, "cue4.json"), "{}");
        File.WriteAllText(Path.Combine(appData, "cue4.json"), "{}");

        var found = ConfigLoader.FindConfigFile(null, work, appData);

        Assert.Equal(Path.Combine(work, "cue4.json"), found);
    }

    [Fact]
    public void FindConfigFileFallsBackToAppData()
    {
        var work = Directory.CreateTempSubdirectory().FullName;
        var appData = Directory.CreateTempSubdirectory().FullName;
        File.WriteAllText(Path.Combine(appData, "cue4.json"), "{}");

        var found = ConfigLoader.FindConfigFile(null, work, appData);

        Assert.Equal(Path.Combine(appData, "cue4.json"), found);
    }

    [Fact]
    public void LoadThrowsConfigErrorForMalformedJson()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var path = Path.Combine(dir, "cue4.json");
        File.WriteAllText(path, "{ not json");

        var ex = Assert.Throws<CliException>(() => ConfigLoader.Load(path));
        Assert.Equal(ExitCode.Config, ex.ExitCode);
        Assert.Equal("BAD_CONFIG", ex.ErrorCode);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet run --project CUE4Parse.Cli.Tests/CUE4Parse.Cli.Tests.csproj -- --filter-method "*CliConfig*"`
Expected: build failure — `CliConfig` does not exist.

- [ ] **Step 3: Implement the config model and loader**

`CUE4Parse.Cli/Services/CliConfig.cs`:

```csharp
using CUE4Parse.Cli.Output;
using CUE4Parse.UE4.Versions;
using Newtonsoft.Json;

namespace CUE4Parse.Cli.Services;

public sealed record AesConfig
{
    public string? Main { get; init; }
    public Dictionary<string, string>? Dynamic { get; init; }
}

public sealed record ProfileConfig
{
    public string? PaksDir { get; init; }
    public string? Game { get; init; }
    public string? Mappings { get; init; }
    public AesConfig? Aes { get; init; }
}

public sealed record CliConfig
{
    public string? DefaultProfile { get; init; }
    public Dictionary<string, ProfileConfig>? Profiles { get; init; }
}

public sealed record ProfileOverrides(
    string? PaksDir = null,
    string? Game = null,
    string? Mappings = null,
    string? Aes = null);

public sealed record ResolvedProfile(
    string PaksDir,
    EGame Game,
    string? Mappings,
    string? MainAesKey,
    IReadOnlyDictionary<string, string> DynamicKeys);

public static class ConfigLoader
{
    public const string FileName = "cue4.json";

    public static CliConfig Load(string path)
    {
        try
        {
            return JsonConvert.DeserializeObject<CliConfig>(File.ReadAllText(path)) ?? new CliConfig();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            throw new CliException(ExitCode.Config, "BAD_CONFIG", $"Could not read config '{path}': {ex.Message}");
        }
    }

    /// <summary>Resolves the config file location: explicit path, then working dir, then app data.</summary>
    public static string? FindConfigFile(string? explicitPath, string workingDirectory, string appDataDirectory)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            if (!File.Exists(explicitPath))
                throw new CliException(ExitCode.Config, "CONFIG_NOT_FOUND", $"Config file not found: {explicitPath}");
            return explicitPath;
        }

        var local = Path.Combine(workingDirectory, FileName);
        if (File.Exists(local)) return local;

        var appData = Path.Combine(appDataDirectory, FileName);
        return File.Exists(appData) ? appData : null;
    }

    public static ResolvedProfile Resolve(CliConfig config, string? profileName, ProfileOverrides overrides)
    {
        ProfileConfig profile;

        var name = profileName ?? config.DefaultProfile;
        if (name is null)
        {
            profile = new ProfileConfig();
        }
        else if (config.Profiles?.TryGetValue(name, out var found) == true)
        {
            profile = found;
        }
        else
        {
            var known = config.Profiles is null ? "none" : string.Join(", ", config.Profiles.Keys);
            throw new CliException(
                ExitCode.Config, "UNKNOWN_PROFILE",
                $"Profile '{name}' not found. Known profiles: {known}.");
        }

        var paksDir = overrides.PaksDir ?? profile.PaksDir
            ?? throw new CliException(
                ExitCode.Config, "MISSING_PAKS_DIR",
                "No paks directory configured. Set 'paksDir' in the profile or pass --paks.");

        var gameText = overrides.Game ?? profile.Game
            ?? throw new CliException(
                ExitCode.Config, "MISSING_GAME",
                "No game version configured. Set 'game' in the profile or pass --game.");

        return new ResolvedProfile(
            paksDir,
            GameParser.Parse(gameText),
            overrides.Mappings ?? profile.Mappings,
            overrides.Aes ?? profile.Aes?.Main,
            profile.Aes?.Dynamic ?? new Dictionary<string, string>());
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet run --project CUE4Parse.Cli.Tests/CUE4Parse.Cli.Tests.csproj -- --filter-method "*CliConfig*"`
Expected: PASS, 8 tests.

- [ ] **Step 5: Commit**

```bash
git add CUE4Parse.Cli/Services/CliConfig.cs CUE4Parse.Cli.Tests/CliConfigTests.cs
git commit -m "feat(cli): add cue4.json profile model, discovery and override precedence"
```

---

## Task 4: Asset matching and the safety limit

**Files:**
- Create: `CUE4Parse.Cli/Services/AssetMatcher.cs`
- Create: `CUE4Parse.Cli.Tests/AssetMatcherTests.cs`

**Interfaces:**
- Consumes: `CliException`/`ExitCode` (Task 1).
- Produces:
  - `sealed record MatchCriteria(string[]? Globs = null, string? Regex = null, string? Extension = null, int? Limit = null)`
  - `readonly record struct MatchResult(IReadOnlyList<string> Paths, int TotalMatched)`
  - `static class AssetMatcher` with:
    - `static bool IsMatch(string path, MatchCriteria criteria)`
    - `static MatchResult Filter(IEnumerable<string> paths, MatchCriteria criteria)`
    - `static void EnforceLimit(MatchResult result, MatchCriteria criteria, bool force, int limit = DefaultLimit)`
    - `const int DefaultLimit = 1000`

Glob semantics: `*` matches within a path segment, `**` matches across separators, `?` matches one non-separator character. Matching is case-insensitive because UE asset paths are treated case-insensitively throughout CUE4Parse.

`Filter` deduplicates, because `FileProviderDictionary.Keys` concatenates every mounted index and can yield the same path more than once.

**`Filter` reports the true match count separately from the truncated list.** If it did not, `--limit 100` against 50 000 matches would slip past the 1000-asset guard, which is precisely the silent truncation the guard exists to prevent. An explicit `--limit` is treated as consent — the caller has stated how many they want — so `EnforceLimit` skips the check in that case, exactly as `--force` does.

- [ ] **Step 1: Write the failing test**

`CUE4Parse.Cli.Tests/AssetMatcherTests.cs`:

```csharp
using CUE4Parse.Cli.Output;
using CUE4Parse.Cli.Services;

namespace CUE4Parse.Cli.Tests;

public class AssetMatcherTests
{
    [Theory]
    [InlineData("Game/Content/Chars/A.uasset", "**/Chars/**", true)]
    [InlineData("Game/Content/Chars/A.uasset", "**/*.uasset", true)]
    [InlineData("Game/Content/Chars/A.uasset", "**/Props/**", false)]
    [InlineData("Game/Content/A.uasset", "Game/*/A.uasset", true)]
    [InlineData("Game/Content/Sub/A.uasset", "Game/*/A.uasset", false)]
    public void GlobMatchesAcrossAndWithinSegments(string path, string glob, bool expected)
        => Assert.Equal(expected, AssetMatcher.IsMatch(path, new MatchCriteria(Globs: [glob])));

    [Fact]
    public void GlobMatchingIsCaseInsensitive()
        => Assert.True(AssetMatcher.IsMatch("Game/Content/A.uasset", new MatchCriteria(Globs: ["**/content/**"])));

    [Fact]
    public void MultipleGlobsMatchIfAnyMatches()
    {
        var criteria = new MatchCriteria(Globs: ["**/Props/**", "**/Chars/**"]);
        Assert.True(AssetMatcher.IsMatch("Game/Chars/A.uasset", criteria));
    }

    [Fact]
    public void RegexCriteriaIsApplied()
        => Assert.True(AssetMatcher.IsMatch("Game/Chars/CID_001.uasset", new MatchCriteria(Regex: @"CID_\d+")));

    [Fact]
    public void ExtensionCriteriaIsAppliedWithoutRequiringLeadingDot()
    {
        Assert.True(AssetMatcher.IsMatch("A.uasset", new MatchCriteria(Extension: "uasset")));
        Assert.True(AssetMatcher.IsMatch("A.uasset", new MatchCriteria(Extension: ".uasset")));
        Assert.False(AssetMatcher.IsMatch("A.umap", new MatchCriteria(Extension: "uasset")));
    }

    [Fact]
    public void EmptyCriteriaMatchesEverything()
        => Assert.True(AssetMatcher.IsMatch("anything", new MatchCriteria()));

    [Fact]
    public void FilterAppliesLimitAsATruncationButStillReportsTheTrueTotal()
    {
        string[] paths = ["a.uasset", "b.uasset", "c.uasset"];
        var result = AssetMatcher.Filter(paths, new MatchCriteria(Limit: 2));
        Assert.Equal(2, result.Paths.Count);
        Assert.Equal(3, result.TotalMatched);
    }

    [Fact]
    public void FilterDeduplicatesPathsRepeatedAcrossMountedIndices()
    {
        string[] paths = ["a.uasset", "A.uasset", "b.uasset"];
        var result = AssetMatcher.Filter(paths, new MatchCriteria());
        Assert.Equal(2, result.TotalMatched);
    }

    [Fact]
    public void EnforceLimitThrowsUsageErrorWithMatchCountWhenExceeded()
    {
        var result = new MatchResult([], 1001);
        var ex = Assert.Throws<CliException>(
            () => AssetMatcher.EnforceLimit(result, new MatchCriteria(), force: false));

        Assert.Equal(ExitCode.Usage, ex.ExitCode);
        Assert.Equal("LIMIT_EXCEEDED", ex.ErrorCode);
        Assert.Contains("1001", ex.Message);
    }

    [Fact]
    public void EnforceLimitUsesTheTrueTotalNotTheTruncatedCount()
    {
        // The truncated list is small; the guard must still fire on the real total.
        var result = new MatchResult(["a", "b"], 50_000);
        Assert.Throws<CliException>(
            () => AssetMatcher.EnforceLimit(result, new MatchCriteria(), force: false));
    }

    [Fact]
    public void EnforceLimitTreatsAnExplicitLimitAsConsent()
        => AssetMatcher.EnforceLimit(
            new MatchResult(["a"], 50_000), new MatchCriteria(Limit: 1), force: false);

    [Fact]
    public void EnforceLimitAllowsExceedingWhenForced()
        => AssetMatcher.EnforceLimit(new MatchResult([], 5000), new MatchCriteria(), force: true);

    [Fact]
    public void EnforceLimitAllowsExactlyTheLimit()
        => AssetMatcher.EnforceLimit(new MatchResult([], 1000), new MatchCriteria(), force: false);
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet run --project CUE4Parse.Cli.Tests/CUE4Parse.Cli.Tests.csproj -- --filter-method "*AssetMatcher*"`
Expected: build failure — `AssetMatcher` does not exist.

- [ ] **Step 3: Implement AssetMatcher**

`CUE4Parse.Cli/Services/AssetMatcher.cs`:

```csharp
using System.Text;
using System.Text.RegularExpressions;
using CUE4Parse.Cli.Output;

namespace CUE4Parse.Cli.Services;

public sealed record MatchCriteria(
    string[]? Globs = null,
    string? Regex = null,
    string? Extension = null,
    int? Limit = null);

/// <summary>Truncated results plus the count before truncation.</summary>
public readonly record struct MatchResult(IReadOnlyList<string> Paths, int TotalMatched);

public static class AssetMatcher
{
    public const int DefaultLimit = 1000;

    private static readonly Dictionary<string, Regex> GlobCache = new(StringComparer.Ordinal);

    public static bool IsMatch(string path, MatchCriteria criteria)
    {
        if (criteria.Globs is { Length: > 0 } globs && !globs.Any(g => GlobRegex(g).IsMatch(path)))
            return false;

        if (!string.IsNullOrEmpty(criteria.Regex) &&
            !Regex.IsMatch(path, criteria.Regex, RegexOptions.IgnoreCase))
            return false;

        if (!string.IsNullOrEmpty(criteria.Extension))
        {
            var wanted = criteria.Extension.TrimStart('.');
            var actual = Path.GetExtension(path.AsSpan()).TrimStart('.');
            if (!actual.Equals(wanted, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Filters and deduplicates, returning both the (optionally truncated) list and
    /// the true match count. <c>FileProviderDictionary.Keys</c> concatenates every
    /// mounted index and can repeat a path, hence the distinct pass.
    /// </summary>
    public static MatchResult Filter(IEnumerable<string> paths, MatchCriteria criteria)
    {
        var matched = paths
            .Where(p => IsMatch(p, criteria))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return criteria.Limit is { } limit && limit < matched.Count
            ? new MatchResult(matched.Take(limit).ToList(), matched.Count)
            : new MatchResult(matched, matched.Count);
    }

    /// <summary>
    /// Guards bulk operations against the true match count, never the truncated one —
    /// otherwise <c>--limit 100</c> against 50,000 matches would slip past the guard,
    /// which is the silent truncation this exists to prevent.
    /// An explicit <c>--limit</c> is consent: the caller has stated how many they want.
    /// </summary>
    public static void EnforceLimit(
        MatchResult result, MatchCriteria criteria, bool force, int limit = DefaultLimit)
    {
        if (force || criteria.Limit is not null || result.TotalMatched <= limit) return;

        throw new CliException(
            ExitCode.Usage,
            "LIMIT_EXCEEDED",
            $"Matched {result.TotalMatched} assets, which exceeds the safety limit of {limit}. " +
            "Narrow the pattern, pass --limit to take the first N, or pass --force.",
            new { matchCount = result.TotalMatched, limit });
    }

    private static Regex GlobRegex(string glob)
    {
        lock (GlobCache)
        {
            if (GlobCache.TryGetValue(glob, out var cached)) return cached;

            var sb = new StringBuilder("^");
            for (var i = 0; i < glob.Length; i++)
            {
                var c = glob[i];
                switch (c)
                {
                    case '*' when i + 1 < glob.Length && glob[i + 1] == '*':
                        sb.Append(".*");
                        i++;
                        // Swallow a following separator so "**/x" also matches "x".
                        if (i + 1 < glob.Length && (glob[i + 1] == '/' || glob[i + 1] == '\\')) i++;
                        break;
                    case '*':
                        sb.Append("[^/\\\\]*");
                        break;
                    case '?':
                        sb.Append("[^/\\\\]");
                        break;
                    case '/':
                    case '\\':
                        sb.Append("[/\\\\]");
                        break;
                    default:
                        sb.Append(Regex.Escape(c.ToString()));
                        break;
                }
            }
            sb.Append('$');

            var regex = new Regex(sb.ToString(), RegexOptions.IgnoreCase | RegexOptions.Compiled);
            GlobCache[glob] = regex;
            return regex;
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet run --project CUE4Parse.Cli.Tests/CUE4Parse.Cli.Tests.csproj -- --filter-method "*AssetMatcher*"`
Expected: PASS, 18 tests.

- [ ] **Step 5: Commit**

```bash
git add CUE4Parse.Cli/Services/AssetMatcher.cs CUE4Parse.Cli.Tests/AssetMatcherTests.cs
git commit -m "feat(cli): add glob/regex asset matching and bulk-operation safety limit"
```

---

## Task 5: Provider bootstrap

**Files:**
- Create: `CUE4Parse.Cli/Services/ProviderFactory.cs`
- Create: `CUE4Parse.Cli.Tests/ProviderFactoryTests.cs`

**Interfaces:**
- Consumes: `ResolvedProfile` (Task 3), `CliException`/`ExitCode` (Task 1).
- Produces: `static class ProviderFactory` with:
  - `static DefaultFileProvider Create(ResolvedProfile profile)`
  - `static FAesKey ParseAesKey(string value)`
  - `static FGuid ParseAesGuid(string value)`
  - `static void ThrowIfKeysMissing(AbstractVfsFileProvider provider)`

The bootstrap order is fixed and must not be reordered:

`OodleHelper.Initialize()` → `ZlibHelper.Initialize()` → construct provider →
assign `MappingsContainer` → `Initialize()` → `SubmitKeys(...)` → **`Mount()`** →
`PostMount()`.

> **`Mount()` is not optional.** `Initialize()` only registers readers.
> `SubmitKeys` mounts only the readers matching a submitted key GUID, so a game
> with no encryption — or any profile without an AES key — mounts **nothing**
> and every verb returns an empty result set. The original draft of this plan
> omitted `Mount()`; that was a silent, total failure. `MappingsContainer` moves
> ahead of `Initialize()` to match every call site in `CUE4Parse.Tests`.

Use the `StringComparer` constructor overload. The `bool isCaseInsensitive` overloads are `[Obsolete]`.

`ThrowIfKeysMissing` replaces the never-called `VerifyMounted`. It is invoked by
the commands **only after a lookup fails**, so that "asset not found" and "the
archive holding it is still encrypted" are distinguishable — the whole reason
exit code 5 is separate from 7. Calling it unconditionally after mount would be
wrong: a game can legitimately have optional encrypted chunks the caller does
not need.

- [ ] **Step 1: Write the failing test**

`CUE4Parse.Cli.Tests/ProviderFactoryTests.cs`:

```csharp
using CUE4Parse.Cli.Output;
using CUE4Parse.Cli.Services;
using CUE4Parse.UE4.Versions;

namespace CUE4Parse.Cli.Tests;

public class ProviderFactoryTests
{
    [Theory]
    [InlineData("0x0102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E1F20")]
    [InlineData("0102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E1F20")]
    public void ParseAesKeyAcceptsHexWithAndWithoutPrefix(string value)
        => Assert.NotNull(ProviderFactory.ParseAesKey(value));

    [Fact]
    public void ParseAesKeyThrowsAesErrorForMalformedKey()
    {
        var ex = Assert.Throws<CliException>(() => ProviderFactory.ParseAesKey("nonsense"));
        Assert.Equal(ExitCode.AesKey, ex.ExitCode);
        Assert.Equal("BAD_AES_KEY", ex.ErrorCode);
    }

    [Theory]
    [InlineData("11111111222222223333333344444444")]
    [InlineData("11111111-2222-2222-3333-333344444444")]
    [InlineData("{11111111-2222-2222-3333-333344444444}")]
    public void ParseAesGuidAcceptsBareAndDashedForms(string value)
        => Assert.Equal(
            ProviderFactory.ParseAesGuid("11111111222222223333333344444444"),
            ProviderFactory.ParseAesGuid(value));

    [Fact]
    public void ParseAesGuidThrowsAesErrorForMalformedGuid()
    {
        // FGuid has no TryParse and its ctor throws on anything but 32 hex chars.
        var ex = Assert.Throws<CliException>(() => ProviderFactory.ParseAesGuid("not-a-guid"));
        Assert.Equal(ExitCode.AesKey, ex.ExitCode);
        Assert.Equal("BAD_AES_GUID", ex.ErrorCode);
    }

    [Fact]
    public void CreateThrowsMountErrorWhenPaksDirectoryDoesNotExist()
    {
        var profile = new ResolvedProfile(
            Path.Combine(Path.GetTempPath(), "definitely-not-here-" + Guid.NewGuid().ToString("N")),
            EGame.GAME_UE5_6, null, null, new Dictionary<string, string>());

        var ex = Assert.Throws<CliException>(() => ProviderFactory.Create(profile));
        Assert.Equal(ExitCode.Mount, ex.ExitCode);
        Assert.Equal("PAKS_DIR_NOT_FOUND", ex.ErrorCode);
    }

    [Fact]
    public void CreateThrowsMappingsErrorWhenMappingsFileIsMissing()
    {
        var paks = Directory.CreateTempSubdirectory().FullName;
        var profile = new ResolvedProfile(
            paks, EGame.GAME_UE5_6,
            Path.Combine(paks, "nope.usmap"), null, new Dictionary<string, string>());

        var ex = Assert.Throws<CliException>(() => ProviderFactory.Create(profile));
        Assert.Equal(ExitCode.Mappings, ex.ExitCode);
        Assert.Equal("MAPPINGS_NOT_FOUND", ex.ErrorCode);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet run --project CUE4Parse.Cli.Tests/CUE4Parse.Cli.Tests.csproj -- --filter-method "*ProviderFactory*"`
Expected: build failure — `ProviderFactory` does not exist.

- [ ] **Step 3: Implement ProviderFactory**

`CUE4Parse.Cli/Services/ProviderFactory.cs`:

```csharp
using System.Globalization;
using CUE4Parse.Cli.Output;
using CUE4Parse.Compression;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.FileProvider.Vfs;
using CUE4Parse.MappingsProvider;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Versions;

namespace CUE4Parse.Cli.Services;

public static class ProviderFactory
{
    public static DefaultFileProvider Create(ResolvedProfile profile)
    {
        if (!Directory.Exists(profile.PaksDir))
        {
            throw new CliException(
                ExitCode.Mount, "PAKS_DIR_NOT_FOUND",
                $"Paks directory not found: {profile.PaksDir}");
        }

        if (profile.Mappings is { } mappingsPath &&
            !string.Equals(mappingsPath, "auto", StringComparison.OrdinalIgnoreCase) &&
            !File.Exists(mappingsPath))
        {
            throw new CliException(
                ExitCode.Mappings, "MAPPINGS_NOT_FOUND",
                $"Mappings file not found: {mappingsPath}");
        }

        // Order matters: compression backends must be ready before any archive is read.
        OodleHelper.Initialize();
        ZlibHelper.Initialize();

        var provider = new DefaultFileProvider(
            profile.PaksDir,
            SearchOption.AllDirectories,
            new VersionContainer(profile.Game),
            StringComparer.OrdinalIgnoreCase);

        try
        {
            // Mappings are assigned before Initialize, matching every call site in
            // CUE4Parse.Tests. Task 11 replaces this with the "auto" resolution.
            if (profile.Mappings is { } path &&
                !string.Equals(path, "auto", StringComparison.OrdinalIgnoreCase))
            {
                provider.MappingsContainer = new FileUsmapTypeMappingsProvider(path);
            }

            provider.Initialize();

            var keys = new Dictionary<FGuid, FAesKey>();
            if (profile.MainAesKey is { } main) keys[new FGuid()] = ParseAesKey(main);
            foreach (var (guidText, keyText) in profile.DynamicKeys)
                keys[ParseAesGuid(guidText)] = ParseAesKey(keyText);

            // SubmitKeys mounts only the readers matching a submitted GUID...
            if (keys.Count > 0) provider.SubmitKeys(keys);

            // ...Mount() mounts everything else. Without it an unencrypted game
            // mounts nothing at all and every verb silently returns empty.
            provider.Mount();

            provider.PostMount();
        }
        catch (CliException)
        {
            provider.Dispose();
            throw;
        }
        catch (Exception ex)
        {
            provider.Dispose();
            throw new CliException(ExitCode.Mount, "MOUNT_FAILED", $"Failed to mount archives: {ex.Message}");
        }

        return provider;
    }

    public static FAesKey ParseAesKey(string value)
    {
        var hex = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? value[2..] : value;

        if (hex.Length != 64 || !hex.All(c => Uri.IsHexDigit(c)))
        {
            throw new CliException(
                ExitCode.AesKey, "BAD_AES_KEY",
                $"AES key must be 64 hex characters (32 bytes), optionally prefixed with '0x'. Got {hex.Length} characters.");
        }

        try
        {
            return new FAesKey(Convert.FromHexString(hex));
        }
        catch (FormatException ex)
        {
            throw new CliException(ExitCode.AesKey, "BAD_AES_KEY", $"Malformed AES key: {ex.Message}");
        }
    }

    /// <summary>
    /// FGuid exposes no TryParse: the only string entry point is a ctor requiring
    /// exactly 32 hex characters, which throws on the dashed form fortnite-api returns.
    /// </summary>
    public static FGuid ParseAesGuid(string value)
    {
        var hex = value.Trim().Trim('{', '}').Replace("-", string.Empty);

        if (hex.Length != 32 || !hex.All(Uri.IsHexDigit))
        {
            throw new CliException(
                ExitCode.AesKey, "BAD_AES_GUID",
                $"AES GUID must be 32 hex characters, with or without dashes. Got '{value}'.");
        }

        try
        {
            return new FGuid(hex);
        }
        catch (Exception ex)
        {
            throw new CliException(ExitCode.AesKey, "BAD_AES_GUID", $"Malformed AES GUID '{value}': {ex.Message}");
        }
    }

    /// <summary>
    /// Fails with the outstanding GUIDs when archives remain encrypted.
    /// Call this only after a lookup has already failed, so that "asset not found"
    /// stays distinguishable from "the archive holding it is still encrypted".
    /// </summary>
    public static void ThrowIfKeysMissing(AbstractVfsFileProvider provider)
    {
        if (provider.RequiredKeys.Count == 0) return;

        var missing = provider.RequiredKeys.Select(g => g.ToString()).ToArray();
        throw new CliException(
            ExitCode.AesKey, "AES_KEY_MISSING",
            $"{missing.Length} archive(s) remain encrypted. Supply the missing AES key(s).",
            new { missingGuids = missing });
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet run --project CUE4Parse.Cli.Tests/CUE4Parse.Cli.Tests.csproj -- --filter-method "*ProviderFactory*"`
Expected: PASS, 9 tests.

- [ ] **Step 5: Commit**

```bash
git add CUE4Parse.Cli/Services/ProviderFactory.cs CUE4Parse.Cli.Tests/ProviderFactoryTests.cs
git commit -m "feat(cli): centralize provider bootstrap with typed failure classification"
```

---

## Task 6: Root command wiring and the info command

This is the first task producing a runnable executable. It wires global options, the error boundary, Serilog-to-stderr, and one real command end to end.

**Files:**
- Create: `CUE4Parse.Cli/Commands/InfoCommand.cs`
- Create: `CUE4Parse.Cli/Services/CommandContext.cs`
- Modify: `CUE4Parse.Cli/Program.cs`
- Create: `CUE4Parse.Cli.Tests/InfoCommandTests.cs`

**Interfaces:**
- Consumes: everything from Tasks 1–5.
- Produces:
  - `sealed record CommandContext(ResolvedProfile Profile, JsonOutput Output, bool Verbose)`
  - `static class GlobalOptions` exposing the shared `Option<>` instances: `Profile`, `Config`, `Paks`, `Game`, `Mappings`, `Aes`, `Verbose`
  - `static class ContextBuilder` with `static CommandContext Build(ParseResult parseResult)`
  - `static class InfoCommand` with `static int Execute(CommandContext context)`

**Every global option must set `Recursive = true`.** This was verified: without it, subcommands reject the flags.

- [ ] **Step 1: Write the failing test**

`CUE4Parse.Cli.Tests/InfoCommandTests.cs`:

```csharp
using CUE4Parse.Cli.Output;
using CUE4Parse.Cli.Services;
using CUE4Parse.UE4.Versions;
using Newtonsoft.Json.Linq;

namespace CUE4Parse.Cli.Tests;

public class InfoCommandTests
{
    [Fact]
    public void ExecuteReportsGameVersionAndMountCountsAsJson()
    {
        var paks = Directory.CreateTempSubdirectory().FullName;
        var profile = new ResolvedProfile(paks, EGame.GAME_UE5_6, null, null, new Dictionary<string, string>());
        var sw = new StringWriter();

        var code = InfoCommand.Execute(new CommandContext(profile, new JsonOutput(sw), Verbose: false));

        Assert.Equal((int)ExitCode.Success, code);
        var parsed = JObject.Parse(sw.ToString());
        Assert.Equal("GAME_UE5_6", parsed["game"]?.Value<string>());
        Assert.Equal(0, parsed["mountedVfs"]?.Value<int>());
        Assert.Equal(0, parsed["fileCount"]?.Value<int>());
        Assert.NotNull(parsed["missingAesGuids"]);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet run --project CUE4Parse.Cli.Tests/CUE4Parse.Cli.Tests.csproj -- --filter-method "*InfoCommand*"`
Expected: build failure — `InfoCommand` does not exist.

- [ ] **Step 3: Implement CommandContext and the shared options**

`CUE4Parse.Cli/Services/CommandContext.cs`:

```csharp
using System.CommandLine;
using CUE4Parse.Cli.Output;

namespace CUE4Parse.Cli.Services;

public sealed record CommandContext(ResolvedProfile Profile, JsonOutput Output, bool Verbose);

/// <summary>
/// Shared options available to every subcommand.
/// All are <c>Recursive</c>: System.CommandLine 2.0 does not propagate root options otherwise.
/// </summary>
public static class GlobalOptions
{
    public static readonly Option<string?> Profile =
        new("--profile", "-p") { Description = "Profile name from cue4.json", Recursive = true };

    public static readonly Option<string?> Config =
        new("--config") { Description = "Path to cue4.json", Recursive = true };

    public static readonly Option<string?> Paks =
        new("--paks") { Description = "Override the paks directory", Recursive = true };

    public static readonly Option<string?> Game =
        new("--game") { Description = "Override the game version (GAME_UE5_6 or 5.6)", Recursive = true };

    public static readonly Option<string?> Mappings =
        new("--mappings") { Description = "Override the .usmap path, or 'auto'", Recursive = true };

    public static readonly Option<string?> Aes =
        new("--aes") { Description = "Override the main AES key", Recursive = true };

    public static readonly Option<bool> Verbose =
        new("--verbose", "-v") { Description = "Verbose logging to stderr", Recursive = true };

    public static void AddTo(RootCommand root)
    {
        root.Options.Add(Profile);
        root.Options.Add(Config);
        root.Options.Add(Paks);
        root.Options.Add(Game);
        root.Options.Add(Mappings);
        root.Options.Add(Aes);
        root.Options.Add(Verbose);
    }
}

public static class ContextBuilder
{
    public static CommandContext Build(ParseResult parseResult)
    {
        var configPath = ConfigLoader.FindConfigFile(
            parseResult.GetValue(GlobalOptions.Config),
            Directory.GetCurrentDirectory(),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "cue4"));

        var config = configPath is null ? new CliConfig() : ConfigLoader.Load(configPath);

        var profile = ConfigLoader.Resolve(
            config,
            parseResult.GetValue(GlobalOptions.Profile),
            new ProfileOverrides(
                PaksDir: parseResult.GetValue(GlobalOptions.Paks),
                Game: parseResult.GetValue(GlobalOptions.Game),
                Mappings: parseResult.GetValue(GlobalOptions.Mappings),
                Aes: parseResult.GetValue(GlobalOptions.Aes)));

        return new CommandContext(
            profile,
            new JsonOutput(Console.Out),
            parseResult.GetValue(GlobalOptions.Verbose));
    }
}
```

- [ ] **Step 4: Implement InfoCommand**

`CUE4Parse.Cli/Commands/InfoCommand.cs`:

```csharp
using CUE4Parse.Cli.Output;
using CUE4Parse.Cli.Services;

namespace CUE4Parse.Cli.Commands;

public static class InfoCommand
{
    public static int Execute(CommandContext context)
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
        }, indent: true);

        return (int)ExitCode.Success;
    }
}
```

- [ ] **Step 5: Implement Program.cs with the error boundary**

`CUE4Parse.Cli/Program.cs`:

```csharp
using System.CommandLine;
using CUE4Parse;
using CUE4Parse.Cli.Commands;
using CUE4Parse.Cli.Output;
using CUE4Parse.Cli.Services;
using CUE4Parse.UE4.Exceptions;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.SystemConsole.Themes;

var root = new RootCommand("cue4 - CUE4Parse command line interface");
GlobalOptions.AddTo(root);

var infoCmd = new Command("info", "Report mount state, game version and missing AES keys");
infoCmd.SetAction(pr => Run(pr, InfoCommand.Execute));
root.Subcommands.Add(infoCmd);

var parseResult = root.Parse(args);

// System.CommandLine exits 1 on parse errors; the contract requires 2.
if (parseResult.Errors.Count > 0)
{
    new JsonOutput(Console.Out).WriteError(
        "USAGE", string.Join("; ", parseResult.Errors.Select(e => e.Message)));
    return (int)ExitCode.Usage;
}

return await parseResult.InvokeAsync();

static int Run(ParseResult parseResult, Func<CommandContext, int> body)
{
    ConfigureLogging(parseResult.GetValue(GlobalOptions.Verbose));

    try
    {
        return body(ContextBuilder.Build(parseResult));
    }
    catch (Exception ex)
    {
        return Classify(ex);
    }
}

// Single classification point, shared with RunAsync (Task 10), so the two
// boundaries can never drift apart.
static int Classify(Exception ex)
{
    var output = new JsonOutput(Console.Out);

    switch (ex)
    {
        case CliException cli:
            output.WriteError(cli.ErrorCode, cli.Message, cli.Details);
            return (int)cli.ExitCode;

        // CUE4Parse throws this from AbstractUePackage.CanDeserialize when a
        // package has unversioned properties and no .usmap is loaded. Without
        // this arm the CLI would emit plausible-looking but wrong JSON.
        case MappingException:
            output.WriteError(
                "MAPPINGS_REQUIRED",
                "This package uses unversioned properties and cannot be read without a .usmap. " +
                "Pass --mappings <file>, or --mappings auto after running 'cue4 update'.",
                new { detail = ex.Message });
            return (int)ExitCode.Mappings;

        default:
            Log.Debug(ex, "Unhandled exception");
            output.WriteError("INTERNAL", ex.Message);
            return (int)ExitCode.Error;
    }
}

// Logging goes to stderr so stdout carries only structured data.
static void ConfigureLogging(bool verbose)
{
    Log.Logger = new LoggerConfiguration()
        .MinimumLevel.Is(verbose ? LogEventLevel.Debug : LogEventLevel.Warning)
        .WriteTo.Console(standardErrorFromLevel: LogEventLevel.Verbose, theme: AnsiConsoleTheme.Literate)
        .CreateLogger();

    CUE4ParseLog.UseLogger(Log.Logger);
}
```

> `CUE4ParseLog` lives in the `CUE4Parse` namespace and `MappingException` in
> `CUE4Parse.UE4.Exceptions`; both usings are already in the header above.

- [ ] **Step 6: Run the test to verify it passes**

Run: `dotnet run --project CUE4Parse.Cli.Tests/CUE4Parse.Cli.Tests.csproj -- --filter-method "*InfoCommand*"`
Expected: PASS, 1 test.

- [ ] **Step 7: Verify the executable behaves correctly end to end**

```bash
dotnet build CUE4Parse.Cli.Tests/CUE4Parse.Cli.Tests.csproj -c Release
FIX=CUE4Parse.Cli.Tests/bin/Release/net10.0/Fixtures/UE5_8
CUE4=./CUE4Parse.Cli/bin/Release/net10.0/cue4.exe

$CUE4 --help
$CUE4 info --paks "$FIX/LegacyPak/Unversioned/Oodle" --game 5.8 \
  --mappings "$FIX/Mappings/CUE4ParseFixtures-Oodle.usmap"
$CUE4 info --nonsense; echo "exit=$?"
```

Expected: help lists `info` and the seven global options; `info` prints indented
JSON with a **non-zero `fileCount`** (a zero here means `Mount()` is missing);
the bad flag prints a JSON `USAGE` error and exits **2**.

- [ ] **Step 8: Commit**

```bash
git add CUE4Parse.Cli CUE4Parse.Cli.Tests
git commit -m "feat(cli): wire root command, error boundary and info command"
```

---

## Task 7: list command

**Files:**
- Create: `CUE4Parse.Cli/Commands/ListCommand.cs`
- Create: `CUE4Parse.Cli.Tests/FixtureSupport.cs`
- Modify: `CUE4Parse.Cli/Program.cs`
- Create: `CUE4Parse.Cli.Tests/ListCommandTests.cs`

**Interfaces:**
- Consumes: `CommandContext`, `AssetMatcher`, `ProviderFactory`.
- Produces:
  - `sealed record ListOptions(MatchCriteria Criteria, bool CountOnly)`
  - `static class ListCommand` with `static int Execute(CommandContext context, ListOptions options)`
  - `static class CriteriaOptions` with the shared `--glob`/`--regex`/`--ext`/`--limit` `Option<>` instances and `static MatchCriteria Read(ParseResult pr)` — **registered on `list`, `dump`, `unpack` and `export` alike.**
  - `static class FixtureSupport` with `static string Dir(params string[] parts)`, `static ResolvedProfile Profile()` and `static ResolvedProfile EncryptedProfile()` — **shared by Tasks 7, 8 and 9; do not duplicate it in later test files.**

`list` is exempt from the safety limit — enumerating paths is cheap and is how a caller discovers what a glob would match. `--limit` still truncates when explicitly given.

**All four matching verbs share one option set.** The original draft gave `list`
`--glob/--regex/--ext/--limit` but `dump`, `unpack` and `export` only `--glob`,
so a caller could not narrow a bulk operation the same way they discovered it.
That asymmetry is the single most likely thing to trip an agent, and it costs
nothing to remove.

### Which fixture

The obvious-looking `Fixtures/UE5_8/Pak/Oodle` is a **minimal** pak whose entire
contents are `AssetRegistry.bin`, `DefaultGame.ini` and two `.locres` files —
**no `.uasset`.** Tests pointed at it match nothing and fail. Use
`LegacyPak/Unversioned/Oodle`, which holds the real cooked packages and is
unencrypted; the `.usmap` is required because that variant is unversioned.
`LegacyPak/Unversioned/OodleEncrypted` is the same content behind the fixture
AES key, and exercises the `SubmitKeys` path.

The fixture AES key is the ASCII string `CUE4ParseFixtureAESKey0123456789`; the CLI takes hex, so `FixtureSupport` converts it.

- [ ] **Step 1: Write the shared fixture helper**

`CUE4Parse.Cli.Tests/FixtureSupport.cs`:

```csharp
using System.Text;
using CUE4Parse.Cli.Services;
using CUE4Parse.UE4.Versions;

namespace CUE4Parse.Cli.Tests;

/// <summary>
/// Points the CLI at the real fixture archives. The csproj links
/// CUE4Parse.Tests/Fixtures into this project's own output, so the paths are
/// configuration-independent.
/// </summary>
public static class FixtureSupport
{
    private const string AesKeyText = "CUE4ParseFixtureAESKey0123456789";

    public static string Dir(params string[] parts)
        => Path.GetFullPath(Path.Combine([AppContext.BaseDirectory, "Fixtures", "UE5_8", .. parts]));

    private static string Mappings() => Dir("Mappings", "CUE4ParseFixtures-Oodle.usmap");

    private static string RequireDir(params string[] parts)
    {
        var path = Dir(parts);
        Assert.True(Directory.Exists(path), $"Missing fixture directory: {path}");
        return path;
    }

    /// <summary>Unencrypted cooked packages. No AES key needed; mappings are.</summary>
    public static ResolvedProfile Profile() => new(
        RequireDir("LegacyPak", "Unversioned", "Oodle"),
        EGame.GAME_UE5_8,
        Mappings(),
        MainAesKey: null,
        new Dictionary<string, string>());

    /// <summary>Same content behind the fixture AES key, for the SubmitKeys path.</summary>
    public static ResolvedProfile EncryptedProfile() => new(
        RequireDir("LegacyPak", "Unversioned", "OodleEncrypted"),
        EGame.GAME_UE5_8,
        Mappings(),
        "0x" + Convert.ToHexString(Encoding.ASCII.GetBytes(AesKeyText)),
        new Dictionary<string, string>());
}
```

- [ ] **Step 2: Write the failing test**

`CUE4Parse.Cli.Tests/ListCommandTests.cs`:

```csharp
using CUE4Parse.Cli.Commands;
using CUE4Parse.Cli.Output;
using CUE4Parse.Cli.Services;
using Newtonsoft.Json.Linq;

namespace CUE4Parse.Cli.Tests;

public class ListCommandTests
{
    [Fact]
    public void ExecuteEmitsOneNdjsonObjectPerAssetWithPathAndSize()
    {
        var sw = new StringWriter();
        var context = new CommandContext(FixtureSupport.Profile(), new JsonOutput(sw), Verbose: false);

        var code = ListCommand.Execute(context, new ListOptions(
            new MatchCriteria(Globs: ["**/*.uasset"], Limit: 3), CountOnly: false));

        Assert.Equal((int)ExitCode.Success, code);

        var lines = sw.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.NotEmpty(lines);
        Assert.True(lines.Length <= 3, "Limit was not applied.");

        foreach (var line in lines)
        {
            var parsed = JObject.Parse(line);
            Assert.EndsWith(".uasset", parsed["path"]?.Value<string>());
            Assert.NotNull(parsed["size"]);
        }
    }

    /// <summary>
    /// Regression guard for the bootstrap. This profile carries no AES key, so if
    /// ProviderFactory ever drops provider.Mount() again, nothing mounts and the
    /// count is zero rather than an error — a silent, total failure.
    /// </summary>
    [Fact]
    public void ExecuteWithCountOnlyEmitsASingleCountObject()
    {
        var sw = new StringWriter();
        var context = new CommandContext(FixtureSupport.Profile(), new JsonOutput(sw), Verbose: false);

        var code = ListCommand.Execute(context, new ListOptions(new MatchCriteria(), CountOnly: true));

        Assert.Equal((int)ExitCode.Success, code);
        var parsed = JObject.Parse(sw.ToString());
        Assert.True(parsed["count"]?.Value<int>() > 0,
            "Nothing mounted. ProviderFactory must call provider.Mount(): Initialize() only registers readers.");
    }

    [Fact]
    public void ExecuteMountsAnEncryptedArchiveWhenTheKeyIsSupplied()
    {
        var sw = new StringWriter();
        var context = new CommandContext(FixtureSupport.EncryptedProfile(), new JsonOutput(sw), Verbose: false);

        var code = ListCommand.Execute(context, new ListOptions(new MatchCriteria(), CountOnly: true));

        Assert.Equal((int)ExitCode.Success, code);
        Assert.True(JObject.Parse(sw.ToString())["count"]?.Value<int>() > 0);
    }

    [Fact]
    public void ExecuteEmitsEachPathOnlyOnce()
    {
        var sw = new StringWriter();
        var context = new CommandContext(FixtureSupport.Profile(), new JsonOutput(sw), Verbose: false);

        ListCommand.Execute(context, new ListOptions(new MatchCriteria(Globs: ["**/*.uasset"]), CountOnly: false));

        // FileProviderDictionary.Keys concatenates every mounted index and can repeat.
        var paths = sw.ToString()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JObject.Parse(line)["path"]!.Value<string>())
            .ToArray();

        Assert.Equal(paths.Length, paths.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void ExecuteWithANonMatchingGlobEmitsNothingAndStillSucceeds()
    {
        var sw = new StringWriter();
        var context = new CommandContext(FixtureSupport.Profile(), new JsonOutput(sw), Verbose: false);

        var code = ListCommand.Execute(context, new ListOptions(
            new MatchCriteria(Globs: ["**/NoSuchFolder/**"]), CountOnly: false));

        Assert.Equal((int)ExitCode.Success, code);
        Assert.Empty(sw.ToString().Trim());
    }
}
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `dotnet run --project CUE4Parse.Cli.Tests/CUE4Parse.Cli.Tests.csproj -- --filter-method "*ListCommand*"`
Expected: build failure — `ListCommand` does not exist.

(The fixtures are linked into this project's own output by its csproj, so
building `CUE4Parse.Tests` first is not required.)

- [ ] **Step 4: Implement ListCommand**

`CUE4Parse.Cli/Commands/ListCommand.cs`:

```csharp
using CUE4Parse.Cli.Output;
using CUE4Parse.Cli.Services;

namespace CUE4Parse.Cli.Commands;

public sealed record ListOptions(MatchCriteria Criteria, bool CountOnly);

public static class ListCommand
{
    public static int Execute(CommandContext context, ListOptions options)
    {
        using var provider = ProviderFactory.Create(context.Profile);

        var matches = AssetMatcher.Filter(provider.Files.Keys, options.Criteria);

        if (options.CountOnly)
        {
            // totalMatched is reported alongside count so a caller can tell a
            // truncated listing from a complete one without a second invocation.
            context.Output.WriteResult(new
            {
                count = matches.Paths.Count,
                totalMatched = matches.TotalMatched,
                truncated = matches.Paths.Count < matches.TotalMatched,
            });
            return (int)ExitCode.Success;
        }

        foreach (var path in matches.Paths)
        {
            var file = provider.Files[path];
            context.Output.WriteLine(new
            {
                path,
                size = file.Size,
                extension = file.Extension,
                encrypted = file.IsEncrypted,
            });
        }

        return (int)ExitCode.Success;
    }
}
```

- [ ] **Step 5: Wire the command into Program.cs**

First add the shared criteria option set to
`CUE4Parse.Cli/Services/CommandContext.cs`, so `list`, `dump`, `unpack` and
`export` all accept the same filters:

```csharp
/// <summary>
/// The matching options shared by list, dump, unpack and export.
/// These are per-command (not Recursive) — each verb registers them via AddTo.
/// </summary>
public static class CriteriaOptions
{
    public static readonly Option<string[]> Glob =
        new("--glob") { Description = "Glob pattern, repeatable; ** crosses separators" };

    public static readonly Option<string?> Regex =
        new("--regex") { Description = "Regex applied to the asset path" };

    public static readonly Option<string?> Ext =
        new("--ext") { Description = "Filter by file extension" };

    public static readonly Option<int?> Limit =
        new("--limit") { Description = "Take only the first N matches (implies --force)" };

    public static void AddTo(Command command)
    {
        command.Options.Add(Glob);
        command.Options.Add(Regex);
        command.Options.Add(Ext);
        command.Options.Add(Limit);
    }

    public static MatchCriteria Read(ParseResult pr) => new(
        Globs: pr.GetValue(Glob),
        Regex: pr.GetValue(Regex),
        Extension: pr.GetValue(Ext),
        Limit: pr.GetValue(Limit));
}
```

Then, in `Program.cs`, insert before `var parseResult = root.Parse(args);`:

```csharp
var countOpt = new Option<bool>("--count") { Description = "Print only the match count" };

var listCmd = new Command("list", "Enumerate assets in the mounted VFS");
CriteriaOptions.AddTo(listCmd);
listCmd.Options.Add(countOpt);
listCmd.SetAction(pr => Run(pr, ctx => ListCommand.Execute(ctx, new ListOptions(
    CriteriaOptions.Read(pr),
    CountOnly: pr.GetValue(countOpt)))));
root.Subcommands.Add(listCmd);
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet run --project CUE4Parse.Cli.Tests/CUE4Parse.Cli.Tests.csproj -- --filter-method "*ListCommand*"`
Expected: PASS, 5 tests.

- [ ] **Step 7: Verify against a real fixture archive**

```bash
dotnet build CUE4Parse.Cli.Tests/CUE4Parse.Cli.Tests.csproj -c Release
FIX=CUE4Parse.Cli.Tests/bin/Release/net10.0/Fixtures/UE5_8
./CUE4Parse.Cli/bin/Release/net10.0/cue4.exe list \
  --paks "$FIX/LegacyPak/Unversioned/Oodle" --game 5.8 \
  --mappings "$FIX/Mappings/CUE4ParseFixtures-Oodle.usmap" --count
```

Expected: a JSON object with a non-zero `count`. A `count` of 0 means
`ProviderFactory` is not calling `Mount()`.

- [ ] **Step 8: Commit**

```bash
git add CUE4Parse.Cli CUE4Parse.Cli.Tests
git commit -m "feat(cli): add list command with glob, regex and extension filters"
```

---

## Task 8: dump command

**Files:**
- Create: `CUE4Parse.Cli/Commands/DumpCommand.cs`
- Modify: `CUE4Parse.Cli/Program.cs`
- Create: `CUE4Parse.Cli.Tests/DumpCommandTests.cs`

**Interfaces:**
- Consumes: `CommandContext`, `AssetMatcher`, `ProviderFactory`, `FixtureSupport` (Task 7, tests only).
- Produces: `sealed record DumpOptions(string[] Paths, MatchCriteria Criteria, string? ExportName, string? ClassName, FileInfo? Output, bool Indent, bool Force)`; `static class DumpCommand` with `static int Execute(CommandContext context, DumpOptions options)`.

Single path with no `--glob` writes one JSON object. Multiple paths or a glob emits NDJSON, one object per asset, regardless of `-o`.

`--class <name>` filters exports by class name **after** loading. This is where
the spec's `list --class` belongs: knowing an asset's class requires
deserializing it, which contradicts `list` being the cheap discovery verb. Task
12 updates the spec's usage block to match.

Serialization uses `JToken.FromObject`, not a `SerializeObject`/`DeserializeObject`
string round-trip. Both run CUE4Parse's `UObjectConverter` (declared by the
`[JsonConverter]` attribute on `UObject`), but the round-trip serializes the
whole export graph twice and materializes an intermediate string per asset.

Tests use `FixtureSupport.Profile()` from Task 7. Do not redefine the fixture helper here.

- [ ] **Step 1: Write the failing test**

`CUE4Parse.Cli.Tests/DumpCommandTests.cs`:

```csharp
using CUE4Parse.Cli.Commands;
using CUE4Parse.Cli.Output;
using CUE4Parse.Cli.Services;
using Newtonsoft.Json.Linq;

namespace CUE4Parse.Cli.Tests;

public class DumpCommandTests
{
    [Fact]
    public void DumpEmitsNdjsonWithOneObjectPerMatchedAsset()
    {
        var sw = new StringWriter();
        var context = new CommandContext(FixtureSupport.Profile(), new JsonOutput(sw), Verbose: false);

        var code = DumpCommand.Execute(context, new DumpOptions(
            Paths: [],
            Criteria: new MatchCriteria(Globs: ["**/*.uasset"], Limit: 3),
            ExportName: null, ClassName: null, Output: null, Indent: false, Force: false));

        Assert.Equal((int)ExitCode.Success, code);

        var lines = sw.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.NotEmpty(lines);
        foreach (var line in lines)
        {
            var parsed = JObject.Parse(line);
            Assert.NotNull(parsed["path"]);
            Assert.NotNull(parsed["status"]);
        }
    }

    [Fact]
    public void DumpDeserializesUnversionedPropertiesUsingTheFixtureMappings()
    {
        var sw = new StringWriter();
        var context = new CommandContext(FixtureSupport.Profile(), new JsonOutput(sw), Verbose: false);

        var code = DumpCommand.Execute(context, new DumpOptions(
            Paths: ["CUE4ParseFixtures/Content/Fixtures/Properties/DA_AllProperties.uasset"],
            Criteria: new MatchCriteria(),
            ExportName: null, ClassName: null, Output: null, Indent: false, Force: false));

        Assert.Equal((int)ExitCode.Success, code);
        // 0x12345678 — the deterministic fixture value asserted by CUE4Parse.Tests.
        Assert.Contains("305419896", sw.ToString());
    }

    [Fact]
    public void DumpFiltersExportsByClassName()
    {
        var sw = new StringWriter();
        var context = new CommandContext(FixtureSupport.Profile(), new JsonOutput(sw), Verbose: false);

        DumpCommand.Execute(context, new DumpOptions(
            Paths: [],
            Criteria: new MatchCriteria(Globs: ["**/*.uasset"]),
            ExportName: null, ClassName: "Texture2D", Output: null, Indent: false, Force: true));

        var lines = sw.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.NotEmpty(lines);
        Assert.All(lines, line => Assert.Equal("ok", JObject.Parse(line)["status"]?.Value<string>()));
    }

    [Fact]
    public void DumpReturnsNotFoundForAnUnknownExplicitPath()
    {
        var sw = new StringWriter();
        var context = new CommandContext(FixtureSupport.Profile(), new JsonOutput(sw), Verbose: false);

        var ex = Assert.Throws<CliException>(() => DumpCommand.Execute(context, new DumpOptions(
            Paths: ["Nope/Does/Not/Exist.uasset"],
            Criteria: new MatchCriteria(),
            ExportName: null, ClassName: null, Output: null, Indent: false, Force: false)));

        Assert.Equal(ExitCode.NotFound, ex.ExitCode);
        Assert.Equal("ASSET_NOT_FOUND", ex.ErrorCode);
    }

    /// <summary>
    /// The whole reason exit 5 is separate from exit 7: when an archive is still
    /// encrypted, "supply a key" and "fix the path" are different remedies.
    /// </summary>
    [Fact]
    public void DumpReportsMissingAesKeysRatherThanNotFoundWhenArchivesAreStillEncrypted()
    {
        var profile = FixtureSupport.EncryptedProfile() with { MainAesKey = null };
        var context = new CommandContext(profile, new JsonOutput(new StringWriter()), Verbose: false);

        var ex = Assert.Throws<CliException>(() => DumpCommand.Execute(context, new DumpOptions(
            Paths: ["CUE4ParseFixtures/Content/Fixtures/Properties/DA_AllProperties.uasset"],
            Criteria: new MatchCriteria(),
            ExportName: null, ClassName: null, Output: null, Indent: false, Force: false)));

        Assert.Equal(ExitCode.AesKey, ex.ExitCode);
        Assert.Equal("AES_KEY_MISSING", ex.ErrorCode);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet run --project CUE4Parse.Cli.Tests/CUE4Parse.Cli.Tests.csproj -- --filter-method "*DumpCommand*"`
Expected: build failure — `DumpCommand` does not exist.

- [ ] **Step 3: Implement DumpCommand**

`CUE4Parse.Cli/Commands/DumpCommand.cs`:

```csharp
using CUE4Parse.Cli.Output;
using CUE4Parse.Cli.Services;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports;
using Newtonsoft.Json.Linq;

namespace CUE4Parse.Cli.Commands;

public sealed record DumpOptions(
    string[] Paths,
    MatchCriteria Criteria,
    string? ExportName,
    string? ClassName,
    FileInfo? Output,
    bool Indent,
    bool Force);

public static class DumpCommand
{
    public static int Execute(CommandContext context, DumpOptions options)
    {
        using var provider = ProviderFactory.Create(context.Profile);

        var targets = TargetResolver.Resolve(provider, options.Paths, options.Criteria, options.Force);

        using var writer = options.Output is null
            ? null
            : new StreamWriter(options.Output.FullName, append: false);

        var output = writer is null ? context.Output : new JsonOutput(writer);
        var single = targets.Count == 1 && options.Paths.Length == 1;

        foreach (var path in targets)
        {
            try
            {
                var payload = Load(provider, path, options);

                if (single)
                {
                    output.WriteResult(payload, options.Indent);
                    return (int)ExitCode.Success;
                }

                output.WriteLine(new { path, status = "ok", data = payload });
            }
            catch (Exception ex)
            {
                // A single explicit target rethrows so the error boundary can classify it
                // (notably MappingException -> exit 6). In NDJSON mode, per-asset failures
                // are data, not a process failure.
                if (single) throw;
                output.WriteLine(new { path, status = "error", message = ex.Message });
            }
        }

        return (int)ExitCode.Success;
    }

    /// <summary>
    /// JToken.FromObject runs the same [JsonConverter(typeof(UObjectConverter))]
    /// declared on UObject, without serializing the export graph to a string and
    /// parsing it back only to serialize it a second time.
    /// </summary>
    private static JToken Load(AbstractFileProvider provider, string path, DumpOptions options)
    {
        if (options.ExportName is { } name)
            return JToken.FromObject(provider.LoadPackageObject(path, name));

        IEnumerable<UObject> exports = provider.LoadPackage(path).GetExports();

        if (options.ClassName is { } className)
        {
            exports = exports.Where(e =>
                string.Equals(e.ExportType, className, StringComparison.OrdinalIgnoreCase));
        }

        return JToken.FromObject(exports.ToArray());
    }
}
```

- [ ] **Step 3b: Implement the shared target resolver**

`CUE4Parse.Cli/Services/TargetResolver.cs` — used identically by `dump`, `unpack`
and `export`, which previously carried three copies of this block:

```csharp
using CUE4Parse.Cli.Output;
using CUE4Parse.FileProvider.Vfs;

namespace CUE4Parse.Cli.Services;

public static class TargetResolver
{
    public static IReadOnlyList<string> Resolve(
        AbstractVfsFileProvider provider, string[] explicitPaths, MatchCriteria criteria, bool force)
    {
        if (explicitPaths.Length == 0)
        {
            var matches = AssetMatcher.Filter(provider.Files.Keys, criteria);
            AssetMatcher.EnforceLimit(matches, criteria, force);
            return matches.Paths;
        }

        // ContainsKey beats materializing a HashSet of every mounted path.
        var missing = explicitPaths.Where(p => !provider.Files.ContainsKey(p)).ToArray();
        if (missing.Length > 0)
        {
            // Distinguish "no such asset" from "the archive holding it is still
            // encrypted" — the reason exit 5 and exit 7 are separate codes.
            ProviderFactory.ThrowIfKeysMissing(provider);

            throw new CliException(
                ExitCode.NotFound, "ASSET_NOT_FOUND",
                $"{missing.Length} asset path(s) not found in the mounted archives.",
                new { missing });
        }

        AssetMatcher.EnforceLimit(
            new MatchResult(explicitPaths, explicitPaths.Length), criteria, force);
        return explicitPaths;
    }
}
```

- [ ] **Step 4: Wire the command into Program.cs**

```csharp
var dumpPathsArg = new Argument<string[]>("paths")
    { Description = "Asset paths", Arity = ArgumentArity.ZeroOrMore };
var dumpExportOpt = new Option<string?>("--export") { Description = "Serialize only this named export" };
var dumpClassOpt = new Option<string?>("--class") { Description = "Keep only exports of this class" };
var dumpOutOpt = new Option<FileInfo?>("--output", "-o") { Description = "Write to a file instead of stdout" };
var dumpIndentOpt = new Option<bool>("--indent") { Description = "Pretty-print single-asset output" };
var dumpForceOpt = new Option<bool>("--force") { Description = "Bypass the 1000-asset safety limit" };

var dumpCmd = new Command("dump", "Deserialize exports to JSON");
dumpCmd.Arguments.Add(dumpPathsArg);
CriteriaOptions.AddTo(dumpCmd);
dumpCmd.Options.Add(dumpExportOpt);
dumpCmd.Options.Add(dumpClassOpt);
dumpCmd.Options.Add(dumpOutOpt);
dumpCmd.Options.Add(dumpIndentOpt);
dumpCmd.Options.Add(dumpForceOpt);
dumpCmd.SetAction(pr => Run(pr, ctx => DumpCommand.Execute(ctx, new DumpOptions(
    Paths: pr.GetValue(dumpPathsArg) ?? [],
    Criteria: CriteriaOptions.Read(pr),
    ExportName: pr.GetValue(dumpExportOpt),
    ClassName: pr.GetValue(dumpClassOpt),
    Output: pr.GetValue(dumpOutOpt),
    Indent: pr.GetValue(dumpIndentOpt),
    Force: pr.GetValue(dumpForceOpt)))));
root.Subcommands.Add(dumpCmd);
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet run --project CUE4Parse.Cli.Tests/CUE4Parse.Cli.Tests.csproj -- --filter-method "*DumpCommand*"`
Expected: PASS, 5 tests.

- [ ] **Step 6: Commit**

```bash
git add CUE4Parse.Cli CUE4Parse.Cli.Tests
git commit -m "feat(cli): add dump command emitting JSON and NDJSON export data"
```

---

## Task 9: unpack command

**Files:**
- Create: `CUE4Parse.Cli/Commands/UnpackCommand.cs`
- Modify: `CUE4Parse.Cli/Program.cs`
- Create: `CUE4Parse.Cli.Tests/UnpackCommandTests.cs`

**Interfaces:**
- Consumes: `CommandContext`, `AssetMatcher`, `ProviderFactory`, `FixtureSupport` (Task 7, tests only).
- Produces: `sealed record UnpackOptions(string[] Paths, MatchCriteria Criteria, DirectoryInfo Output, bool Flat, bool Force)`; `static class UnpackCommand` with `static int Execute(CommandContext context, UnpackOptions options)`.

**Uses `provider.SavePackage(path)` for UE packages, `SaveAsset(path)` only for
everything else.** `SaveAsset` returns a single file's bytes, but a cooked UE
package is `.uasset` + `.uexp` (+ `.ubulk`/`.uptnl`). Writing only the `.uasset`
produces a file no tool — including CUE4Parse — can open. `SavePackage` returns
a `path -> bytes` dictionary of every payload file; `GameFile.IsUePackage`
selects between the two.

Preserves directory structure under `-o` unless `--flat`.

`--flat` **fails on filename collisions** rather than silently overwriting. Two
assets with the same leaf name are common, and a caller who ends up with fewer
files than they asked for and no error has been given a wrong answer.

Tests use `FixtureSupport.Profile()` from Task 7. Do not redefine the fixture helper here.

- [ ] **Step 1: Write the failing test**

`CUE4Parse.Cli.Tests/UnpackCommandTests.cs`:

```csharp
using CUE4Parse.Cli.Commands;
using CUE4Parse.Cli.Output;
using CUE4Parse.Cli.Services;
using Newtonsoft.Json.Linq;

namespace CUE4Parse.Cli.Tests;

public class UnpackCommandTests
{
    [Fact]
    public void UnpackWritesFilesAndReportsEachAsNdjson()
    {
        var outDir = new DirectoryInfo(Directory.CreateTempSubdirectory().FullName);
        var sw = new StringWriter();
        var context = new CommandContext(FixtureSupport.Profile(), new JsonOutput(sw), Verbose: false);

        var code = UnpackCommand.Execute(context, new UnpackOptions(
            Paths: [],
            Criteria: new MatchCriteria(Globs: ["**/*.uasset"], Limit: 2),
            Output: outDir, Flat: false, Force: false));

        Assert.Equal((int)ExitCode.Success, code);
        Assert.NotEmpty(outDir.GetFiles("*", SearchOption.AllDirectories));

        foreach (var line in sw.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries))
            Assert.Equal("ok", JObject.Parse(line)["status"]?.Value<string>());
    }

    /// <summary>
    /// A .uasset without its .uexp is unopenable. SaveAsset returns only the one
    /// file; SavePackage returns every payload file for the package.
    /// </summary>
    [Fact]
    public void UnpackWritesTheUexpPayloadAlongsideTheUasset()
    {
        var outDir = new DirectoryInfo(Directory.CreateTempSubdirectory().FullName);
        var context = new CommandContext(FixtureSupport.Profile(), new JsonOutput(new StringWriter()), Verbose: false);

        UnpackCommand.Execute(context, new UnpackOptions(
            Paths: ["CUE4ParseFixtures/Content/Fixtures/Properties/DA_AllProperties.uasset"],
            Criteria: new MatchCriteria(),
            Output: outDir, Flat: false, Force: false));

        var written = outDir.GetFiles("*", SearchOption.AllDirectories).Select(f => f.Name).ToArray();
        Assert.Contains("DA_AllProperties.uasset", written);
        Assert.Contains("DA_AllProperties.uexp", written);
    }

    [Fact]
    public void UnpackFlatWritesAllFilesIntoTheOutputRoot()
    {
        var outDir = new DirectoryInfo(Directory.CreateTempSubdirectory().FullName);
        var context = new CommandContext(FixtureSupport.Profile(), new JsonOutput(new StringWriter()), Verbose: false);

        UnpackCommand.Execute(context, new UnpackOptions(
            Paths: ["CUE4ParseFixtures/Content/Fixtures/Properties/DA_AllProperties.uasset"],
            Criteria: new MatchCriteria(),
            Output: outDir, Flat: true, Force: false));

        Assert.Empty(outDir.GetDirectories());
        Assert.NotEmpty(outDir.GetFiles());
    }

    [Fact]
    public void UnpackFlatFailsOnAFilenameCollisionInsteadOfOverwriting()
    {
        var outDir = new DirectoryInfo(Directory.CreateTempSubdirectory().FullName);
        var context = new CommandContext(FixtureSupport.Profile(), new JsonOutput(new StringWriter()), Verbose: false);

        // Every package contributes at least .uasset + .uexp under the same leaf name,
        // so a broad flat unpack always collides.
        var ex = Assert.Throws<CliException>(() => UnpackCommand.Execute(context, new UnpackOptions(
            Paths: [],
            Criteria: new MatchCriteria(Globs: ["**/*"]),
            Output: outDir, Flat: true, Force: true)));

        Assert.Equal(ExitCode.Usage, ex.ExitCode);
        Assert.Equal("OUTPUT_COLLISION", ex.ErrorCode);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet run --project CUE4Parse.Cli.Tests/CUE4Parse.Cli.Tests.csproj -- --filter-method "*UnpackCommand*"`
Expected: build failure — `UnpackCommand` does not exist.

- [ ] **Step 3: Implement UnpackCommand**

`CUE4Parse.Cli/Commands/UnpackCommand.cs`:

```csharp
using CUE4Parse.Cli.Output;
using CUE4Parse.Cli.Services;

namespace CUE4Parse.Cli.Commands;

public sealed record UnpackOptions(
    string[] Paths,
    MatchCriteria Criteria,
    DirectoryInfo Output,
    bool Flat,
    bool Force);

public static class UnpackCommand
{
    public static int Execute(CommandContext context, UnpackOptions options)
    {
        using var provider = ProviderFactory.Create(context.Profile);

        var targets = TargetResolver.Resolve(provider, options.Paths, options.Criteria, options.Force);
        options.Output.Create();

        var written = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var failed = 0;

        foreach (var path in targets)
        {
            try
            {
                // A .uasset alone is unusable: the exports live in the .uexp.
                // SavePackage returns every payload file for the package.
                var files = provider.Files[path].IsUePackage
                    ? provider.SavePackage(path)
                    : new Dictionary<string, byte[]> { [path] = provider.SaveAsset(path) };

                var outputs = new List<string>(files.Count);
                var bytes = 0L;

                foreach (var (filePath, data) in files)
                {
                    var destination = Destination(options, filePath, written);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.WriteAllBytes(destination, data);
                    outputs.Add(destination);
                    bytes += data.Length;
                }

                context.Output.WriteLine(new { path, status = "ok", bytes, output = outputs });
            }
            catch (CliException)
            {
                throw;  // OUTPUT_COLLISION is a usage error, not a per-item failure.
            }
            catch (Exception ex)
            {
                failed++;
                context.Output.WriteLine(new { path, status = "error", message = ex.Message });
            }
        }

        return failed > 0 ? (int)ExitCode.PartialExport : (int)ExitCode.Success;
    }

    /// <summary>
    /// Fails loudly on a --flat name clash. Silently overwriting would hand the
    /// caller fewer files than they asked for with no way to notice.
    /// </summary>
    private static string Destination(
        UnpackOptions options, string filePath, Dictionary<string, string> written)
    {
        var destination = options.Flat
            ? Path.Combine(options.Output.FullName, Path.GetFileName(filePath))
            : Path.Combine(options.Output.FullName, filePath.Replace('/', Path.DirectorySeparatorChar));

        if (!written.TryAdd(destination, filePath) && written[destination] != filePath)
        {
            throw new CliException(
                ExitCode.Usage, "OUTPUT_COLLISION",
                $"'{filePath}' and '{written[destination]}' both map to '{destination}'. " +
                "Drop --flat to preserve the directory structure.",
                new { destination, first = written[destination], second = filePath });
        }

        return destination;
    }
}
```

- [ ] **Step 4: Wire the command into Program.cs**

```csharp
var unpackPathsArg = new Argument<string[]>("paths")
    { Description = "Asset paths", Arity = ArgumentArity.ZeroOrMore };
var unpackOutOpt = new Option<DirectoryInfo>("--output", "-o")
    { Description = "Output directory", Required = true };
var unpackFlatOpt = new Option<bool>("--flat") { Description = "Ignore directory structure" };
var unpackForceOpt = new Option<bool>("--force") { Description = "Bypass the 1000-asset safety limit" };

var unpackCmd = new Command("unpack", "Extract raw asset bytes");
unpackCmd.Arguments.Add(unpackPathsArg);
CriteriaOptions.AddTo(unpackCmd);
unpackCmd.Options.Add(unpackOutOpt);
unpackCmd.Options.Add(unpackFlatOpt);
unpackCmd.Options.Add(unpackForceOpt);
unpackCmd.SetAction(pr => Run(pr, ctx => UnpackCommand.Execute(ctx, new UnpackOptions(
    Paths: pr.GetValue(unpackPathsArg) ?? [],
    Criteria: CriteriaOptions.Read(pr),
    Output: pr.GetValue(unpackOutOpt)!,
    Flat: pr.GetValue(unpackFlatOpt),
    Force: pr.GetValue(unpackForceOpt)))));
root.Subcommands.Add(unpackCmd);
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet run --project CUE4Parse.Cli.Tests/CUE4Parse.Cli.Tests.csproj -- --filter-method "*UnpackCommand*"`
Expected: PASS, 4 tests.

- [ ] **Step 6: Commit**

```bash
git add CUE4Parse.Cli CUE4Parse.Cli.Tests
git commit -m "feat(cli): add unpack command for raw asset extraction"
```

---

## Task 10: export command

**Files:**
- Create: `CUE4Parse.Cli/Commands/ExportCommand.cs`
- Create: `CUE4Parse.Cli/Services/ExportOptionsMapper.cs`
- Modify: `CUE4Parse.Cli/Program.cs`
- Create: `CUE4Parse.Cli.Tests/ExportOptionsMapperTests.cs`

**Interfaces:**
- Consumes: `CommandContext`, `AssetMatcher`, `ProviderFactory`.
- Produces:
  - `sealed record ExportFlags(string MeshFormat, string TextureFormat, string TexturePlatform, string MeshQuality, string Nanite, string SocketFormat, string MaterialDepth, int TextureQuality, bool NoMaterials, bool AllMips)`
  - `static class ExportOptionsMapper` with `static ExportOptions Map(ExportFlags flags)`
  - `sealed record ExportCommandOptions(string[] Paths, MatchCriteria Criteria, DirectoryInfo Output, ExportFlags Flags, int Parallel, bool Force)`
  - `static class ExportCommand` with `static Task<int> ExecuteAsync(CommandContext context, ExportCommandOptions options, CancellationToken ct)`

`ExportSession.RunAsync(string baseDirectory, ExportOptions options, IProgress<ExportProgress>?, CancellationToken)` returns `IReadOnlyList<ExportResult>`. `ExportResult` is `(bool Success, string ObjectPath, IReadOnlyList<string>? DiskFilePaths, Exception? Error)`.

**Exit 8 means something failed, not something was skipped.** `ExportSession.Add`
throws `NotSupportedException` for every object type without an exporter —
DataAssets, DataTables, StringTables, Blueprints and so on. Treating that as a
partial failure makes any realistic glob return 8 every time, which drains the
code of meaning. Skipped items are reported with `status: "skipped"` and do not
affect the exit code; only `ExportResult.Success == false` does.

`--texture-platform` is exposed because `ExportOptions` takes `texturePlatform`
and console/Switch textures decode wrong without it. `exportHdrTexturesAsHdr`,
`exportMorphTargets` and `compressionFormat` are deliberately left at their
defaults — add them only if a caller asks.

- [ ] **Step 1: Write the failing test**

`CUE4Parse.Cli.Tests/ExportOptionsMapperTests.cs`:

```csharp
using CUE4Parse.Cli.Commands;
using CUE4Parse.Cli.Output;
using CUE4Parse.Cli.Services;
using CUE4Parse.UE4.Assets.Exports.Material;
using CUE4Parse_Conversion.Options;

namespace CUE4Parse.Cli.Tests;

public class ExportOptionsMapperTests
{
    private static ExportFlags Defaults() => new(
        MeshFormat: "ueformat", TextureFormat: "png", TexturePlatform: "desktop",
        MeshQuality: "highest", Nanite: "no-nanite", SocketFormat: "bone",
        MaterialDepth: "top-layer-only", TextureQuality: 100, NoMaterials: false, AllMips: false);

    [Fact]
    public void MapTranslatesAllDefaultsToTheExpectedEnums()
    {
        var mapped = ExportOptionsMapper.Map(Defaults());

        Assert.Equal(EMeshFormat.UEFormat, mapped.MeshFormat);
        Assert.Equal(ETextureFormat.Png, mapped.TextureFormat);
        Assert.Equal(ETexturePlatform.DesktopMobile, mapped.TexturePlatform);
        Assert.Equal(EMeshQuality.Highest, mapped.MeshQuality);
        Assert.Equal(ENaniteMeshFormat.NoNanite, mapped.NaniteMeshFormat);
        Assert.Equal(ESocketFormat.Bone, mapped.SocketFormat);
        Assert.Equal(EMaterialDepth.TopLayerOnly, mapped.MaterialDepth);
        Assert.True(mapped.ExportMaterials);
    }

    [Theory]
    [InlineData("desktop", ETexturePlatform.DesktopMobile)]
    [InlineData("xbox-ps4", ETexturePlatform.XboxAndPlaystation4)]
    [InlineData("switch", ETexturePlatform.NintendoSwitch)]
    [InlineData("ps5", ETexturePlatform.Playstation5)]
    public void MapTranslatesEachTexturePlatform(string flag, ETexturePlatform expected)
        => Assert.Equal(expected, ExportOptionsMapper.Map(Defaults() with { TexturePlatform = flag }).TexturePlatform);

    /// <summary>
    /// ExportOptions rewrites TextureFormat to Png for USD regardless of the flag.
    /// Documented here so the behaviour is not mistaken for a mapper bug.
    /// </summary>
    [Fact]
    public void UsdForcesPngTexturesRegardlessOfTheTextureFormatFlag()
        => Assert.Equal(
            ETextureFormat.Png,
            ExportOptionsMapper.Map(Defaults() with { MeshFormat = "usd", TextureFormat = "tga" }).TextureFormat);

    [Theory]
    [InlineData("actorx", EMeshFormat.ActorX)]
    [InlineData("gltf2", EMeshFormat.Gltf2)]
    [InlineData("usd", EMeshFormat.USD)]
    public void MapTranslatesEachMeshFormat(string flag, EMeshFormat expected)
        => Assert.Equal(expected, ExportOptionsMapper.Map(Defaults() with { MeshFormat = flag }).MeshFormat);

    [Theory]
    [InlineData("nanite-only", ENaniteMeshFormat.NaniteOnly)]
    [InlineData("nanite-first", ENaniteMeshFormat.NaniteFirst)]
    [InlineData("nanite-last", ENaniteMeshFormat.NaniteLast)]
    public void MapTranslatesEachNaniteMode(string flag, ENaniteMeshFormat expected)
        => Assert.Equal(expected, ExportOptionsMapper.Map(Defaults() with { Nanite = flag }).NaniteMeshFormat);

    [Fact]
    public void NoMaterialsFlagDisablesMaterialExport()
        => Assert.False(ExportOptionsMapper.Map(Defaults() with { NoMaterials = true }).ExportMaterials);

    [Fact]
    public void MapThrowsUsageErrorForAnUnknownMeshFormat()
    {
        var ex = Assert.Throws<CliException>(
            () => ExportOptionsMapper.Map(Defaults() with { MeshFormat = "nope" }));

        Assert.Equal(ExitCode.Usage, ex.ExitCode);
        Assert.Equal("BAD_OPTION", ex.ErrorCode);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet run --project CUE4Parse.Cli.Tests/CUE4Parse.Cli.Tests.csproj -- --filter-method "*ExportOptionsMapper*"`
Expected: build failure — `ExportOptionsMapper` does not exist.

- [ ] **Step 3: Implement ExportOptionsMapper**

`CUE4Parse.Cli/Services/ExportOptionsMapper.cs`:

```csharp
using CUE4Parse.Cli.Commands;
using CUE4Parse.Cli.Output;
using CUE4Parse.UE4.Assets.Exports.Material;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse_Conversion.Options;

namespace CUE4Parse.Cli.Services;

public static class ExportOptionsMapper
{
    public static ExportOptions Map(ExportFlags flags) => new(
        meshFormat: Pick(flags.MeshFormat, "--mesh-format", new()
        {
            ["actorx"] = EMeshFormat.ActorX,
            ["gltf2"] = EMeshFormat.Gltf2,
            ["ueformat"] = EMeshFormat.UEFormat,
            ["usd"] = EMeshFormat.USD,
        }),
        naniteMeshFormat: Pick(flags.Nanite, "--nanite", new()
        {
            ["nanite-only"] = ENaniteMeshFormat.NaniteOnly,
            ["no-nanite"] = ENaniteMeshFormat.NoNanite,
            ["nanite-first"] = ENaniteMeshFormat.NaniteFirst,
            ["nanite-last"] = ENaniteMeshFormat.NaniteLast,
        }),
        meshQuality: Pick(flags.MeshQuality, "--mesh-quality", new()
        {
            ["highest"] = EMeshQuality.Highest,
            ["lowest"] = EMeshQuality.Lowest,
            ["all"] = EMeshQuality.All,
        }),
        textureFormat: Pick(flags.TextureFormat, "--texture-format", new()
        {
            ["png"] = ETextureFormat.Png,
            ["jpeg"] = ETextureFormat.Jpeg,
            ["tga"] = ETextureFormat.Tga,
            ["webp"] = ETextureFormat.Webp,
        }),
        texturePlatform: Pick(flags.TexturePlatform, "--texture-platform", new()
        {
            ["desktop"] = ETexturePlatform.DesktopMobile,
            ["xbox-ps4"] = ETexturePlatform.XboxAndPlaystation4,
            ["switch"] = ETexturePlatform.NintendoSwitch,
            ["ps5"] = ETexturePlatform.Playstation5,
        }),
        textureQuality: flags.TextureQuality,
        exportAllTextureMips: flags.AllMips,
        materialDepth: Pick(flags.MaterialDepth, "--material-depth", new()
        {
            ["top-layer-only"] = EMaterialDepth.TopLayerOnly,
            ["all-layers-no-ref"] = EMaterialDepth.AllLayersNoRef,
            ["all-layers"] = EMaterialDepth.AllLayers,
        }),
        exportMaterials: !flags.NoMaterials,
        socketFormat: Pick(flags.SocketFormat, "--socket-format", new()
        {
            ["socket"] = ESocketFormat.Socket,
            ["bone"] = ESocketFormat.Bone,
            ["none"] = ESocketFormat.None,
        }));

    private static T Pick<T>(string value, string flagName, Dictionary<string, T> map)
        => map.TryGetValue(value.ToLowerInvariant(), out var result)
            ? result
            : throw new CliException(
                ExitCode.Usage, "BAD_OPTION",
                $"Invalid value '{value}' for {flagName}. Valid values: {string.Join(", ", map.Keys)}.");
}
```

- [ ] **Step 4: Implement ExportCommand**

`CUE4Parse.Cli/Commands/ExportCommand.cs`:

```csharp
using CUE4Parse.Cli.Output;
using CUE4Parse.Cli.Services;
using CUE4Parse_Conversion;

namespace CUE4Parse.Cli.Commands;

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
    bool AllMips);

public sealed record ExportCommandOptions(
    string[] Paths,
    MatchCriteria Criteria,
    DirectoryInfo Output,
    ExportFlags Flags,
    int Parallel,
    bool Force);

public static class ExportCommand
{
    public static async Task<int> ExecuteAsync(
        CommandContext context, ExportCommandOptions options, CancellationToken ct)
    {
        using var provider = ProviderFactory.Create(context.Profile);

        var targets = TargetResolver.Resolve(provider, options.Paths, options.Criteria, options.Force);
        options.Output.Create();

        // MaxDegreeOfParallelism must be positive; 0 or a negative value throws deep
        // inside the session where the error is unrecognizable.
        var session = new ExportSession { MaxDegreeOfParallelism = Math.Max(1, options.Parallel) };
        var skipped = new List<object>();
        var loadFailures = 0;

        foreach (var path in targets)
        {
            // LoadPackage throws for non-package entries (.ini, .locres, .bin), which a
            // broad glob will happily match. Skipping is not a failure.
            if (!provider.Files[path].IsUePackage)
            {
                skipped.Add(new { path, status = "skipped", reason = "not a UE package" });
                continue;
            }

            try
            {
                foreach (var export in provider.LoadPackage(path).GetExports())
                {
                    try
                    {
                        session.Add(export);
                    }
                    catch (NotSupportedException)
                    {
                        // ExportSession.Add throws for object types with no exporter.
                        skipped.Add(new
                        {
                            path, status = "skipped", export = export.Name,
                            reason = "no exporter for this type",
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                loadFailures++;
                context.Output.WriteLine(new { path, status = "error", message = ex.Message });
            }
        }

        var results = await session.RunAsync(
            options.Output.FullName, ExportOptionsMapper.Map(options.Flags), progress: null, ct);

        foreach (var result in results)
        {
            context.Output.WriteLine(new
            {
                path = result.ObjectPath,
                status = result.Success ? "ok" : "error",
                files = result.DiskFilePaths,
                message = result.Error?.Message,
            });
        }

        foreach (var entry in skipped) context.Output.WriteLine(entry);

        // Exit 8 means something failed. "No exporter for a DataTable" is not a
        // failure — counting it as one would make almost every real glob return 8.
        var failed = results.Count(r => !r.Success) + loadFailures;
        return failed > 0 ? (int)ExitCode.PartialExport : (int)ExitCode.Success;
    }
}
```

- [ ] **Step 5: Wire the command into Program.cs**

```csharp
var exportPathsArg = new Argument<string[]>("paths")
    { Description = "Asset paths", Arity = ArgumentArity.ZeroOrMore };
var exportOutOpt = new Option<DirectoryInfo>("--output", "-o")
    { Description = "Output directory", Required = true };
var meshFormatOpt = new Option<string>("--mesh-format")
    { Description = "Mesh output format", DefaultValueFactory = _ => "ueformat" };
var textureFormatOpt = new Option<string>("--texture-format")
    { Description = "Texture output format", DefaultValueFactory = _ => "png" };
var texturePlatformOpt = new Option<string>("--texture-platform")
    { Description = "Source platform for texture deswizzling", DefaultValueFactory = _ => "desktop" };
var meshQualityOpt = new Option<string>("--mesh-quality")
    { Description = "LOD selection", DefaultValueFactory = _ => "highest" };
var naniteOpt = new Option<string>("--nanite")
    { Description = "Nanite LOD handling", DefaultValueFactory = _ => "no-nanite" };
var socketFormatOpt = new Option<string>("--socket-format")
    { Description = "Bone socket handling", DefaultValueFactory = _ => "bone" };
var materialDepthOpt = new Option<string>("--material-depth")
    { Description = "Material layer depth", DefaultValueFactory = _ => "top-layer-only" };
var textureQualityOpt = new Option<int>("--texture-quality")
    { Description = "Texture quality 1-100", DefaultValueFactory = _ => 100 };
var noMaterialsOpt = new Option<bool>("--no-materials") { Description = "Skip material export" };
var allMipsOpt = new Option<bool>("--all-mips") { Description = "Export every texture mip" };
var parallelOpt = new Option<int>("--parallel")
    { Description = "Max degree of parallelism", DefaultValueFactory = _ => Environment.ProcessorCount };
var exportForceOpt = new Option<bool>("--force") { Description = "Bypass the 1000-asset safety limit" };

meshFormatOpt.AcceptOnlyFromAmong("actorx", "gltf2", "ueformat", "usd");
textureFormatOpt.AcceptOnlyFromAmong("png", "jpeg", "tga", "webp");
texturePlatformOpt.AcceptOnlyFromAmong("desktop", "xbox-ps4", "switch", "ps5");
meshQualityOpt.AcceptOnlyFromAmong("highest", "lowest", "all");
naniteOpt.AcceptOnlyFromAmong("nanite-only", "no-nanite", "nanite-first", "nanite-last");
socketFormatOpt.AcceptOnlyFromAmong("socket", "bone", "none");
materialDepthOpt.AcceptOnlyFromAmong("top-layer-only", "all-layers-no-ref", "all-layers");

var exportCmd = new Command("export", "Export meshes, animations, textures and materials");
exportCmd.Arguments.Add(exportPathsArg);
CriteriaOptions.AddTo(exportCmd);
foreach (var option in new Option[]
{
    exportOutOpt, meshFormatOpt, textureFormatOpt, texturePlatformOpt, meshQualityOpt,
    naniteOpt, socketFormatOpt, materialDepthOpt, textureQualityOpt, noMaterialsOpt,
    allMipsOpt, parallelOpt, exportForceOpt,
})
{
    exportCmd.Options.Add(option);
}

exportCmd.SetAction(async (pr, ct) => await RunAsync(pr, async ctx => await ExportCommand.ExecuteAsync(ctx,
    new ExportCommandOptions(
        Paths: pr.GetValue(exportPathsArg) ?? [],
        Criteria: CriteriaOptions.Read(pr),
        Output: pr.GetValue(exportOutOpt)!,
        Flags: new ExportFlags(
            pr.GetValue(meshFormatOpt)!, pr.GetValue(textureFormatOpt)!, pr.GetValue(texturePlatformOpt)!,
            pr.GetValue(meshQualityOpt)!, pr.GetValue(naniteOpt)!, pr.GetValue(socketFormatOpt)!,
            pr.GetValue(materialDepthOpt)!, pr.GetValue(textureQualityOpt),
            pr.GetValue(noMaterialsOpt), pr.GetValue(allMipsOpt)),
        Parallel: pr.GetValue(parallelOpt),
        Force: pr.GetValue(exportForceOpt)),
    ct)));
root.Subcommands.Add(exportCmd);
```

- [ ] **Step 6: Add the async error boundary to Program.cs**

Add alongside the existing `Run`:

```csharp
static async Task<int> RunAsync(ParseResult parseResult, Func<CommandContext, Task<int>> body)
{
    ConfigureLogging(parseResult.GetValue(GlobalOptions.Verbose));

    try
    {
        return await body(ContextBuilder.Build(parseResult));
    }
    catch (Exception ex)
    {
        return Classify(ex);  // shared with Run — see Task 6
    }
}
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet run --project CUE4Parse.Cli.Tests/CUE4Parse.Cli.Tests.csproj -- --filter-method "*ExportOptionsMapper*"`
Expected: PASS, 14 tests.

- [ ] **Step 8: Verify export against a real fixture**

```bash
dotnet build CUE4Parse.Cli.Tests/CUE4Parse.Cli.Tests.csproj -c Release
FIX=CUE4Parse.Cli.Tests/bin/Release/net10.0/Fixtures/UE5_8
./CUE4Parse.Cli/bin/Release/net10.0/cue4.exe export \
  --paks "$FIX/LegacyPak/Unversioned/Oodle" --game 5.8 \
  --mappings "$FIX/Mappings/CUE4ParseFixtures-Oodle.usmap" \
  --glob "**/Meshes/*.uasset" -o ./scratch-export
echo "exit=$?"
```

Expected: **exit 0**, NDJSON lines with `status` of `ok` or `skipped`, and
`.uemodel` files under `./scratch-export`. An exit of 8 here means a real export
failed — `skipped` items must not affect the exit code.

- [ ] **Step 9: Commit**

```bash
git add CUE4Parse.Cli CUE4Parse.Cli.Tests
git commit -m "feat(cli): add export command driving ExportSession"
```

---

## Task 11: Fortnite auto-fetch and the update command

**Files:**
- Create: `CUE4Parse.Cli/Services/IFortniteApiClient.cs`
- Create: `CUE4Parse.Cli/Services/FortniteApiClient.cs`
- Create: `CUE4Parse.Cli/Commands/UpdateCommand.cs`
- Modify: `CUE4Parse.Cli/Program.cs`
- Modify: `CUE4Parse.Cli/Services/ProviderFactory.cs`
- Create: `CUE4Parse.Cli.Tests/FortniteApiClientTests.cs`

**Interfaces:**
- Consumes: `CliException`/`ExitCode`, `CommandContext`.
- Produces:
  - `sealed record AesKeys(string MainKey, IReadOnlyDictionary<string, string> DynamicKeys)`
  - `interface IFortniteApiClient` with `Task<AesKeys> GetAesKeysAsync(CancellationToken ct)` and `Task<byte[]> GetMappingsAsync(CancellationToken ct)`
  - `sealed class FortniteApiClient(HttpClient http) : IFortniteApiClient`
  - `static class CachePaths` with `static string Root { get; }`, `static string MappingsFile { get; }`, `static string KeysFile { get; }`, `static AesKeys? ReadCachedKeys()`
  - `static class UpdateCommand` with `static Task<int> ExecuteAsync(CommandContext context, IFortniteApiClient client, bool keys, bool mappings, CancellationToken ct)`

Endpoints: `https://fortnite-api.com/v2/aes` and `https://fortnite-api.com/v2/mappings`. The mappings response lists files; download the first entry's `url`.

Two `"auto"` resolutions, not one:

- **Mappings.** `ProviderFactory.Create` resolves `"auto"` to
  `CachePaths.MappingsFile`, failing with `MAPPINGS_NOT_FOUND` and the hint "run
  `cue4 update`" when that file is absent.
- **AES keys.** `--aes auto` / `"aes": { "main": "auto" }` resolves to the cached
  `aes.json`, supplying **both** the main key and every dynamic key. The original
  draft wrote `aes.json` and then never read it, so `cue4 update --keys` had no
  observable effect anywhere in the tool — half of a headline spec feature that
  did not exist. Dynamic keys matter here: Fortnite ships encrypted chunks whose
  GUIDs are not the zero GUID, and the main key alone will not mount them.

The `update` flags are `--aes-keys` and `--usmap`. The draft's `--mappings-only`
existed only to dodge the recursive global `--mappings`; naming both refresh
targets after what they fetch is clearer than naming one after a collision.

- [ ] **Step 1: Write the failing test**

`CUE4Parse.Cli.Tests/FortniteApiClientTests.cs`:

```csharp
using System.Net;
using System.Text;
using CUE4Parse.Cli.Output;
using CUE4Parse.Cli.Services;

namespace CUE4Parse.Cli.Tests;

file sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        => Task.FromResult(responder(request));
}

public class FortniteApiClientTests
{
    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    [Fact]
    public async Task GetAesKeysAsyncParsesMainAndDynamicKeys()
    {
        var client = new FortniteApiClient(new HttpClient(new StubHandler(_ => Json("""
        {
          "data": {
            "mainKey": "0xAAAA",
            "dynamicKeys": [
              { "guid": "11111111-1111-1111-1111-111111111111", "key": "0xBBBB" }
            ]
          }
        }
        """))));

        var keys = await client.GetAesKeysAsync(CancellationToken.None);

        Assert.Equal("0xAAAA", keys.MainKey);
        Assert.Equal("0xBBBB", keys.DynamicKeys["11111111-1111-1111-1111-111111111111"]);
    }

    [Fact]
    public async Task GetAesKeysAsyncThrowsWhenTheApiReturnsAnError()
    {
        var client = new FortniteApiClient(new HttpClient(new StubHandler(
            _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))));

        var ex = await Assert.ThrowsAsync<CliException>(() => client.GetAesKeysAsync(CancellationToken.None));
        Assert.Equal("API_UNAVAILABLE", ex.ErrorCode);
    }

    [Fact]
    public async Task GetMappingsAsyncDownloadsTheFirstListedFile()
    {
        var payload = new byte[] { 1, 2, 3, 4 };
        var client = new FortniteApiClient(new HttpClient(new StubHandler(request =>
            request.RequestUri!.AbsoluteUri.Contains("/v2/mappings")
                ? Json("""{ "data": [ { "url": "https://example.invalid/m.usmap" } ] }""")
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) })));

        Assert.Equal(payload, await client.GetMappingsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task GetMappingsAsyncThrowsWhenTheApiListsNoFiles()
    {
        var client = new FortniteApiClient(new HttpClient(new StubHandler(
            _ => Json("""{ "data": [] }"""))));

        var ex = await Assert.ThrowsAsync<CliException>(() => client.GetMappingsAsync(CancellationToken.None));
        Assert.Equal("NO_MAPPINGS_AVAILABLE", ex.ErrorCode);
    }

    /// <summary>
    /// UpdateCommand writes AesKeys with JsonConvert and CachePaths.ReadCachedKeys
    /// reads it back. A positional record needs the ctor binding to line up, and a
    /// silent failure here would leave 'cue4 update' with no observable effect.
    /// </summary>
    [Fact]
    public void CachedKeysRoundTripThroughTheSameSerializer()
    {
        var original = new AesKeys("0xAAAA", new Dictionary<string, string> { ["11111111222222223333333344444444"] = "0xBBBB" });

        var restored = Newtonsoft.Json.JsonConvert.DeserializeObject<AesKeys>(
            Newtonsoft.Json.JsonConvert.SerializeObject(original))!;

        Assert.Equal("0xAAAA", restored.MainKey);
        Assert.Equal("0xBBBB", restored.DynamicKeys["11111111222222223333333344444444"]);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet run --project CUE4Parse.Cli.Tests/CUE4Parse.Cli.Tests.csproj -- --filter-method "*FortniteApiClient*"`
Expected: build failure — `FortniteApiClient` does not exist.

- [ ] **Step 3: Implement the interface and cache paths**

`CUE4Parse.Cli/Services/IFortniteApiClient.cs`:

```csharp
namespace CUE4Parse.Cli.Services;

public sealed record AesKeys(string MainKey, IReadOnlyDictionary<string, string> DynamicKeys);

public interface IFortniteApiClient
{
    Task<AesKeys> GetAesKeysAsync(CancellationToken ct);
    Task<byte[]> GetMappingsAsync(CancellationToken ct);
}

public static class CachePaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "cue4", "cache");

    public static string MappingsFile => Path.Combine(Root, "mappings.usmap");
    public static string KeysFile => Path.Combine(Root, "aes.json");

    /// <summary>Reads the keys written by <c>cue4 update</c>; null when never fetched.</summary>
    public static AesKeys? ReadCachedKeys()
        => File.Exists(KeysFile)
            ? Newtonsoft.Json.JsonConvert.DeserializeObject<AesKeys>(File.ReadAllText(KeysFile))
            : null;
}
```

- [ ] **Step 4: Implement FortniteApiClient**

`CUE4Parse.Cli/Services/FortniteApiClient.cs`:

```csharp
using CUE4Parse.Cli.Output;
using Newtonsoft.Json.Linq;

namespace CUE4Parse.Cli.Services;

public sealed class FortniteApiClient(HttpClient http) : IFortniteApiClient
{
    private const string AesEndpoint = "https://fortnite-api.com/v2/aes";
    private const string MappingsEndpoint = "https://fortnite-api.com/v2/mappings";

    public async Task<AesKeys> GetAesKeysAsync(CancellationToken ct)
    {
        var data = (await GetJsonAsync(AesEndpoint, ct))["data"]
            ?? throw new CliException(ExitCode.Error, "BAD_API_RESPONSE", "AES response had no 'data' field.");

        var dynamic = new Dictionary<string, string>();
        foreach (var entry in data["dynamicKeys"] as JArray ?? [])
        {
            var guid = entry["guid"]?.Value<string>();
            var key = entry["key"]?.Value<string>();
            if (guid is not null && key is not null) dynamic[guid] = key;
        }

        return new AesKeys(data["mainKey"]?.Value<string>() ?? string.Empty, dynamic);
    }

    public async Task<byte[]> GetMappingsAsync(CancellationToken ct)
    {
        var files = (await GetJsonAsync(MappingsEndpoint, ct))["data"] as JArray;

        var url = files?.FirstOrDefault()?["url"]?.Value<string>()
            ?? throw new CliException(
                ExitCode.Mappings, "NO_MAPPINGS_AVAILABLE",
                "The mappings API listed no downloadable files.");

        var response = await http.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new CliException(
                ExitCode.Error, "API_UNAVAILABLE",
                $"Mappings download failed with HTTP {(int)response.StatusCode}.");
        }

        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    private async Task<JObject> GetJsonAsync(string url, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            response = await http.GetAsync(url, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new CliException(ExitCode.Error, "API_UNAVAILABLE", $"Could not reach {url}: {ex.Message}");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new CliException(
                ExitCode.Error, "API_UNAVAILABLE",
                $"{url} returned HTTP {(int)response.StatusCode}.");
        }

        return JObject.Parse(await response.Content.ReadAsStringAsync(ct));
    }
}
```

- [ ] **Step 5: Implement UpdateCommand**

`CUE4Parse.Cli/Commands/UpdateCommand.cs`:

```csharp
using CUE4Parse.Cli.Output;
using CUE4Parse.Cli.Services;
using Newtonsoft.Json;

namespace CUE4Parse.Cli.Commands;

public static class UpdateCommand
{
    public static async Task<int> ExecuteAsync(
        CommandContext context, IFortniteApiClient client, bool keys, bool mappings, CancellationToken ct)
    {
        // With neither flag, refresh both.
        if (!keys && !mappings) keys = mappings = true;

        Directory.CreateDirectory(CachePaths.Root);
        var updated = new List<string>();

        if (keys)
        {
            var fetched = await client.GetAesKeysAsync(ct);
            await File.WriteAllTextAsync(
                CachePaths.KeysFile, JsonConvert.SerializeObject(fetched, Formatting.Indented), ct);
            updated.Add(CachePaths.KeysFile);
        }

        if (mappings)
        {
            await File.WriteAllBytesAsync(CachePaths.MappingsFile, await client.GetMappingsAsync(ct), ct);
            updated.Add(CachePaths.MappingsFile);
        }

        context.Output.WriteResult(new { status = "ok", updated }, indent: true);
        return (int)ExitCode.Success;
    }
}
```

- [ ] **Step 6: Resolve both `"auto"` values in ProviderFactory**

In `CUE4Parse.Cli/Services/ProviderFactory.cs`, replace both `"auto"` checks with a single resolved path computed at the top of `Create`:

```csharp
static bool IsAuto(string? v) => string.Equals(v, "auto", StringComparison.OrdinalIgnoreCase);

var mappingsPath = profile.Mappings switch
{
    null => null,
    var m when IsAuto(m) => CachePaths.MappingsFile,
    var m => m,
};

if (mappingsPath is not null && !File.Exists(mappingsPath))
{
    var hint = IsAuto(profile.Mappings)
        ? " Run 'cue4 update' to download them."
        : string.Empty;

    throw new CliException(
        ExitCode.Mappings, "MAPPINGS_NOT_FOUND",
        $"Mappings file not found: {mappingsPath}.{hint}");
}
```

Then assign unconditionally, before `Initialize()`:

```csharp
if (mappingsPath is not null)
    provider.MappingsContainer = new FileUsmapTypeMappingsProvider(mappingsPath);
```

And resolve `"auto"` AES keys where the key dictionary is built, so that
`cue4 update` actually feeds every other verb:

```csharp
var keys = new Dictionary<FGuid, FAesKey>();

if (IsAuto(profile.MainAesKey))
{
    var cached = CachePaths.ReadCachedKeys()
        ?? throw new CliException(
            ExitCode.AesKey, "AES_KEYS_NOT_CACHED",
            $"No cached AES keys at {CachePaths.KeysFile}. Run 'cue4 update' first.");

    if (!string.IsNullOrEmpty(cached.MainKey)) keys[new FGuid()] = ParseAesKey(cached.MainKey);

    // Dynamic keys are not optional: Fortnite ships encrypted chunks under
    // non-zero GUIDs that the main key will not mount.
    foreach (var (guidText, keyText) in cached.DynamicKeys)
        keys[ParseAesGuid(guidText)] = ParseAesKey(keyText);
}
else if (profile.MainAesKey is { } main)
{
    keys[new FGuid()] = ParseAesKey(main);
}

// Profile-level dynamic keys are applied last so they win over the cache.
foreach (var (guidText, keyText) in profile.DynamicKeys)
    keys[ParseAesGuid(guidText)] = ParseAesKey(keyText);
```

- [ ] **Step 7: Wire the command into Program.cs**

```csharp
var updateKeysOpt = new Option<bool>("--aes-keys") { Description = "Refresh AES keys only" };
var updateMappingsOpt = new Option<bool>("--usmap") { Description = "Refresh mappings only" };

var updateCmd = new Command("update", "Fetch AES keys and mappings from fortnite-api.com");
updateCmd.Options.Add(updateKeysOpt);
updateCmd.Options.Add(updateMappingsOpt);
updateCmd.SetAction(async (pr, ct) =>
{
    ConfigureLogging(pr.GetValue(GlobalOptions.Verbose));
    var output = new JsonOutput(Console.Out);
    try
    {
        using var http = new HttpClient();
        // update needs no mounted provider, so it does not build a CommandContext profile.
        var context = new CommandContext(
            new ResolvedProfile("", default, null, null, new Dictionary<string, string>()), output, false);

        return await UpdateCommand.ExecuteAsync(
            context, new FortniteApiClient(http),
            pr.GetValue(updateKeysOpt), pr.GetValue(updateMappingsOpt), ct);
    }
    catch (Exception ex)
    {
        return Classify(ex);
    }
});
root.Subcommands.Add(updateCmd);
```

> `update` names its flags after what they fetch (`--aes-keys`, `--usmap`) rather
> than after the recursive global `--mappings` they must not collide with.

- [ ] **Step 8: Run the tests to verify they pass**

Run: `dotnet run --project CUE4Parse.Cli.Tests/CUE4Parse.Cli.Tests.csproj -- --filter-method "*FortniteApiClient*"`
Expected: PASS, 5 tests.

- [ ] **Step 9: Run the full CLI test suite**

Run: `dotnet run --project CUE4Parse.Cli.Tests/CUE4Parse.Cli.Tests.csproj`
Expected: all tests pass.

- [ ] **Step 10: Commit**

```bash
git add CUE4Parse.Cli CUE4Parse.Cli.Tests
git commit -m "feat(cli): add fortnite-api auto-fetch for AES keys and mappings"
```

---

## Task 12: Publishing and documentation

**Files:**
- Create: `CUE4Parse.Cli/publish.ps1`
- Create: `CUE4Parse.Cli/README.md`
- Modify: `CLAUDE.md`
- Modify: `docs/superpowers/specs/2026-08-16-cue4parse-cli-design.md`

**Interfaces:**
- Consumes: the finished CLI.
- Produces: no code interfaces.

- [ ] **Step 1: Write the publish script**

`CUE4Parse.Cli/publish.ps1`:

```powershell
#!/usr/bin/env pwsh
# Publishes cue4.exe as a self-contained single file.
# Trimming and NativeAOT are deliberately NOT used: ObjectTypeRegistry reflects
# over the assembly at static init, and trimming breaks UObject construction.

param(
    [string] $Runtime = "win-x64",
    [string] $Output  = "./artifacts"
)

$ErrorActionPreference = "Stop"

dotnet publish CUE4Parse.Cli/CUE4Parse.Cli.csproj `
    -c Release `
    -r $Runtime `
    --self-contained `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $Output

Write-Host "Published to $Output"
Get-ChildItem $Output -Filter "cue4*" | Select-Object Name, Length
```

- [ ] **Step 2: Run the publish script and verify the executable**

```bash
pwsh ./CUE4Parse.Cli/publish.ps1
FIX=CUE4Parse.Cli.Tests/bin/Release/net10.0/Fixtures/UE5_8
./artifacts/cue4.exe --help
./artifacts/cue4.exe info --paks "$FIX/LegacyPak/Unversioned/Oodle" --game 5.8 \
  --mappings "$FIX/Mappings/CUE4ParseFixtures-Oodle.usmap"
```

Expected: `cue4.exe` exists and runs standalone; help lists all six verbs;
`info` reports a non-zero `fileCount`.

- [ ] **Step 3: Write the CLI README**

`CUE4Parse.Cli/README.md` must document: the six verbs with a worked example each, the `cue4.json` schema, the full exit-code table, the stdout/stderr split, and the 1000-asset safety limit. Copy the exit-code table verbatim from the Global Constraints section of this plan.

It must also state three things a caller will otherwise get wrong:

- **Every invocation re-mounts the archives.** There is no daemon and no
  persisted index, so `dump a b c` is one mount and three `dump` calls are three.
  Batch paths into a single invocation.
- `--aes auto` and `--mappings auto` read the cache written by `cue4 update`;
  nothing else performs network I/O.
- `unpack` writes every payload file of a package, so one asset path yields
  `.uasset` **and** `.uexp` (and `.ubulk`/`.uptnl` when present).

- [ ] **Step 4: Document the CLI in CLAUDE.md**

Add a short subsection under "Architecture" noting that `CUE4Parse.Cli` builds `cue4.exe`, that it is the CLI front end for automation, and that `CUE4Parse.Cli/publish.ps1` produces the standalone binary. Keep it to a few lines, matching the density of surrounding sections.

- [ ] **Step 5: Reconcile the spec with what was built**

The design spec was written before the API was verified against source. Update
`docs/superpowers/specs/2026-08-16-cue4parse-cli-design.md` so the two documents
do not contradict each other:

- **Provider bootstrap** — the seven-step list omits `Mount()` and assigns
  `MappingsContainer` after `SubmitKeys`. Replace with the verified order:
  construct → `MappingsContainer` → `Initialize()` → `SubmitKeys` → `Mount()` →
  `PostMount()`.
- **Output contract** — the error example shows `missingGuids` as a sibling of
  `code`/`message`; the implementation nests structured payloads under
  `error.details`. Correct the example.
- **`list --class`** — remove it from the usage block. Determining a class
  requires deserializing the package, which contradicts `list` being the cheap
  discovery verb. It is implemented on `dump` instead.
- **`info --json`** — remove it. All output is JSON; the flag would be a no-op.
- **Per-verb arguments** — `--glob/--regex/--ext/--limit` are available on
  `list`, `dump`, `unpack` and `export` alike, not `--glob` alone on three of them.
- **Export options table** — add `--texture-platform`
  (`desktop`/`xbox-ps4`/`switch`/`ps5`, default `desktop`).
- **`unpack`** — state that it writes every payload file of a package
  (`.uasset` + `.uexp` + `.ubulk`/`.uptnl`) via `SavePackage`, and that `--flat`
  fails on a filename collision rather than overwriting.
- **Exit code 8** — clarify that it means an item genuinely failed. Object types
  with no exporter are reported as `skipped` and do not affect the exit code.
- **Fortnite auto-fetch** — state that `--aes auto` and `--mappings auto` are what
  consume the cache, and that dynamic keys are applied alongside the main key.
- **Packaging** — `publish.ps1` lives in `CUE4Parse.Cli/`, preserving the
  two-file divergence claim.

- [ ] **Step 6: Verify the whole solution builds and every test passes**

```bash
dotnet build CUE4Parse.slnx -c Release
dotnet test CUE4Parse.Tests/CUE4Parse.Tests.csproj -c Release
dotnet run --project CUE4Parse.Cli.Tests/CUE4Parse.Cli.Tests.csproj
```

Expected: 0 build errors; the existing CUE4Parse tests still pass; all CLI tests pass.

- [ ] **Step 7: Commit**

```bash
git add CUE4Parse.Cli/publish.ps1 CUE4Parse.Cli/README.md CLAUDE.md docs/superpowers/specs
git commit -m "docs(cli): add publish script and CLI documentation"
```

---

## Self-Review Notes

Checked against the spec:

- **Command surface** — all six verbs covered by Tasks 6–11. Per-verb arguments from the spec's usage block are wired in each task's Program.cs step.
- **Configuration** — Task 3. Discovery order, profile precedence, and flag override all tested.
- **Provider bootstrap** — Task 5, with the exact seven-step order from the spec.
- **Output contract** — Task 1 (envelope), Task 6 (stderr logging, exit code 2 for parse errors). Every command emits NDJSON for multi-result output.
- **Exit codes** — all nine defined in Task 1 and used: 2 (Task 4 limit, Task 10 bad option, Task 6 parse errors), 3 (Tasks 2–3), 4 (Task 5), 5 (Task 5), 6 (Tasks 5, 11), 7 (Tasks 8–10), 8 (Tasks 9–10).
- **Fortnite auto-fetch** — Task 11, behind `IFortniteApiClient` with a mocked-handler test.
- **Safety limits** — Task 4 defines it; Tasks 8, 9, 10 enforce it; `list` exempt per spec.
- **Testing** — every unit-test area named in the spec has a task. Integration tests against fixtures appear in Tasks 8 and 9.
- **Packaging** — Task 12, with trimming/AOT exclusion documented in the script itself.

Naming consistency verified across tasks: `CliException.Details` (never `Data`), `ExportFlags` fields match `ExportOptionsMapper.Map` parameters, `MatchCriteria` construction is identical everywhere, and `CommandContext` is the single argument shape for all `Execute` methods.

Two issues found and fixed during the first review:

- Task 7's tests originally asserted only against `AssetMatcher`, duplicating Task 4's coverage and leaving `ListCommand.Execute` untested. They now exercise the command itself against real fixtures, including the count-only and empty-result paths.
- Tasks 8 and 9 each redefined the fixture directory and AES-key boilerplate. That is now a single `FixtureSupport` helper introduced in Task 7 and consumed by both.

### Second review: verified against CUE4Parse source

The first review checked the plan against the spec but not against the library.
Reading the source turned up nine defects, three of which would have shipped a
CLI that silently returned nothing.

**Silent-failure class:**

1. **`ProviderFactory` never mounted.** `Initialize()` only registers readers;
   `SubmitKeys` mounts only the readers matching a submitted key GUID. Any
   profile without an AES key mounted **zero** archives, so `list`, `dump`,
   `export` and `unpack` all returned empty successfully. Fixed in Task 5 by
   adding `provider.Mount()`, with a regression test in Task 7 that uses a
   keyless profile precisely so the bug cannot come back unnoticed.
2. **The integration fixture contained no assets.** `Fixtures/UE5_8/Pak/Oodle`
   is a minimal pak holding only `AssetRegistry.bin`, `DefaultGame.ini` and two
   `.locres` files. Every `**/*.uasset` assertion in Tasks 7-9 would have failed.
   Switched to `LegacyPak/Unversioned/Oodle`, plus an encrypted variant so the
   `SubmitKeys` path is covered at all.
3. **`unpack` produced unopenable files.** `SaveAsset` returns one file's bytes;
   a cooked package needs its `.uexp`. Switched to `SavePackage`.

**Contract-correctness class:**

4. `FGuid.TryParse` does not exist, and `FGuid(string)` throws on the dashed GUID
   form the Fortnite API returns. Added `ParseAesGuid` with normalization.
5. `VerifyMounted` was written and never called — the spec's central diagnostic
   was dead code. Renamed `ThrowIfKeysMissing` and wired into the not-found path
   so exit 5 and exit 7 are actually distinguishable.
6. Missing mappings were never mapped to exit 6. `MappingException` is now caught
   in a shared `Classify`, which also removes the duplicated `Run`/`RunAsync`
   catch blocks.
7. `cue4 update` wrote `aes.json` that nothing read. `--aes auto` now consumes
   it, main and dynamic keys alike.
8. The 1000-asset guard measured the truncated count, so `--limit 100` against
   50,000 matches slipped past the very check that exists to prevent silent
   truncation. `Filter` now reports the true total.
9. Exit 8 fired whenever any object lacked an exporter, which is almost every
   realistic glob. Skipped items are now `status: "skipped"` at exit 0.

**Consistency and ergonomics:**

- `--regex`/`--ext`/`--limit` existed only on `list`; all four matching verbs now
  share one `CriteriaOptions` set.
- `--texture-platform` added; console and Switch textures decode wrong without it.
- `dump` used a `SerializeObject`/`DeserializeObject` string round-trip where
  `JToken.FromObject` runs the same converter once.
- `list` now deduplicates: `FileProviderDictionary.Keys` concatenates every
  mounted index and can repeat a path.
- `--flat` now fails on filename collisions instead of silently overwriting.
- `--parallel` is clamped to at least 1.
- Test fixtures are linked into the CLI test project's own output instead of
  reaching into `CUE4Parse.Tests/bin/Release`, which broke under Debug and on CI.
- `GameParser` rejects bare integers and comma-separated lists, both of which
  `Enum.TryParse` accepts and neither of which is a real game.
- Three copies of the explicit-paths-vs-criteria block collapsed into
  `TargetResolver`.
- `update`'s flags renamed `--aes-keys`/`--usmap`; `publish.ps1` moved into
  `CUE4Parse.Cli/` so the spec's two-file divergence claim stays true.

**Resolved from the first review:** the spec's `list --class` is implemented on
`dump` as a post-load filter, and Task 12 now reconciles the spec document with
everything above rather than leaving the two in contradiction.
