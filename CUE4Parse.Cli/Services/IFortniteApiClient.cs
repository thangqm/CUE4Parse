namespace CUE4Parse.Cli.Services;

public sealed record AesKeys(string MainKey, IReadOnlyDictionary<string, string> DynamicKeys);

public interface IFortniteApiClient
{
    Task<AesKeys> GetAesKeysAsync(CancellationToken ct);
    Task<byte[]> GetMappingsAsync(CancellationToken ct);
}

public static class CachePaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "cue4", "cache");

    public static string MappingsFile => Path.Combine(Root, "mappings.usmap");
    public static string KeysFile => Path.Combine(Root, "aes.json");

    /// <summary>Reads the keys written by <c>cue4 update</c>; null when never fetched.</summary>
    public static AesKeys? ReadCachedKeys()
        => File.Exists(KeysFile)
            ? Newtonsoft.Json.JsonConvert.DeserializeObject<AesKeys>(File.ReadAllText(KeysFile))
            : null;
}
