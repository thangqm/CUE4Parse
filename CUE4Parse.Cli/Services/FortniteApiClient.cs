using CUE4Parse.Cli.Output;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CUE4Parse.Cli.Services;

public sealed record AesKeys(string MainKey, IReadOnlyDictionary<string, string> DynamicKeys);

public sealed class FortniteApiClient(HttpClient http)
{
    private const string AesEndpoint = "https://fortnite-api.com/v2/aes";
    private const string MappingsEndpoint = "https://fortnite-api.com/v2/mappings";

    public async Task<AesKeys> GetAesKeysAsync(CancellationToken ct)
    {
        var data = (await GetJsonAsync(AesEndpoint, ct))["data"]
            ?? throw new CliException(ExitCode.Error, "BAD_API_RESPONSE", "AES response had no 'data' field.");

        var dynamic = new Dictionary<string, string>();
        foreach (var entry in data["dynamicKeys"] as JArray ?? [])
        {
            var guid = entry["guid"]?.Value<string>();
            var key = entry["key"]?.Value<string>();
            if (guid is not null && key is not null) dynamic[guid] = key;
        }

        return new AesKeys(data["mainKey"]?.Value<string>() ?? string.Empty, dynamic);
    }

    /// <summary>
    /// Streams straight to disk. Fortnite's .usmap runs to tens of megabytes, which
    /// buffering into a byte[] would park on the large object heap for no reason.
    /// </summary>
    public async Task DownloadMappingsAsync(string destination, CancellationToken ct)
    {
        var files = (await GetJsonAsync(MappingsEndpoint, ct))["data"] as JArray;

        var url = files?.FirstOrDefault()?["url"]?.Value<string>()
            ?? throw new CliException(
                ExitCode.Mappings, "NO_MAPPINGS_AVAILABLE",
                "The mappings API listed no downloadable files.");

        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new CliException(
                ExitCode.Error, "API_UNAVAILABLE",
                $"Mappings download failed with HTTP {(int)response.StatusCode}.");
        }

        await using var file = File.Create(destination);
        await response.Content.CopyToAsync(file, ct);
    }

    private async Task<JObject> GetJsonAsync(string url, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            response = await http.GetAsync(url, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new CliException(ExitCode.Error, "API_UNAVAILABLE", $"Could not reach {url}: {ex.Message}");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new CliException(
                    ExitCode.Error, "API_UNAVAILABLE",
                    $"{url} returned HTTP {(int)response.StatusCode}.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = new JsonTextReader(new StreamReader(stream));
            return await JObject.LoadAsync(reader, ct);
        }
    }
}
