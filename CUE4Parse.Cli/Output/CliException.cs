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
