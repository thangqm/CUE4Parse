using System.CommandLine;
using CUE4Parse;
using CUE4Parse.Cli.Commands;
using CUE4Parse.Cli.Output;
using CUE4Parse.Cli.Services;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.SystemConsole.Themes;

var root = new RootCommand("cue4 - CUE4Parse command line interface");
GlobalOptions.AddTo(root);

var infoCmd = new Command("info", "Report mount state, game version and missing AES keys");
infoCmd.SetAction((pr, _) => Run(pr, ctx => InfoCommand.Execute(ctx)));
root.Subcommands.Add(infoCmd);

var countOpt = new Option<bool>("--count") { Description = "Print only the match count" };

var listCmd = new Command("list", "Enumerate assets in the mounted VFS");
CriteriaOptions.AddTo(listCmd);
listCmd.Options.Add(countOpt);
listCmd.SetAction((pr, _) => Run(pr, ctx => ListCommand.Execute(ctx, new ListOptions(
    CriteriaOptions.Read(pr),
    CountOnly: pr.GetValue(countOpt)))));
root.Subcommands.Add(listCmd);

var dumpExportOpt = new Option<string?>("--export") { Description = "Serialize only this named export" };
var dumpClassOpt = new Option<string?>("--class") { Description = "Keep only exports of this class" };
var dumpOutOpt = new Option<FileInfo?>("--output", "-o") { Description = "Write to a file instead of stdout" };
var dumpIndentOpt = new Option<bool>("--indent") { Description = "Pretty-print single-asset output" };

var dumpCmd = new Command("dump", "Deserialize exports to JSON");
CriteriaOptions.AddTo(dumpCmd);
TargetOptions.AddTo(dumpCmd);
dumpCmd.Options.Add(dumpExportOpt);
dumpCmd.Options.Add(dumpClassOpt);
dumpCmd.Options.Add(dumpOutOpt);
dumpCmd.Options.Add(dumpIndentOpt);
dumpCmd.SetAction((pr, _) => Run(pr, ctx => DumpCommand.Execute(ctx, new DumpOptions(
    Paths: TargetOptions.ReadPaths(pr),
    Criteria: CriteriaOptions.Read(pr),
    ExportName: pr.GetValue(dumpExportOpt),
    ClassName: pr.GetValue(dumpClassOpt),
    Output: pr.GetValue(dumpOutOpt),
    Indent: pr.GetValue(dumpIndentOpt),
    Force: pr.GetValue(TargetOptions.Force)))));
root.Subcommands.Add(dumpCmd);

var unpackFlatOpt = new Option<bool>("--flat") { Description = "Ignore directory structure" };

var unpackCmd = new Command("unpack", "Extract raw asset bytes");
CriteriaOptions.AddTo(unpackCmd);
TargetOptions.AddTo(unpackCmd);
unpackCmd.Options.Add(TargetOptions.OutputDir);
unpackCmd.Options.Add(unpackFlatOpt);
unpackCmd.SetAction((pr, _) => Run(pr, ctx => UnpackCommand.Execute(ctx, new UnpackOptions(
    Paths: TargetOptions.ReadPaths(pr),
    Criteria: CriteriaOptions.Read(pr),
    Output: pr.GetValue(TargetOptions.OutputDir)!,
    Flat: pr.GetValue(unpackFlatOpt),
    Force: pr.GetValue(TargetOptions.Force)))));
root.Subcommands.Add(unpackCmd);

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

// The accepted values come from the mapper's own tables, so the parser and the
// mapper cannot disagree about which values are legal.
meshFormatOpt.AcceptOnlyFromAmong([.. ExportOptionsMapper.MeshFormats.Keys]);
textureFormatOpt.AcceptOnlyFromAmong([.. ExportOptionsMapper.TextureFormats.Keys]);
texturePlatformOpt.AcceptOnlyFromAmong([.. ExportOptionsMapper.TexturePlatforms.Keys]);
meshQualityOpt.AcceptOnlyFromAmong([.. ExportOptionsMapper.MeshQualities.Keys]);
naniteOpt.AcceptOnlyFromAmong([.. ExportOptionsMapper.NaniteFormats.Keys]);
socketFormatOpt.AcceptOnlyFromAmong([.. ExportOptionsMapper.SocketFormats.Keys]);
materialDepthOpt.AcceptOnlyFromAmong([.. ExportOptionsMapper.MaterialDepths.Keys]);

