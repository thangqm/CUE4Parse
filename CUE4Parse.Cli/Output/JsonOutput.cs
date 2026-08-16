using System.Text;
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

    // JsonConvert.SerializeObject builds a serializer, a StringBuilder and a string
    // per call. NDJSON emits one record per asset, so all three are reused instead.
    // Instance-scoped rather than static: JsonSerializer is reusable but not thread-safe.
    private readonly JsonSerializer _compact = JsonSerializer.Create(Compact);
    private readonly JsonSerializer _indented = JsonSerializer.Create(Indented);
    private readonly StringWriter _buffer = new(new StringBuilder(1024));

    public void WriteResult(object value, bool indent = false)
    {
        var buffer = _buffer.GetStringBuilder();
        buffer.Clear();

        (indent ? _indented : _compact).Serialize(_buffer, value);
        buffer.Append('\n');

        writer.Write(buffer);
        writer.Flush();
    }

    /// <summary>Emits one compact JSON object per line (NDJSON).</summary>
    public void WriteLine(object value) => WriteResult(value);

    public void WriteError(string code, string message, object? details = null)
        => WriteResult(new { error = new { code, message, details } });
}
