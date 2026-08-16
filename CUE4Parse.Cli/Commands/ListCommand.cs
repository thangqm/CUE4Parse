using CUE4Parse.Cli.Output;
using CUE4Parse.Cli.Services;

namespace CUE4Parse.Cli.Commands;

public sealed record ListOptions(MatchCriteria Criteria, bool CountOnly);

public static class ListCommand
{
    public static int Execute(CommandContext context, ListOptions options)
    {
        using var provider = ProviderFactory.Create(context.Profile);

        var matches = AssetMatcher.Filter(provider.Files, options.Criteria);

        if (options.CountOnly)
        {
            // totalMatched is reported alongside count so a caller can tell a
            // truncated listing from a complete one without a second invocation.
            context.Output.WriteResult(new
            {
                count = matches.Files.Count,
                totalMatched = matches.TotalMatched,
                truncated = matches.Files.Count < matches.TotalMatched,
            });
            return (int)ExitCode.Success;
        }

        foreach (var file in matches.Files)
        {
            context.Output.WriteLine(new
            {
                path = file.Path,
                size = file.Size,
                extension = file.Extension,
                encrypted = file.IsEncrypted,
            });
        }

        return (int)ExitCode.Success;
    }
}
