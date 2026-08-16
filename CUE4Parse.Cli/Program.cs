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

var countOpt = new Option<bool>("--count") { Description = "Print only the match count" };

var listCmd = new Command("list", "Enumerate assets in the mounted VFS");
CriteriaOptions.AddTo(listCmd);
listCmd.Options.Add(countOpt);
listCmd.SetAction(pr => Run(pr, ctx => ListCommand.Execute(ctx, new ListOptions(
    CriteriaOptions.Read(pr),
    CountOnly: pr.GetValue(countOpt)))));
root.Subcommands.Add(listCmd);

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

static async Task<int> RunAsync(ParseResult parseResult, Func<CommandContext, Task<int>> body)
{
    ConfigureLogging(parseResult.GetValue(GlobalOptions.Verbose));

    try
    {
        return await body(ContextBuilder.Build(parseResult));
    }
    catch (Exception ex)
    {
        return Classify(ex);  // shared with Run
    }
}

// Single classification point, shared by Run and RunAsync, so the two
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
