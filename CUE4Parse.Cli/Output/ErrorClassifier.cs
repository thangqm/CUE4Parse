using CUE4Parse.UE4.Exceptions;
using Serilog;

namespace CUE4Parse.Cli.Output;

/// <summary>An exception rendered into the CLI's error contract.</summary>
public readonly record struct ClassifiedError(ExitCode ExitCode, string Code, string Message, object? Details = null);

/// <summary>
/// The single place an exception becomes an exit code and a stable error code.
/// Both the process boundary and the per-asset NDJSON records classify through
/// here, so a given failure reports the same <c>code</c> either way.
/// </summary>
public static class ErrorClassifier
{
    public static ClassifiedError Classify(Exception ex) => ex switch
    {
        CliException cli => new ClassifiedError(cli.ExitCode, cli.ErrorCode, cli.Message, cli.Details),

        // CUE4Parse throws this from AbstractUePackage.CanDeserialize when a package
        // has unversioned properties and no .usmap is loaded. Without this arm the
        // CLI would emit plausible-looking but wrong JSON.
        MappingException => new ClassifiedError(
            ExitCode.Mappings,
            "MAPPINGS_REQUIRED",
            "This package uses unversioned properties and cannot be read without a .usmap. " +
            "Pass --mappings <file>, or --mappings auto after running 'cue4 update'.",
            new { detail = ex.Message }),

        _ => new ClassifiedError(ExitCode.Error, "INTERNAL", ex.Message),
    };

    /// <summary>Classifies, writes the error object and returns the exit code.</summary>
    public static int Report(Exception ex, JsonOutput output)
    {
        var error = Classify(ex);
        if (error.Code == "INTERNAL") Log.Debug(ex, "Unhandled exception");

        output.WriteError(error.Code, error.Message, error.Details);
        return (int)error.ExitCode;
    }
}
