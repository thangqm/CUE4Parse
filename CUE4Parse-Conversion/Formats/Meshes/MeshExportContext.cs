using System.Collections.Generic;
using CUE4Parse_Conversion.Options;

namespace CUE4Parse_Conversion.Formats.Meshes;

/// <summary>
/// Everything a mesh writer needs about the export in progress.
/// <para>
/// <see cref="SaveDirectory"/> is the exporter's own directory. It cannot be derived
/// from <see cref="ObjectPath"/> without duplicating <c>ExporterBase</c>'s path rules,
/// and two sources of truth for output paths is exactly the failure the glTF material
/// URIs must not have.
/// </para>
/// </summary>
public readonly record struct MeshExportContext(
    string ObjectName,
    string ObjectPath,
    string SaveDirectory,
    ExportOptions Options,
    IReadOnlyDictionary<string, string>? MaterialPaths = null);
