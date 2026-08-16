using Newtonsoft.Json;

namespace CUE4Parse.Cli.Output;

public sealed class JsonOutput(TextWriter writer)
{
    private static readonly JsonSerializerSettings Compact = new()
    {
        Formatting = Formatting.None,
        NullValueHandling = NullValueHandling.Ignore,
    };

    private static readonly JsonSerializerSettings Indented = new()
    {
        Formatting = Formatting.Indented,
        NullValueHandling = NullValueHandling.Ignore,
    };

    public void WriteResult(object value, bool indent = false)
    {
        writer.Write(JsonConvert.SerializeObject(value, indent ? Indented : Compact));
        writer.Write('\n');
        writer.Flush();
    }

    /// <summary>Emits one compact JSON object per line (NDJSON).</summary>
    public void WriteLine(object value)
    {
        writer.Write(JsonConvert.SerializeObject(value, Compact));
        writer.Write('\n');
        writer.Flush();
    }

    public void WriteError(string code, string message, object? details = null)
        => WriteResult(new { error = new { code, message, details } });
}
