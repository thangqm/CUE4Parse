using System;

namespace CUE4Parse_Conversion;

public readonly record struct ExportFile(string Extension, byte[] Data, string? NameSuffix = null);

/// <summary>
/// <para>
/// <paramref name="ClassName"/> is reported because a caller cannot derive it: the CLI
/// only enqueues root assets, while materials, textures, ORM images and DNA are added by
/// the library itself and never seen by the caller — yet they all appear in the results.
/// <c>ExporterBase.ClassName</c> already knew the answer; it was simply not passed on.
/// </para>
/// </summary>
public sealed record ExportResult(bool Success, string ObjectPath, string ClassName, IReadOnlyList<string>? DiskFilePaths = null, Exception? Error = null)
{
    public static ExportResult Failure(string objectPath, string className, Exception ex) => new(false, objectPath, className, null, ex);
}

public readonly record struct ExportProgress(int Completed, int Total, ExportResult? LastResult = null)
{
    public float Percentage => Total > 0 ? Completed / (float)Total : -1f;
    public string DisplayText => Total > 0 ? $"{Completed} / {Total}  ({Percentage * 100:F0}%)" : $"{Completed}";
}

