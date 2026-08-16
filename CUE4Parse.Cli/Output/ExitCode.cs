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
