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
