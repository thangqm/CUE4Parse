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
