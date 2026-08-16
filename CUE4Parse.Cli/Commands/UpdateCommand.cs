using CUE4Parse.Cli.Output;
using CUE4Parse.Cli.Services;
using Newtonsoft.Json;

namespace CUE4Parse.Cli.Commands;

public static class UpdateCommand
{
    public static async Task<int> ExecuteAsync(
        JsonOutput output, FortniteApiClient client, bool keys, bool mappings, CancellationToken ct)
    {
        // With neither flag, refresh both.
        if (!keys && !mappings) keys = mappings = true;

        Directory.CreateDirectory(CachePaths.Root);
        var updated = new List<string>();

        // Two independent round trips, one of which downloads several megabytes.
        var keysTask = keys ? WriteKeysAsync(client, ct) : Task.CompletedTask;
        var mappingsTask = mappings
            ? client.DownloadMappingsAsync(CachePaths.MappingsFile, ct)
            : Task.CompletedTask;

        await Task.WhenAll(keysTask, mappingsTask);

        if (keys) updated.Add(CachePaths.KeysFile);
        if (mappings) updated.Add(CachePaths.MappingsFile);

        output.WriteResult(new { status = "ok", updated }, indent: true);
        return (int)ExitCode.Success;
    }

    private static async Task WriteKeysAsync(FortniteApiClient client, CancellationToken ct)
    {
        var fetched = await client.GetAesKeysAsync(ct);
        await File.WriteAllTextAsync(
            CachePaths.KeysFile, JsonConvert.SerializeObject(fetched, Formatting.Indented), ct);
    }
}
