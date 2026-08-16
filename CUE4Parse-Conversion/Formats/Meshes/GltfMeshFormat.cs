using CUE4Parse_Conversion.Dto;
using CUE4Parse_Conversion.Writers.Gltf;
using CUE4Parse.UE4.Writers;

namespace CUE4Parse_Conversion.Formats.Meshes;

public sealed class GltfMeshFormat : IMeshExportFormat
{
    public string DisplayName => "glTF 2.0 (binary)";

    public IReadOnlyList<ExportFile> BuildSkeletalMesh(in MeshExportContext context, SkeletalMeshDto dto)
    {
        var objectName = context.ObjectName;
        var exportMorphTargets = context.Options.ExportMorphTargets;
        var results = new List<ExportFile>();

        foreach (var lod in dto.LODs)
        {
            using var ar = new FArchiveWriter();
            new Gltf(objectName, lod, exportMorphTargets).Save(ar);

            results.Add(new ExportFile("glb", ar.GetBuffer(), lod._suffix));
        }

        return results;
    }

    public IReadOnlyList<ExportFile> BuildStaticMesh(in MeshExportContext context, StaticMeshDto dto)
    {
        var objectName = context.ObjectName;
        var results = new List<ExportFile>();

        foreach (var lod in dto.LODs)
        {
            using var ar = new FArchiveWriter();
            new Gltf(objectName, lod).Save(ar);

            results.Add(new ExportFile("glb", ar.GetBuffer(), lod._suffix));
        }

        return results;
    }

    public IReadOnlyList<ExportFile> BuildSkeleton(in MeshExportContext context, SkeletonDto dto)
        => throw new NotSupportedException(
            "glTF does not support skeleton-only exports. Please export a skeletal mesh to get a glTF file containing the skeleton.");
}
