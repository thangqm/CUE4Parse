using CUE4Parse_Conversion.Dto;
using CUE4Parse_Conversion.Formats.Meshes;
using CUE4Parse_Conversion.Options;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Material;

namespace CUE4Parse_Conversion.Exporters;

public abstract class MeshExporter<T>(T mesh) : ExporterBase(mesh) where T : UObject
{
    protected abstract IReadOnlyList<ExportFile> BuildFiles(T original, IMeshExportFormat format);

    /// <summary>
    /// The bundle every mesh writer receives. <c>SaveDirectory</c> is included because a
    /// writer that emits references to sibling files (glTF image URIs) must resolve them
    /// from the exporter's own directory, and re-deriving that from <c>ObjectPath</c>
    /// would be a second implementation of <see cref="ExporterBase"/>'s path rules.
    /// </summary>
    protected MeshExportContext CreateContext(IReadOnlyDictionary<string, string>? materialPaths = null)
        => new(ObjectName, ObjectPath, SaveDirectory, Session.Options, materialPaths);

    /// <summary>
    /// The default stays <c>--nanite no-nanite</c>: changing it would be a blind bet on
    /// what the user wants. A warning is never wrong, and on a UE5 title whose meshes
    /// carry only Nanite geometry the current silence produces a near-empty mesh with no
    /// explanation.
    /// </summary>
    protected void WarnIfNaniteDataIsBeingDropped<TVertex>(MeshDto<TVertex> dto)
        where TVertex : struct, IMeshVertex
    {
        if (Session.Options.NaniteMeshFormat != ENaniteMeshFormat.NoNanite || !dto.HasNaniteData) return;

        Log.Warning(
            "Mesh has Nanite data that is being skipped ({LodCount} non-Nanite LOD(s) exported). " +
            "Pass --nanite nanite-only or --nanite nanite-first to include it.",
            dto.LODs.Count);
    }

    /// <summary>
    /// Everything a static-mesh-shaped export does once its DTO exists. Only the DTO
    /// construction differs between a static mesh and a geometry collection, so only that
    /// belongs in the subclass.
    /// </summary>
    /// <param name="label">Names the asset kind in the empty-LOD error, e.g. "Static mesh".</param>
    protected IReadOnlyList<ExportFile> BuildStaticMeshFiles(StaticMeshDto dto, IMeshExportFormat format, string label)
    {
        if (dto.LODs.Count == 0)
        {
            throw new Exception($"{label} has no LODs");
        }

        WarnIfNaniteDataIsBeingDropped(dto);

        var materialPaths = EnqueueMaterials(dto.Materials);
        return format.BuildStaticMesh(CreateContext(materialPaths), dto);
    }

    protected override IReadOnlyList<ExportFile> BuildExportFiles(CancellationToken ct = default)
    {
        Log.Debug("Converting mesh to {Format} at {Quality} quality ({NaniteFormat})", Session.Options.MeshFormat, Session.Options.MeshQuality, Session.Options.NaniteMeshFormat);

        return BuildFiles(mesh, GetMeshFormat(Session.Options.MeshFormat));
    }

    protected Dictionary<string, string>? EnqueueMaterials(params MeshMaterialDto[] materials)
    {
        if (!Session.Options.ExportMaterials) return null;

        // TODO: currently only usda needs such thing, but maybe other formats will need it in the future, so we can keep it for now
        var paths = Session.Options.MeshFormat == EMeshFormat.USD
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : null;

        foreach (var slot in materials)
        {
            if (slot.Material?.TryLoad<UMaterialInterface>(out var material) == true)
            {
                Session.Add(material);
                if (paths != null)
                {
                    paths[slot.SlotName] = Resolve(material, "usda");
                }
            }
        }

        return paths;
    }

    private IMeshExportFormat GetMeshFormat(EMeshFormat format) => format switch
    {
        EMeshFormat.ActorX => new ActorXMeshFormat(),
        EMeshFormat.Gltf2 => new GltfMeshFormat(),
        EMeshFormat.UEFormat => new UEFormatMeshFormat(),
        EMeshFormat.USD => new UsdMeshFormat(),
        _ => throw new NotSupportedException($"Mesh export does not support format {format}. Available formats: {string.Join(", ", Enum.GetNames<EMeshFormat>())}")
    };
}