var exportCmd = new Command("export", "Export meshes, animations, textures and materials");
CriteriaOptions.AddTo(exportCmd);
TargetOptions.AddTo(exportCmd);
foreach (var option in new Option[]
{
    TargetOptions.OutputDir, meshFormatOpt, textureFormatOpt, texturePlatformOpt, meshQualityOpt,
    naniteOpt, socketFormatOpt, materialDepthOpt, textureQualityOpt, noMaterialsOpt,
    allMipsOpt, parallelOpt,
})
{
    exportCmd.Options.Add(option);
}

exportCmd.SetAction((pr, ct) => RunAsync(pr, ctx => ExportCommand.ExecuteAsync(ctx,
    new ExportCommandOptions(
        Paths: TargetOptions.ReadPaths(pr),
        Criteria: CriteriaOptions.Read(pr),
        Output: pr.GetValue(TargetOptions.OutputDir)!,
        Flags: new ExportFlags(
            MeshFormat: pr.GetValue(meshFormatOpt)!,
            TextureFormat: pr.GetValue(textureFormatOpt)!,
            TexturePlatform: pr.GetValue(texturePlatformOpt)!,
            MeshQuality: pr.GetValue(meshQualityOpt)!,
            Nanite: pr.GetValue(naniteOpt)!,
            SocketFormat: pr.GetValue(socketFormatOpt)!,
            MaterialDepth: pr.GetValue(materialDepthOpt)!,
            TextureQuality: pr.GetValue(textureQualityOpt),
            NoMaterials: pr.GetValue(noMaterialsOpt),
            AllMips: pr.GetValue(allMipsOpt)),
        Parallel: pr.GetValue(parallelOpt),
        Force: pr.GetValue(TargetOptions.Force)),
    ct)));
root.Subcommands.Add(exportCmd);

var updateKeysOpt = new Option<bool>("--aes-keys") { Description = "Refresh AES keys only" };
var updateMappingsOpt = new Option<bool>("--usmap") { Description = "Refresh mappings only" };

var updateCmd = new Command("update", "Fetch AES keys and mappings from fortnite-api.com");
updateCmd.Options.Add(updateKeysOpt);
updateCmd.Options.Add(updateMappingsOpt);
updateCmd.SetAction((pr, ct) => RunAsync(pr, async ctx =>
{
    // CommandContext resolves its profile lazily, so update runs through the same
    // boundary without needing a paks directory or a game version.
    using var http = new HttpClient();
    return await UpdateCommand.ExecuteAsync(
        ctx.Output, new FortniteApiClient(http),
        pr.GetValue(updateKeysOpt), pr.GetValue(updateMappingsOpt), ct);
}));
root.Subcommands.Add(updateCmd);

var parseResult = root.Parse(args);

// System.CommandLine exits 1 on parse errors; the contract requires 2.
if (parseResult.Errors.Count > 0)
{
    new JsonOutput(Console.Out).WriteError(
        "USAGE", string.Join("; ", parseResult.Errors.Select(e => e.Message)));
    return (int)ExitCode.Usage;
}

return await parseResult.InvokeAsync();

// The single error boundary, shared by every verb including update.
static async Task<int> RunAsync(ParseResult parseResult, Func<CommandContext, Task<int>> body)
{
    ConfigureLogging(parseResult.GetValue(GlobalOptions.Verbose));

    try
    {
        return await body(ContextBuilder.Build(parseResult));
    }
    catch (Exception ex)
    {
        return ErrorClassifier.Report(ex, new JsonOutput(Console.Out));
    }
}

// Synchronous commands lift their result rather than keeping a second copy of the
// try/catch in step with the one above. Top-level statements cannot overload a
// local function, hence the second name.
static Task<int> Run(ParseResult parseResult, Func<CommandContext, int> body)
    => RunAsync(parseResult, ctx => Task.FromResult(body(ctx)));

// Logging goes to stderr so stdout carries only structured data.
static void ConfigureLogging(bool verbose)
{
    Log.Logger = new LoggerConfiguration()
        .MinimumLevel.Is(verbose ? LogEventLevel.Debug : LogEventLevel.Warning)
        .WriteTo.Console(standardErrorFromLevel: LogEventLevel.Verbose, theme: AnsiConsoleTheme.Literate)
        .CreateLogger();

    CUE4ParseLog.UseLogger(Log.Logger);
}
