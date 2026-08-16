using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse_Conversion.Sounds;

namespace CUE4Parse_Conversion.Exporters;

/// <summary>
/// Writes a sound asset's audio bytes, with the extension the decoder reports.
/// <para>
/// cue4 extracts, it does not transcode. <c>SoundDecoder.Decompress</c> decompresses
/// nothing: it relabels PCM as <c>.wav</c> and rejects formats it does not recognise.
/// Wwise assets therefore come out as raw <c>.wem</c> and Bink as raw <c>.binka</c> —
/// the same bytes FModel extracts. FModel can play them because it bundles vgmstream;
/// CUE4Parse has no codec, so turning those into playable audio is a downstream step.
/// <c>OGG</c> is already a final format and plays as-is.
/// </para>
/// <para>
/// <c>shouldDecompress</c> is fixed at true. The false path only skips a header check
/// and a relabel, so a flag would promise far more than it delivers.
/// </para>
/// </summary>
public sealed class SoundExporter(UObject sound) : ExporterBase(sound)
{
    protected override IReadOnlyList<ExportFile> BuildExportFiles(CancellationToken ct = default)
    {
        sound.Decode(shouldDecompress: true, out var audioFormat, out var data);

        if (data is not { Length: > 0 })
        {
            throw new Exception($"Sound '{ObjectName}' produced no audio data (format: '{audioFormat}')");
        }

        Log.Debug("Extracted {Bytes} bytes of {Format} audio", data.Length, audioFormat);
        return [new ExportFile(audioFormat.ToLowerInvariant(), data)];
    }
}
