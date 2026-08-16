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
